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
    /// <summary>Primeiro ano guardado: cobre execuções de alimentos e correções antigas.</summary>
    public const int AnoInicioHistorico = 2000;

    public const int SerieSalarioMinimo = 1619;

    public static readonly IReadOnlyList<(int Serie, TipoIndice Tipo)> Series =
    [
        (433, TipoIndice.IPCA),
        (189, TipoIndice.IGPM),
        (10764, TipoIndice.TJSP), // IPCA-E — base da Tabela Prática TJSP desde jan/2021
        (188, TipoIndice.INPC),
        (4390, TipoIndice.SELIC), // Selic acumulada no mês (% a.m.) — taxa legal da Lei 14.905/2024
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
        foreach (var (serie, tipo) in Series)
            await AtualizarIndiceAsync(serie, tipo);
        await AtualizarSalarioMinimoAsync();
        _logger.LogInformation("[IndicesCorrecaoJob] Concluído.");
    }

    /// <summary>Verdadeiro quando falta o histórico de algum índice ou do salário mínimo (carga inicial ao subir a API).</summary>
    public static async Task<bool> PrecisaCargaHistoricaAsync(AppDbContext db, CancellationToken ct = default)
    {
        var tiposComHistorico = await db.IndicesCorrecaoMonetaria
            .Where(i => i.Ano == AnoInicioHistorico)
            .Select(i => i.Tipo).Distinct().CountAsync(ct);
        if (tiposComHistorico < Series.Count) return true;
        return !await db.SalariosMinimos.AnyAsync(s => s.Ano == AnoInicioHistorico, ct);
    }

    private async Task AtualizarIndiceAsync(int serie, TipoIndice tipo)
    {
        try
        {
            var existentes = await _context.IndicesCorrecaoMonetaria
                .Where(i => i.Tipo == tipo)
                .ToDictionaryAsync(i => (i.Ano, i.Mes));
            var historicoCompleto = existentes.Keys.Any(k => k.Ano == AnoInicioHistorico);

            var itens = await BuscarSerieAsync(serie, historicoCompleto);
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

            var itens = await BuscarSerieAsync(SerieSalarioMinimo, historicoCompleto);
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
    private async Task<List<(int Ano, int Mes, decimal Valor)>> BuscarSerieAsync(int serie, bool soRecentes)
    {
        var client = _httpClientFactory.CreateClient("BCB");
        var baseUrl = $"https://api.bcb.gov.br/dados/serie/bcdata.sgs.{serie}/dados";
        var resultado = new List<(int Ano, int Mes, decimal Valor)>();
        if (soRecentes)
            return Ordenar(Parse(await client.GetStringAsync($"{baseUrl}/ultimos/12?formato=json")));

        var hoje = DateTime.UtcNow.Date;
        for (var inicio = AnoInicioHistorico; inicio <= hoje.Year; inicio += 10)
        {
            var fim = Math.Min(inicio + 9, hoje.Year);
            var dataFinal = fim == hoje.Year ? hoje : new DateTime(fim, 12, 31);
            var url = $"{baseUrl}?formato=json&dataInicial=01/01/{inicio}&dataFinal={dataFinal.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)}";
            resultado.AddRange(Parse(await client.GetStringAsync(url)));
        }
        return Ordenar(resultado);
    }

    // A série da Selic (4390) vem fora de ordem e inclui o mês corrente parcial — que é
    // regravado nas execuções seguintes e nunca entra no cálculo (o mês da data-base fica de fora).
    private static List<(int Ano, int Mes, decimal Valor)> Ordenar(List<(int Ano, int Mes, decimal Valor)> itens) =>
        itens.GroupBy(i => (i.Ano, i.Mes)).Select(g => g.Last())
            .OrderBy(i => i.Ano).ThenBy(i => i.Mes)
            .ToList();

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
