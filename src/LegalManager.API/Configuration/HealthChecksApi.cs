using System.Text.Json;
using LegalManager.Infrastructure.Persistence;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Serilog.Events;

namespace LegalManager.API.Configuration;

/// <summary>
/// Endpoints de saúde da API, públicos e sem cache:
///
/// - <c>GET /health/live</c>: o processo está de pé. Não toca em dependências, então um banco fora
///   do ar não faz o orquestrador reiniciar o container à toa.
/// - <c>GET /health</c>: pronta para atender. Verifica o banco (<see cref="BancoDeDadosHealthCheck"/>);
///   responde 200 (Healthy) ou 503 (Unhealthy), com um JSON por verificação.
///
/// A resposta não traz mensagens de exceção (o endpoint é público); a falha fica no log da API.
/// </summary>
public static class HealthChecksApi
{
    public const string RotaPronta = "/health";
    public const string RotaViva = "/health/live";
    public const string NomeBanco = "banco";

    private const string TagPronta = "pronta";

    /// <summary>Tempo máximo do teste de conexão; sem ele, um banco travado segura o probe até o timeout do Npgsql.</summary>
    public static readonly TimeSpan TimeoutBanco = TimeSpan.FromSeconds(5);

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static IServiceCollection AdicionarHealthChecks(this IServiceCollection services)
    {
        services.AddHealthChecks()
            .AddCheck<BancoDeDadosHealthCheck>(NomeBanco, tags: [TagPronta], timeout: TimeoutBanco);
        return services;
    }

    public static IEndpointRouteBuilder MapearHealthChecks(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapHealthChecks(RotaViva, new HealthCheckOptions { Predicate = _ => false });
        endpoints.MapHealthChecks(RotaPronta, new HealthCheckOptions
        {
            Predicate = c => c.Tags.Contains(TagPronta),
            ResponseWriter = EscreverJsonAsync
        });
        return endpoints;
    }

    /// <summary>
    /// Nível do log de requisição do Serilog: os probes de saúde (a cada poucos segundos) vão para
    /// Verbose quando passam, para não encher o log; falhas e o resto seguem o padrão.
    /// </summary>
    public static LogEventLevel NivelDoLog(HttpContext ctx, double _, Exception? ex)
    {
        if (ex is not null || ctx.Response.StatusCode >= 500) return LogEventLevel.Error;
        return ctx.Request.Path.StartsWithSegments(RotaPronta) ? LogEventLevel.Verbose : LogEventLevel.Information;
    }

    private static Task EscreverJsonAsync(HttpContext ctx, HealthReport relatorio)
    {
        ctx.Response.ContentType = "application/json; charset=utf-8";
        var corpo = new
        {
            status = relatorio.Status.ToString(),
            duracaoMs = Math.Round(relatorio.TotalDuration.TotalMilliseconds, 1),
            verificacoes = relatorio.Entries.Select(e => new
            {
                nome = e.Key,
                status = e.Value.Status.ToString(),
                duracaoMs = Math.Round(e.Value.Duration.TotalMilliseconds, 1),
                descricao = e.Value.Description
            })
        };
        return ctx.Response.WriteAsync(JsonSerializer.Serialize(corpo, Json));
    }
}

/// <summary>O banco aceita conexão (<c>CanConnectAsync</c>, sem consultar tabelas).</summary>
public sealed class BancoDeDadosHealthCheck(AppDbContext db, ILogger<BancoDeDadosHealthCheck> logger) : IHealthCheck
{
    public const string DescricaoOk = "Banco de dados acessível.";
    public const string DescricaoFalha = "Banco de dados inacessível.";

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            if (await db.Database.CanConnectAsync(cancellationToken))
                return HealthCheckResult.Healthy(DescricaoOk);

            logger.LogWarning("Health check: o banco de dados não aceitou a conexão");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Health check: erro ao conectar no banco de dados");
        }
        return new HealthCheckResult(context.Registration.FailureStatus, DescricaoFalha);
    }
}
