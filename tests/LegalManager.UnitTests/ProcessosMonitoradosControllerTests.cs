using LegalManager.API.Controllers;
using LegalManager.Application.DTOs.Processos;
using LegalManager.Application.Interfaces;
using LegalManager.Infrastructure.Tribunais;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;

namespace LegalManager.UnitTests;

public class ProcessosMonitoradosControllerTests
{
    private static Mock<IProcessoMonitoradoService> CreateServiceMock()
    {
        var mock = new Mock<IProcessoMonitoradoService>();
        mock.Setup(s => s.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Enumerable.Empty<ProcessoMonitoradoResponseDto>());
        mock.Setup(s => s.CreateAsync(It.IsAny<CreateProcessoMonitoradoDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProcessoMonitoradoCreateResultDto(Guid.NewGuid(), "1234567-89.2024.1.01.0001", "Teste", true, DateTime.UtcNow, true, "TJSP", "Central", null));
        mock.Setup(s => s.ToggleAtivoAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        mock.Setup(s => s.DeleteAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        return mock;
    }

    private static DataJudAdapter CreateDataJudAdapter()
    {
        var httpClient = new HttpClient() { BaseAddress = new Uri("https://api.cnj.jus.br") };
        var logger = Mock.Of<ILogger<DataJudAdapter>>();
        return new DataJudAdapter(httpClient, logger);
    }

    private static Mock<ILogger<ProcessosMonitoradosController>> CreateLoggerMock()
    {
        return new Mock<ILogger<ProcessosMonitoradosController>>();
    }

    [Fact]
    public async Task GetAll_ReturnsOkWithProcessos()
    {
        var service = CreateServiceMock();
        var dataJud = CreateDataJudAdapter();
        var logger = CreateLoggerMock();
        var controller = new ProcessosMonitoradosController(service.Object, dataJud, logger.Object);

        var result = await controller.GetAll(CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);
    }

    [Fact]
    public async Task Search_WithEmptyCNJ_ReturnsBadRequest()
    {
        var service = CreateServiceMock();
        var dataJud = CreateDataJudAdapter();
        var logger = CreateLoggerMock();
        var controller = new ProcessosMonitoradosController(service.Object, dataJud, logger.Object);

        var result = await controller.Search("", null, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task Search_WithWhitespaceCNJ_ReturnsBadRequest()
    {
        var service = CreateServiceMock();
        var dataJud = CreateDataJudAdapter();
        var logger = CreateLoggerMock();
        var controller = new ProcessosMonitoradosController(service.Object, dataJud, logger.Object);

        var result = await controller.Search("   ", null, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task Search_WithCNJTooShort_ReturnsBadRequest()
    {
        var service = CreateServiceMock();
        var dataJud = CreateDataJudAdapter();
        var logger = CreateLoggerMock();
        var controller = new ProcessosMonitoradosController(service.Object, dataJud, logger.Object);

        var result = await controller.Search("12345", null, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Theory]
    [InlineData("12345")]
    [InlineData("123456")]
    [InlineData("1")]
    [InlineData("ab")]
    public async Task Search_WithCNJTooShortVariousFormats_ReturnsBadRequest(string cnj)
    {
        var service = CreateServiceMock();
        var dataJud = CreateDataJudAdapter();
        var logger = CreateLoggerMock();
        var controller = new ProcessosMonitoradosController(service.Object, dataJud, logger.Object);

        var result = await controller.Search(cnj, null, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task Create_ValidDto_ReturnsCreatedAtAction()
    {
        var service = CreateServiceMock();
        var dataJud = CreateDataJudAdapter();
        var logger = CreateLoggerMock();
        var controller = new ProcessosMonitoradosController(service.Object, dataJud, logger.Object);
        var dto = new CreateProcessoMonitoradoDto("1234567-89.2024.1.01.0001", "Teste", null);

        var result = await controller.Create(dto, CancellationToken.None);

        var createdResult = Assert.IsType<CreatedAtActionResult>(result);
        Assert.Equal(nameof(ProcessosMonitoradosController.GetAll), createdResult.ActionName);
    }

    [Fact]
    public async Task Create_WithInvalidOperation_ReturnsBadRequest()
    {
        var service = CreateServiceMock();
        var dataJud = CreateDataJudAdapter();
        var logger = CreateLoggerMock();
        var controller = new ProcessosMonitoradosController(service.Object, dataJud, logger.Object);
        var dto = new CreateProcessoMonitoradoDto("1234567-89.2024.1.01.0001", "Teste", null);

        service.Setup(s => s.CreateAsync(dto, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("CNJ ja cadastrado"));

        var result = await controller.Create(dto, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task Toggle_ExistingId_ReturnsNoContent()
    {
        var service = CreateServiceMock();
        var dataJud = CreateDataJudAdapter();
        var logger = CreateLoggerMock();
        var controller = new ProcessosMonitoradosController(service.Object, dataJud, logger.Object);

        var result = await controller.Toggle(Guid.NewGuid(), CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
    }

    [Fact]
    public async Task Toggle_NonExistingId_ReturnsNotFound()
    {
        var service = CreateServiceMock();
        var dataJud = CreateDataJudAdapter();
        var logger = CreateLoggerMock();
        var controller = new ProcessosMonitoradosController(service.Object, dataJud, logger.Object);
        var id = Guid.NewGuid();

        service.Setup(s => s.ToggleAtivoAsync(id, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new KeyNotFoundException());

        var result = await controller.Toggle(id, CancellationToken.None);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task Delete_ExistingId_ReturnsNoContent()
    {
        var service = CreateServiceMock();
        var dataJud = CreateDataJudAdapter();
        var logger = CreateLoggerMock();
        var controller = new ProcessosMonitoradosController(service.Object, dataJud, logger.Object);

        var result = await controller.Delete(Guid.NewGuid(), CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
    }

    [Fact]
    public async Task Delete_NonExistingId_ReturnsNotFound()
    {
        var service = CreateServiceMock();
        var dataJud = CreateDataJudAdapter();
        var logger = CreateLoggerMock();
        var controller = new ProcessosMonitoradosController(service.Object, dataJud, logger.Object);
        var id = Guid.NewGuid();

        service.Setup(s => s.DeleteAsync(id, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new KeyNotFoundException());

        var result = await controller.Delete(id, CancellationToken.None);

        Assert.IsType<NotFoundResult>(result);
    }
}