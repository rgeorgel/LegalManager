using LegalManager.Application.Interfaces;
using Microsoft.Extensions.Logging;
using Stripe;
using Stripe.Checkout;

namespace LegalManager.Infrastructure.Services;

public class StripeService : IStripeService
{
    private readonly StripeClient _stripeClient;
    private readonly ILogger<StripeService> _logger;

    private static readonly Dictionary<string, int> _valorCentavosPorPlano = new()
    {
        ["Plus"] = 2_000,
        ["Pro"] = 5_000,
        ["Max"] = 12_500
    };

    public StripeService(StripeClient stripeClient, ILogger<StripeService> logger)
    {
        _stripeClient = stripeClient;
        _logger = logger;
    }

    public async Task<StripeCheckoutResult> CriarCheckoutAssinaturaAsync(CriarCheckoutAssinaturaInput input, CancellationToken ct = default)
    {
        var customerId = await ObterOuCriarClienteAsync(
            input.StripeCustomerId, input.NomeAdmin, input.Email, input.Cnpj, input.TenantId, ct);

        var priceId = await ObterOuCriarPrecoAsync(input.Plano, input.Periodo, ct);

        var metadata = new Dictionary<string, string>
        {
            ["tenantId"] = input.TenantId,
            ["plano"] = input.Plano,
            ["periodo"] = input.Periodo
        };

        var sessionService = new SessionService(_stripeClient);
        var session = await sessionService.CreateAsync(new SessionCreateOptions
        {
            Mode = "subscription",
            Customer = customerId,
            PaymentMethodTypes = new List<string> { "card" },
            LineItems = new List<SessionLineItemOptions>
            {
                new() { Price = priceId, Quantity = 1 }
            },
            SuccessUrl = input.CompletionUrl,
            CancelUrl = input.ReturnUrl,
            Metadata = metadata,
            SubscriptionData = new SessionSubscriptionDataOptions { Metadata = metadata }
        }, cancellationToken: ct);

        if (string.IsNullOrEmpty(session.Url))
            throw new InvalidOperationException("Stripe não retornou a URL de checkout.");

        _logger.LogInformation("Checkout Stripe (assinatura) criado: {SessionId} para tenant {TenantId}, plano {Plano}",
            session.Id, input.TenantId, input.Plano);

        return new StripeCheckoutResult(session.Id, session.Url, customerId);
    }

    public async Task<StripeAtualizarAssinaturaResult> AtualizarAssinaturaAsync(AtualizarAssinaturaInput input, CancellationToken ct = default)
    {
        var subscriptionService = new SubscriptionService(_stripeClient);
        var subscription = await subscriptionService.GetAsync(input.SubscriptionId, cancellationToken: ct);
        var itemId = subscription.Items.Data[0].Id;

        var novoPriceId = await ObterOuCriarPrecoAsync(input.PlanoAlvo, input.Periodo, ct);

        var atualizada = await subscriptionService.UpdateAsync(input.SubscriptionId, new SubscriptionUpdateOptions
        {
            Items = new List<SubscriptionItemOptions>
            {
                new() { Id = itemId, Price = novoPriceId }
            },
            // "create_prorations" só fatura no próximo ciclo — para cobrar a diferença
            // agora (no cartão já salvo) é preciso "always_invoice".
            // https://docs.stripe.com/api/subscriptions/update#update_subscription-proration_behavior
            ProrationBehavior = "always_invoice",
            Metadata = new Dictionary<string, string>
            {
                ["plano"] = input.PlanoAlvo,
                ["periodo"] = input.Periodo
            }
        }, cancellationToken: ct);

        var valorCobrado = 0m;
        if (!string.IsNullOrEmpty(atualizada.LatestInvoiceId))
        {
            var invoiceService = new InvoiceService(_stripeClient);
            var invoice = await invoiceService.GetAsync(atualizada.LatestInvoiceId, cancellationToken: ct);
            valorCobrado = invoice.AmountPaid / 100m;
        }

        _logger.LogInformation("Assinatura Stripe {SubscriptionId} atualizada para plano {Plano}, status {Status}, cobrado R$ {Valor}",
            atualizada.Id, input.PlanoAlvo, atualizada.Status, valorCobrado);

        return new StripeAtualizarAssinaturaResult(atualizada.Id, atualizada.Status, valorCobrado);
    }

    public async Task<DateTime?> CancelarAssinaturaAsync(string subscriptionId, CancellationToken ct = default)
    {
        var subscriptionService = new SubscriptionService(_stripeClient);
        try
        {
            var subscription = await subscriptionService.UpdateAsync(subscriptionId, new SubscriptionUpdateOptions
            {
                CancelAtPeriodEnd = true
            }, cancellationToken: ct);

            var periodoFim = subscription.Items.Data.FirstOrDefault()?.CurrentPeriodEnd;

            _logger.LogInformation("Assinatura Stripe {SubscriptionId} agendada para cancelar em {Data}",
                subscriptionId, periodoFim);

            return periodoFim;
        }
        catch (StripeException ex)
        {
            _logger.LogWarning(ex, "Erro ao cancelar assinatura Stripe {SubscriptionId}", subscriptionId);
            return null;
        }
    }

