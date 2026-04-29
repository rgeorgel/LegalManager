using LegalManager.Application.DTOs.Modelos;
using LegalManager.Application.Interfaces;
using LegalManager.Domain.Enums;
using LegalManager.Domain.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LegalManager.API.Controllers;

[ApiController]
[Route("api/modelos")]
[Authorize]
public class ModelosController : ControllerBase
{
    private readonly IModeloDocumentoService _service;
    private readonly ITenantContext _tenantContext;

    public ModelosController(IModeloDocumentoService service, ITenantContext tenantContext)
    {
        _service = service;
        _tenantContext = tenantContext;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<ModeloDocumentoDto>>> GetAll(CancellationToken ct = default)
    {
        var result = await _service.GetAllAsync(ct);
        return Ok(result);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ModeloDocumentoDto>> GetById(Guid id, CancellationToken ct = default)
    {
        var result = await _service.GetByIdAsync(id, ct);
        return result == null ? NotFound() : Ok(result);
    }

    [HttpPost]
    public async Task<ActionResult<ModeloDocumentoDto>> Create([FromBody] CreateModeloDocumentoDto dto, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(dto.Nome))
            return BadRequest("Nome é obrigatório.");

        var result = await _service.CreateAsync(dto, ct);
        return CreatedAtAction(nameof(GetById), new { id = result.Id }, result);
    }

    [HttpPost("gerar-com-ia")]
    public async Task<ActionResult<GerarModeloComIAResultDto>> GerarComIA([FromBody] GerarModeloComIADto dto, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(dto.Descricao))
            return BadRequest("Descrição é obrigatória.");

        var result = await _service.GerarComIAAsync(dto.Descricao, ct);
        return Ok(result);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<ModeloDocumentoDto>> Update(Guid id, [FromBody] UpdateModeloDocumentoDto dto, CancellationToken ct = default)
    {
        try
        {
            var result = await _service.UpdateAsync(id, dto, ct);
            return Ok(result);
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct = default)
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

    [HttpPost("{id:guid}/aplicar")]
    public async Task<ActionResult<string>> AplicarVariaveis(Guid id, [FromBody] Dictionary<string, string> variaveis, CancellationToken ct = default)
    {
        try
        {
            var resultado = await _service.AplicarVariaveisAsync(id, variaveis, ct);
            return Ok(new { conteudo = resultado });
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
    }
}
