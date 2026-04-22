using LegalManager.Application.DTOs.Prazos;
using LegalManager.Application.Interfaces;
using LegalManager.Domain;
using LegalManager.Domain.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LegalManager.API.Controllers;

[ApiController]
[Route("api/prazos")]
[Authorize]
public class PrazosController(IPrazoService service, ITenantContext tenantContext) : ControllerBase
{
    private readonly IPrazoService _service = service;

    [HttpGet]
    public async Task<IActionResult> GetPendentes(
        [FromQuery] int diasAteVencer = 30,
        CancellationToken ct = default)
        => Ok(await _service.GetPendentesAsync(diasAteVencer, ct));

    [HttpGet("processo/{processoId:guid}")]
    public async Task<IActionResult> GetByProcesso(Guid processoId, CancellationToken ct)
        => Ok(await _service.GetByProcessoAsync(processoId, ct));

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        var result = await _service.GetByIdAsync(id, ct);
        return result == null ? NotFound() : Ok(result);
    }

    [HttpPost]
    public async Task<ActionResult<PrazoResponseDto>> Create(CreatePrazoDto dto, CancellationToken ct)
    {
        var result = await _service.CreateAsync(dto, ct);
        return CreatedAtAction(nameof(GetById), new { id = result.Id }, result);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, UpdatePrazoDto dto, CancellationToken ct)
        => Ok(await _service.UpdateAsync(id, dto, ct));

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await _service.DeleteAsync(id, ct);
        return NoContent();
    }

    [HttpPost("calcular")]
    public IActionResult Calcular([FromBody] CalcularPrazoDto dto)
    {
        if (!PlanoRestricoes.PermiteCalculadoraPrazos(tenantContext.Plano))
            return StatusCode(402, new { message = "Calculadora de prazos disponível apenas no plano Pro." });

        var dataFinal = _service.CalcularDataFinal(dto.DataInicio, dto.QuantidadeDias,
            dto.TipoCalculo == Domain.Enums.TipoCalculo.DiasUteis);
        var feriados = Infrastructure.Services.FeriadosService.ListarFeriadosNoIntervalo(dto.DataInicio, dataFinal);
        return Ok(new CalcularPrazoResultDto(
            dto.DataInicio, dto.QuantidadeDias, dto.TipoCalculo,
            dataFinal, 0, feriados));
    }
}
