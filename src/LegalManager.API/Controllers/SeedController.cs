using LegalManager.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LegalManager.API.Controllers;

[ApiController]
[Route("api/seed")]
[Authorize(Roles = "Admin")]
public class SeedController : ControllerBase
{
    private readonly SeedService _seedService;

    public SeedController(SeedService seedService)
    {
        _seedService = seedService;
    }

    [HttpPost("gerar")]
    public async Task<IActionResult> GerarDadosDemo([FromQuery] Guid tenantId, CancellationToken ct)
    {
        if (tenantId == Guid.Empty)
            return BadRequest(new { message = "tenantId é obrigatório." });

        try
        {
            await _seedService.GerarDadosDemoAsync(tenantId, ct);
            return Ok(new { message = "Dados demo gerados com sucesso.", tenantId });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpDelete("desfazer")]
    public async Task<IActionResult> DesfazerDadosDemo([FromQuery] Guid tenantId, CancellationToken ct)
    {
        if (tenantId == Guid.Empty)
            return BadRequest(new { message = "tenantId é obrigatório." });

        try
        {
            await _seedService.DesfazerDadosDemoAsync(tenantId, ct);
            return Ok(new { message = "Dados demo removidos com sucesso.", tenantId });
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }
}