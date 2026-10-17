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

    /// <summary>
    /// Prévia da próxima fatura da assinatura (valor já com descontos/cupons e prorações).
    /// Null quando não há próxima cobrança (ex.: cancelamento agendado para o fim do ciclo).
    /// </summary>
    Task<StripeProximaCobranca?> ObterProximaCobrancaAsync(string subscriptionId, CancellationToken ct = default);
}

public record StripeProximaCobranca(decimal Valor, DateTime? Data);

public record CriarCheckoutAssinaturaInput(
    string TenantId,
    string? StripeCustomerId,
    string NomeEscritorio,
    string Email,
    string NomeAdmin,
    string? Cnpj,
    string Plano,
    string Periodo,
    decimal ValorMensal,
    string ReturnUrl,
    string CompletionUrl,
    DescontoAssinaturaInput? Desconto = null
);

/// <summary>Desconto de código promocional: Percentual% por Meses ciclos (null = para sempre).</summary>
public record DescontoAssinaturaInput(string CodigoPromocional, int Percentual, int? Meses);

public record AtualizarAssinaturaInput(
    string SubscriptionId,
    string PlanoAlvo,
    string Periodo,
    decimal ValorMensal
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
