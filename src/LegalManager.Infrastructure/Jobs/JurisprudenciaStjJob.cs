using System.Text.Json;
using LegalManager.Domain.Entities;
using LegalManager.Infrastructure.Jurisprudencia;
using LegalManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace LegalManager.Infrastructure.Jobs;

/// <summary>
/// Carrega e atualiza a base de jurisprudência (tabela Precedentes) com os dados abertos do STJ:
/// todos os temas/precedentes qualificados (lista completa, recarregada a cada execução) e os espelhos
/// de acórdãos dos últimos <c>Jurisprudencia:MesesAcordaos</c> meses (padrão 12), um arquivo por mês e
/// por órgão julgador, pulando os já importados. Só roda com <c>Jurisprudencia:ImportarStj=true</c>.
/// </summary>
public class JurisprudenciaStjJob(
    AppDbContext context,
    StjDadosAbertos stj,
    IConfiguration config,
    ILogger<JurisprudenciaStjJob> logger)
{
    public const string ChaveConfigHabilitado = "Jurisprudencia:ImportarStj";
    public const int MesesPadrao = 12;
    private const int Lote = 500;

    public static bool Habilitado(IConfiguration config) => config.GetValue(ChaveConfigHabilitado, false);

    /// <summary>A base está vazia (primeira carga a fazer).</summary>
    public static async Task<bool> PrecisaCargaInicialAsync(AppDbContext db, CancellationToken ct = default) =>
        !await db.FontesPrecedentes.AnyAsync(f => f.Total > 0, ct);

    public async Task ExecutarAsync(CancellationToken ct = default)
    {
        await ImportarTemasAsync(ct);
        await ImportarEspelhosAsync(ct);
    }

    public async Task ImportarTemasAsync(CancellationToken ct = default)
    {
        var fonte = await FonteAsync(StjDadosAbertos.FonteTemas, ct);
        try
        {
            var arquivo = StjDadosAbertos.ArquivoDeTemas(await stj.ListarArquivosAsync(StjDadosAbertos.PacoteTemas, ct))
                ?? throw new InvalidOperationException("Arquivo de temas não encontrado no portal do STJ.");
            var temas = StjDadosAbertos.LerTemas(await stj.BaixarAsync(arquivo.Url, ct), DateTime.UtcNow);
            if (temas.Count == 0) throw new InvalidOperationException("Arquivo de temas veio vazio.");

            await GravarAsync(temas, StjDadosAbertos.FonteTemas, ct);
            fonte = await FonteAsync(StjDadosAbertos.FonteTemas, ct);
            fonte.Total = await context.Precedentes.CountAsync(p => p.Fonte == StjDadosAbertos.FonteTemas, ct);
            // O CSV traz todos os temas: dá para afirmar que um número que não está nele não existe.
            fonte.ListaCompleta = true;
            fonte.ArquivosProcessadosJson = JsonSerializer.Serialize(new[] { arquivo.Chave });
            fonte.AtualizadoEm = DateTime.UtcNow;
            fonte.UltimoErro = null;
            await context.SaveChangesAsync(ct);
            logger.LogInformation("Jurisprudência: {Total} temas/precedentes qualificados do STJ", fonte.Total);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Jurisprudência: falha ao importar temas do STJ");
            await RegistrarErroAsync(StjDadosAbertos.FonteTemas, ex.Message, ct);
        }
    }

    public async Task ImportarEspelhosAsync(CancellationToken ct = default)
    {
        var meses = Math.Max(1, config.GetValue("Jurisprudencia:MesesAcordaos", MesesPadrao));
        var hoje = DateTime.UtcNow.Date;
        var desde = new DateTime(hoje.Year, hoje.Month, 1).AddMonths(-meses);

        foreach (var pacote in StjDadosAbertos.PacotesEspelhos)
        {
            var chaveFonte = StjDadosAbertos.FonteDoPacote(pacote);
            try
            {
                var fonte = await FonteAsync(chaveFonte, ct);
                var processados = JsonSerializer.Deserialize<HashSet<string>>(fonte.ArquivosProcessadosJson) ?? [];
                var arquivos = StjDadosAbertos.ArquivosDeEspelhos(await stj.ListarArquivosAsync(pacote, ct), desde);

                foreach (var (arquivo, mes) in arquivos.Where(a => !processados.Contains(a.Arquivo.Chave)))
                {
                    var acordaos = StjDadosAbertos.LerEspelhos(await stj.BaixarAsync(arquivo.Url, ct), chaveFonte, DateTime.UtcNow);
                    await GravarAsync(acordaos, chaveFonte, ct);

                    fonte = await FonteAsync(chaveFonte, ct);
                    processados.Add(arquivo.Chave);
                    fonte.ArquivosProcessadosJson = JsonSerializer.Serialize(processados);
                    fonte.Total = await context.Precedentes.CountAsync(p => p.Fonte == chaveFonte, ct);
                    fonte.AtualizadoEm = DateTime.UtcNow;
                    fonte.UltimoErro = null;
                    await context.SaveChangesAsync(ct);
                    logger.LogInformation("Jurisprudência: {Fonte} {Mes:yyyy-MM}: {Qtd} acórdãos", chaveFonte, mes, acordaos.Count);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Um órgão com problema não impede os outros; tenta de novo na próxima execução.
                logger.LogWarning(ex, "Jurisprudência: falha ao importar {Fonte}", chaveFonte);
                await RegistrarErroAsync(chaveFonte, ex.Message, ct);
            }
        }
    }

    /// <summary>Insere os novos e atualiza os existentes (mesma fonte + id externo), em lotes.</summary>
    private async Task GravarAsync(List<Precedente> itens, string fonte, CancellationToken ct)
    {
        foreach (var lote in itens.DistinctBy(p => p.ExternoId).Chunk(Lote))
        {
            var ids = lote.Select(p => p.ExternoId).ToList();
            var existentes = await context.Precedentes
                .Where(p => p.Fonte == fonte && ids.Contains(p.ExternoId))
                .ToDictionaryAsync(p => p.ExternoId, ct);

            foreach (var novo in lote)
            {
                if (existentes.TryGetValue(novo.ExternoId, out var atual))
                {
                    atual.Tipo = novo.Tipo;
                    atual.Classe = novo.Classe;
                    atual.ClasseBase = novo.ClasseBase;
                    atual.Numero = novo.Numero;
                    atual.Relator = novo.Relator;
                    atual.OrgaoJulgador = novo.OrgaoJulgador;
                    atual.DataJulgamento = novo.DataJulgamento;
                    atual.DataPublicacao = novo.DataPublicacao;
                    atual.Texto = novo.Texto;
                    atual.Situacao = novo.Situacao;
                    atual.Cancelado = novo.Cancelado;
                    atual.UrlFonte = novo.UrlFonte;
                    atual.ImportadoEm = novo.ImportadoEm;
                }
                else context.Precedentes.Add(novo);
            }
            await context.SaveChangesAsync(ct);
            context.ChangeTracker.Clear();
        }
    }

    private async Task<FontePrecedentes> FonteAsync(string chave, CancellationToken ct)
    {
        var fonte = await context.FontesPrecedentes.FirstOrDefaultAsync(f => f.Chave == chave, ct);
        if (fonte is not null) return fonte;
        fonte = new FontePrecedentes { Chave = chave };
        context.FontesPrecedentes.Add(fonte);
        await context.SaveChangesAsync(ct);
        return fonte;
    }

    private async Task RegistrarErroAsync(string chave, string erro, CancellationToken ct)
    {
        context.ChangeTracker.Clear();
        var fonte = await FonteAsync(chave, ct);
        fonte.UltimoErro = erro.Length > 2000 ? erro[..2000] : erro;
        await context.SaveChangesAsync(ct);
    }
}
