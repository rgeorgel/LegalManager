using System.Text.Json;
using LegalManager.Application.Interfaces;
using LegalManager.Domain.Entities;
using LegalManager.Domain.Enums;
using LegalManager.Domain.Interfaces;
using LegalManager.Infrastructure.Persistence;
using LegalManager.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LegalManager.API.Controllers;

[ApiController]
[Route("api/assinatura")]
[Authorize]
public class AssinaturaController(
    IAbacatePayService abacatePay,
    AppDbContext context,
    ITenantContext tenantContext,
    UserManager<Usuario> userManager,
    IConfiguration config) : ControllerBase
{
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
            temBilling = tenant.AbacatePayBillingId != null
        });
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
        if (dto.Periodo != "Mensal" && dto.Periodo != "Anual")
            return BadRequest(new { message = "Período inválido. Use 'Mensal' ou 'Anual'." });

        var tenant = await context.Tenants.FindAsync([tenantContext.TenantId], ct);
        if (tenant is null) return NotFound();

        if (tenant.Plano == PlanoTipo.Pro && tenant.Status == StatusTenant.Ativo && tenant.PlanoExpiraEm == null)
            return BadRequest(new { message = "Você já possui uma assinatura Pro ativa." });

        var admin = await userManager.GetUserAsync(User);
        if (admin is null) return Unauthorized();

        var frontendUrl = config["App:FrontendUrl"] ?? "http://localhost:6600";
        var returnUrl = $"{frontendUrl}/pages/assinatura.html?checkout=pendente";
        var completionUrl = $"{frontendUrl}/pages/assinatura.html?checkout=processando";

        AbacatePayBillingResult result;
        try
        {
            result = await abacatePay.CriarBillingAsync(new CriarBillingInput(
                TenantId: tenant.Id.ToString(),
                NomeEscritorio: tenant.Nome,
                Email: admin.Email!,
                NomeAdmin: admin.Nome,
                Cnpj: tenant.Cnpj,
                Periodo: dto.Periodo,
                ReturnUrl: returnUrl,
                CompletionUrl: completionUrl
            ), ct);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }

        // Salva o billing ID e período para rastrear
        tenant.AbacatePayBillingId = result.BillingId;
        tenant.PeriodoBilling = dto.Periodo;
        await context.SaveChangesAsync(ct);

        return Ok(new { checkoutUrl = result.CheckoutUrl });
    }

    [HttpPost("cancelar")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Cancelar(CancellationToken ct)
    {
        var tenant = await context.Tenants.FindAsync([tenantContext.TenantId], ct);
        if (tenant is null) return NotFound();

        if (tenant.Plano == PlanoTipo.Free)
            return BadRequest(new { message = "Você já está no plano Free." });

        // Calcula a data de expiração com base no período
        var expiraEm = tenant.PeriodoBilling == "Anual"
            ? DateTime.UtcNow.AddYears(1).Date    // simplificação: próximo ciclo anual
            : DateTime.UtcNow.AddMonths(1).Date;  // próximo ciclo mensal

        // Cancela no AbacatePay se houver billing ativo
        if (!string.IsNullOrEmpty(tenant.AbacatePayBillingId))
        {
            try { await abacatePay.CancelarBillingAsync(tenant.AbacatePayBillingId, ct); }
            catch (Exception ex)
            {
                Request.HttpContext.RequestServices
                    .GetRequiredService<ILogger<AssinaturaController>>()
                    .LogWarning(ex, "Erro ao cancelar billing {Id} no AbacatePay", tenant.AbacatePayBillingId);
            }
        }

        // Mantém Pro ativo até o fim do período
        tenant.Status = StatusTenant.Cancelado;
        tenant.PlanoExpiraEm = expiraEm;
        await context.SaveChangesAsync(ct);

        return Ok(new
        {
            message = $"Assinatura cancelada. Você continuará com o plano Pro até {expiraEm:dd/MM/yyyy}.",
            expiraEm
        });
    }
}

