using LegalManager.Application.DTOs.Documentos;
using LegalManager.Application.Interfaces;
using LegalManager.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LegalManager.API.Controllers;

[ApiController]
[Route("api/pastas")]
[Authorize]
public class PastasController : ControllerBase
{
    private readonly IPastaService _service;

    public PastasController(IPastaService service)
    {
        _service = service;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<PastaTreeDto>>> GetTree(
        [FromQuery] EntidadeTipo entidadeTipo,
        CancellationToken ct = default)
    {
        var result = await _service.GetTreeAsync(entidadeTipo, ct);
        return Ok(result);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<PastaDto>> GetById(
        Guid id,
        [FromQuery] EntidadeTipo entidadeTipo,
        CancellationToken ct = default)
    {
        var result = await _service.GetByIdAsync(id, entidadeTipo, ct);
        return result == null ? NotFound() : Ok(result);
    }

    [HttpGet("{id:guid}/documentos")]
    public async Task<ActionResult<IEnumerable<DocumentoDto>>> GetDocumentos(
        Guid id,
        CancellationToken ct = default)
    {
        var result = await _service.GetDocumentosAsync(id, ct);
        return Ok(result);
    }

    [HttpPost]
    public async Task<ActionResult<PastaDto>> Create(
        [FromBody] CreatePastaDto dto,
        [FromQuery] EntidadeTipo entidadeTipo,
        CancellationToken ct = default)
    {
        try
        {
            var result = await _service.CreateAsync(dto, entidadeTipo, ct);
            return CreatedAtAction(nameof(GetById), new { id = result.Id, entidadeTipo }, result);
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { message = ex.Message });
        }
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<PastaDto>> Update(
        Guid id,
        [FromBody] UpdatePastaDto dto,
        [FromQuery] EntidadeTipo entidadeTipo,
        CancellationToken ct = default)
    {
        try
        {
            var result = await _service.UpdateAsync(id, dto, entidadeTipo, ct);
            return Ok(result);
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { message = ex.Message });
        }
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(
        Guid id,
        [FromQuery] bool excluirDocs = false,
        CancellationToken ct = default)
    {
        try
        {
            var result = await _service.DeleteAsync(id, excluirDocs, ct);
            return Ok(result);
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
    }

    [HttpPost("{id:guid}/mover")]
    public async Task<IActionResult> Move(
        Guid id,
        [FromBody] MovePastaDto dto,
        [FromQuery] EntidadeTipo entidadeTipo,
        CancellationToken ct = default)
    {
        try
        {
            await _service.MoveAsync(id, dto.NewParentPastaId, entidadeTipo, ct);
            return NoContent();
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { message = ex.Message });
        }
    }
}

public record MovePastaDto(Guid? NewParentPastaId);