    public async Task<StripeCheckoutResult> CriarCheckoutAvulsoAsync(CriarCheckoutAvulsoInput input, CancellationToken ct = default)
    {
        var customerId = await ObterOuCriarClienteAsync(
            input.StripeCustomerId, input.NomeAdmin, input.Email, input.Cnpj, input.TenantId, ct);

        var metadata = new Dictionary<string, string>
        {
            ["tenantId"] = input.TenantId,
            ["tipo"] = input.Tipo,
            ["referenciaId"] = input.ReferenciaId
        };

        var sessionService = new SessionService(_stripeClient);
        var session = await sessionService.CreateAsync(new SessionCreateOptions
        {
            Mode = "payment",
            Customer = customerId,
            PaymentMethodTypes = new List<string> { "card", "pix" },
            LineItems = new List<SessionLineItemOptions>
            {
                new()
                {
                    Quantity = 1,
                    PriceData = new SessionLineItemPriceDataOptions
                    {
                        Currency = "brl",
                        UnitAmount = input.ValorCentavos,
                        ProductData = new SessionLineItemPriceDataProductDataOptions
                        {
                            Name = input.Nome,
                            Description = input.Descricao
                        }
                    }
                }
            },
            SuccessUrl = input.CompletionUrl,
            CancelUrl = input.ReturnUrl,
            Metadata = metadata,
            PaymentIntentData = new SessionPaymentIntentDataOptions { Metadata = metadata }
        }, cancellationToken: ct);

        if (string.IsNullOrEmpty(session.Url))
            throw new InvalidOperationException("Stripe não retornou a URL de checkout.");

        _logger.LogInformation("Checkout Stripe (avulso) criado: {SessionId} para tenant {TenantId}, tipo {Tipo}",
            session.Id, input.TenantId, input.Tipo);

        return new StripeCheckoutResult(session.Id, session.Url, customerId);
    }

    private async Task<string> ObterOuCriarClienteAsync(
        string? existingCustomerId, string nome, string email, string? cnpj, string tenantId, CancellationToken ct)
    {
        if (!string.IsNullOrEmpty(existingCustomerId))
            return existingCustomerId;

        var customerService = new CustomerService(_stripeClient);
        var customer = await customerService.CreateAsync(new CustomerCreateOptions
        {
            Name = nome,
            Email = email,
            Metadata = new Dictionary<string, string>
            {
                ["tenantId"] = tenantId,
                ["cnpj"] = cnpj ?? ""
            }
        }, cancellationToken: ct);

        _logger.LogInformation("Cliente Stripe criado: {CustomerId} para tenant {TenantId}", customer.Id, tenantId);
        return customer.Id;
    }

    private async Task<string> ObterOuCriarPrecoAsync(string plano, string periodo, CancellationToken ct)
    {
        var lookupKey = $"causify-{plano.ToLowerInvariant()}-{periodo.ToLowerInvariant()}";

        var priceService = new PriceService(_stripeClient);
        var existentes = await priceService.ListAsync(new PriceListOptions
        {
            LookupKeys = new List<string> { lookupKey },
            Active = true
        }, cancellationToken: ct);

        if (existentes.Data.Count > 0)
            return existentes.Data[0].Id;

        var valorCentavos = _valorCentavosPorPlano.GetValueOrDefault(plano, _valorCentavosPorPlano["Pro"]);
        var interval = periodo == "Anual" ? "year" : "month";

        var productService = new ProductService(_stripeClient);
        var produto = await productService.CreateAsync(new ProductCreateOptions
        {
            Name = $"Causify {plano}",
            Description = $"Assinatura {periodo.ToLowerInvariant()} do plano {plano}"
        }, cancellationToken: ct);

        var preco = await priceService.CreateAsync(new PriceCreateOptions
        {
            Product = produto.Id,
            Currency = "brl",
            UnitAmount = valorCentavos,
            LookupKey = lookupKey,
            Recurring = new PriceRecurringOptions { Interval = interval }
        }, cancellationToken: ct);

        _logger.LogInformation("Preço Stripe criado: {PriceId} ({LookupKey})", preco.Id, lookupKey);
        return preco.Id;
    }

    /// <summary>
    /// Valida a assinatura HMAC do webhook e retorna o evento Stripe já desserializado.
    /// Lança StripeException se a assinatura for inválida.
    /// </summary>
    public static Event ConstruirEventoWebhook(string payload, string assinaturaHeader, string webhookSecret)
        => EventUtility.ConstructEvent(payload, assinaturaHeader, webhookSecret, throwOnApiVersionMismatch: false);
}
