using LegalManager.Application.DTOs.SuperAdmin;
using LegalManager.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LegalManager.API.Controllers;

/// <summary>
/// Auditoria cross-tenant de consultas às APIs jurídicas externas (Escavador, DataJud,
/// e-SAJ/TJSP). Consumido pela página Super Admin "Consultas Externas".
/// </summary>
[ApiController]
[Route("api/superadmin/consultas")]
[Authorize(Roles = "SuperAdmin")]
public class ConsultasExternasController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<ConsultaExternaPagedResultDto>> GetAll(
        [FromQuery] Guid? tenantId,
        [FromQuery] string? tenantNome,
        [FromQuery] Guid? usuarioId,
        [FromQuery] string? api,
        [FromQuery] string? tipoConsulta,
        [FromQuery] string? origem,
        [FromQuery] bool? sucesso,
        [FromQuery] DateTime? de,
        [FromQuery] DateTime? ate,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);

        var query = db.ConsultasExternas.AsNoTracking().AsQueryable();

        if (tenantId.HasValue) query = query.Where(c => c.TenantId == tenantId.Value);
        if (!string.IsNullOrWhiteSpace(tenantNome))
        {
            var idsPorNome = await db.Tenants
                .Where(t => t.Nome.Contains(tenantNome))
                .Select(t => t.Id)
                .ToListAsync(ct);
            query = query.Where(c => idsPorNome.Contains(c.TenantId));
        }
        if (usuarioId.HasValue) query = query.Where(c => c.UsuarioId == usuarioId.Value);
        if (!string.IsNullOrWhiteSpace(api)) query = query.Where(c => c.Api == api);
        if (!string.IsNullOrWhiteSpace(tipoConsulta)) query = query.Where(c => c.TipoConsulta == tipoConsulta);
        if (!string.IsNullOrWhiteSpace(origem)) query = query.Where(c => c.Origem == origem);
        if (sucesso.HasValue) query = query.Where(c => c.Sucesso == sucesso.Value);
        if (de.HasValue) query = query.Where(c => c.CriadoEm >= de.Value);
        if (ate.HasValue) query = query.Where(c => c.CriadoEm <= ate.Value);

        var total = await query.CountAsync(ct);

        var logs = await query
            .OrderByDescending(c => c.CriadoEm)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        var tenantIds = logs.Select(l => l.TenantId).Distinct().ToList();
        var tenantNomes = await db.Tenants
            .Where(t => tenantIds.Contains(t.Id))
            .ToDictionaryAsync(t => t.Id, t => t.Nome, ct);

        var usuarioIds = logs.Where(l => l.UsuarioId != null).Select(l => l.UsuarioId!.Value).Distinct().ToList();
        var usuarioNomes = usuarioIds.Count > 0
            ? await db.Users.Where(u => usuarioIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.Nome, ct)
            : new Dictionary<Guid, string>();

        var items = logs.Select(l => new ConsultaExternaListItemDto(
            l.Id,
            l.CriadoEm,
            l.TenantId,
            tenantNomes.GetValueOrDefault(l.TenantId),
            l.UsuarioId,
            l.UsuarioId.HasValue ? usuarioNomes.GetValueOrDefault(l.UsuarioId.Value) : null,
            l.Api,
            l.TipoConsulta,
            l.Origem,
            l.Sucesso,
            l.QuantidadeResultados,
            l.DuracaoMs
        )).ToList();

        return Ok(new ConsultaExternaPagedResultDto(items, total, page, pageSize));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ConsultaExternaDetalheDto>> GetById(Guid id, CancellationToken ct)
    {
        var log = await db.ConsultasExternas.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, ct);
        if (log == null) return NotFound();

        var tenantNome = await db.Tenants.Where(t => t.Id == log.TenantId).Select(t => t.Nome).FirstOrDefaultAsync(ct);
        var usuarioNome = log.UsuarioId.HasValue
            ? await db.Users.Where(u => u.Id == log.UsuarioId.Value).Select(u => u.Nome).FirstOrDefaultAsync(ct)
            : null;

        return Ok(new ConsultaExternaDetalheDto(
            log.Id, log.CriadoEm, log.TenantId, tenantNome, log.UsuarioId, usuarioNome,
            log.Api, log.TipoConsulta, log.Origem, log.ParametrosJson,
            log.Sucesso, log.MensagemErro, log.QuantidadeResultados, log.ResultadoResumoJson, log.DuracaoMs
        ));
    }

    /// <summary>Valores distintos já usados, para popular os selects de filtro no frontend.</summary>
    [HttpGet("filtros")]
    public async Task<IActionResult> GetFiltros(CancellationToken ct)
    {
        var apis = await db.ConsultasExternas.Select(c => c.Api).Distinct().OrderBy(x => x).ToListAsync(ct);
        var tipos = await db.ConsultasExternas.Select(c => c.TipoConsulta).Distinct().OrderBy(x => x).ToListAsync(ct);
        var origens = await db.ConsultasExternas.Select(c => c.Origem).Distinct().OrderBy(x => x).ToListAsync(ct);
        return Ok(new { apis, tipos, origens });
    }
}
