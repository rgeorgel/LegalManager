using System.Globalization;
using System.Diagnostics;
using LegalManager.Application.Interfaces;
using LegalManager.Domain.Entities;
using LegalManager.Domain.Enums;
using LegalManager.Infrastructure.Services;
using LegalManager.Infrastructure.Observability;
using LegalManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace LegalManager.Infrastructure.Jobs;

public class AlertasJob
{
    private readonly AppDbContext _context;
    private readonly IEmailService _emailService;
    private readonly IPreferenciasNotificacaoService _prefs;
    private readonly ILogger<AlertasJob> _logger;
    private readonly IServiceProvider? _services;

    /// <param name="services">
    /// De onde sai a IA dos textos do resumo diário (planos Plus+). Resolvida só na hora de usar:
    /// sem IA configurada (IA:ApiKey), o resumo sai sem esses textos e os demais alertas seguem.
    /// </param>
    public AlertasJob(AppDbContext context, IEmailService emailService,
        IPreferenciasNotificacaoService prefs, ILogger<AlertasJob> logger, IServiceProvider? services = null)
    {
        _context = context;
        _emailService = emailService;
        _prefs = prefs;
        _logger = logger;
        _services = services;
    }

    /// <summary>Relógio do resumo diário (testes podem fixar o instante). Padrão: agora, em UTC.</summary>
    public Func<DateTime> RelogioUtc { get; init; } = () => DateTime.UtcNow;

    public Task ExecutarAsync() => ExecutarAsync(null);

    /// <param name="hojeFixo">
    /// Força o "hoje" de todos os escritórios (testes). Nulo = hoje no fuso de cada escritório
    /// (Configurações → Perfil do Escritório), no mesmo referencial "de parede" de prazos e eventos.
    /// </param>
    public async Task ExecutarAsync(DateTime? hojeFixo)
    {
        using var activity = Telemetry.Hangfire.StartActivity($"{nameof(AlertasJob)}.{nameof(ExecutarAsync)}");
        activity?.SetTag("job.cron", "alertas-diarios");
        // Cada etapa é isolada: uma falha no resumo diário não pode impedir os avisos de fim de
        // trial/período promocional.
        await EtapaAsync(nameof(AlertarResumoDiarioAsync), () => AlertarResumoDiarioAsync(hojeFixo));
        await EtapaAsync(nameof(AlertarTrialExpirandoAsync), () => AlertarTrialExpirandoAsync(hojeFixo ?? BrasiliaTime.Hoje));
        await EtapaAsync(nameof(AlertarPlanoPromocionalExpirandoAsync), () => AlertarPlanoPromocionalExpirandoAsync(hojeFixo ?? BrasiliaTime.Hoje));
    }

    private async Task EtapaAsync(string nome, Func<Task> etapa)
    {
        try { await etapa(); }
        catch (Exception ex)
        {
            DescartarPendentes();
            _logger.LogError(ex, "Erro na etapa {Etapa} do AlertasJob", nome);
        }
    }

    // O job usa um único DbContext. Se um SaveChanges falha, a entidade que não foi gravada
    // continua no ChangeTracker e faz todo SaveChanges seguinte falhar também — inclusive o
    // que cria as preferências de notificação de quem ainda não tem, antes do e-mail de trial.
    private void DescartarPendentes() => _context.ChangeTracker.Clear();

    private static string Limitar(string texto, int max) =>
        texto.Length <= max ? texto : texto[..(max - 1)] + "…";

    private async Task<DateTime> HojeDoTenantAsync(Guid tenantId, DateTime? hojeFixo) =>
        hojeFixo?.Date ?? (await AgoraDoTenantAsync(tenantId)).Date;

    private async Task<DateTime> AgoraDoTenantAsync(Guid tenantId) =>
        TimeZoneInfo.ConvertTimeFromUtc(RelogioUtc(), await _context.DoTenantAsync(tenantId));

    // Os fusos do Brasil ficam a no máximo 1 dia de Brasília, então buscamos no banco uma janela
    // folgada em torno do "hoje" de Brasília e o recorte exato é feito por escritório depois.
    private const int FolgaFusoDias = 1;

