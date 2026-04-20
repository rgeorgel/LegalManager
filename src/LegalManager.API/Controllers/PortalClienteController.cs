using System.Security.Claims;
using LegalManager.Application.DTOs.PortalCliente;
using LegalManager.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LegalManager.API.Controllers;

[ApiController]
[Route("api/portal")]
public class PortalClienteController : ControllerBase
{
    private readonly IPortalClienteService _service;

    public PortalClienteController(IPortalClienteService service)
    {
        _service = service;
    }

    [HttpPost("login")]
    public async Task<ActionResult<PortalAuthResponseDto>> Login([FromBody] LoginPortalDto dto, CancellationToken ct)
    {
        try
        {
            var result = await _service.LoginAsync(dto, ct);
            return Ok(result);
        }
        catch (UnauthorizedAccessException ex)
        {
            return Unauthorized(new { message = ex.Message });
        }
    }

    [HttpGet("me")]
    [Authorize(Roles = "Cliente")]
    public async Task<ActionResult<ClientePerfilDto>> GetMe(CancellationToken ct)
    {
        var acessoId = GetAcessoId();
        var result = await _service.GetPerfilAsync(acessoId, ct);
        return Ok(result);
    }

    [HttpGet("meus-processos")]
    [Authorize(Roles = "Cliente")]
    public async Task<ActionResult<IEnumerable<MeuProcessoDto>>> GetMeusProcessos(CancellationToken ct)
    {
        var (contatoId, tenantId) = GetContatoTenant();
        var result = await _service.GetMeusProcessosAsync(contatoId, tenantId, ct);
        return Ok(result);
    }

    [HttpGet("meus-processos/{processoId:guid}")]
    [Authorize(Roles = "Cliente")]
    public async Task<ActionResult<MeuProcessoDto>> GetProcesso(Guid processoId, CancellationToken ct)
    {
        var (contatoId, tenantId) = GetContatoTenant();
        var result = await _service.GetProcessoAsync(processoId, contatoId, tenantId, ct);
        return result == null ? NotFound() : Ok(result);
    }

    [HttpGet("meus-processos/{processoId:guid}/andamentos")]
    [Authorize(Roles = "Cliente")]
    public async Task<ActionResult<IEnumerable<MeuAndamentoDto>>> GetAndamentos(Guid processoId, CancellationToken ct)
    {
        var (contatoId, tenantId) = GetContatoTenant();
        var result = await _service.GetAndamentosAsync(processoId, contatoId, tenantId, ct);
        return Ok(result);
    }

    private Guid GetAcessoId() =>
        Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue("sub")!);

    private (Guid contatoId, Guid tenantId) GetContatoTenant() => (
        Guid.Parse(User.FindFirstValue("contatoId")!),
        Guid.Parse(User.FindFirstValue("tenantId")!)
    );
}
