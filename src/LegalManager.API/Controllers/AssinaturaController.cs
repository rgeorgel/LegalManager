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
    IAbacatePayService abacatePay,
    AppDbContext context,
    ITenantContext tenantContext,
    UserManager<Usuario> userManager,
    IConfiguration config) : ControllerBase
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
        var planoAlvo = dto.Plano?.ToLowerInvariant() switch
        {
            "plus" => PlanoTipo.Plus,
            _ => PlanoTipo.Pro
        };

        if (planoAlvo == PlanoTipo.Plus && dto.Periodo != "Mensal")
            return BadRequest(new { message = "O plano Plus está disponível apenas na modalidade Mensal." });

        if (planoAlvo == PlanoTipo.Pro && dto.Periodo != "Mensal" && dto.Periodo != "Anual")
            return BadRequest(new { message = "Período inválido. Use 'Mensal' ou 'Anual'." });

        var tenant = await context.Tenants.FindAsync([tenantContext.TenantId], ct);
        if (tenant is null) return NotFound();

        if (tenant.Plano == planoAlvo && tenant.Status == StatusTenant.Ativo && tenant.PlanoExpiraEm == null)
            return BadRequest(new { message = $"Você já possui uma assinatura {planoAlvo} ativa." });

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
                CompletionUrl: completionUrl,
                Plano: planoAlvo.ToString()
            ), ct);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }

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

        AbacatePayBillingResult result;
        try
        {
            result = await abacatePay.CriarCheckoutUnicoAsync(new CriarCheckoutUnicoInput(
                TenantId: tenant.Id.ToString(),
                Email: admin.Email!,
                NomeAdmin: admin.Nome,
                Cnpj: tenant.Cnpj,
                PacoteId: pacote.Id,
                PacoteNome: pacote.Nome,
                ValorCentavos: (int)(pacote.Valor * 100),
                ReturnUrl: returnUrl,
                CompletionUrl: completionUrl
            ), ct);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }

        return Ok(new { checkoutUrl = result.CheckoutUrl });
    }
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
            case "checkout.completed":
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

        var tipo = ExtrairMetadata(root, "tipo");
        if (tipo == "creditos_ia")
        {
            await HandleCreditosComprados(root, tenantId.Value, ct);
            return;
        }

        var tenant = await context.Tenants.FindAsync([tenantId.Value], ct);
        if (tenant is null) return;

        var periodo = ExtrairMetadata(root, "periodo") ?? tenant.PeriodoBilling ?? "Mensal";

        var planoStr = ExtrairMetadata(root, "plano");
        tenant.Plano = planoStr == "Plus" ? PlanoTipo.Plus : PlanoTipo.Pro;
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
                Descricao = $"Assinatura {tenant.Plano} {periodo}"
            });
        }

        await context.SaveChangesAsync(ct);
        logger.LogInformation("Plano {Plano} ativado via webhook para tenant {TenantId}", tenant.Plano, tenantId);
    }

    private async Task HandleCreditosComprados(JsonElement root, Guid tenantId, CancellationToken ct)
    {
        var pacoteId = ExtrairMetadata(root, "pacoteId");
        var pacote = PacotesCreditos.Todos.FirstOrDefault(p => p.Id == pacoteId);
        if (pacote is null)
        {
            logger.LogWarning("Pacote de créditos desconhecido: {PacoteId}", pacoteId);
            return;
        }

        await creditoService.AdicionarCreditosCompradosAsync(tenantId, pacote.CreditosTraducao, pacote.CreditosPeca, ct);

        var valor = ExtrairValor(root);
        var billingId = ExtrairBillingId(root);
        if (!string.IsNullOrEmpty(billingId))
        {
            context.Faturamentos.Add(new Faturamento
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                BillingId = billingId,
                Periodo = $"Pacote {pacote.Nome}",
                Valor = valor > 0 ? valor : pacote.Valor,
                Status = StatusFaturamento.Pago,
                DataPagamento = DateTime.UtcNow,
                DataCriacao = DateTime.UtcNow,
                Descricao = $"Créditos IA — {pacote.Nome} ({pacote.CreditosTraducao + pacote.CreditosPeca} créditos)"
            });
            await context.SaveChangesAsync(ct);
        }

        logger.LogInformation("Créditos IA adicionados via webhook para tenant {TenantId}, pacote {Pacote}", tenantId, pacote.Nome);
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

    private static JsonElement? ExtrairDataObject(JsonElement root)
    {
        var data = root.GetProperty("data");
        foreach (var key in new[] { "checkout", "billing", "subscription" })
            if (data.TryGetProperty(key, out var obj))
                return obj;
        return null;
    }

    private static Guid? ExtrairTenantId(JsonElement root)
    {
        try
        {
            var obj = ExtrairDataObject(root);
            if (obj is null) return null;
            var metadata = obj.Value.GetProperty("metadata");
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
            var obj = ExtrairDataObject(root);
            if (obj is null) return null;
            var metadata = obj.Value.GetProperty("metadata");
            return metadata.TryGetProperty(key, out var val) ? val.GetString() : null;
        }
        catch { return null; }
    }

    private static decimal ExtrairValor(JsonElement root)
    {
        try
        {
            var obj = ExtrairDataObject(root);
            if (obj is null) return 0;
            return obj.Value.GetProperty("amount").GetInt32() / 100m;
        }
        catch { return 0; }
    }

    private static string? ExtrairBillingId(JsonElement root)
    {
        try
        {
            var obj = ExtrairDataObject(root);
            return obj?.GetProperty("id").GetString();
        }
        catch { return null; }
    }
}

public record IniciarCheckoutDto(string Periodo, string Plano = "Pro");
public record ComprarCreditosDto(string PacoteId);

public static class PacotesCreditos
{
    public static readonly List<PacoteCreditosDto> Todos =
    [
        new("basico",   "Básico",    "Para começar com IA",          20,  10,  29.90m, null),
        new("padrao",   "Padrão",    "Para uso moderado",             65,  35, 89.90m, "Mais popular"),
        new("avancado", "Avançado",  "Para escritórios ativos",      135,  65, 159.90m, null),
    ];
}
