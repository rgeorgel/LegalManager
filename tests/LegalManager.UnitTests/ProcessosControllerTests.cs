using LegalManager.API.Controllers;
using LegalManager.Application.DTOs.Contatos;
using LegalManager.Application.DTOs.Monitoramento;
using LegalManager.Application.DTOs.Processos;
using LegalManager.Application.Interfaces;
using LegalManager.Domain.Enums;
using LegalManager.Domain.Interfaces;
using LegalManager.Infrastructure.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace LegalManager.UnitTests;

public class ProcessosControllerTests
{
    private static Mock<IProcessoService> CreateProcessoServiceMock()
    {
        var mock = new Mock<IProcessoService>();
        mock.Setup(s => s.GetAllAsync(It.IsAny<ProcessoFiltroDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedResultDto<ProcessoListItemDto>(Enumerable.Empty<ProcessoListItemDto>(), 0, 0, 1, 20));
        mock.Setup(s => s.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ProcessoResponseDto?)null);
        mock.Setup(s => s.CreateAsync(It.IsAny<CreateProcessoDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateProcessoResponseDto(Guid.NewGuid()));
        mock.Setup(s => s.UpdateAsync(It.IsAny<Guid>(), It.IsAny<UpdateProcessoDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateProcessoResponseDto(Guid.NewGuid()));
        mock.Setup(s => s.DeleteAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        mock.Setup(s => s.EncerrarAsync(It.IsAny<Guid>(), It.IsAny<EncerrarProcessoDto>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        mock.Setup(s => s.GetAndamentosAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Enumerable.Empty<AndamentoResponseDto>());
        mock.Setup(s => s.AddAndamentoAsync(It.IsAny<Guid>(), It.IsAny<CreateAndamentoDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AndamentoResponseDto(Guid.NewGuid(), DateTime.UtcNow, TipoAndamento.Despacho, "Andamento", FonteAndamento.Manual, null, null, null, DateTime.UtcNow, null, null, null, true));
        mock.Setup(s => s.DeleteAndamentoAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        mock.Setup(s => s.AdicionarParteAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        mock.Setup(s => s.RemoverParteAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        return mock;
    }

    private static Mock<IMonitoramentoService> CreateMonitoramentoServiceMock()
    {
        var mock = new Mock<IMonitoramentoService>();
        mock.Setup(m => m.AlternarMonitoramentoAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        mock.Setup(m => m.MonitorarProcessoAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MonitoramentoResultDto(Guid.NewGuid(), "1234567-89.2024.1.01.0001", true, 0, null, DateTime.UtcNow));
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

private static ProcessoResponseDto CreateProcessoResponseDto(Guid id) =>
        new(
            id,
            "1234567-89.2024.1.01.0001",
            null,
            null,
            null,
            AreaDireito.Civil,
            null,
            FaseProcessual.Conhecimento,
            StatusProcesso.Ativo,
            10000m,
            null,
            null,
            false,
            null,
            null,
            null,
            DateTime.UtcNow,
            null,
            new List<ProcessoParteResponseDto>(),
            0,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            DateTime.UtcNow
        );

    [Fact]
    public async Task GetAll_ReturnsOkWithPagedResult()
    {
        var service = CreateProcessoServiceMock();
        var monitoramento = CreateMonitoramentoServiceMock();
        var audit = CreateAuditServiceMock();
        var tenantContext = CreateTenantContextMock();
        var controller = new ProcessosController(service.Object, monitoramento.Object, audit.Object, tenantContext.Object, null!, null!, Mock.Of<IContatoResolverService>());

        var result = await controller.GetAll(null, null, null, null, null, 1, 20, CancellationToken.None);

        Assert.IsType<OkObjectResult>(result.Result);
    }

    [Fact]
    public async Task GetAll_WithFilters_PassesCorrectParameters()
    {
        var service = CreateProcessoServiceMock();
        var monitoramento = CreateMonitoramentoServiceMock();
        var audit = CreateAuditServiceMock();
        var tenantContext = CreateTenantContextMock();
        var controller = new ProcessosController(service.Object, monitoramento.Object, audit.Object, tenantContext.Object, null!, null!, Mock.Of<IContatoResolverService>());
        var advogadoId = Guid.NewGuid();
        var contatoId = Guid.NewGuid();

        await controller.GetAll("busca", "Ativo", "Civil", advogadoId, contatoId, 2, 50, CancellationToken.None);

        service.Verify(s => s.GetAllAsync(
            It.Is<ProcessoFiltroDto>(f =>
                f.Busca == "busca" &&
                f.Status == StatusProcesso.Ativo &&
                f.AreaDireito == AreaDireito.Civil &&
                f.AdvogadoResponsavelId == advogadoId &&
                f.ContatoId == contatoId &&
                f.Page == 2 &&
                f.PageSize == 50),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetAll_WithInvalidEnumValues_IgnoresThem()
    {
        var service = CreateProcessoServiceMock();
        var monitoramento = CreateMonitoramentoServiceMock();
        var audit = CreateAuditServiceMock();
        var tenantContext = CreateTenantContextMock();
        var controller = new ProcessosController(service.Object, monitoramento.Object, audit.Object, tenantContext.Object, null!, null!, Mock.Of<IContatoResolverService>());

        await controller.GetAll("busca", "InvalidStatus", "InvalidArea", null, null, 1, 20, CancellationToken.None);

        service.Verify(s => s.GetAllAsync(
            It.Is<ProcessoFiltroDto>(f => f.Status == null && f.AreaDireito == null),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetById_ExistingProcesso_ReturnsOk()
    {
        var service = CreateProcessoServiceMock();
        var monitoramento = CreateMonitoramentoServiceMock();
        var audit = CreateAuditServiceMock();
        var tenantContext = CreateTenantContextMock();
        var controller = new ProcessosController(service.Object, monitoramento.Object, audit.Object, tenantContext.Object, null!, null!, Mock.Of<IContatoResolverService>());
        var processoId = Guid.NewGuid();
        var processo = CreateProcessoResponseDto(processoId);
        service.Setup(s => s.GetByIdAsync(processoId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(processo);

        var result = await controller.GetById(processoId, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal(processoId, ((ProcessoResponseDto)okResult.Value!).Id);
    }

    [Fact]
    public async Task GetById_NonExistingProcesso_ReturnsNotFound()
    {
        var service = CreateProcessoServiceMock();
        var monitoramento = CreateMonitoramentoServiceMock();
        var audit = CreateAuditServiceMock();
        var tenantContext = CreateTenantContextMock();
        var controller = new ProcessosController(service.Object, monitoramento.Object, audit.Object, tenantContext.Object, null!, null!, Mock.Of<IContatoResolverService>());

        var result = await controller.GetById(Guid.NewGuid(), CancellationToken.None);

        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task GetAndamentos_ReturnsOk()
    {
        var service = CreateProcessoServiceMock();
        var monitoramento = CreateMonitoramentoServiceMock();
        var audit = CreateAuditServiceMock();
        var tenantContext = CreateTenantContextMock();
        var controller = new ProcessosController(service.Object, monitoramento.Object, audit.Object, tenantContext.Object, null!, null!, Mock.Of<IContatoResolverService>());

        var result = await controller.GetAndamentos(Guid.NewGuid(), CancellationToken.None);

        Assert.IsType<OkObjectResult>(result.Result);
    }

    [Fact]
    public async Task Create_ValidDto_ReturnsCreatedAtAction()
    {
        var service = CreateProcessoServiceMock();
        var monitoramento = CreateMonitoramentoServiceMock();
        var audit = CreateAuditServiceMock();
        var tenantContext = CreateTenantContextMock();
        var controller = new ProcessosController(service.Object, monitoramento.Object, audit.Object, tenantContext.Object, null!, null!, Mock.Of<IContatoResolverService>());
        var dto = new CreateProcessoDto("1234567-89.2024.1.01.0001", null, null, null, AreaDireito.Civil, null, FaseProcessual.Conhecimento, StatusProcesso.Ativo, null, null, null, false, null, null, null, null, null, null, null, null, null, null);

        var result = await controller.Create(dto, CancellationToken.None);

        var createdResult = Assert.IsType<CreatedAtActionResult>(result.Result);
        Assert.Equal(nameof(ProcessosController.GetById), createdResult.ActionName);
    }

    [Fact]
    public async Task Update_ExistingProcesso_ReturnsOk()
    {
        var service = CreateProcessoServiceMock();
        var monitoramento = CreateMonitoramentoServiceMock();
        var audit = CreateAuditServiceMock();
        var tenantContext = CreateTenantContextMock();
        var controller = new ProcessosController(service.Object, monitoramento.Object, audit.Object, tenantContext.Object, null!, null!, Mock.Of<IContatoResolverService>());
        var processoId = Guid.NewGuid();
        var existing = CreateProcessoResponseDto(processoId);
        service.Setup(s => s.GetByIdAsync(processoId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);
        var dto = new UpdateProcessoDto("1234567-89.2024.1.01.0001", null, null, null, AreaDireito.Civil, null, FaseProcessual.Conhecimento, StatusProcesso.Ativo, null, null, null, null, null, null, null);

        var result = await controller.Update(processoId, dto, CancellationToken.None);

        Assert.IsType<OkObjectResult>(result.Result);
    }

    [Fact]
    public async Task Encerrar_ExistingProcesso_ReturnsNoContent()
    {
        var service = CreateProcessoServiceMock();
        var monitoramento = CreateMonitoramentoServiceMock();
        var audit = CreateAuditServiceMock();
        var tenantContext = CreateTenantContextMock();
        var controller = new ProcessosController(service.Object, monitoramento.Object, audit.Object, tenantContext.Object, null!, null!, Mock.Of<IContatoResolverService>());
        var processoId = Guid.NewGuid();
        var existing = CreateProcessoResponseDto(processoId);
        service.Setup(s => s.GetByIdAsync(processoId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);
        var dto = new EncerrarProcessoDto("Decisao", "Resultado");

        var result = await controller.Encerrar(processoId, dto, CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
    }

    [Fact]
    public async Task Delete_ExistingProcesso_ReturnsNoContent()
    {
        var service = CreateProcessoServiceMock();
        var monitoramento = CreateMonitoramentoServiceMock();
        var audit = CreateAuditServiceMock();
        var tenantContext = CreateTenantContextMock();
        var controller = new ProcessosController(service.Object, monitoramento.Object, audit.Object, tenantContext.Object, null!, null!, Mock.Of<IContatoResolverService>());
        var processoId = Guid.NewGuid();
        var existing = CreateProcessoResponseDto(processoId);
        service.Setup(s => s.GetByIdAsync(processoId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        var result = await controller.Delete(processoId, CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
    }

    [Fact]
    public async Task AddAndamento_ReturnsOk()
    {
        var service = CreateProcessoServiceMock();
        var monitoramento = CreateMonitoramentoServiceMock();
        var audit = CreateAuditServiceMock();
        var tenantContext = CreateTenantContextMock();
        var controller = new ProcessosController(service.Object, monitoramento.Object, audit.Object, tenantContext.Object, null!, null!, Mock.Of<IContatoResolverService>());
        var dto = new CreateAndamentoDto(DateTime.UtcNow, TipoAndamento.Despacho, "Novo andamento");

        var result = await controller.AddAndamento(Guid.NewGuid(), dto, CancellationToken.None);

        Assert.IsType<OkObjectResult>(result.Result);
    }

    [Fact]
    public async Task DeleteAndamento_ReturnsNoContent()
    {
        var service = CreateProcessoServiceMock();
        var monitoramento = CreateMonitoramentoServiceMock();
        var audit = CreateAuditServiceMock();
        var tenantContext = CreateTenantContextMock();
        var controller = new ProcessosController(service.Object, monitoramento.Object, audit.Object, tenantContext.Object, null!, null!, Mock.Of<IContatoResolverService>());

        var result = await controller.DeleteAndamento(Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
    }

    [Fact]
    public async Task AlternarMonitoramento_ReturnsOkWithStatus()
    {
        var service = CreateProcessoServiceMock();
        var monitoramento = CreateMonitoramentoServiceMock();
        var audit = CreateAuditServiceMock();
        var tenantContext = CreateTenantContextMock();
        var controller = new ProcessosController(service.Object, monitoramento.Object, audit.Object, tenantContext.Object, null!, null!, Mock.Of<IContatoResolverService>());

        var result = await controller.AlternarMonitoramento(Guid.NewGuid(), CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);
    }

    [Fact]
    public async Task ExecutarMonitoramento_ReturnsOk()
    {
        var monitoramento = CreateMonitoramentoServiceMock();
        var audit = CreateAuditServiceMock();
        var tenantContext = CreateTenantContextMock();

        var processoId = Guid.NewGuid();
        var processoMock = new Mock<IProcessoService>();
        processoMock.Setup(s => s.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProcessoResponseDto(
                Id: processoId,
                NumeroCNJ: "1234567-89.2024.1.01.0001",
                Tribunal: "TJMG",
                Vara: null,
                Comarca: null,
                AreaDireito: AreaDireito.Civil,
                TipoAcao: null,
                Fase: FaseProcessual.Conhecimento,
                Status: StatusProcesso.Ativo,
                ValorCausa: 10000m,
                AdvogadoResponsavelId: null,
                NomeAdvogadoResponsavel: null,
                Monitorado: false,
                Observacoes: null,
                Decisao: null,
                Resultado: null,
                CriadoEm: DateTime.UtcNow,
                EncerradoEm: null,
                Partes: new List<ProcessoParteResponseDto>(),
                TotalAndamentos: 0,
                Classe: null,
                Assuntos: null,
                DataAjuizamento: null,
                Grau: null,
                Sistema: null,
                Formato: null,
                NivelSigilo: null,
                UltimaAtualizacaoDataJud: null,
                Ementa: null,
                DecisaoDataJud: null,
                Observacao: null,
                Relator: null,
                TipoDecisao: null,
                ResultadoJulgamento: null,
                CodigoClasse: null,
                Instancia: null,
                DataJulgamento: null,
                DataPublicacao: null,
                SiglaTribunal: null,
                Segmento: null,
                DataDistribuicao: null,
                UltimoAndamentoEm: null
            ));

        var controller = new ProcessosController(processoMock.Object, monitoramento.Object, audit.Object, tenantContext.Object, null!, null!, Mock.Of<IContatoResolverService>());

        var result = await controller.ExecutarMonitoramento(processoId, CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);
    }

    [Fact]
    public async Task AdicionarParte_ReturnsOk()
    {
        var service = CreateProcessoServiceMock();
        var monitoramento = CreateMonitoramentoServiceMock();
        var audit = CreateAuditServiceMock();
        var tenantContext = CreateTenantContextMock();
        var controller = new ProcessosController(service.Object, monitoramento.Object, audit.Object, tenantContext.Object, null!, null!, Mock.Of<IContatoResolverService>());
        var dto = new AdicionarParteDto { ContatoId = Guid.NewGuid(), TipoParte = "Autor" };

        var result = await controller.AdicionarParte(Guid.NewGuid(), dto, CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);
    }

    [Fact]
    public async Task RemoverParte_ReturnsNoContent()
    {
        var service = CreateProcessoServiceMock();
        var monitoramento = CreateMonitoramentoServiceMock();
        var audit = CreateAuditServiceMock();
        var tenantContext = CreateTenantContextMock();
        var controller = new ProcessosController(service.Object, monitoramento.Object, audit.Object, tenantContext.Object, null!, null!, Mock.Of<IContatoResolverService>());

        var result = await controller.RemoverParte(Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
    }
}