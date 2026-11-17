using System.Security.Claims;
using LegalManager.Domain;
using LegalManager.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LegalManager.API.Controllers;

/// <summary>
/// Recursos em teste liberados para o escritório do login (escritório e portal do cliente). O
/// frontend consulta a cada página — não vai no token — para que liberar ou tirar valha na hora.
/// </summary>
[ApiController]
[Route("api/recursos")]
[Authorize]
public class RecursosController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    [HttpGet("/api/portal/recursos")]
    public async Task<ActionResult<IReadOnlyList<string>>> Listar(CancellationToken ct)
    {
        if (!Guid.TryParse(User.FindFirstValue("tenantId"), out var tenantId)) return Ok(Array.Empty<string>());
        var tenant = await db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Id == tenantId, ct);
        return Ok(RecursosEmTeste.DoTenant(tenant));
    }
}
