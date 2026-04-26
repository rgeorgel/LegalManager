using LegalManager.Application.DTOs.Financeiro;
using LegalManager.Domain.Entities;
using LegalManager.Domain.Enums;
using LegalManager.Domain.Interfaces;
using LegalManager.Infrastructure.Persistence;
using LegalManager.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace LegalManager.UnitTests;

public class FinanceiroServiceTests
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

    private async Task<(AppDbContext ctx, Guid tenantId)> SeedTenantAsync()
    {
        var ctx = CreateContext();
        var tenantId = Guid.NewGuid();
        ctx.Tenants.Add(new Tenant
        {
            Id = tenantId, Nome = "Test", Plano = PlanoTipo.Pro,
            Status = StatusTenant.Ativo, CriadoEm = DateTime.UtcNow
        });
        await ctx.SaveChangesAsync();
        return (ctx, tenantId);
    }

    [Fact]
    public async Task CriarAsync_DeveCriarLancamento_ComDadosValidos()
    {
        var (ctx, tenantId) = await SeedTenantAsync();
        var service = new FinanceiroService(ctx);

        var dto = new CriarLancamentoDto(TipoLancamento.Receita, "Honorário", 5000m,
            DateTime.UtcNow.AddDays(30), "Serviço prestado");

        var result = await service.CriarAsync(tenantId, dto);

        Assert.NotNull(result);
        Assert.Equal("Honorário", result.Categoria);
        Assert.Equal(5000m, result.Valor);
        Assert.Equal(StatusLancamento.Pendente, result.Status);
    }

    [Fact]
    public async Task GetAllAsync_DeveRetornarApenasLancamentosDoTenant()
    {
        var (ctx, tenantId) = await SeedTenantAsync();
        var outroTenantId = Guid.NewGuid();
        ctx.Tenants.Add(new Tenant { Id = outroTenantId, Nome = "Outro", Plano = PlanoTipo.Free, Status = StatusTenant.Ativo, CriadoEm = DateTime.UtcNow });
        ctx.LancamentosFinanceiros.AddRange(
            new LancamentoFinanceiro { Id = Guid.NewGuid(), TenantId = tenantId, Tipo = TipoLancamento.Receita, Categoria = "H1", Valor = 1000m, DataVencimento = DateTime.UtcNow, Status = StatusLancamento.Pendente },
            new LancamentoFinanceiro { Id = Guid.NewGuid(), TenantId = outroTenantId, Tipo = TipoLancamento.Receita, Categoria = "H2", Valor = 2000m, DataVencimento = DateTime.UtcNow, Status = StatusLancamento.Pendente }
        );
        await ctx.SaveChangesAsync();

        var service = new FinanceiroService(ctx);
        var result = await service.GetAllAsync(tenantId, null, null, null, null, 1, 10);

        Assert.Single(result.Items);
    }

    [Fact]
    public async Task GetAllAsync_DeveFiltrarPorTipo()
    {
        var (ctx, tenantId) = await SeedTenantAsync();
        ctx.LancamentosFinanceiros.AddRange(
            new LancamentoFinanceiro { Id = Guid.NewGuid(), TenantId = tenantId, Tipo = TipoLancamento.Receita, Categoria = "H", Valor = 1000m, DataVencimento = DateTime.UtcNow, Status = StatusLancamento.Pendente },
            new LancamentoFinanceiro { Id = Guid.NewGuid(), TenantId = tenantId, Tipo = TipoLancamento.Despesa, Categoria = "C", Valor = 500m, DataVencimento = DateTime.UtcNow, Status = StatusLancamento.Pendente }
        );
        await ctx.SaveChangesAsync();

        var service = new FinanceiroService(ctx);
        var result = await service.GetAllAsync(tenantId, TipoLancamento.Receita, null, null, null, 1, 10);

        Assert.Single(result.Items);
        Assert.Equal(TipoLancamento.Receita, result.Items.First().Tipo);
    }

    [Fact]
    public async Task GetAllAsync_DeveFiltrarPorStatus()
    {
        var (ctx, tenantId) = await SeedTenantAsync();
        ctx.LancamentosFinanceiros.AddRange(
            new LancamentoFinanceiro { Id = Guid.NewGuid(), TenantId = tenantId, Tipo = TipoLancamento.Receita, Categoria = "H", Valor = 1000m, DataVencimento = DateTime.UtcNow, Status = StatusLancamento.Pendente },
            new LancamentoFinanceiro { Id = Guid.NewGuid(), TenantId = tenantId, Tipo = TipoLancamento.Receita, Categoria = "H2", Valor = 2000m, DataVencimento = DateTime.UtcNow, Status = StatusLancamento.Pago }
        );
        await ctx.SaveChangesAsync();

        var service = new FinanceiroService(ctx);
        var result = await service.GetAllAsync(tenantId, null, StatusLancamento.Pago, null, null, 1, 10);

        Assert.Single(result.Items);
    }

    [Fact]
    public async Task GetByIdAsync_DeveRetornarLancamento_QuandoExistir()
    {
        var (ctx, tenantId) = await SeedTenantAsync();
        var lancId = Guid.NewGuid();
        ctx.LancamentosFinanceiros.Add(new LancamentoFinanceiro
        {
            Id = lancId, TenantId = tenantId, Tipo = TipoLancamento.Receita,
            Categoria = "Honorário", Valor = 3000m, DataVencimento = DateTime.UtcNow,
            Status = StatusLancamento.Pendente
        });
        await ctx.SaveChangesAsync();

        var service = new FinanceiroService(ctx);
        var result = await service.GetByIdAsync(lancId, tenantId);

        Assert.NotNull(result);
        Assert.Equal(3000m, result.Valor);
    }

    [Fact]
    public async Task GetByIdAsync_DeveRetornarNull_QuandoNaoExistir()
    {
        var (ctx, tenantId) = await SeedTenantAsync();
        var service = new FinanceiroService(ctx);

        var result = await service.GetByIdAsync(Guid.NewGuid(), tenantId);

        Assert.Null(result);
    }

    [Fact]
    public async Task PagarAsync_DeveAtualizarStatusEPopularDataPagamento()
    {
        var (ctx, tenantId) = await SeedTenantAsync();
        var lancId = Guid.NewGuid();
        ctx.LancamentosFinanceiros.Add(new LancamentoFinanceiro
        {
            Id = lancId, TenantId = tenantId, Tipo = TipoLancamento.Receita,
            Categoria = "H", Valor = 1000m, DataVencimento = DateTime.UtcNow.AddDays(-5),
            Status = StatusLancamento.Pendente
        });
        await ctx.SaveChangesAsync();

        var service = new FinanceiroService(ctx);
        var dataPag = DateTime.UtcNow.AddDays(-1);
        await service.PagarAsync(lancId, tenantId, dataPag);

        var updated = await ctx.LancamentosFinanceiros.FindAsync(lancId);
        Assert.Equal(StatusLancamento.Pago, updated!.Status);
        Assert.NotNull(updated.DataPagamento);
    }

    [Fact]
    public async Task CancelarAsync_DeveAtualizarStatus()
    {
        var (ctx, tenantId) = await SeedTenantAsync();
        var lancId = Guid.NewGuid();
        ctx.LancamentosFinanceiros.Add(new LancamentoFinanceiro
        {
            Id = lancId, TenantId = tenantId, Tipo = TipoLancamento.Receita,
            Categoria = "H", Valor = 1000m, DataVencimento = DateTime.UtcNow,
            Status = StatusLancamento.Pendente
        });
        await ctx.SaveChangesAsync();

        var service = new FinanceiroService(ctx);
        await service.CancelarAsync(lancId, tenantId);

        var updated = await ctx.LancamentosFinanceiros.FindAsync(lancId);
        Assert.Equal(StatusLancamento.Cancelado, updated!.Status);
    }

    [Fact]
    public async Task AtualizarAsync_DeveAtualizarCampos()
    {
        var (ctx, tenantId) = await SeedTenantAsync();
        var lancId = Guid.NewGuid();
        ctx.LancamentosFinanceiros.Add(new LancamentoFinanceiro
        {
            Id = lancId, TenantId = tenantId, Tipo = TipoLancamento.Receita,
            Categoria = "H", Valor = 1000m, DataVencimento = DateTime.UtcNow,
            Status = StatusLancamento.Pendente
        });
        await ctx.SaveChangesAsync();

        var service = new FinanceiroService(ctx);
        var dto = new AtualizarLancamentoDto("Novo Honorário", 5000m, DateTime.UtcNow.AddDays(15), "Nova descrição");
        await service.AtualizarAsync(lancId, tenantId, dto);

        var updated = await service.GetByIdAsync(lancId, tenantId);
        Assert.Equal("Novo Honorário", updated!.Categoria);
        Assert.Equal(5000m, updated.Valor);
    }

    [Fact]
    public async Task GetResumoCompletoAsync_DeveCalcularResumoMesEAno()
    {
        var (ctx, tenantId) = await SeedTenantAsync();
        var now = DateTime.UtcNow;
        ctx.LancamentosFinanceiros.AddRange(
            new LancamentoFinanceiro { Id = Guid.NewGuid(), TenantId = tenantId, Tipo = TipoLancamento.Receita, Categoria = "H", Valor = 5000m, DataVencimento = new DateTime(now.Year, now.Month, 10), Status = StatusLancamento.Pago },
            new LancamentoFinanceiro { Id = Guid.NewGuid(), TenantId = tenantId, Tipo = TipoLancamento.Despesa, Categoria = "SW", Valor = 1000m, DataVencimento = new DateTime(now.Year, now.Month, 15), Status = StatusLancamento.Pago }
        );
        await ctx.SaveChangesAsync();

        var service = new FinanceiroService(ctx);
        var result = await service.GetResumoCompletoAsync(tenantId, now.Year, now.Month);

        Assert.Equal(5000m, result.Mes.TotalReceitas);
        Assert.Equal(1000m, result.Mes.TotalDespesas);
        Assert.Equal(4000m, result.Mes.Saldo);
    }

    [Fact]
    public async Task GetAllAsync_DevePaginarCorretamente()
    {
        var (ctx, tenantId) = await SeedTenantAsync();
        for (int i = 0; i < 25; i++)
        {
            ctx.LancamentosFinanceiros.Add(new LancamentoFinanceiro
            {
                Id = Guid.NewGuid(), TenantId = tenantId, Tipo = TipoLancamento.Receita,
                Categoria = $"H{i}", Valor = 1000m + i,
                DataVencimento = DateTime.UtcNow.AddDays(-i), Status = StatusLancamento.Pendente
            });
        }
        await ctx.SaveChangesAsync();

        var service = new FinanceiroService(ctx);
        var page1 = await service.GetAllAsync(tenantId, null, null, null, null, 1, 10);
        var page3 = await service.GetAllAsync(tenantId, null, null, null, null, 3, 10);

        Assert.Equal(10, page1.Items.Count());
        Assert.Equal(5, page3.Items.Count());
        Assert.Equal(25, page1.Total);
    }
}
