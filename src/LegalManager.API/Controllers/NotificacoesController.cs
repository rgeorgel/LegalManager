using LegalManager.Application.DTOs.Notificacoes;
using LegalManager.Application.Interfaces;
using LegalManager.Domain.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LegalManager.API.Controllers;

[ApiController]
[Route("api/notificacoes")]
[Authorize]
public class NotificacoesController : ControllerBase
{
    private readonly INotificacaoService _service;
    private readonly IPreferenciasNotificacaoService _prefs;
    private readonly ITenantContext _tenant;

    public NotificacoesController(INotificacaoService service, IPreferenciasNotificacaoService prefs, ITenantContext tenant)
    {
        _service = service;
        _prefs = prefs;
        _tenant = tenant;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<NotificacaoDto>>> GetUnread(CancellationToken ct)
        => Ok(await _service.GetUnreadAsync(ct));

    [HttpGet("count")]
    public async Task<ActionResult<int>> GetCount(CancellationToken ct)
        => Ok(await _service.GetUnreadCountAsync(ct));

    [HttpGet("historico")]
    public async Task<IActionResult> GetHistorico([FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
    {
        var (items, total) = await _service.GetHistoricoAsync(page, pageSize, ct);
        return Ok(new { items, total });
    }

    [HttpPost("{id:guid}/lida")]
    public async Task<IActionResult> MarcarLida(Guid id, CancellationToken ct)
    {
        await _service.MarcarLidaAsync(id, ct);
        return NoContent();
    }

    [HttpPost("marcar-todas-lidas")]
    public async Task<IActionResult> MarcarTodasLidas(CancellationToken ct)
    {
        await _service.MarcarTodasLidasAsync(ct);
        return NoContent();
    }

    [HttpGet("preferencias")]
    public async Task<ActionResult<PreferenciasNotificacaoDto>> GetPreferencias(CancellationToken ct)
        => Ok(await _prefs.GetAsync(_tenant.TenantId, _tenant.UserId, ct));

    [HttpPut("preferencias")]
    public async Task<ActionResult<PreferenciasNotificacaoDto>> AtualizarPreferencias(
        [FromBody] AtualizarPreferenciasDto dto, CancellationToken ct)
        => Ok(await _prefs.AtualizarAsync(_tenant.TenantId, _tenant.UserId, dto, ct));
}
