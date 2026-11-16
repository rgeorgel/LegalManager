using System.Buffers.Text;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using LegalManager.Domain;
using LegalManager.Domain.Entities;
using LegalManager.Domain.Enums;
using LegalManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace LegalManager.Infrastructure.Agenda;

/// <summary>Conteúdo do feed e o ETag (hash do conteúdo) para responder 304.</summary>
public sealed record FeedAgendaArquivo(string Conteudo, string ETag);

/// <summary>Token do link do feed: 32 bytes aleatórios em base64url; no banco, só o SHA-256 dele.</summary>
public static class TokenFeedAgenda
{
    public const int TamanhoMaximo = 64;

    public static string Gerar() => Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));

    public static string Hash(string token) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}

/// <summary>
/// Monta o .ics da agenda de um usuário (eventos + prazos e tarefas em aberto) a partir do token
/// do link. Prazos e eventos estão gravados na hora "de parede" do escritório: aqui viram UTC real
/// pelo fuso do tenant (<see cref="FusoHorario.DoTenantAsync"/>).
/// </summary>
public class FeedAgendaService(AppDbContext db, IConfiguration config)
{
    public const int DiasPassados = 30;
    public const int DiasFuturos = 365;
    public const int MaxItens = 3000;
    public const int DuracaoEventoPadraoMin = 60;
    public const int DuracaoTarefaPadraoMin = 30;
    private const int MaxDescricao = 1000;
    private static readonly TimeSpan IntervaloRegistroAcesso = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan AlarmeUmDia = TimeSpan.FromDays(1);
    // Dia inteiro começa à 0h: 15h antes = 9h do dia anterior.
    private static readonly TimeSpan AlarmeDiaInteiro = TimeSpan.FromHours(15);

    private static readonly Dictionary<TipoEvento, string> RotuloEvento = new()
    {
        [TipoEvento.Audiencia] = "Audiência",
        [TipoEvento.Reuniao] = "Reunião",
        [TipoEvento.Pericia] = "Perícia",
        [TipoEvento.Prazo] = "Prazo",
        [TipoEvento.Despacho] = "Despacho",
        [TipoEvento.Outro] = "Compromisso",
    };

    /// <summary>
    /// O .ics do feed, ou null se o token não existe, o usuário está inativo ou o escritório está
    /// suspenso/cancelado (o controller responde 404 em todos os casos, sem dizer qual).
    /// </summary>
    public async Task<FeedAgendaArquivo?> MontarAsync(string token, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > TokenFeedAgenda.TamanhoMaximo) return null;

        var hash = TokenFeedAgenda.Hash(token);
        var feed = await db.FeedsAgenda
            .Include(f => f.Usuario)
            .Include(f => f.Tenant)
            .FirstOrDefaultAsync(f => f.TokenHash == hash, ct);
        if (feed is null || !feed.Usuario.Ativo || feed.Usuario.TenantId != feed.TenantId
            || feed.Tenant.Status is StatusTenant.Suspenso or StatusTenant.Cancelado)
            return null;

        // Quem deixou de ser Admin volta a ver só os próprios itens.
        var escritorio = feed.Escopo == EscopoFeedAgenda.Escritorio && feed.Usuario.Perfil == PerfilUsuario.Admin;

        var tz = await db.DoTenantAsync(feed.TenantId, ct);
        var hoje = FusoHorario.HojeParede(tz);
        var de = hoje.AddDays(-DiasPassados);
        var ate = hoje.AddDays(DiasFuturos + 1);

        var nomeCalendario = escritorio ? $"Causify – {feed.Tenant.Nome}" : "Causify – Minha agenda";
        var ics = new IcsBuilder(nomeCalendario, feed.Tenant.FusoHorario ?? FusosHorarios.Padrao, TimeSpan.FromHours(1));
        var baseUrl = (config["App:FrontendUrl"] ?? string.Empty).TrimEnd('/');
        var ctx = new Contexto(tz, baseUrl, escritorio, feed.OcultarClientes);

        foreach (var e in await EventosAsync(feed, escritorio, de, ate, ct)) ics.Adicionar(ctx.DeEvento(e));
        foreach (var t in await TarefasAsync(feed, escritorio, de, ate, ct)) ics.Adicionar(ctx.DeTarefa(t));

        var conteudo = ics.Concluir();
        await RegistrarAcessoAsync(feed, ct);

