using System.Security.Claims;
using LegalManager.Application.DTOs.SuperAdmin;
using LegalManager.Application.Interfaces;
using LegalManager.Domain;
using LegalManager.Domain.Enums;
using LegalManager.Infrastructure.Persistence;
using LegalManager.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LegalManager.API.Controllers;

[ApiController]
[Route("api/superadmin")]
[Authorize(Roles = "SuperAdmin")]
public class SuperAdminController(AppDbContext db, IAuditService audit, AuthService authService) : ControllerBase
{
    private static readonly Guid SystemTenantId = TenantConstants.SystemTenantId;

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
        [FromQuery] string? sortBy,
        [FromQuery] string? sortDir,
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

        var normalizedSortBy = sortBy?.Trim().ToLowerInvariant();
        if (normalizedSortBy is not ("nome" or "cadastro" or "ultimoacesso"))
            normalizedSortBy = "cadastro";

        var ascending = string.Equals(sortDir?.Trim(), "asc", StringComparison.OrdinalIgnoreCase);

        var total = await query.CountAsync(ct);

        // Dicionário só é pré-preenchido quando o sort é por ultimoAcesso (ver abaixo);
        // nos demais casos é computado depois, como já era feito, restrito à página atual.
        Dictionary<Guid, DateTime?>? ultimoAcessoPorTenant = null;
        List<Guid> tenantIds;

        if (normalizedSortBy == "ultimoacesso")
        {
            // Ordenar por um agregado (MAX de UltimoAcessoEm por tenant) exige calcular o
            // agregado para TODO o conjunto filtrado antes do Skip/Take -- não dá pra paginar
            // primeiro e agregar depois, como é feito para colunas diretas do Tenant.
            var ultimoAcessoAgg = db.Users
                .Where(u => u.UltimoAcessoEm != null)
                .GroupBy(u => u.TenantId)
                .Select(g => new { TenantId = g.Key, UltimoAcesso = g.Max(u => u.UltimoAcessoEm) });

            var joined = query
                .GroupJoin(ultimoAcessoAgg, t => t.Id, a => a.TenantId, (t, agg) => new { Tenant = t, Agg = agg })
                .SelectMany(
                    x => x.Agg.DefaultIfEmpty(),
                    (x, agg) => new { x.Tenant.Id, UltimoAcesso = agg != null ? agg.UltimoAcesso : null });

            // Tenants sem nenhum acesso (UltimoAcesso null) sempre por último, seja asc ou desc --
            // "nunca acessou" não é nem o mais recente nem o mais antigo, é desconhecido.
            var ordered = ascending
                ? joined.OrderBy(x => x.UltimoAcesso == null).ThenBy(x => x.UltimoAcesso)
                : joined.OrderBy(x => x.UltimoAcesso == null).ThenByDescending(x => x.UltimoAcesso);

            var pagedJoined = await ordered
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync(ct);

            tenantIds = pagedJoined.Select(x => x.Id).ToList();
            ultimoAcessoPorTenant = pagedJoined.ToDictionary(x => x.Id, x => x.UltimoAcesso);
        }
        else
        {
            var orderedTenants = normalizedSortBy == "nome"
                ? (ascending ? query.OrderBy(t => t.Nome) : query.OrderByDescending(t => t.Nome))
                : (ascending ? query.OrderBy(t => t.CriadoEm) : query.OrderByDescending(t => t.CriadoEm));

            tenantIds = await orderedTenants
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(t => t.Id)
                .ToListAsync(ct);
        }

