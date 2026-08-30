using LegalManager.API.Controllers;
using LegalManager.Application.DTOs.Atividades;
using LegalManager.Application.DTOs.Contatos;
using LegalManager.Application.DTOs.Documentos;
using LegalManager.Application.DTOs.Indicadores;
using LegalManager.Application.DTOs.Notificacoes;
using LegalManager.Application.DTOs.PortalCliente;
using LegalManager.Application.DTOs.Prazos;
using LegalManager.Application.DTOs.Publicacoes;
using LegalManager.Application.Interfaces;
using LegalManager.Domain.Entities;
using LegalManager.Domain.Enums;
using LegalManager.Domain.Interfaces;
using LegalManager.Infrastructure.Persistence;
using LegalManager.Infrastructure.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace LegalManager.UnitTests;

public class AuditoriaControllerTests
{
    private static Mock<IAuditService> CreateAuditServiceMock()
    {
        var mock = new Mock<IAuditService>();
        mock.Setup(a => a.GetByTenantAsync(It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AuditLogResponseDto>());
        mock.Setup(a => a.GetByEntityAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AuditLogResponseDto>());
        return mock;
    }

    [Fact]
    public async Task GetAll_ReturnsOkWithLogs()
    {
        var mock = CreateAuditServiceMock();
        var controller = new AuditoriaController(mock.Object);
        var result = await controller.GetAll(null, null, 1, 50, CancellationToken.None);
        Assert.IsType<OkObjectResult>(result.Result);
    }

    [Fact]
    public async Task GetAll_WithDateFilters_PassesCorrectParameters()
    {
        var mock = CreateAuditServiceMock();
        var controller = new AuditoriaController(mock.Object);
        var de = DateTime.UtcNow.AddDays(-7);
        var ate = DateTime.UtcNow;
        await controller.GetAll(de, ate, 1, 50, CancellationToken.None);
        mock.Verify(a => a.GetByTenantAsync(de, ate, 1, 50, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetByEntity_ReturnsOkWithLogs()
    {
        var mock = CreateAuditServiceMock();
        var controller = new AuditoriaController(mock.Object);
        var entityId = Guid.NewGuid();
        var result = await controller.GetByEntity("Processo", entityId, CancellationToken.None);
        Assert.IsType<OkObjectResult>(result.Result);
    }

    [Fact]
    public async Task GetByEntity_CallsServiceWithCorrectParameters()
    {
        var mock = CreateAuditServiceMock();
        var controller = new AuditoriaController(mock.Object);
        var entityId = Guid.NewGuid();
        await controller.GetByEntity("Usuario", entityId, CancellationToken.None);
        mock.Verify(a => a.GetByEntityAsync("Usuario", entityId, It.IsAny<CancellationToken>()), Times.Once);
    }
}

public class ConfiguracoesExtrasControllerTests
{
    private static AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options);
    }

    private static Mock<ITenantContext> CreateTenantContextMock(Guid tenantId)
    {
        var mock = new Mock<ITenantContext>();
        mock.Setup(t => t.TenantId).Returns(tenantId);
        return mock;
    }

    [Fact]
    public async Task GetAreas_ReturnsOk_WithAreas()
    {
        var ctx = CreateContext();
        var tenantId = Guid.NewGuid();
        ctx.AreasAtuacao.Add(new AreaAtuacao
        {
            Id = Guid.NewGuid(), TenantId = tenantId, Nome = "Civil",
            Descricao = "Direito Civil", Ativo = true, CriadoEm = DateTime.UtcNow
        });
        await ctx.SaveChangesAsync();
        var tenantContext = CreateTenantContextMock(tenantId);
        var controller = new ConfiguracoesExtrasController(ctx, tenantContext.Object);
        var result = await controller.GetAreas(CancellationToken.None);
        Assert.IsType<OkObjectResult>(result);
    }

    [Fact]
    public async Task CreateArea_ReturnsCreated()
    {
        var ctx = CreateContext();
        var tenantId = Guid.NewGuid();
        var tenantContext = CreateTenantContextMock(tenantId);
        var controller = new ConfiguracoesExtrasController(ctx, tenantContext.Object);
        var dto = new CreateAreaDto("Criminal", "Direito Criminal");
        var result = await controller.CreateArea(dto, CancellationToken.None);
        Assert.IsType<CreatedResult>(result);
    }

    [Fact]
    public async Task UpdateArea_ReturnsNoContent_WhenFound()
    {
        var ctx = CreateContext();
        var tenantId = Guid.NewGuid();
        var areaId = Guid.NewGuid();
        ctx.AreasAtuacao.Add(new AreaAtuacao
        {
            Id = areaId, TenantId = tenantId, Nome = "Civil", Ativo = true, CriadoEm = DateTime.UtcNow
        });
        await ctx.SaveChangesAsync();
        var tenantContext = CreateTenantContextMock(tenantId);
        var controller = new ConfiguracoesExtrasController(ctx, tenantContext.Object);
        var dto = new UpdateAreaDto("Criminal", "Updated");
        var result = await controller.UpdateArea(areaId, dto, CancellationToken.None);
        Assert.IsType<NoContentResult>(result);
    }

    [Fact]
    public async Task UpdateArea_ReturnsNotFound_WhenMissing()
    {
        var ctx = CreateContext();
        var tenantContext = CreateTenantContextMock(Guid.NewGuid());
        var controller = new ConfiguracoesExtrasController(ctx, tenantContext.Object);
        var dto = new UpdateAreaDto("Criminal", "Updated");
        var result = await controller.UpdateArea(Guid.NewGuid(), dto, CancellationToken.None);
        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task DeleteArea_ReturnsNoContent_WhenFound()
    {
        var ctx = CreateContext();
        var tenantId = Guid.NewGuid();
        var areaId = Guid.NewGuid();
        ctx.AreasAtuacao.Add(new AreaAtuacao
        {
            Id = areaId, TenantId = tenantId, Nome = "Civil", Ativo = true, CriadoEm = DateTime.UtcNow
        });
        await ctx.SaveChangesAsync();
        var tenantContext = CreateTenantContextMock(tenantId);
        var controller = new ConfiguracoesExtrasController(ctx, tenantContext.Object);
        var result = await controller.DeleteArea(areaId, CancellationToken.None);
        Assert.IsType<NoContentResult>(result);
    }

    [Fact]
    public async Task GetCategorias_ReturnsOk_WithCategorias()
    {
        var ctx = CreateContext();
        var tenantId = Guid.NewGuid();
        ctx.CategoriasFinanceiras.Add(new CategoriaFinanceira
        {
            Id = Guid.NewGuid(), TenantId = tenantId, Nome = "Honorário",
            Tipo = TipoCategoriaFinanceira.Receita, Ativo = true, CriadoEm = DateTime.UtcNow
        });
        await ctx.SaveChangesAsync();
        var tenantContext = CreateTenantContextMock(tenantId);
        var controller = new ConfiguracoesExtrasController(ctx, tenantContext.Object);
        var result = await controller.GetCategorias(CancellationToken.None);
        Assert.IsType<OkObjectResult>(result);
    }

    [Fact]
    public async Task CreateCategoria_ReturnsCreated()
    {
        var ctx = CreateContext();
        var tenantId = Guid.NewGuid();
        var tenantContext = CreateTenantContextMock(tenantId);
        var controller = new ConfiguracoesExtrasController(ctx, tenantContext.Object);
        var dto = new CreateCategoriaDto("Custas", "Receita");
        var result = await controller.CreateCategoria(dto, CancellationToken.None);
        Assert.IsType<CreatedResult>(result);
    }

    [Fact]
    public async Task CreateCategoria_ReturnsBadRequest_WhenInvalidTipo()
    {
        var ctx = CreateContext();
        var tenantContext = CreateTenantContextMock(Guid.NewGuid());
        var controller = new ConfiguracoesExtrasController(ctx, tenantContext.Object);
        var dto = new CreateCategoriaDto("Custas", "InvalidTipo");
        var result = await controller.CreateCategoria(dto, CancellationToken.None);
        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task UpdateCategoria_ReturnsNoContent_WhenFound()
    {
        var ctx = CreateContext();
        var tenantId = Guid.NewGuid();
        var catId = Guid.NewGuid();
        ctx.CategoriasFinanceiras.Add(new CategoriaFinanceira
        {
            Id = catId, TenantId = tenantId, Nome = "Custas",
            Tipo = TipoCategoriaFinanceira.Despesa, Ativo = true, CriadoEm = DateTime.UtcNow
        });
        await ctx.SaveChangesAsync();
        var tenantContext = CreateTenantContextMock(tenantId);
        var controller = new ConfiguracoesExtrasController(ctx, tenantContext.Object);
        var dto = new UpdateCategoriaDto("Novas Custas", "Receita");
        var result = await controller.UpdateCategoria(catId, dto, CancellationToken.None);
        Assert.IsType<NoContentResult>(result);
    }

    [Fact]
    public async Task DeleteCategoria_ReturnsNoContent_WhenFound()
    {
        var ctx = CreateContext();
        var tenantId = Guid.NewGuid();
        var catId = Guid.NewGuid();
        ctx.CategoriasFinanceiras.Add(new CategoriaFinanceira
        {
            Id = catId, TenantId = tenantId, Nome = "Custas",
            Tipo = TipoCategoriaFinanceira.Despesa, Ativo = true, CriadoEm = DateTime.UtcNow
        });
        await ctx.SaveChangesAsync();
        var tenantContext = CreateTenantContextMock(tenantId);
        var controller = new ConfiguracoesExtrasController(ctx, tenantContext.Object);
        var result = await controller.DeleteCategoria(catId, CancellationToken.None);
        Assert.IsType<NoContentResult>(result);
    }
}

public class IndicadoresControllerTests
{
    [Fact]
    public async Task Get_ReturnsIndicadores_WhenProPlan()
    {
        var serviceMock = new Mock<IIndicadoresService>();
        serviceMock.Setup(s => s.GetIndicadoresAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IndicadoresDto(
                new ProcessosIndicadoresDto(0, 0, 0, 0, 0, 0, Enumerable.Empty<ItemCountDto>(), Enumerable.Empty<ItemCountDto>()),
                new TarefasIndicadoresDto(0, 0, 0, 0, 0, 0, Enumerable.Empty<ItemCountDto>()),
                new FinanceiroIndicadoresDto(0, 0, 0, 0, 0, 0, 0, Enumerable.Empty<MesFinanceiroDto>()),
                new TimesheetIndicadoresDto(0, 0, 0, Enumerable.Empty<ItemCountDto>())
            ));
        var tenantContextMock = new Mock<ITenantContext>();
        tenantContextMock.Setup(t => t.TenantId).Returns(Guid.NewGuid());
        tenantContextMock.Setup(t => t.Plano).Returns(PlanoTipo.Pro);
        var controller = new IndicadoresController(serviceMock.Object, tenantContextMock.Object);
        var result = await controller.Get(CancellationToken.None);
        Assert.IsType<OkObjectResult>(result.Result);
    }

    [Fact]
    public async Task Get_Returns402_WhenFreePlan()
    {
        var serviceMock = new Mock<IIndicadoresService>();
        var tenantContextMock = new Mock<ITenantContext>();
        tenantContextMock.Setup(t => t.TenantId).Returns(Guid.NewGuid());
        tenantContextMock.Setup(t => t.Plano).Returns(PlanoTipo.Free);
        var controller = new IndicadoresController(serviceMock.Object, tenantContextMock.Object);
        var result = await controller.Get(CancellationToken.None);
        var statusResult = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(402, statusResult.StatusCode);
    }
}

public class PortalClienteControllerTests
{
    private static Mock<IPortalClienteService> CreateServiceMock()
    {
        var mock = new Mock<IPortalClienteService>();
        mock.Setup(s => s.LoginAsync(It.IsAny<LoginPortalDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PortalAuthResponseDto("token", DateTime.UtcNow.AddDays(7), new ClientePerfilDto(Guid.NewGuid(), Guid.NewGuid(), "Cliente", "test@test.com", null)));
        mock.Setup(s => s.GetPerfilAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ClientePerfilDto(Guid.NewGuid(), Guid.NewGuid(), "Cliente", "test@test.com", null));
        mock.Setup(s => s.GetMeusProcessosAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Enumerable.Empty<MeuProcessoDto>());
        mock.Setup(s => s.GetProcessoAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((MeuProcessoDto?)null);
        mock.Setup(s => s.GetAndamentosAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Enumerable.Empty<MeuAndamentoDto>());
        return mock;
    }

    [Fact]
    public async Task Login_ReturnsOk_WhenValid()
    {
        var service = CreateServiceMock();
        var tenantContext = new Mock<ITenantContext>();
        var controller = new PortalClienteController(service.Object, new Mock<IDocumentoService>().Object, tenantContext.Object);
        var result = await controller.Login(new LoginPortalDto("test@test.com", "senha"), CancellationToken.None);
        Assert.IsType<OkObjectResult>(result.Result);
    }

    [Fact]
    public async Task Login_ReturnsUnauthorized_WhenInvalid()
    {
        var service = CreateServiceMock();
        service.Setup(s => s.LoginAsync(It.IsAny<LoginPortalDto>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new UnauthorizedAccessException("Invalid credentials"));
        var tenantContext = new Mock<ITenantContext>();
        var controller = new PortalClienteController(service.Object, new Mock<IDocumentoService>().Object, tenantContext.Object);
        var result = await controller.Login(new LoginPortalDto("invalid@test.com", "wrong"), CancellationToken.None);
        Assert.IsType<UnauthorizedObjectResult>(result.Result);
    }

    [Fact]
    public async Task GetMe_ReturnsOk_WhenAuthorized()
    {
        var service = CreateServiceMock();
        var tenantContext = new Mock<ITenantContext>();
        var controller = new PortalClienteController(service.Object, new Mock<IDocumentoService>().Object, tenantContext.Object);
        var user = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(new[]
        {
            new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.NameIdentifier, Guid.NewGuid().ToString())
        }));
        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = user } };
        var result = await controller.GetMe(CancellationToken.None);
        Assert.IsType<OkObjectResult>(result.Result);
    }

    [Fact]
    public async Task GetMeusProcessos_ReturnsOk()
    {
        var service = CreateServiceMock();
        var tenantContext = new Mock<ITenantContext>();
        var controller = new PortalClienteController(service.Object, new Mock<IDocumentoService>().Object, tenantContext.Object);
        var user = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(new[]
        {
            new System.Security.Claims.Claim("contatoId", Guid.NewGuid().ToString()),
            new System.Security.Claims.Claim("tenantId", Guid.NewGuid().ToString())
        }));
        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = user } };
        var result = await controller.GetMeusProcessos(CancellationToken.None);
        Assert.IsType<OkObjectResult>(result.Result);
    }

