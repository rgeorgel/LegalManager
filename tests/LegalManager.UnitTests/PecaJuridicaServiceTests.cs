using LegalManager.Application.DTOs.IA;
using LegalManager.Application.Interfaces;
using LegalManager.Domain.Entities;
using LegalManager.Domain.Enums;
using LegalManager.Domain.Interfaces;
using LegalManager.Infrastructure.Persistence;
using LegalManager.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace LegalManager.UnitTests;

public class PecaJuridicaServiceTests
{
    private AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options);
    }

    private ITenantContext CreateTenantContext(Guid tenantId)
    {
        var mock = new Mock<ITenantContext>();
        mock.Setup(t => t.TenantId).Returns(tenantId);
        return mock.Object;
    }

    private async Task<(AppDbContext ctx, Guid tenantId, Guid usuarioId)> SeedAsync()
    {
        var ctx = CreateContext();
        var tenantId = Guid.NewGuid();
        var tenant = new Tenant
        {
            Id = tenantId, Nome = "Test", Plano = PlanoTipo.Pro,
            Status = StatusTenant.Ativo, CriadoEm = DateTime.UtcNow
        };
        ctx.Tenants.Add(tenant);
        var usuarioId = Guid.NewGuid();
        var usuario = new Usuario
        {
            Id = usuarioId, TenantId = tenantId, Nome = "Advogado",
            Email = "adv@test.com", UserName = "adv@test.com",
            Perfil = PerfilUsuario.Advogado, Ativo = true, CriadoEm = DateTime.UtcNow
        };
        ctx.Users.Add(usuario);
        ctx.CreditosAI.Add(new CreditoAI
        {
            Id = Guid.NewGuid(), TenantId = tenantId,
            Tipo = TipoCreditoAI.GeracaoPeca, QuantidadeTotal = 5, QuantidadeUsada = 0,
            Origem = OrigemCreditoAI.Cortesai, CriadoEm = DateTime.UtcNow
        });
        await ctx.SaveChangesAsync();
        return (ctx, tenantId, usuarioId);
    }

    [Fact]
    public async Task GerarPecaAsync_DeveLancarExcecao_QuandoCreditoEsgotado()
    {
        var (ctx, tenantId, usuarioId) = await SeedAsync();
        var credito = await ctx.CreditosAI.FirstAsync(c => c.TenantId == tenantId);
        credito.QuantidadeUsada = credito.QuantidadeTotal;
        await ctx.SaveChangesAsync();

        var mockIa = new Mock<IIAService>();
        var mockCredito = new Mock<ICreditoService>();
        mockCredito.Setup(c => c.TemCreditoDisponivelAsync(TipoCreditoAI.GeracaoPeca, 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var service = new PecaJuridicaService(ctx, CreateTenantContext(tenantId), mockIa.Object, mockCredito.Object);
        var dto = new GerarPecaDto(null, TipoPecaJuridica.PeticaoInicial, "Ação de cobrança");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.GerarPecaAsync(dto, usuarioId));
    }

    [Fact]
    public async Task GerarPecaAsync_DeveGerarPeca_QuandoCreditoDisponivel()
    {
        var (ctx, tenantId, usuarioId) = await SeedAsync();
        var mockIa = new Mock<IIAService>();
        mockIa.Setup(i => i.GerarPecaJuridicaAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("Petição gerada com sucesso.");
        mockIa.Setup(i => i.BuscarJurisprudenciaAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("Jurisprudência aplicável.");

        var mockCredito = new Mock<ICreditoService>();
        mockCredito.Setup(c => c.TemCreditoDisponivelAsync(TipoCreditoAI.GeracaoPeca, 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        mockCredito.Setup(c => c.ConsumirCreditoAsync(TipoCreditoAI.GeracaoPeca, 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var service = new PecaJuridicaService(ctx, CreateTenantContext(tenantId), mockIa.Object, mockCredito.Object);
        var dto = new GerarPecaDto(null, TipoPecaJuridica.Contestacao, "Ação trabalhista");

        var result = await service.GerarPecaAsync(dto, usuarioId);

        Assert.NotNull(result);
        Assert.Equal(TipoPecaJuridica.Contestacao, result.Tipo);
        Assert.Equal("Petição gerada com sucesso.", result.ConteudoGerado);
        mockCredito.Verify(c => c.ConsumirCreditoAsync(TipoCreditoAI.GeracaoPeca, 1, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ObterPecaAsync_DeveRetornarPeca_QuandoExistir()
    {
        var (ctx, tenantId, usuarioId) = await SeedAsync();
        var pecaId = Guid.NewGuid();
        ctx.PecasGeradas.Add(new PecaGerada
        {
            Id = pecaId, TenantId = tenantId, GeradoPorId = usuarioId,
            Tipo = TipoPecaJuridica.Recurso, DescricaoSolicitacao = "Recurso de apelação",
            ConteudoGerado = "Conteúdo do recurso", CriadoEm = DateTime.UtcNow
        });
        await ctx.SaveChangesAsync();

        var service = new PecaJuridicaService(ctx, CreateTenantContext(tenantId),
            Mock.Of<IIAService>(), Mock.Of<ICreditoService>());

        var result = await service.ObterPecaAsync(pecaId);

        Assert.NotNull(result);
        Assert.Equal(pecaId, result.Id);
    }

    [Fact]
    public async Task ObterPecaAsync_DeveRetornarNull_QuandoNaoExistir()
    {
        var (ctx, tenantId, _) = await SeedAsync();
        var service = new PecaJuridicaService(ctx, CreateTenantContext(tenantId),
            Mock.Of<IIAService>(), Mock.Of<ICreditoService>());

        var result = await service.ObterPecaAsync(Guid.NewGuid());

        Assert.Null(result);
    }

    [Fact]
    public async Task ObterPecaAsync_DeveRetornarNull_QuandoDeOutroTenant()
    {
        var (ctx, tenantId, _) = await SeedAsync();
        var outroTenantId = Guid.NewGuid();
        var pecaId = Guid.NewGuid();
        ctx.PecasGeradas.Add(new PecaGerada
        {
            Id = pecaId, TenantId = outroTenantId, GeradoPorId = Guid.NewGuid(),
            Tipo = TipoPecaJuridica.PeticaoInicial, DescricaoSolicitacao = "Teste",
            ConteudoGerado = "Teste", CriadoEm = DateTime.UtcNow
        });
        await ctx.SaveChangesAsync();

        var service = new PecaJuridicaService(ctx, CreateTenantContext(tenantId),
            Mock.Of<IIAService>(), Mock.Of<ICreditoService>());

        var result = await service.ObterPecaAsync(pecaId);

        Assert.Null(result);
    }

    [Fact]
    public async Task ListarAsync_DeveRetornarPecasDoTenant()
    {
        var (ctx, tenantId, usuarioId) = await SeedAsync();
        ctx.PecasGeradas.AddRange(
            new PecaGerada { Id = Guid.NewGuid(), TenantId = tenantId, GeradoPorId = usuarioId, Tipo = TipoPecaJuridica.PeticaoInicial, DescricaoSolicitacao = "Peca 1", ConteudoGerado = "C1", CriadoEm = DateTime.UtcNow.AddMinutes(-1) },
            new PecaGerada { Id = Guid.NewGuid(), TenantId = tenantId, GeradoPorId = usuarioId, Tipo = TipoPecaJuridica.Contestacao, DescricaoSolicitacao = "Peca 2", ConteudoGerado = "C2", CriadoEm = DateTime.UtcNow }
        );
        await ctx.SaveChangesAsync();

        var service = new PecaJuridicaService(ctx, CreateTenantContext(tenantId),
            Mock.Of<IIAService>(), Mock.Of<ICreditoService>());

        var result = await service.ListarAsync(new ListPecasGeradasDto(1, 10, null, null));

        Assert.Equal(2, result.Count());
    }

    [Fact]
    public async Task ListarAsync_DeveFiltrarPorProcessoId()
    {
        var (ctx, tenantId, usuarioId) = await SeedAsync();
        var processoId = Guid.NewGuid();
        ctx.PecasGeradas.AddRange(
            new PecaGerada { Id = Guid.NewGuid(), TenantId = tenantId, GeradoPorId = usuarioId, ProcessoId = processoId, Tipo = TipoPecaJuridica.PeticaoInicial, DescricaoSolicitacao = "Peca 1", ConteudoGerado = "C1", CriadoEm = DateTime.UtcNow },
            new PecaGerada { Id = Guid.NewGuid(), TenantId = tenantId, GeradoPorId = usuarioId, Tipo = TipoPecaJuridica.PeticaoInicial, DescricaoSolicitacao = "Peca 2", ConteudoGerado = "C2", CriadoEm = DateTime.UtcNow }
        );
        await ctx.SaveChangesAsync();

        var service = new PecaJuridicaService(ctx, CreateTenantContext(tenantId),
            Mock.Of<IIAService>(), Mock.Of<ICreditoService>());

        var result = await service.ListarAsync(new ListPecasGeradasDto(1, 10, processoId, null));

        Assert.Single(result);
    }

    [Fact]
    public async Task ListarAsync_DevePaginarCorretamente()
    {
        var (ctx, tenantId, usuarioId) = await SeedAsync();
        for (int i = 0; i < 15; i++)
        {
            ctx.PecasGeradas.Add(new PecaGerada
            {
                Id = Guid.NewGuid(), TenantId = tenantId, GeradoPorId = usuarioId,
                Tipo = TipoPecaJuridica.PeticaoInicial, DescricaoSolicitacao = $"Peca {i}",
                ConteudoGerado = $"C{i}", CriadoEm = DateTime.UtcNow.AddMinutes(-i)
            });
        }
        await ctx.SaveChangesAsync();

        var service = new PecaJuridicaService(ctx, CreateTenantContext(tenantId),
            Mock.Of<IIAService>(), Mock.Of<ICreditoService>());

        var page1 = await service.ListarAsync(new ListPecasGeradasDto(1, 10, null, null));
        var page2 = await service.ListarAsync(new ListPecasGeradasDto(2, 10, null, null));

        Assert.Equal(10, page1.Count());
        Assert.Equal(5, page2.Count());
    }
}
