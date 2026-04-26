using LegalManager.Application.DTOs.Prazos;
using LegalManager.Domain.Entities;
using LegalManager.Domain.Enums;
using LegalManager.Domain.Interfaces;
using LegalManager.Infrastructure.Persistence;
using LegalManager.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace LegalManager.UnitTests;

public class PrazoServiceTests
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

    private async Task<(AppDbContext ctx, Guid tenantId)> SeedAsync()
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
    public async Task CreateAsync_DeveCriarPrazo_ComDataFinalCalculada()
    {
        var (ctx, tenantId) = await SeedAsync();
        var service = new PrazoService(ctx, CreateTenantContext(tenantId));
        var dto = new CreatePrazoDto(null, null, "Prazo para contestação",
            DateTime.UtcNow, 15, TipoCalculo.DiasCorridos, null, null);

        var result = await service.CreateAsync(dto);

        Assert.NotNull(result);
        Assert.Equal("Prazo para contestação", result.Descricao);
        Assert.Equal(15, result.QuantidadeDias);
    }

    [Fact]
    public async Task CreateAsync_DeveCalcularDiasUteis_Corretamente()
    {
        var (ctx, tenantId) = await SeedAsync();
        var service = new PrazoService(ctx, CreateTenantContext(tenantId));
        var segunda = DateTime.UtcNow;
        while (segunda.DayOfWeek != DayOfWeek.Monday) segunda = segunda.AddDays(1);

        var dto = new CreatePrazoDto(null, null, "Prazo útil",
            segunda.Date, 5, TipoCalculo.DiasUteis, null, null);

        var result = await service.CreateAsync(dto);

        Assert.Equal(5, result.QuantidadeDias);
        Assert.Equal(TipoCalculo.DiasUteis, result.TipoCalculo);
    }

    [Fact]
    public async Task GetByIdAsync_DeveRetornarPrazo_QuandoExistir()
    {
        var (ctx, tenantId) = await SeedAsync();
        var prazoId = Guid.NewGuid();
        ctx.Prazos.Add(new Prazo
        {
            Id = prazoId, TenantId = tenantId, Descricao = "Teste prazo",
            DataInicio = DateTime.UtcNow, QuantidadeDias = 10, TipoCalculo = TipoCalculo.DiasCorridos,
            DataFinal = DateTime.UtcNow.AddDays(10), Status = StatusPrazo.Pendente,
            CriadoEm = DateTime.UtcNow
        });
        await ctx.SaveChangesAsync();

        var service = new PrazoService(ctx, CreateTenantContext(tenantId));
        var result = await service.GetByIdAsync(prazoId);

        Assert.NotNull(result);
        Assert.Equal("Teste prazo", result.Descricao);
    }

    [Fact]
    public async Task GetByIdAsync_DeveRetornarNull_QuandoNaoExistir()
    {
        var (ctx, tenantId) = await SeedAsync();
        var service = new PrazoService(ctx, CreateTenantContext(tenantId));

        var result = await service.GetByIdAsync(Guid.NewGuid());

        Assert.Null(result);
    }

    [Fact]
    public async Task UpdateAsync_DeveAtualizarCampos()
    {
        var (ctx, tenantId) = await SeedAsync();
        var prazoId = Guid.NewGuid();
        ctx.Prazos.Add(new Prazo
        {
            Id = prazoId, TenantId = tenantId, Descricao = "Original",
            DataInicio = DateTime.UtcNow, QuantidadeDias = 10, TipoCalculo = TipoCalculo.DiasCorridos,
            DataFinal = DateTime.UtcNow.AddDays(10), Status = StatusPrazo.Pendente,
            CriadoEm = DateTime.UtcNow
        });
        await ctx.SaveChangesAsync();

        var service = new PrazoService(ctx, CreateTenantContext(tenantId));
        var dto = new UpdatePrazoDto("Atualizado", DateTime.UtcNow, 20,
            TipoCalculo.DiasCorridos, StatusPrazo.Cumprido, null, "Obs atualizada");
        var result = await service.UpdateAsync(prazoId, dto);

        Assert.Equal("Atualizado", result.Descricao);
        Assert.Equal(StatusPrazo.Cumprido, result.Status);
    }

    [Fact]
    public async Task DeleteAsync_DeveRemoverPrazo()
    {
        var (ctx, tenantId) = await SeedAsync();
        var prazoId = Guid.NewGuid();
        ctx.Prazos.Add(new Prazo
        {
            Id = prazoId, TenantId = tenantId, Descricao = "Para deletar",
            DataInicio = DateTime.UtcNow, QuantidadeDias = 5, TipoCalculo = TipoCalculo.DiasCorridos,
            DataFinal = DateTime.UtcNow.AddDays(5), Status = StatusPrazo.Pendente,
            CriadoEm = DateTime.UtcNow
        });
        await ctx.SaveChangesAsync();

        var service = new PrazoService(ctx, CreateTenantContext(tenantId));
        await service.DeleteAsync(prazoId);

        Assert.Null(await ctx.Prazos.FindAsync(prazoId));
    }

    [Fact]
    public async Task GetByProcessoAsync_DeveRetornarPrazosDoProcesso()
    {
        var (ctx, tenantId) = await SeedAsync();
        var processoId = Guid.NewGuid();
        ctx.Prazos.AddRange(
            new Prazo { Id = Guid.NewGuid(), TenantId = tenantId, ProcessoId = processoId, Descricao = "P1", DataInicio = DateTime.UtcNow, QuantidadeDias = 5, TipoCalculo = TipoCalculo.DiasCorridos, DataFinal = DateTime.UtcNow.AddDays(5), Status = StatusPrazo.Pendente, CriadoEm = DateTime.UtcNow },
            new Prazo { Id = Guid.NewGuid(), TenantId = tenantId, ProcessoId = Guid.NewGuid(), Descricao = "P2", DataInicio = DateTime.UtcNow, QuantidadeDias = 10, TipoCalculo = TipoCalculo.DiasCorridos, DataFinal = DateTime.UtcNow.AddDays(10), Status = StatusPrazo.Pendente, CriadoEm = DateTime.UtcNow }
        );
        await ctx.SaveChangesAsync();

        var service = new PrazoService(ctx, CreateTenantContext(tenantId));
        var result = await service.GetByProcessoAsync(processoId);

        Assert.Single(result);
        Assert.Equal("P1", result.First().Descricao);
    }

    [Fact]
    public async Task GetPendentesAsync_DeveRetornarPrazosPendentes()
    {
        var (ctx, tenantId) = await SeedAsync();
        ctx.Prazos.AddRange(
            new Prazo { Id = Guid.NewGuid(), TenantId = tenantId, Descricao = "Pendente", DataInicio = DateTime.UtcNow.AddDays(-5), QuantidadeDias = 10, TipoCalculo = TipoCalculo.DiasCorridos, DataFinal = DateTime.UtcNow.AddDays(5), Status = StatusPrazo.Pendente, CriadoEm = DateTime.UtcNow },
            new Prazo { Id = Guid.NewGuid(), TenantId = tenantId, Descricao = "Cumprido", DataInicio = DateTime.UtcNow.AddDays(-10), QuantidadeDias = 10, TipoCalculo = TipoCalculo.DiasCorridos, DataFinal = DateTime.UtcNow.AddDays(0), Status = StatusPrazo.Cumprido, CriadoEm = DateTime.UtcNow }
        );
        await ctx.SaveChangesAsync();

        var service = new PrazoService(ctx, CreateTenantContext(tenantId));
        var result = await service.GetPendentesAsync(10);

        Assert.Single(result);
        Assert.Equal("Pendente", result.First().Descricao);
    }

    [Fact]
    public void CalcularDataFinal_DiasCorridos_DeveAdicionarDiasSimples()
    {
        var service = new PrazoService(CreateContext(), CreateTenantContext(Guid.NewGuid()));
        var inicio = new DateTime(2024, 1, 1);

        var result = service.CalcularDataFinal(inicio, 10, false);

        Assert.Equal(new DateTime(2024, 1, 11), result.Date);
    }

    [Fact]
    public void CalcularDataFinal_DiasUteis_DevePularFinsDeSemana()
    {
        var service = new PrazoService(CreateContext(), CreateTenantContext(Guid.NewGuid()));
        var sexta = new DateTime(2024, 1, 5);

        var result = service.CalcularDataFinal(sexta, 1, true);

        Assert.Equal(DayOfWeek.Monday, result.DayOfWeek);
    }
}
