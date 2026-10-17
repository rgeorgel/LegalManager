using System.Globalization;
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
    IPrecosPlanosService precosPlanos,
    AppDbContext context,
    ITenantContext tenantContext,
    UserManager<Usuario> userManager,
    IConfiguration config,
    ILogger<AssinaturaController> logger) : ControllerBase
{
    private static List<PacoteCreditosDto> _pacotes => PacotesCreditos.Todos;

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
            temBilling = tenant.StripeSubscriptionId != null,
            valorMensalAssinatura = tenant.ValorMensalAssinatura,
            // Código usado no cadastro (período grátis) ou aplicado depois (desconto).
            codigoPromocional = tenant.VoucherUtilizado ?? tenant.CodigoDesconto,
            // Plano grátis de código promocional: Ativo com data de fim e sem assinatura paga.
            periodoPromocional = tenant.Status == StatusTenant.Ativo && tenant.PlanoExpiraEm != null
                && tenant.StripeSubscriptionId == null && tenant.AbacatePayBillingId == null,
            podeAplicarCodigoPromocional = MotivoNaoPodeAplicarCodigo(tenant) is null,
            descontoPromocional = await ObterDescontoPendenteAsync(tenant, ct) is { } promo
                ? new DescontoPromocionalDto(promo.Codigo, promo.DescontoPercentual!.Value, promo.DescontoMeses)
                : null
        });
    }

    /// <summary>
    /// Desconto do código promocional do tenant (cadastro ou aplicado aqui), se ainda não foi
    /// aproveitado numa assinatura. Vale mesmo se o código foi desativado/expirou depois.
    /// </summary>
    private async Task<CodigoPromocional?> ObterDescontoPendenteAsync(Tenant tenant, CancellationToken ct)
    {
        if (tenant.CodigoDesconto is null || tenant.DescontoPromocionalUsadoEm is not null)
            return null;

        var promo = await context.CodigosPromocionais
            .FirstOrDefaultAsync(c => c.Codigo == tenant.CodigoDesconto, ct);
        return promo is { TemDesconto: true } ? promo : null;
    }

    // O desconto entra como cupom no checkout de uma assinatura nova — quem já tem assinatura
    // Stripe troca de plano in-place (sem checkout), então não teria onde aplicá-lo.
    private static string? MotivoNaoPodeAplicarCodigo(Tenant tenant) =>
        tenant.DescontoPromocionalUsadoEm is not null ? "Você já utilizou um desconto promocional."
        : !string.IsNullOrEmpty(tenant.StripeSubscriptionId) ? "Códigos promocionais valem apenas para uma nova assinatura."
        : null;

    /// <summary>
    /// Aplica um código promocional a um tenant já cadastrado. Só o desconto vale aqui —
    /// dias grátis são exclusivos do cadastro. Substitui um desconto pendente anterior.
    /// </summary>
    [HttpPost("codigo-promocional")]
    [Authorize(Roles = "Admin")]
    [Microsoft.AspNetCore.RateLimiting.EnableRateLimiting("codigo-promocional")]
    public async Task<IActionResult> AplicarCodigoPromocional([FromBody] AplicarCodigoPromocionalDto dto, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(dto.Codigo))
            return BadRequest(new { message = "Informe o código promocional." });

        var tenant = await context.Tenants.FindAsync([tenantContext.TenantId], ct);
        if (tenant is null) return NotFound();

        if (MotivoNaoPodeAplicarCodigo(tenant) is { } motivo)
            return BadRequest(new { message = motivo });

        CodigoPromocional promo;
        try
        {
            promo = await CodigosPromocionais.ValidarAsync(context, dto.Codigo, tenant.Id, ct);
        }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }

        if (!promo.TemDesconto)
            return BadRequest(new { message = "Este código não oferece desconto na assinatura — ele é válido apenas para novos cadastros." });

        tenant.CodigoDesconto = promo.Codigo;
        await context.SaveChangesAsync(ct);

        logger.LogInformation("Código promocional {Codigo} aplicado ao tenant {TenantId}", promo.Codigo, tenant.Id);
        return Ok(new DescontoPromocionalDto(promo.Codigo, promo.DescontoPercentual!.Value, promo.DescontoMeses));
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
        var precos = await precosPlanos.ObterAsync(ct);
        var podeAtualizarInPlace = !string.IsNullOrEmpty(tenant.StripeSubscriptionId)
                                && tenant.Plano != planoAlvo
                                && precos.ContainsKey(tenant.Plano)
                                && precos.ContainsKey(planoAlvo);

        if (podeAtualizarInPlace)
        {
            // Mesma conta da Stripe (proration): crédito do tempo não usado no valor que o tenant
            // paga de fato (pode ser um preço anterior a um aumento) + o plano novo pelo resto do
            // ciclo, com o cupom que ainda vale. A diferença é cobrada na hora (always_invoice).
            var agora = DateTime.UtcNow;
            var fracao = 0m;
            if (tenant.BillingCycleStart is { } inicio)
            {
                var fim = inicio.AddMonths(1);
                fracao = Math.Clamp((decimal)((fim - agora) / (fim - inicio)), 0m, 1m);
            }
            var promoAtual = tenant.CodigoDesconto is null ? null
                : await context.CodigosPromocionais.FirstOrDefaultAsync(c => c.Codigo == tenant.CodigoDesconto, ct);
            var fator = (100 - CodigosPromocionais.DescontoVigente(promoAtual, tenant.DescontoPromocionalUsadoEm, agora)) / 100m;

            var credito = Math.Round(fracao * (tenant.ValorMensalAssinatura ?? precos[tenant.Plano]) * fator, 2);
            var valorPlanoNovo = Math.Round(fracao * precos[planoAlvo] * fator, 2);

            return Ok(new
            {
                requerConfirmacao = true,
                prorado = true,
                credito,
                valorPlanoNovo,
                valorProrado = Math.Max(0m, valorPlanoNovo - credito),
                // Downgrade: o que sobra fica de crédito na Stripe e abate as próximas faturas.
                creditoRestante = Math.Max(0m, credito - valorPlanoNovo),
                planoAlvo = planoAlvo.ToString(),
                periodo = dto.Periodo
            });
        }

        var promo = await ObterDescontoPendenteAsync(tenant, ct);
        var desconto = promo is null ? null
            : new DescontoAssinaturaInput(promo.Codigo, promo.DescontoPercentual!.Value, promo.DescontoMeses);

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
                ValorMensal: precos[planoAlvo],
                ReturnUrl: returnUrl,
                CompletionUrl: completionUrl,
                Desconto: desconto
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

        var precoCheio = precos[planoAlvo];
        return Ok(new
        {
            requerConfirmacao = false,
            checkoutUrl = result.CheckoutUrl,
            prorado = false,
            valorProrado = desconto is null ? precoCheio
                : Math.Round(precoCheio * (100 - desconto.Percentual) / 100m, 2),
            desconto = desconto is null ? null
                : new DescontoPromocionalDto(desconto.CodigoPromocional, desconto.Percentual, desconto.Meses)
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

        var precos = await precosPlanos.ObterAsync(ct);
        StripeAtualizarAssinaturaResult atualizado;
        try
        {
            atualizado = await stripe.AtualizarAssinaturaAsync(new AtualizarAssinaturaInput(
                tenant.StripeSubscriptionId, planoAlvo.ToString(), dto.Periodo, precos[planoAlvo]), ct);
        }
        catch (StripeException ex)
        {
            logger.LogError(ex, "Erro ao atualizar assinatura Stripe {Id} para tenant {TenantId}",
                tenant.StripeSubscriptionId, tenant.Id);
            // error_if_incomplete: cartão recusado chega aqui e a assinatura fica como estava.
            return BadRequest(new { message = ex.StripeError?.Type == "card_error"
                ? "O cartão cadastrado recusou a cobrança da diferença. Atualize o cartão e tente de novo."
                : "Não foi possível processar a troca de plano. Tente novamente em instantes." });
        }

        context.AuditLogs.Add(AuditExtensions.NovoAuditLog(
            tenant.Id, tenantContext.UserId, TimelineAcoes.AssinaturaAlterada, TimelineAcoes.EntidadeAssinatura,
            atualizado.SubscriptionId,
            new { Plano = tenant.Plano.ToString(), tenant.PeriodoBilling, tenant.ValorMensalAssinatura },
            new
            {
                Plano = planoAlvo.ToString(), PeriodoBilling = dto.Periodo,
                ValorMensalAssinatura = precos[planoAlvo], ValorCobrado = atualizado.ValorCobradoImediato
            }));

        tenant.Plano = planoAlvo;
        tenant.Status = StatusTenant.Ativo;
        tenant.PlanoExpiraEm = null;
        tenant.PeriodoBilling = dto.Periodo;
        tenant.StripeSubscriptionId = atualizado.SubscriptionId;
        tenant.ValorMensalAssinatura = precos[planoAlvo];

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
                Descricao = $"Troca de plano → {planoAlvo} {dto.Periodo} (proporcional)"
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

        context.AuditLogs.Add(AuditExtensions.NovoAuditLog(
            tenant.Id, tenantContext.UserId, TimelineAcoes.AssinaturaCancelada, TimelineAcoes.EntidadeAssinatura,
            tenant.StripeSubscriptionId,
            new { Plano = tenant.Plano.ToString(), Status = tenant.Status.ToString(), tenant.PeriodoBilling },
            new { PlanoExpiraEm = expiraEm }));

        tenant.Status = StatusTenant.Cancelado;
        tenant.PlanoExpiraEm = expiraEm;
        await context.SaveChangesAsync(ct);

        return Ok(new
        {
            message = $"Assinatura cancelada. Você continuará com o plano {tenant.Plano} até {LegalManager.Infrastructure.BrasiliaTime.DeUtc(expiraEm.Value).ToString("dd/MM/yyyy", LegalManager.Infrastructure.BrasiliaTime.PtBr)}.",
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
    IPrecosPlanosService precosPlanos,
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
        var anterior = new { Plano = tenant.Plano.ToString(), Status = tenant.Status.ToString() };

        tenant.Plano = plano switch { "Plus" => PlanoTipo.Plus, "Max" => PlanoTipo.Max, _ => PlanoTipo.Pro };
        tenant.Status = StatusTenant.Ativo;
        tenant.TrialExpiraEm = null;
        tenant.PlanoExpiraEm = null;
        tenant.PeriodoBilling = periodo;
        tenant.BillingCycleStart = DateTime.UtcNow;
        tenant.StripeCustomerId = session.CustomerId;
        tenant.StripeSubscriptionId = session.SubscriptionId;
        // Preço enviado ao Stripe no checkout; sessões criadas antes deste campo caem na tabela.
        tenant.ValorMensalAssinatura = decimal.TryParse(session.Metadata.GetValueOrDefault("valorMensal"),
            NumberStyles.Number, CultureInfo.InvariantCulture, out var valorMensal)
            ? valorMensal
            : (await precosPlanos.ObterAsync(ct)).GetValueOrDefault(tenant.Plano);
        tenant.TrialConcedidoPorId = null;
        tenant.TrialConcedidoEm = null;
        tenant.TrialConcedidoDias = null;
        tenant.TrialConcedidoMotivo = null;
        if (session.Metadata.ContainsKey("codigoPromocional"))
            tenant.DescontoPromocionalUsadoEm ??= DateTime.UtcNow;

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
        context.AuditLogs.Add(AuditExtensions.NovoAuditLog(
            tenant.Id, null, TimelineAcoes.AssinaturaIniciada, TimelineAcoes.EntidadeAssinatura,
            session.SubscriptionId, anterior,
            new
            {
                Plano = tenant.Plano.ToString(),
                Periodo = periodo,
                Valor = (session.AmountTotal ?? 0) / 100m,
                tenant.ValorMensalAssinatura,
                CodigoPromocional = session.Metadata.GetValueOrDefault("codigoPromocional")
            }));

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

        context.AuditLogs.Add(AuditExtensions.NovoAuditLog(
            tenant.Id, null, TimelineAcoes.AssinaturaEncerrada, TimelineAcoes.EntidadeAssinatura,
            subscription.Id,
            new { Plano = tenant.Plano.ToString(), Status = tenant.Status.ToString() },
            new { Motivo = subscription.CancellationDetails?.Reason }));

        tenant.Status = StatusTenant.Cancelado;
        tenant.PlanoExpiraEm ??= DateTime.UtcNow;
        tenant.ValorMensalAssinatura = null;

        await context.SaveChangesAsync(ct);
        logger.LogInformation("Assinatura Stripe {SubscriptionId} encerrada para tenant {TenantId}", subscription.Id, tenant.Id);
    }
}

public record IniciarCheckoutDto(string Periodo, string Plano = "Pro");
public record ComprarCreditosDto(string PacoteId);
public record AplicarCodigoPromocionalDto(string Codigo);
public record DescontoPromocionalDto(string Codigo, int Percentual, int? Meses);
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
