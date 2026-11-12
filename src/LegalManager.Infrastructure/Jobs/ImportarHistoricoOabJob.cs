using LegalManager.Application.Interfaces;
using LegalManager.Domain.Entities;
using LegalManager.Domain.Enums;
using LegalManager.Infrastructure.Escavador;
using LegalManager.Infrastructure.Persistence;
using LegalManager.Infrastructure.Services;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace LegalManager.Infrastructure.Jobs;

/// <summary>Resultado da busca retroativa de uma OAB (período consultado, páginas pagas e o que entrou).</summary>
public record ResultadoHistoricoOab(DateTime De, DateTime Ate, int Paginas, int Encontradas, int Novas);

/// <summary>
/// Busca retroativa de publicações por OAB: o monitoramento do Escavador só traz o que sai depois do
/// cadastro, então cada OAB ativa de um escritório Pro+ ganha, uma única vez, as publicações dos
/// <see cref="DiasRetroativos"/> dias anteriores ao cadastro (a janela que o monitoramento não cobre —
/// assim não duplica o que já chegou pelo webhook). Enfileirado no cadastro da OAB e de hora em hora
/// para as pendentes (<see cref="TenantOab.HistoricoImportadoEm"/> null — inclui as OABs cadastradas
/// antes deste job existir). Consulta paga: falha deixa a OAB pendente para a próxima execução.
/// As publicações entram como "Nova", sem notificação (são antigas); a triagem da IA as pega.
/// </summary>
public class ImportarHistoricoOabJob(
    AppDbContext db,
    IEscavadorService escavador,
    IConsultaExternaLogService consultaLog,
    ILogger<ImportarHistoricoOabJob> logger)
{
    public const int DiasRetroativos = 30;
    /// <summary>Teto de páginas por OAB (60 trechos cada) — limita o custo de uma OAB muito citada.</summary>
    public const int MaxPaginas = 10;
    private const int Lote = 20;

    // Mesmos planos de PlanoRestricoes.PermiteCapturacaoPublicacoes.
    private static readonly PlanoTipo[] PlanosComPublicacoes = [PlanoTipo.Pro, PlanoTipo.Max, PlanoTipo.Enterprise];

    [DisableConcurrentExecution(30 * 60)]
    [AutomaticRetry(Attempts = 0)]
    public Task ExecutarAsync() => ExecutarAsync(CancellationToken.None);

    public async Task ExecutarAsync(CancellationToken ct)
    {
        var pendentes = await db.TenantOabs
            .Where(o => o.Ativo && o.HistoricoImportadoEm == null && PlanosComPublicacoes.Contains(o.Tenant.Plano))
            .OrderBy(o => o.CriadoEm)
            .Take(Lote)
            .ToListAsync(ct);
        if (pendentes.Count == 0) return;

        foreach (var oab in pendentes)
        {
            try
            {
                await ImportarAsync(oab, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "[HistoricoOab] Falha na OAB {Uf}/{Numero} (tenant {TenantId}) — fica para a próxima execução",
                    oab.Uf, oab.Numero, oab.TenantId);
                db.ChangeTracker.Clear();
            }
        }
    }

    /// <summary>
    /// Roda a busca de uma OAB na hora (superadmin — o servidor de teste não tem Hangfire, e serve
    /// de suporte quando o histórico não veio). Ignora plano e "já importada": repetir não duplica
    /// (as chaves já gravadas são puladas), mas paga a consulta de novo. Erro do Escavador propaga.
    /// </summary>
    public async Task<ResultadoHistoricoOab?> ImportarOabAsync(Guid oabId, CancellationToken ct)
    {
        var oab = await db.TenantOabs.FirstOrDefaultAsync(o => o.Id == oabId, ct);
        return oab == null ? null : await ImportarAsync(oab, ct);
    }

    private async Task<ResultadoHistoricoOab> ImportarAsync(TenantOab oab, CancellationToken ct)
    {
        var ate = oab.CriadoEm.Date;
        var de = ate.AddDays(-DiasRetroativos);

        var trechos = new List<EscavadorPublicacaoDto>();
        var paginas = 0;
        for (var pagina = 1; pagina <= MaxPaginas; pagina++)
        {
            paginas = pagina;
            var p = pagina;
            var resultado = await consultaLog.RegistrarAsync(
                "Escavador", "BuscaPublicacoesPorOab", "OABs Monitoradas — Histórico de 30 dias",
                new { oab.Uf, oab.Numero, de, ate, pagina = p },
                () => escavador.BuscarPublicacoesPorOabAsync(oab.Numero, oab.Uf, de, ate, p, oab.Nome, ct),
                r => (r.Data.Count, r.Data.Select(x => new { x.NumeroCnj, x.Data, x.DiarioSigla })),
                tenantId: oab.TenantId, usuarioId: null, ct: ct);
            trechos.AddRange(resultado.Data);
            if (!resultado.TemProxima) break;
            if (pagina == MaxPaginas)
                logger.LogWarning("[HistoricoOab] OAB {Uf}/{Numero}: parou no teto de {Max} páginas", oab.Uf, oab.Numero, MaxPaginas);
        }

        trechos = trechos.GroupBy(t => t.Uuid).Select(g => g.First()).ToList();
        var chaves = trechos.Select(t => t.Uuid).ToList();
        var existentes = (await db.Publicacoes
                .Where(p => p.TenantId == oab.TenantId && p.UuidExterno != null && chaves.Contains(p.UuidExterno))
                .Select(p => p.UuidExterno!)
                .ToListAsync(ct))
            .ToHashSet();

        var cnjs = trechos.Where(t => t.NumeroCnj != null).Select(t => t.NumeroCnj!).Distinct().ToList();
        var processos = (await db.Processos
                .Where(p => p.TenantId == oab.TenantId && cnjs.Contains(p.NumeroCNJ))
                .ToListAsync(ct))
            .GroupBy(p => p.NumeroCNJ)
            .ToDictionary(g => g.Key, g => g.First());

        // No dia do cadastro o monitoramento pode já ter trazido a mesma publicação, com outra chave.
        var jaCapturadasNoDia = (await db.Publicacoes
                .Where(p => p.TenantId == oab.TenantId && p.NumeroCNJ != null
                            && p.DataPublicacao >= ate && p.DataPublicacao < ate.AddDays(1))
                .Select(p => p.NumeroCNJ!)
                .ToListAsync(ct))
            .ToHashSet();

        var agora = DateTime.UtcNow;
        var novas = 0;
        foreach (var t in trechos)
        {
            if (existentes.Contains(t.Uuid)) continue;
            if (FiltroPublicacaoOab.EhDeOutraOab(t.Snippet, oab.Numero, oab.Uf, oab.Nome)) continue;
            if (t.Data.Date == ate && t.NumeroCnj != null && jaCapturadasNoDia.Contains(t.NumeroCnj)) continue;

            var processo = t.NumeroCnj != null && processos.TryGetValue(t.NumeroCnj, out var p) ? p : null;
            var publicacao = new Publicacao
            {
                Id = Guid.NewGuid(),
                TenantId = oab.TenantId,
                ProcessoId = processo?.Id,
                NumeroCNJ = t.NumeroCnj,
                Diario = PublicacaoMapper.Cortar(t.Diario ?? t.DiarioSigla, 100) ?? "Diário Oficial",
                DataPublicacao = t.Data,
                Conteudo = t.Snippet ?? "",
                Tipo = EscavadorCallbackProcessor.InferirTipoPublicacao(null, t.Snippet),
                Status = StatusPublicacao.Nova,
                FonteCaptura = FonteCaptura.Escavador,
                UuidExterno = PublicacaoMapper.Cortar(t.Uuid, 100),
                IdExterno = t.Id > 0 ? t.Id.ToString(System.Globalization.CultureInfo.InvariantCulture) : null,
                LinkEscavador = PublicacaoMapper.Cortar(t.Link, 500),
                DiarioSigla = PublicacaoMapper.Cortar(t.DiarioSigla, 50),
                Tribunal = PublicacaoMapper.Cortar(t.DiarioSigla, 50),
                Snippet = PublicacaoMapper.Cortar(t.Snippet, 1000),
                CapturaEm = agora
            };
            db.Publicacoes.Add(publicacao);
            // A publicação também vira movimentação do processo (sem aviso: é antiga).
            if (processo != null)
            {
                db.Andamentos.Add(AndamentoDePublicacao.Montar(publicacao, processo.Id, agora));
                AndamentoDePublicacao.AtualizarUltimoAndamento(processo, publicacao.DataPublicacao);
            }
            novas++;
        }

        oab.HistoricoImportadoEm = agora;
        await db.SaveChangesAsync(ct);
        logger.LogInformation("[HistoricoOab] OAB {Uf}/{Numero} (tenant {TenantId}): {Paginas} página(s), {Encontradas} trecho(s), {Novas} nova(s)",
            oab.Uf, oab.Numero, oab.TenantId, paginas, trechos.Count, novas);
        return new ResultadoHistoricoOab(de, ate, paginas, trechos.Count, novas);
    }
}
