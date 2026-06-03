using LegalManager.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LegalManager.API.Controllers;

[ApiController]
[Route("api/tenant-oabs")]
[Authorize]
public class TenantOabsController : ControllerBase
{
    private readonly ITenantOabService _service;

    public TenantOabsController(ITenantOabService service)
    {
        _service = service;
    }

    [HttpGet]
    public async Task<IActionResult> Listar(CancellationToken ct)
    {
        try
        {
            var items = await _service.ListarAsync(ct);
            return Ok(items);
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(402, new { error = "captura_disabled_for_plan", message = ex.Message });
        }
    }

    [HttpPost]
    public async Task<IActionResult> Criar([FromBody] CriarTenantOabRequest req, CancellationToken ct)
    {
        if (req == null) return BadRequest(new { error = "invalid_body" });
        try
        {
            var dto = await _service.CriarAsync(req, ct);
            return CreatedAtAction(nameof(Listar), new { id = dto.Id }, dto);
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(402, new { error = "captura_disabled_for_plan", message = ex.Message });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = "validation_error", message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { error = "duplicate", message = ex.Message });
        }
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Atualizar(Guid id, [FromBody] AtualizarTenantOabRequest req, CancellationToken ct)
    {
        if (req == null) return BadRequest(new { error = "invalid_body" });
        try
        {
            var dto = await _service.AtualizarAsync(id, req, ct);
            return Ok(dto);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = "not_found", message = ex.Message });
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(402, new { error = "captura_disabled_for_plan", message = ex.Message });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = "validation_error", message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { error = "duplicate", message = ex.Message });
        }
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Remover(Guid id, CancellationToken ct)
    {
        try
        {
            await _service.RemoverAsync(id, ct);
            return NoContent();
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = "not_found", message = ex.Message });
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(402, new { error = "captura_disabled_for_plan", message = ex.Message });
        }
    }

    [HttpPost("{id:guid}/sincronizar")]
    public async Task<IActionResult> Sincronizar(Guid id, CancellationToken ct)
    {
        try
        {
            var dto = await _service.SincronizarAsync(id, ct);
            return Ok(dto);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = "not_found", message = ex.Message });
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(402, new { error = "captura_disabled_for_plan", message = ex.Message });
        }
    }

    [HttpPost("sincronizar-todas")]
    public async Task<IActionResult> SincronizarTodas(CancellationToken ct)
    {
        try
        {
            var count = await _service.SincronizarTodasAsync(ct);
            return Ok(new { sincronizadas = count });
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(402, new { error = "captura_disabled_for_plan", message = ex.Message });
        }
    }
}
