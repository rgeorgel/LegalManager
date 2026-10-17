using System.Diagnostics;
using System.Globalization;
using LegalManager.Application.Interfaces;
using LegalManager.Infrastructure.Observability;
using Microsoft.Extensions.Logging;
using Stripe;
using Stripe.Checkout;

namespace LegalManager.Infrastructure.Services;

public class StripeService : IStripeService
{
    private readonly StripeClient _stripeClient;
    private readonly ILogger<StripeService> _logger;

    public StripeService(StripeClient stripeClient, ILogger<StripeService> logger)
    {
        _stripeClient = stripeClient;
        _logger = logger;
    }

    public async Task<StripeCheckoutResult> CriarCheckoutAssinaturaAsync(CriarCheckoutAssinaturaInput input, CancellationToken ct = default)
    {
        using var activity = Telemetry.Stripe.StartActivity(nameof(CriarCheckoutAssinaturaAsync));
        activity?.SetTag("stripe.operation", "checkout.subscription.create");
        activity?.SetTag("stripe.tenant_id", input.TenantId);
        var customerId = await ObterOuCriarClienteAsync(
            input.StripeCustomerId, input.NomeAdmin, input.Email, input.Cnpj, input.TenantId, ct);

        var priceId = await ObterOuCriarPrecoAsync(input.Plano, input.Periodo, input.ValorMensal, ct);

        var metadata = new Dictionary<string, string>
        {
            ["tenantId"] = input.TenantId,
            ["plano"] = input.Plano,
            ["periodo"] = input.Periodo,
            ["valorMensal"] = input.ValorMensal.ToString(CultureInfo.InvariantCulture)
        };

        List<SessionDiscountOptions>? discounts = null;
        if (input.Desconto is { } desconto)
        {
            discounts = [new() { Coupon = await ObterOuCriarCupomAsync(desconto, ct) }];
            metadata["codigoPromocional"] = desconto.CodigoPromocional;
        }

        var sessionService = new SessionService(_stripeClient);
        var session = await sessionService.CreateAsync(new SessionCreateOptions
        {
            Mode = "subscription",
            Discounts = discounts,
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
        using var activity = Telemetry.Stripe.StartActivity(nameof(AtualizarAssinaturaAsync));
        activity?.SetTag("stripe.operation", "subscription.update");
        activity?.SetTag("stripe.subscription_id", input.SubscriptionId);
        var subscriptionService = new SubscriptionService(_stripeClient);
        var subscription = await subscriptionService.GetAsync(input.SubscriptionId, cancellationToken: ct);
        var itemId = subscription.Items.Data[0].Id;

        var novoPriceId = await ObterOuCriarPrecoAsync(input.PlanoAlvo, input.Periodo, input.ValorMensal, ct);

        var atualizada = await subscriptionService.UpdateAsync(input.SubscriptionId, new SubscriptionUpdateOptions
        {
            Items = new List<SubscriptionItemOptions>
            {
                new() { Id = itemId, Price = novoPriceId }
            },
            // Cobra a diferença na hora, numa fatura própria (create_prorations a deixaria para a
            // renovação); cartão recusado lança StripeException e a assinatura não muda. Num
            // downgrade a diferença vira crédito no saldo do cliente, abatido nas próximas faturas.
            ProrationBehavior = "always_invoice",
            PaymentBehavior = "error_if_incomplete",
            Metadata = new Dictionary<string, string>
            {
                ["plano"] = input.PlanoAlvo,
                ["periodo"] = input.Periodo,
                ["valorMensal"] = input.ValorMensal.ToString(CultureInfo.InvariantCulture)
            }
        }, cancellationToken: ct);

        var valorCobrado = 0m;
        if (!string.IsNullOrEmpty(atualizada.LatestInvoiceId))
        {
            var invoiceService = new InvoiceService(_stripeClient);
            var invoice = await invoiceService.GetAsync(atualizada.LatestInvoiceId, cancellationToken: ct);
            // Sem diferença a cobrar a Stripe não gera fatura nova, e a última ainda é a do ciclo.
            if (invoice.BillingReason == "subscription_update")
                valorCobrado = invoice.AmountPaid / 100m;
        }

        _logger.LogInformation("Assinatura Stripe {SubscriptionId} atualizada para plano {Plano}, status {Status}, cobrado R$ {Valor}",
            atualizada.Id, input.PlanoAlvo, atualizada.Status, valorCobrado);

        return new StripeAtualizarAssinaturaResult(atualizada.Id, atualizada.Status, valorCobrado);
    }

    public async Task<DateTime?> CancelarAssinaturaAsync(string subscriptionId, CancellationToken ct = default)
    {
        using var activity = Telemetry.Stripe.StartActivity(nameof(CancelarAssinaturaAsync));
        activity?.SetTag("stripe.operation", "subscription.cancel");
        activity?.SetTag("stripe.subscription_id", subscriptionId);
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
        using var activity = Telemetry.Stripe.StartActivity(nameof(CriarCheckoutAvulsoAsync));
        activity?.SetTag("stripe.operation", "checkout.one_off.create");
        activity?.SetTag("stripe.tenant_id", input.TenantId);
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

    /// <summary>
    /// Um cupom Stripe por combinação código/percentual/duração — o id muda se o super admin
    /// editar o desconto do código, e cupons de assinaturas já criadas não são afetados.
    /// </summary>
    private async Task<string> ObterOuCriarCupomAsync(DescontoAssinaturaInput desconto, CancellationToken ct)
    {
        var cupomId = $"causify-{desconto.CodigoPromocional}-{desconto.Percentual}off-{(desconto.Meses is { } m ? $"{m}m" : "sempre")}";

        var couponService = new CouponService(_stripeClient);
        try
        {
            var existente = await couponService.GetAsync(cupomId, cancellationToken: ct);
            return existente.Id;
        }
        catch (StripeException ex) when (ex.StripeError?.Code == "resource_missing")
        {
        }

        var nome = $"{desconto.CodigoPromocional.ToUpperInvariant()} {desconto.Percentual}% off";
        var cupom = await couponService.CreateAsync(new CouponCreateOptions
        {
            Id = cupomId,
            Name = nome.Length > 40 ? nome[..40] : nome,
            PercentOff = desconto.Percentual,
            Duration = desconto.Meses.HasValue ? "repeating" : "forever",
            DurationInMonths = desconto.Meses
        }, cancellationToken: ct);

        _logger.LogInformation("Cupom Stripe criado: {CouponId}", cupom.Id);
        return cupom.Id;
    }

    /// <summary>
    /// O Price ativo com a lookup key do plano/período é o preço de tabela vigente. Quando o super
    /// admin muda o valor, cria um Price novo no mesmo produto e transfere a lookup key para ele;
    /// o antigo continua existindo, e as assinaturas já criadas com ele seguem cobrando o valor antigo.
    /// </summary>
    private async Task<string> ObterOuCriarPrecoAsync(string plano, string periodo, decimal valorMensal, CancellationToken ct)
    {
        var lookupKey = $"causify-{plano.ToLowerInvariant()}-{periodo.ToLowerInvariant()}";
        var anual = periodo == "Anual";
        var valorCentavos = (long)Math.Round((anual ? valorMensal * 12 : valorMensal) * 100m);

        var priceService = new PriceService(_stripeClient);
        var existentes = await priceService.ListAsync(new PriceListOptions
        {
            LookupKeys = new List<string> { lookupKey },
            Active = true
        }, cancellationToken: ct);

        var atual = existentes.Data.FirstOrDefault();
        if (atual?.UnitAmount == valorCentavos)
            return atual.Id;

        var produtoId = atual?.ProductId;
        if (string.IsNullOrEmpty(produtoId))
        {
            var productService = new ProductService(_stripeClient);
            var produto = await productService.CreateAsync(new ProductCreateOptions
            {
                Name = $"Causify {plano}",
                Description = $"Assinatura {periodo.ToLowerInvariant()} do plano {plano}"
            }, cancellationToken: ct);
            produtoId = produto.Id;
        }

        var preco = await priceService.CreateAsync(new PriceCreateOptions
        {
            Product = produtoId,
            Currency = "brl",
            UnitAmount = valorCentavos,
            LookupKey = lookupKey,
            TransferLookupKey = atual is not null,
            Recurring = new PriceRecurringOptions { Interval = anual ? "year" : "month" }
        }, cancellationToken: ct);

        _logger.LogInformation("Preço Stripe criado: {PriceId} ({LookupKey}, {Centavos} centavos, anterior {Anterior})",
            preco.Id, lookupKey, valorCentavos, atual?.Id);
        return preco.Id;
    }

    public async Task<StripeProximaCobranca?> ObterProximaCobrancaAsync(string subscriptionId, CancellationToken ct = default)
    {
        using var activity = Telemetry.Stripe.StartActivity(nameof(ObterProximaCobrancaAsync));
        activity?.SetTag("stripe.operation", "invoice.preview");
        var invoiceService = new InvoiceService(_stripeClient);
        try
        {
            var fatura = await invoiceService.CreatePreviewAsync(
                new InvoiceCreatePreviewOptions { Subscription = subscriptionId }, cancellationToken: ct);
            // A prévia ainda não tem tentativa de cobrança agendada: a data é o início do próximo período.
            var data = fatura.NextPaymentAttempt
                ?? fatura.Lines?.Data?.Select(l => (DateTime?)l.Period?.Start).Max();
            return new StripeProximaCobranca(fatura.AmountDue / 100m, data);
        }
        catch (StripeException ex) when (ex.StripeError?.Code == "invoice_upcoming_none")
        {
            return null;
        }
    }

    /// <summary>
    /// Valida a assinatura HMAC do webhook e retorna o evento Stripe já desserializado.
    /// Lança StripeException se a assinatura for inválida.
    /// </summary>
    public static Event ConstruirEventoWebhook(string payload, string assinaturaHeader, string webhookSecret)
        => EventUtility.ConstructEvent(payload, assinaturaHeader, webhookSecret, throwOnApiVersionMismatch: false);
}