    private async Task<(bool Email, bool InApp)> PreferenciaAsync(
        Dictionary<string, (bool, bool)> cache, Guid tenantId, Guid usuarioId, string categoria)
    {
        if (cache.TryGetValue(categoria, out var p)) return p;
        p = (await _prefs.PermiteEmailAsync(tenantId, usuarioId, categoria),
             await _prefs.PermiteInAppAsync(tenantId, usuarioId, categoria));
        cache[categoria] = p;
        return p;
    }

    /// <summary>Hora local (fuso do escritório) a partir da qual o resumo diário é enviado.</summary>
    public const int HoraResumo = 7;
    /// <summary>O e-mail mostra os compromissos até esta quantidade de dias à frente ("Próximos 7 dias").</summary>
    private const int DiasProximos = 7;
    /// <summary>Limites do que vai para a IA, para o custo por resumo ficar previsível.</summary>
    private const int MaxProcessosResumo = 10, MaxAndamentosPorProcesso = 3, MaxSugestoesResumo = 10;

    private sealed record Destinatario(Guid TenantId, Guid Id, string Nome, string? Email);
    private sealed record AndamentoNovo(Guid TenantId, Guid ResponsavelId, Guid ProcessoId, string NumeroCNJ,
        string? Classe, DateTime Data, string Descricao);

