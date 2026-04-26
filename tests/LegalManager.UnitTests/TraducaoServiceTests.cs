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

public class TraducaoServiceTests
{
    private AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options);
    }

    private ITenantContext CreateTenantContext(Guid tenantId, Guid userId)
    {
        var mock = new Mock<ITenantContext>();
        mock.Setup(t => t.TenantId).Returns(tenantId);
        mock.Setup(t => t.UserId).Returns(userId);
        return mock.Object;
    }

    private async Task<(AppDbContext ctx, Guid tenantId, Guid usuarioId, Guid processoId, Guid andamentoId)> SeedAsync()
    {
        var ctx = CreateContext();
        var tenantId = Guid.NewGuid();
        var usuarioId = Guid.NewGuid();
        var tenant = new Tenant
        {
            Id = tenantId, Nome = "Test", Plano = PlanoTipo.Pro,
            Status = StatusTenant.Ativo, CriadoEm = DateTime.UtcNow
        };
        ctx.Tenants.Add(tenant);
        ctx.Users.Add(new Usuario
        {
            Id = usuarioId, TenantId = tenantId, Nome = "Advogado",
            Email = "adv@test.com", UserName = "adv@test.com",
            Perfil = PerfilUsuario.Advogado, Ativo = true, CriadoEm = DateTime.UtcNow
        });
        var processoId = Guid.NewGuid();
        ctx.Processos.Add(new Processo
        {
            Id = processoId, TenantId = tenantId, NumeroCNJ = "0000001-00.2024.8.26.0001",
            AreaDireito = AreaDireito.Civil, Fase = FaseProcessual.Conhecimento,
            Status = StatusProcesso.Ativo, CriadoEm = DateTime.UtcNow
        });
        var andamentoId = Guid.NewGuid();
        ctx.Andamentos.Add(new Andamento
        {
            Id = andamentoId, ProcessoId = processoId, TenantId = tenantId,
            Data = DateTime.UtcNow, Tipo = TipoAndamento.Despacho,
            Descricao = "Citação validada", Fonte = FonteAndamento.Manual,
            CriadoEm = DateTime.UtcNow
        });
        ctx.CreditosAI.Add(new CreditoAI
        {
            Id = Guid.NewGuid(), TenantId = tenantId,
            Tipo = TipoCreditoAI.TraducaoAndamento, QuantidadeTotal = 5, QuantidadeUsada = 0,
            Origem = OrigemCreditoAI.Cortesai, CriadoEm = DateTime.UtcNow
        });
        await ctx.SaveChangesAsync();
        return (ctx, tenantId, usuarioId, processoId, andamentoId);
    }

    [Fact]
    public async Task TraduzirAndamentoAsync_DeveLancarExcecao_QuandoCreditoEsgotado()
    {
        var (ctx, tenantId, usuarioId, _, andamentoId) = await SeedAsync();
        var mockIa = new Mock<IIAService>();
        var mockCredito = new Mock<ICreditoService>();
        mockCredito.Setup(c => c.TemCreditoDisponivelAsync(TipoCreditoAI.TraducaoAndamento, 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var service = new TraducaoService(ctx, CreateTenantContext(tenantId, usuarioId),
            mockIa.Object, mockCredito.Object, Mock.Of<IEmailService>());

        var dto = new TraduzirAndamentoDto(andamentoId, null, false, false);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.TraduzirAndamentoAsync(dto, usuarioId));
    }

    [Fact]
    public async Task TraduzirAndamentoAsync_DeveTraduzir_QuandoCreditoDisponivel()
    {
        var (ctx, tenantId, usuarioId, _, andamentoId) = await SeedAsync();
        var mockIa = new Mock<IIAService>();
        mockIa.Setup(i => i.TraduzirTextoAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("Citação realizada com sucesso. Aguarde próximos andamentos.");

        var mockCredito = new Mock<ICreditoService>();
        mockCredito.Setup(c => c.TemCreditoDisponivelAsync(TipoCreditoAI.TraducaoAndamento, 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        mockCredito.Setup(c => c.ConsumirCreditoAsync(TipoCreditoAI.TraducaoAndamento, 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var service = new TraducaoService(ctx, CreateTenantContext(tenantId, usuarioId),
            mockIa.Object, mockCredito.Object, Mock.Of<IEmailService>());

        var dto = new TraduzirAndamentoDto(andamentoId, null, false, false);
        var result = await service.TraduzirAndamentoAsync(dto, usuarioId);

        Assert.NotNull(result);
        Assert.Contains("Citação", result.TextoTraduzido);
        mockCredito.Verify(c => c.ConsumirCreditoAsync(TipoCreditoAI.TraducaoAndamento, 1, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task TraduzirAndamentoAsync_DeveLancarExcecao_QuandoAndamentoNaoEncontrado()
    {
        var (ctx, tenantId, usuarioId, _, _) = await SeedAsync();
        var mockIa = new Mock<IIAService>();
        var mockCredito = new Mock<ICreditoService>();
        mockCredito.Setup(c => c.TemCreditoDisponivelAsync(TipoCreditoAI.TraducaoAndamento, 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var service = new TraducaoService(ctx, CreateTenantContext(tenantId, usuarioId),
            mockIa.Object, mockCredito.Object, Mock.Of<IEmailService>());

        var dto = new TraduzirAndamentoDto(Guid.NewGuid(), null, false, false);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.TraduzirAndamentoAsync(dto, usuarioId));
    }

    [Fact]
    public async Task ObterTraducaoAsync_DeveRetornarTraducao_QuandoExistir()
    {
        var (ctx, tenantId, usuarioId, _, andamentoId) = await SeedAsync();
        ctx.TraducoesAndamentos.Add(new TraducaoAndamento
        {
            Id = Guid.NewGuid(), AndamentoId = andamentoId, TenantId = tenantId,
            SolicitadoPorId = usuarioId, TextoOriginal = "Original",
            TextoTraduzido = "Tradução", EnviadoAoCliente = false,
            RevisadoPreviamente = false, CriadoEm = DateTime.UtcNow
        });
        await ctx.SaveChangesAsync();

        var service = new TraducaoService(ctx, CreateTenantContext(tenantId, usuarioId),
            Mock.Of<IIAService>(), Mock.Of<ICreditoService>(), Mock.Of<IEmailService>());

        var result = await service.ObterTraducaoAsync(andamentoId);

        Assert.NotNull(result);
        Assert.Equal("Tradução", result.TextoTraduzido);
    }

    [Fact]
    public async Task ObterTraducaoAsync_DeveRetornarNull_QuandoNaoExistir()
    {
        var (ctx, tenantId, usuarioId, _, _) = await SeedAsync();
        var service = new TraducaoService(ctx, CreateTenantContext(tenantId, usuarioId),
            Mock.Of<IIAService>(), Mock.Of<ICreditoService>(), Mock.Of<IEmailService>());

        var result = await service.ObterTraducaoAsync(Guid.NewGuid());

        Assert.Null(result);
    }

    [Fact]
    public async Task ListarPorClienteAsync_DeveRetornarTraducoesDoCliente()
    {
        var (ctx, tenantId, usuarioId, _, _) = await SeedAsync();
        var clienteId = Guid.NewGuid();
        ctx.TraducoesAndamentos.AddRange(
            new TraducaoAndamento { Id = Guid.NewGuid(), AndamentoId = Guid.NewGuid(), TenantId = tenantId, SolicitadoPorId = usuarioId, ClienteId = clienteId, TextoOriginal = "O1", TextoTraduzido = "T1", EnviadoAoCliente = false, RevisadoPreviamente = false, CriadoEm = DateTime.UtcNow.AddMinutes(-1) },
            new TraducaoAndamento { Id = Guid.NewGuid(), AndamentoId = Guid.NewGuid(), TenantId = tenantId, SolicitadoPorId = usuarioId, ClienteId = clienteId, TextoOriginal = "O2", TextoTraduzido = "T2", EnviadoAoCliente = false, RevisadoPreviamente = false, CriadoEm = DateTime.UtcNow }
        );
        await ctx.SaveChangesAsync();

        var service = new TraducaoService(ctx, CreateTenantContext(tenantId, usuarioId),
            Mock.Of<IIAService>(), Mock.Of<ICreditoService>(), Mock.Of<IEmailService>());

        var result = await service.ListarPorClienteAsync(clienteId, 1, 10);

        Assert.Equal(2, result.Count());
    }

    [Fact]
    public async Task ListarPorClienteAsync_DevePaginarCorretamente()
    {
        var (ctx, tenantId, usuarioId, _, _) = await SeedAsync();
        var clienteId = Guid.NewGuid();
        for (int i = 0; i < 15; i++)
        {
            ctx.TraducoesAndamentos.Add(new TraducaoAndamento
            {
                Id = Guid.NewGuid(), AndamentoId = Guid.NewGuid(), TenantId = tenantId,
                SolicitadoPorId = usuarioId, ClienteId = clienteId,
                TextoOriginal = $"O{i}", TextoTraduzido = $"T{i}",
                EnviadoAoCliente = false, RevisadoPreviamente = false,
                CriadoEm = DateTime.UtcNow.AddMinutes(-i)
            });
        }
        await ctx.SaveChangesAsync();

        var service = new TraducaoService(ctx, CreateTenantContext(tenantId, usuarioId),
            Mock.Of<IIAService>(), Mock.Of<ICreditoService>(), Mock.Of<IEmailService>());

        var page1 = await service.ListarPorClienteAsync(clienteId, 1, 10);
        var page2 = await service.ListarPorClienteAsync(clienteId, 2, 10);

        Assert.Equal(10, page1.Count());
        Assert.Equal(5, page2.Count());
    }
}
