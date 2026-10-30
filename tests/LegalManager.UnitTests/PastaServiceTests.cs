using LegalManager.API.Controllers;
using LegalManager.Application.DTOs.Documentos;
using LegalManager.Domain.Entities;
using LegalManager.Domain.Enums;
using LegalManager.Domain.Interfaces;
using LegalManager.Infrastructure.Persistence;
using LegalManager.Infrastructure.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace LegalManager.UnitTests;

public class PastaServiceTests
{
    private readonly AppDbContext _db = new(new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly PastaService _service;
    private readonly PastasController _controller;

    public PastaServiceTests()
    {
        var tenant = new Mock<ITenantContext>();
        tenant.Setup(t => t.TenantId).Returns(_tenantId);
        _service = new PastaService(_db, tenant.Object);
        _controller = new PastasController(_service);
    }

    private Documento Doc(Guid? pastaId, string nome = "doc.pdf")
    {
        var d = new Documento
        {
            Id = Guid.NewGuid(), TenantId = _tenantId, PastaId = pastaId, Nome = nome, ObjectKey = "k/" + nome,
            ContentType = "application/pdf", TamanhoBytes = 10, CriadoEm = DateTime.UtcNow
        };
        _db.Documentos.Add(d);
        _db.SaveChanges();
        return d;
    }

    [Fact]
    public async Task Create_RaizESubpasta_MontaArvore()
    {
        var raiz = await _service.CreateAsync(new CreatePastaDto("Contratos", null), EntidadeTipo.Documento);
        var sub = await _service.CreateAsync(new CreatePastaDto("2026", raiz.Id), EntidadeTipo.Documento);
        Doc(sub.Id);

        var dto = await _service.GetByIdAsync(raiz.Id, EntidadeTipo.Documento);
        Assert.NotNull(dto);
        var filho = Assert.Single(dto!.SubPastas);
        Assert.Equal("2026", filho.Nome);
        Assert.Equal(1, filho.DocumentoCount);

        var arvore = (await _service.GetTreeAsync(EntidadeTipo.Documento)).ToList();
        Assert.Equal(2, arvore.Count);
        Assert.Empty(await _service.GetTreeAsync(EntidadeTipo.Contato));
    }

    [Fact]
    public async Task Create_NomeDuplicadoNoMesmoLocal_Falha()
    {
        await _service.CreateAsync(new CreatePastaDto("A", null), EntidadeTipo.Documento);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.CreateAsync(new CreatePastaDto("A", null), EntidadeTipo.Documento));

        // Mesmo nome em outra entidade é permitido
        await _service.CreateAsync(new CreatePastaDto("A", null), EntidadeTipo.Contato);
    }