    // Um único e-mail diário por pessoa ("Seu dia"), com tarefas, prazos e eventos da agenda juntos —
    // para não mandar várias mensagens no mesmo dia — e, conforme o plano do escritório, as sugestões
    // da triagem de publicações (Pro+) e os processos que tiveram andamento novo (explicados pela IA
    // no Plus+; com o texto do tribunal no Free).
    // - Tarefas e prazos aparecem juntos na mesma tela (Tarefas/Prazos): um prazo pode ter sido
    //   criado como Tarefa (Tipo=Prazo) ou direto como Evento na Agenda (Tipo=Prazo) — as duas
    //   origens entram nas mesmas janelas: no e-mail, até 5 dias de atraso e os próximos 7 dias;
    //   no in-app, hoje, D+1, D+3 e D+5 antes de vencer e até 5 dias de atraso.
    // - Os demais eventos da agenda entram no e-mail de hoje até daqui a 7 dias.
    // Cada item respeita a preferência da sua categoria — "Prazos" para os dois tipos de prazo,
    // "PrazoTarefa"/"TarefaAtrasada" para as demais tarefas, "PrazoEvento" para os eventos,
    // "Publicacoes" para publicações e andamentos —, separadamente para e-mail e in-app.
    // O resumo sai a partir das 7h no fuso do escritório (o job roda de hora em hora), uma vez por
    // usuário e dia; o que for cadastrado depois disso entra no resumo do dia seguinte.
    private async Task AlertarResumoDiarioAsync(DateTime? hojeFixo)
    {
        var janelasFuturasInApp = new HashSet<int> { 0, 1, 3, 5 };
        const int limiteDiasAtraso = 5;

        var baseHoje = hojeFixo?.Date ?? TimeZoneInfo.ConvertTimeFromUtc(RelogioUtc(), BrasiliaTime.Tz).Date;
        var buscaDe = baseHoje.AddDays(-limiteDiasAtraso - FolgaFusoDias);
        var buscaAte = baseHoje.AddDays(DiasProximos + 1 + FolgaFusoDias);
        var novidadesDesde = RelogioUtc().AddHours(-24);

        var tarefas = await _context.Tarefas
            .Where(t => t.Prazo.HasValue &&
                        t.Prazo.Value >= buscaDe && t.Prazo.Value < buscaAte &&
                        t.Status != StatusTarefa.Concluida &&
                        t.Status != StatusTarefa.Cancelada &&
                        t.Status != StatusTarefa.Perdida)
            .Select(t => new
            {
                t.Id,
                t.TenantId,
                t.Titulo,
                Prazo = t.Prazo!.Value,
                DestinatarioId = t.ResponsavelId ?? t.CriadoPorId,
                DestinatarioNome = t.ResponsavelId.HasValue ? t.Responsavel!.Nome : t.CriadoPor!.Nome,
                DestinatarioEmail = t.ResponsavelId.HasValue ? t.Responsavel!.Email : t.CriadoPor!.Email,
                EhPrazo = t.Tipo == TipoTarefa.Prazo
            })
            .Where(x => x.DestinatarioEmail != null && x.DestinatarioEmail != "")
            .ToListAsync();

        var prazosAgenda = await _context.Eventos
            .Where(e => e.Tipo == TipoEvento.Prazo && e.ResponsavelId.HasValue &&
                        e.DataHora >= buscaDe && e.DataHora < buscaAte)
            .Select(e => new
            {
                e.Id,
                e.TenantId,
                e.Titulo,
                Prazo = e.DataHora,
                DestinatarioId = e.ResponsavelId!.Value,
                DestinatarioNome = e.Responsavel!.Nome,
                DestinatarioEmail = e.Responsavel!.Email,
                EhPrazo = true
            })
            .Where(x => x.DestinatarioEmail != null && x.DestinatarioEmail != "")
            .ToListAsync();

        var eventos = await _context.Eventos
            .Where(e => e.Tipo != TipoEvento.Prazo && e.ResponsavelId.HasValue &&
                        e.DataHora >= buscaDe && e.DataHora < buscaAte)
            .Select(e => new
            {
                e.Id,
                e.TenantId,
                e.Titulo,
                e.DataHora,
                e.Local,
                DestinatarioId = e.ResponsavelId!.Value,
                DestinatarioNome = e.Responsavel!.Nome,
                DestinatarioEmail = e.Responsavel!.Email
            })
            .ToListAsync();

        // Andamentos capturados nas últimas 24h (não os lançados à mão), para o advogado responsável.
        var andamentos = await _context.Andamentos
            .Where(a => a.CriadoEm >= novidadesDesde && a.Fonte != FonteAndamento.Manual &&
                        a.Processo.AdvogadoResponsavelId != null)
            .Select(a => new AndamentoNovo(a.TenantId, a.Processo.AdvogadoResponsavelId!.Value, a.ProcessoId,
                a.Processo.NumeroCNJ, a.Processo.Classe ?? a.Processo.TipoAcao, a.Data, a.Descricao))
            .ToListAsync();

        // Sugestões da triagem ainda não confirmadas e publicações novas sem providência.
        var sugestoes = await _context.TriagensPublicacao
            .Where(t => t.Status == StatusTriagem.Sugerida && t.Publicacao.Processo != null &&
                        t.Publicacao.Processo.AdvogadoResponsavelId != null)
            .Select(t => new
            {
                t.TenantId,
                ResponsavelId = t.Publicacao.Processo!.AdvogadoResponsavelId!.Value,
                t.Ato,
                t.Resumo,
                t.Publicacao.NumeroCNJ,
                t.Publicacao.Diario,
                t.Publicacao.DataPublicacao,
                t.Vencimento,
                t.PrazoDias,
                t.TipoCalculo,
                t.AudienciaDataHora,
                t.CriadoEm
            })
            .ToListAsync();
        var semProvidencia = await _context.TriagensPublicacao
            .Where(t => t.Status == StatusTriagem.SemAcao && t.Publicacao.CapturaEm >= novidadesDesde &&
                        t.Publicacao.Processo != null && t.Publicacao.Processo.AdvogadoResponsavelId != null)
            .GroupBy(t => new { t.TenantId, ResponsavelId = t.Publicacao.Processo!.AdvogadoResponsavelId!.Value })
            .Select(g => new { g.Key.TenantId, g.Key.ResponsavelId, Total = g.Count() })
            .ToListAsync();

        var itensTarefas = tarefas.Concat(prazosAgenda).ToList();
        var tarefasPorDestinatario = itensTarefas.ToLookup(t => (t.TenantId, t.DestinatarioId));
        var eventosPorDestinatario = eventos.ToLookup(e => (e.TenantId, e.DestinatarioId));
        var andamentosPorDestinatario = andamentos.ToLookup(a => (a.TenantId, a.ResponsavelId));
        var sugestoesPorDestinatario = sugestoes.ToLookup(s => (s.TenantId, s.ResponsavelId));
        var semProvidenciaPorDestinatario = semProvidencia.ToDictionary(s => (s.TenantId, s.ResponsavelId), s => s.Total);

        var destinatarios = itensTarefas
            .Select(t => new Destinatario(t.TenantId, t.DestinatarioId, t.DestinatarioNome, t.DestinatarioEmail))
            .Concat(eventos.Select(e => new Destinatario(e.TenantId, e.DestinatarioId, e.DestinatarioNome, e.DestinatarioEmail)))
            .DistinctBy(d => (d.TenantId, d.Id))
            .ToList();
        // Quem só tem novidades (andamentos ou publicações), sem tarefas nem eventos no período.
        var conhecidos = destinatarios.Select(d => d.Id).ToHashSet();
        var soNovidades = andamentos.Select(a => a.ResponsavelId)
            .Concat(sugestoes.Select(s => s.ResponsavelId))
            .Concat(semProvidencia.Select(s => s.ResponsavelId))
            .Where(id => !conhecidos.Contains(id))
            .Distinct()
            .ToList();
        if (soNovidades.Count > 0)
            destinatarios.AddRange(await _context.Users
                .Where(u => soNovidades.Contains(u.Id) && u.Ativo)
                .Select(u => new Destinatario(u.TenantId, u.Id, u.Nome, u.Email))
                .ToListAsync());

        var tenantIds = destinatarios.Select(d => d.TenantId).Distinct().ToList();
        var planos = await _context.Tenants
            .Where(t => tenantIds.Contains(t.Id))
            .ToDictionaryAsync(t => t.Id, t => t.Plano);

        foreach (var dest in destinatarios)
        {
            try
            {
                // Sem data fixa (produção), só a partir das 7h no fuso do escritório.
                if (hojeFixo is null && (await AgoraDoTenantAsync(dest.TenantId)).Hour < HoraResumo)
                    continue;

                var hoje = await HojeDoTenantAsync(dest.TenantId, hojeFixo);
                var hojeStr = hoje.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
                var chave = (dest.TenantId, dest.Id);
                var plano = planos.GetValueOrDefault(dest.TenantId, PlanoTipo.Free);
                var prefs = new Dictionary<string, (bool, bool)>();
                var tarefasEmail = new List<ResumoTarefaItem>();
                var tarefasInApp = new List<ResumoTarefaItem>();
                var eventosEmail = new List<ResumoEventoItem>();

                foreach (var t in tarefasPorDestinatario[chave].OrderBy(t => t.Prazo))
                {
                    var diasPrazo = (t.Prazo.Date - hoje).Days;
                    if (diasPrazo < -limiteDiasAtraso || diasPrazo > DiasProximos) continue;

                    var categoria = t.EhPrazo ? "Prazos" : diasPrazo < 0 ? "TarefaAtrasada" : "PrazoTarefa";
                    var (email, inApp) = await PreferenciaAsync(prefs, dest.TenantId, dest.Id, categoria);

                    var item = new ResumoTarefaItem(t.Titulo, t.Prazo, diasPrazo);
                    if (email) tarefasEmail.Add(item);
                    if (inApp && (diasPrazo < 0 || janelasFuturasInApp.Contains(diasPrazo))) tarefasInApp.Add(item);
                }

                foreach (var e in eventosPorDestinatario[chave].OrderBy(e => e.DataHora))
                {
                    var dias = (e.DataHora.Date - hoje).Days;
                    if (dias < 0 || dias > DiasProximos) continue;

                    var (email, inApp) = await PreferenciaAsync(prefs, dest.TenantId, dest.Id, "PrazoEvento");
                    if (email) eventosEmail.Add(new ResumoEventoItem(e.Titulo, e.DataHora, e.Local, dias));

                    // In-app: um alerta por evento, na véspera.
                    if (inApp && dias == 1)
                        await CriarNotificacaoAsync(
                            dest.TenantId, dest.Id,
                            TipoNotificacao.PrazoEvento,
                            "Evento amanhã",
                            $"\"{e.Titulo}\" amanhã às {e.DataHora.ToString("HH:mm", BrasiliaTime.PtBr)}.",
                            "/pages/agenda.html", $"evento-{e.Id}-1d-{hojeStr}");
                }

                // Publicações e andamentos seguem a preferência "Publicações" (a mesma dos avisos avulsos).
                var (novidadesEmail, _) = await PreferenciaAsync(prefs, dest.TenantId, dest.Id, "Publicacoes");
                var temPublicacoes = Domain.PlanoRestricoes.PermiteCapturacaoPublicacoes(plano);
                var sugestoesEmail = novidadesEmail && temPublicacoes
                    ? sugestoesPorDestinatario[chave]
                        .OrderBy(s => s.Vencimento ?? s.AudienciaDataHora ?? DateTime.MaxValue)
                        .Take(MaxSugestoesResumo)
                        .Select(s => new ResumoSugestaoItem(
                            s.Ato ?? s.Resumo ?? (s.AudienciaDataHora is not null ? "Audiência" : "Prazo da publicação"),
                            s.NumeroCNJ, s.Diario, s.DataPublicacao, s.Vencimento, s.PrazoDias,
                            s.TipoCalculo != TipoCalculo.DiasCorridos, s.AudienciaDataHora))
                        .ToList()
                    : [];
                var semProvidenciaEmail = novidadesEmail && temPublicacoes
                    ? semProvidenciaPorDestinatario.GetValueOrDefault(chave)
                    : 0;
                var andamentosEmail = novidadesEmail
                    ? andamentosPorDestinatario[chave]
                        .GroupBy(a => a.ProcessoId)
                        .OrderByDescending(g => g.Max(a => a.Data))
                        .Take(MaxProcessosResumo)
                        .Select(g => g.OrderByDescending(a => a.Data).Take(MaxAndamentosPorProcesso).ToList())
                        .ToList()
                    : [];

                var enviarEmail = !string.IsNullOrEmpty(dest.Email) &&
                                  (tarefasEmail.Count > 0 || eventosEmail.Count > 0 || sugestoesEmail.Count > 0 ||
                                   semProvidenciaEmail > 0 || andamentosEmail.Count > 0);
                if (!enviarEmail && tarefasInApp.Count == 0) continue;

                var chaveDigest = $"digest-tarefas-{dest.Id}-{hojeStr}";
                var jaEnviado = await _context.Notificacoes.AnyAsync(n => n.ChaveDedup == chaveDigest);
                if (jaEnviado) continue;

                if (enviarEmail)
                {
                    var processos = await ProcessosDoResumoAsync(andamentosEmail);
                    var temAgoraOuNovidade = tarefasEmail.Any(t => t.Dias <= 0) || eventosEmail.Any(e => e.Dias == 0) ||
                                             sugestoesEmail.Count > 0 || semProvidenciaEmail > 0 || processos.Count > 0;
                    string? visaoGeral = null;
                    if (temAgoraOuNovidade && _services is not null && Domain.PlanoRestricoes.PermiteAssistenteIA(plano))
                        (visaoGeral, processos) = await TextosDaIAAsync(hoje, tarefasEmail, eventosEmail, sugestoesEmail, processos);

                    // Convite para o plano seguinte: no máximo uma vez por semana (segunda-feira).
                    var convite = hoje.DayOfWeek != DayOfWeek.Monday ? ConviteResumo.Nenhum
                        : plano == PlanoTipo.Free ? ConviteResumo.Plus
                        : plano == PlanoTipo.Plus ? ConviteResumo.Pro
                        : ConviteResumo.Nenhum;

                    await _emailService.EnviarResumoDiarioAsync(
                        dest.Email!, dest.Nome, tarefasEmail, eventosEmail,
                        new ResumoDiarioExtras(hoje, visaoGeral, sugestoesEmail, semProvidenciaEmail, processos,
                            temPublicacoes, convite));
                }

                // A notificação também é o registro de dedup do dia. Se o usuário desligou o
                // in-app das tarefas (ou o resumo não tem tarefas), ela só registra o envio do
                // e-mail e já nasce lida.
                var itensNotificacao = tarefasInApp.Count > 0 ? tarefasInApp : tarefasEmail;
                string titulo, mensagem, url;
                TipoNotificacao tipo;
                if (itensNotificacao.Count > 0)
                {
                    mensagem = string.Join("\n", itensNotificacao.Select(i =>
                        i.Dias < 0 ? $"• [ATRASADA {Math.Abs(i.Dias)}d] {i.Titulo}"
                        : i.Dias == 0 ? $"• [HOJE] {i.Titulo}"
                        : $"• [{i.Dias}d] {i.Titulo}"));
                    var atrasadasCount = itensNotificacao.Count(i => i.Dias < 0);
                    titulo = atrasadasCount > 0
                        ? $"{itensNotificacao.Count} tarefa(s) — {atrasadasCount} atrasada(s)"
                        : $"{itensNotificacao.Count} tarefa(s) com prazo próximo";
                    tipo = TipoNotificacao.PrazoTarefa;
                    url = "/pages/tarefas.html";
                }
                else if (eventosEmail.Count > 0)
                {
                    titulo = $"Resumo diário: {eventosEmail.Count} evento(s)";
                    mensagem = $"Email enviado para {dest.Email}";
                    tipo = TipoNotificacao.PrazoEvento;
                    url = "/pages/agenda.html";
                }
                else
                {
                    titulo = "Resumo diário enviado";
                    mensagem = $"Email enviado para {dest.Email}";
                    tipo = TipoNotificacao.Geral;
                    url = sugestoesEmail.Count > 0 ? "/pages/publicacoes.html" : "/pages/processos.html";
                }

                _context.Notificacoes.Add(new Domain.Entities.Notificacao
                {
                    Id = Guid.NewGuid(),
                    TenantId = dest.TenantId,
                    UsuarioId = dest.Id,
                    Tipo = tipo,
                    Titulo = Limitar(titulo, 300),
                    Mensagem = Limitar(mensagem, 1000),
                    Url = url,
                    Lida = tarefasInApp.Count == 0,
                    CriadaEm = DateTime.UtcNow,
                    ChaveDedup = chaveDigest
                });
                await _context.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                DescartarPendentes();
                _logger.LogError(ex, "Erro ao gerar resumo diário para usuário {UsuarioId}", dest.Id);
            }
        }
    }

