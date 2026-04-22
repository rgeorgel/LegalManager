using LegalManager.Application.DTOs.Publicacoes;
using LegalManager.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LegalManager.API.Controllers;

[Authorize]
[ApiController]
[Route("api/nomes-captura")]
public class NomesCapturaController : ControllerBase
{
    private readonly INomeCapturaService _service;

    public NomesCapturaController(INomeCapturaService service) => _service = service;

    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken ct)
        => Ok(await _service.GetAllAsync(ct));

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateNomeCapturaDto dto, CancellationToken ct)
    {
        try
        {
            var result = await _service.CreateAsync(dto, ct);
            return CreatedAtAction(nameof(GetAll), result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPatch("{id:guid}/toggle")]
    public async Task<IActionResult> Toggle(Guid id, CancellationToken ct)
    {
        try
        {
            await _service.ToggleAtivoAsync(id, ct);
            return NoContent();
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        try
        {
            await _service.DeleteAsync(id, ct);
            return NoContent();
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
    }
}
