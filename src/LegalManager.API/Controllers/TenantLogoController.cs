using LegalManager.Application.Interfaces;
using LegalManager.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LegalManager.API.Controllers;

// Endpoint público (sem [Authorize]): logos de tenant são usados em <img src>
// no browser (sem header de auth) e no gerador de PDF (fetch server-to-server).
// Não expõe nada sensível — apenas redireciona para uma URL assinada de curta
// duração, gerada na hora, resolvendo o problema de presigned URLs não poderem
// ter validade "permanente" (limite do protocolo SigV4 é 7 dias).
[ApiController]
[Route("api/tenants")]
public class TenantLogoController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly IStorageService _storage;

    public TenantLogoController(AppDbContext context, IStorageService storage)
    {
        _context = context;
        _storage = storage;
    }

    [HttpGet("{tenantId:guid}/logo")]
    public async Task<IActionResult> GetLogo(Guid tenantId, CancellationToken ct)
    {
        var objectKey = await _context.Tenants
            .Where(t => t.Id == tenantId)
            .Select(t => t.LogoObjectKey)
            .FirstOrDefaultAsync(ct);

        if (string.IsNullOrWhiteSpace(objectKey))
            return NotFound();

        var url = await _storage.GetPresignedUrlAsync(objectKey, 15, ct);
        Response.Headers.CacheControl = "no-store";
        return Redirect(url);
    }
}
