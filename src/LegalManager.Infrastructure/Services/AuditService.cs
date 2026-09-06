using System.Text.Json;
using LegalManager.Application.Interfaces;
using LegalManager.Domain.Entities;
using LegalManager.Domain.Interfaces;
using LegalManager.Infrastructure.Observability;
using LegalManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace LegalManager.Infrastructure.Services;

public class AuditService : IAuditService
{
    private readonly AppDbContext _context;
    private readonly ITenantContext _tenantContext;
    private readonly ILogger<AuditService> _logger;
    private static readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = false };

    public AuditService(AppDbContext context, ITenantContext tenantContext, ILogger<AuditService> logger)
    {
        _context = context;
        _tenantContext = tenantContext;
        _logger = logger;
    }

    public async Task LogAsync(AuditLogEntry entry, CancellationToken ct = default)
    {
        var log = new AuditLog
        {
            Id = Guid.NewGuid(),
            TenantId = entry.TenantId,
            UsuarioId = entry.UsuarioId,
            Acao = entry.Acao,
            Entidade = entry.Entidade,
            EntidadeId = entry.EntidadeId,
            DadosAnteriores = entry.DadosAnteriores != null ? JsonSerializer.Serialize(entry.DadosAnteriores, _jsonOptions) : null,
            DadosNovos = entry.DadosNovos != null ? JsonSerializer.Serialize(entry.DadosNovos, _jsonOptions) : null,
            IpAddress = entry.IpAddress,
            CriadoEm = DateTime.UtcNow,
            ImpersonadoPorId = _tenantContext.ImpersonadoPorId
        };

        _context.AuditLogs.Add(log);
        await _context.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Audit {Acao} {Entidade}#{EntidadeId} por Usuario={UsuarioId} Tenant={TenantId} Ip={Ip}",
            entry.Acao, entry.Entidade, entry.EntidadeId ?? "-",
            entry.UsuarioId, entry.TenantId, entry.IpAddress ?? "-");
    }

    public async Task<IEnumerable<AuditLogResponseDto>> GetByEntityAsync(string entity, Guid entityId, CancellationToken ct = default)
    {
        var tenantId = _tenantContext.TenantId;

        var logs = await _context.AuditLogs
            .Where(a => a.TenantId == tenantId && a.Entidade == entity && a.EntidadeId == entityId.ToString())
            .OrderByDescending(a => a.CriadoEm)
            .ToListAsync(ct);

        var userIds = logs.Where(l => l.UsuarioId != null).Select(l => l.UsuarioId!.Value).Distinct().ToList();
        var userNames = await _context.Users
            .Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.Nome, ct);

        return logs.Select(l => new AuditLogResponseDto(
            l.Id,
            l.UsuarioId,
            l.UsuarioId.HasValue && userNames.TryGetValue(l.UsuarioId!.Value, out var nome) ? nome : null,
            l.Acao,
            l.Entidade,
            l.EntidadeId,
            l.DadosAnteriores,
            l.DadosNovos,
            l.IpAddress,
            l.CriadoEm
        ));
    }

    public async Task<IEnumerable<AuditLogResponseDto>> GetByTenantAsync(DateTime? from, DateTime? to, int page = 1, int pageSize = 50, CancellationToken ct = default)
    {
        var tenantId = _tenantContext.TenantId;

        var query = _context.AuditLogs
            .Where(a => a.TenantId == tenantId);

        if (from.HasValue)
            query = query.Where(a => a.CriadoEm >= from.Value);

        if (to.HasValue)
            query = query.Where(a => a.CriadoEm <= to.Value);

        var logs = await query
            .OrderByDescending(a => a.CriadoEm)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        var userIds = logs.Where(l => l.UsuarioId != null).Select(l => l.UsuarioId!.Value).Distinct().ToList();
        var userNames = userIds.Any()
            ? await _context.Users.Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.Nome, ct)
            : new Dictionary<Guid, string>();

        return logs.Select(l => new AuditLogResponseDto(
            l.Id,
            l.UsuarioId,
            l.UsuarioId.HasValue && userNames.TryGetValue(l.UsuarioId!.Value, out var nome) ? nome : null,
            l.Acao,
            l.Entidade,
            l.EntidadeId,
            l.DadosAnteriores,
            l.DadosNovos,
            l.IpAddress,
            l.CriadoEm
        ));
    }
}