using System.Globalization;
using System.Text.Json;
using LegalManager.Domain.Entities;
using LegalManager.Domain.Enums;
using LegalManager.Infrastructure.Observability;
using LegalManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace LegalManager.Infrastructure.Jobs;

/// <summary>
/// Atualiza os índices mensais (SGS/BCB) e o salário mínimo. Na primeira execução — ou quando o
/// histórico desde <see cref="AnoInicioHistorico"/> está incompleto — baixa a série inteira em
/// janelas de 10 anos (limite da API do BCB); depois disso só os últimos 12 meses, para pegar as
/// publicações novas e eventuais revisões.
/// </summary>
public class IndicesCorrecaoJob
{
    /// <summary>Primeiro ano guardado: cobre a revisão do FGTS (desde jan/1999), execuções de alimentos e correções antigas.</summary>
    public const int AnoInicioHistorico = 1999;

    public const int SerieSalarioMinimo = 1619;

    /// <summary>Série do SGS guardada em IndicesCorrecaoMonetaria, a partir de <paramref name="AnoInicio"/>.</summary>
    public sealed record SerieBcb(int Codigo, TipoIndice Tipo, int AnoInicio = AnoInicioHistorico);

    // Taxas médias de juros (pessoa física, recursos livres, % a.m.) existem desde mar/2011.
    private const int InicioTaxasMedias = 2011;

    public static readonly IReadOnlyList<SerieBcb> Series =
    [
        new(433, TipoIndice.IPCA),
        new(189, TipoIndice.IGPM),
        new(10764, TipoIndice.TJSP), // IPCA-E — base da Tabela Prática TJSP desde jan/2021
        new(188, TipoIndice.INPC),
        new(4390, TipoIndice.SELIC), // Selic acumulada no mês (% a.m.) — taxa legal da Lei 14.905/2024
        new(7811, TipoIndice.TR),    // TR do 1º dia do mês (% a.m.) — FGTS e juros trabalhistas pré-judiciais (art. 39 da Lei 8.177)
        new(190, TipoIndice.IGPDI),  // IGP-DI (FGV) — reajuste de aluguel
        // Revisional bancária e RMC/RCC: taxa média mensal por modalidade (critério do STJ, REsp 1.061.530)
        new(25463, TipoIndice.TxCheque, InicioTaxasMedias),
        new(25464, TipoIndice.TxPessoal, InicioTaxasMedias),
        new(25466, TipoIndice.TxPrivado, InicioTaxasMedias),
        new(25467, TipoIndice.TxPublico, InicioTaxasMedias),
        new(25468, TipoIndice.TxINSS, InicioTaxasMedias),
        new(25471, TipoIndice.TxVeiculo, InicioTaxasMedias),
    ];

    private const string Fonte = "BCB";

    private readonly AppDbContext _context;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<IndicesCorrecaoJob> _logger;

