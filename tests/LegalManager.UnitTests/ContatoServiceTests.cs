using LegalManager.Application.DTOs.Contatos;
using LegalManager.Application.DTOs.Honorarios;
using LegalManager.Application.Interfaces;
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

    private static IHonorarioService CreateHonorarioService(AppDbContext ctx) =>
        new HonorarioService(ctx, Mock.Of<IAuditService>());

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
        var service = new ContatoService(ctx, tenantCtx, CreateHonorarioService(ctx));

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
        var service = new ContatoService(ctx, tenantCtx, CreateHonorarioService(ctx));

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
        var service = new ContatoService(ctx, tenantCtx, CreateHonorarioService(ctx));

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
        var service = new ContatoService(ctx, tenantCtx, CreateHonorarioService(ctx));

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
        var service = new ContatoService(ctx, tenantCtx, CreateHonorarioService(ctx));

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
        var service = new ContatoService(ctx, tenantCtx, CreateHonorarioService(ctx));

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
        var service = new ContatoService(ctx, tenantCtx, CreateHonorarioService(ctx));

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
        var service = new ContatoService(ctx, tenantCtx, CreateHonorarioService(ctx));

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
        var service = new ContatoService(ctx, tenantCtx, CreateHonorarioService(ctx));

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
        var service = new ContatoService(ctx, tenantCtx, CreateHonorarioService(ctx));

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
        var service = new ContatoService(ctx, tenantCtx, CreateHonorarioService(ctx));

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
        var service = new ContatoService(ctx, tenantCtx, CreateHonorarioService(ctx));

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
        var service = new ContatoService(ctx, tenantCtx, CreateHonorarioService(ctx));

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
        var service = new ContatoService(ctx, tenantCtx, CreateHonorarioService(ctx));

        var dto = new UpdateContatoDto(TipoPessoa.PF, TipoContato.Cliente, "Teste",
            null, null, null, null, null, null, null, null, null, null, false, null);

        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.UpdateAsync(Guid.NewGuid(), dto));
    }

    private static Contato MakeContato(
        Guid tenantId, string nome, TipoPessoa tipo, TipoContato tipoContato,
        string? cpfCnpj = null, string? email = null, string? telefone = null)
    {
        return new Contato
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Nome = nome,
            Tipo = tipo,
            TipoContato = tipoContato,
            CpfCnpj = cpfCnpj,
            Email = email,
            Telefone = telefone,
            Ativo = true,
            CriadoEm = DateTime.UtcNow
        };
    }

    [Fact]
    public async Task GetAllAsync_SortByNomeAsc_RetornaEmOrdemAlfabetica()
    {
        var (ctx, tenant, _) = await SeedTenantAsync();
        ctx.Contatos.AddRange(
            MakeContato(tenant.Id, "Carlos", TipoPessoa.PF, TipoContato.Cliente),
            MakeContato(tenant.Id, "Ana", TipoPessoa.PF, TipoContato.Cliente),
            MakeContato(tenant.Id, "Beatriz", TipoPessoa.PF, TipoContato.Cliente));
        await ctx.SaveChangesAsync();

        var service = new ContatoService(ctx, CreateTenantContext(tenant.Id, Guid.NewGuid()), CreateHonorarioService(ctx));
        var result = await service.GetAllAsync(new ContatoFiltroDto(null, null, null, null, null, 1, 20, "nome", "asc"));

        Assert.Equal(new[] { "Ana", "Beatriz", "Carlos" }, result.Items.Select(i => i.Nome));
    }

    [Fact]
    public async Task GetAllAsync_SortByNomeDesc_RetornaEmOrdemInversa()
    {
        var (ctx, tenant, _) = await SeedTenantAsync();
        ctx.Contatos.AddRange(
            MakeContato(tenant.Id, "Carlos", TipoPessoa.PF, TipoContato.Cliente),
            MakeContato(tenant.Id, "Ana", TipoPessoa.PF, TipoContato.Cliente));
        await ctx.SaveChangesAsync();

        var service = new ContatoService(ctx, CreateTenantContext(tenant.Id, Guid.NewGuid()), CreateHonorarioService(ctx));
        var result = await service.GetAllAsync(new ContatoFiltroDto(null, null, null, null, null, 1, 20, "nome", "desc"));

        Assert.Equal(new[] { "Carlos", "Ana" }, result.Items.Select(i => i.Nome));
    }

    [Fact]
    public async Task GetAllAsync_SortByEmailAsc_NullNoFim()
    {
        var (ctx, tenant, _) = await SeedTenantAsync();
        ctx.Contatos.AddRange(
            MakeContato(tenant.Id, "SemEmail1", TipoPessoa.PF, TipoContato.Cliente, email: null),
            MakeContato(tenant.Id, "Bruno", TipoPessoa.PF, TipoContato.Cliente, email: "bruno@x.com"),
            MakeContato(tenant.Id, "Ana", TipoPessoa.PF, TipoContato.Cliente, email: "ana@x.com"));
        await ctx.SaveChangesAsync();

        var service = new ContatoService(ctx, CreateTenantContext(tenant.Id, Guid.NewGuid()), CreateHonorarioService(ctx));
        var result = await service.GetAllAsync(new ContatoFiltroDto(null, null, null, null, null, 1, 20, "email", "asc"));

        var nomes = result.Items.Select(i => i.Nome).ToList();
        Assert.Equal("Ana", nomes[0]);
        Assert.Equal("Bruno", nomes[1]);
        Assert.Equal("SemEmail1", nomes[2]);
    }

    [Fact]
    public async Task GetAllAsync_SortByTipoContato_OrdenaPorEnum()
    {
        var (ctx, tenant, _) = await SeedTenantAsync();
        ctx.Contatos.AddRange(
            MakeContato(tenant.Id, "A-Perito", TipoPessoa.PF, TipoContato.Perito),
            MakeContato(tenant.Id, "A-Cliente", TipoPessoa.PF, TipoContato.Cliente),
            MakeContato(tenant.Id, "A-Testemunha", TipoPessoa.PF, TipoContato.Testemunha));
        await ctx.SaveChangesAsync();

        var service = new ContatoService(ctx, CreateTenantContext(tenant.Id, Guid.NewGuid()), CreateHonorarioService(ctx));
        var result = await service.GetAllAsync(new ContatoFiltroDto(null, null, null, null, null, 1, 20, "tipoContato", "asc"));

        Assert.Equal(TipoContato.Cliente, result.Items.First().TipoContato);
        Assert.Equal(TipoContato.Perito, result.Items.Last().TipoContato);
    }

    [Fact]
    public async Task GetAllAsync_SortInvalido_CaiNoDefaultNomeAsc()
    {
        var (ctx, tenant, _) = await SeedTenantAsync();
        ctx.Contatos.AddRange(
            MakeContato(tenant.Id, "Carlos", TipoPessoa.PF, TipoContato.Cliente),
            MakeContato(tenant.Id, "Ana", TipoPessoa.PF, TipoContato.Cliente));
        await ctx.SaveChangesAsync();

        var service = new ContatoService(ctx, CreateTenantContext(tenant.Id, Guid.NewGuid()), CreateHonorarioService(ctx));
        var result = await service.GetAllAsync(new ContatoFiltroDto(null, null, null, null, null, 1, 20, "campo-inexistente", "asc"));

        Assert.Equal(new[] { "Ana", "Carlos" }, result.Items.Select(i => i.Nome));
    }

    [Fact]
    public async Task GetAllAsync_SortByCpfCnpj_NullsOrdenadosComoVazio()
    {
        var (ctx, tenant, _) = await SeedTenantAsync();
        ctx.Contatos.AddRange(
            MakeContato(tenant.Id, "SemDoc", TipoPessoa.PF, TipoContato.Cliente, cpfCnpj: null),
            MakeContato(tenant.Id, "ComDoc", TipoPessoa.PF, TipoContato.Cliente, cpfCnpj: "999"));
        await ctx.SaveChangesAsync();

        var service = new ContatoService(ctx, CreateTenantContext(tenant.Id, Guid.NewGuid()), CreateHonorarioService(ctx));
        var result = await service.GetAllAsync(new ContatoFiltroDto(null, null, null, null, null, 1, 20, "cpfCnpj", "asc"));

        Assert.Equal("ComDoc", result.Items.First().Nome);
        Assert.Equal("SemDoc", result.Items.Last().Nome);
    }

    [Fact]
    public async Task GetAllAsync_DefaultSemSort_RetornaPorNomeAsc()
    {
        var (ctx, tenant, _) = await SeedTenantAsync();
        ctx.Contatos.AddRange(
            MakeContato(tenant.Id, "Zelia", TipoPessoa.PF, TipoContato.Cliente),
            MakeContato(tenant.Id, "Alberto", TipoPessoa.PF, TipoContato.Cliente));
        await ctx.SaveChangesAsync();

        var service = new ContatoService(ctx, CreateTenantContext(tenant.Id, Guid.NewGuid()), CreateHonorarioService(ctx));
        var result = await service.GetAllAsync(new ContatoFiltroDto(null, null, null, null, null));

        Assert.Equal(new[] { "Alberto", "Zelia" }, result.Items.Select(i => i.Nome));
    }

    // ── Vínculos entre contatos ──────────────────────────────────────────────

    [Fact]
    public async Task AddVinculoAsync_DeveCriarVinculo_EAparecerParaOsDoisContatos()
    {
        var (ctx, tenant, _) = await SeedTenantAsync();
        var socio1 = MakeContato(tenant.Id, "Sócio 1", TipoPessoa.PF, TipoContato.Cliente);
        var socio2 = MakeContato(tenant.Id, "Sócio 2", TipoPessoa.PF, TipoContato.Cliente);
        ctx.Contatos.AddRange(socio1, socio2);
        await ctx.SaveChangesAsync();

        var service = new ContatoService(ctx, CreateTenantContext(tenant.Id, Guid.NewGuid()), CreateHonorarioService(ctx));
        await service.AddVinculoAsync(socio1.Id, new CreateContatoVinculoDto(socio2.Id, TipoVinculoContato.Socio, "Sócios da mesma empresa"));

        var vinculosDe1 = await service.GetVinculosAsync(socio1.Id);
        var vinculosDe2 = await service.GetVinculosAsync(socio2.Id);

        Assert.Single(vinculosDe1);
        Assert.Equal("Sócio 2", vinculosDe1.First().ContatoRelacionadoNome);
        Assert.Single(vinculosDe2);
        Assert.Equal("Sócio 1", vinculosDe2.First().ContatoRelacionadoNome);
    }

    [Fact]
    public async Task AddVinculoAsync_DeveLancarExcecao_QuandoContatoVinculadoASiMesmo()
    {
        var (ctx, tenant, _) = await SeedTenantAsync();
        var contato = MakeContato(tenant.Id, "Sozinho", TipoPessoa.PF, TipoContato.Cliente);
        ctx.Contatos.Add(contato);
        await ctx.SaveChangesAsync();

        var service = new ContatoService(ctx, CreateTenantContext(tenant.Id, Guid.NewGuid()), CreateHonorarioService(ctx));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.AddVinculoAsync(contato.Id, new CreateContatoVinculoDto(contato.Id, TipoVinculoContato.Outro, null)));
    }

    [Fact]
    public async Task RemoveVinculoAsync_DeveRemoverVinculo()
    {
        var (ctx, tenant, _) = await SeedTenantAsync();
        var c1 = MakeContato(tenant.Id, "C1", TipoPessoa.PF, TipoContato.Cliente);
        var c2 = MakeContato(tenant.Id, "C2", TipoPessoa.PF, TipoContato.Cliente);
        ctx.Contatos.AddRange(c1, c2);
        await ctx.SaveChangesAsync();

        var service = new ContatoService(ctx, CreateTenantContext(tenant.Id, Guid.NewGuid()), CreateHonorarioService(ctx));
        var vinculo = await service.AddVinculoAsync(c1.Id, new CreateContatoVinculoDto(c2.Id, TipoVinculoContato.Indicacao, null));

        await service.RemoveVinculoAsync(c1.Id, vinculo.Id);

        Assert.Empty(await service.GetVinculosAsync(c1.Id));
    }

    // ── Detecção de duplicados ────────────────────────────────────────────────

    [Fact]
    public async Task GetDuplicadosAsync_DeveAgruparPorCpfCnpjEEmail()
    {
        var (ctx, tenant, _) = await SeedTenantAsync();
        ctx.Contatos.AddRange(
            MakeContato(tenant.Id, "João Silva", TipoPessoa.PF, TipoContato.Cliente, cpfCnpj: "123.456.789-00", email: "joao@x.com"),
            MakeContato(tenant.Id, "João S.", TipoPessoa.PF, TipoContato.Cliente, cpfCnpj: "12345678900", email: "outro@x.com"),
            MakeContato(tenant.Id, "Maria", TipoPessoa.PF, TipoContato.Cliente, cpfCnpj: "999", email: "joao@x.com"),
            MakeContato(tenant.Id, "Único", TipoPessoa.PF, TipoContato.Cliente, cpfCnpj: "111", email: "unico@x.com")
        );
        await ctx.SaveChangesAsync();

        var service = new ContatoService(ctx, CreateTenantContext(tenant.Id, Guid.NewGuid()), CreateHonorarioService(ctx));
        var grupos = (await service.GetDuplicadosAsync()).ToList();

        Assert.Contains(grupos, g => g.Criterio == "CpfCnpj" && g.Contatos.Count == 2);
        Assert.Contains(grupos, g => g.Criterio == "Email" && g.Contatos.Count == 2);
    }

    [Fact]
    public async Task GetDuplicadosAsync_NaoDeveConsiderarContatosInativos()
    {
        var (ctx, tenant, _) = await SeedTenantAsync();
        var ativo = MakeContato(tenant.Id, "Ativo", TipoPessoa.PF, TipoContato.Cliente, email: "dup@x.com");
        var inativo = MakeContato(tenant.Id, "Inativo", TipoPessoa.PF, TipoContato.Cliente, email: "dup@x.com");
        inativo.Ativo = false;
        ctx.Contatos.AddRange(ativo, inativo);
        await ctx.SaveChangesAsync();

        var service = new ContatoService(ctx, CreateTenantContext(tenant.Id, Guid.NewGuid()), CreateHonorarioService(ctx));
        var grupos = await service.GetDuplicadosAsync();

        Assert.Empty(grupos);
    }

    // ── Aniversariantes ───────────────────────────────────────────────────────

    [Fact]
    public async Task GetAniversariantesAsync_DeveFiltrarPorMes_EOrdenarPorDia()
    {
        var (ctx, tenant, _) = await SeedTenantAsync();
        var c1 = MakeContato(tenant.Id, "Nasceu dia 20", TipoPessoa.PF, TipoContato.Cliente);
        c1.DataNascimento = new DateTime(1990, 5, 20);
        var c2 = MakeContato(tenant.Id, "Nasceu dia 5", TipoPessoa.PF, TipoContato.Cliente);
        c2.DataNascimento = new DateTime(1985, 5, 5);
        var c3 = MakeContato(tenant.Id, "Outro mês", TipoPessoa.PF, TipoContato.Cliente);
        c3.DataNascimento = new DateTime(1990, 8, 1);
        ctx.Contatos.AddRange(c1, c2, c3);
        await ctx.SaveChangesAsync();

        var service = new ContatoService(ctx, CreateTenantContext(tenant.Id, Guid.NewGuid()), CreateHonorarioService(ctx));
        var result = (await service.GetAniversariantesAsync(5)).ToList();

        Assert.Equal(2, result.Count);
        Assert.Equal("Nasceu dia 5", result[0].Nome);
        Assert.Equal("Nasceu dia 20", result[1].Nome);
    }

    // ── Filtros salvos ────────────────────────────────────────────────────────

    [Fact]
    public async Task FiltroSalvo_CrudCompleto()
    {
        var (ctx, tenant, usuario) = await SeedTenantAsync();
        var service = new ContatoService(ctx, CreateTenantContext(tenant.Id, usuario.Id), CreateHonorarioService(ctx));

        var criado = await service.AddFiltroSalvoAsync(new CreateContatoFiltroSalvoDto("VIPs inadimplentes", "vip", TipoContato.Cliente, null, null));
        Assert.Equal("VIPs inadimplentes", criado.Nome);

        var listados = await service.GetFiltrosSalvosAsync();
        Assert.Single(listados);

        await service.RemoveFiltroSalvoAsync(criado.Id);
        Assert.Empty(await service.GetFiltrosSalvosAsync());
    }

    [Fact]
    public async Task GetFiltrosSalvosAsync_NaoDeveRetornarFiltrosDeOutroUsuario()
    {
        var (ctx, tenant, usuario) = await SeedTenantAsync();
        var outroUsuarioId = Guid.NewGuid();
        var serviceOutro = new ContatoService(ctx, CreateTenantContext(tenant.Id, outroUsuarioId), CreateHonorarioService(ctx));
        await serviceOutro.AddFiltroSalvoAsync(new CreateContatoFiltroSalvoDto("Filtro de outro usuário", null, null, null, null));

        var service = new ContatoService(ctx, CreateTenantContext(tenant.Id, usuario.Id), CreateHonorarioService(ctx));
        Assert.Empty(await service.GetFiltrosSalvosAsync());
    }

    // ── Saldo financeiro (perfil) ────────────────────────────────────────────

    [Fact]
    public async Task GetPerfilAsync_SaldoFinanceiro_IncluiParcelasDeHonorariosPendentes()
    {
        // Regressão: parcelas de contrato de honorários nunca viram LancamentoFinanceiro
        // enquanto não são pagas, então o saldo não pode depender só dessa tabela.
        var (ctx, tenant, usuario) = await SeedTenantAsync();
        var tenantCtx = CreateTenantContext(tenant.Id, usuario.Id);
        var honorarioService = CreateHonorarioService(ctx);
        var service = new ContatoService(ctx, tenantCtx, honorarioService);

        var contato = await service.CreateAsync(new CreateContatoDto(TipoPessoa.PF, TipoContato.Cliente, "Cliente Honorario",
            null, null, null, null, null, null, null, null, null, null, false, null));

        await honorarioService.CriarAsync(tenant.Id, usuario.Id, new CriarContratoHonorarioDto(
            contato.Id, null, null, null, 5000m, FormaPagamentoContrato.AVista, null, null,
            DateTime.UtcNow.AddDays(10), null, null, null, null, "Fixo", null, DateTime.UtcNow, null));

        var perfil = await service.GetPerfilAsync(contato.Id);

        Assert.Equal(5000m, perfil.Resumo.SaldoFinanceiro);
    }

    [Fact]
    public async Task GetPerfilAsync_SaldoFinanceiro_SomaLancamentosAvulsosEHonorarios()
    {
        var (ctx, tenant, usuario) = await SeedTenantAsync();
        var tenantCtx = CreateTenantContext(tenant.Id, usuario.Id);
        var honorarioService = CreateHonorarioService(ctx);
        var service = new ContatoService(ctx, tenantCtx, honorarioService);

        var contato = await service.CreateAsync(new CreateContatoDto(TipoPessoa.PF, TipoContato.Cliente, "Cliente Misto",
            null, null, null, null, null, null, null, null, null, null, false, null));

        ctx.LancamentosFinanceiros.Add(new LancamentoFinanceiro
        {
            Id = Guid.NewGuid(), TenantId = tenant.Id, ContatoId = contato.Id,
            Tipo = TipoLancamento.Despesa, Categoria = "Reembolso", Valor = 300m,
            DataVencimento = DateTime.UtcNow, Status = StatusLancamento.Pendente, CriadoEm = DateTime.UtcNow
        });
        await ctx.SaveChangesAsync();

        await honorarioService.CriarAsync(tenant.Id, usuario.Id, new CriarContratoHonorarioDto(
            contato.Id, null, null, null, 2000m, FormaPagamentoContrato.AVista, null, null,
            DateTime.UtcNow.AddDays(10), null, null, null, null, "Fixo", null, DateTime.UtcNow, null));

        var perfil = await service.GetPerfilAsync(contato.Id);

        // 2000 (honorário pendente) - 300 (despesa pendente) = 1700
        Assert.Equal(1700m, perfil.Resumo.SaldoFinanceiro);
    }

    [Fact]
    public async Task GetPerfilAsync_SaldoFinanceiro_IgnoraContratosEncerrados()
    {
        var (ctx, tenant, usuario) = await SeedTenantAsync();
        var tenantCtx = CreateTenantContext(tenant.Id, usuario.Id);
        var honorarioService = CreateHonorarioService(ctx);
        var service = new ContatoService(ctx, tenantCtx, honorarioService);

        var contato = await service.CreateAsync(new CreateContatoDto(TipoPessoa.PF, TipoContato.Cliente, "Cliente Encerrado",
            null, null, null, null, null, null, null, null, null, null, false, null));

        var ativo = await honorarioService.CriarAsync(tenant.Id, usuario.Id, new CriarContratoHonorarioDto(
            contato.Id, null, null, null, 5000m, FormaPagamentoContrato.AVista, null, null,
            DateTime.UtcNow.AddDays(10), null, null, null, null, "Fixo", null, DateTime.UtcNow, null));

        var encerrado = await honorarioService.CriarAsync(tenant.Id, usuario.Id, new CriarContratoHonorarioDto(
            contato.Id, null, null, null, 6200m, FormaPagamentoContrato.AVista, null, null,
            DateTime.UtcNow.AddDays(10), null, null, null, null, "Fixo", null, DateTime.UtcNow, null));
        await honorarioService.ExcluirAsync(encerrado.Id, tenant.Id, usuario.Id);

        var perfil = await service.GetPerfilAsync(contato.Id);

        // Só o contrato ativo (5000) conta; o encerrado (6200) fica de fora.
        Assert.Equal(5000m, perfil.Resumo.SaldoFinanceiro);
    }

    // ── Links da timeline respeitam o plano do tenant ───────────────────────

    [Fact]
    public async Task GetPerfilAsync_TenantSemAcessoAHonorarios_NaoGeraLinkParaContrato()
    {
        // SeedTenantAsync cria um tenant Free — Honorários é recurso pago (Plus+). O link não
        // pode apontar pra uma tela que o usuário não tem acesso (cai em tela vazia/402).
        var (ctx, tenant, usuario) = await SeedTenantAsync();
        var tenantCtx = CreateTenantContext(tenant.Id, usuario.Id);
        var honorarioService = CreateHonorarioService(ctx);
        var service = new ContatoService(ctx, tenantCtx, honorarioService);

        var contato = await service.CreateAsync(new CreateContatoDto(TipoPessoa.PF, TipoContato.Cliente, "Cliente Free",
            null, null, null, null, null, null, null, null, null, null, false, null));

        var contrato = await honorarioService.CriarAsync(tenant.Id, usuario.Id, new CriarContratoHonorarioDto(
            contato.Id, null, null, null, 1000m, FormaPagamentoContrato.AVista, null, null,
            DateTime.UtcNow.AddDays(10), null, null, null, null, "Fixo", null, DateTime.UtcNow, null));
        var parcelas = await honorarioService.ListarParcelasAsync(contrato.Id, tenant.Id);
        var parcela = parcelas.Parcelas.Single();
        await honorarioService.QuitarParcelaAsync(contrato.Id, parcela.Id, tenant.Id,
            new QuitarParcelaDto(DateTime.UtcNow, 1000m, null));

        var perfil = await service.GetPerfilAsync(contato.Id);

        var itemFinanceiro = Assert.Single(perfil.Timeline, i => i.Tipo == "Financeiro");
        Assert.Null(itemFinanceiro.Link);
    }
}
