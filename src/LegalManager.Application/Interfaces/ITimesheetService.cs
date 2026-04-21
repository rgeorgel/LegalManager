using LegalManager.Application.DTOs.Timesheet;

namespace LegalManager.Application.Interfaces;

public interface ITimesheetService
{
    Task<RegistroTempoPagedDto> GetAllAsync(Guid tenantId, Guid? usuarioId, Guid? processoId,
        DateTime? de, DateTime? ate, int page, int pageSize, CancellationToken ct = default);
    Task<RegistroTempoDto?> GetByIdAsync(Guid id, Guid tenantId, CancellationToken ct = default);
    Task<RegistroTempoDto?> GetCronometroAtivoAsync(Guid tenantId, Guid usuarioId, CancellationToken ct = default);
    Task<RegistroTempoDto> IniciarCronometroAsync(Guid tenantId, Guid usuarioId, IniciarRegistroDto dto, CancellationToken ct = default);
    Task<RegistroTempoDto> PararCronometroAsync(Guid tenantId, Guid usuarioId, PararRegistroDto dto, CancellationToken ct = default);
    Task<RegistroTempoDto> CriarManualAsync(Guid tenantId, Guid usuarioId, CriarRegistroManualDto dto, CancellationToken ct = default);
    Task<RegistroTempoDto> AtualizarAsync(Guid id, Guid tenantId, AtualizarRegistroDto dto, CancellationToken ct = default);
    Task DeletarAsync(Guid id, Guid tenantId, CancellationToken ct = default);
}
