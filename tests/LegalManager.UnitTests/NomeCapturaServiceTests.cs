using LegalManager.Application.DTOs.Publicacoes;
using LegalManager.Domain.Entities;
using LegalManager.Domain.Enums;
using LegalManager.Domain.Interfaces;
using LegalManager.Infrastructure.Persistence;
using LegalManager.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace LegalManager.UnitTests;

public class NomeCapturaServiceTests
{
    private AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options);
    }

    private ITenantContext CreateTenantContext(Guid tenantId, PlanoTipo plano)
    {
        var mock = new Mock<ITenantContext>();
        mock.Setup(t => t.TenantId).Returns(tenantId);
        mock.Setup(t => t.Plano).Returns(plano);
        return mock.Object;
    }

    private async Task<(AppDbContext ctx, Guid tenantId)> SeedAsync(PlanoTipo plano)
    {
        var ctx = CreateContext();
        var tenantId = Guid.NewGuid();
        ctx.Tenants.Add(new Tenant
        {
            Id = tenantId, Nome = "Test", Plano = plano,
            Status = StatusTenant.Ativo, CriadoEm = DateTime.UtcNow
        });
        await ctx.SaveChangesAsync();
        return (ctx, tenantId);
    }

    [Fact]
    public async Task GetAllAsync_DeveRetornarNomesDoTenant()
    {
        var (ctx, tenantId) = await SeedAsync(PlanoTipo.Pro);
        var outroTenantId = Guid.NewGuid();
        ctx.NomesCaptura.AddRange(
            new NomeCaptura { Id = Guid.NewGuid(), TenantId = tenantId, Nome = "Nome A", Ativo = true, CriadoEm = DateTime.UtcNow },
            new NomeCaptura { Id = Guid.NewGuid(), TenantId = outroTenantId, Nome = "Nome B", Ativo = true, CriadoEm = DateTime.UtcNow }
        );
        await ctx.SaveChangesAsync();

        var service = new NomeCapturaService(ctx, CreateTenantContext(tenantId, PlanoTipo.Pro));
        var result = await service.GetAllAsync();

        Assert.Single(result);
        Assert.Equal("Nome A", result.First().Nome);
    }

    [Fact]
    public async Task CreateAsync_DeveCriarNome_QuandoDentroDoLimite()
    {
        var (ctx, tenantId) = await SeedAsync(PlanoTipo.Pro);
        var service = new NomeCapturaService(ctx, CreateTenantContext(tenantId, PlanoTipo.Pro));

        var result = await service.CreateAsync(new CreateNomeCapturaDto("Empresa ABC"));

        Assert.NotNull(result);
        Assert.Equal("Empresa ABC", result.Nome);
        Assert.True(result.Ativo);
    }

    [Fact]
    public async Task CreateAsync_DeveLancarExcecao_QuandoLimiteAtingido()
    {
        var (ctx, tenantId) = await SeedAsync(PlanoTipo.Pro);
        for (int i = 0; i < 3; i++)
        {
            ctx.NomesCaptura.Add(new NomeCaptura
            {
                Id = Guid.NewGuid(), TenantId = tenantId, Nome = $"Nome {i}",
                Ativo = true, CriadoEm = DateTime.UtcNow
            });
        }
        await ctx.SaveChangesAsync();

        var service = new NomeCapturaService(ctx, CreateTenantContext(tenantId, PlanoTipo.Pro));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CreateAsync(new CreateNomeCapturaDto("Novo Nome")));
    }

    [Fact]
    public async Task CreateAsync_DeveLancarExcecao_QuandoNomeDuplicado()
    {
        var (ctx, tenantId) = await SeedAsync(PlanoTipo.Pro);
        ctx.NomesCaptura.Add(new NomeCaptura
        {
            Id = Guid.NewGuid(), TenantId = tenantId, Nome = "Duplicado",
            Ativo = true, CriadoEm = DateTime.UtcNow
        });
        await ctx.SaveChangesAsync();

        var service = new NomeCapturaService(ctx, CreateTenantContext(tenantId, PlanoTipo.Pro));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CreateAsync(new CreateNomeCapturaDto("Duplicado")));
    }

    [Fact]
    public async Task CreateAsync_DeveLancarExcecao_QuandoNaoDisponivelNoPlanoFree()
    {
        var (ctx, tenantId) = await SeedAsync(PlanoTipo.Free);
        var service = new NomeCapturaService(ctx, CreateTenantContext(tenantId, PlanoTipo.Free));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CreateAsync(new CreateNomeCapturaDto("Nome Qualquer")));
    }

    [Fact]
    public async Task ToggleAtivoAsync_DeveAlternarStatus()
    {
        var (ctx, tenantId) = await SeedAsync(PlanoTipo.Pro);
        var nomeId = Guid.NewGuid();
        ctx.NomesCaptura.Add(new NomeCaptura
        {
            Id = nomeId, TenantId = tenantId, Nome = "Teste", Ativo = true,
            CriadoEm = DateTime.UtcNow
        });
        await ctx.SaveChangesAsync();

        var service = new NomeCapturaService(ctx, CreateTenantContext(tenantId, PlanoTipo.Pro));
        await service.ToggleAtivoAsync(nomeId);

        var updated = await ctx.NomesCaptura.FindAsync(nomeId);
        Assert.False(updated!.Ativo);

        await service.ToggleAtivoAsync(nomeId);
        updated = await ctx.NomesCaptura.FindAsync(nomeId);
        Assert.True(updated.Ativo);
    }

    [Fact]
    public async Task ToggleAtivoAsync_DeveLancarKeyNotFoundException_QuandoNaoExistir()
    {
        var (ctx, tenantId) = await SeedAsync(PlanoTipo.Pro);
        var service = new NomeCapturaService(ctx, CreateTenantContext(tenantId, PlanoTipo.Pro));

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            service.ToggleAtivoAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task DeleteAsync_DeveRemoverNome()
    {
        var (ctx, tenantId) = await SeedAsync(PlanoTipo.Pro);
        var nomeId = Guid.NewGuid();
        ctx.NomesCaptura.Add(new NomeCaptura
        {
            Id = nomeId, TenantId = tenantId, Nome = "Para Deletar",
            Ativo = true, CriadoEm = DateTime.UtcNow
        });
        await ctx.SaveChangesAsync();

        var service = new NomeCapturaService(ctx, CreateTenantContext(tenantId, PlanoTipo.Pro));
        await service.DeleteAsync(nomeId);

        Assert.Null(await ctx.NomesCaptura.FindAsync(nomeId));
    }

    [Fact]
    public async Task DeleteAsync_DeveLancarKeyNotFoundException_QuandoDeOutroTenant()
    {
        var (ctx, tenantId) = await SeedAsync(PlanoTipo.Pro);
        var outroTenantId = Guid.NewGuid();
        var nomeId = Guid.NewGuid();
        ctx.NomesCaptura.Add(new NomeCaptura
        {
            Id = nomeId, TenantId = outroTenantId, Nome = "Outro Tenant",
            Ativo = true, CriadoEm = DateTime.UtcNow
        });
        await ctx.SaveChangesAsync();

        var service = new NomeCapturaService(ctx, CreateTenantContext(tenantId, PlanoTipo.Pro));

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            service.DeleteAsync(nomeId));
    }
}
