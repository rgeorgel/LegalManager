using LegalManager.Application.DTOs.Notificacoes;

namespace LegalManager.Application.Interfaces;

public interface INotificacaoService
{
    Task<IEnumerable<NotificacaoDto>> GetUnreadAsync(CancellationToken ct = default);
    Task<int> GetUnreadCountAsync(CancellationToken ct = default);
    Task MarcarLidaAsync(Guid id, CancellationToken ct = default);
    Task MarcarTodasLidasAsync(CancellationToken ct = default);
    Task CriarAsync(Guid tenantId, Guid usuarioId, Domain.Enums.TipoNotificacao tipo, string titulo, string mensagem, string? url = null, CancellationToken ct = default);
    Task<(IEnumerable<NotificacaoDto> Items, int Total)> GetHistoricoAsync(int page, int pageSize, CancellationToken ct = default);
}
