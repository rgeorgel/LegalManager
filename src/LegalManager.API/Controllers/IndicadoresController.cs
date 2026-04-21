using LegalManager.Application.DTOs.Indicadores;
using LegalManager.Application.Interfaces;
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
        var result = await service.GetIndicadoresAsync(tenantContext.TenantId, ct);
        return Ok(result);
    }
}