    /// <summary>
    /// Um item por processo com o andamento mais recente, no texto do tribunal. Nome: "Autor x Réu"
    /// quando o processo tem as partes cadastradas; senão a classe/tipo de ação.
    /// </summary>
    private async Task<List<ResumoProcessoItem>> ProcessosDoResumoAsync(List<List<AndamentoNovo>> porProcesso)
    {
        if (porProcesso.Count == 0) return [];
        var ids = porProcesso.Select(g => g[0].ProcessoId).ToList();
        var partes = (await _context.Set<Domain.Entities.ProcessoParte>()
                .Where(p => ids.Contains(p.ProcessoId) &&
                            (p.TipoParte == TipoParteProcesso.Autor || p.TipoParte == TipoParteProcesso.Reu))
                .Select(p => new { p.ProcessoId, p.TipoParte, p.Contato.Nome })
                .ToListAsync())
            .ToLookup(p => p.ProcessoId);

        return porProcesso.Select(g =>
        {
            var a = g[0];
            var autor = partes[a.ProcessoId].FirstOrDefault(p => p.TipoParte == TipoParteProcesso.Autor)?.Nome;
            var reu = partes[a.ProcessoId].FirstOrDefault(p => p.TipoParte == TipoParteProcesso.Reu)?.Nome;
            var nome = autor is not null && reu is not null ? $"{autor} x {reu}" : a.Classe ?? "Processo";
            return new ResumoProcessoItem(nome, a.NumeroCNJ, a.Data, Limitar(a.Descricao.Trim(), 400), null, false);
        }).ToList();
    }

