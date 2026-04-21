using LegalManager.Application.DTOs.Notificacoes;

namespace LegalManager.Application.Interfaces;

public interface IPreferenciasNotificacaoService
{
    Task<PreferenciasNotificacaoDto> GetAsync(Guid tenantId, Guid usuarioId, CancellationToken ct = default);
    Task<PreferenciasNotificacaoDto> AtualizarAsync(Guid tenantId, Guid usuarioId, AtualizarPreferenciasDto dto, CancellationToken ct = default);
    Task<bool> PermiteInAppAsync(Guid tenantId, Guid usuarioId, string tipo, CancellationToken ct = default);
    Task<bool> PermiteEmailAsync(Guid tenantId, Guid usuarioId, string tipo, CancellationToken ct = default);
}
