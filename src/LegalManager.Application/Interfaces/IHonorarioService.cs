using LegalManager.Application.DTOs.Honorarios;
using LegalManager.Domain.Enums;

namespace LegalManager.Application.Interfaces;

public interface IHonorarioService
{
    Task<DashboardHonorariosDto> GetDashboardAsync(Guid tenantId, CancellationToken ct = default);

    Task<ContratosPagedDto> ListarAsync(Guid tenantId, FiltroContratoHonorario filtro, CancellationToken ct = default);

    Task<ContratoHonorarioDto?> ObterAsync(Guid id, Guid tenantId, CancellationToken ct = default);

    Task<ContratoHonorarioDto> CriarAsync(Guid tenantId, Guid usuarioId, CriarContratoHonorarioDto dto, CancellationToken ct = default);

    Task<ContratoHonorarioDto> AtualizarAsync(Guid id, Guid tenantId, Guid usuarioId, AtualizarContratoHonorarioDto dto, CancellationToken ct = default);

    Task ExcluirAsync(Guid id, Guid tenantId, Guid usuarioId, CancellationToken ct = default);

    Task<ParcelasContratoDto> ListarParcelasAsync(Guid contratoId, Guid tenantId, CancellationToken ct = default);

    Task<ParcelaHonorarioDto> QuitarParcelaAsync(Guid contratoId, Guid parcelaId, Guid tenantId, QuitarParcelaDto dto, CancellationToken ct = default);

    Task<ParcelaHonorarioDto> CancelarParcelaAsync(Guid contratoId, Guid parcelaId, Guid tenantId, Guid usuarioId, string motivo, CancellationToken ct = default);

    Task EstornarPagamentoParcelaAsync(Guid contratoId, Guid parcelaId, Guid tenantId, CancellationToken ct = default);

    Task<IEnumerable<HistoricoContratoDto>> ListarHistoricoAsync(Guid contratoId, Guid tenantId, CancellationToken ct = default);

    Task<ContratoHonorarioDto> SuspenderAsync(Guid id, Guid tenantId, Guid usuarioId, CancellationToken ct = default);

    Task<ContratoHonorarioDto> ReativarAsync(Guid id, Guid tenantId, Guid usuarioId, CancellationToken ct = default);

    Task<ContratoHonorarioDto> DistratoAsync(Guid id, Guid tenantId, Guid usuarioId, string motivo, CancellationToken ct = default);

    Task<ExtratoPdfDadosDto> ObterDadosExtratoAsync(Guid id, Guid tenantId, ExtratoPdfRequestDto? dto, CancellationToken ct = default);
}

public interface IConfiguracaoHonorarioService
{
    Task<ConfiguracaoHonorarioDto> ObterOuCriarPadraoAsync(Guid tenantId, CancellationToken ct = default);
    Task<ConfiguracaoHonorarioDto> SalvarAsync(Guid tenantId, ConfiguracaoHonorarioDto dto, CancellationToken ct = default);
}
