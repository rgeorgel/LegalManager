using LegalManager.Application.DTOs.Financeiro;
using LegalManager.Domain.Enums;

namespace LegalManager.Application.Interfaces;

public interface IFinanceiroService
{
    Task<LancamentosPagedDto> GetAllAsync(Guid tenantId, TipoLancamento? tipo, StatusLancamento? status,
        Guid? processoId, Guid? contatoId, int page, int pageSize, int? mes = null, int? ano = null, CancellationToken ct = default);
    Task<LancamentoDto?> GetByIdAsync(Guid id, Guid tenantId, CancellationToken ct = default);
    Task<LancamentoDto> CriarAsync(Guid tenantId, CriarLancamentoDto dto, CancellationToken ct = default);
    Task<LancamentoDto> AtualizarAsync(Guid id, Guid tenantId, AtualizarLancamentoDto dto, CancellationToken ct = default);
    Task PagarAsync(Guid id, Guid tenantId, DateTime? dataPagamento = null, CancellationToken ct = default);
    Task CancelarAsync(Guid id, Guid tenantId, CancellationToken ct = default);
    Task<ResumoFinanceiroCompletoDto> GetResumoCompletoAsync(Guid tenantId, int ano, int mes, CancellationToken ct = default);
}
