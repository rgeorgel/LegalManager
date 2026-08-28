namespace LegalManager.Application.Interfaces;

public interface IStripeService
{
    /// <summary>
    /// Cria uma Checkout Session em modo assinatura (cartão) para um tenant que ainda não
    /// possui assinatura Stripe ativa. Retorna a URL de checkout hospedado pela Stripe.
    /// </summary>
    Task<StripeCheckoutResult> CriarCheckoutAssinaturaAsync(CriarCheckoutAssinaturaInput input, CancellationToken ct = default);

    /// <summary>
    /// Troca o plano de uma assinatura Stripe já ativa (upgrade/downgrade), com proration
    /// calculada e cobrada automaticamente pela Stripe no cartão salvo — sem novo checkout.
    /// </summary>
    Task<StripeAtualizarAssinaturaResult> AtualizarAssinaturaAsync(AtualizarAssinaturaInput input, CancellationToken ct = default);

    /// <summary>
    /// Agenda o cancelamento da assinatura para o fim do ciclo vigente (cancel_at_period_end).
    /// Retorna a data em que o acesso realmente expira.
    /// </summary>
    Task<DateTime?> CancelarAssinaturaAsync(string subscriptionId, CancellationToken ct = default);

    /// <summary>
    /// Cria uma Checkout Session em modo pagamento único (cartão + Pix) — usada para compra
    /// avulsa de créditos de IA.
    /// </summary>
    Task<StripeCheckoutResult> CriarCheckoutAvulsoAsync(CriarCheckoutAvulsoInput input, CancellationToken ct = default);
}

public record CriarCheckoutAssinaturaInput(
    string TenantId,
    string? StripeCustomerId,
    string NomeEscritorio,
    string Email,
    string NomeAdmin,
    string? Cnpj,
    string Plano,
    string Periodo,
    string ReturnUrl,
    string CompletionUrl
);

public record AtualizarAssinaturaInput(
    string SubscriptionId,
    string PlanoAlvo,
    string Periodo
);

public record StripeAtualizarAssinaturaResult(
    string SubscriptionId,
    string Status,
    decimal ValorCobradoImediato
);

public record CriarCheckoutAvulsoInput(
    string TenantId,
    string? StripeCustomerId,
    string Email,
    string NomeAdmin,
    string? Cnpj,
    string ReferenciaId,
    string Nome,
    string Descricao,
    int ValorCentavos,
    string Tipo,
    string ReturnUrl,
    string CompletionUrl
);

public record StripeCheckoutResult(
    string SessionId,
    string CheckoutUrl,
    string CustomerId
);
