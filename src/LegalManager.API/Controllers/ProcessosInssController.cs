using LegalManager.Application.DTOs.Processos;
using LegalManager.Application.Interfaces;
using LegalManager.Domain;
using LegalManager.Domain.Interfaces;
using LegalManager.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LegalManager.API.Controllers;

/// <summary>Requerimento no INSS de um processo administrativo e os prazos que ele sugere.</summary>
[ApiController]
[Authorize]
public class ProcessosInssController(
    IRequerimentoInssService service, IAuditService audit, ITenantContext tenantContext) : ControllerBase
{
    [HttpGet("api/inss/especies")]
    public IEnumerable<EspecieBeneficioDto> Especies() =>
        EspeciesBeneficioInss.Nomes.Select(e => new EspecieBeneficioDto(e.Key, e.Value));

    /// <summary>204 quando o processo ainda não tem dados do INSS.</summary>
    [HttpGet("api/processos/{id:guid}/inss")]
    public async Task<ActionResult<RequerimentoInssResponseDto>> Obter(Guid id, CancellationToken ct)
    {
        try
        {
            var dto = await service.ObterAsync(id, ct);
            return dto is null ? NoContent() : Ok(dto);
        }
        catch (KeyNotFoundException) { return NotFound(); }
    }

    [HttpPut("api/processos/{id:guid}/inss")]
    public async Task<ActionResult<RequerimentoInssResponseDto>> Salvar(Guid id, RequerimentoInssDto dto, CancellationToken ct)
    {
        try
        {
            var antes = await service.ObterAsync(id, ct);
            var result = await service.SalvarAsync(id, dto, ct);
            await audit.LogAsync(tenantContext.CreateEntry(AuditActions.Update, AuditEntities.Processo, id,
                antes is null ? null : antes with { Sugestoes = [] }, dto, HttpContext.GetClientIpAddress()), ct);
            return Ok(result);
        }
        catch (KeyNotFoundException) { return NotFound(); }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }

    [HttpPost("api/processos/{id:guid}/inss/sugestoes/{chave}")]
    public async Task<ActionResult<SugestaoInssCriadaDto>> CriarSugestao(Guid id, string chave, CancellationToken ct)
    {
        try
        {
            var criado = await service.CriarSugestaoAsync(id, chave, ct);
            await audit.LogAsync(tenantContext.CreateEntry(AuditActions.Create,
                criado.Tipo == "evento" ? AuditEntities.Evento : AuditEntities.Tarefa, criado.Id, null,
                new { ProcessoId = id, Sugestao = chave }, HttpContext.GetClientIpAddress()), ct);
            return Ok(criado);
        }
        catch (KeyNotFoundException ex) { return NotFound(new { message = ex.Message }); }
    }
}