    [Fact]
    public async Task GetProcesso_ReturnsNotFound_WhenNull()
    {
        var service = CreateServiceMock();
        var tenantContext = new Mock<ITenantContext>();
        var controller = new PortalClienteController(service.Object, new Mock<IDocumentoService>().Object, tenantContext.Object);
        var user = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(new[]
        {
            new System.Security.Claims.Claim("contatoId", Guid.NewGuid().ToString()),
            new System.Security.Claims.Claim("tenantId", Guid.NewGuid().ToString())
        }));
        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = user } };
        var result = await controller.GetProcesso(Guid.NewGuid(), CancellationToken.None);
        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task GetAndamentos_ReturnsOk()
    {
        var service = CreateServiceMock();
        var tenantContext = new Mock<ITenantContext>();
        var controller = new PortalClienteController(service.Object, new Mock<IDocumentoService>().Object, tenantContext.Object);
        var user = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(new[]
        {
            new System.Security.Claims.Claim("contatoId", Guid.NewGuid().ToString()),
            new System.Security.Claims.Claim("tenantId", Guid.NewGuid().ToString())
        }));
        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = user } };
        var result = await controller.GetAndamentos(Guid.NewGuid(), CancellationToken.None);
        Assert.IsType<OkObjectResult>(result.Result);
    }
}

