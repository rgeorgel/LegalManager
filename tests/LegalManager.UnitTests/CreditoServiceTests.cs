using LegalManager.Application.DTOs.IA;
using LegalManager.Domain.Entities;
using LegalManager.Domain.Enums;
using LegalManager.Domain.Interfaces;
using LegalManager.Infrastructure.Persistence;
using LegalManager.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace LegalManager.UnitTests;

public class CreditoServiceTests
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
        mock.Setup(t => t.Plano).Returns(PlanoTipo.Free);
        return mock.Object;
    }

    private async Task<(AppDbContext ctx, Guid tenantId)> SeedTenantAsync()
    {
        var ctx = CreateContext();
        var tenantId = Guid.NewGuid();
        var tenant = new Tenant
        {
            Id = tenantId,
            Nome = "Test Tenant",
            Plano = PlanoTipo.Pro,
            Status = StatusTenant.Ativo,
            CriadoEm = DateTime.UtcNow
        };
        ctx.Tenants.Add(tenant);
        await ctx.SaveChangesAsync();
        return (ctx, tenantId);
    }

    [Fact]
    public async Task ObterCreditosAsync_DeveRetornarCreditosDoTenant()
    {
        var (ctx, tenantId) = await SeedTenantAsync();
        ctx.CreditosAI.Add(new CreditoAI
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Tipo = TipoCreditoAI.TraducaoAndamento,
            QuantidadeTotal = 5,
            QuantidadeUsada = 2,
            Origem = OrigemCreditoAI.Cortesai,
            CriadoEm = DateTime.UtcNow
        });
        await ctx.SaveChangesAsync();

        var service = new CreditoService(ctx, CreateTenantContext(tenantId));
        var result = await service.ObterCreditosAsync();

        Assert.Single(result.Creditos);
        Assert.Equal(3, result.TotalDisponivelGeral);
    }

    [Fact]
    public async Task ObterCreditosAsync_DeveRetornarVazio_QuandoNenhumCredito()
    {
        var (ctx, tenantId) = await SeedTenantAsync();
        var service = new CreditoService(ctx, CreateTenantContext(tenantId));

        var result = await service.ObterCreditosAsync();

        Assert.Empty(result.Creditos);
        Assert.Equal(0, result.TotalDisponivelGeral);
    }

    [Fact]
    public async Task TemCreditoDisponivelAsync_DeveRetornarTrue_QuandoDisponivel()
    {
        var (ctx, tenantId) = await SeedTenantAsync();
        ctx.CreditosAI.Add(new CreditoAI
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Tipo = TipoCreditoAI.GeracaoPeca,
            QuantidadeTotal = 2,
            QuantidadeUsada = 0,
            Origem = OrigemCreditoAI.Cortesai,
            CriadoEm = DateTime.UtcNow
        });
        await ctx.SaveChangesAsync();

        var service = new CreditoService(ctx, CreateTenantContext(tenantId));
        var result = await service.TemCreditoDisponivelAsync(TipoCreditoAI.GeracaoPeca);

        Assert.True(result);
    }

    [Fact]
    public async Task TemCreditoDisponivelAsync_DeveRetornarFalse_QuandoEsgotado()
    {
        var (ctx, tenantId) = await SeedTenantAsync();
        ctx.CreditosAI.Add(new CreditoAI
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Tipo = TipoCreditoAI.TraducaoAndamento,
            QuantidadeTotal = 5,
            QuantidadeUsada = 5,
            Origem = OrigemCreditoAI.Cortesai,
            CriadoEm = DateTime.UtcNow
        });
        await ctx.SaveChangesAsync();

        var service = new CreditoService(ctx, CreateTenantContext(tenantId));
        var result = await service.TemCreditoDisponivelAsync(TipoCreditoAI.TraducaoAndamento);

        Assert.False(result);
    }

    [Fact]
    public async Task TemCreditoDisponivelAsync_DeveRetornarFalse_QuandoCreditoNaoExiste()
    {
        var (ctx, tenantId) = await SeedTenantAsync();
        var service = new CreditoService(ctx, CreateTenantContext(tenantId));

        var result = await service.TemCreditoDisponivelAsync(TipoCreditoAI.GeracaoPeca);

        Assert.False(result);
    }

    [Fact]
    public async Task ConsumirCreditoAsync_DeveDiminuirQuantidadeDisponivel()
    {
        var (ctx, tenantId) = await SeedTenantAsync();
        var credito = new CreditoAI
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Tipo = TipoCreditoAI.GeracaoPeca,
            QuantidadeTotal = 2,
            QuantidadeUsada = 0,
            Origem = OrigemCreditoAI.Cortesai,
            CriadoEm = DateTime.UtcNow
        };
        ctx.CreditosAI.Add(credito);
        await ctx.SaveChangesAsync();

        var service = new CreditoService(ctx, CreateTenantContext(tenantId));
        var result = await service.ConsumirCreditoAsync(TipoCreditoAI.GeracaoPeca);

        Assert.True(result);
        var updated = await ctx.CreditosAI.FindAsync(credito.Id);
        Assert.Equal(1, updated!.QuantidadeUsada);
        Assert.Equal(1, updated.QuantidadeDisponivel);
    }

    [Fact]
    public async Task ConsumirCreditoAsync_DeveRetornarFalse_QuandoNaoHaCredito()
    {
        var (ctx, tenantId) = await SeedTenantAsync();
        var service = new CreditoService(ctx, CreateTenantContext(tenantId));

        var result = await service.ConsumirCreditoAsync(TipoCreditoAI.ClassificacaoPublicacao);

        Assert.False(result);
    }

    [Fact]
    public async Task ConsumirCreditoAsync_DeveRetornarFalse_QuandoQuantidadeInsuficiente()
    {
        var (ctx, tenantId) = await SeedTenantAsync();
        ctx.CreditosAI.Add(new CreditoAI
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Tipo = TipoCreditoAI.TraducaoAndamento,
            QuantidadeTotal = 5,
            QuantidadeUsada = 4,
            Origem = OrigemCreditoAI.Cortesai,
            CriadoEm = DateTime.UtcNow
        });
        await ctx.SaveChangesAsync();

        var service = new CreditoService(ctx, CreateTenantContext(tenantId));
        var result = await service.ConsumirCreditoAsync(TipoCreditoAI.TraducaoAndamento, 2);

        Assert.False(result);
    }

    [Fact]
    public async Task InicializarCreditosPadraoAsync_DeveCriarCreditos_QuandoNenhumExiste()
    {
        var (ctx, tenantId) = await SeedTenantAsync();
        var service = new CreditoService(ctx, CreateTenantContext(tenantId));

        await service.InicializarCreditosPadraoAsync(tenantId, PlanoTipo.Pro);

        var creditos = await ctx.CreditosAI.Where(c => c.TenantId == tenantId).ToListAsync();
        Assert.Equal(3, creditos.Count);
    }

    [Fact]
    public async Task InicializarCreditosPadraoAsync_DeveIgnorar_QuandoJaExistenCreditos()
    {
        var (ctx, tenantId) = await SeedTenantAsync();
        ctx.CreditosAI.Add(new CreditoAI
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Tipo = TipoCreditoAI.TraducaoAndamento,
            QuantidadeTotal = 10,
            QuantidadeUsada = 0,
            Origem = OrigemCreditoAI.Cortesai,
            CriadoEm = DateTime.UtcNow
        });
        await ctx.SaveChangesAsync();

        var service = new CreditoService(ctx, CreateTenantContext(tenantId));
        await service.InicializarCreditosPadraoAsync(tenantId, PlanoTipo.Pro);

        var count = await ctx.CreditosAI.Where(c => c.TenantId == tenantId).CountAsync();
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task ObterCreditosAsync_DeveCalcularTotalGeralCorretamente()
    {
        var (ctx, tenantId) = await SeedTenantAsync();
        ctx.CreditosAI.AddRange(
            new CreditoAI { Id = Guid.NewGuid(), TenantId = tenantId, Tipo = TipoCreditoAI.TraducaoAndamento, QuantidadeTotal = 5, QuantidadeUsada = 2, Origem = OrigemCreditoAI.Cortesai, CriadoEm = DateTime.UtcNow },
            new CreditoAI { Id = Guid.NewGuid(), TenantId = tenantId, Tipo = TipoCreditoAI.GeracaoPeca, QuantidadeTotal = 2, QuantidadeUsada = 1, Origem = OrigemCreditoAI.Cortesai, CriadoEm = DateTime.UtcNow }
        );
        await ctx.SaveChangesAsync();

        var service = new CreditoService(ctx, CreateTenantContext(tenantId));
        var result = await service.ObterCreditosAsync();

        Assert.Equal(4, result.TotalDisponivelGeral);
    }
}
