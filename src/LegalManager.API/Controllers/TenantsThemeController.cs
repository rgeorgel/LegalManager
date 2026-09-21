using LegalManager.Application.DTOs.Tenants;
using LegalManager.Application.Interfaces;
using LegalManager.Domain.Enums;
using LegalManager.Domain.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LegalManager.API.Controllers;

[ApiController]
[Route("api/tenants")]
[Authorize]
public class TenantsThemeController : ControllerBase
{
    private readonly ITenantsThemeService _service;
    private readonly ITenantContext _tenantContext;
    private readonly IConfiguration _configuration;

    public TenantsThemeController(ITenantsThemeService service, ITenantContext tenantContext, IConfiguration configuration)
    {
        _service = service;
        _tenantContext = tenantContext;
        _configuration = configuration;
    }

    [HttpGet("current/theme")]
    public async Task<ActionResult<TenantThemeDto>> GetTheme(CancellationToken ct)
    {
        var theme = await _service.GetThemeAsync(_tenantContext.TenantId, ct);
        if (theme is null) return NotFound();
        return Ok(theme);
    }

    [HttpPut("current/theme")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public async Task<ActionResult<TenantThemeDto>> UpdateTheme(
        [FromBody] UpdateTenantThemeDto dto,
        CancellationToken ct)
    {
        if (!dto.HasAnyChange)
            return BadRequest(new { message = "Nenhuma alteração enviada." });

        var permite = await _service.PermitePersonalizacaoAsync(_tenantContext.Plano, ct);
        if (!permite)
            return StatusCode(403, new { message = "Personalização de tema disponível a partir do plano Plus." });

        var theme = await _service.UpdateThemeAsync(_tenantContext.TenantId, dto, ct);
        return Ok(theme);
    }

    [HttpPost("current/logo")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public async Task<IActionResult> UploadLogo(
        [FromBody] UploadLogoDto dto,
        [FromServices] IStorageService storage,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(dto.Base64) || string.IsNullOrWhiteSpace(dto.ContentType))
            return BadRequest(new { message = "Logo ausente." });

        var permite = await _service.PermitePersonalizacaoAsync(_tenantContext.Plano, ct);
        if (!permite)
            return StatusCode(403, new { message = "Personalização de tema disponível a partir do plano Plus." });

        byte[] bytes;
        try
        {
            var commaIdx = dto.Base64.IndexOf(',');
            var raw = commaIdx >= 0 ? dto.Base64[(commaIdx + 1)..] : dto.Base64;
            bytes = Convert.FromBase64String(raw);
        }
        catch (FormatException)
        {
            return BadRequest(new { message = "Base64 inválido." });
        }

        if (bytes.Length > 2 * 1024 * 1024)
            return BadRequest(new { message = "Logo deve ter no máximo 2 MB." });

        var ext = dto.ContentType switch
        {
            "image/png" => "png",
            "image/jpeg" => "jpg",
            "image/svg+xml" => "svg",
            "image/webp" => "webp",
            _ => null
        };
        if (ext is null)
            return BadRequest(new { message = "Formato não suportado. Use PNG, JPG, SVG ou WebP." });

        var objectKey = $"tenants/{_tenantContext.TenantId}/logo-{DateTime.UtcNow:yyyyMMddHHmmss}.{ext}";
        using var stream = new MemoryStream(bytes);
        await storage.UploadAsync(stream, objectKey, dto.ContentType, ct);

        var ctx = HttpContext.RequestServices.GetRequiredService<LegalManager.Infrastructure.Persistence.AppDbContext>();
        var tenant = await ctx.Tenants.FindAsync([_tenantContext.TenantId], ct);
        if (tenant is null)
            return NotFound();

        var oldObjectKey = tenant.LogoObjectKey;

        // URL estável que nunca expira: resolve para uma presigned URL nova a
        // cada acesso (presigned URLs não podem ter validade "permanente").
        var frontendUrl = _configuration["App:FrontendUrl"]?.TrimEnd('/');
        var logoUrl = $"{frontendUrl}/api/tenants/{tenant.Id}/logo";

        tenant.LogoObjectKey = objectKey;
        tenant.LogoUrl = logoUrl;
        await ctx.SaveChangesAsync(ct);

        if (!string.IsNullOrWhiteSpace(oldObjectKey) && oldObjectKey != objectKey)
        {
            try { await storage.DeleteAsync(oldObjectKey, ct); } catch { /* best-effort cleanup */ }
        }

        return Ok(new { logoUrl });
    }

    [HttpDelete("current/logo")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public async Task<IActionResult> RemoveLogo(CancellationToken ct)
    {
        var permite = await _service.PermitePersonalizacaoAsync(_tenantContext.Plano, ct);
        if (!permite)
            return StatusCode(403, new { message = "Personalização de tema disponível a partir do plano Plus." });

        await _service.RemoveLogoAsync(_tenantContext.TenantId, ct);
        return NoContent();
    }
}

public record UploadLogoDto(string Base64, string ContentType);