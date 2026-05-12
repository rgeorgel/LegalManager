using LegalManager.Application.DTOs.Publicacoes;
using LegalManager.Application.Interfaces;
using LegalManager.Domain;
using LegalManager.Domain.Enums;
using LegalManager.Domain.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LegalManager.API.Controllers;

[ApiController]
[Route("api/publicacoes")]
[Authorize]
public class PublicacoesController : ControllerBase
{
    private readonly IPublicacaoService _service;
    private readonly ITenantContext _tenant;

    public PublicacoesController(IPublicacaoService service, ITenantContext tenant)
    {
        _service = service;
        _tenant = tenant;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] Guid? processoId,
        [FromQuery] string? tipo,
        [FromQuery] string? status,
        [FromQuery] DateTime? de,
        [FromQuery] DateTime? ate,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        if (!PlanoRestricoes.PermiteCapturacaoPublicacoes(_tenant.Plano))
            return StatusCode(402, new { message = "A captura de publicações está disponível a partir do plano Pro." });
        var filtro = new PublicacaoFiltroDto(
            processoId,
            tipo != null && Enum.TryParse<TipoPublicacao>(tipo, true, out var t) ? t : null,
            status != null && Enum.TryParse<StatusPublicacao>(status, true, out var s) ? s : null,
            de, ate, page, pageSize);
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

    [HttpGet("nao-lidas/count")]
    public async Task<IActionResult> GetNaoLidasCount(CancellationToken ct)
        => Ok(new { count = await _service.GetNaoLidasCountAsync(ct) });
}
