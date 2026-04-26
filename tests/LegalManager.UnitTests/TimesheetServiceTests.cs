using LegalManager.Application.DTOs.Timesheet;
using LegalManager.Domain.Entities;
using LegalManager.Domain.Enums;
using LegalManager.Domain.Interfaces;
using LegalManager.Infrastructure.Persistence;
using LegalManager.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace LegalManager.UnitTests;

public class TimesheetServiceTests
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

    private async Task<(AppDbContext ctx, Guid tenantId, Guid usuarioId)> SeedAsync()
    {
        var ctx = CreateContext();
        var tenantId = Guid.NewGuid();
        var usuarioId = Guid.NewGuid();
        ctx.Tenants.Add(new Tenant
        {
            Id = tenantId, Nome = "Test", Plano = PlanoTipo.Pro,
            Status = StatusTenant.Ativo, CriadoEm = DateTime.UtcNow
        });
        ctx.Users.Add(new Usuario
        {
            Id = usuarioId, TenantId = tenantId, Nome = "Advogado",
            Email = "adv@test.com", UserName = "adv@test.com",
            Perfil = PerfilUsuario.Advogado, Ativo = true, CriadoEm = DateTime.UtcNow
        });
        await ctx.SaveChangesAsync();
        return (ctx, tenantId, usuarioId);
    }

    [Fact]
    public async Task IniciarCronometroAsync_DeveCriarRegistroEmAndamento()
    {
        var (ctx, tenantId, usuarioId) = await SeedAsync();
        var service = new TimesheetService(ctx);

        var dto = new IniciarRegistroDto("Trabalho em petição", null, null);
        var result = await service.IniciarCronometroAsync(tenantId, usuarioId, dto);

        Assert.True(result.EmAndamento);
        Assert.Equal("Trabalho em petição", result.Descricao);
    }

    [Fact]
    public async Task IniciarCronometroAsync_DeveLancarExcecao_QuandoCronometroJaAtivo()
    {
        var (ctx, tenantId, usuarioId) = await SeedAsync();
        ctx.RegistrosTempo.Add(new RegistroTempo
        {
            Id = Guid.NewGuid(), TenantId = tenantId, UsuarioId = usuarioId,
            Inicio = DateTime.UtcNow, Descricao = "Em andamento",
            EmAndamento = true, CriadoEm = DateTime.UtcNow
        });
        await ctx.SaveChangesAsync();

        var service = new TimesheetService(ctx);
        var dto = new IniciarRegistroDto("Novo trabalho", null, null);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.IniciarCronometroAsync(tenantId, usuarioId, dto));
    }

    [Fact]
    public async Task PararCronometroAsync_DeveCalcularDuracao()
    {
        var (ctx, tenantId, usuarioId) = await SeedAsync();
        var inicio = DateTime.UtcNow.AddMinutes(-30);
        ctx.RegistrosTempo.Add(new RegistroTempo
        {
            Id = Guid.NewGuid(), TenantId = tenantId, UsuarioId = usuarioId,
            Inicio = inicio, EmAndamento = true, Descricao = "Trabalhando",
            CriadoEm = DateTime.UtcNow
        });
        await ctx.SaveChangesAsync();

        var service = new TimesheetService(ctx);
        var result = await service.PararCronometroAsync(tenantId, usuarioId, new PararRegistroDto(null));

        Assert.False(result.EmAndamento);
        Assert.NotNull(result.Fim);
        Assert.True(result.DuracaoMinutos >= 29);
    }

    [Fact]
    public async Task PararCronometroAsync_DeveLancarExcecao_QuandoNenhumCronometroAtivo()
    {
        var (ctx, tenantId, usuarioId) = await SeedAsync();
        var service = new TimesheetService(ctx);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.PararCronometroAsync(tenantId, usuarioId, new PararRegistroDto(null)));
    }

    [Fact]
    public async Task CriarManualAsync_DeveCriarRegistro()
    {
        var (ctx, tenantId, usuarioId) = await SeedAsync();
        var service = new TimesheetService(ctx);
        var inicio = DateTime.UtcNow.AddHours(-2);
        var fim = DateTime.UtcNow;

        var dto = new CriarRegistroManualDto(inicio, fim, "Registro manual", null, null);
        var result = await service.CriarManualAsync(tenantId, usuarioId, dto);

        Assert.False(result.EmAndamento);
        Assert.Equal(120, result.DuracaoMinutos);
    }

    [Fact]
    public async Task CriarManualAsync_DeveLancarExcecao_QuandoFimAntesDoInicio()
    {
        var (ctx, tenantId, usuarioId) = await SeedAsync();
        var service = new TimesheetService(ctx);
        var inicio = DateTime.UtcNow;
        var fim = DateTime.UtcNow.AddHours(-1);

        var dto = new CriarRegistroManualDto(inicio, fim, "Inválido", null, null);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.CriarManualAsync(tenantId, usuarioId, dto));
    }

    [Fact]
    public async Task GetAllAsync_DeveFiltrarPorUsuario()
    {
        var (ctx, tenantId, usuarioId) = await SeedAsync();
        var outroUserId = Guid.NewGuid();
        ctx.RegistrosTempo.AddRange(
            new RegistroTempo { Id = Guid.NewGuid(), TenantId = tenantId, UsuarioId = usuarioId, Inicio = DateTime.UtcNow, Fim = DateTime.UtcNow.AddHours(1), DuracaoMinutos = 60, EmAndamento = false, CriadoEm = DateTime.UtcNow },
            new RegistroTempo { Id = Guid.NewGuid(), TenantId = tenantId, UsuarioId = outroUserId, Inicio = DateTime.UtcNow, Fim = DateTime.UtcNow.AddHours(2), DuracaoMinutos = 120, EmAndamento = false, CriadoEm = DateTime.UtcNow }
        );
        await ctx.SaveChangesAsync();

        var service = new TimesheetService(ctx);
        var result = await service.GetAllAsync(tenantId, usuarioId, null, null, null, 1, 10);

        Assert.Single(result.Items);
    }

    [Fact]
    public async Task GetCronometroAtivoAsync_DeveRetornarCronometroEmAndamento()
    {
        var (ctx, tenantId, usuarioId) = await SeedAsync();
        ctx.RegistrosTempo.Add(new RegistroTempo
        {
            Id = Guid.NewGuid(), TenantId = tenantId, UsuarioId = usuarioId,
            Inicio = DateTime.UtcNow.AddMinutes(-10), EmAndamento = true,
            Descricao = "Cronometro ativo", CriadoEm = DateTime.UtcNow
        });
        await ctx.SaveChangesAsync();

        var service = new TimesheetService(ctx);
        var result = await service.GetCronometroAtivoAsync(tenantId, usuarioId);

        Assert.NotNull(result);
        Assert.True(result.EmAndamento);
    }

    [Fact]
    public async Task GetCronometroAtivoAsync_DeveRetornarNull_QuandoNenhumAtivo()
    {
        var (ctx, tenantId, usuarioId) = await SeedAsync();
        var service = new TimesheetService(ctx);

        var result = await service.GetCronometroAtivoAsync(tenantId, usuarioId);

        Assert.Null(result);
    }

    [Fact]
    public async Task AtualizarAsync_DeveAtualizarCampos()
    {
        var (ctx, tenantId, _) = await SeedAsync();
        var regId = Guid.NewGuid();
        ctx.RegistrosTempo.Add(new RegistroTempo
        {
            Id = regId, TenantId = tenantId, UsuarioId = Guid.NewGuid(),
            Inicio = DateTime.UtcNow, Descricao = "Original",
            EmAndamento = false, CriadoEm = DateTime.UtcNow
        });
        await ctx.SaveChangesAsync();

        var service = new TimesheetService(ctx);
        var dto = new AtualizarRegistroDto("Atualizado", null, null);
        await service.AtualizarAsync(regId, tenantId, dto);

        var updated = await ctx.RegistrosTempo.FindAsync(regId);
        Assert.Equal("Atualizado", updated!.Descricao);
    }

    [Fact]
    public async Task DeletarAsync_DeveRemoverRegistro()
    {
        var (ctx, tenantId, _) = await SeedAsync();
        var regId = Guid.NewGuid();
        ctx.RegistrosTempo.Add(new RegistroTempo
        {
            Id = regId, TenantId = tenantId, UsuarioId = Guid.NewGuid(),
            Inicio = DateTime.UtcNow, EmAndamento = false, CriadoEm = DateTime.UtcNow
        });
        await ctx.SaveChangesAsync();

        var service = new TimesheetService(ctx);
        await service.DeletarAsync(regId, tenantId);

        Assert.Null(await ctx.RegistrosTempo.FindAsync(regId));
    }

    [Fact]
    public async Task GetAllAsync_DevePaginarCorretamente()
    {
        var (ctx, tenantId, _) = await SeedAsync();
        for (int i = 0; i < 25; i++)
        {
            ctx.RegistrosTempo.Add(new RegistroTempo
            {
                Id = Guid.NewGuid(), TenantId = tenantId, UsuarioId = Guid.NewGuid(),
                Inicio = DateTime.UtcNow.AddMinutes(-i * 10), Fim = DateTime.UtcNow.AddMinutes(-i * 10 + 30),
                DuracaoMinutos = 30, EmAndamento = false, CriadoEm = DateTime.UtcNow
            });
        }
        await ctx.SaveChangesAsync();

        var service = new TimesheetService(ctx);
        var page1 = await service.GetAllAsync(tenantId, null, null, null, null, 1, 10);
        var page3 = await service.GetAllAsync(tenantId, null, null, null, null, 3, 10);

        Assert.Equal(10, page1.Items.Count());
        Assert.Equal(5, page3.Items.Count());
        Assert.Equal(25, page1.Total);
    }
}
