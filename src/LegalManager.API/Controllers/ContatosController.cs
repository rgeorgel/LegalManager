using LegalManager.Application.DTOs.Contatos;
using LegalManager.Application.DTOs.PortalCliente;
using LegalManager.Application.Interfaces;
using LegalManager.Domain.Interfaces;
using LegalManager.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LegalManager.API.Controllers;

[ApiController]
[Route("api/contatos")]
[Authorize]
public class ContatosController : ControllerBase
{
    private readonly IContatoService _service;
    private readonly IPortalClienteService _portalService;
    private readonly ITenantContext _tenantContext;
    private readonly IAuditService _audit;

    public ContatosController(IContatoService service, IPortalClienteService portalService, ITenantContext tenantContext, IAuditService audit)
    {
        _service = service;
        _portalService = portalService;
        _tenantContext = tenantContext;
        _audit = audit;
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
        await _audit.LogAsync(_tenantContext.CreateEntry(AuditActions.Create, AuditEntities.Contato, result.Id, null, dto), ct);
        return CreatedAtAction(nameof(GetById), new { id = result.Id }, result);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<ContatoResponseDto>> Update(Guid id, [FromBody] UpdateContatoDto dto, CancellationToken ct)
    {
        var existing = await _service.GetByIdAsync(id, ct);
        var result = await _service.UpdateAsync(id, dto, ct);
        await _audit.LogAsync(_tenantContext.CreateEntry(AuditActions.Update, AuditEntities.Contato, id, existing, result, HttpContext.GetClientIpAddress()), ct);
        return Ok(result);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var existing = await _service.GetByIdAsync(id, ct);
        await _service.DeleteAsync(id, ct);
        await _audit.LogAsync(_tenantContext.CreateEntry(AuditActions.Delete, AuditEntities.Contato, id, existing, null, HttpContext.GetClientIpAddress()), ct);
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

    [HttpPost("{id:guid}/portal-acesso")]
    public async Task<ActionResult<AcessoPortalInfoDto>> CriarPortalAcesso(Guid id, [FromBody] CriarAcessoPortalDto dto, CancellationToken ct)
    {
        var result = await _portalService.CriarAcessoAsync(id, dto, _tenantContext.TenantId, ct);
        return Ok(result);
    }

    [HttpGet("{id:guid}/portal-acesso")]
    public async Task<ActionResult<AcessoPortalInfoDto>> GetPortalAcesso(Guid id, CancellationToken ct)
    {
        var result = await _portalService.GetAcessoAsync(id, _tenantContext.TenantId, ct);
        return result == null ? NotFound() : Ok(result);
    }

    [HttpDelete("{id:guid}/portal-acesso")]
    public async Task<IActionResult> RevogarPortalAcesso(Guid id, CancellationToken ct)
    {
        await _portalService.RevogarAcessoAsync(id, _tenantContext.TenantId, ct);
        return NoContent();
    }
}