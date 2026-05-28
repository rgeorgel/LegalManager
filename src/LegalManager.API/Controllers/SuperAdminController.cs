using LegalManager.Application.DTOs.SuperAdmin;
using LegalManager.Domain.Enums;
using LegalManager.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LegalManager.API.Controllers;

[ApiController]
[Route("api/superadmin")]
[Authorize(Roles = "SuperAdmin")]
public class SuperAdminController(AppDbContext db) : ControllerBase
{
    private static readonly Guid SystemTenantId = new("00000000-0000-0000-0000-000000000001");

    private static readonly Dictionary<string, decimal> PlanoPrices = new()
    {
        ["Free"] = 0m,
        ["Plus"] = 79m,
        ["Pro"] = 149m,
        ["Max"] = 299m,
        ["Enterprise"] = 499m
    };

    [HttpGet("metrics")]
    public async Task<IActionResult> GetMetrics(CancellationToken ct)
    {
        var tenants = await db.Tenants
            .Where(t => t.Id != SystemTenantId)
            .Select(t => new { t.Id, t.Plano, t.Status, t.CriadoEm })
            .ToListAsync(ct);

        var userCounts = await db.Users
            .Where(u => u.TenantId != SystemTenantId)
            .GroupBy(u => u.Ativo)
            .Select(g => new { Ativo = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        var waitlistPending = await db.WaitlistEntries
            .CountAsync(w => w.Status == null || w.Status == "Pendente", ct);

        var recentSignups = await db.Tenants
            .Where(t => t.Id != SystemTenantId)
            .OrderByDescending(t => t.CriadoEm)
            .Take(10)
            .Select(t => new RecentTenantDto(t.Id, t.Nome, t.Plano.ToString(), t.Status.ToString(), t.CriadoEm))
            .ToListAsync(ct);

        var byPlan = tenants
            .GroupBy(t => t.Plano.ToString())
            .ToDictionary(g => g.Key, g => g.Count());

        var activeTenants = tenants.Count(t => t.Status == StatusTenant.Ativo);
        var trialTenants = tenants.Count(t => t.Status == StatusTenant.Trial);
        var suspendedTenants = tenants.Count(t => t.Status == StatusTenant.Suspenso);
        var canceledTenants = tenants.Count(t => t.Status == StatusTenant.Cancelado);

        var totalUsers = userCounts.Sum(u => u.Count);
        var activeUsers = userCounts.FirstOrDefault(u => u.Ativo)?.Count ?? 0;

        var mrrEstimate = tenants
            .Where(t => t.Status is StatusTenant.Ativo or StatusTenant.Trial)
            .Sum(t => PlanoPrices.TryGetValue(t.Plano.ToString(), out var price) ? price : 0m);

        return Ok(new SystemMetricsDto(
            tenants.Count, activeTenants, trialTenants, suspendedTenants, canceledTenants,
            byPlan, totalUsers, activeUsers, waitlistPending, mrrEstimate, recentSignups
        ));
    }

    [HttpGet("tenants")]
    public async Task<IActionResult> GetTenants(
        [FromQuery] string? search,
        [FromQuery] string? status,
        [FromQuery] string? plano,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        var query = db.Tenants
            .Where(t => t.Id != SystemTenantId)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(t => t.Nome.Contains(search) || (t.Cnpj != null && t.Cnpj.Contains(search)));

        if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<StatusTenant>(status, true, out var statusEnum))
            query = query.Where(t => t.Status == statusEnum);

        if (!string.IsNullOrWhiteSpace(plano) && Enum.TryParse<PlanoTipo>(plano, true, out var planoEnum))
            query = query.Where(t => t.Plano == planoEnum);

        var total = await query.CountAsync(ct);

        var tenantIds = await query
            .OrderByDescending(t => t.CriadoEm)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(t => t.Id)
            .ToListAsync(ct);

        var userCounts = await db.Users
            .Where(u => tenantIds.Contains(u.TenantId))
            .GroupBy(u => u.TenantId)
            .Select(g => new { TenantId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.TenantId, g => g.Count, ct);

        var processoCounts = await db.Processos
            .Where(p => tenantIds.Contains(p.TenantId))
            .GroupBy(p => p.TenantId)
            .Select(g => new { TenantId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.TenantId, g => g.Count, ct);

        var docStats = await db.Documentos
            .Where(d => tenantIds.Contains(d.TenantId))
            .GroupBy(d => d.TenantId)
            .Select(g => new { TenantId = g.Key, Count = g.Count(), TotalBytes = g.Sum(d => d.TamanhoBytes) })
            .ToDictionaryAsync(g => g.TenantId, g => (g.Count, g.TotalBytes), ct);

        var tarefaCounts = await db.Tarefas
            .Where(t => tenantIds.Contains(t.TenantId))
            .GroupBy(t => t.TenantId)
            .Select(g => new { TenantId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.TenantId, g => g.Count, ct);

        var tenants = await db.Tenants
            .Where(t => tenantIds.Contains(t.Id))
            .OrderByDescending(t => t.CriadoEm)
            .Select(t => t)
            .ToListAsync(ct);

        var items = tenants.Select(t => new TenantListItemDto(
            t.Id, t.Nome, t.Cnpj, t.Plano.ToString(), t.Status.ToString(),
            t.CriadoEm,
            userCounts.GetValueOrDefault(t.Id),
            t.TrialExpiraEm, t.PlanoExpiraEm,
            processoCounts.GetValueOrDefault(t.Id),
            docStats.TryGetValue(t.Id, out var ds) ? ds.Count : 0,
            docStats.TryGetValue(t.Id, out var dsb) ? dsb.TotalBytes : 0,
            tarefaCounts.GetValueOrDefault(t.Id)
        )).ToList();

        return Ok(new { total, page, pageSize, items });
    }

    [HttpGet("tenants/{id:guid}")]
    public async Task<IActionResult> GetTenant(Guid id, CancellationToken ct)
    {
        var tenant = await db.Tenants
            .Include(t => t.Usuarios)
            .FirstOrDefaultAsync(t => t.Id == id && t.Id != SystemTenantId, ct);

        if (tenant == null) return NotFound();

        var processoCount = await db.Processos.CountAsync(p => p.TenantId == id, ct);
        var docResult = await db.Documentos
            .Where(d => d.TenantId == id)
            .GroupBy(_ => 1)
            .Select(g => new { Count = g.Count(), TotalBytes = g.Sum(d => d.TamanhoBytes) })
            .FirstOrDefaultAsync(ct);
        var tarefaCount = await db.Tarefas.CountAsync(t => t.TenantId == id, ct);

        var dto = new TenantDetailDto(
            tenant.Id, tenant.Nome, tenant.Cnpj, tenant.Endereco,
            tenant.Plano.ToString(), tenant.PeriodoBilling, tenant.Status.ToString(),
            tenant.CriadoEm, tenant.Usuarios.Count, tenant.TrialExpiraEm, tenant.PlanoExpiraEm,
            tenant.AbacatePayBillingId,
            processoCount,
            docResult?.Count ?? 0,
            docResult?.TotalBytes ?? 0,
            tarefaCount,
            tenant.Usuarios.Select(u => new TenantUserDto(u.Id, u.Nome, u.Email, u.Perfil.ToString(), u.Ativo, u.UltimoAcessoEm)).ToList()
        );

        return Ok(dto);
    }

    [HttpPut("tenants/{id:guid}/plano")]
    public async Task<IActionResult> UpdatePlano(Guid id, [FromBody] UpdateTenantPlanoDto dto, CancellationToken ct)
    {
        var tenant = await db.Tenants.FindAsync([id], ct);
        if (tenant == null || tenant.Id == SystemTenantId) return NotFound();

        if (!Enum.TryParse<PlanoTipo>(dto.Plano, true, out var plano))
            return BadRequest(new { message = "Plano inválido." });

        tenant.Plano = plano;
        tenant.PeriodoBilling = dto.PeriodoBilling;
        await db.SaveChangesAsync(ct);

        return NoContent();
    }

    [HttpPut("tenants/{id:guid}/status")]
    public async Task<IActionResult> UpdateStatus(Guid id, [FromBody] UpdateTenantStatusDto dto, CancellationToken ct)
    {
        var tenant = await db.Tenants.FindAsync([id], ct);
        if (tenant == null || tenant.Id == SystemTenantId) return NotFound();

        if (!Enum.TryParse<StatusTenant>(dto.Status, true, out var status))
            return BadRequest(new { message = "Status inválido." });

        tenant.Status = status;
        await db.SaveChangesAsync(ct);

        return NoContent();
    }

    [HttpGet("users")]
    public async Task<IActionResult> GetUsers(
        [FromQuery] string? search,
        [FromQuery] Guid? tenantId,
        [FromQuery] bool? ativo,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 30,
        CancellationToken ct = default)
    {
        var query = db.Users
            .Include(u => u.Tenant)
            .Where(u => u.TenantId != SystemTenantId)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(u => u.Nome.Contains(search) || (u.Email != null && u.Email.Contains(search)));

        if (tenantId.HasValue)
            query = query.Where(u => u.TenantId == tenantId.Value);

        if (ativo.HasValue)
            query = query.Where(u => u.Ativo == ativo.Value);

        var total = await query.CountAsync(ct);

        var users = await query
            .OrderByDescending(u => u.CriadoEm)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(u => new UserListItemDto(
                u.Id, u.Nome, u.Email, u.TenantId,
                u.Tenant.Nome, u.Tenant.Plano.ToString(),
                u.Perfil.ToString(), u.Ativo, u.UltimoAcessoEm, u.CriadoEm))
            .ToListAsync(ct);

        return Ok(new { total, page, pageSize, items = users });
    }

    [HttpGet("waitlist")]
    public async Task<IActionResult> GetWaitlist(
        [FromQuery] string? status,
        [FromQuery] string? plano,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 30,
        CancellationToken ct = default)
    {
        var query = db.WaitlistEntries.AsQueryable();

        if (!string.IsNullOrWhiteSpace(status))
        {
            if (status.Equals("Pendente", StringComparison.OrdinalIgnoreCase))
                query = query.Where(w => w.Status == null || w.Status == "Pendente");
            else
                query = query.Where(w => w.Status == status);
        }

        if (!string.IsNullOrWhiteSpace(plano))
            query = query.Where(w => w.Plano == plano);

        var total = await query.CountAsync(ct);

        var items = await query
            .OrderByDescending(w => w.CriadoEm)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(w => new WaitlistListItemDto(
                w.Id, w.Nome, w.Email, w.Plano, w.CriadoEm,
                w.Status ?? "Pendente"))
            .ToListAsync(ct);

        return Ok(new { total, page, pageSize, items });
    }

    [HttpGet("vouchers/{codigo}/usuarios")]
    public async Task<IActionResult> GetVoucherUsuarios(string codigo, CancellationToken ct)
    {
        var code = codigo.Trim().ToLowerInvariant();

        var tenants = await db.Tenants
            .Where(t => t.VoucherUtilizado == code)
            .OrderByDescending(t => t.CriadoEm)
            .Select(t => new
            {
                t.Id,
                NomeEscritorio = t.Nome,
                EmailAdmin = db.Users
                    .Where(u => u.TenantId == t.Id && u.Perfil == PerfilUsuario.Admin)
                    .Select(u => u.Email)
                    .FirstOrDefault(),
                t.CriadoEm,
                t.PlanoExpiraEm
            })
            .ToListAsync(ct);

        return Ok(new { voucher = code, total = tenants.Count, usuarios = tenants });
    }

    [HttpPut("waitlist/{id:guid}/status")]
    public async Task<IActionResult> UpdateWaitlistStatus(Guid id, [FromBody] UpdateWaitlistStatusDto dto, CancellationToken ct)
    {
        var entry = await db.WaitlistEntries.FindAsync([id], ct);
        if (entry == null) return NotFound();

        var validStatuses = new[] { "Pendente", "Aprovado", "Rejeitado" };
        if (!validStatuses.Contains(dto.Status))
            return BadRequest(new { message = "Status inválido. Use: Pendente, Aprovado ou Rejeitado." });

        entry.Status = dto.Status;
        await db.SaveChangesAsync(ct);

        return NoContent();
    }
}