        var etag = "\"" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(conteudo)))[..32] + "\"";
        return new FeedAgendaArquivo(conteudo, etag);
    }

    private async Task RegistrarAcessoAsync(FeedAgenda feed, CancellationToken ct)
    {
        var agora = DateTime.UtcNow;
        if (feed.UltimoAcessoEm is { } ultimo && agora - ultimo < IntervaloRegistroAcesso) return;
        feed.UltimoAcessoEm = agora;
        await db.SaveChangesAsync(ct);
    }

    private Task<List<EventoFeed>> EventosAsync(FeedAgenda feed, bool escritorio, DateTime de, DateTime ate, CancellationToken ct)
    {
        var q = db.Eventos.AsNoTracking()
            .Where(e => e.TenantId == feed.TenantId && e.DataHora >= de && e.DataHora < ate);
        if (!escritorio) q = q.Where(e => e.ResponsavelId == feed.UsuarioId);
        return q.OrderBy(e => e.DataHora).Take(MaxItens)
            .Select(e => new EventoFeed(e.Id, e.Tipo, e.Titulo, e.DataHora, e.DataHoraFim, e.Local, e.Observacoes,
                e.CriadoEm, e.AtualizadoEm,
                e.Processo != null ? e.Processo.NumeroCNJ : null,
                e.Responsavel != null ? e.Responsavel.Nome : null))
            .ToListAsync(ct);
    }

    private Task<List<TarefaFeed>> TarefasAsync(FeedAgenda feed, bool escritorio, DateTime de, DateTime ate, CancellationToken ct)
    {
        var q = db.Tarefas.AsNoTracking()
            .Where(t => t.TenantId == feed.TenantId && t.Prazo != null && t.Prazo >= de && t.Prazo < ate
                && (t.Status == StatusTarefa.Pendente || t.Status == StatusTarefa.EmAndamento));
        // Sem responsável, a tarefa é de quem a criou.
        if (!escritorio)
            q = q.Where(t => t.ResponsavelId == feed.UsuarioId || (t.ResponsavelId == null && t.CriadoPorId == feed.UsuarioId));
        return q.OrderBy(t => t.Prazo).Take(MaxItens)
            .Select(t => new TarefaFeed(t.Id, t.Tipo, t.Titulo, t.Descricao, t.Prazo!.Value, t.DuracaoMinutos,
                t.Prioridade, t.Status, t.CriadoEm, t.AtualizadoEm,
                t.Processo != null ? t.Processo.NumeroCNJ : null,
                t.Contato != null ? t.Contato.Nome : null,
                t.Responsavel != null ? t.Responsavel.Nome : null))
            .ToListAsync(ct);
    }

    private sealed record EventoFeed(Guid Id, TipoEvento Tipo, string Titulo, DateTime DataHora, DateTime? DataHoraFim,
        string? Local, string? Observacoes, DateTime CriadoEm, DateTime? AtualizadoEm, string? Processo, string? Responsavel);

    private sealed record TarefaFeed(Guid Id, TipoTarefa Tipo, string Titulo, string? Descricao, DateTime Prazo,
        int? DuracaoMinutos, PrioridadeTarefa Prioridade, StatusTarefa Status, DateTime CriadoEm, DateTime? AtualizadoEm,
        string? Processo, string? Contato, string? Responsavel);

    private sealed record Contexto(TimeZoneInfo Tz, string BaseUrl, bool Escritorio, bool OcultarClientes)
    {
        public IcsEvento DeEvento(EventoFeed e)
        {
            var rotulo = RotuloEvento.GetValueOrDefault(e.Tipo, "Compromisso");
            var inicio = ParaUtc(e.DataHora, Tz);
            var fim = e.DataHoraFim is { } f && f > e.DataHora
                ? ParaUtc(f, Tz)
                : inicio.AddMinutes(DuracaoEventoPadraoMin);
            var url = Link(string.Create(CultureInfo.InvariantCulture, $"/pages/agenda.html?date={e.DataHora:yyyy-MM-dd}&abrirId={e.Id}"));

            var titulo = OcultarClientes ? ComProcesso(rotulo, e.Processo) : ComRotulo(rotulo, e.Titulo);
            var linhas = new List<string?>();
            if (OcultarClientes)
                linhas.Add(Item("Processo", e.Processo));
            else
            {
                if (!ComecaCom(e.Titulo, rotulo)) linhas.Add(Item("Tipo", rotulo));
                linhas.Add(Item("Processo", e.Processo));
                if (Escritorio) linhas.Add(Item("Responsável", e.Responsavel));
                linhas.Add(Texto(e.Observacoes));
            }

            return new IcsEvento(
                Uid: $"evento-{e.Id}@causify.com.br",
                Titulo: titulo,
                Inicio: inicio,
                Fim: fim,
                DiaInteiro: false,
                ModificadoEmUtc: UtcReal(e.AtualizadoEm ?? e.CriadoEm),
                Descricao: Descricao(linhas, url),
                Local: e.Local,
                Url: url,
                Categoria: rotulo,
                AlarmeAntes: e.Tipo is TipoEvento.Audiencia or TipoEvento.Pericia or TipoEvento.Prazo ? AlarmeUmDia : null);
        }

        public IcsEvento DeTarefa(TarefaFeed t)
        {
            var rotulo = t.Tipo == TipoTarefa.Prazo ? "Prazo" : "Tarefa";
            // Prazo sem hora (0h) e sem duração = dia inteiro, o caso típico do prazo processual.
            var diaInteiro = t.Prazo.TimeOfDay == TimeSpan.Zero && t.DuracaoMinutos is null or <= 0;
            DateTime inicio, fim;
            if (diaInteiro)
            {
                inicio = t.Prazo.Date;
                fim = inicio.AddDays(1);
            }
            else
            {
                inicio = ParaUtc(t.Prazo, Tz);
                fim = inicio.AddMinutes(t.DuracaoMinutos is > 0 ? t.DuracaoMinutos.Value : DuracaoTarefaPadraoMin);
            }
            var url = Link($"/pages/tarefas.html?abrirId={t.Id}");

            var titulo = OcultarClientes ? ComProcesso(rotulo, t.Processo) : ComRotulo(rotulo, t.Titulo);
            var linhas = new List<string?>();
            if (OcultarClientes)
                linhas.Add(Item("Processo", t.Processo));
            else
            {
                linhas.Add(Item("Processo", t.Processo));
                linhas.Add(Item("Contato", t.Contato));
                if (Escritorio) linhas.Add(Item("Responsável", t.Responsavel));
                if (t.Prioridade is PrioridadeTarefa.Alta or PrioridadeTarefa.Urgente)
                    linhas.Add(Item("Prioridade", t.Prioridade == PrioridadeTarefa.Urgente ? "Urgente" : "Alta"));
                if (t.Status == StatusTarefa.EmAndamento) linhas.Add("Status: Em andamento");
                linhas.Add(Texto(t.Descricao));
            }

            return new IcsEvento(
                Uid: $"tarefa-{t.Id}@causify.com.br",
                Titulo: titulo,
                Inicio: inicio,
                Fim: fim,
                DiaInteiro: diaInteiro,
                ModificadoEmUtc: UtcReal(t.AtualizadoEm ?? t.CriadoEm),
                Descricao: Descricao(linhas, url),
                Url: url,
                Categoria: rotulo,
                AlarmeAntes: t.Tipo == TipoTarefa.Prazo ? (diaInteiro ? AlarmeDiaInteiro : AlarmeUmDia) : null);
        }

        private string? Link(string caminho) => string.IsNullOrEmpty(BaseUrl) ? null : BaseUrl + caminho;

        private static string Descricao(List<string?> linhas, string? url)
        {
            var partes = linhas.Where(l => !string.IsNullOrWhiteSpace(l)).ToList();
            if (url != null) partes.Add("Abrir no Causify: " + url);
            return string.Join("\n", partes);
        }

        private static string? Item(string rotulo, string? valor) =>
            string.IsNullOrWhiteSpace(valor) ? null : $"{rotulo}: {valor.Trim()}";

        private static string? Texto(string? texto)
        {
            if (string.IsNullOrWhiteSpace(texto)) return null;
            texto = texto.Trim();
            return "\n" + (texto.Length > MaxDescricao ? texto[..MaxDescricao] + "…" : texto);
        }

        private static bool ComecaCom(string titulo, string rotulo) =>
            titulo.TrimStart().StartsWith(rotulo, StringComparison.CurrentCultureIgnoreCase);

        private static string ComRotulo(string rotulo, string titulo) =>
            ComecaCom(titulo, rotulo) ? titulo.Trim() : $"{rotulo}: {titulo.Trim()}";

        private static string ComProcesso(string rotulo, string? processo) =>
            string.IsNullOrWhiteSpace(processo) ? rotulo : $"{rotulo} – Proc. {processo}";
    }

    /// <summary>Hora "de parede" no fuso do escritório → UTC real (hora inexistente na virada do horário de verão: +1h).</summary>
    public static DateTime ParaUtc(DateTime parede, TimeZoneInfo tz)
    {
        var local = DateTime.SpecifyKind(parede, DateTimeKind.Unspecified);
        if (tz.IsInvalidTime(local)) local = local.AddHours(1);
        return TimeZoneInfo.ConvertTimeToUtc(local, tz);
    }

    /// <summary>Carimbos do servidor (CriadoEm etc.) já são UTC; só garante o Kind.</summary>
    private static DateTime UtcReal(DateTime d) => DateTime.SpecifyKind(d, DateTimeKind.Utc);
}
