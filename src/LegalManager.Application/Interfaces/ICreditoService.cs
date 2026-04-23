using LegalManager.Application.DTOs.IA;
using LegalManager.Domain.Enums;

namespace LegalManager.Application.Interfaces;

public interface ICreditoService
{
    Task<CreditosTotaisDto> ObterCreditosAsync(CancellationToken ct = default);
    Task<bool> ConsumirCreditoAsync(TipoCreditoAI tipo, int quantidade = 1, CancellationToken ct = default);
    Task<bool> TemCreditoDisponivelAsync(TipoCreditoAI tipo, int quantidade = 1, CancellationToken ct = default);
    Task InicializarCreditosPadraoAsync(Guid tenantId, PlanoTipo plano, CancellationToken ct = default);
}