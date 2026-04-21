using LegalManager.Application.DTOs.Atividades;
using LegalManager.Application.DTOs.Contatos;
using LegalManager.Application.Interfaces;
using LegalManager.Domain.Enums;
using LegalManager.Domain.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LegalManager.API.Controllers;

[ApiController]
[Route("api/tarefas")]
[Authorize]
public class TarefasController : ControllerBase
{
    private readonly ITarefaService _service;
    private readonly ITenantContext _tenantContext;

    public TarefasController(ITarefaService service, ITenantContext tenantContext)
    {
        _service = service;
        _tenantContext = tenantContext;
    }

    [HttpGet]
    public async Task<ActionResult<PagedResultDto<TarefaListItemDto>>> GetAll(
        [FromQuery] string? busca,
        [FromQuery] string? status,
        [FromQuery] string? prioridade,
        [FromQuery] Guid? responsavelId,
        [FromQuery] Guid? processoId,
        [FromQuery] Guid? contatoId,
        [FromQuery] bool? atrasada,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        var filtro = new TarefaFiltroDto(
            busca,
            status != null && Enum.TryParse<StatusTarefa>(status, true, out var s) ? s : null,
            prioridade != null && Enum.TryParse<PrioridadeTarefa>(prioridade, true, out var p) ? p : null,
            responsavelId, processoId, contatoId, atrasada, page, pageSize);

        return Ok(await _service.GetAllAsync(filtro, ct));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<TarefaResponseDto>> GetById(Guid id, CancellationToken ct)
    {
        var result = await _service.GetByIdAsync(id, ct);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpPost]
    public async Task<ActionResult<TarefaResponseDto>> Create([FromBody] CreateTarefaDto dto, CancellationToken ct)
    {
        var result = await _service.CreateAsync(dto, ct);
        return CreatedAtAction(nameof(GetById), new { id = result.Id }, result);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<TarefaResponseDto>> Update(Guid id, [FromBody] UpdateTarefaDto dto, CancellationToken ct)
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

    [HttpPost("{id:guid}/concluir")]
    public async Task<IActionResult> Concluir(Guid id, CancellationToken ct)
    {
        try
        {
            await _service.ConcluirAsync(id, ct);
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

    [HttpPatch("{id:guid}/mover")]
    public async Task<IActionResult> MoverKanban(Guid id, [FromBody] MoverKanbanDto dto, CancellationToken ct)
    {
        try
        {
            await _service.MoverKanbanAsync(id, _tenantContext.TenantId, dto.Status, ct);
            return NoContent();
        }
        catch (KeyNotFoundException) { return NotFound(); }
    }
}

public record MoverKanbanDto(StatusTarefa Status);
