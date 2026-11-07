using LegalManager.API.Controllers;
using LegalManager.Application.DTOs.Documentos;
using LegalManager.Application.Interfaces;
using LegalManager.Domain.Enums;
using LegalManager.Domain.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace LegalManager.UnitTests;

public class DocumentosControllerTests
{
    private static ITenantContext CreateTenantContext(Guid tenantId, Guid userId)
    {
        var mock = new Mock<ITenantContext>();
        mock.Setup(t => t.TenantId).Returns(tenantId);
        mock.Setup(t => t.UserId).Returns(userId);
        return mock.Object;
    }

    private static Mock<IDocumentoService> CreateMockService()
    {
        return new Mock<IDocumentoService>();
    }

    [Fact]
    public async Task GetAll_ReturnsOkWithDocuments()
    {
        var service = CreateMockService();
        var tenantContext = CreateTenantContext(Guid.NewGuid(), Guid.NewGuid());
        var controller = new DocumentosController(service.Object, tenantContext);

        var documentos = new List<DocumentoDto>
        {
            new() { Id = Guid.NewGuid(), Nome = "Doc1.pdf" },
            new() { Id = Guid.NewGuid(), Nome = "Doc2.pdf" }
        };
        service.Setup(s => s.GetAllAsync(It.IsAny<Guid?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(documentos);

        var result = await controller.GetAll();

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var returned = Assert.IsAssignableFrom<IEnumerable<DocumentoDto>>(okResult.Value);
        Assert.Equal(2, returned.Count());
    }

    [Fact]
    public async Task GetById_ExistingDocument_ReturnsOk()
    {
        var service = CreateMockService();
        var tenantContext = CreateTenantContext(Guid.NewGuid(), Guid.NewGuid());
        var controller = new DocumentosController(service.Object, tenantContext);

        var docId = Guid.NewGuid();
        var documento = new DocumentoDto { Id = docId, Nome = "Test.pdf" };
        service.Setup(s => s.GetByIdAsync(docId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(documento);

        var result = await controller.GetById(docId);

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal(docId, ((DocumentoDto)okResult.Value!).Id);
    }

    [Fact]
    public async Task GetById_NonExistingDocument_ReturnsNotFound()
    {
        var service = CreateMockService();
        var tenantContext = CreateTenantContext(Guid.NewGuid(), Guid.NewGuid());
        var controller = new DocumentosController(service.Object, tenantContext);

        service.Setup(s => s.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((DocumentoDto?)null);

        var result = await controller.GetById(Guid.NewGuid());

        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task Delete_ExistingDocument_ReturnsNoContent()
    {
        var service = CreateMockService();
        var tenantContext = CreateTenantContext(Guid.NewGuid(), Guid.NewGuid());
        var controller = new DocumentosController(service.Object, tenantContext);

        var docId = Guid.NewGuid();
        service.Setup(s => s.DeleteAsync(docId, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var result = await controller.Delete(docId);

        Assert.IsType<NoContentResult>(result);
    }

    [Fact]
    public async Task GetDownloadUrl_ExistingDocument_ReturnsOkWithUrl()
    {
        var service = CreateMockService();
        var tenantContext = CreateTenantContext(Guid.NewGuid(), Guid.NewGuid());
        var controller = new DocumentosController(service.Object, tenantContext);

        var docId = Guid.NewGuid();
        var url = "https://storage.example.com/signed-url";
        service.Setup(s => s.GetDownloadUrlAsync(docId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(url);

        var result = await controller.GetDownloadUrl(docId);

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var anonType = okResult.Value.GetType();
        Assert.Equal("url", anonType.GetProperty("url")?.Name);
    }

    [Fact]
    public async Task GetDownloadUrl_DocumentoInexistenteOuDeOutroTenant_ReturnsNotFound()
    {
        var service = CreateMockService();
        var controller = new DocumentosController(service.Object, CreateTenantContext(Guid.NewGuid(), Guid.NewGuid()));
        service.Setup(s => s.GetDownloadUrlAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new KeyNotFoundException());

        var result = await controller.GetDownloadUrl(Guid.NewGuid());

        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task GetFile_DocumentoInexistenteOuDeOutroTenant_ReturnsNotFound()
    {
        var service = CreateMockService();
        var controller = new DocumentosController(service.Object, CreateTenantContext(Guid.NewGuid(), Guid.NewGuid()));
        service.Setup(s => s.GetFileStreamAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new KeyNotFoundException());

        var result = await controller.GetFile(Guid.NewGuid());

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task GetFile_DocumentoDoTenant_RetornaArquivo()
    {
        var service = CreateMockService();
        var controller = new DocumentosController(service.Object, CreateTenantContext(Guid.NewGuid(), Guid.NewGuid()));
        service.Setup(s => s.GetFileStreamAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(((Stream)new MemoryStream([1]), "application/pdf", "a.pdf"));

        var result = await controller.GetFile(Guid.NewGuid());

        var file = Assert.IsType<FileStreamResult>(result);
        Assert.Equal("application/pdf", file.ContentType);
        Assert.Equal("a.pdf", file.FileDownloadName);
    }

    [Fact]
    public async Task Delete_DocumentoInexistenteOuDeOutroTenant_ReturnsNotFound()
    {
        var service = CreateMockService();
        var controller = new DocumentosController(service.Object, CreateTenantContext(Guid.NewGuid(), Guid.NewGuid()));
        service.Setup(s => s.DeleteAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new KeyNotFoundException());

        var result = await controller.Delete(Guid.NewGuid());

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task GetCota_ReturnsOkWithQuota()
    {
        var service = CreateMockService();
        var tenantContext = CreateTenantContext(Guid.NewGuid(), Guid.NewGuid());
        var controller = new DocumentosController(service.Object, tenantContext);

        var quota = new CotaArmazenamentoDto
        {
            UsadoBytes = 5L * 1024 * 1024 * 1024,
            CotaBytes = 20L * 1024 * 1024 * 1024
        };
        service.Setup(s => s.GetCotaAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(quota);

        var result = await controller.GetCota();

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var returned = (CotaArmazenamentoDto)okResult.Value!;
        Assert.Equal(5L * 1024 * 1024 * 1024, returned.UsadoBytes);
    }

    [Fact]
    public async Task GetByProcesso_ReturnsOkWithDocuments()
    {
        var service = CreateMockService();
        var tenantContext = CreateTenantContext(Guid.NewGuid(), Guid.NewGuid());
        var controller = new DocumentosController(service.Object, tenantContext);

        var processoId = Guid.NewGuid();
        var documentos = new List<DocumentoDto>
        {
            new() { Id = Guid.NewGuid(), ProcessoId = processoId, Nome = "Proc Doc.pdf" }
        };
        service.Setup(s => s.GetByProcessoAsync(processoId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(documentos);

        var result = await controller.GetByProcesso(processoId);

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var returned = Assert.IsAssignableFrom<IEnumerable<DocumentoDto>>(okResult.Value);
        Assert.Single(returned);
    }

    [Fact]
    public async Task Upload_NullFile_ReturnsBadRequest()
    {
        var service = CreateMockService();
        var tenantContext = CreateTenantContext(Guid.NewGuid(), Guid.NewGuid());
        var controller = new DocumentosController(service.Object, tenantContext);

        var result = await controller.Upload(null!, null, null, null, null, TipoDocumento.Prova, null);

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task Upload_EmptyFile_ReturnsBadRequest()
    {
        var service = CreateMockService();
        var tenantContext = CreateTenantContext(Guid.NewGuid(), Guid.NewGuid());
        var controller = new DocumentosController(service.Object, tenantContext);

        var mockFile = new Mock<IFormFile>();
        mockFile.Setup(f => f.Length).Returns(0);

        var result = await controller.Upload(mockFile.Object, null, null, null, null, TipoDocumento.Prova, null);

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task Atualizar_DevolveODocumentoAtualizado()
    {
        var service = CreateMockService();
        var controller = new DocumentosController(service.Object, CreateTenantContext(Guid.NewGuid(), Guid.NewGuid()));
        var id = Guid.NewGuid();
        var dto = new AtualizarDocumentoDto("Novo nome", "Obs", TipoDocumento.Prova);
        service.Setup(s => s.AtualizarAsync(id, dto, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DocumentoDto { Id = id, Nome = "Novo nome.pdf", Observacoes = "Obs" });

        var ok = Assert.IsType<OkObjectResult>((await controller.Atualizar(id, dto)).Result);
        Assert.Equal("Novo nome.pdf", Assert.IsType<DocumentoDto>(ok.Value).Nome);
    }

    [Fact]
    public async Task Atualizar_NaoEncontrado_Devolve404()
    {
        var service = CreateMockService();
        var controller = new DocumentosController(service.Object, CreateTenantContext(Guid.NewGuid(), Guid.NewGuid()));
        service.Setup(s => s.AtualizarAsync(It.IsAny<Guid>(), It.IsAny<AtualizarDocumentoDto>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new KeyNotFoundException());

        var r = await controller.Atualizar(Guid.NewGuid(), new AtualizarDocumentoDto("x", null, TipoDocumento.Outro));
        Assert.IsType<NotFoundResult>(r.Result);
    }

    [Fact]
    public async Task Atualizar_NomeInvalido_Devolve400()
    {
        var service = CreateMockService();
        var controller = new DocumentosController(service.Object, CreateTenantContext(Guid.NewGuid(), Guid.NewGuid()));
        service.Setup(s => s.AtualizarAsync(It.IsAny<Guid>(), It.IsAny<AtualizarDocumentoDto>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ArgumentException("Informe o nome do documento."));

        var r = await controller.Atualizar(Guid.NewGuid(), new AtualizarDocumentoDto(" ", null, TipoDocumento.Outro));
        Assert.IsType<BadRequestObjectResult>(r.Result);
    }

    [Fact]
    public async Task PortalCliente_Documentos_NaoExpoemObservacoes()
    {
        var contatoId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var processoId = Guid.NewGuid();
        var portal = new Mock<IPortalClienteService>();
        portal.Setup(p => p.GetProcessoAsync(processoId, contatoId, tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LegalManager.Application.DTOs.PortalCliente.MeuProcessoDto(
                processoId, "0000000-00.2026.8.26.0000", null, null, default, default, default, default, 0, DateTime.UtcNow));
        var docs = CreateMockService();
        docs.Setup(s => s.GetByProcessoAsync(processoId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new DocumentoDto { Id = Guid.NewGuid(), Nome = "a.pdf", Observacoes = "nota interna" }]);
        var controller = new PortalClienteController(portal.Object, docs.Object, CreateTenantContext(tenantId, Guid.NewGuid()))
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(
                    [
                        new System.Security.Claims.Claim("contatoId", contatoId.ToString()),
                        new System.Security.Claims.Claim("tenantId", tenantId.ToString())
                    ], "test"))
                }
            }
        };

        var ok = Assert.IsType<OkObjectResult>((await controller.GetDocumentos(processoId, default)).Result);
        var doc = Assert.Single((IEnumerable<DocumentoDto>)ok.Value!);
        Assert.Null(doc.Observacoes);
    }
}