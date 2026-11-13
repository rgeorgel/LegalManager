using System.Net;
using System.Text.Json;
using LegalManager.API.Configuration;
using LegalManager.Infrastructure.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging.Abstractions;
using Serilog.Events;

namespace LegalManager.IntegrationTests;

public class HealthChecksTests
{
    // Porta 1 em localhost: a conexão é recusada na hora, sem esperar timeout.
    private const string PostgresInacessivel =
        "Host=127.0.0.1;Port=1;Database=x;Username=x;Password=x;Timeout=2";

    private static async Task<WebApplication> CriarAppAsync(Action<DbContextOptionsBuilder> configurarDb)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddDbContext<AppDbContext>(configurarDb);
        builder.Services.AdicionarHealthChecks();
        var app = builder.Build();
        app.MapearHealthChecks();
        await app.StartAsync();
        return app;
    }

    private static Task<WebApplication> CriarAppComBancoOkAsync() =>
        CriarAppAsync(o => o.UseInMemoryDatabase(Guid.NewGuid().ToString()));

    private static Task<WebApplication> CriarAppComBancoForaAsync() =>
        CriarAppAsync(o => o.UseNpgsql(PostgresInacessivel));

    private static async Task<(HttpStatusCode Status, JsonElement Corpo, HttpResponseMessage Resposta)> GetJsonAsync(
        WebApplication app, string rota)
    {
        var resposta = await app.GetTestClient().GetAsync(rota);
        var texto = await resposta.Content.ReadAsStringAsync();
        return (resposta.StatusCode, JsonDocument.Parse(texto).RootElement.Clone(), resposta);
    }

    // ─── GET /health ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Health_BancoAcessivel_Retorna200ComVerificacaoDoBanco()
    {
        await using var app = await CriarAppComBancoOkAsync();

        var (status, corpo, resposta) = await GetJsonAsync(app, HealthChecksApi.RotaPronta);

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("application/json", resposta.Content.Headers.ContentType?.MediaType);
        Assert.Equal("Healthy", corpo.GetProperty("status").GetString());
        var banco = Assert.Single(corpo.GetProperty("verificacoes").EnumerateArray());
        Assert.Equal(HealthChecksApi.NomeBanco, banco.GetProperty("nome").GetString());
        Assert.Equal("Healthy", banco.GetProperty("status").GetString());
        Assert.Equal(BancoDeDadosHealthCheck.DescricaoOk, banco.GetProperty("descricao").GetString());
        Assert.True(banco.GetProperty("duracaoMs").GetDouble() >= 0);
    }

    [Fact]
    public async Task Health_BancoInacessivel_Retorna503SemDetalhesDaExcecao()
    {
        await using var app = await CriarAppComBancoForaAsync();

        var resposta = await app.GetTestClient().GetAsync(HealthChecksApi.RotaPronta);
        var texto = await resposta.Content.ReadAsStringAsync();
        var corpo = JsonDocument.Parse(texto).RootElement;

        Assert.Equal(HttpStatusCode.ServiceUnavailable, resposta.StatusCode);
        Assert.Equal("Unhealthy", corpo.GetProperty("status").GetString());
        var banco = Assert.Single(corpo.GetProperty("verificacoes").EnumerateArray());
        Assert.Equal("Unhealthy", banco.GetProperty("status").GetString());
        Assert.Equal(BancoDeDadosHealthCheck.DescricaoFalha, banco.GetProperty("descricao").GetString());
        // Endpoint público: nada de host, porta ou mensagem do Npgsql na resposta.
        Assert.DoesNotContain("127.0.0.1", texto);
        Assert.DoesNotContain("Exception", texto);
    }

    [Fact]
    public async Task Health_RespostaNaoEhCacheada()
    {
        await using var app = await CriarAppComBancoOkAsync();

        var resposta = await app.GetTestClient().GetAsync(HealthChecksApi.RotaPronta);

        Assert.True(resposta.Headers.CacheControl?.NoCache ?? false);
        Assert.True(resposta.Headers.CacheControl?.NoStore ?? false);
    }

    // ─── GET /health/live ─────────────────────────────────────────────────────

    [Fact]
    public async Task Live_BancoInacessivel_Retorna200()
    {
        await using var app = await CriarAppComBancoForaAsync();

        var resposta = await app.GetTestClient().GetAsync(HealthChecksApi.RotaViva);

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        Assert.Equal("Healthy", await resposta.Content.ReadAsStringAsync());
    }

    // ─── BancoDeDadosHealthCheck ──────────────────────────────────────────────

    private static HealthCheckContext Contexto(IHealthCheck check, HealthStatus falha = HealthStatus.Unhealthy) =>
        new() { Registration = new HealthCheckRegistration(HealthChecksApi.NomeBanco, check, falha, null) };

    [Fact]
    public async Task Check_ErroAoConectar_RetornaStatusDeFalhaDoRegistro()
    {
        var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        await db.DisposeAsync(); // CanConnectAsync lança ObjectDisposedException
        var check = new BancoDeDadosHealthCheck(db, NullLogger<BancoDeDadosHealthCheck>.Instance);

        var resultado = await check.CheckHealthAsync(Contexto(check, HealthStatus.Degraded));

        Assert.Equal(HealthStatus.Degraded, resultado.Status);
        Assert.Equal(BancoDeDadosHealthCheck.DescricaoFalha, resultado.Description);
        Assert.Null(resultado.Exception);
    }

    [Fact]
    public async Task Check_Cancelado_PropagaOCancelamento()
    {
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(PostgresInacessivel).Options);
        var check = new BancoDeDadosHealthCheck(db, NullLogger<BancoDeDadosHealthCheck>.Instance);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // O timeout do registro vira cancelamento; quem o trata é o HealthCheckService.
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => check.CheckHealthAsync(Contexto(check), cts.Token));
    }

    // ─── Nível do log de requisição ───────────────────────────────────────────

    private static HttpContext Requisicao(string rota, int status)
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Path = rota;
        ctx.Response.StatusCode = status;
        return ctx;
    }

    [Theory]
    [InlineData("/health", 200, LogEventLevel.Verbose)]
    [InlineData("/health/live", 200, LogEventLevel.Verbose)]
    [InlineData("/health", 503, LogEventLevel.Error)]
    [InlineData("/healthz", 200, LogEventLevel.Information)]
    [InlineData("/api/processos", 200, LogEventLevel.Information)]
    [InlineData("/api/processos", 500, LogEventLevel.Error)]
    public void NivelDoLog_SilenciaSoProbesQuePassam(string rota, int status, LogEventLevel esperado)
    {
        Assert.Equal(esperado, HealthChecksApi.NivelDoLog(Requisicao(rota, status), 1, null));
    }

    [Fact]
    public void NivelDoLog_ComExcecao_EhErro()
    {
        Assert.Equal(LogEventLevel.Error,
            HealthChecksApi.NivelDoLog(Requisicao("/health", 200), 1, new InvalidOperationException()));
    }
}
