using LegalManager.Application.DTOs.Notificacoes;
using LegalManager.Domain.Entities;
using LegalManager.Domain.Enums;
using LegalManager.Domain.Interfaces;
using LegalManager.Infrastructure.Persistence;
using LegalManager.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace LegalManager.UnitTests;

public class PreferenciasNotificacaoServiceTests
{
    private AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options);
    }

    private async Task<(AppDbContext ctx, Guid tenantId, Guid userId)> SeedAsync()
    {
        var ctx = CreateContext();
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        ctx.Tenants.Add(new Tenant
        {
            Id = tenantId, Nome = "Test", Plano = PlanoTipo.Pro,
            Status = StatusTenant.Ativo, CriadoEm = DateTime.UtcNow
        });
        ctx.Users.Add(new Usuario
        {
            Id = userId, TenantId = tenantId, Nome = "User",
            Email = "user@test.com", UserName = "user@test.com",
            Perfil = PerfilUsuario.Advogado, Ativo = true, CriadoEm = DateTime.UtcNow
        });
        await ctx.SaveChangesAsync();
        return (ctx, tenantId, userId);
    }

    [Fact]
    public async Task GetAsync_DeveCriarPreferencias_QuandoNaoExistirem()
    {
        var (ctx, tenantId, userId) = await SeedAsync();
        var service = new PreferenciasNotificacaoService(ctx);

        var result = await service.GetAsync(tenantId, userId);

        Assert.NotNull(result);
        Assert.True(result.TarefasInApp);
        Assert.True(result.EventosEmail);
        var prefs = await ctx.PreferenciasNotificacoes.FirstOrDefaultAsync();
        Assert.NotNull(prefs);
    }

    [Fact]
    public async Task GetAsync_DeveRetornarPreferenciasExistentes()
    {
        var (ctx, tenantId, userId) = await SeedAsync();
        ctx.PreferenciasNotificacoes.Add(new PreferenciasNotificacao
        {
            Id = Guid.NewGuid(), TenantId = tenantId, UsuarioId = userId,
            TarefasInApp = false, TarefasEmail = true
        });
        await ctx.SaveChangesAsync();

        var service = new PreferenciasNotificacaoService(ctx);
        var result = await service.GetAsync(tenantId, userId);

        Assert.False(result.TarefasInApp);
        Assert.True(result.TarefasEmail);
    }

    [Fact]
    public async Task AtualizarAsync_DeveAtualizarTodosOsCampos()
    {
        var (ctx, tenantId, userId) = await SeedAsync();
        ctx.PreferenciasNotificacoes.Add(new PreferenciasNotificacao
        {
            Id = Guid.NewGuid(), TenantId = tenantId, UsuarioId = userId
        });
        await ctx.SaveChangesAsync();

        var service = new PreferenciasNotificacaoService(ctx);
        var dto = new AtualizarPreferenciasDto(
            false, false, true, false, true, false, false, true, false, false);

        var result = await service.AtualizarAsync(tenantId, userId, dto);

        Assert.False(result.TarefasInApp);
        Assert.False(result.TarefasEmail);
        Assert.True(result.EventosInApp);
    }

    [Fact]
    public async Task PermiteInAppAsync_DeveRetornarCorretamente_PorTipo()
    {
        var (ctx, tenantId, userId) = await SeedAsync();
        ctx.PreferenciasNotificacoes.Add(new PreferenciasNotificacao
        {
            Id = Guid.NewGuid(), TenantId = tenantId, UsuarioId = userId,
            TarefasInApp = true, TarefasEmail = false,
            EventosInApp = false, PrazosInApp = true,
            PublicacoesInApp = false, TrialInApp = true, GeralInApp = false
        });
        await ctx.SaveChangesAsync();

        var service = new PreferenciasNotificacaoService(ctx);

        Assert.True(await service.PermiteInAppAsync(tenantId, userId, "Tarefas"));
        Assert.True(await service.PermiteInAppAsync(tenantId, userId, "PrazoTarefa"));
        Assert.False(await service.PermiteInAppAsync(tenantId, userId, "Eventos"));
        Assert.True(await service.PermiteInAppAsync(tenantId, userId, "Prazos"));
        Assert.False(await service.PermiteInAppAsync(tenantId, userId, "Publicacoes"));
        Assert.True(await service.PermiteInAppAsync(tenantId, userId, "Trial"));
        Assert.False(await service.PermiteInAppAsync(tenantId, userId, "Outro"));
    }

    [Fact]
    public async Task PermiteEmailAsync_DeveRetornarTrue_ParaTrial()
    {
        var (ctx, tenantId, userId) = await SeedAsync();
        ctx.PreferenciasNotificacoes.Add(new PreferenciasNotificacao
        {
            Id = Guid.NewGuid(), TenantId = tenantId, UsuarioId = userId,
            TarefasEmail = false, TrialInApp = false
        });
        await ctx.SaveChangesAsync();

        var service = new PreferenciasNotificacaoService(ctx);

        Assert.True(await service.PermiteEmailAsync(tenantId, userId, "TrialExpirando"));
        Assert.False(await service.PermiteEmailAsync(tenantId, userId, "Tarefas"));
    }

    [Fact]
    public async Task PermiteEmailAsync_DeveRetornarTrue_ComoDefault()
    {
        var (ctx, tenantId, userId) = await SeedAsync();
        ctx.PreferenciasNotificacoes.Add(new PreferenciasNotificacao
        {
            Id = Guid.NewGuid(), TenantId = tenantId, UsuarioId = userId
        });
        await ctx.SaveChangesAsync();

        var service = new PreferenciasNotificacaoService(ctx);

        Assert.True(await service.PermiteEmailAsync(tenantId, userId, "TipoDesconhecido"));
    }
}
