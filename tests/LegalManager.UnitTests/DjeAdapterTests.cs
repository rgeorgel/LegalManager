using System.Net;
using System.Text;
using LegalManager.Application.Interfaces;
using LegalManager.Domain.Enums;
using LegalManager.Infrastructure.Jobs;
using LegalManager.Infrastructure.Persistence;
using LegalManager.Infrastructure.Tribunais.Dje;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;

namespace LegalManager.UnitTests;

public class DjeAdapterTests
{
    private static DbContextOptions<AppDbContext> CreateOptions()
    {
        return new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
    }

    private static AppDbContext CreateContext()
    {
        return new AppDbContext(CreateOptions());
    }

    private class FakeHttpMessageHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _statusCode;
        private readonly string _responseBody;

        public Uri? LastRequestUri { get; private set; }

        public FakeHttpMessageHandler(string responseBody, HttpStatusCode statusCode = HttpStatusCode.OK)
        {
            _responseBody = responseBody;
            _statusCode = statusCode;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            LastRequestUri = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(_statusCode)
            {
                Content = new StringContent(_responseBody, Encoding.UTF8, "text/html")
            });
        }
    }

    private static (Guid tenantId, Guid nomeCapturaId, Guid processoId) SeedData(AppDbContext ctx)
    {
        var tenantId = Guid.NewGuid();
        var nomeCapturaId = Guid.NewGuid();
        var processoId = Guid.NewGuid();

        ctx.Tenants.Add(new LegalManager.Domain.Entities.Tenant
        {
            Id = tenantId, Nome = "Teste DJE", Plano = PlanoTipo.Pro,
            Status = StatusTenant.Ativo, CriadoEm = DateTime.UtcNow
        });
        ctx.NomesCaptura.Add(new LegalManager.Domain.Entities.NomeCaptura
        {
            Id = nomeCapturaId, TenantId = tenantId, Nome = "João Silva",
            Ativo = true, CriadoEm = DateTime.UtcNow
        });
        ctx.Processos.Add(new LegalManager.Domain.Entities.Processo
        {
            Id = processoId, TenantId = tenantId, NumeroCNJ = "0000001-00.2024.8.26.0001",
            AreaDireito = AreaDireito.Civil, Fase = FaseProcessual.Conhecimento,
            Status = StatusProcesso.Ativo, Tribunal = "TJSP",
            AdvogadoResponsavelId = Guid.NewGuid(), CriadoEm = DateTime.UtcNow
        });
        ctx.SaveChanges();
        return (tenantId, nomeCapturaId, processoId);
    }

    [Fact]
    public void TjspDjeAdapter_Nome_DeveRetornarTJSP()
    {
        var handler = new FakeHttpMessageHandler("[]");
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://esaj.tjsp.jus.br") };
        var logger = Mock.Of<ILogger<TjspDjeAdapter>>();
        var adapter = new TjspDjeAdapter(http, logger);

        Assert.Equal("TJSP - Diário da Justiça Eletrônico", adapter.Nome);
        Assert.Equal("TJSP", adapter.Sigla);
        Assert.Equal("https://esaj.tjsp.jus.br", adapter.BaseUrl);
    }

    [Theory]
    [InlineData(TipoDje.Djus, true)]
    [InlineData(TipoDje.Djen, false)]
    [InlineData(TipoDje.Dou, false)]
    public void TjspDjeAdapter_SuportaTipo_DeveRetornarCorretamente(TipoDje tipo, bool esperado)
    {
        var handler = new FakeHttpMessageHandler("[]");
        var adapter = new TjspDjeAdapter(new HttpClient(handler), Mock.Of<ILogger<TjspDjeAdapter>>());

        Assert.Equal(esperado, adapter.SuportaTipo(tipo));
    }

    [Fact]
    public async Task TjspDjeAdapter_ConsultarPorNomeAsync_DeveRetornarVazio_QuandoNenhumaPublicacao()
    {
        var json = @"[]";
        var handler = new FakeHttpMessageHandler(json);
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://esaj.tjsp.jus.br") };
        var adapter = new TjspDjeAdapter(http, Mock.Of<ILogger<TjspDjeAdapter>>());

        var resultado = await adapter.ConsultarPorNomeAsync("NomeInexistente",
            DateTime.UtcNow.AddDays(-7), DateTime.UtcNow);

        Assert.True(resultado.Sucesso);
    }

    [Fact]
    public void TjrjDjeAdapter_Nome_DeveRetornarTJRJ()
    {
        var handler = new FakeHttpMessageHandler("<html></html>");
        var adapter = new TjrjDjeAdapter(new HttpClient(handler), Mock.Of<ILogger<TjrjDjeAdapter>>());

        Assert.Equal("TJRJ - Diário da Justiça Eletrônico", adapter.Nome);
        Assert.Equal("TJRJ", adapter.Sigla);
    }

    [Theory]
    [InlineData(TipoDje.Djus, true)]
    [InlineData(TipoDje.Djen, false)]
    public void TjrjDjeAdapter_SuportaTipo_DeveRetornarCorretamente(TipoDje tipo, bool esperado)
    {
        var handler = new FakeHttpMessageHandler("<html></html>");
        var adapter = new TjrjDjeAdapter(new HttpClient(handler), Mock.Of<ILogger<TjrjDjeAdapter>>());

        Assert.Equal(esperado, adapter.SuportaTipo(tipo));
    }

    [Fact]
    public void TjmgDjeAdapter_Nome_DeveRetornarTJMG()
    {
        var handler = new FakeHttpMessageHandler("<html></html>");
        var adapter = new TjmgDjeAdapter(new HttpClient(handler), Mock.Of<ILogger<TjmgDjeAdapter>>());

        Assert.Equal("TJMG - Diário da Justiça Eletrônico", adapter.Nome);
        Assert.Equal("TJMG", adapter.Sigla);
    }

    [Theory]
    [InlineData(TipoDje.Djus, true)]
    [InlineData(TipoDje.Djen, true)]
    [InlineData(TipoDje.Dou, false)]
    public void TjmgDjeAdapter_SuportaTipo_DeveRetornarCorretamente(TipoDje tipo, bool esperado)
    {
        var handler = new FakeHttpMessageHandler("<html></html>");
        var adapter = new TjmgDjeAdapter(new HttpClient(handler), Mock.Of<ILogger<TjmgDjeAdapter>>());

        Assert.Equal(esperado, adapter.SuportaTipo(tipo));
    }

    [Fact]
    public async Task DjeJob_ExecutarAsync_DeveNaoFalhar_QuandoNenhumNomeConfigurado()
    {
        var options = CreateOptions();
        var ctx = new AppDbContext(options);
        ctx.Tenants.Add(new LegalManager.Domain.Entities.Tenant
        {
            Id = Guid.NewGuid(), Nome = "Test", Plano = PlanoTipo.Pro,
            Status = StatusTenant.Ativo, CriadoEm = DateTime.UtcNow
        });
        await ctx.SaveChangesAsync();

        var logger = Mock.Of<ILogger<DjeJob>>();
        var mockAdapter = new Mock<IDjeAdapter>();
        mockAdapter.Setup(d => d.Sigla).Returns("TJSP");
        var job = new DjeJob(ctx, new[] { mockAdapter.Object }, logger);

        await job.ExecutarAsync(CancellationToken.None);

        var count = await ctx.Publicacoes.CountAsync();
        Assert.Equal(0, count);
    }

    [Fact]
    public async Task DjeJob_ExecutarAsync_DeveNaoFalhar_QuandoAdapterRetornaErro()
    {
        var options = CreateOptions();
        var ctx = new AppDbContext(options);
        var tenantId = Guid.NewGuid();
        var processoId = Guid.NewGuid();
        ctx.Tenants.Add(new LegalManager.Domain.Entities.Tenant
        {
            Id = tenantId, Nome = "Test", Plano = PlanoTipo.Pro,
            Status = StatusTenant.Ativo, CriadoEm = DateTime.UtcNow
        });
        ctx.NomesCaptura.Add(new LegalManager.Domain.Entities.NomeCaptura
        {
            Id = Guid.NewGuid(), TenantId = tenantId, Nome = "João Silva",
            Ativo = true, CriadoEm = DateTime.UtcNow
        });
        ctx.Processos.Add(new LegalManager.Domain.Entities.Processo
        {
            Id = processoId, TenantId = tenantId, NumeroCNJ = "0000001-00.2024.8.26.0001",
            AreaDireito = AreaDireito.Civil, Fase = FaseProcessual.Conhecimento,
            Status = StatusProcesso.Ativo, Tribunal = "TJSP",
            AdvogadoResponsavelId = Guid.NewGuid(), CriadoEm = DateTime.UtcNow
        });
        await ctx.SaveChangesAsync();

        var logger = Mock.Of<ILogger<DjeJob>>();
        var mockAdapter = new Mock<IDjeAdapter>();
        mockAdapter.Setup(d => d.Sigla).Returns("TJSP");
        mockAdapter.Setup(d => d.ConsultarPorNomeAsync(
            It.IsAny<string>(), It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DjeConsultaResult(false, "Erro de rede", []));
        var job = new DjeJob(ctx, new[] { mockAdapter.Object }, logger);

        await job.ExecutarAsync(CancellationToken.None);

        var count = await ctx.Publicacoes.CountAsync();
        Assert.Equal(0, count);
    }

    [Fact]
    public async Task DjeJob_ExecutarAsync_DeveSalvarPublicacao_QuandoAdapterRetornaSucesso()
    {
        var options = CreateOptions();
        var ctx = new AppDbContext(options);
        var tenantId = Guid.NewGuid();
        var processoId = Guid.NewGuid();
        ctx.Tenants.Add(new LegalManager.Domain.Entities.Tenant
        {
            Id = tenantId, Nome = "Test", Plano = PlanoTipo.Pro,
            Status = StatusTenant.Ativo, CriadoEm = DateTime.UtcNow
        });
        ctx.NomesCaptura.Add(new LegalManager.Domain.Entities.NomeCaptura
        {
            Id = Guid.NewGuid(), TenantId = tenantId, Nome = "João Silva",
            Ativo = true, CriadoEm = DateTime.UtcNow
        });
        ctx.Processos.Add(new LegalManager.Domain.Entities.Processo
        {
            Id = processoId, TenantId = tenantId, NumeroCNJ = "0000001-00.2024.8.26.0001",
            AreaDireito = AreaDireito.Civil, Fase = FaseProcessual.Conhecimento,
            Status = StatusProcesso.Ativo, Tribunal = "TJSP",
            AdvogadoResponsavelId = Guid.NewGuid(), CriadoEm = DateTime.UtcNow
        });
        await ctx.SaveChangesAsync();

        var pubs = new List<DjePublicacao>
        {
            new("ext-1", "TJSP", DateTime.UtcNow.Date, null, null, "Intimação",
                "Intimação", "0000001-00.2024.8.26.0001 - Intimação de João Silva no prazo de 15 dias",
                new List<string> { "João Silva" }, "https://dje.tjsp.jus.br", 15, false)
        };

        var logger = Mock.Of<ILogger<DjeJob>>();
        var mockAdapter = new Mock<IDjeAdapter>();
        mockAdapter.Setup(d => d.Sigla).Returns("TJSP");
        mockAdapter.Setup(d => d.ConsultarPorNomeAsync(
            It.IsAny<string>(), It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DjeConsultaResult(true, null, pubs));
        var job = new DjeJob(ctx, new[] { mockAdapter.Object }, logger);

        await job.ExecutarAsync(CancellationToken.None);

        var count = await ctx.Publicacoes.CountAsync();
        Assert.Equal(1, count);

        var pub = await ctx.Publicacoes.FirstAsync();
        Assert.Equal("TJSP", pub.Diario);
        Assert.Equal(TipoPublicacao.Intimacao, pub.Tipo);
        Assert.False(pub.Urgente);
        Assert.NotNull(pub.HashDje);
        Assert.Equal("ext-1", pub.IdExterno);
    }

    [Fact]
    public async Task DjeJob_ExecutarAsync_NaoDeveDuplicar_QuandoHashJaExiste()
    {
        var options = CreateOptions();
        var ctx = new AppDbContext(options);
        var tenantId = Guid.NewGuid();
        var processoId = Guid.NewGuid();
        ctx.Tenants.Add(new LegalManager.Domain.Entities.Tenant
        {
            Id = tenantId, Nome = "Test", Plano = PlanoTipo.Pro,
            Status = StatusTenant.Ativo, CriadoEm = DateTime.UtcNow
        });
        ctx.NomesCaptura.Add(new LegalManager.Domain.Entities.NomeCaptura
        {
            Id = Guid.NewGuid(), TenantId = tenantId, Nome = "João Silva",
            Ativo = true, CriadoEm = DateTime.UtcNow
        });
        ctx.Processos.Add(new LegalManager.Domain.Entities.Processo
        {
            Id = processoId, TenantId = tenantId, NumeroCNJ = "0000001-00.2024.8.26.0001",
            AreaDireito = AreaDireito.Civil, Fase = FaseProcessual.Conhecimento,
            Status = StatusProcesso.Ativo, Tribunal = "TJSP",
            AdvogadoResponsavelId = Guid.NewGuid(), CriadoEm = DateTime.UtcNow
        });

        var pubs = new List<DjePublicacao>
        {
            new("ext-1", "TJSP", DateTime.UtcNow.Date, null, null, "Intimação",
                "Intimação", "0000001-00.2024.8.26.0001 - Intimação de João Silva no prazo de 15 dias",
                new List<string> { "João Silva" }, "https://dje.tjsp.jus.br", 15, false)
        };

        var expectedHash = DjeJob.GerarHash(pubs[0]);

        ctx.Publicacoes.Add(new LegalManager.Domain.Entities.Publicacao
        {
            Id = Guid.NewGuid(), TenantId = tenantId, ProcessoId = processoId,
            NumeroCNJ = "0000001-00.2024.8.26.0001", Diario = "TJSP",
            DataPublicacao = DateTime.UtcNow.Date, Conteudo = "Teste",
            Tipo = TipoPublicacao.Intimacao, Status = StatusPublicacao.Nova,
            HashDje = expectedHash,
            CapturaEm = DateTime.UtcNow
        });
        await ctx.SaveChangesAsync();

        var logger = Mock.Of<ILogger<DjeJob>>();
        var mockAdapter = new Mock<IDjeAdapter>();
        mockAdapter.Setup(d => d.Sigla).Returns("TJSP");
        mockAdapter.Setup(d => d.ConsultarPorNomeAsync(
            It.IsAny<string>(), It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DjeConsultaResult(true, null, pubs));
        var job = new DjeJob(ctx, new[] { mockAdapter.Object }, logger);

        await job.ExecutarAsync(CancellationToken.None);

        var count = await ctx.Publicacoes.CountAsync();
        Assert.Equal(1, count);
    }

    [Fact]
    public void DjePublicacao_WithConteudo_DeveCriarNovaInstancia()
    {
        var original = new DjePublicacao(
            "1", "TJSP", DateTime.UtcNow, null, null, "Intimação",
            "Intimação", "Texto original",
            new List<string> { "João" }, "http://url", 5, false);

        var atualizada = original.WithConteudo("Texto novo com mais conteúdo");

        Assert.Equal("Texto original", original.Conteudo);
        Assert.Equal("Texto novo com mais conteúdo", atualizada.Conteudo);
        Assert.Equal(original.Id, atualizada.Id);
        Assert.Equal(original.SiglaTribunal, atualizada.SiglaTribunal);
    }
}
