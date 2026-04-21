using LegalManager.Application.DTOs.Indicadores;

namespace LegalManager.Application.Interfaces;

public interface IIndicadoresService
{
    Task<IndicadoresDto> GetIndicadoresAsync(Guid tenantId, CancellationToken ct = default);
}
