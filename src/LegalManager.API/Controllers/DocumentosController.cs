using LegalManager.Application.DTOs.Documentos;
using LegalManager.Application.Interfaces;
using LegalManager.Domain.Enums;
using LegalManager.Domain.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LegalManager.API.Controllers;

[ApiController]
[Route("api/documentos")]
[Authorize]
public class DocumentosController : ControllerBase
{
    private readonly IDocumentoService _service;
    private readonly ITenantContext _tenantContext;

    public DocumentosController(IDocumentoService service, ITenantContext tenantContext)
    {
        _service = service;
        _tenantContext = tenantContext;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<DocumentoDto>>> GetAll(CancellationToken ct = default)
    {
        var result = await _service.GetAllAsync(ct);
        return Ok(result);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<DocumentoDto>> GetById(Guid id, CancellationToken ct = default)
    {
        var result = await _service.GetByIdAsync(id, ct);
        return result == null ? NotFound() : Ok(result);
    }

    [HttpGet("processo/{processoId:guid}")]
    public async Task<ActionResult<IEnumerable<DocumentoDto>>> GetByProcesso(Guid processoId, CancellationToken ct = default)
    {
        var result = await _service.GetByProcessoAsync(processoId, ct);
        return Ok(result);
    }

    [HttpGet("cliente/{clienteId:guid}")]
    public async Task<ActionResult<IEnumerable<DocumentoDto>>> GetByCliente(Guid clienteId, CancellationToken ct = default)
    {
        var result = await _service.GetByClienteAsync(clienteId, ct);
        return Ok(result);
    }

    [HttpPost("upload")]
    [RequestSizeLimit(100_000_000)]
    [RequestFormLimits(MultipartBodyLengthLimit = 100_000_000)]
    public async Task<ActionResult<DocumentoDto>> Upload(
        IFormFile file,
        [FromForm] Guid? processoId,
        [FromForm] Guid? clienteId,
        [FromForm] Guid? contratoId,
        [FromForm] Guid? modeloId,
        [FromForm] TipoDocumento tipo = TipoDocumento.Outro,
        [FromForm] string? nome = null,
        CancellationToken ct = default)
    {
        if (file == null || file.Length == 0)
            return BadRequest("Arquivo não fornecido.");

        using var stream = file.OpenReadStream();
        var uploadInfo = new DocumentoUploadDto
        {
            ProcessoId = processoId,
            ClienteId = clienteId,
            ContratoId = contratoId,
            ModeloId = modeloId,
            Tipo = tipo,
            Nome = nome
        };

        var result = await _service.UploadAsync(stream, file.FileName, file.ContentType, uploadInfo, ct);
        return CreatedAtAction(nameof(GetById), new { id = result.Id }, result);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct = default)
    {
        await _service.DeleteAsync(id, ct);
        return NoContent();
    }

    [HttpGet("{id:guid}/download")]
    public async Task<ActionResult<string>> GetDownloadUrl(Guid id, CancellationToken ct = default)
    {
        var url = await _service.GetDownloadUrlAsync(id, ct);
        return Ok(new { url });
    }

    [HttpGet("{id:guid}/file")]
    public async Task<IActionResult> GetFile(Guid id, CancellationToken ct = default)
    {
        var (stream, contentType, fileName) = await _service.GetFileStreamAsync(id, ct);
        return File(stream, contentType, fileName);
    }

    [HttpGet("cota")]
    public async Task<ActionResult<CotaArmazenamentoDto>> GetCota(CancellationToken ct = default)
    {
        var result = await _service.GetCotaAsync(ct);
        return Ok(result);
    }
}