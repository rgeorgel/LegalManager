using LegalManager.API.Controllers;
using LegalManager.Application.DTOs.Processos;
using LegalManager.Application.Interfaces;
using LegalManager.Domain.Enums;
using LegalManager.Domain.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace LegalManager.UnitTests;

public class ProcessosInssControllerTests
{
    private static (ProcessosInssController Controller, Mock<IRequerimentoInssService> Service, Mock<IAuditService> Audit) Criar()
    {
        var service = new Mock<IRequerimentoInssService>();
        var audit = new Mock<IAuditService>();
        var tenant = new Mock<ITenantContext>();
        tenant.Setup(t => t.TenantId).Returns(Guid.NewGuid());
        tenant.Setup(t => t.UserId).Returns(Guid.NewGuid());
        var controller = new ProcessosInssController(service.Object, audit.Object, tenant.Object)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
        return (controller, service, audit);
    }

    private static RequerimentoInssResponseDto Dados(Guid id) => new(id, 41, "B41 — Aposentadoria por idade", null, null,
        SituacaoRequerimentoInss.EmAnalise, null, null, null, null, null, null, null, null, null, []);

    [Fact]
    public void Especies_ListaOCatalogo()
    {
        var (c, _, _) = Criar();

        var especies = c.Especies().ToList();

        Assert.Contains(especies, e => e.Codigo == 41 && e.Nome == "Aposentadoria por idade");
    }

    [Fact]
    public async Task Obter_SemDados204_ProcessoInexistente404_ComDados200()
    {
        var (c, service, _) = Criar();
        var semDados = Guid.NewGuid();
        var inexistente = Guid.NewGuid();
        var comDados = Guid.NewGuid();
        service.Setup(s => s.ObterAsync(semDados, It.IsAny<CancellationToken>())).ReturnsAsync((RequerimentoInssResponseDto?)null);
        service.Setup(s => s.ObterAsync(inexistente, It.IsAny<CancellationToken>())).ThrowsAsync(new KeyNotFoundException());
        service.Setup(s => s.ObterAsync(comDados, It.IsAny<CancellationToken>())).ReturnsAsync(Dados(comDados));

        Assert.IsType<NoContentResult>((await c.Obter(semDados, default)).Result);
        Assert.IsType<NotFoundResult>((await c.Obter(inexistente, default)).Result);
        Assert.IsType<OkObjectResult>((await c.Obter(comDados, default)).Result);
    }

    [Fact]
    public async Task Salvar_OkAudita_InvalidoDa400()
    {
        var (c, service, audit) = Criar();
        var id = Guid.NewGuid();
        service.Setup(s => s.SalvarAsync(id, It.IsAny<RequerimentoInssDto>(), It.IsAny<CancellationToken>())).ReturnsAsync(Dados(id));

        Assert.IsType<OkObjectResult>((await c.Salvar(id, new RequerimentoInssDto(), default)).Result);
        audit.Verify(a => a.LogAsync(It.IsAny<AuditLogEntry>(), It.IsAny<CancellationToken>()), Times.Once);

        service.Setup(s => s.SalvarAsync(id, It.IsAny<RequerimentoInssDto>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Os dados do INSS são de processo administrativo."));
        Assert.IsType<BadRequestObjectResult>((await c.Salvar(id, new RequerimentoInssDto(), default)).Result);

        service.Setup(s => s.SalvarAsync(id, It.IsAny<RequerimentoInssDto>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new KeyNotFoundException());
        Assert.IsType<NotFoundResult>((await c.Salvar(id, new RequerimentoInssDto(), default)).Result);
    }

    [Fact]
    public async Task CriarSugestao_OkAudita_JaCriada404()
    {
        var (c, service, audit) = Criar();
        var id = Guid.NewGuid();
        service.Setup(s => s.CriarSugestaoAsync(id, "pericia", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SugestaoInssCriadaDto("evento", Guid.NewGuid()));
        service.Setup(s => s.CriarSugestaoAsync(id, "exigencia", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new KeyNotFoundException("Sugestão não encontrada ou já criada."));

        Assert.IsType<OkObjectResult>((await c.CriarSugestao(id, "pericia", default)).Result);
        audit.Verify(a => a.LogAsync(It.Is<AuditLogEntry>(e => e.Entidade == "Evento"), It.IsAny<CancellationToken>()), Times.Once);
        Assert.IsType<NotFoundObjectResult>((await c.CriarSugestao(id, "exigencia", default)).Result);
    }
}
