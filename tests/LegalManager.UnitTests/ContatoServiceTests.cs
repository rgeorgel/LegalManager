using LegalManager.Application.DTOs.Contatos;
using LegalManager.Domain.Entities;
using LegalManager.Domain.Enums;
using LegalManager.Domain.Interfaces;
using LegalManager.Infrastructure.Persistence;
using LegalManager.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace LegalManager.UnitTests;

public class ContatoServiceTests
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

    private async Task<(AppDbContext ctx, Tenant tenant, Usuario usuario)> SeedTenantAsync()
    {
        var ctx = CreateContext();
        var tenant = new Tenant
        {
            Id = Guid.NewGuid(),
            Nome = "Escritório Teste",
            Plano = PlanoTipo.Free,
            Status = StatusTenant.Trial,
            CriadoEm = DateTime.UtcNow
        };
        ctx.Tenants.Add(tenant);

        var usuario = new Usuario
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.Id,
            Nome = "Admin Teste",
            Email = "admin@teste.com",
            UserName = "admin@teste.com",
            Perfil = PerfilUsuario.Admin,
            Ativo = true,
            CriadoEm = DateTime.UtcNow
        };
        ctx.Users.Add(usuario);
        await ctx.SaveChangesAsync();

        return (ctx, tenant, usuario);
    }

    [Fact]
    public async Task CreateAsync_DeveRetornarContato_QuandoDadosValidos()
    {
        var (ctx, tenant, usuario) = await SeedTenantAsync();
        var tenantCtx = CreateTenantContext(tenant.Id, usuario.Id);
        var service = new ContatoService(ctx, tenantCtx);

        var dto = new CreateContatoDto(TipoPessoa.PF, TipoContato.Cliente, "João da Silva",
            "123.456.789-00", null, "joao@teste.com", null, null, null, null, null, null, null, false, ["vip"]);

        var result = await service.CreateAsync(dto);

        Assert.NotNull(result);
        Assert.Equal("João da Silva", result.Nome);
        Assert.Equal(TipoPessoa.PF, result.Tipo);
        Assert.Contains("vip", result.Tags);
    }

    [Fact]
    public async Task GetAllAsync_DeveRetornarApenasDadosDoTenant()
    {
        var (ctx, tenant, _) = await SeedTenantAsync();

        var outroTenant = new Tenant { Id = Guid.NewGuid(), Nome = "Outro", Plano = PlanoTipo.Free, Status = StatusTenant.Trial, CriadoEm = DateTime.UtcNow };
        ctx.Tenants.Add(outroTenant);

        ctx.Contatos.AddRange(
            new Contato { Id = Guid.NewGuid(), TenantId = tenant.Id, Nome = "Meu Contato", Tipo = TipoPessoa.PF, TipoContato = TipoContato.Cliente, Ativo = true, CriadoEm = DateTime.UtcNow },
            new Contato { Id = Guid.NewGuid(), TenantId = outroTenant.Id, Nome = "Contato Outro", Tipo = TipoPessoa.PF, TipoContato = TipoContato.Cliente, Ativo = true, CriadoEm = DateTime.UtcNow }
        );
        await ctx.SaveChangesAsync();

        var tenantCtx = CreateTenantContext(tenant.Id, Guid.NewGuid());
        var service = new ContatoService(ctx, tenantCtx);

        var result = await service.GetAllAsync(new ContatoFiltroDto(null, null, null, null, null));

        Assert.Equal(1, result.Total);
        Assert.Equal("Meu Contato", result.Items.First().Nome);
    }

    [Fact]
    public async Task UpdateAsync_DeveLancarExcecao_QuandoContatoNaoPertenceAoTenant()
    {
        var (ctx, tenant, _) = await SeedTenantAsync();
        var outroTenant = new Tenant { Id = Guid.NewGuid(), Nome = "Outro", Plano = PlanoTipo.Free, Status = StatusTenant.Trial, CriadoEm = DateTime.UtcNow };
        ctx.Tenants.Add(outroTenant);

        var contato = new Contato
        {
            Id = Guid.NewGuid(),
            TenantId = outroTenant.Id,
            Nome = "Contato Alheio",
            Tipo = TipoPessoa.PF,
            TipoContato = TipoContato.Cliente,
            Ativo = true,
            CriadoEm = DateTime.UtcNow
        };
        ctx.Contatos.Add(contato);
        await ctx.SaveChangesAsync();

        var tenantCtx = CreateTenantContext(tenant.Id, Guid.NewGuid());
        var service = new ContatoService(ctx, tenantCtx);

        var dto = new UpdateContatoDto(TipoPessoa.PF, TipoContato.Cliente, "Novo Nome",
            null, null, null, null, null, null, null, null, null, null, false, null);

        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.UpdateAsync(contato.Id, dto));
    }

    [Fact]
    public async Task DeleteAsync_DeveDesativarContato_QuandoEncontrado()
    {
        var (ctx, tenant, _) = await SeedTenantAsync();
        var contato = new Contato
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.Id,
            Nome = "Para Deletar",
            Tipo = TipoPessoa.PF,
            TipoContato = TipoContato.Cliente,
            Ativo = true,
            CriadoEm = DateTime.UtcNow
        };
        ctx.Contatos.Add(contato);
        await ctx.SaveChangesAsync();

        var tenantCtx = CreateTenantContext(tenant.Id, Guid.NewGuid());
        var service = new ContatoService(ctx, tenantCtx);

        await service.DeleteAsync(contato.Id);

        var updated = await ctx.Contatos.FindAsync(contato.Id);
        Assert.False(updated!.Ativo);
    }

    [Fact]
    public async Task GetAllAsync_DeveFiltrarPorBusca()
    {
        var (ctx, tenant, _) = await SeedTenantAsync();
        ctx.Contatos.AddRange(
            new Contato { Id = Guid.NewGuid(), TenantId = tenant.Id, Nome = "Maria Santos", Tipo = TipoPessoa.PF, TipoContato = TipoContato.Cliente, Ativo = true, CriadoEm = DateTime.UtcNow },
            new Contato { Id = Guid.NewGuid(), TenantId = tenant.Id, Nome = "Pedro Oliveira", Tipo = TipoPessoa.PF, TipoContato = TipoContato.Cliente, Ativo = true, CriadoEm = DateTime.UtcNow }
        );
        await ctx.SaveChangesAsync();

        var tenantCtx = CreateTenantContext(tenant.Id, Guid.NewGuid());
        var service = new ContatoService(ctx, tenantCtx);

        var result = await service.GetAllAsync(new ContatoFiltroDto("Maria", null, null, null, null));

        Assert.Equal(1, result.Total);
        Assert.Equal("Maria Santos", result.Items.First().Nome);
    }
}
