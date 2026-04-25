using LegalManager.Application.Interfaces;
using LegalManager.Domain.Entities;
using LegalManager.Domain.Enums;
using LegalManager.Domain.Interfaces;
using LegalManager.Infrastructure.Persistence;
using LegalManager.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace LegalManager.UnitTests;

public class AuditServiceTests
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
        mock.Setup(t => t.Plano).Returns(PlanoTipo.Free);
        mock.Setup(t => t.UserRole).Returns("Admin");
        return mock.Object;
    }

    private async Task<(AppDbContext ctx, Tenant tenant, Usuario usuario)> SeedTenantAsync()
    {
        var ctx = CreateContext();
        var tenant = new Tenant
        {
            Id = Guid.NewGuid(),
            Nome = "Escritório Audit Test",
            Plano = PlanoTipo.Free,
            Status = StatusTenant.Trial,
            CriadoEm = DateTime.UtcNow
        };
        ctx.Tenants.Add(tenant);

        var usuario = new Usuario
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.Id,
            Nome = "Admin Audit",
            Email = "admin@audit.com",
            UserName = "admin@audit.com",
            Perfil = PerfilUsuario.Admin,
            Ativo = true,
            CriadoEm = DateTime.UtcNow
        };
        ctx.Users.Add(usuario);
        await ctx.SaveChangesAsync();

        return (ctx, tenant, usuario);
    }

    [Fact]
    public async Task LogAsync_DeveCriarAuditLog_QuandoChamado()
    {
        var (ctx, tenant, usuario) = await SeedTenantAsync();
        var tenantCtx = CreateTenantContext(tenant.Id, usuario.Id);
        var service = new AuditService(ctx, tenantCtx);

        var entry = new AuditLogEntry(
            tenant.Id,
            usuario.Id,
            AuditActions.Create,
            AuditEntities.Contato,
            Guid.NewGuid().ToString(),
            new { Nome = "João Silva", Email = "joao@teste.com" },
            null,
            "192.168.1.1"
        );

        await service.LogAsync(entry);

        var log = await ctx.AuditLogs.FirstOrDefaultAsync();
        Assert.NotNull(log);
        Assert.Equal(tenant.Id, log.TenantId);
        Assert.Equal(usuario.Id, log.UsuarioId);
        Assert.Equal(AuditActions.Create, log.Acao);
        Assert.Equal(AuditEntities.Contato, log.Entidade);
        Assert.Equal("192.168.1.1", log.IpAddress);
        Assert.NotNull(log.DadosAnteriores);
    }

    [Fact]
    public async Task LogAsync_DeveSerializarDadosAnterioresENovos_ComoJSON()
    {
        var (ctx, tenant, usuario) = await SeedTenantAsync();
        var tenantCtx = CreateTenantContext(tenant.Id, usuario.Id);
        var service = new AuditService(ctx, tenantCtx);

        var dadosAnteriores = new { Nome = "Antigo", Email = "antigo@teste.com" };
        var dadosNovos = new { Nome = "Novo", Email = "novo@teste.com" };

        var entry = new AuditLogEntry(
            tenant.Id, usuario.Id, AuditActions.Update, AuditEntities.Contato,
            Guid.NewGuid().ToString(), dadosAnteriores, dadosNovos, null
        );

        await service.LogAsync(entry);

        var log = await ctx.AuditLogs.FirstOrDefaultAsync();
        Assert.NotNull(log);
        Assert.Contains("Antigo", log.DadosAnteriores!);
        Assert.Contains("Novo", log.DadosNovos!);
    }

    [Fact]
    public async Task LogAsync_DevePermitirNullParaDadosAnterioresENovos()
    {
        var (ctx, tenant, usuario) = await SeedTenantAsync();
        var tenantCtx = CreateTenantContext(tenant.Id, usuario.Id);
        var service = new AuditService(ctx, tenantCtx);

        var entry = new AuditLogEntry(
            tenant.Id, usuario.Id, AuditActions.Delete, AuditEntities.Processo,
            Guid.NewGuid().ToString(), null, null, null
        );

        await service.LogAsync(entry);

        var log = await ctx.AuditLogs.FirstOrDefaultAsync();
        Assert.NotNull(log);
        Assert.Null(log.DadosAnteriores);
        Assert.Null(log.DadosNovos);
    }

    [Fact]
    public async Task GetByEntityAsync_DeveRetornarLogsDaEntidade_CorroborandoTenant()
    {
        var (ctx, tenant, usuario) = await SeedTenantAsync();
        var tenantCtx = CreateTenantContext(tenant.Id, usuario.Id);
        var service = new AuditService(ctx, tenantCtx);

        var entityId = Guid.NewGuid();

        ctx.AuditLogs.AddRange(
            new AuditLog { Id = Guid.NewGuid(), TenantId = tenant.Id, UsuarioId = usuario.Id, Acao = AuditActions.Create, Entidade = AuditEntities.Contato, EntidadeId = entityId.ToString(), CriadoEm = DateTime.UtcNow },
            new AuditLog { Id = Guid.NewGuid(), TenantId = tenant.Id, UsuarioId = usuario.Id, Acao = AuditActions.Update, Entidade = AuditEntities.Contato, EntidadeId = entityId.ToString(), CriadoEm = DateTime.UtcNow.AddMinutes(1) },
            new AuditLog { Id = Guid.NewGuid(), TenantId = tenant.Id, UsuarioId = usuario.Id, Acao = AuditActions.Create, Entidade = AuditEntities.Processo, EntidadeId = Guid.NewGuid().ToString(), CriadoEm = DateTime.UtcNow }
        );
        await ctx.SaveChangesAsync();

        var result = await service.GetByEntityAsync(AuditEntities.Contato, entityId);

        Assert.Equal(2, result.Count());
        Assert.All(result, r => Assert.Equal(AuditEntities.Contato, r.Entidade));
    }

    [Fact]
    public async Task GetByEntityAsync_DeveFiltrarPorTenant_NaoRetornandoLogsDeOutrosTenants()
    {
        var (ctx, tenant, usuario) = await SeedTenantAsync();
        var tenantCtx = CreateTenantContext(tenant.Id, usuario.Id);
        var service = new AuditService(ctx, tenantCtx);

        var outroTenantId = Guid.NewGuid();
        var entityId = Guid.NewGuid();

        ctx.AuditLogs.AddRange(
            new AuditLog { Id = Guid.NewGuid(), TenantId = tenant.Id, UsuarioId = usuario.Id, Acao = AuditActions.Create, Entidade = AuditEntities.Contato, EntidadeId = entityId.ToString(), CriadoEm = DateTime.UtcNow },
            new AuditLog { Id = Guid.NewGuid(), TenantId = outroTenantId, UsuarioId = usuario.Id, Acao = AuditActions.Create, Entidade = AuditEntities.Contato, EntidadeId = entityId.ToString(), CriadoEm = DateTime.UtcNow }
        );
        await ctx.SaveChangesAsync();

        var result = await service.GetByEntityAsync(AuditEntities.Contato, entityId);

        Assert.Single(result);
    }

    [Fact]
    public async Task GetByTenantAsync_DeveRetornarTodosLogsDoTenant_ComPaginacao()
    {
        var (ctx, tenant, usuario) = await SeedTenantAsync();
        var tenantCtx = CreateTenantContext(tenant.Id, usuario.Id);
        var service = new AuditService(ctx, tenantCtx);

        for (int i = 0; i < 25; i++)
        {
            ctx.AuditLogs.Add(new AuditLog
            {
                Id = Guid.NewGuid(),
                TenantId = tenant.Id,
                UsuarioId = usuario.Id,
                Acao = AuditActions.Create,
                Entidade = AuditEntities.Contato,
                CriadoEm = DateTime.UtcNow.AddMinutes(-i)
            });
        }
        await ctx.SaveChangesAsync();

        var page1 = await service.GetByTenantAsync(null, null, 1, 10);
        var page2 = await service.GetByTenantAsync(null, null, 2, 10);
        var page3 = await service.GetByTenantAsync(null, null, 3, 10);

        Assert.Equal(10, page1.Count());
        Assert.Equal(10, page2.Count());
        Assert.Equal(5, page3.Count());
        Assert.Equal(25, page1.Concat(page2).Concat(page3).Count());
    }

    [Fact]
    public async Task GetByTenantAsync_DeveFiltrarPorDataDeInicio()
    {
        var (ctx, tenant, usuario) = await SeedTenantAsync();
        var tenantCtx = CreateTenantContext(tenant.Id, usuario.Id);
        var service = new AuditService(ctx, tenantCtx);

        var cutoff = DateTime.UtcNow.AddHours(-1);

        ctx.AuditLogs.AddRange(
            new AuditLog { Id = Guid.NewGuid(), TenantId = tenant.Id, Acao = AuditActions.Create, Entidade = AuditEntities.Contato, CriadoEm = DateTime.UtcNow.AddHours(-2) },
            new AuditLog { Id = Guid.NewGuid(), TenantId = tenant.Id, Acao = AuditActions.Create, Entidade = AuditEntities.Processo, CriadoEm = DateTime.UtcNow }
        );
        await ctx.SaveChangesAsync();

        var result = await service.GetByTenantAsync(cutoff, null, 1, 50);

        Assert.Single(result);
    }

    [Fact]
    public async Task GetByTenantAsync_DeveFiltrarPorDataDeFim()
    {
        var (ctx, tenant, usuario) = await SeedTenantAsync();
        var tenantCtx = CreateTenantContext(tenant.Id, usuario.Id);
        var service = new AuditService(ctx, tenantCtx);

        var cutoff = DateTime.UtcNow.AddHours(-1);

        ctx.AuditLogs.AddRange(
            new AuditLog { Id = Guid.NewGuid(), TenantId = tenant.Id, Acao = AuditActions.Create, Entidade = AuditEntities.Contato, CriadoEm = DateTime.UtcNow.AddHours(-2) },
            new AuditLog { Id = Guid.NewGuid(), TenantId = tenant.Id, Acao = AuditActions.Create, Entidade = AuditEntities.Processo, CriadoEm = DateTime.UtcNow }
        );
        await ctx.SaveChangesAsync();

        var result = await service.GetByTenantAsync(null, cutoff, 1, 50);

        Assert.Single(result);
    }

    [Fact]
    public async Task GetByEntityAsync_DeveOrdenarPorDataDecrescente()
    {
        var (ctx, tenant, usuario) = await SeedTenantAsync();
        var tenantCtx = CreateTenantContext(tenant.Id, usuario.Id);
        var service = new AuditService(ctx, tenantCtx);

        var entityId = Guid.NewGuid();

        ctx.AuditLogs.AddRange(
            new AuditLog { Id = Guid.NewGuid(), TenantId = tenant.Id, Acao = AuditActions.Create, Entidade = AuditEntities.Contato, EntidadeId = entityId.ToString(), CriadoEm = DateTime.UtcNow.AddMinutes(-2) },
            new AuditLog { Id = Guid.NewGuid(), TenantId = tenant.Id, Acao = AuditActions.Update, Entidade = AuditEntities.Contato, EntidadeId = entityId.ToString(), CriadoEm = DateTime.UtcNow },
            new AuditLog { Id = Guid.NewGuid(), TenantId = tenant.Id, Acao = AuditActions.Delete, Entidade = AuditEntities.Contato, EntidadeId = entityId.ToString(), CriadoEm = DateTime.UtcNow.AddMinutes(-1) }
        );
        await ctx.SaveChangesAsync();

        var result = (await service.GetByEntityAsync(AuditEntities.Contato, entityId)).ToList();

        Assert.Equal(3, result.Count);
        Assert.Equal(AuditActions.Update, result[0].Acao);
        Assert.Equal(AuditActions.Delete, result[1].Acao);
        Assert.Equal(AuditActions.Create, result[2].Acao);
    }
}