    /// <summary>
    /// Visão geral do dia e os andamentos explicados, numa única chamada à IA configurada. Se a IA
    /// falhar ou responder fora do formato, o resumo segue com o texto do tribunal e sem visão geral.
    /// </summary>
    private async Task<(string? VisaoGeral, List<ResumoProcessoItem> Processos)> TextosDaIAAsync(
        DateTime hoje, List<ResumoTarefaItem> tarefas, List<ResumoEventoItem> eventos,
        List<ResumoSugestaoItem> sugestoes, List<ResumoProcessoItem> processos)
    {
        var pt = BrasiliaTime.PtBr;
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"Hoje: {hoje.ToString("dddd, dd/MM/yyyy", pt)}");
        foreach (var t in tarefas.Where(t => t.Dias < 0))
            sb.AppendLine($"Tarefa atrasada há {-t.Dias} dia(s): {t.Titulo}");
        foreach (var t in tarefas.Where(t => t.Dias == 0))
            sb.AppendLine($"Tarefa que vence hoje às {t.Prazo.ToString("HH:mm", pt)}: {t.Titulo}");
        foreach (var e in eventos.Where(e => e.Dias == 0))
            sb.AppendLine($"Compromisso hoje às {e.DataHora.ToString("HH:mm", pt)}: {e.Titulo}");
        var semana = tarefas.Count(t => t.Dias > 0) + eventos.Count(e => e.Dias > 0);
        if (semana > 0) sb.AppendLine($"Prazos e compromissos nos próximos 7 dias: {semana}");
        foreach (var s in sugestoes)
            sb.AppendLine($"Publicação com sugestão a confirmar: {s.Titulo}" +
                          (s.Vencimento is { } v ? $" (vence {v.ToString("dd/MM/yyyy", pt)})" : ""));
        if (processos.Count > 0)
        {
            sb.AppendLine("Processos com andamento novo:");
            for (var i = 0; i < processos.Count; i++)
                sb.AppendLine($"[{i + 1}] {processos[i].Titulo}, andamento de {processos[i].Data.ToString("dd/MM/yyyy", pt)}: {processos[i].Texto}");
        }

