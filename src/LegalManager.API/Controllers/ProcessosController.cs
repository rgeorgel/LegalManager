using LegalManager.Application.DTOs.Contatos;
using LegalManager.Application.DTOs.Processos;
using LegalManager.Application.Interfaces;
using LegalManager.Domain.Enums;
using LegalManager.Domain.Interfaces;
using LegalManager.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

// ReSharper disable RouteTemplates.ActionRoutePrefixCanBeExtractedToControllerRoute

namespace LegalManager.API.Controllers;

[ApiController]
[Route("api/processos")]
[Authorize]
public class ProcessosController : ControllerBase
{
    private readonly IProcessoService _service;
    private readonly IMonitoramentoService _monitoramento;
    private readonly IAuditService _audit;
    private readonly ITenantContext _tenantContext;

    public ProcessosController(IProcessoService service, IMonitoramentoService monitoramento, IAuditService audit, ITenantContext tenantContext)
    {
        _service = service;
        _monitoramento = monitoramento;
        _audit = audit;
        _tenantContext = tenantContext;
    }

    [HttpGet]
    public async Task<ActionResult<PagedResultDto<ProcessoListItemDto>>> GetAll(
        [FromQuery] string? busca,
        [FromQuery] string? status,
        [FromQuery] string? areaDireito,
        [FromQuery] Guid? advogadoResponsavelId,
        [FromQuery] Guid? contatoId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        var filtro = new ProcessoFiltroDto(
            busca,
            status != null && Enum.TryParse<StatusProcesso>(status, true, out var s) ? s : null,
            areaDireito != null && Enum.TryParse<AreaDireito>(areaDireito, true, out var a) ? a : null,
            advogadoResponsavelId,
            contatoId,
            page, pageSize);

        return Ok(await _service.GetAllAsync(filtro, ct));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ProcessoResponseDto>> GetById(Guid id, CancellationToken ct)
    {
        var result = await _service.GetByIdAsync(id, ct);
        return result == null ? NotFound() : Ok(result);
    }

    [HttpGet("{id:guid}/andamentos")]
    public async Task<ActionResult<IEnumerable<AndamentoResponseDto>>> GetAndamentos(Guid id, CancellationToken ct)
    {
        var result = await _service.GetAndamentosAsync(id, ct);
        return Ok(result);
    }

[HttpPost]
    public async Task<ActionResult<ProcessoResponseDto>> Create(CreateProcessoDto dto, CancellationToken ct)
    {
        var result = await _service.CreateAsync(dto, ct);
        await _audit.LogAsync(_tenantContext.CreateEntry(AuditActions.Create, AuditEntities.Processo, result.Id, null, dto), ct);
        return CreatedAtAction(nameof(GetById), new { id = result.Id }, result);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<ProcessoResponseDto>> Update(Guid id, UpdateProcessoDto dto, CancellationToken ct)
    {
        var existing = await _service.GetByIdAsync(id, ct);
        var result = await _service.UpdateAsync(id, dto, ct);
        await _audit.LogAsync(_tenantContext.CreateEntry(AuditActions.Update, AuditEntities.Processo, id, existing, result, HttpContext.GetClientIpAddress()), ct);
        return Ok(result);
    }

    [HttpPost("{id:guid}/encerrar")]
    public async Task<IActionResult> Encerrar(Guid id, EncerrarProcessoDto dto, CancellationToken ct)
    {
        var existing = await _service.GetByIdAsync(id, ct);
        await _service.EncerrarAsync(id, dto, ct);
        await _audit.LogAsync(_tenantContext.CreateEntry(AuditActions.Update, AuditEntities.Processo, id, existing, dto, HttpContext.GetClientIpAddress()), ct);
        return NoContent();
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Roles = "Admin,Advogado")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var existing = await _service.GetByIdAsync(id, ct);
        await _service.DeleteAsync(id, ct);
        await _audit.LogAsync(_tenantContext.CreateEntry(AuditActions.Delete, AuditEntities.Processo, id, existing, null, HttpContext.GetClientIpAddress()), ct);
        return NoContent();
    }

    [HttpPost("{id:guid}/andamentos")]
    public async Task<ActionResult<AndamentoResponseDto>> AddAndamento(Guid id, CreateAndamentoDto dto, CancellationToken ct)
    {
        var result = await _service.AddAndamentoAsync(id, dto, ct);
        await _audit.LogAsync(_tenantContext.CreateEntry(AuditActions.Create, AuditEntities.Processo + ".Andamento", result.Id, null, dto), ct);
        return Ok(result);
    }

    [HttpDelete("{id:guid}/andamentos/{andamentoId:guid}")]
    public async Task<IActionResult> DeleteAndamento(Guid id, Guid andamentoId, CancellationToken ct)
    {
        await _service.DeleteAndamentoAsync(id, andamentoId, ct);
        return NoContent();
    }

    [HttpPost("{id:guid}/monitoramento/alternar")]
    public async Task<IActionResult> AlternarMonitoramento(Guid id, CancellationToken ct)
    {
        var ativo = await _monitoramento.AlternarMonitoramentoAsync(id, ct);
        return Ok(new { monitorado = ativo });
    }

    [HttpPost("{id:guid}/monitoramento/executar")]
    [Authorize(Roles = "Admin,Advogado")]
    public async Task<IActionResult> ExecutarMonitoramento(Guid id, CancellationToken ct)
        => Ok(await _monitoramento.MonitorarProcessoAsync(id, ct));
}
