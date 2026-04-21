using LegalManager.Application.DTOs.Timesheet;
using LegalManager.Application.Interfaces;
using LegalManager.Domain.Entities;
using LegalManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LegalManager.Infrastructure.Services;

public class TimesheetService(AppDbContext db) : ITimesheetService
{
    public async Task<RegistroTempoPagedDto> GetAllAsync(Guid tenantId, Guid? usuarioId, Guid? processoId,
        DateTime? de, DateTime? ate, int page, int pageSize, CancellationToken ct = default)
    {
        var q = db.RegistrosTempo
            .AsNoTracking()
            .Where(r => r.TenantId == tenantId);

        if (usuarioId.HasValue) q = q.Where(r => r.UsuarioId == usuarioId.Value);
        if (processoId.HasValue) q = q.Where(r => r.ProcessoId == processoId.Value);
        if (de.HasValue) q = q.Where(r => r.Inicio >= de.Value);
        if (ate.HasValue) q = q.Where(r => r.Inicio <= ate.Value);

        var total = await q.CountAsync(ct);
        var items = await q
            .OrderByDescending(r => r.Inicio)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(r => ToDto(r))
            .ToListAsync(ct);

        return new RegistroTempoPagedDto(items, total);
    }

    public async Task<RegistroTempoDto?> GetByIdAsync(Guid id, Guid tenantId, CancellationToken ct = default)
    {
        var r = await db.RegistrosTempo.AsNoTracking()
            .Include(x => x.Usuario)
            .Include(x => x.Processo)
            .Include(x => x.Tarefa)
            .FirstOrDefaultAsync(x => x.Id == id && x.TenantId == tenantId, ct);
        return r == null ? null : MapDto(r);
    }

    public async Task<RegistroTempoDto?> GetCronometroAtivoAsync(Guid tenantId, Guid usuarioId, CancellationToken ct = default)
    {
        var r = await db.RegistrosTempo.AsNoTracking()
            .Include(x => x.Usuario)
            .Include(x => x.Processo)
            .Include(x => x.Tarefa)
            .FirstOrDefaultAsync(x => x.TenantId == tenantId && x.UsuarioId == usuarioId && x.EmAndamento, ct);
        return r == null ? null : MapDto(r);
    }

    public async Task<RegistroTempoDto> IniciarCronometroAsync(Guid tenantId, Guid usuarioId, IniciarRegistroDto dto, CancellationToken ct = default)
    {
        var ativo = await db.RegistrosTempo
            .FirstOrDefaultAsync(r => r.TenantId == tenantId && r.UsuarioId == usuarioId && r.EmAndamento, ct);

        if (ativo != null)
            throw new InvalidOperationException("Já existe um cronômetro em andamento. Pare o atual antes de iniciar outro.");

        var registro = new RegistroTempo
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            UsuarioId = usuarioId,
            Inicio = DateTime.UtcNow,
            Descricao = dto.Descricao,
            ProcessoId = dto.ProcessoId,
            TarefaId = dto.TarefaId,
            EmAndamento = true
        };

        db.RegistrosTempo.Add(registro);
        await db.SaveChangesAsync(ct);
        return (await GetByIdAsync(registro.Id, tenantId, ct))!;
    }

    public async Task<RegistroTempoDto> PararCronometroAsync(Guid tenantId, Guid usuarioId, PararRegistroDto dto, CancellationToken ct = default)
    {
        var registro = await db.RegistrosTempo
            .FirstOrDefaultAsync(r => r.TenantId == tenantId && r.UsuarioId == usuarioId && r.EmAndamento, ct)
            ?? throw new InvalidOperationException("Nenhum cronômetro em andamento encontrado.");

        registro.Fim = DateTime.UtcNow;
        registro.EmAndamento = false;
        registro.DuracaoMinutos = (int)(registro.Fim.Value - registro.Inicio).TotalMinutes;
        if (dto.Descricao != null) registro.Descricao = dto.Descricao;

        await db.SaveChangesAsync(ct);
        return (await GetByIdAsync(registro.Id, tenantId, ct))!;
    }

    public async Task<RegistroTempoDto> CriarManualAsync(Guid tenantId, Guid usuarioId, CriarRegistroManualDto dto, CancellationToken ct = default)
    {
        if (dto.Fim <= dto.Inicio)
            throw new ArgumentException("A data de fim deve ser posterior ao início.");

        var registro = new RegistroTempo
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            UsuarioId = usuarioId,
            Inicio = dto.Inicio,
            Fim = dto.Fim,
            DuracaoMinutos = (int)(dto.Fim - dto.Inicio).TotalMinutes,
            Descricao = dto.Descricao,
            ProcessoId = dto.ProcessoId,
            TarefaId = dto.TarefaId,
            EmAndamento = false
        };

        db.RegistrosTempo.Add(registro);
        await db.SaveChangesAsync(ct);
        return (await GetByIdAsync(registro.Id, tenantId, ct))!;
    }

    public async Task<RegistroTempoDto> AtualizarAsync(Guid id, Guid tenantId, AtualizarRegistroDto dto, CancellationToken ct = default)
    {
        var registro = await db.RegistrosTempo
            .FirstOrDefaultAsync(r => r.Id == id && r.TenantId == tenantId, ct)
            ?? throw new KeyNotFoundException("Registro não encontrado.");

        registro.Descricao = dto.Descricao;
        registro.ProcessoId = dto.ProcessoId;
        registro.TarefaId = dto.TarefaId;

        await db.SaveChangesAsync(ct);
        return (await GetByIdAsync(id, tenantId, ct))!;
    }

    public async Task DeletarAsync(Guid id, Guid tenantId, CancellationToken ct = default)
    {
        var registro = await db.RegistrosTempo
            .FirstOrDefaultAsync(r => r.Id == id && r.TenantId == tenantId, ct)
            ?? throw new KeyNotFoundException("Registro não encontrado.");

        db.RegistrosTempo.Remove(registro);
        await db.SaveChangesAsync(ct);
    }

    private static RegistroTempoDto ToDto(RegistroTempo r) =>
        new(r.Id, r.Inicio, r.Fim, r.DuracaoMinutos, r.Descricao, r.EmAndamento,
            r.ProcessoId, null, r.TarefaId, null, r.UsuarioId, string.Empty, r.CriadoEm);

    private static RegistroTempoDto MapDto(RegistroTempo r) =>
        new(r.Id, r.Inicio, r.Fim, r.DuracaoMinutos, r.Descricao, r.EmAndamento,
            r.ProcessoId, r.Processo?.NumeroCNJ, r.TarefaId, r.Tarefa?.Titulo,
            r.UsuarioId, r.Usuario?.Nome ?? string.Empty, r.CriadoEm);
}
