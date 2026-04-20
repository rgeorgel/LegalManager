using LegalManager.Application.DTOs.Atividades;
using LegalManager.Application.DTOs.Contatos;
using LegalManager.Application.Interfaces;
using LegalManager.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LegalManager.API.Controllers;

[ApiController]
[Route("api/eventos")]
[Authorize]
public class EventosController : ControllerBase
{
    private readonly IEventoService _service;

    public EventosController(IEventoService service)
    {
        _service = service;
    }

    [HttpGet]
    public async Task<ActionResult<PagedResultDto<EventoResponseDto>>> GetAll(
        [FromQuery] DateTime? de,
        [FromQuery] DateTime? ate,
        [FromQuery] string? tipo,
        [FromQuery] Guid? responsavelId,
        [FromQuery] Guid? processoId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken ct = default)
    {
        var filtro = new EventoFiltroDto(
            de, ate,
            tipo != null && Enum.TryParse<TipoEvento>(tipo, true, out var t) ? t : null,
            responsavelId, processoId, page, pageSize);

        return Ok(await _service.GetAllAsync(filtro, ct));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<EventoResponseDto>> GetById(Guid id, CancellationToken ct)
    {
        var result = await _service.GetByIdAsync(id, ct);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpPost]
    public async Task<ActionResult<EventoResponseDto>> Create([FromBody] CreateEventoDto dto, CancellationToken ct)
    {
        var result = await _service.CreateAsync(dto, ct);
        return CreatedAtAction(nameof(GetById), new { id = result.Id }, result);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<EventoResponseDto>> Update(Guid id, [FromBody] UpdateEventoDto dto, CancellationToken ct)
    {
        try
        {
            return Ok(await _service.UpdateAsync(id, dto, ct));
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

[ApiController]
[Route("api/agenda")]
[Authorize]
public class AgendaController : ControllerBase
{
    private readonly IEventoService _service;

    public AgendaController(IEventoService service)
    {
        _service = service;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<AgendaItemDto>>> GetAgenda(
        [FromQuery] DateTime de,
        [FromQuery] DateTime ate,
        [FromQuery] Guid? responsavelId,
        [FromQuery] Guid? processoId,
        CancellationToken ct = default)
    {
        var filtro = new AgendaFiltroDto(de, ate, responsavelId, processoId);
        return Ok(await _service.GetAgendaAsync(filtro, ct));
    }
}
