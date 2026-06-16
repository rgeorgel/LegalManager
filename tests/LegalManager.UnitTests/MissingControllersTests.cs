using System.Security.Claims;
using LegalManager.API.Controllers;
using LegalManager.Application.DTOs.Atividades;
using LegalManager.Application.DTOs.Contatos;
using LegalManager.Application.DTOs.IA;
using LegalManager.Application.DTOs.Modelos;
using LegalManager.Application.DTOs.Timesheet;
using LegalManager.Application.Interfaces;
using LegalManager.Domain;
using LegalManager.Domain.Entities;
using LegalManager.Domain.Enums;
using LegalManager.Domain.Interfaces;
using LegalManager.Infrastructure.Persistence;
using LegalManager.Infrastructure.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;

namespace LegalManager.UnitTests;

// ─── UsuariosController ───────────────────────────────────────────────────────

public class UsuariosControllerTests
{
    private static AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options);
    }

    private static Mock<ITenantContext> CreateTenantMock(Guid tenantId, Guid userId)
    {
        var mock = new Mock<ITenantContext>();
        mock.Setup(t => t.TenantId).Returns(tenantId);
        mock.Setup(t => t.UserId).Returns(userId);
        return mock;
    }

    private static Mock<UserManager<Usuario>> CreateUserManagerMock()
    {
        var storeMock = new Mock<IUserStore<Usuario>>();
        var mgr = new Mock<UserManager<Usuario>>(storeMock.Object, null!, null!, null!, null!, null!, null!, null!, null!);
        mgr.Setup(m => m.GetRolesAsync(It.IsAny<Usuario>())).ReturnsAsync(new List<string>());
        mgr.Setup(m => m.RemoveFromRolesAsync(It.IsAny<Usuario>(), It.IsAny<IEnumerable<string>>())).ReturnsAsync(IdentityResult.Success);
        mgr.Setup(m => m.AddToRoleAsync(It.IsAny<Usuario>(), It.IsAny<string>())).ReturnsAsync(IdentityResult.Success);
        return mgr;
    }

    [Fact]
    public async Task GetAll_ReturnsOk_ComUsuariosDoTenant()
    {
        using var ctx = CreateContext();
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        ctx.Users.Add(new Usuario { Id = userId, TenantId = tenantId, Nome = "Admin", Email = "a@a.com", UserName = "a@a.com", Ativo = true, CriadoEm = DateTime.UtcNow });
        ctx.Users.Add(new Usuario { Id = Guid.NewGuid(), TenantId = Guid.NewGuid(), Nome = "Outro", Email = "b@b.com", UserName = "b@b.com", Ativo = true, CriadoEm = DateTime.UtcNow });
        await ctx.SaveChangesAsync();

        var controller = new UsuariosController(CreateUserManagerMock().Object, ctx, CreateTenantMock(tenantId, userId).Object);
        var result = await controller.GetAll(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var list = ok.Value as System.Collections.IEnumerable;
        Assert.NotNull(list);
        Assert.Single(list!.Cast<object>());
    }

    [Fact]
    public async Task Desativar_ReturnsNotFound_QuandoUsuarioNaoExiste()
    {
        using var ctx = CreateContext();
        var tenantId = Guid.NewGuid();
        var controller = new UsuariosController(CreateUserManagerMock().Object, ctx, CreateTenantMock(tenantId, Guid.NewGuid()).Object);
        var result = await controller.Desativar(Guid.NewGuid(), CancellationToken.None);
        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task Desativar_ReturnsBadRequest_QuandoDesativandoPropriaContato()
    {
        using var ctx = CreateContext();
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        ctx.Users.Add(new Usuario { Id = userId, TenantId = tenantId, Nome = "Self", Email = "s@s.com", UserName = "s@s.com", Ativo = true, CriadoEm = DateTime.UtcNow });
        await ctx.SaveChangesAsync();

        var controller = new UsuariosController(CreateUserManagerMock().Object, ctx, CreateTenantMock(tenantId, userId).Object);
        var result = await controller.Desativar(userId, CancellationToken.None);
        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task Desativar_ReturnsNoContent_QuandoSucesso()
    {
        using var ctx = CreateContext();
        var tenantId = Guid.NewGuid();
        var adminId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        ctx.Users.Add(new Usuario { Id = targetId, TenantId = tenantId, Nome = "Target", Email = "t@t.com", UserName = "t@t.com", Ativo = true, CriadoEm = DateTime.UtcNow });
        await ctx.SaveChangesAsync();

        var controller = new UsuariosController(CreateUserManagerMock().Object, ctx, CreateTenantMock(tenantId, adminId).Object);
        var result = await controller.Desativar(targetId, CancellationToken.None);
        Assert.IsType<NoContentResult>(result);
        Assert.False((await ctx.Users.FindAsync(targetId))!.Ativo);
    }

    [Fact]
    public async Task AlterarPerfil_ReturnsNotFound_QuandoUsuarioNaoExiste()
    {
        using var ctx = CreateContext();
        var controller = new UsuariosController(CreateUserManagerMock().Object, ctx, CreateTenantMock(Guid.NewGuid(), Guid.NewGuid()).Object);
        var result = await controller.AlterarPerfil(Guid.NewGuid(), new AlterarPerfilDto("Admin"), CancellationToken.None);
        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task AlterarPerfil_ReturnsBadRequest_QuandoPerfilInvalido()
    {
        using var ctx = CreateContext();
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        ctx.Users.Add(new Usuario { Id = userId, TenantId = tenantId, Nome = "U", Email = "u@u.com", UserName = "u@u.com", Ativo = true, CriadoEm = DateTime.UtcNow });
        await ctx.SaveChangesAsync();

        var controller = new UsuariosController(CreateUserManagerMock().Object, ctx, CreateTenantMock(tenantId, Guid.NewGuid()).Object);
        var result = await controller.AlterarPerfil(userId, new AlterarPerfilDto("PerfilInvalido"), CancellationToken.None);
        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task AlterarPerfil_ReturnsNoContent_QuandoSucesso()
    {
        using var ctx = CreateContext();
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        ctx.Users.Add(new Usuario { Id = userId, TenantId = tenantId, Nome = "U", Email = "u@u.com", UserName = "u@u.com", Ativo = true, CriadoEm = DateTime.UtcNow });
        await ctx.SaveChangesAsync();

        var controller = new UsuariosController(CreateUserManagerMock().Object, ctx, CreateTenantMock(tenantId, Guid.NewGuid()).Object);
        var result = await controller.AlterarPerfil(userId, new AlterarPerfilDto("Advogado"), CancellationToken.None);
        Assert.IsType<NoContentResult>(result);
    }

    [Fact]
    public async Task GetMe_ReturnsNotFound_QuandoUsuarioNaoExiste()
    {
        using var ctx = CreateContext();
        var controller = new UsuariosController(CreateUserManagerMock().Object, ctx, CreateTenantMock(Guid.NewGuid(), Guid.NewGuid()).Object);
        var result = await controller.GetMe();
        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task GetMe_ReturnsOk_QuandoEncontrado()
    {
        using var ctx = CreateContext();
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var tenant = new Tenant { Id = tenantId, Nome = "T", Plano = PlanoTipo.Free, Status = StatusTenant.Trial, CriadoEm = DateTime.UtcNow };
        ctx.Tenants.Add(tenant);
        ctx.Users.Add(new Usuario { Id = userId, TenantId = tenantId, Nome = "Me", Email = "me@me.com", UserName = "me@me.com", Ativo = true, CriadoEm = DateTime.UtcNow, Tenant = tenant });
        await ctx.SaveChangesAsync();

        var controller = new UsuariosController(CreateUserManagerMock().Object, ctx, CreateTenantMock(tenantId, userId).Object);
        var result = await controller.GetMe();
        Assert.IsType<OkObjectResult>(result);
    }
}

// ─── TarefasController ────────────────────────────────────────────────────────

public class TarefasControllerTests
{
    private static Mock<ITarefaService> CreateServiceMock()
    {
        var mock = new Mock<ITarefaService>();
        var dto = new TarefaResponseDto(Guid.NewGuid(), "T", null, null, null, Guid.NewGuid(), "U",
            null, PrioridadeTarefa.Media, StatusTarefa.Pendente, null, null, null, null, [], DateTime.UtcNow, null, false);
        mock.Setup(s => s.GetAllAsync(It.IsAny<TarefaFiltroDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedResultDto<TarefaListItemDto>([], 0, 1, 20, 0));
        mock.Setup(s => s.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((TarefaResponseDto?)null);
        mock.Setup(s => s.CreateAsync(It.IsAny<CreateTarefaDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(dto);
        mock.Setup(s => s.UpdateAsync(It.IsAny<Guid>(), It.IsAny<UpdateTarefaDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(dto);
        mock.Setup(s => s.ConcluirAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        mock.Setup(s => s.DeleteAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        mock.Setup(s => s.MoverKanbanAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<StatusTarefa>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        return mock;
    }

    private static Mock<IAuditService> CreateAuditMock()
    {
        var mock = new Mock<IAuditService>();
        mock.Setup(a => a.LogAsync(It.IsAny<AuditLogEntry>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        return mock;
    }

    private static Mock<ITenantContext> CreateTenantMock()
    {
        var mock = new Mock<ITenantContext>();
        mock.Setup(t => t.TenantId).Returns(Guid.NewGuid());
        mock.Setup(t => t.UserId).Returns(Guid.NewGuid());
        return mock;
    }

    private static TarefasController CreateController(Mock<ITarefaService>? service = null, Mock<IAuditService>? audit = null, Mock<ITenantContext>? tenant = null)
    {
        var controller = new TarefasController(
            service?.Object ?? CreateServiceMock().Object,
            tenant?.Object ?? CreateTenantMock().Object,
            audit?.Object ?? CreateAuditMock().Object);
        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };
        return controller;
    }

    [Fact]
    public async Task GetAll_ReturnsOk()
    {
        var result = await CreateController().GetAll(null, null, null, null, null, null, false, 1, 20, null, CancellationToken.None);
        Assert.IsType<OkObjectResult>(result.Result);
    }

    [Fact]
    public async Task GetById_ReturnsNotFound_QuandoNulo()
    {
        var result = await CreateController().GetById(Guid.NewGuid(), CancellationToken.None);
        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task GetById_ReturnsOk_QuandoEncontrado()
    {
        var service = CreateServiceMock();
        var dto = new TarefaResponseDto(Guid.NewGuid(), "T", null, null, null, Guid.NewGuid(), "U",
            null, PrioridadeTarefa.Media, StatusTarefa.Pendente, null, null, null, null, [], DateTime.UtcNow, null, false);
        service.Setup(s => s.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(dto);

        var result = await CreateController(service).GetById(Guid.NewGuid(), CancellationToken.None);
        Assert.IsType<OkObjectResult>(result.Result);
    }

    [Fact]
    public async Task Create_ReturnsCreatedAtAction()
    {
        var dto = new CreateTarefaDto("Titulo", null, null, null, PrioridadeTarefa.Alta, null, null, null);
        var result = await CreateController().Create(dto, CancellationToken.None);
        Assert.IsType<CreatedAtActionResult>(result.Result);
    }

    [Fact]
    public async Task Update_ReturnsNotFound_QuandoKeyNotFoundException()
    {
        var service = CreateServiceMock();
        service.Setup(s => s.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new KeyNotFoundException());

        var dto = new UpdateTarefaDto("T", null, null, null, PrioridadeTarefa.Alta, StatusTarefa.Pendente, null, null, null);
        var result = await CreateController(service).Update(Guid.NewGuid(), dto, CancellationToken.None);
        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task Update_ReturnsOk_QuandoSucesso()
    {
        var service = CreateServiceMock();
        var existente = new TarefaResponseDto(Guid.NewGuid(), "T", null, null, null, Guid.NewGuid(), "U",
            null, PrioridadeTarefa.Media, StatusTarefa.Pendente, null, null, null, null, [], DateTime.UtcNow, null, false);
        service.Setup(s => s.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(existente);

        var dto = new UpdateTarefaDto("T", null, null, null, PrioridadeTarefa.Alta, StatusTarefa.Pendente, null, null, null);
        var result = await CreateController(service).Update(Guid.NewGuid(), dto, CancellationToken.None);
        Assert.IsType<OkObjectResult>(result.Result);
    }

    [Fact]
    public async Task Concluir_ReturnsNotFound_QuandoKeyNotFoundException()
    {
        var service = CreateServiceMock();
        service.Setup(s => s.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new KeyNotFoundException());

        var result = await CreateController(service).Concluir(Guid.NewGuid(), CancellationToken.None);
        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task Concluir_ReturnsNoContent_QuandoSucesso()
    {
        var service = CreateServiceMock();
        var existente = new TarefaResponseDto(Guid.NewGuid(), "T", null, null, null, Guid.NewGuid(), "U",
            null, PrioridadeTarefa.Media, StatusTarefa.Pendente, null, null, null, null, [], DateTime.UtcNow, null, false);
        service.Setup(s => s.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(existente);

        var result = await CreateController(service).Concluir(Guid.NewGuid(), CancellationToken.None);
        Assert.IsType<NoContentResult>(result);
    }

    [Fact]
    public async Task Delete_ReturnsNotFound_QuandoKeyNotFoundException()
    {
        var service = CreateServiceMock();
        service.Setup(s => s.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new KeyNotFoundException());

        var result = await CreateController(service).Delete(Guid.NewGuid(), CancellationToken.None);
        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task Delete_ReturnsNoContent_QuandoSucesso()
    {
        var service = CreateServiceMock();
        var existente = new TarefaResponseDto(Guid.NewGuid(), "T", null, null, null, Guid.NewGuid(), "U",
            null, PrioridadeTarefa.Media, StatusTarefa.Pendente, null, null, null, null, [], DateTime.UtcNow, null, false);
        service.Setup(s => s.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(existente);

        var result = await CreateController(service).Delete(Guid.NewGuid(), CancellationToken.None);
        Assert.IsType<NoContentResult>(result);
    }

    [Fact]
    public async Task MoverKanban_ReturnsNotFound_QuandoKeyNotFoundException()
    {
        var service = CreateServiceMock();
        service.Setup(s => s.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new KeyNotFoundException());

        var result = await CreateController(service).MoverKanban(Guid.NewGuid(), new MoverKanbanDto(StatusTarefa.EmAndamento), CancellationToken.None);
        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task MoverKanban_ReturnsNoContent_QuandoSucesso()
    {
        var service = CreateServiceMock();
        var existente = new TarefaResponseDto(Guid.NewGuid(), "T", null, null, null, Guid.NewGuid(), "U",
            null, PrioridadeTarefa.Media, StatusTarefa.Pendente, null, null, null, null, [], DateTime.UtcNow, null, false);
        service.Setup(s => s.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(existente);

        var result = await CreateController(service).MoverKanban(Guid.NewGuid(), new MoverKanbanDto(StatusTarefa.EmAndamento), CancellationToken.None);
        Assert.IsType<NoContentResult>(result);
    }
}

// ─── TimesheetController ──────────────────────────────────────────────────────

public class TimesheetControllerTests
{
    private static Mock<ITimesheetService> CreateServiceMock()
    {
        var mock = new Mock<ITimesheetService>();
        var dto = new RegistroTempoDto(Guid.NewGuid(), DateTime.UtcNow, null, null, null, true, null, null, null, null, Guid.NewGuid(), "U", DateTime.UtcNow);
        mock.Setup(s => s.GetAllAsync(It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RegistroTempoPagedDto([], 0, 0));
        mock.Setup(s => s.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((RegistroTempoDto?)null);
        mock.Setup(s => s.GetCronometroAtivoAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((RegistroTempoDto?)null);
        mock.Setup(s => s.IniciarCronometroAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<IniciarRegistroDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(dto);
        mock.Setup(s => s.PararCronometroAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<PararRegistroDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(dto);
        mock.Setup(s => s.CriarManualAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CriarRegistroManualDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(dto);
        mock.Setup(s => s.AtualizarAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<AtualizarRegistroDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(dto);
        mock.Setup(s => s.DeletarAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        return mock;
    }

    private static Mock<ITenantContext> CreateTenantMock()
    {
        var mock = new Mock<ITenantContext>();
        mock.Setup(t => t.TenantId).Returns(Guid.NewGuid());
        mock.Setup(t => t.UserId).Returns(Guid.NewGuid());
        return mock;
    }

    [Fact]
    public async Task GetAll_ReturnsOk()
    {
        var controller = new TimesheetController(CreateServiceMock().Object, CreateTenantMock().Object);
        var result = await controller.GetAll(null, null, null, null, 1, 20, CancellationToken.None);
        Assert.IsType<OkObjectResult>(result.Result);
    }

    [Fact]
    public async Task GetById_ReturnsNotFound_QuandoNulo()
    {
        var controller = new TimesheetController(CreateServiceMock().Object, CreateTenantMock().Object);
        var result = await controller.GetById(Guid.NewGuid(), CancellationToken.None);
        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task GetById_ReturnsOk_QuandoEncontrado()
    {
        var service = CreateServiceMock();
        var dto = new RegistroTempoDto(Guid.NewGuid(), DateTime.UtcNow, null, null, null, true, null, null, null, null, Guid.NewGuid(), "U", DateTime.UtcNow);
        service.Setup(s => s.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(dto);
        var controller = new TimesheetController(service.Object, CreateTenantMock().Object);
        var result = await controller.GetById(Guid.NewGuid(), CancellationToken.None);
        Assert.IsType<OkObjectResult>(result.Result);
    }

    [Fact]
    public async Task GetAtivo_ReturnsOk()
    {
        var controller = new TimesheetController(CreateServiceMock().Object, CreateTenantMock().Object);
        var result = await controller.GetAtivo(CancellationToken.None);
        Assert.IsType<OkObjectResult>(result.Result);
    }

    [Fact]
    public async Task Iniciar_ReturnsConflict_QuandoInvalidOperation()
    {
        var service = CreateServiceMock();
        service.Setup(s => s.IniciarCronometroAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<IniciarRegistroDto>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Já existe um cronômetro ativo"));
        var controller = new TimesheetController(service.Object, CreateTenantMock().Object);
        var result = await controller.Iniciar(new IniciarRegistroDto(), CancellationToken.None);
        Assert.IsType<ConflictObjectResult>(result.Result);
    }

    [Fact]
    public async Task Iniciar_ReturnsCreated_QuandoSucesso()
    {
        var controller = new TimesheetController(CreateServiceMock().Object, CreateTenantMock().Object);
        var result = await controller.Iniciar(new IniciarRegistroDto(), CancellationToken.None);
        Assert.IsType<CreatedAtActionResult>(result.Result);
    }

    [Fact]
    public async Task Parar_ReturnsConflict_QuandoInvalidOperation()
    {
        var service = CreateServiceMock();
        service.Setup(s => s.PararCronometroAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<PararRegistroDto>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Nenhum cronômetro ativo"));
        var controller = new TimesheetController(service.Object, CreateTenantMock().Object);
        var result = await controller.Parar(new PararRegistroDto(), CancellationToken.None);
        Assert.IsType<ConflictObjectResult>(result.Result);
    }

    [Fact]
    public async Task Parar_ReturnsOk_QuandoSucesso()
    {
        var controller = new TimesheetController(CreateServiceMock().Object, CreateTenantMock().Object);
        var result = await controller.Parar(new PararRegistroDto(), CancellationToken.None);
        Assert.IsType<OkObjectResult>(result.Result);
    }

    [Fact]
    public async Task CriarManual_ReturnsBadRequest_QuandoArgumentException()
    {
        var service = CreateServiceMock();
        service.Setup(s => s.CriarManualAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CriarRegistroManualDto>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ArgumentException("Data fim deve ser maior que data início"));
        var controller = new TimesheetController(service.Object, CreateTenantMock().Object);
        var dto = new CriarRegistroManualDto(DateTime.UtcNow, DateTime.UtcNow.AddHours(-1));
        var result = await controller.CriarManual(dto, CancellationToken.None);
        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task CriarManual_ReturnsCreated_QuandoSucesso()
    {
        var controller = new TimesheetController(CreateServiceMock().Object, CreateTenantMock().Object);
        var dto = new CriarRegistroManualDto(DateTime.UtcNow.AddHours(-1), DateTime.UtcNow);
        var result = await controller.CriarManual(dto, CancellationToken.None);
        Assert.IsType<CreatedAtActionResult>(result.Result);
    }

    [Fact]
    public async Task Atualizar_ReturnsNotFound_QuandoKeyNotFoundException()
    {
        var service = CreateServiceMock();
        service.Setup(s => s.AtualizarAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<AtualizarRegistroDto>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new KeyNotFoundException("Registro não encontrado"));
        var controller = new TimesheetController(service.Object, CreateTenantMock().Object);
        var result = await controller.Atualizar(Guid.NewGuid(), new AtualizarRegistroDto(null, null, null), CancellationToken.None);
        Assert.IsType<NotFoundObjectResult>(result.Result);
    }

    [Fact]
    public async Task Atualizar_ReturnsOk_QuandoSucesso()
    {
        var controller = new TimesheetController(CreateServiceMock().Object, CreateTenantMock().Object);
        var result = await controller.Atualizar(Guid.NewGuid(), new AtualizarRegistroDto(null, null, null), CancellationToken.None);
        Assert.IsType<OkObjectResult>(result.Result);
    }

    [Fact]
    public async Task Deletar_ReturnsNotFound_QuandoKeyNotFoundException()
    {
        var service = CreateServiceMock();
        service.Setup(s => s.DeletarAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new KeyNotFoundException("Registro não encontrado"));
        var controller = new TimesheetController(service.Object, CreateTenantMock().Object);
        var result = await controller.Deletar(Guid.NewGuid(), CancellationToken.None);
        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public async Task Deletar_ReturnsNoContent_QuandoSucesso()
    {
        var controller = new TimesheetController(CreateServiceMock().Object, CreateTenantMock().Object);
        var result = await controller.Deletar(Guid.NewGuid(), CancellationToken.None);
        Assert.IsType<NoContentResult>(result);
    }
}

// ─── ModelosController ────────────────────────────────────────────────────────

public class ModelosControllerTests
{
    private static Mock<IModeloDocumentoService> CreateServiceMock()
    {
        var mock = new Mock<IModeloDocumentoService>();
        var dto = new ModeloDocumentoDto { Id = Guid.NewGuid(), Nome = "Modelo", Conteudo = "Conteudo {{NOME}}", Variaveis = ["NOME"], CriadoEm = DateTime.UtcNow };
        mock.Setup(s => s.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync([dto]);
        mock.Setup(s => s.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync((ModeloDocumentoDto?)null);
        mock.Setup(s => s.CreateAsync(It.IsAny<CreateModeloDocumentoDto>(), It.IsAny<CancellationToken>())).ReturnsAsync(dto);
        mock.Setup(s => s.UpdateAsync(It.IsAny<Guid>(), It.IsAny<UpdateModeloDocumentoDto>(), It.IsAny<CancellationToken>())).ReturnsAsync(dto);
        mock.Setup(s => s.DeleteAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        mock.Setup(s => s.AplicarVariaveisAsync(It.IsAny<Guid>(), It.IsAny<Dictionary<string, string>>(), It.IsAny<CancellationToken>())).ReturnsAsync("Conteudo João");
        mock.Setup(s => s.GerarComIAAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GerarModeloComIAResultDto { Conteudo = "Gerado", Variaveis = [] });
        return mock;
    }

    private static Mock<ITenantContext> CreateTenantMock()
    {
        var mock = new Mock<ITenantContext>();
        mock.Setup(t => t.TenantId).Returns(Guid.NewGuid());
        return mock;
    }

    [Fact]
    public async Task GetAll_ReturnsOk()
    {
        var controller = new ModelosController(CreateServiceMock().Object, CreateTenantMock().Object);
        var result = await controller.GetAll();
        Assert.IsType<OkObjectResult>(result.Result);
    }

    [Fact]
    public async Task GetById_ReturnsNotFound_QuandoNulo()
    {
        var controller = new ModelosController(CreateServiceMock().Object, CreateTenantMock().Object);
        var result = await controller.GetById(Guid.NewGuid());
        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task GetById_ReturnsOk_QuandoEncontrado()
    {
        var service = CreateServiceMock();
        service.Setup(s => s.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ModeloDocumentoDto { Id = Guid.NewGuid(), Nome = "M", Conteudo = "C", CriadoEm = DateTime.UtcNow });
        var controller = new ModelosController(service.Object, CreateTenantMock().Object);
        var result = await controller.GetById(Guid.NewGuid());
        Assert.IsType<OkObjectResult>(result.Result);
    }

    [Fact]
    public async Task Create_ReturnsBadRequest_QuandoNomeVazio()
    {
        var controller = new ModelosController(CreateServiceMock().Object, CreateTenantMock().Object);
        var result = await controller.Create(new CreateModeloDocumentoDto { Nome = "", Conteudo = "C" });
        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task Create_ReturnsCreatedAtAction_QuandoSucesso()
    {
        var controller = new ModelosController(CreateServiceMock().Object, CreateTenantMock().Object);
        var result = await controller.Create(new CreateModeloDocumentoDto { Nome = "Modelo", Conteudo = "C" });
        Assert.IsType<CreatedAtActionResult>(result.Result);
    }

    [Fact]
    public async Task GerarComIA_ReturnsBadRequest_QuandoDescricaoVazia()
    {
        var controller = new ModelosController(CreateServiceMock().Object, CreateTenantMock().Object);
        var result = await controller.GerarComIA(new GerarModeloComIADto { Descricao = "" });
        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task GerarComIA_ReturnsOk_QuandoSucesso()
    {
        var controller = new ModelosController(CreateServiceMock().Object, CreateTenantMock().Object);
        var result = await controller.GerarComIA(new GerarModeloComIADto { Descricao = "Petição inicial" });
        Assert.IsType<OkObjectResult>(result.Result);
    }

    [Fact]
    public async Task Update_ReturnsNotFound_QuandoKeyNotFoundException()
    {
        var service = CreateServiceMock();
        service.Setup(s => s.UpdateAsync(It.IsAny<Guid>(), It.IsAny<UpdateModeloDocumentoDto>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new KeyNotFoundException());
        var controller = new ModelosController(service.Object, CreateTenantMock().Object);
        var result = await controller.Update(Guid.NewGuid(), new UpdateModeloDocumentoDto());
        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task Update_ReturnsOk_QuandoSucesso()
    {
        var controller = new ModelosController(CreateServiceMock().Object, CreateTenantMock().Object);
        var result = await controller.Update(Guid.NewGuid(), new UpdateModeloDocumentoDto { Nome = "Novo" });
        Assert.IsType<OkObjectResult>(result.Result);
    }

    [Fact]
    public async Task Delete_ReturnsNotFound_QuandoKeyNotFoundException()
    {
        var service = CreateServiceMock();
        service.Setup(s => s.DeleteAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new KeyNotFoundException());
        var controller = new ModelosController(service.Object, CreateTenantMock().Object);
        var result = await controller.Delete(Guid.NewGuid());
        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task Delete_ReturnsNoContent_QuandoSucesso()
    {
        var controller = new ModelosController(CreateServiceMock().Object, CreateTenantMock().Object);
        var result = await controller.Delete(Guid.NewGuid());
        Assert.IsType<NoContentResult>(result);
    }

    [Fact]
    public async Task AplicarVariaveis_ReturnsNotFound_QuandoKeyNotFoundException()
    {
        var service = CreateServiceMock();
        service.Setup(s => s.AplicarVariaveisAsync(It.IsAny<Guid>(), It.IsAny<Dictionary<string, string>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new KeyNotFoundException());
        var controller = new ModelosController(service.Object, CreateTenantMock().Object);
        var result = await controller.AplicarVariaveis(Guid.NewGuid(), new Dictionary<string, string>());
        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task AplicarVariaveis_ReturnsOk_QuandoSucesso()
    {
        var controller = new ModelosController(CreateServiceMock().Object, CreateTenantMock().Object);
        var result = await controller.AplicarVariaveis(Guid.NewGuid(), new Dictionary<string, string> { ["NOME"] = "João" });
        Assert.IsType<OkObjectResult>(result.Result);
    }
}

// ─── ConfiguracoesController ──────────────────────────────────────────────────

public class ConfiguracoesControllerTests
{
    private static AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options);
    }

    private static Mock<ITenantContext> CreateTenantMock(Guid tenantId, PlanoTipo plano = PlanoTipo.Free)
    {
        var mock = new Mock<ITenantContext>();
        mock.Setup(t => t.TenantId).Returns(tenantId);
        mock.Setup(t => t.Plano).Returns(plano);
        return mock;
    }

    private static Mock<UserManager<Usuario>> CreateUserManagerMock(Usuario? user = null, bool changePasswordSuccess = true)
    {
        var storeMock = new Mock<IUserStore<Usuario>>();
        var mgr = new Mock<UserManager<Usuario>>(storeMock.Object, null!, null!, null!, null!, null!, null!, null!, null!);
        mgr.Setup(m => m.GetUserAsync(It.IsAny<ClaimsPrincipal>())).ReturnsAsync(user);
        var changeResult = changePasswordSuccess ? IdentityResult.Success
            : IdentityResult.Failed(new IdentityError { Description = "Senha inválida" });
        mgr.Setup(m => m.ChangePasswordAsync(It.IsAny<Usuario>(), It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(changeResult);
        return mgr;
    }

    [Fact]
    public async Task GetConfiguracoes_ReturnsNotFound_QuandoTenantNaoExiste()
    {
        using var ctx = CreateContext();
        var controller = new ConfiguracoesController(ctx, CreateTenantMock(Guid.NewGuid()).Object, CreateUserManagerMock().Object);
        var result = await controller.GetConfiguracoes(CancellationToken.None);
        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task GetConfiguracoes_ReturnsOk_QuandoTenantExiste()
    {
        using var ctx = CreateContext();
        var tenantId = Guid.NewGuid();
        ctx.Tenants.Add(new Tenant { Id = tenantId, Nome = "T", Plano = PlanoTipo.Free, Status = StatusTenant.Trial, CriadoEm = DateTime.UtcNow });
        await ctx.SaveChangesAsync();

        var controller = new ConfiguracoesController(ctx, CreateTenantMock(tenantId).Object, CreateUserManagerMock().Object);
        var result = await controller.GetConfiguracoes(CancellationToken.None);
        Assert.IsType<OkObjectResult>(result);
    }

    [Fact]
    public async Task UpdateConfiguracoes_ReturnsNotFound_QuandoTenantNaoExiste()
    {
        using var ctx = CreateContext();
        var controller = new ConfiguracoesController(ctx, CreateTenantMock(Guid.NewGuid()).Object, CreateUserManagerMock().Object);
        var result = await controller.UpdateConfiguracoes(new UpdateConfiguracoesDto("Nome", null, null), CancellationToken.None);
        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task UpdateConfiguracoes_ReturnsNoContent_QuandoSucesso()
    {
        using var ctx = CreateContext();
        var tenantId = Guid.NewGuid();
        ctx.Tenants.Add(new Tenant { Id = tenantId, Nome = "T", Plano = PlanoTipo.Free, Status = StatusTenant.Trial, CriadoEm = DateTime.UtcNow });
        await ctx.SaveChangesAsync();

        var controller = new ConfiguracoesController(ctx, CreateTenantMock(tenantId).Object, CreateUserManagerMock().Object);
        var result = await controller.UpdateConfiguracoes(new UpdateConfiguracoesDto("Novo Nome", "12.345.678/0001-99", "Rua Teste"), CancellationToken.None);
        Assert.IsType<NoContentResult>(result);
    }

    [Fact]
    public async Task GetUso_ReturnsOk()
    {
        using var ctx = CreateContext();
        var tenantId = Guid.NewGuid();
        var controller = new ConfiguracoesController(ctx, CreateTenantMock(tenantId, PlanoTipo.Pro).Object, CreateUserManagerMock().Object);
        var result = await controller.GetUso(CancellationToken.None);
        Assert.IsType<OkObjectResult>(result);
    }

    [Fact]
    public async Task Upgrade_ReturnsNotFound_QuandoTenantNaoExiste()
    {
        using var ctx = CreateContext();
        var controller = new ConfiguracoesController(ctx, CreateTenantMock(Guid.NewGuid()).Object, CreateUserManagerMock().Object);
        var result = await controller.Upgrade(new UpgradePlanoDto("Pro"), CancellationToken.None);
        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task Upgrade_ReturnsBadRequest_QuandoPlanoInvalido()
    {
        using var ctx = CreateContext();
        var tenantId = Guid.NewGuid();
        ctx.Tenants.Add(new Tenant { Id = tenantId, Nome = "T", Plano = PlanoTipo.Free, Status = StatusTenant.Trial, CriadoEm = DateTime.UtcNow });
        await ctx.SaveChangesAsync();

        var controller = new ConfiguracoesController(ctx, CreateTenantMock(tenantId).Object, CreateUserManagerMock().Object);
        var result = await controller.Upgrade(new UpgradePlanoDto("PlanoInvalido"), CancellationToken.None);
        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task Upgrade_ReturnsOk_QuandoSucesso()
    {
        using var ctx = CreateContext();
        var tenantId = Guid.NewGuid();
        ctx.Tenants.Add(new Tenant { Id = tenantId, Nome = "T", Plano = PlanoTipo.Free, Status = StatusTenant.Trial, CriadoEm = DateTime.UtcNow });
        await ctx.SaveChangesAsync();

        var controller = new ConfiguracoesController(ctx, CreateTenantMock(tenantId).Object, CreateUserManagerMock().Object);
        var result = await controller.Upgrade(new UpgradePlanoDto("Pro"), CancellationToken.None);
        Assert.IsType<OkObjectResult>(result);
    }

    [Fact]
    public async Task Cancelar_ReturnsNotFound_QuandoTenantNaoExiste()
    {
        using var ctx = CreateContext();
        var controller = new ConfiguracoesController(ctx, CreateTenantMock(Guid.NewGuid()).Object, CreateUserManagerMock().Object);
        var result = await controller.Cancelar(CancellationToken.None);
        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task Cancelar_ReturnsOk_QuandoSucesso()
    {
        using var ctx = CreateContext();
        var tenantId = Guid.NewGuid();
        ctx.Tenants.Add(new Tenant { Id = tenantId, Nome = "T", Plano = PlanoTipo.Pro, Status = StatusTenant.Ativo, CriadoEm = DateTime.UtcNow });
        await ctx.SaveChangesAsync();

        var controller = new ConfiguracoesController(ctx, CreateTenantMock(tenantId).Object, CreateUserManagerMock().Object);
        var result = await controller.Cancelar(CancellationToken.None);
        Assert.IsType<OkObjectResult>(result);
    }

    [Fact]
    public async Task AlterarSenha_ReturnsUnauthorized_QuandoUsuarioNulo()
    {
        using var ctx = CreateContext();
        var controller = new ConfiguracoesController(ctx, CreateTenantMock(Guid.NewGuid()).Object, CreateUserManagerMock(null).Object);
        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };
        var result = await controller.AlterarSenha(new AlterarSenhaDto("atual123", "nova12345"));
        Assert.IsType<UnauthorizedResult>(result);
    }

    [Fact]
    public async Task AlterarSenha_ReturnsBadRequest_QuandoSenhaInvalida()
    {
        using var ctx = CreateContext();
        var user = new Usuario { Id = Guid.NewGuid(), Nome = "U", Email = "u@u.com", UserName = "u@u.com", Ativo = true, CriadoEm = DateTime.UtcNow };
        var controller = new ConfiguracoesController(ctx, CreateTenantMock(Guid.NewGuid()).Object, CreateUserManagerMock(user, false).Object);
        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };
        var result = await controller.AlterarSenha(new AlterarSenhaDto("errada", "nova12345"));
        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task AlterarSenha_ReturnsNoContent_QuandoSucesso()
    {
        using var ctx = CreateContext();
        var user = new Usuario { Id = Guid.NewGuid(), Nome = "U", Email = "u@u.com", UserName = "u@u.com", Ativo = true, CriadoEm = DateTime.UtcNow };
        var controller = new ConfiguracoesController(ctx, CreateTenantMock(Guid.NewGuid()).Object, CreateUserManagerMock(user, true).Object);
        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };
        var result = await controller.AlterarSenha(new AlterarSenhaDto("atual123", "nova12345"));
        Assert.IsType<NoContentResult>(result);
    }
}

// ─── IAController ─────────────────────────────────────────────────────────────

public class IAControllerTests
{
    private static Mock<ITraducaoService> CreateTraducaoMock()
    {
        var mock = new Mock<ITraducaoService>();
        var dto = new TraducaoResponseDto(Guid.NewGuid(), Guid.NewGuid(), "Original", "Traduzido", false, false, DateTime.UtcNow);
        mock.Setup(s => s.TraduzirAndamentoAsync(It.IsAny<TraduzirAndamentoDto>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(dto);
        mock.Setup(s => s.ObterTraducaoAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((TraducaoResponseDto?)null);
        return mock;
    }

    private static Mock<IPecaJuridicaService> CreatePecaMock()
    {
        var mock = new Mock<IPecaJuridicaService>();
        var dto = new PecaGeradaResponseDto(Guid.NewGuid(), null, Guid.NewGuid(), Domain.Entities.TipoPecaJuridica.PeticaoInicial, "Petição", "Conteúdo gerado", null, null, DateTime.UtcNow);
        mock.Setup(s => s.GerarPecaAsync(It.IsAny<GerarPecaDto>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(dto);
        mock.Setup(s => s.ListarAsync(It.IsAny<ListPecasGeradasDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([dto]);
        mock.Setup(s => s.ObterPecaAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((PecaGeradaResponseDto?)null);
        return mock;
    }

    private static Mock<ICreditoService> CreateCreditoMock()
    {
        var mock = new Mock<ICreditoService>();
        mock.Setup(s => s.ObterCreditosAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CreditosTotaisDto([], 0));
        return mock;
    }

    private static Mock<IResumoProcessoService> CreateResumoMock()
    {
        var mock = new Mock<IResumoProcessoService>();
        var dto = new ResumoProcessoResponseDto(Guid.NewGuid(), Guid.NewGuid(), "Resumo", "Usuário", DateTime.UtcNow, false);
        mock.Setup(s => s.GerarAsync(It.IsAny<GerarResumoDto>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(dto);
        mock.Setup(s => s.ListarAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([dto]);
        return mock;
    }

    private static Mock<ITenantContext> CreateTenantMock()
    {
        var mock = new Mock<ITenantContext>();
        mock.Setup(t => t.TenantId).Returns(Guid.NewGuid());
        mock.Setup(t => t.UserId).Returns(Guid.NewGuid());
        return mock;
    }

    [Fact]
    public async Task TraduzirAndamento_ReturnsOk_QuandoSucesso()
    {
        var controller = new IAController(CreateTraducaoMock().Object, CreatePecaMock().Object, CreateCreditoMock().Object, CreateTenantMock().Object, CreateResumoMock().Object);
        var dto = new TraduzirAndamentoDto(Guid.NewGuid(), null, false, false);
        var result = await controller.TraduzirAndamento(dto, CancellationToken.None);
        Assert.IsType<OkObjectResult>(result.Result);
    }

    [Fact]
    public async Task TraduzirAndamento_ReturnsBadRequest_QuandoInvalidOperation()
    {
        var traducao = CreateTraducaoMock();
        traducao.Setup(s => s.TraduzirAndamentoAsync(It.IsAny<TraduzirAndamentoDto>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Créditos insuficientes"));
        var controller = new IAController(traducao.Object, CreatePecaMock().Object, CreateCreditoMock().Object, CreateTenantMock().Object, CreateResumoMock().Object);
        var result = await controller.TraduzirAndamento(new TraduzirAndamentoDto(Guid.NewGuid(), null, false, false), CancellationToken.None);
        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task ObterTraducao_ReturnsNotFound_QuandoNulo()
    {
        var controller = new IAController(CreateTraducaoMock().Object, CreatePecaMock().Object, CreateCreditoMock().Object, CreateTenantMock().Object, CreateResumoMock().Object);
        var result = await controller.ObterTraducao(Guid.NewGuid(), CancellationToken.None);
        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task ObterTraducao_ReturnsOk_QuandoEncontrado()
    {
        var traducao = CreateTraducaoMock();
        var dto = new TraducaoResponseDto(Guid.NewGuid(), Guid.NewGuid(), "O", "T", false, false, DateTime.UtcNow);
        traducao.Setup(s => s.ObterTraducaoAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(dto);
        var controller = new IAController(traducao.Object, CreatePecaMock().Object, CreateCreditoMock().Object, CreateTenantMock().Object, CreateResumoMock().Object);
        var result = await controller.ObterTraducao(Guid.NewGuid(), CancellationToken.None);
        Assert.IsType<OkObjectResult>(result.Result);
    }

    [Fact]
    public async Task GerarPeca_ReturnsOk_QuandoSucesso()
    {
        var controller = new IAController(CreateTraducaoMock().Object, CreatePecaMock().Object, CreateCreditoMock().Object, CreateTenantMock().Object, CreateResumoMock().Object);
        var dto = new GerarPecaDto(null, Domain.Entities.TipoPecaJuridica.PeticaoInicial, "Petição inicial...");
        var result = await controller.GerarPeca(dto, CancellationToken.None);
        Assert.IsType<OkObjectResult>(result.Result);
    }

    [Fact]
    public async Task GerarPeca_ReturnsBadRequest_QuandoInvalidOperation()
    {
        var peca = CreatePecaMock();
        peca.Setup(s => s.GerarPecaAsync(It.IsAny<GerarPecaDto>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Sem créditos"));
        var controller = new IAController(CreateTraducaoMock().Object, peca.Object, CreateCreditoMock().Object, CreateTenantMock().Object, CreateResumoMock().Object);
        var result = await controller.GerarPeca(new GerarPecaDto(null, Domain.Entities.TipoPecaJuridica.PeticaoInicial, "..."), CancellationToken.None);
        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task ListarPecasGeradas_ReturnsOk()
    {
        var controller = new IAController(CreateTraducaoMock().Object, CreatePecaMock().Object, CreateCreditoMock().Object, CreateTenantMock().Object, CreateResumoMock().Object);
        var result = await controller.ListarPecasGeradas(1, 20, null, null, CancellationToken.None);
        Assert.IsType<OkObjectResult>(result.Result);
    }

    [Fact]
    public async Task ObterPeca_ReturnsNotFound_QuandoNulo()
    {
        var controller = new IAController(CreateTraducaoMock().Object, CreatePecaMock().Object, CreateCreditoMock().Object, CreateTenantMock().Object, CreateResumoMock().Object);
        var result = await controller.ObterPeca(Guid.NewGuid(), CancellationToken.None);
        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task ObterPeca_ReturnsOk_QuandoEncontrado()
    {
        var peca = CreatePecaMock();
        var dto = new PecaGeradaResponseDto(Guid.NewGuid(), null, Guid.NewGuid(), Domain.Entities.TipoPecaJuridica.PeticaoInicial, "P", "C", null, null, DateTime.UtcNow);
        peca.Setup(s => s.ObterPecaAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(dto);
        var controller = new IAController(CreateTraducaoMock().Object, peca.Object, CreateCreditoMock().Object, CreateTenantMock().Object, CreateResumoMock().Object);
        var result = await controller.ObterPeca(Guid.NewGuid(), CancellationToken.None);
        Assert.IsType<OkObjectResult>(result.Result);
    }
}

// ─── CreditosController ───────────────────────────────────────────────────────

public class CreditosControllerTests
{
    [Fact]
    public async Task GetCreditos_ReturnsOk()
    {
        var mock = new Mock<ICreditoService>();
        mock.Setup(s => s.ObterCreditosAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CreditosTotaisDto([], 0));
        var controller = new CreditosController(mock.Object);
        var result = await controller.GetCreditos(CancellationToken.None);
        Assert.IsType<OkObjectResult>(result.Result);
    }
}

// ─── SeedController ───────────────────────────────────────────────────────────

public class SeedControllerTests
{
    private static AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options);
    }

    private static SeedService CreateSeedService(AppDbContext ctx)
    {
        var logger = new Mock<ILogger<SeedService>>().Object;
        return new SeedService(ctx, logger);
    }

    [Fact]
    public async Task GerarDadosDemo_ReturnsBadRequest_QuandoTenantIdVazio()
    {
        using var ctx = CreateContext();
        var controller = new SeedController(CreateSeedService(ctx));
        var result = await controller.GerarDadosDemo(Guid.Empty, CancellationToken.None);
        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task GerarDadosDemo_ReturnsBadRequest_QuandoDadosJaExistem()
    {
        using var ctx = CreateContext();
        var tenantId = Guid.NewGuid();
        ctx.Tenants.Add(new Tenant { Id = tenantId, Nome = "T", Plano = PlanoTipo.Free, Status = StatusTenant.Trial, CriadoEm = DateTime.UtcNow });
        ctx.Users.Add(new Usuario { Id = Guid.NewGuid(), TenantId = tenantId, Nome = "Admin", Email = "a@a.com", UserName = "a@a.com", Ativo = true, CriadoEm = DateTime.UtcNow });
        ctx.Contatos.Add(new Contato { Id = Guid.NewGuid(), TenantId = tenantId, Nome = "Contato Existente", Tipo = TipoPessoa.PF, TipoContato = TipoContato.Cliente, Ativo = true, CriadoEm = DateTime.UtcNow });
        await ctx.SaveChangesAsync();

        var controller = new SeedController(CreateSeedService(ctx));
        var result = await controller.GerarDadosDemo(tenantId, CancellationToken.None);
        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task GerarDadosDemo_ReturnsOk_QuandoSucesso()
    {
        using var ctx = CreateContext();
        var tenantId = Guid.NewGuid();
        ctx.Tenants.Add(new Tenant { Id = tenantId, Nome = "T", Plano = PlanoTipo.Free, Status = StatusTenant.Trial, CriadoEm = DateTime.UtcNow });
        ctx.Users.Add(new Usuario { Id = Guid.NewGuid(), TenantId = tenantId, Nome = "Admin", Email = "a@a.com", UserName = "a@a.com", Ativo = true, CriadoEm = DateTime.UtcNow });
        await ctx.SaveChangesAsync();

        var controller = new SeedController(CreateSeedService(ctx));
        var result = await controller.GerarDadosDemo(tenantId, CancellationToken.None);
        Assert.IsType<OkObjectResult>(result);
    }

    [Fact]
    public async Task DesfazerDadosDemo_ReturnsBadRequest_QuandoTenantIdVazio()
    {
        using var ctx = CreateContext();
        var controller = new SeedController(CreateSeedService(ctx));
        var result = await controller.DesfazerDadosDemo(Guid.Empty, CancellationToken.None);
        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task DesfazerDadosDemo_ReturnsOk_QuandoSucesso()
    {
        using var ctx = CreateContext();
        var tenantId = Guid.NewGuid();
        ctx.Tenants.Add(new Tenant { Id = tenantId, Nome = "T", Plano = PlanoTipo.Free, Status = StatusTenant.Trial, CriadoEm = DateTime.UtcNow });
        await ctx.SaveChangesAsync();

        var controller = new SeedController(CreateSeedService(ctx));
        var result = await controller.DesfazerDadosDemo(tenantId, CancellationToken.None);
        Assert.IsType<OkObjectResult>(result);
    }
}
