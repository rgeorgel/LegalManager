using LegalManager.Application.DTOs.Monitoramento;

namespace LegalManager.Application.Interfaces;

public interface IMonitoramentoService
{
    Task<MonitoramentoResultDto> MonitorarProcessoAsync(Guid processoId, CancellationToken ct = default);
    Task<int> MonitorarTodosAsync(CancellationToken ct = default);
    Task<bool> AlternarMonitoramentoAsync(Guid processoId, CancellationToken ct = default);
}