// Webhook — sem autenticação JWT
[ApiController]
[Route("api/webhooks")]
public class WebhookController(
    AppDbContext context,
    IConfiguration config,
    ILogger<WebhookController> logger) : ControllerBase
{
    [HttpPost("abacatepay")]
    [AllowAnonymous]
    public async Task<IActionResult> AbacatePay(
        [FromQuery] string? secret,
        CancellationToken ct)
    {
        // Verifica secret na query string
        var expectedSecret = config["AbacatePay:WebhookSecret"];
        if (!string.IsNullOrEmpty(expectedSecret) && secret != expectedSecret)
        {
            logger.LogWarning("Webhook AbacatePay recebido com secret inválido.");
            return Unauthorized();
        }

        // Lê o body raw para verificação de assinatura
        Request.EnableBuffering();
        using var reader = new StreamReader(Request.Body, leaveOpen: true);
        var rawBody = await reader.ReadToEndAsync(ct);
        Request.Body.Position = 0;

        // Verifica HMAC se houver header de assinatura
        var assinaturaHeader = Request.Headers["X-Webhook-Signature"].FirstOrDefault();
        if (!string.IsNullOrEmpty(assinaturaHeader) && !string.IsNullOrEmpty(expectedSecret))
        {
            if (!AbacatePayService.VerificarAssinatura(rawBody, assinaturaHeader, expectedSecret))
            {
                logger.LogWarning("Webhook AbacatePay com assinatura HMAC inválida.");
                return Unauthorized();
            }
        }

        using var doc = JsonDocument.Parse(rawBody);
        var root = doc.RootElement;

        var eventType = root.TryGetProperty("event", out var ev) ? ev.GetString() : null;
        logger.LogInformation("Webhook AbacatePay recebido: {Event}", eventType);

        switch (eventType)
        {
            case "billing.paid":
            case "subscription.completed":
            case "subscription.renewed":
                await HandlePagamentoConfirmado(root, ct);
                break;

            case "subscription.cancelled":
                await HandleSubscriptionCancelada(root, ct);
                break;

            default:
                logger.LogInformation("Evento AbacatePay ignorado: {Event}", eventType);
                break;
        }

        return Ok();
    }

    private async Task HandlePagamentoConfirmado(JsonElement root, CancellationToken ct)
    {
        var tenantId = ExtrairTenantId(root);
        if (tenantId == null) return;

        var tenant = await context.Tenants.FindAsync([tenantId.Value], ct);
        if (tenant is null) return;

        var periodo = ExtrairMetadata(root, "periodo") ?? tenant.PeriodoBilling ?? "Mensal";

        tenant.Plano = PlanoTipo.Pro;
        tenant.Status = StatusTenant.Ativo;
        tenant.TrialExpiraEm = null;
        tenant.PlanoExpiraEm = null;
        tenant.PeriodoBilling = periodo;

        var valor = ExtrairValor(root);
        var billingId = ExtrairBillingId(root);
        if (!string.IsNullOrEmpty(billingId))
        {
            context.Faturamentos.Add(new Faturamento
            {
                Id = Guid.NewGuid(),
                TenantId = tenant.Id,
                BillingId = billingId,
                Periodo = periodo,
                Valor = valor,
                Status = StatusFaturamento.Pago,
                DataPagamento = DateTime.UtcNow,
                DataCriacao = DateTime.UtcNow,
                Descricao = $"Assinatura Pro {periodo}"
            });
        }

        await context.SaveChangesAsync(ct);
        logger.LogInformation("Plano Pro ativado via webhook para tenant {TenantId}", tenantId);
    }

    private async Task HandleSubscriptionCancelada(JsonElement root, CancellationToken ct)
    {
        var tenantId = ExtrairTenantId(root);
        if (tenantId == null) return;

        var tenant = await context.Tenants.FindAsync([tenantId.Value], ct);
        if (tenant is null || tenant.PlanoExpiraEm.HasValue) return;

        // Se não foi cancelado pela UI (sem PlanoExpiraEm), expira no fim do período
        var expiraEm = tenant.PeriodoBilling == "Anual"
            ? DateTime.UtcNow.AddYears(1).Date
            : DateTime.UtcNow.AddMonths(1).Date;

        tenant.Status = StatusTenant.Cancelado;
        tenant.PlanoExpiraEm = expiraEm;

        await context.SaveChangesAsync(ct);
        logger.LogInformation("Assinatura cancelada via webhook para tenant {TenantId}, expira em {Data}", tenantId, expiraEm);
    }

    private static Guid? ExtrairTenantId(JsonElement root)
    {
        try
        {
            var metadata = root.GetProperty("data").GetProperty("billing").GetProperty("metadata");
            if (metadata.TryGetProperty("tenantId", out var tid) &&
                Guid.TryParse(tid.GetString(), out var guid))
                return guid;
        }
        catch { }
        return null;
    }

    private static string? ExtrairMetadata(JsonElement root, string key)
    {
        try
        {
            var metadata = root.GetProperty("data").GetProperty("billing").GetProperty("metadata");
            return metadata.TryGetProperty(key, out var val) ? val.GetString() : null;
        }
        catch { return null; }
    }

    private static decimal ExtrairValor(JsonElement root)
    {
        try
        {
            var amount = root.GetProperty("data").GetProperty("billing").GetProperty("amount");
            return amount.GetInt32() / 100m;
        }
        catch { return 0; }
    }

    private static string? ExtrairBillingId(JsonElement root)
    {
        try { return root.GetProperty("data").GetProperty("billing").GetProperty("id").GetString(); }
        catch { return null; }
    }
}

public record IniciarCheckoutDto(string Periodo);
