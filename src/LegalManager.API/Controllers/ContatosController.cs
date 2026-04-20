using LegalManager.Application.DTOs.Contatos;
using LegalManager.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LegalManager.API.Controllers;

[ApiController]
[Route("api/contatos")]
[Authorize]
public class ContatosController : ControllerBase
{
    private readonly IContatoService _service;

    public ContatosController(IContatoService service)
    {
        _service = service;
    }

    [HttpGet]
    public async Task<ActionResult<PagedResultDto<ContatoListItemDto>>> GetAll(
        [FromQuery] string? busca,
        [FromQuery] string? tipoContato,
        [FromQuery] string? tipo,
        [FromQuery] string? tag,
        [FromQuery] bool? ativo,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        var filtro = new ContatoFiltroDto(
            busca,
            tipoContato != null && Enum.TryParse<Domain.Enums.TipoContato>(tipoContato, true, out var tc) ? tc : null,
            tipo != null && Enum.TryParse<Domain.Enums.TipoPessoa>(tipo, true, out var tp) ? tp : null,
            tag, ativo, page, pageSize);

        var result = await _service.GetAllAsync(filtro, ct);
        return Ok(result);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ContatoResponseDto>> GetById(Guid id, CancellationToken ct)
    {
        var result = await _service.GetByIdAsync(id, ct);
        return result == null ? NotFound() : Ok(result);
    }

    [HttpPost]
    public async Task<ActionResult<ContatoResponseDto>> Create([FromBody] CreateContatoDto dto, CancellationToken ct)
    {
        var result = await _service.CreateAsync(dto, ct);
        return CreatedAtAction(nameof(GetById), new { id = result.Id }, result);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<ContatoResponseDto>> Update(Guid id, UpdateContatoDto dto, CancellationToken ct)
    {
        var result = await _service.UpdateAsync(id, dto, ct);
        return Ok(result);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await _service.DeleteAsync(id, ct);
        return NoContent();
    }

    [HttpGet("{id:guid}/atendimentos")]
    public async Task<ActionResult<IEnumerable<AtendimentoResponseDto>>> GetAtendimentos(Guid id, CancellationToken ct)
    {
        var result = await _service.GetAtendimentosAsync(id, ct);
        return Ok(result);
    }

    [HttpPost("{id:guid}/atendimentos")]
    public async Task<ActionResult<AtendimentoResponseDto>> AddAtendimento(Guid id, CreateAtendimentoDto dto, CancellationToken ct)
    {
        var result = await _service.AddAtendimentoAsync(id, dto, ct);
        return Ok(result);
    }
}
