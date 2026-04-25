using LegalManager.Application.DTOs.Financeiro;
using LegalManager.Application.Interfaces;
using LegalManager.Domain;
using LegalManager.Domain.Enums;
using LegalManager.Domain.Interfaces;
using LegalManager.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LegalManager.API.Controllers;

[ApiController]
[Route("api/financeiro")]
[Authorize]
public class FinanceiroController(IFinanceiroService service, ITenantContext tenantContext, IAuditService audit) : ControllerBase
{
    private ActionResult? CheckPlano() =>
        !PlanoRestricoes.PermiteFinanceiro(tenantContext.Plano)
            ? StatusCode(402, new { message = "Controle financeiro disponível apenas no plano Pro." })
            : null;

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
        if (CheckPlano() is { } err) return err;
        var result = await service.GetAllAsync(tenantContext.TenantId, tipo, status, processoId, contatoId, page, pageSize, mes, ano, ct);
        return Ok(result);
    }

    [HttpGet("resumo")]
    public async Task<ActionResult<ResumoFinanceiroCompletoDto>> GetResumo(
        [FromQuery] int? ano,
        [FromQuery] int? mes,
        CancellationToken ct = default)
    {
        if (CheckPlano() is { } err) return err;
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
        if (CheckPlano() is { } err) return err;
        var item = await service.GetByIdAsync(id, tenantContext.TenantId, ct);
        return item == null ? NotFound() : Ok(item);
    }

    [HttpPost]
    public async Task<ActionResult<LancamentoDto>> Criar([FromBody] CriarLancamentoDto dto, CancellationToken ct)
    {
        if (CheckPlano() is { } err) return err;
        var result = await service.CriarAsync(tenantContext.TenantId, dto, ct);
        await audit.LogAsync(tenantContext.CreateEntry(AuditActions.Create, AuditEntities.Financeiro, result.Id, null, dto), ct);
        return CreatedAtAction(nameof(GetById), new { id = result.Id }, result);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<LancamentoDto>> Atualizar(Guid id, [FromBody] AtualizarLancamentoDto dto, CancellationToken ct)
    {
        if (CheckPlano() is { } err) return err;
        try
        {
            var existing = await service.GetByIdAsync(id, tenantContext.TenantId, ct);
            var result = await service.AtualizarAsync(id, tenantContext.TenantId, dto, ct);
            await audit.LogAsync(tenantContext.CreateEntry(AuditActions.Update, AuditEntities.Financeiro, id, existing, result, HttpContext.GetClientIpAddress()), ct);
            return Ok(result);
        }
        catch (KeyNotFoundException ex) { return NotFound(new { message = ex.Message }); }
    }

    [HttpPost("{id:guid}/pagar")]
    public async Task<IActionResult> Pagar(Guid id, [FromBody] PagarDto? dto, CancellationToken ct)
    {
        if (CheckPlano() is { } err) return err;
        try
        {
            var existing = await service.GetByIdAsync(id, tenantContext.TenantId, ct);
            await service.PagarAsync(id, tenantContext.TenantId, dto?.DataPagamento, ct);
            await audit.LogAsync(tenantContext.CreateEntry(AuditActions.Update, AuditEntities.Financeiro, id, existing, new { Status = "Pago", DataPagamento = dto?.DataPagamento }, HttpContext.GetClientIpAddress()), ct);
            return NoContent();
        }
        catch (KeyNotFoundException ex) { return NotFound(new { message = ex.Message }); }
    }

    [HttpPost("{id:guid}/cancelar")]
    public async Task<IActionResult> Cancelar(Guid id, CancellationToken ct)
    {
        if (CheckPlano() is { } err) return err;
        try
        {
            var existing = await service.GetByIdAsync(id, tenantContext.TenantId, ct);
            await service.CancelarAsync(id, tenantContext.TenantId, ct);
            await audit.LogAsync(tenantContext.CreateEntry(AuditActions.Delete, AuditEntities.Financeiro, id, existing, null, HttpContext.GetClientIpAddress()), ct);
            return NoContent();
        }
        catch (KeyNotFoundException ex) { return NotFound(new { message = ex.Message }); }
    }
}

public record PagarDto(DateTime? DataPagamento);