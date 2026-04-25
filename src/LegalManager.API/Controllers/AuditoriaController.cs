using LegalManager.Application.Interfaces;
using LegalManager.Domain.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LegalManager.API.Controllers;

[ApiController]
[Route("api/auditoria")]
[Authorize(Roles = "Admin")]
public class AuditoriaController : ControllerBase
{
    private readonly IAuditService _audit;

    public AuditoriaController(IAuditService audit)
    {
        _audit = audit;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<AuditLogResponseDto>>> GetAll(
        [FromQuery] DateTime? de,
        [FromQuery] DateTime? ate,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken ct = default)
    {
        var result = await _audit.GetByTenantAsync(de, ate, page, pageSize, ct);
        return Ok(result);
    }

    [HttpGet("entidade/{entity}/{entityId:guid}")]
    public async Task<ActionResult<IEnumerable<AuditLogResponseDto>>> GetByEntity(string entity, Guid entityId, CancellationToken ct)
    {
        var result = await _audit.GetByEntityAsync(entity, entityId, ct);
        return Ok(result);
    }
}