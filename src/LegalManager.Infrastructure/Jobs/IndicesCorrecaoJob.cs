using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using LegalManager.Domain.Entities;
using LegalManager.Domain.Enums;
using LegalManager.Infrastructure.Observability;
using LegalManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace LegalManager.Infrastructure.Jobs;

public class IndicesCorrecaoJob
{
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
        await AtualizarBcbAsync(433, TipoIndice.IPCA);
        await AtualizarBcbAsync(189, TipoIndice.IGPM);
        await AtualizarBcbAsync(10764, TipoIndice.TJSP); // IPCA-E — base da Tabela Prática TJSP desde jan/2021
        _logger.LogInformation("[IndicesCorrecaoJob] Concluído.");
    }

    private async Task AtualizarBcbAsync(int serie, TipoIndice tipo)
    {
        try
        {
            var client = _httpClientFactory.CreateClient("BCB");
            var url = $"https://api.bcb.gov.br/dados/serie/bcdata.sgs.{serie}/dados/ultimos/3?formato=json";
            var json = await client.GetStringAsync(url);

            var items = JsonSerializer.Deserialize<List<BcbItem>>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (items == null || items.Count == 0) return;

            // BCB retorna [{"data":"01/04/2026","valor":"0.43"}]
            // "valor" é a taxa mensal em %, ex: 0.43 = 0,43%
            var latest = items.Last();
            if (!DateTime.TryParseExact(latest.Data, "dd/MM/yyyy",
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt)) return;
            if (!decimal.TryParse(latest.Valor,
                    NumberStyles.Any, CultureInfo.InvariantCulture, out var pct)) return;

            var taxa = pct / 100m;
            await UpsertAsync(tipo, dt.Year, dt.Month, taxa, "BCB");
            _logger.LogInformation("[IndicesCorrecaoJob] {Tipo} {Ano}/{Mes}: {Taxa:P4}", tipo, dt.Year, dt.Month, taxa);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[IndicesCorrecaoJob] Erro ao atualizar {Tipo} via BCB", tipo);
        }
    }

    private async Task UpsertAsync(TipoIndice tipo, int ano, int mes, decimal valor, string fonte)
    {
        var existing = await _context.IndicesCorrecaoMonetaria
            .FirstOrDefaultAsync(i => i.Tipo == tipo && i.Ano == ano && i.Mes == mes);

        if (existing != null)
        {
            existing.Valor = valor;
            existing.AtualizadoEm = DateTime.UtcNow;
        }
        else
        {
            _context.IndicesCorrecaoMonetaria.Add(new IndiceCorrecaoMonetaria
            {
                Id = Guid.NewGuid(),
                Tipo = tipo,
                Ano = ano,
                Mes = mes,
                Valor = valor,
                Fonte = fonte,
                AtualizadoEm = DateTime.UtcNow
            });
        }

        await _context.SaveChangesAsync();
    }

    private record BcbItem(string Data, string Valor);
}
