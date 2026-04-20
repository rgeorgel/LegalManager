using LegalManager.Application.DTOs.Notificacoes;
using LegalManager.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LegalManager.API.Controllers;

[ApiController]
[Route("api/notificacoes")]
[Authorize]
public class NotificacoesController : ControllerBase
{
    private readonly INotificacaoService _service;

    public NotificacoesController(INotificacaoService service)
    {
        _service = service;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<NotificacaoDto>>> GetUnread(CancellationToken ct)
        => Ok(await _service.GetUnreadAsync(ct));

    [HttpGet("count")]
    public async Task<ActionResult<int>> GetCount(CancellationToken ct)
        => Ok(await _service.GetUnreadCountAsync(ct));

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
}