    public IndicesCorrecaoJob(AppDbContext context, IHttpClientFactory httpClientFactory, ILogger<IndicesCorrecaoJob> logger)
    {
        _context = context;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task ExecutarAsync()
    {
        using var activity = Telemetry.Hangfire.StartActivity($"{nameof(IndicesCorrecaoJob)}.{nameof(ExecutarAsync)}");
        activity?.SetTag("job.cron", "indices-correcao-mensal");
        _logger.LogInformation("[IndicesCorrecaoJob] Iniciando atualização de índices.");
        foreach (var serie in Series)
            await AtualizarIndiceAsync(serie);
        await AtualizarSalarioMinimoAsync();
        _logger.LogInformation("[IndicesCorrecaoJob] Concluído.");
    }

    /// <summary>Verdadeiro quando falta o histórico de algum índice ou do salário mínimo (carga inicial ao subir a API).</summary>
    public static async Task<bool> PrecisaCargaHistoricaAsync(AppDbContext db, CancellationToken ct = default)
    {
        var inicios = await db.IndicesCorrecaoMonetaria
            .GroupBy(i => i.Tipo)
            .Select(g => new { Tipo = g.Key, Ano = g.Min(i => i.Ano) })
            .ToListAsync(ct);
        if (Series.Any(s => !inicios.Any(i => i.Tipo == s.Tipo && i.Ano <= s.AnoInicio))) return true;
        return !await db.SalariosMinimos.AnyAsync(s => s.Ano == AnoInicioHistorico, ct);
    }

    private async Task AtualizarIndiceAsync(SerieBcb serieBcb)
    {
        var tipo = serieBcb.Tipo;
        try
        {
            var existentes = await _context.IndicesCorrecaoMonetaria
                .Where(i => i.Tipo == tipo)
                .ToDictionaryAsync(i => (i.Ano, i.Mes));
            var historicoCompleto = existentes.Keys.Any(k => k.Ano <= serieBcb.AnoInicio);

            var itens = await BuscarSerieAsync(serieBcb.Codigo, historicoCompleto, serieBcb.AnoInicio);
            if (itens.Count == 0) return;

            var agora = DateTime.UtcNow;
            foreach (var (ano, mes, pct) in itens)
            {
                var taxa = pct / 100m; // BCB: "0.43" = 0,43% no mês
                if (existentes.TryGetValue((ano, mes), out var atual))
                {
                    if (atual.Valor == taxa) continue;
                    atual.Valor = taxa;
                    atual.AtualizadoEm = agora;
                }
                else
                {
                    var novo = new IndiceCorrecaoMonetaria
                    {
                        Id = Guid.NewGuid(), Tipo = tipo, Ano = ano, Mes = mes,
                        Valor = taxa, Fonte = Fonte, AtualizadoEm = agora
                    };
                    _context.IndicesCorrecaoMonetaria.Add(novo);
                    existentes[(ano, mes)] = novo;
                }
            }
            await _context.SaveChangesAsync();

            var (ultAno, ultMes, ultPct) = itens[^1];
            _logger.LogInformation("[IndicesCorrecaoJob] {Tipo}: {Qtd} meses lidos, último {Ano}/{Mes}: {Taxa}%",
                tipo, itens.Count, ultAno, ultMes, ultPct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[IndicesCorrecaoJob] Erro ao atualizar {Tipo} via BCB", tipo);
        }
    }

    private async Task AtualizarSalarioMinimoAsync()
    {
        try
        {
            var existentes = await _context.SalariosMinimos.ToDictionaryAsync(s => (s.Ano, s.Mes));
            var historicoCompleto = existentes.Keys.Any(k => k.Ano == AnoInicioHistorico);

            var itens = await BuscarSerieAsync(SerieSalarioMinimo, historicoCompleto, AnoInicioHistorico);
            if (itens.Count == 0) return;

            var agora = DateTime.UtcNow;
            foreach (var (ano, mes, valor) in itens)
            {
                if (existentes.TryGetValue((ano, mes), out var atual))
                {
                    if (atual.Valor == valor) continue;
                    atual.Valor = valor;
                    atual.AtualizadoEm = agora;
                }
                else
                {
                    var novo = new SalarioMinimo
                    {
                        Id = Guid.NewGuid(), Ano = ano, Mes = mes, Valor = valor, Fonte = Fonte, AtualizadoEm = agora
                    };
                    _context.SalariosMinimos.Add(novo);
                    existentes[(ano, mes)] = novo;
                }
            }
            await _context.SaveChangesAsync();
            _logger.LogInformation("[IndicesCorrecaoJob] Salário mínimo: {Qtd} meses lidos, vigente R$ {Valor}",
                itens.Count, itens[^1].Valor);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[IndicesCorrecaoJob] Erro ao atualizar o salário mínimo via BCB");
        }
    }

    /// <summary>
    /// Histórico completo desde <see cref="AnoInicioHistorico"/> (janelas de 10 anos) ou só os últimos 12 meses.
    /// Itens em ordem cronológica.
    /// </summary>
    private async Task<List<(int Ano, int Mes, decimal Valor)>> BuscarSerieAsync(int serie, bool soRecentes, int anoInicio)
    {
        var client = _httpClientFactory.CreateClient("BCB");
        var baseUrl = $"https://api.bcb.gov.br/dados/serie/bcdata.sgs.{serie}/dados";
        var resultado = new List<(int Ano, int Mes, decimal Valor)>();
        if (soRecentes)
            return Ordenar(Parse(await GetOuVazioAsync(client, $"{baseUrl}/ultimos/12?formato=json")));

        var hoje = DateTime.UtcNow.Date;
        for (var inicio = anoInicio; inicio <= hoje.Year; inicio += 10)
        {
            var fim = Math.Min(inicio + 9, hoje.Year);
            var dataFinal = fim == hoje.Year ? hoje : new DateTime(fim, 12, 31);
            var url = $"{baseUrl}?formato=json&dataInicial=01/01/{inicio}&dataFinal={dataFinal.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)}";
            resultado.AddRange(Parse(await GetOuVazioAsync(client, url)));
        }
        return Ordenar(resultado);
    }

    // A série da Selic (4390) vem fora de ordem e inclui o mês corrente parcial — que é
    // regravado nas execuções seguintes e nunca entra no cálculo (o mês da data-base fica de fora).
    private static List<(int Ano, int Mes, decimal Valor)> Ordenar(List<(int Ano, int Mes, decimal Valor)> itens) =>
        itens.GroupBy(i => (i.Ano, i.Mes)).Select(g => g.Last())
            .OrderBy(i => i.Ano).ThenBy(i => i.Mes)
            .ToList();

    // O SGS responde 404 ("Value(s) not found") quando a janela não tem dados (ex.: antes do início da série).
    private static async Task<string> GetOuVazioAsync(HttpClient client, string url)
    {
        try { return await client.GetStringAsync(url); }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound) { return "[]"; }
    }

    /// <summary>Lê o JSON do SGS: [{"data":"01/04/2026","valor":"0.43"}]. Ignora itens malformados.</summary>
    internal static List<(int Ano, int Mes, decimal Valor)> Parse(string json)
    {
        var items = JsonSerializer.Deserialize<List<BcbItem>>(json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];
        var resultado = new List<(int, int, decimal)>();
        foreach (var item in items)
        {
            if (!DateTime.TryParseExact(item.Data, "dd/MM/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt)) continue;
            if (!decimal.TryParse(item.Valor, NumberStyles.Any, CultureInfo.InvariantCulture, out var valor)) continue;
            resultado.Add((dt.Year, dt.Month, valor));
        }
        return resultado;
    }

    private record BcbItem(string Data, string Valor);
}