public class AuditExtensionsTests
{
    [Fact]
    public void CreateEntry_DeveRetornarAuditLogEntry_ComDadosCorretos()
    {
        var mockTenant = new Mock<ITenantContext>();
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        mockTenant.Setup(t => t.TenantId).Returns(tenantId);
        mockTenant.Setup(t => t.UserId).Returns(userId);

        var entry = mockTenant.Object.CreateEntry(
            AuditActions.Create,
            AuditEntities.Contato,
            Guid.NewGuid(),
            new { Nome = "Teste" },
            null,
            "127.0.0.1"
        );

        Assert.Equal(tenantId, entry.TenantId);
        Assert.Equal(userId, entry.UsuarioId);
        Assert.Equal(AuditActions.Create, entry.Acao);
        Assert.Equal(AuditEntities.Contato, entry.Entidade);
        Assert.NotNull(entry.DadosAnteriores);
        Assert.Null(entry.DadosNovos);
        Assert.Equal("127.0.0.1", entry.IpAddress);
    }

    [Fact]
    public void CreateEntry_DevePermitirEntityIdNull()
    {
        var mockTenant = new Mock<ITenantContext>();
        mockTenant.Setup(t => t.TenantId).Returns(Guid.NewGuid());
        mockTenant.Setup(t => t.UserId).Returns(Guid.NewGuid());

        var entry = mockTenant.Object.CreateEntry(AuditActions.Login, "Auth");

        Assert.Null(entry.EntidadeId);
    }

    [Fact]
    public void AuditActions_DeveTerConstantesCorretas()
    {
        Assert.Equal("CREATE", AuditActions.Create);
        Assert.Equal("UPDATE", AuditActions.Update);
        Assert.Equal("DELETE", AuditActions.Delete);
        Assert.Equal("LOGIN", AuditActions.Login);
        Assert.Equal("LOGOUT", AuditActions.Logout);
        Assert.Equal("ACCESS", AuditActions.Access);
    }

    [Fact]
    public void AuditEntities_DeveTerConstantesCorretas()
    {
        Assert.Equal("Contato", AuditEntities.Contato);
        Assert.Equal("Processo", AuditEntities.Processo);
        Assert.Equal("Tarefa", AuditEntities.Tarefa);
        Assert.Equal("Evento", AuditEntities.Evento);
        Assert.Equal("LancamentoFinanceiro", AuditEntities.Financeiro);
        Assert.Equal("Usuario", AuditEntities.Usuario);
        Assert.Equal("Documento", AuditEntities.Documento);
        Assert.Equal("Configuracao", AuditEntities.Configuracao);
    }
}