        try
        {
            var servico = (IIAService?)_services!.GetService(typeof(IIAService));
            if (servico is null) return (null, processos);
            var ia = ResumoDiaParser.Parse(await servico.ResumirDiaAsync(sb.ToString()));
            if (ia is null) return (null, processos);
            var explicados = processos.Select((p, i) =>
                ia.Processos.TryGetValue(i + 1, out var t) ? p with { Texto = t.Texto, ProximoPasso = t.ProximoPasso, TextoIA = true } : p)
                .ToList();
            return (ia.VisaoGeral, explicados);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "IA indisponível no resumo diário; enviando sem os textos da IA");
            return (null, processos);
        }
    }

    private async Task AlertarTrialExpirandoAsync(DateTime hoje)
    {
        var limites = new[] { 7, 3, 1 };

        foreach (var dias in limites)
        {
            var dataAlvo = hoje.AddDays(dias).Date;

            var tenants = await _context.Tenants
                .Where(t => t.Status == StatusTenant.Trial &&
                            t.TrialExpiraEm.HasValue &&
                            t.TrialExpiraEm.Value.Date == dataAlvo)
                .Select(t => new { t.Id, t.Nome })
                .ToListAsync();

            foreach (var tenant in tenants)
            {
                var admins = await _context.Users
                    .Where(u => u.TenantId == tenant.Id && u.Perfil == PerfilUsuario.Admin && u.Ativo)
                    .Select(u => new { u.Id, u.Nome, u.Email })
                    .ToListAsync();

                foreach (var admin in admins)
                {
                    try
                    {
                        var chave = $"trial-{tenant.Id}-{dias}d-{hoje:yyyyMMdd}";
                        var permiteInApp = await _prefs.PermiteInAppAsync(tenant.Id, admin.Id, "TrialExpirando");

                        if (!string.IsNullOrEmpty(admin.Email))
                        {
                            var chaveEmail = $"email-trial-{tenant.Id}-{dias}d-{hoje:yyyyMMdd}";
                            var emailJaEnviado = await _context.Notificacoes.AnyAsync(n => n.ChaveDedup == chaveEmail);
                            if (!emailJaEnviado)
                            {
                                await _emailService.EnviarTrialExpirandoAsync(admin.Email, tenant.Nome, dias);
                                _context.Notificacoes.Add(new Domain.Entities.Notificacao
                                {
                                    Id = Guid.NewGuid(),
                                    TenantId = tenant.Id,
                                    UsuarioId = admin.Id,
                                    Tipo = TipoNotificacao.TrialExpirando,
                                    Titulo = $"Email trial {tenant.Nome}",
                                    Mensagem = $"Email enviado para {admin.Email}",
                                    Lida = false,
                                    CriadaEm = DateTime.UtcNow,
                                    ChaveDedup = chaveEmail
                                });
                                await _context.SaveChangesAsync();
                            }
                        }

                        if (permiteInApp)
                            await CriarNotificacaoAsync(
                                tenant.Id, admin.Id,
                                TipoNotificacao.TrialExpirando,
                                $"Trial expira em {dias} dia(s)",
                                $"Seu período de trial expira em {dias} dia(s). Assine para continuar.",
                                "/pages/assinatura.html", chave);
                    }
                    catch (Exception ex)
                    {
                        DescartarPendentes();
                        _logger.LogError(ex, "Erro ao alertar trial tenant {TenantId}", tenant.Id);
                    }
                }
            }
        }
    }

    // Período grátis de código promocional: o tenant nasce Ativo (não Trial) com PlanoExpiraEm,
    // então não entra em AlertarTrialExpirandoAsync. Ficam de fora assinaturas pagas (Stripe/
    // AbacatePay) e canceladas (Status Cancelado, que também usam PlanoExpiraEm).
    private async Task AlertarPlanoPromocionalExpirandoAsync(DateTime hoje)
    {
        var limites = new[] { 7, 3, 1 };
        var janelaIni = hoje.AddDays(limites.Min() - FolgaFusoDias);
        var janelaFim = hoje.AddDays(limites.Max() + 1 + FolgaFusoDias);

        var candidatos = await _context.Tenants
            .Where(t => t.Status == StatusTenant.Ativo &&
                        t.Plano != PlanoTipo.Free &&
                        t.PlanoExpiraEm.HasValue &&
                        t.PlanoExpiraEm.Value >= janelaIni &&
                        t.PlanoExpiraEm.Value < janelaFim &&
                        t.StripeSubscriptionId == null &&
                        t.AbacatePayBillingId == null)
            .Select(t => new { t.Id, t.Nome, t.Plano, ExpiraEm = t.PlanoExpiraEm!.Value })
            .ToListAsync();

        foreach (var tenant in candidatos)
        {
            // PlanoExpiraEm é um instante UTC; o dia do fim é o do calendário de Brasília.
            var dias = (BrasiliaTime.DeUtc(tenant.ExpiraEm).Date - hoje.Date).Days;
            if (!limites.Contains(dias)) continue;

            var admins = await _context.Users
                .Where(u => u.TenantId == tenant.Id && u.Perfil == PerfilUsuario.Admin && u.Ativo)
                .Select(u => new { u.Id, u.Nome, u.Email })
                .ToListAsync();

            foreach (var admin in admins)
            {
                try
                {
                    var chave = $"promo-{tenant.Id}-{dias}d-{hoje:yyyyMMdd}";
                    var permiteInApp = await _prefs.PermiteInAppAsync(tenant.Id, admin.Id, "TrialExpirando");

                    if (!string.IsNullOrEmpty(admin.Email))
                    {
                        var chaveEmail = $"email-promo-{tenant.Id}-{dias}d-{hoje:yyyyMMdd}";
                        var emailJaEnviado = await _context.Notificacoes.AnyAsync(n => n.ChaveDedup == chaveEmail);
                        if (!emailJaEnviado)
                        {
                            await _emailService.EnviarPlanoPromocionalExpirandoAsync(
                                admin.Email, tenant.Nome, tenant.Plano, tenant.ExpiraEm, dias);
                            _context.Notificacoes.Add(new Domain.Entities.Notificacao
                            {
                                Id = Guid.NewGuid(),
                                TenantId = tenant.Id,
                                UsuarioId = admin.Id,
                                Tipo = TipoNotificacao.TrialExpirando,
                                Titulo = $"Email período promocional {tenant.Nome}",
                                Mensagem = $"Email enviado para {admin.Email}",
                                Lida = false,
                                CriadaEm = DateTime.UtcNow,
                                ChaveDedup = chaveEmail
                            });
                            await _context.SaveChangesAsync();
                        }
                    }

                    if (permiteInApp)
                        await CriarNotificacaoAsync(
                            tenant.Id, admin.Id,
                            TipoNotificacao.TrialExpirando,
                            $"Plano {tenant.Plano} expira em {dias} dia(s)",
                            $"Seu período promocional do plano {tenant.Plano} expira em {dias} dia(s). Assine para continuar com os recursos.",
                            "/pages/assinatura.html", chave);
                }
                catch (Exception ex)
                {
                    DescartarPendentes();
                    _logger.LogError(ex, "Erro ao alertar período promocional tenant {TenantId}", tenant.Id);
                }
            }
        }
    }

    private async Task CriarNotificacaoAsync(Guid tenantId, Guid usuarioId, TipoNotificacao tipo,
        string titulo, string mensagem, string? url, string chaveDedup)
    {
        var jaExiste = await _context.Notificacoes
            .AnyAsync(n => n.ChaveDedup == chaveDedup);

        if (jaExiste) return;

        _context.Notificacoes.Add(new Domain.Entities.Notificacao
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            UsuarioId = usuarioId,
            Tipo = tipo,
            Titulo = titulo,
            Mensagem = mensagem,
            Url = url,
            Lida = false,
            CriadaEm = DateTime.UtcNow,
            ChaveDedup = chaveDedup
        });
        await _context.SaveChangesAsync();
    }
}
