namespace LegalManager.Application.Interfaces;

public interface IAbacatePayService
{
    Task<AbacatePayBillingResult> CriarBillingAsync(CriarBillingInput input, CancellationToken ct = default);
    Task CancelarBillingAsync(string billingId, CancellationToken ct = default);
    Task<AbacatePayBillingResult> CriarCheckoutUnicoAsync(CriarCheckoutUnicoInput input, CancellationToken ct = default);
    Task<AbacatePayBillingResult> CriarCheckoutProradoAsync(CriarCheckoutProradoInput input, CancellationToken ct = default);
}

public record CriarBillingInput(
    string TenantId,
    string NomeEscritorio,
    string Email,
    string NomeAdmin,
    string? Cnpj,
    string Periodo,
    string ReturnUrl,
    string CompletionUrl,
    string Plano = "Pro"
);

public record CriarCheckoutUnicoInput(
    string TenantId,
    string Email,
    string NomeAdmin,
    string? Cnpj,
    string PacoteId,
    string PacoteNome,
    int ValorCentavos,
    string ReturnUrl,
    string CompletionUrl
);

public record CriarCheckoutProradoInput(
    string TenantId,
    string NomeEscritorio,
    string Email,
    string NomeAdmin,
    string? Cnpj,
    string PlanoAlvo,
    int ValorProradoCentavos,
    string Periodo,
    string ReturnUrl,
    string CompletionUrl
);

public record AbacatePayBillingResult(string BillingId, string CheckoutUrl);
