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
    public async Task<ActionResult<IEnumerable<DocumentoDto>>> GetAll([FromQuery] Guid? pastaId = null, [FromQuery] bool raiz = false, CancellationToken ct = default)
    {
        var result = await _service.GetAllAsync(pastaId, raiz, ct);
        return Ok(result);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<DocumentoDto>> GetById(Guid id, CancellationToken ct = default)
    {
        var result = await _service.GetByIdAsync(id, ct);
        return result == null ? NotFound() : Ok(result);
    }

    [HttpPatch("{id:guid}/pasta")]
    public async Task<IActionResult> UpdatePasta(Guid id, [FromBody] UpdateDocumentoPastaDto dto, CancellationToken ct = default)
    {
        try
        {
            await _service.UpdatePastaAsync(id, dto.PastaId, ct);
            return NoContent();
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
    }

    [HttpGet("processo/{processoId:guid}")]
    public async Task<ActionResult<IEnumerable<DocumentoDto>>> GetByProcesso(Guid processoId, CancellationToken ct = default)
    {
        var result = await _service.GetByProcessoAsync(processoId, ct);
        return Ok(result);
    }

    [HttpGet("cliente/{clienteId:guid}")]
    public async Task<ActionResult<IEnumerable<DocumentoDto>>> GetByCliente(Guid clienteId, [FromQuery] Guid? pastaId = null, CancellationToken ct = default)
    {
        var result = await _service.GetByClienteAsync(clienteId, pastaId, ct);
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
        [FromForm] Guid? pastaId = null,
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
            Nome = nome,
            PastaId = pastaId
        };

        var result = await _service.UploadAsync(stream, file.FileName, file.ContentType, uploadInfo, null, ct);
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

    [HttpPost("em-massa/excluir")]
    public async Task<IActionResult> ExcluirEmMassa([FromBody] DocumentosEmMassaDto dto, CancellationToken ct = default)
        => Ok(new { alterados = await _service.ExcluirEmMassaAsync(dto.Ids, ct) });

    [HttpPost("em-massa/pasta")]
    public async Task<IActionResult> MoverEmMassa([FromBody] MoverDocumentosEmMassaDto dto, CancellationToken ct = default)
    {
        try
        {
            return Ok(new { alterados = await _service.MoverEmMassaAsync(dto.Ids, dto.PastaId, ct) });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
    }

    [HttpPost("em-massa/tipo")]
    public async Task<IActionResult> AlterarTipoEmMassa([FromBody] AlterarTipoDocumentosEmMassaDto dto, CancellationToken ct = default)
        => Ok(new { alterados = await _service.AlterarTipoEmMassaAsync(dto.Ids, dto.Tipo, ct) });

    [HttpPost("em-massa/zip")]
    public async Task<IActionResult> BaixarZip([FromBody] DocumentosEmMassaDto dto, CancellationToken ct = default)
    {
        if (dto.Ids.Count > DocumentosEmMassaDto.MaxDocumentosZip)
            return BadRequest(new { message = $"Selecione no máximo {DocumentosEmMassaDto.MaxDocumentosZip} documentos para baixar de uma vez." });

        // Arquivo temporário (apagado ao fechar): não segura o .zip inteiro na memória.
        var temp = new FileStream(Path.GetTempFileName(), FileMode.Create, FileAccess.ReadWrite, FileShare.None,
            81920, FileOptions.DeleteOnClose | FileOptions.Asynchronous);
        try
        {
            var total = await _service.GerarZipAsync(dto.Ids, temp, ct);
            if (total == 0) { await temp.DisposeAsync(); return NotFound(new { message = "Nenhum documento encontrado." }); }
            temp.Position = 0;
            return File(temp, "application/zip", $"documentos-{DateTime.UtcNow:yyyyMMdd-HHmm}.zip");
        }
        catch (InvalidOperationException ex)
        {
            await temp.DisposeAsync();
            return BadRequest(new { message = ex.Message });
        }
        catch
        {
            await temp.DisposeAsync();
            throw;
        }
    }

    [HttpGet("cota")]
    public async Task<ActionResult<CotaArmazenamentoDto>> GetCota(CancellationToken ct = default)
    {
        var result = await _service.GetCotaAsync(ct);
        return Ok(result);
    }
}