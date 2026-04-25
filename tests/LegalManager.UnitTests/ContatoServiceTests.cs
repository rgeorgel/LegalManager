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

    [Fact]
    public async Task GetAllAsync_DeveFiltrarPorTipoContato()
    {
        var (ctx, tenant, _) = await SeedTenantAsync();
        ctx.Contatos.AddRange(
            new Contato { Id = Guid.NewGuid(), TenantId = tenant.Id, Nome = "Cliente PF", Tipo = TipoPessoa.PF, TipoContato = TipoContato.Cliente, Ativo = true, CriadoEm = DateTime.UtcNow },
            new Contato { Id = Guid.NewGuid(), TenantId = tenant.Id, Nome = "Parte PF", Tipo = TipoPessoa.PF, TipoContato = TipoContato.ParteContraria, Ativo = true, CriadoEm = DateTime.UtcNow }
        );
        await ctx.SaveChangesAsync();

        var tenantCtx = CreateTenantContext(tenant.Id, Guid.NewGuid());
        var service = new ContatoService(ctx, tenantCtx);

        var result = await service.GetAllAsync(new ContatoFiltroDto(null, TipoContato.Cliente, null, null, null));

        Assert.Equal(1, result.Total);
        Assert.Equal("Cliente PF", result.Items.First().Nome);
    }

    [Fact]
    public async Task GetAllAsync_DeveFiltrarPorTipoPessoa()
    {
        var (ctx, tenant, _) = await SeedTenantAsync();
        ctx.Contatos.AddRange(
            new Contato { Id = Guid.NewGuid(), TenantId = tenant.Id, Nome = "Pessoa Física", Tipo = TipoPessoa.PF, TipoContato = TipoContato.Cliente, Ativo = true, CriadoEm = DateTime.UtcNow },
            new Contato { Id = Guid.NewGuid(), TenantId = tenant.Id, Nome = "Pessoa Jurídica", Tipo = TipoPessoa.PJ, TipoContato = TipoContato.Cliente, Ativo = true, CriadoEm = DateTime.UtcNow }
        );
        await ctx.SaveChangesAsync();

        var tenantCtx = CreateTenantContext(tenant.Id, Guid.NewGuid());
        var service = new ContatoService(ctx, tenantCtx);

        var result = await service.GetAllAsync(new ContatoFiltroDto(null, null, TipoPessoa.PJ, null, null));

        Assert.Equal(1, result.Total);
        Assert.Equal("Pessoa Jurídica", result.Items.First().Nome);
    }

    [Fact]
    public async Task GetAllAsync_DeveFiltrarPorTag()
    {
        var (ctx, tenant, _) = await SeedTenantAsync();
        ctx.Contatos.AddRange(
            new Contato
            {
                Id = Guid.NewGuid(), TenantId = tenant.Id, Nome = "Com Tag VIP", Tipo = TipoPessoa.PF, TipoContato = TipoContato.Cliente,
                Ativo = true, CriadoEm = DateTime.UtcNow,
                Tags = [new ContatoTag { Id = Guid.NewGuid(), Tag = "vip" }]
            },
            new Contato
            {
                Id = Guid.NewGuid(), TenantId = tenant.Id, Nome = "Sem Tag", Tipo = TipoPessoa.PF, TipoContato = TipoContato.Cliente,
                Ativo = true, CriadoEm = DateTime.UtcNow,
                Tags = []
            }
        );
        await ctx.SaveChangesAsync();

        var tenantCtx = CreateTenantContext(tenant.Id, Guid.NewGuid());
        var service = new ContatoService(ctx, tenantCtx);

        var result = await service.GetAllAsync(new ContatoFiltroDto(null, null, null, "vip", null));

        Assert.Equal(1, result.Total);
        Assert.Equal("Com Tag VIP", result.Items.First().Nome);
    }

    [Fact]
    public async Task GetAllAsync_DeveFiltrarPorAtivo()
    {
        var (ctx, tenant, _) = await SeedTenantAsync();
        ctx.Contatos.AddRange(
            new Contato { Id = Guid.NewGuid(), TenantId = tenant.Id, Nome = "Ativo", Tipo = TipoPessoa.PF, TipoContato = TipoContato.Cliente, Ativo = true, CriadoEm = DateTime.UtcNow },
            new Contato { Id = Guid.NewGuid(), TenantId = tenant.Id, Nome = "Inativo", Tipo = TipoPessoa.PF, TipoContato = TipoContato.Cliente, Ativo = false, CriadoEm = DateTime.UtcNow }
        );
        await ctx.SaveChangesAsync();

        var tenantCtx = CreateTenantContext(tenant.Id, Guid.NewGuid());
        var service = new ContatoService(ctx, tenantCtx);

        var result = await service.GetAllAsync(new ContatoFiltroDto(null, null, null, null, true));

        Assert.Equal(1, result.Total);
        Assert.Equal("Ativo", result.Items.First().Nome);
    }

    [Fact]
    public async Task GetAllAsync_DeveSuportarCombinacaoDeFiltros()
    {
        var (ctx, tenant, _) = await SeedTenantAsync();
        ctx.Contatos.AddRange(
            new Contato { Id = Guid.NewGuid(), TenantId = tenant.Id, Nome = "João PF Cliente", Tipo = TipoPessoa.PF, TipoContato = TipoContato.Cliente, Ativo = true, CriadoEm = DateTime.UtcNow },
            new Contato { Id = Guid.NewGuid(), TenantId = tenant.Id, Nome = "Maria PF Cliente", Tipo = TipoPessoa.PF, TipoContato = TipoContato.Cliente, Ativo = true, CriadoEm = DateTime.UtcNow },
            new Contato { Id = Guid.NewGuid(), TenantId = tenant.Id, Nome = "Pedro PJ Cliente", Tipo = TipoPessoa.PJ, TipoContato = TipoContato.Cliente, Ativo = true, CriadoEm = DateTime.UtcNow }
        );
        await ctx.SaveChangesAsync();

        var tenantCtx = CreateTenantContext(tenant.Id, Guid.NewGuid());
        var service = new ContatoService(ctx, tenantCtx);

        var result = await service.GetAllAsync(new ContatoFiltroDto("João", null, TipoPessoa.PF, null, true));

        Assert.Equal(1, result.Total);
        Assert.Equal("João PF Cliente", result.Items.First().Nome);
    }

    [Fact]
    public async Task GetAllAsync_DevePaginarCorretamente()
    {
        var (ctx, tenant, _) = await SeedTenantAsync();
        for (int i = 0; i < 25; i++)
        {
            ctx.Contatos.Add(new Contato
            {
                Id = Guid.NewGuid(), TenantId = tenant.Id, Nome = $"Contato {i:D2}",
                Tipo = TipoPessoa.PF, TipoContato = TipoContato.Cliente, Ativo = true, CriadoEm = DateTime.UtcNow
            });
        }
        await ctx.SaveChangesAsync();

        var tenantCtx = CreateTenantContext(tenant.Id, Guid.NewGuid());
        var service = new ContatoService(ctx, tenantCtx);

        var page1 = await service.GetAllAsync(new ContatoFiltroDto(null, null, null, null, null, 1, 10));
        var page2 = await service.GetAllAsync(new ContatoFiltroDto(null, null, null, null, null, 2, 10));
        var page3 = await service.GetAllAsync(new ContatoFiltroDto(null, null, null, null, null, 3, 10));

        Assert.Equal(10, page1.Items.Count());
        Assert.Equal(10, page2.Items.Count());
        Assert.Equal(5, page3.Items.Count());
        Assert.Equal(25, page1.Total);
    }

    [Fact]
    public async Task CreateAsync_DeveSetarAtivoTrue_PorPadrao()
    {
        var (ctx, tenant, usuario) = await SeedTenantAsync();
        var tenantCtx = CreateTenantContext(tenant.Id, usuario.Id);
        var service = new ContatoService(ctx, tenantCtx);

        var dto = new CreateContatoDto(TipoPessoa.PF, TipoContato.Cliente, "Teste",
            "000.000.000-00", null, "teste@teste.com", null, null, null, null, null, null, null, false, null);

        var result = await service.CreateAsync(dto);

        Assert.True(result.Ativo);
    }

    [Fact]
    public async Task CreateAsync_DeveSuportarMultiplasTags()
    {
        var (ctx, tenant, usuario) = await SeedTenantAsync();
        var tenantCtx = CreateTenantContext(tenant.Id, usuario.Id);
        var service = new ContatoService(ctx, tenantCtx);

        var dto = new CreateContatoDto(TipoPessoa.PF, TipoContato.Cliente, "Teste Tags",
            "000.000.000-00", null, "tags@teste.com", null, null, null, null, null, null, null, false, ["vip", "priority", "follow-up"]);

        var result = await service.CreateAsync(dto);

        Assert.Equal(3, result.Tags.Count);
        Assert.Contains("vip", result.Tags);
        Assert.Contains("priority", result.Tags);
        Assert.Contains("follow-up", result.Tags);
    }

    [Fact]
    public async Task UpdateAsync_DeveLancarKeyNotFoundException_QuandoContatoNaoExiste()
    {
        var (ctx, tenant, _) = await SeedTenantAsync();
        var tenantCtx = CreateTenantContext(tenant.Id, Guid.NewGuid());
        var service = new ContatoService(ctx, tenantCtx);

        var dto = new UpdateContatoDto(TipoPessoa.PF, TipoContato.Cliente, "Teste",
            null, null, null, null, null, null, null, null, null, null, false, null);

        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.UpdateAsync(Guid.NewGuid(), dto));
    }
}
