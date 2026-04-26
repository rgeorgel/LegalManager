using LegalManager.Application.DTOs.Monitoramento;
using LegalManager.Application.Interfaces;
using LegalManager.Domain.Entities;
using LegalManager.Domain.Enums;
using LegalManager.Domain.Interfaces;
using LegalManager.Infrastructure.Persistence;
using LegalManager.Infrastructure.Services;
using LegalManager.Infrastructure.Tribunais;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;

namespace LegalManager.UnitTests;

public class MonitoramentoServiceTests
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

    private async Task<(AppDbContext ctx, Guid tenantId, Guid advogadoId)> SeedAsync()
    {
        var ctx = CreateContext();
        var tenantId = Guid.NewGuid();
        var advogadoId = Guid.NewGuid();
        ctx.Tenants.Add(new Tenant
        {
            Id = tenantId, Nome = "Test", Plano = PlanoTipo.Pro,
            Status = StatusTenant.Ativo, CriadoEm = DateTime.UtcNow
        });
        ctx.Users.Add(new Usuario
        {
            Id = advogadoId, TenantId = tenantId, Nome = "Advogado",
            Email = "adv@test.com", UserName = "adv@test.com",
            Perfil = PerfilUsuario.Advogado, Ativo = true, CriadoEm = DateTime.UtcNow
        });
        await ctx.SaveChangesAsync();
        return (ctx, tenantId, advogadoId);
    }

    [Fact]
    public async Task AlternarMonitoramentoAsync_DeveAlternarMonitorado()
    {
        var (ctx, tenantId, _) = await SeedAsync();
        var processoId = Guid.NewGuid();
        ctx.Processos.Add(new Processo
        {
            Id = processoId, TenantId = tenantId, NumeroCNJ = "0000001-00.2024.8.26.0001",
            AreaDireito = AreaDireito.Civil, Fase = FaseProcessual.Conhecimento,
            Status = StatusProcesso.Ativo, Monitorado = false, CriadoEm = DateTime.UtcNow
        });
        await ctx.SaveChangesAsync();

        var dataJud = new DataJudAdapter(Mock.Of<HttpClient>(), Mock.Of<ILogger<DataJudAdapter>>());
        var service = new MonitoramentoService(ctx, dataJud, CreateTenantContext(tenantId),
            Mock.Of<IEmailService>(), Mock.Of<ILogger<MonitoramentoService>>());

        var result = await service.AlternarMonitoramentoAsync(processoId);

        Assert.True(result);
        var updated = await ctx.Processos.FindAsync(processoId);
        Assert.True(updated!.Monitorado);
    }

    [Fact]
    public async Task MonitorarProcessoAsync_DeveRetornarNaoEncontrado_QuandoProcessoNaoExiste()
    {
        var (ctx, tenantId, _) = await SeedAsync();
        var dataJud = new DataJudAdapter(Mock.Of<HttpClient>(), Mock.Of<ILogger<DataJudAdapter>>());
        var service = new MonitoramentoService(ctx, dataJud, CreateTenantContext(tenantId),
            Mock.Of<IEmailService>(), Mock.Of<ILogger<MonitoramentoService>>());

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            service.MonitorarProcessoAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task MonitorarTodosAsync_DeveRetornarZero_QuandoNenhumProcessoMonitorado()
    {
        var (ctx, tenantId, _) = await SeedAsync();
        var dataJud = new DataJudAdapter(Mock.Of<HttpClient>(), Mock.Of<ILogger<DataJudAdapter>>());
        var service = new MonitoramentoService(ctx, dataJud, CreateTenantContext(tenantId),
            Mock.Of<IEmailService>(), Mock.Of<ILogger<MonitoramentoService>>());

        var result = await service.MonitorarTodosAsync();

        Assert.Equal(0, result);
    }

    [Fact]
    public async Task AlternarMonitoramentoAsync_DeveLancarKeyNotFoundException_QuandoProcessoNaoExiste()
    {
        var (ctx, tenantId, _) = await SeedAsync();
        var dataJud = new DataJudAdapter(Mock.Of<HttpClient>(), Mock.Of<ILogger<DataJudAdapter>>());
        var service = new MonitoramentoService(ctx, dataJud, CreateTenantContext(tenantId),
            Mock.Of<IEmailService>(), Mock.Of<ILogger<MonitoramentoService>>());

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            service.AlternarMonitoramentoAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task AlternarMonitoramentoAsync_DeveRetornarFalse_AoDesativar()
    {
        var (ctx, tenantId, _) = await SeedAsync();
        var processoId = Guid.NewGuid();
        ctx.Processos.Add(new Processo
        {
            Id = processoId, TenantId = tenantId, NumeroCNJ = "0000001-00.2024.8.26.0001",
            AreaDireito = AreaDireito.Civil, Fase = FaseProcessual.Conhecimento,
            Status = StatusProcesso.Ativo, Monitorado = true, CriadoEm = DateTime.UtcNow
        });
        await ctx.SaveChangesAsync();

        var dataJud = new DataJudAdapter(Mock.Of<HttpClient>(), Mock.Of<ILogger<DataJudAdapter>>());
        var service = new MonitoramentoService(ctx, dataJud, CreateTenantContext(tenantId),
            Mock.Of<IEmailService>(), Mock.Of<ILogger<MonitoramentoService>>());

        var result = await service.AlternarMonitoramentoAsync(processoId);

        Assert.False(result);
    }
}