    [Fact]
    public async Task Create_AlemDaProfundidadeMaxima_Falha()
    {
        Guid? parent = null;
        for (var i = 0; i < 5; i++)
            parent = (await _service.CreateAsync(new CreatePastaDto($"N{i}", parent), EntidadeTipo.Documento)).Id;

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.CreateAsync(new CreatePastaDto("Fundo", parent), EntidadeTipo.Documento));
        Assert.Contains("Profundidade", ex.Message);
    }

    [Fact]
    public async Task Create_SemTenant_NaoAutorizado()
    {
        var semTenant = new PastaService(_db, Mock.Of<ITenantContext>());
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            semTenant.CreateAsync(new CreatePastaDto("X", null), EntidadeTipo.Documento));
    }

    [Fact]
    public async Task Update_RenomeiaReordenaEMove()
    {
        var a = await _service.CreateAsync(new CreatePastaDto("A", null), EntidadeTipo.Documento);
        var b = await _service.CreateAsync(new CreatePastaDto("B", null), EntidadeTipo.Documento);

        var r = await _service.UpdateAsync(b.Id, new UpdatePastaDto("B2", a.Id, 3), EntidadeTipo.Documento);

        Assert.Equal("B2", r.Nome);
        Assert.Equal(a.Id, r.ParentPastaId);
        Assert.Equal(3, r.Ordem);

        await _service.MoveAsync(b.Id, null, EntidadeTipo.Documento);
        Assert.Null((await _service.GetByIdAsync(b.Id, EntidadeTipo.Documento))!.ParentPastaId);
    }

    [Fact]
    public async Task Update_Conflitos()
    {
        var a = await _service.CreateAsync(new CreatePastaDto("A", null), EntidadeTipo.Documento);
        var b = await _service.CreateAsync(new CreatePastaDto("B", null), EntidadeTipo.Documento);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.UpdateAsync(b.Id, new UpdatePastaDto("A", null, null), EntidadeTipo.Documento));
        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            _service.UpdateAsync(b.Id, new UpdatePastaDto(null, Guid.NewGuid(), null), EntidadeTipo.Documento));
        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            _service.UpdateAsync(Guid.NewGuid(), new UpdatePastaDto("X", null, null), EntidadeTipo.Documento));

        Guid? parent = a.Id;
        for (var i = 0; i < 4; i++)
            parent = (await _service.CreateAsync(new CreatePastaDto($"N{i}", parent), EntidadeTipo.Documento)).Id;
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.MoveAsync(b.Id, parent, EntidadeTipo.Documento));
    }

    [Fact]
    public async Task Delete_SemExcluirDocs_MoveDocumentosParaOPai()
    {
        var raiz = await _service.CreateAsync(new CreatePastaDto("Raiz", null), EntidadeTipo.Documento);
        var filha = await _service.CreateAsync(new CreatePastaDto("Filha", raiz.Id), EntidadeTipo.Documento);
        var neta = await _service.CreateAsync(new CreatePastaDto("Neta", filha.Id), EntidadeTipo.Documento);
        var docFilha = Doc(filha.Id, "f.pdf");
        Doc(neta.Id, "n.pdf");

        var r = await _service.DeleteAsync(filha.Id, excluirDocs: false);

        Assert.Equal(1, r.SubpastasExcluidas);
        Assert.Equal(2, r.DocumentosMovidos);
        Assert.Equal(raiz.Id, (await _db.Documentos.FindAsync(docFilha.Id))!.PastaId);
        Assert.Equal(2, await _db.Documentos.CountAsync());
        Assert.Equal(1, await _db.Pastas.CountAsync());
    }

    [Fact]
    public async Task Delete_ExcluindoDocs_RemoveTudo()
    {
        var raiz = await _service.CreateAsync(new CreatePastaDto("Raiz", null), EntidadeTipo.Documento);
        Doc(raiz.Id);

        var r = await _service.DeleteAsync(raiz.Id, excluirDocs: true);

        Assert.Equal(0, r.DocumentosMovidos);
        Assert.Empty(_db.Documentos);
        await Assert.ThrowsAsync<KeyNotFoundException>(() => _service.DeleteAsync(raiz.Id, true));
    }

    [Fact]
    public async Task GetDocumentos_SoDaPastaEDoTenant()
    {
        var p = await _service.CreateAsync(new CreatePastaDto("P", null), EntidadeTipo.Documento);
        Doc(p.Id, "meu.pdf");
        _db.Documentos.Add(new Documento { Id = Guid.NewGuid(), TenantId = Guid.NewGuid(), PastaId = p.Id, Nome = "alheio.pdf" });
        _db.SaveChanges();

        var docs = (await _service.GetDocumentosAsync(p.Id)).ToList();
        Assert.Equal("meu.pdf", Assert.Single(docs).Nome);
        Assert.Equal("P", docs[0].PastaNome);
    }

    // ── Controller ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Controller_FluxoCompleto()
    {
        var criado = Assert.IsType<CreatedAtActionResult>((await _controller.Create(new CreatePastaDto("A", null), EntidadeTipo.Documento)).Result);
        var id = ((PastaDto)criado.Value!).Id;

        Assert.IsType<ConflictObjectResult>((await _controller.Create(new CreatePastaDto("A", null), EntidadeTipo.Documento)).Result);
        Assert.IsType<OkObjectResult>((await _controller.GetTree(EntidadeTipo.Documento)).Result);
        Assert.IsType<OkObjectResult>((await _controller.GetById(id, EntidadeTipo.Documento)).Result);
        Assert.IsType<NotFoundResult>((await _controller.GetById(Guid.NewGuid(), EntidadeTipo.Documento)).Result);
        Assert.IsType<OkObjectResult>((await _controller.GetDocumentos(id)).Result);

        Assert.IsType<OkObjectResult>((await _controller.Update(id, new UpdatePastaDto("A2", null, 1), EntidadeTipo.Documento)).Result);
        Assert.IsType<NotFoundResult>((await _controller.Update(Guid.NewGuid(), new UpdatePastaDto("Z", null, null), EntidadeTipo.Documento)).Result);
        var b = ((PastaDto)((CreatedAtActionResult)(await _controller.Create(new CreatePastaDto("B", null), EntidadeTipo.Documento)).Result!).Value!).Id;
        Assert.IsType<ConflictObjectResult>((await _controller.Update(b, new UpdatePastaDto("A2", null, null), EntidadeTipo.Documento)).Result);

        Assert.IsType<NoContentResult>(await _controller.Move(b, new MovePastaDto(id), EntidadeTipo.Documento));
        Assert.IsType<NotFoundResult>(await _controller.Move(Guid.NewGuid(), new MovePastaDto(null), EntidadeTipo.Documento));

        Assert.IsType<OkObjectResult>(await _controller.Delete(id));
        Assert.IsType<NotFoundResult>(await _controller.Delete(id));
    }

    [Fact]
    public async Task Controller_Move_ProfundidadeExcedida_Conflict()
    {
        Guid? parent = null;
        for (var i = 0; i < 5; i++)
            parent = (await _service.CreateAsync(new CreatePastaDto($"N{i}", parent), EntidadeTipo.Documento)).Id;
        var solta = await _service.CreateAsync(new CreatePastaDto("Solta", null), EntidadeTipo.Documento);

        Assert.IsType<ConflictObjectResult>(await _controller.Move(solta.Id, new MovePastaDto(parent), EntidadeTipo.Documento));
    }
}
