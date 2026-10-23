using LegalManager.Application.Interfaces;
using LegalManager.Domain.Entities;
using LegalManager.Domain.Enums;
using LegalManager.Infrastructure.Persistence;
using LegalManager.Infrastructure.Services;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace LegalManager.Infrastructure.Jobs;

/// <summary>
/// Triagem das publicações capturadas (Escavador, DJe e manuais), a cada 5 min pelo Hangfire: a IA
/// configurada no sistema (IA:Provider/IA:Model, o mesmo modelo das outras funções) lê a
/// publicação e extrai tipo, resumo, providência, prazo e audiência; o código calcula o
/// vencimento (<see cref="PrazoPublicacao"/>) e grava a sugestão em <see cref="TriagemPublicacao"/>,
/// que o advogado confirma na tela de Publicações. Prazo urgente avisa o responsável na hora.
/// </summary>
public class TriagemPublicacoesJob(
    AppDbContext db, IIAService ia, IEmailService email, ILogger<TriagemPublicacoesJob> logger)
{
    public const int MaxTentativas = 3;
    /// <summary>Vencimento (ou audiência) até esta quantidade de dias marca a publicação como urgente.</summary>
    public const int DiasUrgencia = 5;
    /// <summary>Só entram na fila publicações capturadas nesse período (evita reprocessar o acervo antigo).</summary>
    public static readonly TimeSpan JanelaCaptura = TimeSpan.FromDays(15);
    private const int Lote = 50;

    // Mesmos planos de PlanoRestricoes.PermiteCapturacaoPublicacoes (a tela de Publicações).
    private static readonly PlanoTipo[] PlanosComPublicacoes = [PlanoTipo.Pro, PlanoTipo.Max, PlanoTipo.Enterprise];

    [DisableConcurrentExecution(10 * 60)]
    [AutomaticRetry(Attempts = 0)]
    public Task ExecutarAsync() => ExecutarAsync(CancellationToken.None);

    /// <param name="hojeFixo">Para testes: o "hoje" usado na urgência (padrão: hoje no fuso do escritório).</param>
    public async Task ExecutarAsync(CancellationToken ct, DateTime? hojeFixo = null)
    {
        await EnfileirarAsync(ct);

        var pendentes = await db.TriagensPublicacao
            .Where(t => t.Status == StatusTriagem.Pendente)
            .OrderBy(t => t.CriadoEm)
            .Select(t => t.Id)
            .Take(Lote)
            .ToListAsync(ct);
        if (pendentes.Count == 0) return;

        logger.LogInformation("[TriagemPublicacoes] Triando {N} publicações", pendentes.Count);
        foreach (var id in pendentes)
        {
            try
            {
                await TriarAsync(id, ct, hojeFixo);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "[TriagemPublicacoes] Falha ao triar {Id}", id);
                db.ChangeTracker.Clear();
                // Conta a tentativa mesmo assim: senão a mesma publicação voltaria a cada execução.
                var triagem = await db.TriagensPublicacao.FirstOrDefaultAsync(t => t.Id == id, ct);
                if (triagem is { Status: StatusTriagem.Pendente })
                {
                    triagem.Tentativas++;
                    await RegistrarFalhaAsync(triagem, $"Erro ao processar: {ex.Message}", ct);
                }
            }
        }
    }

    private async Task EnfileirarAsync(CancellationToken ct)
    {
        var desde = DateTime.UtcNow - JanelaCaptura;
        var novas = await db.Publicacoes
            .Where(p => p.CapturaEm >= desde && p.Status != StatusPublicacao.Arquivada
                        && PlanosComPublicacoes.Contains(p.Tenant.Plano)
                        && !db.TriagensPublicacao.Any(t => t.PublicacaoId == p.Id))
            .OrderBy(p => p.CapturaEm)
            .Select(p => new { p.Id, p.TenantId })
            .Take(500)
            .ToListAsync(ct);
        if (novas.Count == 0) return;

        var agora = DateTime.UtcNow;
        db.TriagensPublicacao.AddRange(novas.Select(p => new TriagemPublicacao
        {
            Id = Guid.NewGuid(),
            TenantId = p.TenantId,
            PublicacaoId = p.Id,
            Status = StatusTriagem.Pendente,
            CriadoEm = agora
        }));
        await db.SaveChangesAsync(ct);
    }

    public async Task TriarAsync(Guid triagemId, CancellationToken ct = default, DateTime? hojeFixo = null)
    {
        var triagem = await db.TriagensPublicacao
            .Include(t => t.Publicacao).ThenInclude(p => p.Processo)
            .FirstOrDefaultAsync(t => t.Id == triagemId && t.Status == StatusTriagem.Pendente, ct);
        if (triagem is null) return;
        var pub = triagem.Publicacao;
        var conteudo = string.IsNullOrWhiteSpace(pub.Conteudo) ? pub.Snippet ?? "" : pub.Conteudo;

        if (string.IsNullOrWhiteSpace(conteudo))
        {
            triagem.Status = StatusTriagem.SemAcao;
            triagem.ProcessadaEm = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
            return;
        }

        triagem.Tentativas++;
        TriagemExtraida? extraida;
        try
        {
            extraida = TriagemPublicacaoParser.Parse(await ia.TriarPublicacaoAsync(conteudo, ct));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "[TriagemPublicacoes] IA falhou na publicação {Id}", pub.Id);
            await RegistrarFalhaAsync(triagem, $"Falha ao chamar a IA: {ex.Message}", ct);
            return;
        }

        if (extraida is null)
        {
            await RegistrarFalhaAsync(triagem, "A resposta da IA veio fora do formato esperado.", ct);
            return;
        }

        await AplicarAsync(triagem, pub, conteudo, extraida, hojeFixo, ct);
        await db.SaveChangesAsync(ct);
    }

    private async Task RegistrarFalhaAsync(TriagemPublicacao triagem, string erro, CancellationToken ct)
    {
        triagem.Erro = erro.Length > 1000 ? erro[..1000] : erro;
        if (triagem.Tentativas >= MaxTentativas)
        {
            triagem.Status = StatusTriagem.Falhou;
            triagem.ProcessadaEm = DateTime.UtcNow;
        }
        await db.SaveChangesAsync(ct);
    }

    private async Task AplicarAsync(
        TriagemPublicacao triagem, Publicacao pub, string conteudo, TriagemExtraida ext, DateTime? hojeFixo, CancellationToken ct)
    {
        triagem.Resumo = ext.Resumo;
        triagem.Ato = ext.Ato;
        triagem.Erro = null;
        triagem.ProcessadaEm = DateTime.UtcNow;
        pub.Tipo = ext.Tipo;
        if (ext.Resumo is not null) pub.ClassificacaoIA = ext.Resumo;

        var criminal = pub.Processo?.AreaDireito == AreaDireito.Criminal;

        // Prazo: só se o trecho citado está na publicação e a unidade é contável em dias.
        if (ext.PrazoQuantidade is int dias and >= 1 and <= 365
            && TriagemPublicacaoParser.TrechoConfere(ext.PrazoTrecho, conteudo))
        {
            triagem.PrazoTrecho = Cortar(ext.PrazoTrecho, 500);
            TipoCalculo? tipo = ext.PrazoUnidade switch
            {
                "dias_uteis" => TipoCalculo.DiasUteis,
                "dias_corridos" => TipoCalculo.DiasCorridos,
                // Sem dizer úteis/corridos: CPC/CLT contam dias úteis; o CPP, corridos.
                "dias" => criminal ? TipoCalculo.DiasCorridos : TipoCalculo.DiasUteis,
                _ => null
            };
            if (tipo is not null)
            {
                var local = await PrazoPublicacao.LocalAsync(db, pub.TenantId,
                    UfProcesso.DoCnj(pub.Processo?.NumeroCNJ ?? pub.NumeroCNJ) ?? UfValida(pub.OrigemEstado),
                    pub.Processo?.Comarca, ct);
                var calc = PrazoPublicacao.Calcular(pub.DataPublicacao, pub.FonteCaptura != FonteCaptura.Manual,
                    dias, tipo.Value, criminal, local);
                triagem.PrazoDias = dias;
                triagem.TipoCalculo = tipo;
                triagem.DataPublicacao = calc.DataPublicacao;
                triagem.Vencimento = calc.Vencimento;
                triagem.MemoriaCalculo = Cortar(calc.Memoria, 1000);
            }
            else
            {
                triagem.MemoriaCalculo = $"Prazo em {ext.PrazoUnidade ?? "unidade não identificada"}: informe o vencimento.";
            }
        }

        if (ext.AudienciaDataHora is { } audiencia
            && audiencia.Date >= pub.DataPublicacao.Date
            && TriagemPublicacaoParser.TrechoConfere(ext.AudienciaTrecho, conteudo))
        {
            triagem.AudienciaDataHora = audiencia;
            triagem.AudienciaTrecho = Cortar(ext.AudienciaTrecho, 500);
        }

        triagem.Status = ext.ExigeAcao || triagem.Vencimento is not null || triagem.AudienciaDataHora is not null
            ? StatusTriagem.Sugerida
            : StatusTriagem.SemAcao;

        // Urgência pelo calendário do escritório — não pela IA.
        var hoje = hojeFixo?.Date ?? FusoHorario.HojeParede(await db.DoTenantAsync(pub.TenantId, ct));
        var data = triagem.Vencimento ?? triagem.AudienciaDataHora;
        var diasRestantes = data is null ? (int?)null : (data.Value.Date - hoje).Days;
        pub.Urgente = triagem.Status == StatusTriagem.Sugerida && diasRestantes <= DiasUrgencia;
        if (pub.Urgente) await AvisarUrgenteAsync(triagem, pub, diasRestantes!.Value, ct);
    }

    /// <summary>Sino e e-mail ao advogado responsável, conforme a preferência "Publicações" dele.</summary>
    private async Task AvisarUrgenteAsync(TriagemPublicacao triagem, Publicacao pub, int diasRestantes, CancellationToken ct)
    {
        if (pub.Processo?.AdvogadoResponsavelId is not { } usuarioId) return;
        var prefs = await db.PreferenciasNotificacoes.AsNoTracking()
            .FirstOrDefaultAsync(p => p.TenantId == pub.TenantId && p.UsuarioId == usuarioId, ct)
            ?? new PreferenciasNotificacao();

        var cnj = pub.Processo.NumeroCNJ;
        var oQue = triagem.Ato ?? triagem.Resumo ?? "Publicação com prazo";
        var quando = triagem.Vencimento is { } v
            ? $"vence em {v.ToString("dd/MM/yyyy", BrasiliaTime.PtBr)}"
            : $"audiência em {triagem.AudienciaDataHora!.Value.ToString("dd/MM/yyyy 'às' HH:mm", BrasiliaTime.PtBr)}";

        if (prefs.PublicacoesInApp)
        {
            var chave = $"triagem-{triagem.Id}";
            if (!await db.Notificacoes.AnyAsync(n => n.ChaveDedup == chave, ct))
                db.Notificacoes.Add(new Notificacao
                {
                    Id = Guid.NewGuid(),
                    TenantId = pub.TenantId,
                    UsuarioId = usuarioId,
                    Tipo = TipoNotificacao.PrazoTarefa,
                    Titulo = $"Publicação urgente — {cnj}",
                    Mensagem = Cortar($"{oQue}: {quando}. Confira a sugestão em Publicações.", 500)!,
                    Url = "/pages/publicacoes.html",
                    CriadaEm = DateTime.UtcNow,
                    ChaveDedup = chave
                });
        }

        if (!prefs.PublicacoesEmail || triagem.Vencimento is null) return;
        var advogado = await db.Users.AsNoTracking()
            .Where(u => u.Id == usuarioId)
            .Select(u => new { u.Nome, u.Email })
            .FirstOrDefaultAsync(ct);
        if (string.IsNullOrWhiteSpace(advogado?.Email)) return;
        try
        {
            await email.EnviarAlertaPrazoProcessualAsync(advogado.Email, advogado.Nome, cnj,
                $"{oQue} (sugestão da IA, ainda não confirmada)", triagem.Vencimento.Value, Math.Max(diasRestantes, 0), ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[TriagemPublicacoes] Erro ao enviar e-mail de prazo urgente do processo {CNJ}", cnj);
        }
    }

    private static string? UfValida(string? uf) =>
        uf is { Length: 2 } && uf.All(char.IsLetter) ? uf.ToUpperInvariant() : null;

    private static string? Cortar(string? s, int max) => s is null || s.Length <= max ? s : s[..max];
}
