using LegalManager.API.Controllers;
using LegalManager.Application.DTOs.Honorarios;
using LegalManager.Application.Interfaces;
using LegalManager.Domain.Enums;
using LegalManager.Domain.Interfaces;
using LegalManager.Infrastructure.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace LegalManager.UnitTests;

public class HonorariosContratosControllerTests
{
    private static Mock<IHonorarioService> CreateServiceMock()
    {
        var mock = new Mock<IHonorarioService>();
        mock.Setup(s => s.GetDashboardAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DashboardHonorariosDto(0, 0, 0, 0, 0, 0, new List<InadimplenteResumoDto>(), new List<EvolucaoMensalDto>(), null, 0));
        mock.Setup(s => s.ListarAsync(It.IsAny<Guid>(), It.IsAny<FiltroContratoHonorario>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ContratosPagedDto(Enumerable.Empty<ContratoHonorarioDto>(), 0));
        mock.Setup(s => s.ObterAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ContratoHonorarioDto?)null);
        return mock;
    }

    private static Mock<ITenantContext> CreateTenantContextMock(PlanoTipo plano = PlanoTipo.Pro)
    {
        var mock = new Mock<ITenantContext>();
        mock.Setup(t => t.TenantId).Returns(Guid.NewGuid());
        mock.Setup(t => t.UserId).Returns(Guid.NewGuid());
        mock.Setup(t => t.Plano).Returns(plano);
        return mock;
    }

    private static Mock<IAuditService> CreateAuditMock()
    {
        var audit = new Mock<IAuditService>();
        audit.Setup(a => a.LogAsync(It.IsAny<AuditLogEntry>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        return audit;
    }

    [Fact]
    public async Task Dashboard_PlanoFree_Retorna402()
    {
        var svc = CreateServiceMock();
        var tenant = CreateTenantContextMock(PlanoTipo.Free);
        var ctrl = new HonorariosContratosController(svc.Object, tenant.Object, CreateAuditMock().Object);
        ctrl.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };

        var result = await ctrl.GetDashboard(CancellationToken.None);
        var status = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(402, status.StatusCode);
    }

    [Fact]
    public async Task Dashboard_PlanoPlus_RetornaOk()
    {
        var svc = CreateServiceMock();
        var tenant = CreateTenantContextMock(PlanoTipo.Plus);
        var ctrl = new HonorariosContratosController(svc.Object, tenant.Object, CreateAuditMock().Object);
        ctrl.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };

        var result = await ctrl.GetDashboard(CancellationToken.None);
        Assert.IsType<OkObjectResult>(result.Result);
    }

    [Fact]
    public async Task Dashboard_PlanoPro_RetornaOk()
    {
        var svc = CreateServiceMock();
        var tenant = CreateTenantContextMock(PlanoTipo.Pro);
        var ctrl = new HonorariosContratosController(svc.Object, tenant.Object, CreateAuditMock().Object);
        ctrl.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };

        var result = await ctrl.GetDashboard(CancellationToken.None);
        Assert.IsType<OkObjectResult>(result.Result);
    }

    [Fact]
    public async Task Dashboard_PlanoEnterprise_RetornaOk()
    {
        var svc = CreateServiceMock();
        var tenant = CreateTenantContextMock(PlanoTipo.Enterprise);
        var ctrl = new HonorariosContratosController(svc.Object, tenant.Object, CreateAuditMock().Object);
        ctrl.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };

        var result = await ctrl.GetDashboard(CancellationToken.None);
        Assert.IsType<OkObjectResult>(result.Result);
    }

    [Fact]
    public async Task GetAll_PlanoFree_Retorna402()
    {
        var svc = CreateServiceMock();
        var tenant = CreateTenantContextMock(PlanoTipo.Free);
        var ctrl = new HonorariosContratosController(svc.Object, tenant.Object, CreateAuditMock().Object);
        ctrl.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };

        var result = await ctrl.GetAll(null, null, null, null, 1, 20, CancellationToken.None);
        var status = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(402, status.StatusCode);
    }

    [Fact]
    public async Task GetById_NaoEncontrado_Retorna404()
    {
        var svc = CreateServiceMock();
        var tenant = CreateTenantContextMock();
        var ctrl = new HonorariosContratosController(svc.Object, tenant.Object, CreateAuditMock().Object);
        ctrl.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };

        var result = await ctrl.GetById(Guid.NewGuid(), CancellationToken.None);
        Assert.IsType<NotFoundResult>(result.Result);
    }
}
