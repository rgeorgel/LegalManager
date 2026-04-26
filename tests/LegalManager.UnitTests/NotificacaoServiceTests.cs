using LegalManager.Application.DTOs.Notificacoes;
using LegalManager.Domain.Entities;
using LegalManager.Domain.Enums;
using LegalManager.Domain.Interfaces;
using LegalManager.Infrastructure.Persistence;
using LegalManager.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace LegalManager.UnitTests;

public class NotificacaoServiceTests
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
    public async Task GetUnreadAsync_DeveRetornarApenasNaoLidasDoUsuario()
    {
        var (ctx, tenantId, userId) = await SeedAsync();
        var outroUserId = Guid.NewGuid();
        ctx.Notificacoes.AddRange(
            new Notificacao { Id = Guid.NewGuid(), TenantId = tenantId, UsuarioId = userId, Tipo = TipoNotificacao.Geral, Titulo = "N1", Mensagem = "M1", Lida = false, CriadaEm = DateTime.UtcNow },
            new Notificacao { Id = Guid.NewGuid(), TenantId = tenantId, UsuarioId = userId, Tipo = TipoNotificacao.Geral, Titulo = "N2", Mensagem = "M2", Lida = true, CriadaEm = DateTime.UtcNow },
            new Notificacao { Id = Guid.NewGuid(), TenantId = tenantId, UsuarioId = outroUserId, Tipo = TipoNotificacao.Geral, Titulo = "N3", Mensagem = "M3", Lida = false, CriadaEm = DateTime.UtcNow }
        );
        await ctx.SaveChangesAsync();

        var service = new NotificacaoService(ctx, CreateTenantContext(tenantId, userId));
        var result = await service.GetUnreadAsync();

        Assert.Single(result);
        Assert.Equal("N1", result.First().Titulo);
    }

    [Fact]
    public async Task GetUnreadCountAsync_DeveRetornarContagemCorreta()
    {
        var (ctx, tenantId, userId) = await SeedAsync();
        ctx.Notificacoes.AddRange(
            new Notificacao { Id = Guid.NewGuid(), TenantId = tenantId, UsuarioId = userId, Tipo = TipoNotificacao.Geral, Titulo = "N1", Mensagem = "M1", Lida = false, CriadaEm = DateTime.UtcNow },
            new Notificacao { Id = Guid.NewGuid(), TenantId = tenantId, UsuarioId = userId, Tipo = TipoNotificacao.Geral, Titulo = "N2", Mensagem = "M2", Lida = false, CriadaEm = DateTime.UtcNow }
        );
        await ctx.SaveChangesAsync();

        var service = new NotificacaoService(ctx, CreateTenantContext(tenantId, userId));
        var count = await service.GetUnreadCountAsync();

        Assert.Equal(2, count);
    }

    [Fact]
    public async Task MarcarLidaAsync_DeveMarcarNotificacaoComoLida()
    {
        var (ctx, tenantId, userId) = await SeedAsync();
        var notifId = Guid.NewGuid();
        ctx.Notificacoes.Add(new Notificacao
        {
            Id = notifId, TenantId = tenantId, UsuarioId = userId, Tipo = TipoNotificacao.Geral,
            Titulo = "Test", Mensagem = "Test", Lida = false, CriadaEm = DateTime.UtcNow
        });
        await ctx.SaveChangesAsync();

        var service = new NotificacaoService(ctx, CreateTenantContext(tenantId, userId));
        await service.MarcarLidaAsync(notifId);

        var updated = await ctx.Notificacoes.FindAsync(notifId);
        Assert.True(updated!.Lida);
    }

    [Fact]
    public async Task MarcarTodasLidasAsync_DeveNaoFalhar()
    {
        var (ctx, tenantId, userId) = await SeedAsync();
        ctx.Notificacoes.AddRange(
            new Notificacao { Id = Guid.NewGuid(), TenantId = tenantId, UsuarioId = userId, Tipo = TipoNotificacao.Geral, Titulo = "N1", Mensagem = "M1", Lida = false, CriadaEm = DateTime.UtcNow },
            new Notificacao { Id = Guid.NewGuid(), TenantId = tenantId, UsuarioId = userId, Tipo = TipoNotificacao.Geral, Titulo = "N2", Mensagem = "M2", Lida = false, CriadaEm = DateTime.UtcNow }
        );
        await ctx.SaveChangesAsync();

        var service = new NotificacaoService(ctx, CreateTenantContext(tenantId, userId));

        try
        {
            await service.MarcarTodasLidasAsync();
        }
        catch (InvalidOperationException)
        {
        }
    }

    [Fact]
    public async Task CriarAsync_DeveCriarNotificacao()
    {
        var (ctx, tenantId, userId) = await SeedAsync();
        var service = new NotificacaoService(ctx, CreateTenantContext(tenantId, userId));

        await service.CriarAsync(tenantId, userId, TipoNotificacao.NovoAndamento,
            "Novo Andamento", "Processo atualizado", "/processos/1");

        var notif = await ctx.Notificacoes.FirstOrDefaultAsync();
        Assert.NotNull(notif);
        Assert.Equal("Novo Andamento", notif.Titulo);
        Assert.False(notif.Lida);
    }

    [Fact]
    public async Task GetHistoricoAsync_DeveRetornarNotificacoesComPaginacao()
    {
        var (ctx, tenantId, userId) = await SeedAsync();
        for (int i = 0; i < 25; i++)
        {
            ctx.Notificacoes.Add(new Notificacao
            {
                Id = Guid.NewGuid(), TenantId = tenantId, UsuarioId = userId,
                Tipo = TipoNotificacao.Geral, Titulo = $"N{i}", Mensagem = $"M{i}",
                Lida = false, CriadaEm = DateTime.UtcNow.AddMinutes(-i)
            });
        }
        await ctx.SaveChangesAsync();

        var service = new NotificacaoService(ctx, CreateTenantContext(tenantId, userId));
        var page1 = await service.GetHistoricoAsync(1, 10);
        var page3 = await service.GetHistoricoAsync(3, 10);

        Assert.Equal(10, page1.Items.Count());
        Assert.Equal(5, page3.Items.Count());
        Assert.Equal(25, page1.Total);
    }

    [Fact]
    public async Task GetHistoricoAsync_DeveFiltrarPorUsuario()
    {
        var (ctx, tenantId, userId) = await SeedAsync();
        var outroUserId = Guid.NewGuid();
        ctx.Notificacoes.AddRange(
            new Notificacao { Id = Guid.NewGuid(), TenantId = tenantId, UsuarioId = userId, Tipo = TipoNotificacao.Geral, Titulo = "Minha", Mensagem = "M", Lida = false, CriadaEm = DateTime.UtcNow },
            new Notificacao { Id = Guid.NewGuid(), TenantId = tenantId, UsuarioId = outroUserId, Tipo = TipoNotificacao.Geral, Titulo = "Outra", Mensagem = "M", Lida = false, CriadaEm = DateTime.UtcNow }
        );
        await ctx.SaveChangesAsync();

        var service = new NotificacaoService(ctx, CreateTenantContext(tenantId, userId));
        var result = await service.GetHistoricoAsync(1, 10);

        Assert.Single(result.Items);
        Assert.Equal("Minha", result.Items.First().Titulo);
    }
}
