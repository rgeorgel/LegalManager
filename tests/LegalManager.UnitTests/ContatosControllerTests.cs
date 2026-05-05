using LegalManager.API.Controllers;
using LegalManager.Application.DTOs.Contatos;
using LegalManager.Application.DTOs.PortalCliente;
using LegalManager.Application.Interfaces;
using LegalManager.Domain.Enums;
using LegalManager.Domain.Interfaces;
using LegalManager.Infrastructure.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace LegalManager.UnitTests;

public class ContatosControllerTests
{
    private static Mock<IContatoService> CreateContatoServiceMock()
    {
        var mock = new Mock<IContatoService>();
        mock.Setup(s => s.GetAllAsync(It.IsAny<ContatoFiltroDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedResultDto<ContatoListItemDto>(Enumerable.Empty<ContatoListItemDto>(), 0, 0, 1, 20));
        mock.Setup(s => s.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ContatoResponseDto?)null);
        mock.Setup(s => s.CreateAsync(It.IsAny<CreateContatoDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ContatoResponseDto(Guid.NewGuid(), TipoPessoa.PF, TipoContato.Cliente, "Test", null, null, null, null, null, null, null, null, null, null, false, true, new List<string>(), DateTime.UtcNow));
        mock.Setup(s => s.UpdateAsync(It.IsAny<Guid>(), It.IsAny<UpdateContatoDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ContatoResponseDto(Guid.NewGuid(), TipoPessoa.PF, TipoContato.Cliente, "Test Updated", null, null, null, null, null, null, null, null, null, null, false, true, new List<string>(), DateTime.UtcNow));
        mock.Setup(s => s.DeleteAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        mock.Setup(s => s.GetAtendimentosAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Enumerable.Empty<AtendimentoResponseDto>());
        mock.Setup(s => s.AddAtendimentoAsync(It.IsAny<Guid>(), It.IsAny<CreateAtendimentoDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AtendimentoResponseDto(Guid.NewGuid(), "Test", DateTime.UtcNow, Guid.NewGuid(), "User", DateTime.UtcNow));
        return mock;
    }

    private static Mock<IPortalClienteService> CreatePortalServiceMock()
    {
        var mock = new Mock<IPortalClienteService>();
        mock.Setup(s => s.CriarAcessoAsync(It.IsAny<Guid>(), It.IsAny<CriarAcessoPortalDto>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AcessoPortalInfoDto(Guid.NewGuid(), Guid.NewGuid(), "test@test.com", true, DateTime.UtcNow, null));
        mock.Setup(s => s.GetAcessoAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((AcessoPortalInfoDto?)null);
        mock.Setup(s => s.RevogarAcessoAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
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

    private static Mock<IAuditService> CreateAuditServiceMock()
    {
        var mock = new Mock<IAuditService>();
        mock.Setup(a => a.LogAsync(It.IsAny<AuditLogEntry>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        return mock;
    }

    [Fact]
    public async Task GetAll_ReturnsOkWithPagedResult()
    {
        var service = CreateContatoServiceMock();
        var portalService = CreatePortalServiceMock();
        var tenantContext = CreateTenantContextMock();
        var audit = CreateAuditServiceMock();
        var controller = new ContatosController(service.Object, portalService.Object, tenantContext.Object, audit.Object);

        var result = await controller.GetAll(null, null, null, null, null, 1, 20, CancellationToken.None);

        Assert.IsType<OkObjectResult>(result.Result);
    }

    [Fact]
    public async Task GetAll_WithFilters_PassesCorrectParameters()
    {
        var service = CreateContatoServiceMock();
        var portalService = CreatePortalServiceMock();
        var tenantContext = CreateTenantContextMock();
        var audit = CreateAuditServiceMock();
        var controller = new ContatosController(service.Object, portalService.Object, tenantContext.Object, audit.Object);

        await controller.GetAll("busca", "Cliente", "PF", "tag1", true, 2, 50, CancellationToken.None);

        service.Verify(s => s.GetAllAsync(
            It.Is<ContatoFiltroDto>(f =>
                f.Busca == "busca" &&
                f.TipoContato == TipoContato.Cliente &&
                f.Tipo == TipoPessoa.PF &&
                f.Tag == "tag1" &&
                f.Ativo == true &&
                f.Page == 2 &&
                f.PageSize == 50),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetAll_WithInvalidEnumValues_IgnoresThem()
    {
        var service = CreateContatoServiceMock();
        var portalService = CreatePortalServiceMock();
        var tenantContext = CreateTenantContextMock();
        var audit = CreateAuditServiceMock();
        var controller = new ContatosController(service.Object, portalService.Object, tenantContext.Object, audit.Object);

        await controller.GetAll(null, "InvalidTipoContato", "InvalidTipo", null, null, 1, 20, CancellationToken.None);

        service.Verify(s => s.GetAllAsync(
            It.Is<ContatoFiltroDto>(f => f.TipoContato == null && f.Tipo == null),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetById_ExistingContato_ReturnsOk()
    {
        var service = CreateContatoServiceMock();
        var portalService = CreatePortalServiceMock();
        var tenantContext = CreateTenantContextMock();
        var audit = CreateAuditServiceMock();
        var controller = new ContatosController(service.Object, portalService.Object, tenantContext.Object, audit.Object);
        var contatoId = Guid.NewGuid();
        var contato = new ContatoResponseDto(contatoId, TipoPessoa.PF, TipoContato.Cliente, "Test", null, null, null, null, null, null, null, null, null, null, false, true, new List<string>(), DateTime.UtcNow);
        service.Setup(s => s.GetByIdAsync(contatoId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(contato);

        var result = await controller.GetById(contatoId, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal(contatoId, ((ContatoResponseDto)okResult.Value!).Id);
    }

    [Fact]
    public async Task GetById_NonExistingContato_ReturnsNotFound()
    {
        var service = CreateContatoServiceMock();
        var portalService = CreatePortalServiceMock();
        var tenantContext = CreateTenantContextMock();
        var audit = CreateAuditServiceMock();
        var controller = new ContatosController(service.Object, portalService.Object, tenantContext.Object, audit.Object);

        var result = await controller.GetById(Guid.NewGuid(), CancellationToken.None);

        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task Create_ValidDto_ReturnsCreatedAtAction()
    {
        var service = CreateContatoServiceMock();
        var portalService = CreatePortalServiceMock();
        var tenantContext = CreateTenantContextMock();
        var audit = CreateAuditServiceMock();
        var controller = new ContatosController(service.Object, portalService.Object, tenantContext.Object, audit.Object);
        var dto = new CreateContatoDto(TipoPessoa.PF, TipoContato.Cliente, "New Contato", null, null, "test@test.com", null, null, null, null, null, null, null, false, null);

        var result = await controller.Create(dto, CancellationToken.None);

        var createdResult = Assert.IsType<CreatedAtActionResult>(result.Result);
        Assert.Equal(nameof(ContatosController.GetById), createdResult.ActionName);
    }

    [Fact]
    public async Task Update_ExistingContato_ReturnsOk()
    {
        var service = CreateContatoServiceMock();
        var portalService = CreatePortalServiceMock();
        var tenantContext = CreateTenantContextMock();
        var audit = CreateAuditServiceMock();
        var controller = new ContatosController(service.Object, portalService.Object, tenantContext.Object, audit.Object);
        var processoId = Guid.NewGuid();
        var existingContato = new ContatoResponseDto(processoId, TipoPessoa.PF, TipoContato.Cliente, "Old", null, null, null, null, null, null, null, null, null, null, false, true, new List<string>(), DateTime.UtcNow);
        service.Setup(s => s.GetByIdAsync(processoId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingContato);
        var dto = new UpdateContatoDto(TipoPessoa.PF, TipoContato.Cliente, "Updated", null, null, null, null, null, null, null, null, null, null, false, null);

        var result = await controller.Update(processoId, dto, CancellationToken.None);

        Assert.IsType<OkObjectResult>(result.Result);
    }

    [Fact]
    public async Task Delete_ExistingContato_ReturnsNoContent()
    {
        var service = CreateContatoServiceMock();
        var portalService = CreatePortalServiceMock();
        var tenantContext = CreateTenantContextMock();
        var audit = CreateAuditServiceMock();
        var controller = new ContatosController(service.Object, portalService.Object, tenantContext.Object, audit.Object);
        var contatoId = Guid.NewGuid();
        var existingContato = new ContatoResponseDto(contatoId, TipoPessoa.PF, TipoContato.Cliente, "Test", null, null, null, null, null, null, null, null, null, null, false, true, new List<string>(), DateTime.UtcNow);
        service.Setup(s => s.GetByIdAsync(contatoId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingContato);

        var result = await controller.Delete(contatoId, CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
    }

    [Fact]
    public async Task GetAtendimentos_ReturnsOk()
    {
        var service = CreateContatoServiceMock();
        var portalService = CreatePortalServiceMock();
        var tenantContext = CreateTenantContextMock();
        var audit = CreateAuditServiceMock();
        var controller = new ContatosController(service.Object, portalService.Object, tenantContext.Object, audit.Object);

        var result = await controller.GetAtendimentos(Guid.NewGuid(), CancellationToken.None);

        Assert.IsType<OkObjectResult>(result.Result);
    }

    [Fact]
    public async Task AddAtendimento_ReturnsOk()
    {
        var service = CreateContatoServiceMock();
        var portalService = CreatePortalServiceMock();
        var tenantContext = CreateTenantContextMock();
        var audit = CreateAuditServiceMock();
        var controller = new ContatosController(service.Object, portalService.Object, tenantContext.Object, audit.Object);
        var dto = new CreateAtendimentoDto("New atendimento", DateTime.UtcNow);

        var result = await controller.AddAtendimento(Guid.NewGuid(), dto, CancellationToken.None);

        Assert.IsType<OkObjectResult>(result.Result);
    }

    [Fact]
    public async Task CriarPortalAcesso_ReturnsOk()
    {
        var service = CreateContatoServiceMock();
        var portalService = CreatePortalServiceMock();
        var tenantContext = CreateTenantContextMock();
        var audit = CreateAuditServiceMock();
        var controller = new ContatosController(service.Object, portalService.Object, tenantContext.Object, audit.Object);
        var dto = new CriarAcessoPortalDto("test@test.com", "password123");

        var result = await controller.CriarPortalAcesso(Guid.NewGuid(), dto, CancellationToken.None);

        Assert.IsType<OkObjectResult>(result.Result);
    }

    [Fact]
    public async Task GetPortalAcesso_Existing_ReturnsOk()
    {
        var service = CreateContatoServiceMock();
        var portalService = CreatePortalServiceMock();
        var tenantContext = CreateTenantContextMock();
        var audit = CreateAuditServiceMock();
        var controller = new ContatosController(service.Object, portalService.Object, tenantContext.Object, audit.Object);
        var contatoId = Guid.NewGuid();
        var acesso = new AcessoPortalInfoDto(Guid.NewGuid(), contatoId, "test@test.com", true, DateTime.UtcNow, null);
        portalService.Setup(s => s.GetAcessoAsync(contatoId, tenantContext.Object.TenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(acesso);

        var result = await controller.GetPortalAcesso(contatoId, CancellationToken.None);

        Assert.IsType<OkObjectResult>(result.Result);
    }

    [Fact]
    public async Task GetPortalAcesso_NotFound_ReturnsNotFound()
    {
        var service = CreateContatoServiceMock();
        var portalService = CreatePortalServiceMock();
        var tenantContext = CreateTenantContextMock();
        var audit = CreateAuditServiceMock();
        var controller = new ContatosController(service.Object, portalService.Object, tenantContext.Object, audit.Object);

        var result = await controller.GetPortalAcesso(Guid.NewGuid(), CancellationToken.None);

        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task RevogarPortalAcesso_ReturnsNoContent()
    {
        var service = CreateContatoServiceMock();
        var portalService = CreatePortalServiceMock();
        var tenantContext = CreateTenantContextMock();
        var audit = CreateAuditServiceMock();
        var controller = new ContatosController(service.Object, portalService.Object, tenantContext.Object, audit.Object);

        var result = await controller.RevogarPortalAcesso(Guid.NewGuid(), CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
    }
}