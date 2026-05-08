using LegalManager.Application.DTOs.Processos;
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

public class ProcessoMonitoradoServiceTests
{
    private static (ProcessoMonitoradoService Svc, AppDbContext Ctx, Guid TenantId) Build(PlanoTipo plano = PlanoTipo.Pro)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        var ctx = new AppDbContext(options);

        var tenantId = Guid.NewGuid();
        ctx.Tenants.Add(new Tenant { Id = tenantId, Nome = "T", Plano = plano, Status = StatusTenant.Trial, CriadoEm = DateTime.UtcNow });
        ctx.SaveChanges();

        var tenant = new Mock<ITenantContext>();
        tenant.Setup(t => t.TenantId).Returns(tenantId);
        tenant.Setup(t => t.Plano).Returns(plano);

        // DataJudAdapter: with a short CNJ (< 13 chars), InferirTribunal returns null
        // so ConsultarAsync returns ResultadoVazio() without any HTTP call.
        var dataJud = new DataJudAdapter(
            new HttpClient { BaseAddress = new Uri("http://localhost") },
            new Mock<ILogger<DataJudAdapter>>().Object);

        return (new ProcessoMonitoradoService(ctx, tenant.Object, dataJud), ctx, tenantId);
    }

    [Fact]
    public async Task GetAllAsync_RetornaVazioInicialmente()
    {
        var (svc, _, _) = Build();
        var result = await svc.GetAllAsync();
        Assert.Empty(result);
    }

    [Fact]
    public async Task CreateAsync_ProcessoNaoEncontradoNaDataJud_CriaEntidade()
    {
        var (svc, ctx, tenantId) = Build();
        // CNJ with < 13 chars → DataJud returns "not found" without HTTP call
        var dto = new CreateProcessoMonitoradoDto("12345", "Meu Processo");
        var result = await svc.CreateAsync(dto);

        Assert.False(result.ProcessoEncontrado);
        Assert.Equal("12345", result.NumeroCNJ);
        Assert.Equal("Meu Processo", result.NomeExibicao);
        Assert.Equal(1, ctx.ProcessosMonitorados.Count(p => p.TenantId == tenantId));
    }

    [Fact]
    public async Task CreateAsync_NumeroDuplicado_Throws()
    {
        var (svc, _, _) = Build();
        var dto = new CreateProcessoMonitoradoDto("12345", "Processo 1");
        await svc.CreateAsync(dto);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.CreateAsync(new CreateProcessoMonitoradoDto("12345", "Processo 2")));
    }

    [Fact]
    public async Task CreateAsync_LimiteFree_Throws()
    {
        var (svc, ctx, tenantId) = Build(PlanoTipo.Free);
        // Preenche o limite de 40 do plano Free
        for (var i = 0; i < 40; i++)
        {
            ctx.ProcessosMonitorados.Add(new ProcessoMonitorado
            {
                Id = Guid.NewGuid(), TenantId = tenantId,
                NumeroCNJ = $"{i:D5}", Ativo = true, CriadoEm = DateTime.UtcNow
            });
        }
        await ctx.SaveChangesAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.CreateAsync(new CreateProcessoMonitoradoDto("99999", null)));
    }

    [Fact]
    public async Task GetAllAsync_RetornaProcessosDoTenant()
    {
        var (svc, _, _) = Build();
        await svc.CreateAsync(new CreateProcessoMonitoradoDto("11111", "P1"));
        await svc.CreateAsync(new CreateProcessoMonitoradoDto("22222", "P2"));
        var result = (await svc.GetAllAsync()).ToList();
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public async Task ToggleAtivoAsync_AlternaAtivo()
    {
        var (svc, ctx, _) = Build();
        var created = await svc.CreateAsync(new CreateProcessoMonitoradoDto("11111", "P"));
        Assert.True(created.Ativo);

        await svc.ToggleAtivoAsync(created.Id);
        var entity = await ctx.ProcessosMonitorados.FindAsync(created.Id);
        Assert.False(entity!.Ativo);

        await svc.ToggleAtivoAsync(created.Id);
        await ctx.Entry(entity).ReloadAsync();
        Assert.True(entity.Ativo);
    }

    [Fact]
    public async Task ToggleAtivoAsync_NaoEncontrado_Throws()
    {
        var (svc, _, _) = Build();
        await Assert.ThrowsAsync<KeyNotFoundException>(() => svc.ToggleAtivoAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task DeleteAsync_RemoveEntidade()
    {
        var (svc, ctx, _) = Build();
        var created = await svc.CreateAsync(new CreateProcessoMonitoradoDto("11111", "P"));
        await svc.DeleteAsync(created.Id);
        Assert.Equal(0, ctx.ProcessosMonitorados.Count());
    }

    [Fact]
    public async Task DeleteAsync_NaoEncontrado_Throws()
    {
        var (svc, _, _) = Build();
        await Assert.ThrowsAsync<KeyNotFoundException>(() => svc.DeleteAsync(Guid.NewGuid()));
    }
}
