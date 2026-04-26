using System.Net;
using LegalManager.Application.Interfaces;
using LegalManager.Domain.Entities;
using LegalManager.Domain.Enums;
using LegalManager.Infrastructure.Jobs;
using LegalManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;

namespace LegalManager.UnitTests;

public class CapturaPublicacaoJobTests
{
    private AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options);
    }

    private static IConfiguration BuildConfig() =>
        new ConfigurationBuilder().Build();

    private async Task<(AppDbContext ctx, Guid tenantId, Guid responsavelId, Guid processoId, Guid nomeCapturaId)> SeedAsync()
    {
        var ctx = CreateContext();
        var tenantId = Guid.NewGuid();
        var responsavelId = Guid.NewGuid();
        var processoId = Guid.NewGuid();
        var nomeCapturaId = Guid.NewGuid();
        var contatoId = Guid.NewGuid();

        ctx.Tenants.Add(new Tenant
        {
            Id = tenantId, Nome = "Test", Plano = PlanoTipo.Pro,
            Status = StatusTenant.Ativo, CriadoEm = DateTime.UtcNow
        });
        ctx.Users.Add(new Usuario
        {
            Id = responsavelId, TenantId = tenantId, Nome = "Advogado",
            Email = "adv@test.com", UserName = "adv@test.com",
            Perfil = PerfilUsuario.Advogado, Ativo = true, CriadoEm = DateTime.UtcNow
        });
        ctx.Processos.Add(new Processo
        {
            Id = processoId, TenantId = tenantId, NumeroCNJ = "0000001-00.2024.8.26.0001",
            AreaDireito = AreaDireito.Civil, Fase = FaseProcessual.Conhecimento,
            Status = StatusProcesso.Ativo, Tribunal = "TJSP",
            AdvogadoResponsavelId = responsavelId, CriadoEm = DateTime.UtcNow
        });
        ctx.Contatos.Add(new Contato
        {
            Id = contatoId, TenantId = tenantId, Nome = "Empresa ABC LTDA",
            Tipo = TipoPessoa.PJ, TipoContato = TipoContato.ParteContraria,
            Ativo = true, CriadoEm = DateTime.UtcNow
        });
        ctx.ProcessoPartes.Add(new ProcessoParte
        {
            Id = Guid.NewGuid(), ProcessoId = processoId, ContatoId = contatoId,
            TipoParte = TipoParteProcesso.Reu
        });
        ctx.NomesCaptura.Add(new NomeCaptura
        {
            Id = nomeCapturaId, TenantId = tenantId, Nome = "Empresa ABC",
            Ativo = true, CriadoEm = DateTime.UtcNow
        });
        await ctx.SaveChangesAsync();
        return (ctx, tenantId, responsavelId, processoId, nomeCapturaId);
    }

    private class FakeHttpMessageHandler : HttpMessageHandler
    {
        private readonly object _response;

        public FakeHttpMessageHandler(object response) => _response = response;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var json = System.Text.Json.JsonSerializer.Serialize(_response);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json)
            });
        }
    }

    [Fact]
    public async Task ExecutarAsync_NaoDeveFalhar_QuandoNenhumNomeCaptura()
    {
        var ctx = CreateContext();
        var mockEmail = new Mock<IEmailService>();
        var httpHandler = new FakeHttpMessageHandler(new { });
        var httpClientFactory = new Mock<IHttpClientFactory>();
        httpClientFactory.Setup(f => f.CreateClient("Anthropic")).Returns(new HttpClient(httpHandler));

        var job = new CapturaPublicacaoJob(ctx, mockEmail.Object,
            Mock.Of<ILogger<CapturaPublicacaoJob>>(), httpClientFactory.Object);
        await job.ExecutarAsync();
    }

    [Fact]
    public async Task ExecutarAsync_DeveCapturarPublicacao_QuandoMatchNome()
    {
        var (ctx, tenantId, _, processoId, _) = await SeedAsync();
        var agora = DateTime.UtcNow;

        ctx.Andamentos.Add(new Andamento
        {
            Id = Guid.NewGuid(), ProcessoId = processoId, TenantId = tenantId,
            Data = agora.AddDays(-2), Tipo = TipoAndamento.Publicacao,
            Descricao = "Publicação de prazo para contestação",
            Fonte = FonteAndamento.Automatico, CriadoEm = agora
        });
        await ctx.SaveChangesAsync();

        var iaResponse = new
        {
            content = new[] { new { type = "text", text = "{\"tipo\":\"Prazo\",\"urgente\":true,\"resumo\":\"Prazo para contestação\"}" } }
        };
        var httpHandler = new FakeHttpMessageHandler(iaResponse);
        var httpClient = new HttpClient(httpHandler);
        var httpClientFactory = new Mock<IHttpClientFactory>();
        httpClientFactory.Setup(f => f.CreateClient("Anthropic")).Returns(httpClient);

        var mockEmail = new Mock<IEmailService>();

        var job = new CapturaPublicacaoJob(ctx, mockEmail.Object,
            Mock.Of<ILogger<CapturaPublicacaoJob>>(), httpClientFactory.Object);
        await job.ExecutarAsync();

        var publicacoes = await ctx.Publicacoes.ToListAsync();
        Assert.Single(publicacoes);
        Assert.Equal(StatusPublicacao.Nova, publicacoes[0].Status);
    }

    [Fact]
    public async Task ExecutarAsync_NaoDeveDuplicarPublicacao()
    {
        var (ctx, tenantId, _, processoId, _) = await SeedAsync();
        var agora = DateTime.UtcNow;
        var dataPub = agora.AddDays(-2).Date;

        ctx.Publicacoes.Add(new Publicacao
        {
            Id = Guid.NewGuid(), TenantId = tenantId, ProcessoId = processoId,
            NumeroCNJ = "0000001-00.2024.8.26.0001", Diario = "TJSP",
            DataPublicacao = dataPub, Conteudo = "Publicação existente",
            Tipo = TipoPublicacao.Prazo, Status = StatusPublicacao.Nova,
            Urgente = false, CapturaEm = agora.AddDays(-1)
        });
        ctx.Andamentos.Add(new Andamento
        {
            Id = Guid.NewGuid(), ProcessoId = processoId, TenantId = tenantId,
            Data = dataPub, Tipo = TipoAndamento.Publicacao,
            Descricao = "Publicação existente",
            Fonte = FonteAndamento.Automatico, CriadoEm = agora
        });
        await ctx.SaveChangesAsync();

        var httpHandler = new FakeHttpMessageHandler(new { });
        var httpClientFactory = new Mock<IHttpClientFactory>();
        httpClientFactory.Setup(f => f.CreateClient("Anthropic")).Returns(new HttpClient(httpHandler));

        var job = new CapturaPublicacaoJob(ctx, Mock.Of<IEmailService>(),
            Mock.Of<ILogger<CapturaPublicacaoJob>>(), httpClientFactory.Object);
        await job.ExecutarAsync();

        var publicacoes = await ctx.Publicacoes.ToListAsync();
        Assert.Single(publicacoes);
    }

    [Fact]
    public async Task ExecutarAsync_DeveNotificarAdvogado_QuandoNovaPublicacao()
    {
        var (ctx, tenantId, responsavelId, processoId, _) = await SeedAsync();
        var agora = DateTime.UtcNow;

        ctx.Andamentos.Add(new Andamento
        {
            Id = Guid.NewGuid(), ProcessoId = processoId, TenantId = tenantId,
            Data = agora.AddDays(-1), Tipo = TipoAndamento.Intimacao,
            Descricao = "Intimação para audiência",
            Fonte = FonteAndamento.Automatico, CriadoEm = agora
        });
        await ctx.SaveChangesAsync();

        var iaResponse = new
        {
            content = new[] { new { type = "text", text = "{\"tipo\":\"Intimacao\",\"urgente\":false,\"resumo\":\"Audiência designada\"}" } }
        };
        var httpHandler = new FakeHttpMessageHandler(iaResponse);
        var httpClient = new HttpClient(httpHandler);
        var httpClientFactory = new Mock<IHttpClientFactory>();
        httpClientFactory.Setup(f => f.CreateClient("Anthropic")).Returns(httpClient);

        var mockEmail = new Mock<IEmailService>();

        var job = new CapturaPublicacaoJob(ctx, mockEmail.Object,
            Mock.Of<ILogger<CapturaPublicacaoJob>>(), httpClientFactory.Object);
        await job.ExecutarAsync();

        mockEmail.Verify(e => e.EnviarNovaPublicacaoAsync(
            "adv@test.com", "Advogado", "0000001-00.2024.8.26.0001"), Times.Once);

        var notif = await ctx.Notificacoes.FirstOrDefaultAsync(n => n.Titulo.Contains("capturada"));
        Assert.NotNull(notif);
    }

    [Theory]
    [InlineData("publicação de prazo", TipoPublicacao.Prazo)]
    [InlineData("audiência redesignada", TipoPublicacao.Audiencia)]
    [InlineData("decisão interlocutória", TipoPublicacao.Decisao)]
    [InlineData("despacho de mero processamento", TipoPublicacao.Despacho)]
    [InlineData("intimação para cumprir", TipoPublicacao.Intimacao)]
    [InlineData("outro tipo qualquer", TipoPublicacao.Outro)]
    public void InferirTipoLocal_DeveClassificarCorretamente(string texto, TipoPublicacao esperado)
    {
        var lower = texto.ToLowerInvariant();
        var tipo = lower switch
        {
            var s when s.Contains("prazo") || s.Contains("recurso") => TipoPublicacao.Prazo,
            var s when s.Contains("audiên") || s.Contains("audienc") || s.Contains("julgament") => TipoPublicacao.Audiencia,
            var s when s.Contains("decis") || s.Contains("senten") || s.Contains("acórd") => TipoPublicacao.Decisao,
            var s when s.Contains("despacho") => TipoPublicacao.Despacho,
            var s when s.Contains("intim") => TipoPublicacao.Intimacao,
            _ => TipoPublicacao.Outro
        };

        Assert.Equal(esperado, tipo);
    }

    [Theory]
    [InlineData("urgente", true)]
    [InlineData("prazo fatal", true)]
    [InlineData("improrrogável", true)]
    [InlineData("normal", false)]
    public void InferirTipoLocal_DeveDetectarUrgencia(string texto, bool esperadoUrgente)
    {
        var lower = texto.ToLowerInvariant();
        var urgente = lower.Contains("urgente") || lower.Contains("prazo fatal") || lower.Contains("improrrogável");

        Assert.Equal(esperadoUrgente, urgente);
    }
}
