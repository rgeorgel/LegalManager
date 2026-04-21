using LegalManager.Application.DTOs.Financeiro;
using LegalManager.Application.Interfaces;
using LegalManager.Domain.Enums;
using LegalManager.Domain.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LegalManager.API.Controllers;

[ApiController]
[Route("api/financeiro")]
[Authorize]
public class FinanceiroController(IFinanceiroService service, ITenantContext tenantContext) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<LancamentosPagedDto>> GetAll(
        [FromQuery] TipoLancamento? tipo,
        [FromQuery] StatusLancamento? status,
        [FromQuery] Guid? processoId,
        [FromQuery] Guid? contatoId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] int? mes = null,
        [FromQuery] int? ano = null,
        CancellationToken ct = default)
    {
        var result = await service.GetAllAsync(tenantContext.TenantId, tipo, status, processoId, contatoId, page, pageSize, mes, ano, ct);
        return Ok(result);
    }

    [HttpGet("resumo")]
    public async Task<ActionResult<ResumoFinanceiroCompletoDto>> GetResumo(
        [FromQuery] int? ano,
        [FromQuery] int? mes,
        CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var result = await service.GetResumoCompletoAsync(
            tenantContext.TenantId,
            ano ?? now.Year,
            mes ?? now.Month,
            ct);
        return Ok(result);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<LancamentoDto>> GetById(Guid id, CancellationToken ct)
    {
        var item = await service.GetByIdAsync(id, tenantContext.TenantId, ct);
        return item == null ? NotFound() : Ok(item);
    }

    [HttpPost]
    public async Task<ActionResult<LancamentoDto>> Criar([FromBody] CriarLancamentoDto dto, CancellationToken ct)
    {
        var result = await service.CriarAsync(tenantContext.TenantId, dto, ct);
        return CreatedAtAction(nameof(GetById), new { id = result.Id }, result);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<LancamentoDto>> Atualizar(Guid id, [FromBody] AtualizarLancamentoDto dto, CancellationToken ct)
    {
        try
        {
            var result = await service.AtualizarAsync(id, tenantContext.TenantId, dto, ct);
            return Ok(result);
        }
        catch (KeyNotFoundException ex) { return NotFound(new { message = ex.Message }); }
    }

    [HttpPost("{id:guid}/pagar")]
    public async Task<IActionResult> Pagar(Guid id, [FromBody] PagarDto? dto, CancellationToken ct)
    {
        try
        {
            await service.PagarAsync(id, tenantContext.TenantId, dto?.DataPagamento, ct);
            return NoContent();
        }
        catch (KeyNotFoundException ex) { return NotFound(new { message = ex.Message }); }
    }

    [HttpPost("{id:guid}/cancelar")]
    public async Task<IActionResult> Cancelar(Guid id, CancellationToken ct)
    {
        try
        {
            await service.CancelarAsync(id, tenantContext.TenantId, ct);
            return NoContent();
        }
        catch (KeyNotFoundException ex) { return NotFound(new { message = ex.Message }); }
    }
}

public record PagarDto(DateTime? DataPagamento);
