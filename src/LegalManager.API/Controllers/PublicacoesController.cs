using LegalManager.Application.DTOs.Publicacoes;
using LegalManager.Application.Interfaces;
using LegalManager.Domain;
using LegalManager.Domain.Enums;
using LegalManager.Domain.Interfaces;
using LegalManager.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LegalManager.API.Controllers;

[ApiController]
[Route("api/publicacoes")]
[Authorize]
public class PublicacoesController : ControllerBase
{
    private readonly IPublicacaoService _service;
    private readonly ITriagemPublicacaoService _triagem;
    private readonly ITenantContext _tenant;

    public PublicacoesController(IPublicacaoService service, ITriagemPublicacaoService triagem, ITenantContext tenant)
    {
        _service = service;
        _triagem = triagem;
        _tenant = tenant;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] Guid? processoId,
        [FromQuery] string? tipo,
        [FromQuery] string? status,
        [FromQuery] string? fonteCaptura,
        [FromQuery] DateTime? de,
        [FromQuery] DateTime? ate,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] bool comSugestao = false,
        CancellationToken ct = default)
    {
        if (!PlanoRestricoes.PermiteCapturacaoPublicacoes(_tenant.Plano))
            return StatusCode(402, new { message = "A captura de publicações está disponível a partir do plano Pro." });
        var filtro = new PublicacaoFiltroDto(
            processoId,
            tipo != null && Enum.TryParse<TipoPublicacao>(tipo, true, out var t) ? t : null,
            status != null && Enum.TryParse<StatusPublicacao>(status, true, out var s) ? s : null,
            fonteCaptura != null && Enum.TryParse<FonteCaptura>(fonteCaptura, true, out var f) ? f : null,
            de, ate, page, pageSize, comSugestao);
        return Ok(await _service.GetAllAsync(filtro, ct));
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        var result = await _service.GetByIdAsync(id, ct);
        return result == null ? NotFound() : Ok(result);
    }

    [HttpPatch("{id:guid}/lida")]
    public async Task<IActionResult> MarcarLida(Guid id, CancellationToken ct)
    {
        await _service.MarcarLidaAsync(id, ct);
        return NoContent();
    }

    [HttpPatch("{id:guid}/arquivar")]
    public async Task<IActionResult> Arquivar(Guid id, CancellationToken ct)
    {
        await _service.ArquivarAsync(id, ct);
        return NoContent();
    }

    // ── Sugestão da IA (TriagemPublicacoesJob) ──────────────────────────────

    [HttpPost("{id:guid}/triagem/aceitar")]
    public Task<IActionResult> AceitarTriagem(Guid id, [FromBody] AceitarTriagemDto dto, CancellationToken ct)
        => Triagem(() => _triagem.AceitarAsync(id, dto, HttpContext.GetClientIpAddress(), ct));

    [HttpPost("{id:guid}/triagem/ignorar")]
    public Task<IActionResult> IgnorarTriagem(Guid id, CancellationToken ct)
        => Triagem(() => _triagem.IgnorarAsync(id, ct));

    [HttpPost("{id:guid}/triagem/refazer")]
    public Task<IActionResult> RefazerTriagem(Guid id, CancellationToken ct)
        => Triagem(() => _triagem.RefazerAsync(id, ct));

    private async Task<IActionResult> Triagem(Func<Task<TriagemPublicacaoDto>> acao)
    {
        if (!PlanoRestricoes.PermiteCapturacaoPublicacoes(_tenant.Plano))
            return StatusCode(402, new { message = "A captura de publicações está disponível a partir do plano Pro." });
        try
        {
            return Ok(await acao());
        }
        catch (KeyNotFoundException e) { return NotFound(new { message = e.Message }); }
        catch (ArgumentException e) { return BadRequest(new { message = e.Message }); }
        catch (InvalidOperationException e) { return Conflict(new { message = e.Message }); }
    }

    [HttpGet("nao-lidas/count")]
    public async Task<IActionResult> GetNaoLidasCount(CancellationToken ct)
        => Ok(new { count = await _service.GetNaoLidasCountAsync(ct) });
}
