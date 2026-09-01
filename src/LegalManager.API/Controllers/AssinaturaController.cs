using LegalManager.Application.Interfaces;
using LegalManager.Domain;
using LegalManager.Domain.Entities;
using LegalManager.Domain.Enums;
using LegalManager.Domain.Interfaces;
using LegalManager.Infrastructure.Persistence;
using LegalManager.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Stripe;
using StripeCheckoutSession = Stripe.Checkout.Session;

namespace LegalManager.API.Controllers;

public record PacoteCreditosDto(
    string Id,
    string Nome,
    string Descricao,
    int CreditosTraducao,
    int CreditosPeca,
    decimal Valor,
    string? DestaqueBadge
);

[ApiController]
[Route("api/assinatura")]
[Authorize]
public class AssinaturaController(
    IStripeService stripe,
    AppDbContext context,
    ITenantContext tenantContext,
    UserManager<Usuario> userManager,
    IConfiguration config,
    ILogger<AssinaturaController> logger) : ControllerBase
{
    private static List<PacoteCreditosDto> _pacotes => PacotesCreditos.Todos;

    private static readonly Dictionary<PlanoTipo, decimal> _precosPorPlano = new()
    {
        { PlanoTipo.Plus,  20m },
        { PlanoTipo.Pro,   50m },
        { PlanoTipo.Max,  125m }
    };

    [HttpGet]
    public async Task<IActionResult> GetStatus(CancellationToken ct)
    {
        var tenant = await context.Tenants.FindAsync([tenantContext.TenantId], ct);
        if (tenant is null) return NotFound();

        return Ok(new
        {
            plano = tenant.Plano.ToString(),
            status = tenant.Status.ToString(),
            periodo = tenant.PeriodoBilling,
            planoExpiraEm = tenant.PlanoExpiraEm,
            trialExpiraEm = tenant.TrialExpiraEm,
            criadoEm = tenant.CriadoEm,
            temBilling = tenant.StripeSubscriptionId != null
        });
    }

    [HttpGet("trial-boas-vindas")]
    public async Task<IActionResult> GetTrialBoasVindasStatus(CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";

        var tenant = await context.Tenants.FindAsync([tenantContext.TenantId], ct);
        if (tenant is null) return NotFound();

        var elegivel = tenant.Status == StatusTenant.Trial
            && tenant.Plano == PlanoTipo.Plus
            && tenant.TrialConcedidoPorId == null
            && tenant.TrialConcedidoMotivo == TrialGratisConstants.MotivoTrialBoasVindasFree
            && !tenant.TrialGratisBoasVindasVisualizado;

        var diasRestantes = tenant.TrialExpiraEm.HasValue
            ? Math.Max(0, (int)Math.Ceiling((tenant.TrialExpiraEm.Value - DateTime.UtcNow).TotalDays))
            : 0;

        return Ok(new TrialGratisBoasVindasStatusDto(elegivel, diasRestantes, tenant.TrialExpiraEm));
    }

    [HttpPost("trial-boas-vindas/visualizado")]
    public async Task<IActionResult> MarcarTrialBoasVindasVisualizado(CancellationToken ct)
    {
        var tenant = await context.Tenants.FindAsync([tenantContext.TenantId], ct);
        if (tenant is null) return NotFound();

        tenant.TrialGratisBoasVindasVisualizado = true;
        await context.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpGet("historico")]
    public async Task<IActionResult> GetHistorico(CancellationToken ct)
    {
        var historico = await context.Faturamentos
            .Where(f => f.TenantId == tenantContext.TenantId)
            .OrderByDescending(f => f.DataCriacao)
            .Select(f => new {
                f.Id,
                f.Periodo,
                f.Valor,
                f.Moeda,
                Status = f.Status.ToString(),
                f.DataPagamento,
                f.DataCriacao,
                f.Descricao
            })
            .ToListAsync(ct);
        return Ok(historico);
    }

    [HttpPost("iniciar")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> IniciarCheckout([FromBody] IniciarCheckoutDto dto, CancellationToken ct)
    {
        var planoAlvo = dto.Plano?.ToLowerInvariant() switch
        {
            "plus" => PlanoTipo.Plus,
            "max"  => PlanoTipo.Max,
            _      => PlanoTipo.Pro
        };

        if (dto.Periodo != "Mensal")
            return BadRequest(new { message = "Apenas o período mensal está disponível no momento." });

        var tenant = await context.Tenants.FindAsync([tenantContext.TenantId], ct);
        if (tenant is null) return NotFound();

        if (tenant.Plano == planoAlvo && tenant.Status == StatusTenant.Ativo && tenant.PlanoExpiraEm == null)
            return BadRequest(new { message = $"Você já possui uma assinatura {planoAlvo} ativa." });

        var admin = await userManager.GetUserAsync(User);
        if (admin is null) return Unauthorized();

        var frontendUrl = config["App:FrontendUrl"] ?? "http://localhost:6600";
        var returnUrl    = $"{frontendUrl}/pages/assinatura.html?checkout=pendente";
        var completionUrl = $"{frontendUrl}/pages/assinatura.html?checkout=processando";

        // Tenant com assinatura Stripe já vigente: upgrade/downgrade é feito por proration
        // in-place (sem novo checkout) — só entra no cálculo de PREVIEW aqui; a cobrança de
        // fato só acontece quando o admin confirmar em /assinatura/confirmar-upgrade.
        var podeAtualizarInPlace = !string.IsNullOrEmpty(tenant.StripeSubscriptionId)
                                && tenant.Plano != planoAlvo
                                && _precosPorPlano.ContainsKey(tenant.Plano)
                                && _precosPorPlano.ContainsKey(planoAlvo);

        if (podeAtualizarInPlace)
        {
            var diasRestantes = tenant.BillingCycleStart.HasValue
                ? Math.Max(0, (tenant.BillingCycleStart.Value.AddDays(30) - DateTime.UtcNow).TotalDays)
                : 0;
            var credito = Math.Round((decimal)(diasRestantes / 30.0) * _precosPorPlano[tenant.Plano], 2);
            var valorProrado = Math.Max(1m, _precosPorPlano[planoAlvo] - credito);

            return Ok(new
            {
                requerConfirmacao = true,
                prorado = true,
                credito,
                valorProrado,
                planoAlvo = planoAlvo.ToString(),
                periodo = dto.Periodo
            });
        }

        StripeCheckoutResultDto result;
        try
        {
            var checkout = await stripe.CriarCheckoutAssinaturaAsync(new CriarCheckoutAssinaturaInput(
                TenantId: tenant.Id.ToString(),
                StripeCustomerId: tenant.StripeCustomerId,
                NomeEscritorio: tenant.Nome,
                Email: admin.Email!,
                NomeAdmin: admin.Nome,
                Cnpj: tenant.Cnpj,
                Plano: planoAlvo.ToString(),
                Periodo: dto.Periodo,
                ReturnUrl: returnUrl,
                CompletionUrl: completionUrl
            ), ct);
            result = new StripeCheckoutResultDto(checkout.CheckoutUrl, checkout.CustomerId);
        }
        catch (StripeException ex)
        {
            logger.LogError(ex, "Erro ao criar checkout de assinatura Stripe para tenant {TenantId}", tenant.Id);
            return BadRequest(new { message = "Não foi possível iniciar o checkout. Tente novamente em instantes." });
        }

        tenant.StripeCustomerId = result.CustomerId;
        tenant.PeriodoBilling = dto.Periodo;
        await context.SaveChangesAsync(ct);

        return Ok(new
        {
            requerConfirmacao = false,
            checkoutUrl = result.CheckoutUrl,
            prorado = false,
            valorProrado = _precosPorPlano.GetValueOrDefault(planoAlvo, 0m)
        });
    }

    /// <summary>
    /// Confirma um upgrade/downgrade de plano para um tenant que já tem assinatura Stripe
    /// ativa. A cobrança prorada acontece aqui, no cartão já salvo — sem redirect.
    /// </summary>
    [HttpPost("confirmar-upgrade")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> ConfirmarUpgrade([FromBody] IniciarCheckoutDto dto, CancellationToken ct)
    {
        var planoAlvo = dto.Plano?.ToLowerInvariant() switch
        {
            "plus" => PlanoTipo.Plus,
            "max"  => PlanoTipo.Max,
            _      => PlanoTipo.Pro
        };

        var tenant = await context.Tenants.FindAsync([tenantContext.TenantId], ct);
        if (tenant is null) return NotFound();

        if (string.IsNullOrEmpty(tenant.StripeSubscriptionId))
            return BadRequest(new { message = "Tenant não possui assinatura ativa para atualizar." });

        if (tenant.Plano == planoAlvo)
            return BadRequest(new { message = $"Você já possui uma assinatura {planoAlvo} ativa." });

        StripeAtualizarAssinaturaResult atualizado;
        try
        {
            atualizado = await stripe.AtualizarAssinaturaAsync(new AtualizarAssinaturaInput(
                tenant.StripeSubscriptionId, planoAlvo.ToString(), dto.Periodo), ct);
        }
        catch (StripeException ex)
        {
            logger.LogError(ex, "Erro ao atualizar assinatura Stripe {Id} para tenant {TenantId}",
                tenant.StripeSubscriptionId, tenant.Id);
            return BadRequest(new { message = "Não foi possível processar o upgrade. Tente novamente em instantes." });
        }

        tenant.Plano = planoAlvo;
        tenant.Status = StatusTenant.Ativo;
        tenant.PlanoExpiraEm = null;
        tenant.PeriodoBilling = dto.Periodo;
        tenant.StripeSubscriptionId = atualizado.SubscriptionId;

        if (atualizado.ValorCobradoImediato > 0)
        {
            context.Faturamentos.Add(new Faturamento
            {
                Id = Guid.NewGuid(),
                TenantId = tenant.Id,
                BillingId = atualizado.SubscriptionId,
                Periodo = dto.Periodo,
                Valor = atualizado.ValorCobradoImediato,
                Status = StatusFaturamento.Pago,
                DataPagamento = DateTime.UtcNow,
                DataCriacao = DateTime.UtcNow,
                Descricao = $"Upgrade → {planoAlvo} {dto.Periodo} (prorated)"
            });
        }

        await context.SaveChangesAsync(ct);
        logger.LogInformation("Upgrade confirmado para tenant {TenantId}: plano {Plano}, cobrado R$ {Valor}",
            tenant.Id, planoAlvo, atualizado.ValorCobradoImediato);

        return Ok(new { plano = planoAlvo.ToString(), valorCobrado = atualizado.ValorCobradoImediato, status = atualizado.Status });
    }

    [HttpPost("cancelar")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Cancelar(CancellationToken ct)
    {
        var tenant = await context.Tenants.FindAsync([tenantContext.TenantId], ct);
        if (tenant is null) return NotFound();

        if (tenant.Status == StatusTenant.Trial)
            return BadRequest(new
            {
                message = "Você está no período de trial. Não há nenhuma assinatura ativa para cancelar — ao final deste período, sua conta retorna automaticamente ao plano gratuito, sem qualquer cobrança.",
                motivo = "trial"
            });

        if (tenant.Plano == PlanoTipo.Free)
            return BadRequest(new { message = "Você já está no plano Free." });

        DateTime? expiraEm = null;
        if (!string.IsNullOrEmpty(tenant.StripeSubscriptionId))
            expiraEm = await stripe.CancelarAssinaturaAsync(tenant.StripeSubscriptionId, ct);

        // Fallback caso a Stripe não retorne a data (ex.: subscription já removida lá)
        expiraEm ??= tenant.PeriodoBilling == "Anual"
            ? DateTime.UtcNow.AddYears(1).Date
            : DateTime.UtcNow.AddMonths(1).Date;

        tenant.Status = StatusTenant.Cancelado;
        tenant.PlanoExpiraEm = expiraEm;
        await context.SaveChangesAsync(ct);

        return Ok(new
        {
            message = $"Assinatura cancelada. Você continuará com o plano {tenant.Plano} até {expiraEm:dd/MM/yyyy}.",
            expiraEm
        });
    }

    [HttpGet("creditos/pacotes")]
    public IActionResult GetPacotes() => Ok(_pacotes);

    [HttpPost("creditos/comprar")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> ComprarCreditos([FromBody] ComprarCreditosDto dto, CancellationToken ct)
    {
        var pacote = _pacotes.FirstOrDefault(p => p.Id == dto.PacoteId);
        if (pacote is null)
            return BadRequest(new { message = "Pacote inválido." });

        var tenant = await context.Tenants.FindAsync([tenantContext.TenantId], ct);
        if (tenant is null) return NotFound();

        var admin = await userManager.GetUserAsync(User);
        if (admin is null) return Unauthorized();

        var frontendUrl = config["App:FrontendUrl"] ?? "http://localhost:6600";
        var returnUrl = $"{frontendUrl}/pages/assinatura.html?creditos=pendente";
        var completionUrl = $"{frontendUrl}/pages/assinatura.html?creditos=processando";

        StripeCheckoutResult result;
        try
        {
            result = await stripe.CriarCheckoutAvulsoAsync(new CriarCheckoutAvulsoInput(
                TenantId: tenant.Id.ToString(),
                StripeCustomerId: tenant.StripeCustomerId,
                Email: admin.Email!,
                NomeAdmin: admin.Nome,
                Cnpj: tenant.Cnpj,
                ReferenciaId: pacote.Id,
                Nome: $"Causify — Créditos IA {pacote.Nome}",
                Descricao: $"Pacote de créditos de IA — {pacote.Nome}",
                ValorCentavos: (int)(pacote.Valor * 100),
                Tipo: "creditos_ia",
                ReturnUrl: returnUrl,
                CompletionUrl: completionUrl
            ), ct);
        }
        catch (StripeException ex)
        {
            logger.LogError(ex, "Erro ao criar checkout de créditos Stripe para tenant {TenantId}", tenant.Id);
            return BadRequest(new { message = "Não foi possível iniciar o checkout. Tente novamente em instantes." });
        }

        tenant.StripeCustomerId = result.CustomerId;
        await context.SaveChangesAsync(ct);

        return Ok(new { checkoutUrl = result.CheckoutUrl });
    }

    private record StripeCheckoutResultDto(string CheckoutUrl, string CustomerId);
}

// Webhook — sem autenticação JWT
[ApiController]
[Route("api/webhooks")]
public class WebhookController(
    AppDbContext context,
    IConfiguration config,
    ICreditoService creditoService,
    ILogger<WebhookController> logger) : ControllerBase
{
    [HttpPost("stripe")]
    [AllowAnonymous]
    public async Task<IActionResult> Stripe(CancellationToken ct)
    {
        Request.EnableBuffering();
        using var reader = new StreamReader(Request.Body, leaveOpen: true);
        var rawBody = await reader.ReadToEndAsync(ct);
        Request.Body.Position = 0;

        var webhookSecret = config["Stripe:WebhookSecret"];
        var signatureHeader = Request.Headers["Stripe-Signature"].FirstOrDefault();

        Event stripeEvent;
        try
        {
            // throwOnApiVersionMismatch: false — só usamos campos estáveis do payload,
            // não vale a pena rejeitar o evento por causa da versão de API da conta Stripe.
            stripeEvent = string.IsNullOrEmpty(webhookSecret)
                ? EventUtility.ParseEvent(rawBody, throwOnApiVersionMismatch: false)
                : StripeService.ConstruirEventoWebhook(rawBody, signatureHeader ?? "", webhookSecret);
        }
        catch (StripeException ex)
        {
            logger.LogWarning(ex, "Webhook Stripe com assinatura inválida.");
            return Unauthorized();
        }

        logger.LogInformation("Webhook Stripe recebido: {Event}", stripeEvent.Type);

        switch (stripeEvent.Type)
        {
            case "checkout.session.completed":
                await HandleCheckoutCompletedAsync(stripeEvent, ct);
                break;

            case "invoice.paid":
                await HandleInvoicePaidAsync(stripeEvent, ct);
                break;

            case "customer.subscription.deleted":
                await HandleSubscriptionDeletedAsync(stripeEvent, ct);
                break;

            default:
                logger.LogInformation("Evento Stripe ignorado: {Event}", stripeEvent.Type);
                break;
        }

        return Ok();
    }

    private async Task HandleCheckoutCompletedAsync(Event stripeEvent, CancellationToken ct)
    {
        if (stripeEvent.Data.Object is not StripeCheckoutSession session) return;

        if (!session.Metadata.TryGetValue("tenantId", out var tenantIdStr) || !Guid.TryParse(tenantIdStr, out var tenantId))
        {
            logger.LogWarning("checkout.session.completed sem tenantId em metadata: {SessionId}", session.Id);
            return;
        }

        var tenant = await context.Tenants.FindAsync([tenantId], ct);
        if (tenant is null) return;

        if (session.Mode == "payment" && session.Metadata.GetValueOrDefault("tipo") == "creditos_ia")
        {
            await HandleCreditosCompradosAsync(session, tenant.Id, ct);
            return;
        }

        if (session.Mode != "subscription" || string.IsNullOrEmpty(session.SubscriptionId)) return;

        var plano = session.Metadata.GetValueOrDefault("plano");
        var periodo = session.Metadata.GetValueOrDefault("periodo") ?? tenant.PeriodoBilling ?? "Mensal";

        tenant.Plano = plano switch { "Plus" => PlanoTipo.Plus, "Max" => PlanoTipo.Max, _ => PlanoTipo.Pro };
        tenant.Status = StatusTenant.Ativo;
        tenant.TrialExpiraEm = null;
        tenant.PlanoExpiraEm = null;
        tenant.PeriodoBilling = periodo;
        tenant.BillingCycleStart = DateTime.UtcNow;
        tenant.StripeCustomerId = session.CustomerId;
        tenant.StripeSubscriptionId = session.SubscriptionId;
        tenant.TrialConcedidoPorId = null;
        tenant.TrialConcedidoEm = null;
        tenant.TrialConcedidoDias = null;
        tenant.TrialConcedidoMotivo = null;

        context.Faturamentos.Add(new Faturamento
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.Id,
            BillingId = session.SubscriptionId,
            Periodo = periodo,
            Valor = (session.AmountTotal ?? 0) / 100m,
            Status = StatusFaturamento.Pago,
            DataPagamento = DateTime.UtcNow,
            DataCriacao = DateTime.UtcNow,
            Descricao = $"Assinatura {tenant.Plano} {periodo}"
        });

        await context.SaveChangesAsync(ct);
        logger.LogInformation("Plano {Plano} ativado via webhook Stripe para tenant {TenantId}", tenant.Plano, tenant.Id);
    }

    private async Task HandleCreditosCompradosAsync(StripeCheckoutSession session, Guid tenantId, CancellationToken ct)
    {
        var pacoteId = session.Metadata.GetValueOrDefault("referenciaId");
        var pacote = PacotesCreditos.Todos.FirstOrDefault(p => p.Id == pacoteId);
        if (pacote is null)
        {
            logger.LogWarning("Pacote de créditos desconhecido: {PacoteId}", pacoteId);
            return;
        }

        await creditoService.AdicionarCreditosCompradosAsync(tenantId, pacote.CreditosTraducao, pacote.CreditosPeca, ct);

        context.Faturamentos.Add(new Faturamento
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            BillingId = session.Id,
            Periodo = $"Pacote {pacote.Nome}",
            Valor = (session.AmountTotal ?? 0) / 100m,
            Status = StatusFaturamento.Pago,
            DataPagamento = DateTime.UtcNow,
            DataCriacao = DateTime.UtcNow,
            Descricao = $"Créditos IA — {pacote.Nome} ({pacote.CreditosTraducao + pacote.CreditosPeca} créditos)"
        });
        await context.SaveChangesAsync(ct);

        logger.LogInformation("Créditos IA adicionados via webhook Stripe para tenant {TenantId}, pacote {Pacote}", tenantId, pacote.Nome);
    }

    private async Task HandleInvoicePaidAsync(Event stripeEvent, CancellationToken ct)
    {
        if (stripeEvent.Data.Object is not Invoice invoice) return;

        // Renovação automática de ciclo. A cobrança inicial (subscription_create) já foi
        // registrada em checkout.session.completed, e a de upgrade (subscription_update) já
        // foi registrada em ConfirmarUpgrade — aqui só tratamos o ciclo recorrente normal.
        if (invoice.BillingReason != "subscription_cycle") return;

        var subscriptionId = invoice.Parent?.SubscriptionDetails?.SubscriptionId;
        if (string.IsNullOrEmpty(subscriptionId)) return;

        var tenant = await context.Tenants.FirstOrDefaultAsync(t => t.StripeSubscriptionId == subscriptionId, ct);
        if (tenant is null) return;

        tenant.Status = StatusTenant.Ativo;
        tenant.PlanoExpiraEm = null;
        tenant.BillingCycleStart = DateTime.UtcNow;

        context.Faturamentos.Add(new Faturamento
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.Id,
            BillingId = subscriptionId,
            Periodo = tenant.PeriodoBilling ?? "Mensal",
            Valor = invoice.AmountPaid / 100m,
            Status = StatusFaturamento.Pago,
            DataPagamento = DateTime.UtcNow,
            DataCriacao = DateTime.UtcNow,
            Descricao = $"Renovação {tenant.Plano} {tenant.PeriodoBilling}"
        });

        await context.SaveChangesAsync(ct);
        logger.LogInformation("Renovação registrada via webhook Stripe para tenant {TenantId}", tenant.Id);
    }

    private async Task HandleSubscriptionDeletedAsync(Event stripeEvent, CancellationToken ct)
    {
        if (stripeEvent.Data.Object is not Subscription subscription) return;

        var tenant = await context.Tenants.FirstOrDefaultAsync(t => t.StripeSubscriptionId == subscription.Id, ct);
        if (tenant is null) return;

        tenant.Status = StatusTenant.Cancelado;
        tenant.PlanoExpiraEm ??= DateTime.UtcNow;

        await context.SaveChangesAsync(ct);
        logger.LogInformation("Assinatura Stripe {SubscriptionId} encerrada para tenant {TenantId}", subscription.Id, tenant.Id);
    }
}

public record IniciarCheckoutDto(string Periodo, string Plano = "Pro");
public record ComprarCreditosDto(string PacoteId);
public record TrialGratisBoasVindasStatusDto(bool Exibir, int DiasRestantes, DateTime? TrialExpiraEm);

public static class PacotesCreditos
{
    public static readonly List<PacoteCreditosDto> Todos =
    [
        new("basico",   "Básico",    "Para começar com IA",          20,  10,  29.90m, null),
        new("padrao",   "Padrão",    "Para uso moderado",             65,  35, 89.90m, "Mais popular"),
        new("avancado", "Avançado",  "Para escritórios ativos",      135,  65, 159.90m, null),
    ];
}
