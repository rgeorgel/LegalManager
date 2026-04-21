using LegalManager.Application.DTOs.Timesheet;
using LegalManager.Application.Interfaces;
using LegalManager.Domain.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LegalManager.API.Controllers;

[ApiController]
[Route("api/timesheet")]
[Authorize]
public class TimesheetController(ITimesheetService service, ITenantContext tenantContext) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<RegistroTempoPagedDto>> GetAll(
        [FromQuery] Guid? usuarioId,
        [FromQuery] Guid? processoId,
        [FromQuery] DateTime? de,
        [FromQuery] DateTime? ate,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        var result = await service.GetAllAsync(tenantContext.TenantId, usuarioId, processoId, de, ate, page, pageSize, ct);
        return Ok(result);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<RegistroTempoDto>> GetById(Guid id, CancellationToken ct)
    {
        var item = await service.GetByIdAsync(id, tenantContext.TenantId, ct);
        return item == null ? NotFound() : Ok(item);
    }

    [HttpGet("ativo")]
    public async Task<ActionResult<RegistroTempoDto?>> GetAtivo(CancellationToken ct)
    {
        var item = await service.GetCronometroAtivoAsync(tenantContext.TenantId, tenantContext.UserId, ct);
        return Ok(item);
    }

    [HttpPost("iniciar")]
    public async Task<ActionResult<RegistroTempoDto>> Iniciar([FromBody] IniciarRegistroDto dto, CancellationToken ct)
    {
        try
        {
            var result = await service.IniciarCronometroAsync(tenantContext.TenantId, tenantContext.UserId, dto, ct);
            return CreatedAtAction(nameof(GetById), new { id = result.Id }, result);
        }
        catch (InvalidOperationException ex) { return Conflict(new { message = ex.Message }); }
    }

    [HttpPost("parar")]
    public async Task<ActionResult<RegistroTempoDto>> Parar([FromBody] PararRegistroDto dto, CancellationToken ct)
    {
        try
        {
            var result = await service.PararCronometroAsync(tenantContext.TenantId, tenantContext.UserId, dto, ct);
            return Ok(result);
        }
        catch (InvalidOperationException ex) { return Conflict(new { message = ex.Message }); }
    }

    [HttpPost("manual")]
    public async Task<ActionResult<RegistroTempoDto>> CriarManual([FromBody] CriarRegistroManualDto dto, CancellationToken ct)
    {
        try
        {
            var result = await service.CriarManualAsync(tenantContext.TenantId, tenantContext.UserId, dto, ct);
            return CreatedAtAction(nameof(GetById), new { id = result.Id }, result);
        }
        catch (ArgumentException ex) { return BadRequest(new { message = ex.Message }); }
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<RegistroTempoDto>> Atualizar(Guid id, [FromBody] AtualizarRegistroDto dto, CancellationToken ct)
    {
        try
        {
            var result = await service.AtualizarAsync(id, tenantContext.TenantId, dto, ct);
            return Ok(result);
        }
        catch (KeyNotFoundException ex) { return NotFound(new { message = ex.Message }); }
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Deletar(Guid id, CancellationToken ct)
    {
        try
        {
            await service.DeletarAsync(id, tenantContext.TenantId, ct);
            return NoContent();
        }
        catch (KeyNotFoundException ex) { return NotFound(new { message = ex.Message }); }
    }
}