public class PublicacoesControllerTests
{
    private static Mock<IPublicacaoService> CreateServiceMock()
    {
        var mock = new Mock<IPublicacaoService>();
        mock.Setup(s => s.GetAllAsync(It.IsAny<PublicacaoFiltroDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Enumerable.Empty<PublicacaoResponseDto>());
        mock.Setup(s => s.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((PublicacaoResponseDto?)null);
        mock.Setup(s => s.GetNaoLidasCountAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(5);
        return mock;
    }

    private static ITenantContext CreateProTenant()
    {
        var mock = new Mock<ITenantContext>();
        mock.Setup(t => t.Plano).Returns(PlanoTipo.Pro);
        mock.Setup(t => t.TenantId).Returns(Guid.NewGuid());
        mock.Setup(t => t.UserId).Returns(Guid.NewGuid());
        mock.Setup(t => t.UserRole).Returns("Admin");
        return mock.Object;
    }

    [Fact]
    public async Task GetAll_ReturnsOk()
    {
        var service = CreateServiceMock();
        var controller = new PublicacoesController(service.Object, CreateProTenant());
        var result = await controller.GetAll(null, null, null, null, null, null, 1, 20, CancellationToken.None);
        Assert.IsType<OkObjectResult>(result);
    }

    [Fact]
    public async Task GetById_ReturnsNotFound_WhenNull()
    {
        var service = CreateServiceMock();
        var controller = new PublicacoesController(service.Object, CreateProTenant());
        var result = await controller.GetById(Guid.NewGuid(), CancellationToken.None);
        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task MarcarLida_ReturnsNoContent()
    {
        var service = CreateServiceMock();
        service.Setup(s => s.MarcarLidaAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var controller = new PublicacoesController(service.Object, CreateProTenant());
        var result = await controller.MarcarLida(Guid.NewGuid(), CancellationToken.None);
        Assert.IsType<NoContentResult>(result);
    }

    [Fact]
    public async Task Arquivar_ReturnsNoContent()
    {
        var service = CreateServiceMock();
        service.Setup(s => s.ArquivarAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var controller = new PublicacoesController(service.Object, CreateProTenant());
        var result = await controller.Arquivar(Guid.NewGuid(), CancellationToken.None);
        Assert.IsType<NoContentResult>(result);
    }

    [Fact]
    public async Task GetNaoLidasCount_ReturnsOk()
    {
        var service = CreateServiceMock();
        var controller = new PublicacoesController(service.Object, CreateProTenant());
        var result = await controller.GetNaoLidasCount(CancellationToken.None);
        Assert.IsType<OkObjectResult>(result);
    }
}

public class AgendaControllerTests
{
    [Fact]
    public async Task GetAgenda_ReturnsOk()
    {
        var serviceMock = new Mock<IEventoService>();
        serviceMock.Setup(s => s.GetAgendaAsync(It.IsAny<AgendaFiltroDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Enumerable.Empty<AgendaItemDto>());
        var controller = new AgendaController(serviceMock.Object);
        var result = await controller.GetAgenda(DateTime.UtcNow.AddDays(-7), DateTime.UtcNow, null, null, CancellationToken.None);
        Assert.IsType<OkObjectResult>(result.Result);
    }
}

public class NotificacoesControllerTests
{
    private static Mock<INotificacaoService> CreateNotificacaoServiceMock()
    {
        var mock = new Mock<INotificacaoService>();
        mock.Setup(s => s.GetUnreadAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Enumerable.Empty<NotificacaoDto>());
        mock.Setup(s => s.GetUnreadCountAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        mock.Setup(s => s.GetHistoricoAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Enumerable.Empty<NotificacaoDto>(), 0));
        mock.Setup(s => s.MarcarLidaAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        mock.Setup(s => s.MarcarTodasLidasAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        return mock;
    }

    private static Mock<IPreferenciasNotificacaoService> CreatePrefsServiceMock()
    {
        var mock = new Mock<IPreferenciasNotificacaoService>();
        mock.Setup(s => s.GetAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PreferenciasNotificacaoDto(true, true, true, true, true, true, true, true, true, true, true, true));
        mock.Setup(s => s.AtualizarAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<AtualizarPreferenciasDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PreferenciasNotificacaoDto(true, true, true, true, true, true, true, true, true, true, true, true));
        return mock;
    }

    private static Mock<ITenantContext> CreateTenantContextMock()
    {
        var mock = new Mock<ITenantContext>();
        mock.Setup(t => t.TenantId).Returns(Guid.NewGuid());
        mock.Setup(t => t.UserId).Returns(Guid.NewGuid());
        return mock;
    }

    [Fact]
    public async Task GetUnread_ReturnsOk()
    {
        var service = CreateNotificacaoServiceMock();
        var prefs = CreatePrefsServiceMock();
        var tenant = CreateTenantContextMock();
        var controller = new NotificacoesController(service.Object, prefs.Object, tenant.Object);
        var result = await controller.GetUnread(CancellationToken.None);
        Assert.IsType<OkObjectResult>(result.Result);
    }

    [Fact]
    public async Task GetCount_ReturnsOk()
    {
        var service = CreateNotificacaoServiceMock();
        var prefs = CreatePrefsServiceMock();
        var tenant = CreateTenantContextMock();
        var controller = new NotificacoesController(service.Object, prefs.Object, tenant.Object);
        var result = await controller.GetCount(CancellationToken.None);
        Assert.IsType<OkObjectResult>(result.Result);
    }

    [Fact]
    public async Task MarcarLida_ReturnsNoContent()
    {
        var service = CreateNotificacaoServiceMock();
        var prefs = CreatePrefsServiceMock();
        var tenant = CreateTenantContextMock();
        var controller = new NotificacoesController(service.Object, prefs.Object, tenant.Object);
        var result = await controller.MarcarLida(Guid.NewGuid(), CancellationToken.None);
        Assert.IsType<NoContentResult>(result);
    }

    [Fact]
    public async Task MarcarTodasLidas_ReturnsNoContent()
    {
        var service = CreateNotificacaoServiceMock();
        var prefs = CreatePrefsServiceMock();
        var tenant = CreateTenantContextMock();
        var controller = new NotificacoesController(service.Object, prefs.Object, tenant.Object);
        var result = await controller.MarcarTodasLidas(CancellationToken.None);
        Assert.IsType<NoContentResult>(result);
    }
}

public class PrazosControllerTests
{
    private static PrazosController CreateController(PlanoTipo plano = PlanoTipo.Free)
    {
        var tenant = new Mock<ITenantContext>();
        tenant.Setup(t => t.TenantId).Returns(Guid.NewGuid());
        tenant.Setup(t => t.Plano).Returns(plano);
        return new PrazosController(tenant.Object);
    }

    [Fact]
    public void Calcular_Returns402_WhenFreePlan()
    {
        var controller = CreateController(PlanoTipo.Free);
        var dto = new CalcularPrazoDto(DateTime.UtcNow, 5, TipoCalculo.DiasUteis);
        var result = controller.Calcular(dto);
        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(402, objectResult.StatusCode);
    }

    [Fact]
    public void Calcular_ReturnsOk_WhenProPlan()
    {
        var controller = CreateController(PlanoTipo.Pro);
        var dto = new CalcularPrazoDto(DateTime.UtcNow, 5, TipoCalculo.DiasUteis);
        var result = controller.Calcular(dto);
        Assert.IsType<OkObjectResult>(result);
    }

    [Fact]
    public void Calcular_ReturnsOk_WhenPlusPlan()
    {
        var controller = CreateController(PlanoTipo.Plus);
        var dto = new CalcularPrazoDto(DateTime.UtcNow, 10, TipoCalculo.DiasCorridos);
        var result = controller.Calcular(dto);
        Assert.IsType<OkObjectResult>(result);
    }
}

public class EventosControllerTests
{
    private static Mock<IEventoService> CreateEventoServiceMock()
    {
        var mock = new Mock<IEventoService>();
        mock.Setup(s => s.GetAllAsync(It.IsAny<EventoFiltroDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedResultDto<EventoResponseDto>(Enumerable.Empty<EventoResponseDto>(), 0, 0, 1, 50));
        mock.Setup(s => s.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((EventoResponseDto?)null);
        return mock;
    }

    private static Mock<IAuditService> CreateAuditMock()
    {
        var mock = new Mock<IAuditService>();
        mock.Setup(a => a.LogAsync(It.IsAny<AuditLogEntry>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        return mock;
    }

    private static Mock<ITenantContext> CreateTenantContextMock()
    {
        var mock = new Mock<ITenantContext>();
        mock.Setup(t => t.TenantId).Returns(Guid.NewGuid());
        mock.Setup(t => t.UserId).Returns(Guid.NewGuid());
        return mock;
    }

    [Fact]
    public async Task GetAll_ReturnsOk()
    {
        var service = CreateEventoServiceMock();
        var audit = CreateAuditMock();
        var tenant = CreateTenantContextMock();
        var controller = new EventosController(service.Object, audit.Object, tenant.Object);
        var result = await controller.GetAll(null, null, null, null, null, 1, 50, CancellationToken.None);
        Assert.IsType<OkObjectResult>(result.Result);
    }

    [Fact]
    public async Task GetById_ReturnsNotFound_WhenNull()
    {
        var service = CreateEventoServiceMock();
        var audit = CreateAuditMock();
        var tenant = CreateTenantContextMock();
        var controller = new EventosController(service.Object, audit.Object, tenant.Object);
        var result = await controller.GetById(Guid.NewGuid(), CancellationToken.None);
        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task Delete_ReturnsNotFound_WhenKeyNotFound()
    {
        var service = CreateEventoServiceMock();
        service.Setup(s => s.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new KeyNotFoundException());
        var audit = CreateAuditMock();
        var tenant = CreateTenantContextMock();
        var controller = new EventosController(service.Object, audit.Object, tenant.Object);
        var result = await controller.Delete(Guid.NewGuid(), CancellationToken.None);
        Assert.IsType<NotFoundResult>(result);
    }
}