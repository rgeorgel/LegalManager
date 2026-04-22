namespace LegalManager.Application.Interfaces;

public interface IAbacatePayService
{
    Task<AbacatePayBillingResult> CriarBillingAsync(CriarBillingInput input, CancellationToken ct = default);
    Task CancelarBillingAsync(string billingId, CancellationToken ct = default);
}

public record CriarBillingInput(
    string TenantId,
    string NomeEscritorio,
    string Email,
    string NomeAdmin,
    string? Cnpj,
    string Periodo,
    string ReturnUrl,
    string CompletionUrl
);

public record AbacatePayBillingResult(string BillingId, string CheckoutUrl);