        var userCounts = await db.Users
            .Where(u => tenantIds.Contains(u.TenantId))
            .GroupBy(u => u.TenantId)
            .Select(g => new { TenantId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.TenantId, g => g.Count, ct);

        var processoCounts = await db.Processos
            .Where(p => tenantIds.Contains(p.TenantId))
            .GroupBy(p => p.TenantId)
            .Select(g => new
            {
                TenantId = g.Key,
                Count = g.Count(),
                Monitorados = g.Count(p => p.Monitorado),
                MonitoradosDiario = g.Count(p => p.Monitorado && !p.MonitoramentoSemanal),
                MonitoradosSemanal = g.Count(p => p.Monitorado && p.MonitoramentoSemanal)
            })
            .ToDictionaryAsync(g => g.TenantId, ct);

        var docStats = await db.Documentos
            .Where(d => tenantIds.Contains(d.TenantId))
            .GroupBy(d => d.TenantId)
            .Select(g => new { TenantId = g.Key, Count = g.Count(), TotalBytes = g.Sum(d => d.TamanhoBytes) })
            .ToDictionaryAsync(g => g.TenantId, g => (g.Count, g.TotalBytes), ct);

        var tarefaCounts = await db.Tarefas
            .Where(t => tenantIds.Contains(t.TenantId))
            .GroupBy(t => t.TenantId)
            .Select(g => new
            {
                TenantId = g.Key,
                Count = g.Count(),
                Pendente = g.Count(t => t.Status == StatusTarefa.Pendente),
                EmAndamento = g.Count(t => t.Status == StatusTarefa.EmAndamento),
                Concluida = g.Count(t => t.Status == StatusTarefa.Concluida),
                Perdida = g.Count(t => t.Status == StatusTarefa.Perdida)
            })
            .ToDictionaryAsync(g => g.TenantId, ct);

        // Último acesso (max de UltimoAcessoEm entre os usuários do tenant).
        // Quando o sort é por ultimoAcesso, isso já foi calculado acima (para o conjunto
        // filtrado inteiro, antes do Skip/Take) -- reaproveita em vez de recalcular.
        ultimoAcessoPorTenant ??= await db.Users
            .Where(u => tenantIds.Contains(u.TenantId) && u.UltimoAcessoEm != null)
            .GroupBy(u => u.TenantId)
            .Select(g => new { TenantId = g.Key, UltimoAcesso = g.Max(u => u.UltimoAcessoEm) })
            .ToDictionaryAsync(g => g.TenantId, g => (DateTime?)g.UltimoAcesso, ct);

        // OABs por tenant (total e com erro de sync)
        var oabStats = await db.TenantOabs
            .Where(o => tenantIds.Contains(o.TenantId))
            .GroupBy(o => o.TenantId)
            .Select(g => new
            {
                TenantId = g.Key,
                Total = g.Count(),
                ComErro = g.Count(o => o.SyncError != null)
            })
            .ToDictionaryAsync(g => g.TenantId, ct);

        // Publicações capturadas no mês corrente
        var inicioMes = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var publicacoesMes = await db.Publicacoes
            .Where(p => tenantIds.Contains(p.TenantId) && p.CapturaEm >= inicioMes)
            .GroupBy(p => p.TenantId)
            .Select(g => new { TenantId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.TenantId, g => g.Count, ct);

        // A ordem de exibição já foi decidida por tenantIds acima (nome/cadastro/ultimoAcesso);
        // reordenar aqui de novo por CriadoEm bagunçaria a página quando o sort não for cadastro.
        var tenantsById = await db.Tenants
            .Where(t => tenantIds.Contains(t.Id))
            .ToDictionaryAsync(t => t.Id, ct);
        var tenants = tenantIds.Select(id => tenantsById[id]).ToList();

        var items = tenants.Select(t =>
        {
            var pc = processoCounts.GetValueOrDefault(t.Id);
            var tc = tarefaCounts.GetValueOrDefault(t.Id);
            var os = oabStats.GetValueOrDefault(t.Id);
            return new TenantListItemDto(
                t.Id, t.Nome, t.Cnpj, t.Plano.ToString(), t.Status.ToString(),
                t.CriadoEm,
                userCounts.GetValueOrDefault(t.Id),
                t.TrialExpiraEm, t.PlanoExpiraEm,
                pc?.Count ?? 0,
                pc?.Monitorados ?? 0,
                pc?.MonitoradosDiario ?? 0,
                pc?.MonitoradosSemanal ?? 0,
                docStats.TryGetValue(t.Id, out var ds) ? ds.Count : 0,
                docStats.TryGetValue(t.Id, out var dsb) ? dsb.TotalBytes : 0,
                tc?.Count ?? 0,
                tc?.Pendente ?? 0,
                tc?.EmAndamento ?? 0,
                tc?.Concluida ?? 0,
                tc?.Perdida ?? 0,
                PlanoRestricoes.MaxUsuarios(t.Plano),
                PlanoRestricoes.MaxProcessosMonitorados(t.Plano),
                PlanoRestricoes.ArmazenamentoLimiteMB(t.Plano),
                os?.Total ?? 0,
                os?.ComErro ?? 0,
                publicacoesMes.GetValueOrDefault(t.Id),
                ultimoAcessoPorTenant.GetValueOrDefault(t.Id)
            );
        }).ToList();

        return Ok(new { total, page, pageSize, items });
    }

    [HttpGet("tenants/{id:guid}")]
    public async Task<IActionResult> GetTenant(Guid id, CancellationToken ct)
    {
        var tenant = await db.Tenants
            .Include(t => t.Usuarios)
            .FirstOrDefaultAsync(t => t.Id == id && t.Id != SystemTenantId, ct);

        if (tenant == null) return NotFound();

        var processoStats = await db.Processos
            .Where(p => p.TenantId == id)
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Count = g.Count(),
                Monitorados = g.Count(p => p.Monitorado),
                MonitoradosDiario = g.Count(p => p.Monitorado && !p.MonitoramentoSemanal),
                MonitoradosSemanal = g.Count(p => p.Monitorado && p.MonitoramentoSemanal)
            })
            .FirstOrDefaultAsync(ct);
        var docResult = await db.Documentos
            .Where(d => d.TenantId == id)
            .GroupBy(_ => 1)
            .Select(g => new { Count = g.Count(), TotalBytes = g.Sum(d => d.TamanhoBytes) })
            .FirstOrDefaultAsync(ct);
        var tarefaStats = await db.Tarefas
            .Where(t => t.TenantId == id)
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Count = g.Count(),
                Pendente = g.Count(t => t.Status == StatusTarefa.Pendente),
                EmAndamento = g.Count(t => t.Status == StatusTarefa.EmAndamento),
                Concluida = g.Count(t => t.Status == StatusTarefa.Concluida),
                Perdida = g.Count(t => t.Status == StatusTarefa.Perdida)
            })
            .FirstOrDefaultAsync(ct);

        var ultimoAcesso = await db.Users
            .Where(u => u.TenantId == id && u.UltimoAcessoEm != null)
            .MaxAsync(u => (DateTime?)u.UltimoAcessoEm, ct);

        var oabs = await db.TenantOabs
            .Where(o => o.TenantId == id)
            .OrderByDescending(o => o.CriadoEm)
            .Select(o => new TenantOabResumoDto(
                o.Id, o.Uf, o.Numero, o.Nome, o.Ativo,
                o.EscavadorMonitoramentoId != null,
                o.SyncError, o.UltimoSyncEm))
            .ToListAsync(ct);

        var oabTotal = oabs.Count;
        var oabComErro = oabs.Count(o => o.SyncError != null);

        var inicioMes = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var publicacoesMesCount = await db.Publicacoes
            .CountAsync(p => p.TenantId == id && p.CapturaEm >= inicioMes, ct);

        var dto = new TenantDetailDto(
            tenant.Id, tenant.Nome, tenant.Cnpj, tenant.Endereco,
            tenant.Plano.ToString(), tenant.PeriodoBilling, tenant.Status.ToString(),
            tenant.CriadoEm, tenant.Usuarios.Count, tenant.TrialExpiraEm, tenant.PlanoExpiraEm,
            tenant.AbacatePayBillingId,
            tenant.StripeSubscriptionId,
            processoStats?.Count ?? 0,
            processoStats?.Monitorados ?? 0,
            processoStats?.MonitoradosDiario ?? 0,
            processoStats?.MonitoradosSemanal ?? 0,
            docResult?.Count ?? 0,
            docResult?.TotalBytes ?? 0,
            tarefaStats?.Count ?? 0,
            tarefaStats?.Pendente ?? 0,
            tarefaStats?.EmAndamento ?? 0,
            tarefaStats?.Concluida ?? 0,
            tarefaStats?.Perdida ?? 0,
            PlanoRestricoes.MaxUsuarios(tenant.Plano),
            PlanoRestricoes.MaxProcessosMonitorados(tenant.Plano),
            PlanoRestricoes.ArmazenamentoLimiteMB(tenant.Plano),
            oabTotal,
            oabComErro,
            publicacoesMesCount,
            ultimoAcesso,
            tenant.Usuarios.Select(u => new TenantUserDto(u.Id, u.Nome, u.Email, u.Perfil.ToString(), u.Ativo, u.UltimoAcessoEm)).ToList(),
            oabs
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

    [HttpPut("tenants/{id:guid}/trial-plus")]
    public async Task<IActionResult> ConcederTrialPlus(Guid id, [FromBody] ConcederTrialPlusDto dto, CancellationToken ct)
    {
        if (dto.Dias is not (15 or 30 or 45))
            return BadRequest(new { message = "Dias deve ser 15, 30 ou 45." });

        var tenant = await db.Tenants.FindAsync([id], ct);
        if (tenant == null || tenant.Id == SystemTenantId) return NotFound();

        if (tenant.Plano != PlanoTipo.Free)
            return BadRequest(new { message = "Trial Plus só pode ser concedido a tenants no plano Free." });

        if (tenant.Status is StatusTenant.Cancelado or StatusTenant.Suspenso)
            return BadRequest(new { message = "Tenant não está ativo." });

        var superAdminId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(superAdminId) || !Guid.TryParse(superAdminId, out var adminGuid))
            return Unauthorized(new { message = "Identificação do superadmin ausente." });

        var agora = DateTime.UtcNow;
        var planoAnterior = tenant.Plano;
        var statusAnterior = tenant.Status;
        var motivo = string.IsNullOrWhiteSpace(dto.Motivo) ? null : dto.Motivo.Trim();

        tenant.Plano = PlanoTipo.Plus;
        tenant.Status = StatusTenant.Trial;
        tenant.TrialExpiraEm = agora.AddDays(dto.Dias);
        tenant.TrialConcedidoPorId = adminGuid;
        tenant.TrialConcedidoEm = agora;
        tenant.TrialConcedidoDias = dto.Dias;
        tenant.TrialConcedidoMotivo = motivo;
        tenant.PlanoExpiraEm = null;
        tenant.AbacatePayBillingId = null;
        tenant.StripeSubscriptionId = null;
        tenant.PeriodoBilling = null;
        tenant.BillingCycleStart = null;

        await audit.LogAsync(new AuditLogEntry(
            tenant.Id, adminGuid, "trial-plus-concedido", "Tenant",
            tenant.Id.ToString(),
            new { PlanoAnterior = planoAnterior.ToString(), StatusAnterior = statusAnterior.ToString() },
            new { Dias = dto.Dias, TrialExpiraEm = tenant.TrialExpiraEm, Motivo = motivo },
            HttpContext.Connection.RemoteIpAddress?.ToString()), ct);

        await db.SaveChangesAsync(ct);

        return Ok(new TenantTrialInfoDto(
            true, tenant.TrialConcedidoDias, tenant.TrialConcedidoEm,
            null, tenant.TrialExpiraEm, tenant.TrialConcedidoMotivo));
    }

    [HttpGet("tenants/{id:guid}/trial-info")]
    public async Task<IActionResult> GetTrialInfo(Guid id, CancellationToken ct)
    {
        var tenant = await db.Tenants.FindAsync([id], ct);
        if (tenant == null || tenant.Id == SystemTenantId) return NotFound();

        string? concedidoPorNome = null;
        if (tenant.TrialConcedidoPorId.HasValue)
        {
            concedidoPorNome = await db.Users
                .Where(u => u.Id == tenant.TrialConcedidoPorId.Value)
                .Select(u => u.Nome)
                .FirstOrDefaultAsync(ct);
        }

        return Ok(new TenantTrialInfoDto(
            tenant.TrialConcedidoPorId.HasValue,
            tenant.TrialConcedidoDias,
            tenant.TrialConcedidoEm,
            concedidoPorNome,
            tenant.TrialExpiraEm,
            tenant.TrialConcedidoMotivo));
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

    [HttpPost("users/{userId:guid}/impersonate")]
    public async Task<IActionResult> Impersonate(Guid userId, CancellationToken ct)
    {
        var superAdminId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(superAdminId) || !Guid.TryParse(superAdminId, out var adminGuid))
            return Unauthorized(new { message = "Identificação do superadmin ausente." });

        var adminNome = User.FindFirstValue("nome") ?? "SuperAdmin";

        try
        {
            var result = await authService.ImpersonarAsync(adminGuid, adminNome, userId, ct);

            await audit.LogAsync(new AuditLogEntry(
                result.Usuario.TenantId, adminGuid, AuditActions.ImpersonationStart, AuditEntities.Usuario,
                userId.ToString(), null, new { UsuarioAlvo = result.Usuario.Email }, HttpContext.GetClientIpAddress()), ct);

            return Ok(result);
        }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
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
