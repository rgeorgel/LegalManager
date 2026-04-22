using LegalManager.Application.DTOs.Indicadores;
using LegalManager.Application.Interfaces;
using LegalManager.Domain;
using LegalManager.Domain.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LegalManager.API.Controllers;

[ApiController]
[Route("api/indicadores")]
[Authorize]
public class IndicadoresController(IIndicadoresService service, ITenantContext tenantContext) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IndicadoresDto>> Get(CancellationToken ct)
    {
        if (!PlanoRestricoes.PermiteIndicadores(tenantContext.Plano))
            return StatusCode(402, new { message = "Indicadores disponíveis apenas no plano Pro." });

        var result = await service.GetIndicadoresAsync(tenantContext.TenantId, ct);
        return Ok(result);
    }
}
