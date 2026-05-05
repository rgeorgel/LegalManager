using LegalManager.API.Controllers;
using LegalManager.Application.DTOs.Financeiro;
using LegalManager.Application.Interfaces;
using LegalManager.Domain.Enums;
using LegalManager.Domain.Interfaces;
using LegalManager.Infrastructure.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace LegalManager.UnitTests;

public class FinanceiroControllerTests
{
    private static Mock<IFinanceiroService> CreateFinanceiroServiceMock()
    {
        var mock = new Mock<IFinanceiroService>();
        mock.Setup(s => s.GetAllAsync(It.IsAny<Guid>(), It.IsAny<TipoLancamento?>(), It.IsAny<StatusLancamento?>(),
            It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LancamentosPagedDto(Enumerable.Empty<LancamentoDto>(), 0));
        mock.Setup(s => s.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((LancamentoDto?)null);
        mock.Setup(s => s.CriarAsync(It.IsAny<Guid>(), It.IsAny<CriarLancamentoDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LancamentoDto(Guid.NewGuid(), TipoLancamento.Receita, "Categoria", 100m, "Desc", DateTime.UtcNow, null, StatusLancamento.Pendente, null, null, null, null, DateTime.UtcNow));
        mock.Setup(s => s.AtualizarAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<AtualizarLancamentoDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LancamentoDto(Guid.NewGuid(), TipoLancamento.Receita, "Categoria", 100m, "Desc", DateTime.UtcNow, null, StatusLancamento.Pendente, null, null, null, null, DateTime.UtcNow));
        mock.Setup(s => s.PagarAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        mock.Setup(s => s.CancelarAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        mock.Setup(s => s.GetResumoCompletoAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ResumoFinanceiroCompletoDto(
                new ResumoFinanceiroDto(0, 0, 0, 0, 0, 0, 0),
                new ResumoFinanceiroDto(0, 0, 0, 0, 0, 0, 0)));
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

    private static Mock<IAuditService> CreateAuditServiceMock()
    {
        var mock = new Mock<IAuditService>();
        mock.Setup(a => a.LogAsync(It.IsAny<AuditLogEntry>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        return mock;
    }

    private static ControllerContext CreateControllerContext()
    {
        var httpContext = new DefaultHttpContext();
        return new ControllerContext { HttpContext = httpContext };
    }

    [Fact]
    public async Task GetAll_ReturnsOk_WhenProPlan()
    {
        var service = CreateFinanceiroServiceMock();
        var tenantContext = CreateTenantContextMock(PlanoTipo.Pro);
        var audit = CreateAuditServiceMock();
        var controller = new FinanceiroController(service.Object, tenantContext.Object, audit.Object) { ControllerContext = CreateControllerContext() };

        var result = await controller.GetAll(null, null, null, null, 1, 20, null, null, CancellationToken.None);

        Assert.IsType<OkObjectResult>(result.Result);
    }

    [Fact]
    public async Task GetAll_Returns402_WhenFreePlan()
    {
        var service = CreateFinanceiroServiceMock();
        var tenantContext = CreateTenantContextMock(PlanoTipo.Free);
        var audit = CreateAuditServiceMock();
        var controller = new FinanceiroController(service.Object, tenantContext.Object, audit.Object) { ControllerContext = CreateControllerContext() };

        var result = await controller.GetAll(null, null, null, null, 1, 20, null, null, CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(402, objectResult.StatusCode);
    }

    [Fact]
    public async Task GetResumo_ReturnsOk_WhenProPlan()
    {
        var service = CreateFinanceiroServiceMock();
        var tenantContext = CreateTenantContextMock(PlanoTipo.Pro);
        var audit = CreateAuditServiceMock();
        var controller = new FinanceiroController(service.Object, tenantContext.Object, audit.Object) { ControllerContext = CreateControllerContext() };

        var result = await controller.GetResumo(2024, 5, CancellationToken.None);

        Assert.IsType<OkObjectResult>(result.Result);
    }

    [Fact]
    public async Task GetResumo_Returns402_WhenFreePlan()
    {
        var service = CreateFinanceiroServiceMock();
        var tenantContext = CreateTenantContextMock(PlanoTipo.Free);
        var audit = CreateAuditServiceMock();
        var controller = new FinanceiroController(service.Object, tenantContext.Object, audit.Object) { ControllerContext = CreateControllerContext() };

        var result = await controller.GetResumo(null, null, CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(402, objectResult.StatusCode);
    }

    [Fact]
    public async Task GetById_ExistingLancamento_ReturnsOk()
    {
        var service = CreateFinanceiroServiceMock();
        var tenantContext = CreateTenantContextMock(PlanoTipo.Pro);
        var audit = CreateAuditServiceMock();
        var controller = new FinanceiroController(service.Object, tenantContext.Object, audit.Object) { ControllerContext = CreateControllerContext() };
        var lancamentoId = Guid.NewGuid();
        var lancamento = new LancamentoDto(lancamentoId, TipoLancamento.Receita, "Categoria", 100m, "Desc", DateTime.UtcNow, null, StatusLancamento.Pendente, null, null, null, null, DateTime.UtcNow);
        service.Setup(s => s.GetByIdAsync(lancamentoId, tenantContext.Object.TenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(lancamento);

        var result = await controller.GetById(lancamentoId, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal(lancamentoId, ((LancamentoDto)okResult.Value!).Id);
    }

    [Fact]
    public async Task GetById_NonExistingLancamento_ReturnsNotFound()
    {
        var service = CreateFinanceiroServiceMock();
        var tenantContext = CreateTenantContextMock(PlanoTipo.Pro);
        var audit = CreateAuditServiceMock();
        var controller = new FinanceiroController(service.Object, tenantContext.Object, audit.Object) { ControllerContext = CreateControllerContext() };

        var result = await controller.GetById(Guid.NewGuid(), CancellationToken.None);

        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task GetById_Returns402_WhenFreePlan()
    {
        var service = CreateFinanceiroServiceMock();
        var tenantContext = CreateTenantContextMock(PlanoTipo.Free);
        var audit = CreateAuditServiceMock();
        var controller = new FinanceiroController(service.Object, tenantContext.Object, audit.Object) { ControllerContext = CreateControllerContext() };

        var result = await controller.GetById(Guid.NewGuid(), CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(402, objectResult.StatusCode);
    }

    [Fact]
    public async Task Criar_ReturnsCreatedAtAction_WhenProPlan()
    {
        var service = CreateFinanceiroServiceMock();
        var tenantContext = CreateTenantContextMock(PlanoTipo.Pro);
        var audit = CreateAuditServiceMock();
        var controller = new FinanceiroController(service.Object, tenantContext.Object, audit.Object) { ControllerContext = CreateControllerContext() };
        var dto = new CriarLancamentoDto(TipoLancamento.Receita, "Categoria", 100m, DateTime.UtcNow);

        var result = await controller.Criar(dto, CancellationToken.None);

        var createdResult = Assert.IsType<CreatedAtActionResult>(result.Result);
        Assert.Equal(nameof(FinanceiroController.GetById), createdResult.ActionName);
    }

    [Fact]
    public async Task Criar_Returns402_WhenFreePlan()
    {
        var service = CreateFinanceiroServiceMock();
        var tenantContext = CreateTenantContextMock(PlanoTipo.Free);
        var audit = CreateAuditServiceMock();
        var controller = new FinanceiroController(service.Object, tenantContext.Object, audit.Object) { ControllerContext = CreateControllerContext() };
        var dto = new CriarLancamentoDto(TipoLancamento.Receita, "Categoria", 100m, DateTime.UtcNow);

        var result = await controller.Criar(dto, CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(402, objectResult.StatusCode);
    }

    [Fact]
    public async Task Atualizar_ReturnsOk_WhenProPlan()
    {
        var service = CreateFinanceiroServiceMock();
        var tenantContext = CreateTenantContextMock(PlanoTipo.Pro);
        var audit = CreateAuditServiceMock();
        var controller = new FinanceiroController(service.Object, tenantContext.Object, audit.Object) { ControllerContext = CreateControllerContext() };
        var dto = new AtualizarLancamentoDto("Nova Categoria", 200m, DateTime.UtcNow, "Nova Desc");

        var result = await controller.Atualizar(Guid.NewGuid(), dto, CancellationToken.None);

        Assert.IsType<OkObjectResult>(result.Result);
    }

    [Fact]
    public async Task Atualizar_Returns402_WhenFreePlan()
    {
        var service = CreateFinanceiroServiceMock();
        var tenantContext = CreateTenantContextMock(PlanoTipo.Free);
        var audit = CreateAuditServiceMock();
        var controller = new FinanceiroController(service.Object, tenantContext.Object, audit.Object) { ControllerContext = CreateControllerContext() };
        var dto = new AtualizarLancamentoDto("Nova Categoria", 200m, DateTime.UtcNow, "Nova Desc");

        var result = await controller.Atualizar(Guid.NewGuid(), dto, CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(402, objectResult.StatusCode);
    }

    [Fact]
    public async Task Atualizar_ReturnsNotFound_WhenKeyNotFoundException()
    {
        var service = CreateFinanceiroServiceMock();
        var tenantContext = CreateTenantContextMock(PlanoTipo.Pro);
        var audit = CreateAuditServiceMock();
        var controller = new FinanceiroController(service.Object, tenantContext.Object, audit.Object) { ControllerContext = CreateControllerContext() };
        var lancamentoId = Guid.NewGuid();
        service.Setup(s => s.AtualizarAsync(lancamentoId, tenantContext.Object.TenantId, It.IsAny<AtualizarLancamentoDto>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new KeyNotFoundException("Not found"));
        var dto = new AtualizarLancamentoDto("Nova Categoria", 200m, DateTime.UtcNow, "Nova Desc");

        var result = await controller.Atualizar(lancamentoId, dto, CancellationToken.None);

        Assert.IsType<NotFoundObjectResult>(result.Result);
    }

    [Fact]
    public async Task Pagar_ReturnsNoContent_WhenProPlan()
    {
        var service = CreateFinanceiroServiceMock();
        var tenantContext = CreateTenantContextMock(PlanoTipo.Pro);
        var audit = CreateAuditServiceMock();
        var controller = new FinanceiroController(service.Object, tenantContext.Object, audit.Object) { ControllerContext = CreateControllerContext() };
        var lancamentoId = Guid.NewGuid();
        var existing = new LancamentoDto(lancamentoId, TipoLancamento.Receita, "Categoria", 100m, "Desc", DateTime.UtcNow, null, StatusLancamento.Pendente, null, null, null, null, DateTime.UtcNow);
        service.Setup(s => s.GetByIdAsync(lancamentoId, tenantContext.Object.TenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);
        var dto = new PagarDto(DateTime.UtcNow);

        var result = await controller.Pagar(lancamentoId, dto, CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
    }

    [Fact]
    public async Task Pagar_Returns402_WhenFreePlan()
    {
        var service = CreateFinanceiroServiceMock();
        var tenantContext = CreateTenantContextMock(PlanoTipo.Free);
        var audit = CreateAuditServiceMock();
        var controller = new FinanceiroController(service.Object, tenantContext.Object, audit.Object) { ControllerContext = CreateControllerContext() };

        var result = await controller.Pagar(Guid.NewGuid(), null, CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(402, objectResult.StatusCode);
    }

    [Fact]
    public async Task Pagar_ReturnsNotFound_WhenKeyNotFoundException()
    {
        var service = CreateFinanceiroServiceMock();
        var tenantContext = CreateTenantContextMock(PlanoTipo.Pro);
        var audit = CreateAuditServiceMock();
        var controller = new FinanceiroController(service.Object, tenantContext.Object, audit.Object) { ControllerContext = CreateControllerContext() };
        var lancamentoId = Guid.NewGuid();
        service.Setup(s => s.PagarAsync(lancamentoId, tenantContext.Object.TenantId, It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new KeyNotFoundException("Not found"));

        var result = await controller.Pagar(lancamentoId, null, CancellationToken.None);

        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public async Task Cancelar_ReturnsNoContent_WhenProPlan()
    {
        var service = CreateFinanceiroServiceMock();
        var tenantContext = CreateTenantContextMock(PlanoTipo.Pro);
        var audit = CreateAuditServiceMock();
        var controller = new FinanceiroController(service.Object, tenantContext.Object, audit.Object) { ControllerContext = CreateControllerContext() };
        var lancamentoId = Guid.NewGuid();
        var existing = new LancamentoDto(lancamentoId, TipoLancamento.Receita, "Categoria", 100m, "Desc", DateTime.UtcNow, null, StatusLancamento.Pendente, null, null, null, null, DateTime.UtcNow);
        service.Setup(s => s.GetByIdAsync(lancamentoId, tenantContext.Object.TenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        var result = await controller.Cancelar(lancamentoId, CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
    }

    [Fact]
    public async Task Cancelar_Returns402_WhenFreePlan()
    {
        var service = CreateFinanceiroServiceMock();
        var tenantContext = CreateTenantContextMock(PlanoTipo.Free);
        var audit = CreateAuditServiceMock();
        var controller = new FinanceiroController(service.Object, tenantContext.Object, audit.Object) { ControllerContext = CreateControllerContext() };

        var result = await controller.Cancelar(Guid.NewGuid(), CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(402, objectResult.StatusCode);
    }

    [Fact]
    public async Task Cancelar_ReturnsNotFound_WhenKeyNotFoundException()
    {
        var service = CreateFinanceiroServiceMock();
        var tenantContext = CreateTenantContextMock(PlanoTipo.Pro);
        var audit = CreateAuditServiceMock();
        var controller = new FinanceiroController(service.Object, tenantContext.Object, audit.Object) { ControllerContext = CreateControllerContext() };
        var lancamentoId = Guid.NewGuid();
        service.Setup(s => s.CancelarAsync(lancamentoId, tenantContext.Object.TenantId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new KeyNotFoundException("Not found"));

        var result = await controller.Cancelar(lancamentoId, CancellationToken.None);

        Assert.IsType<NotFoundObjectResult>(result);
    }
}