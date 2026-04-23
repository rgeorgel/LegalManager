using LegalManager.Application.DTOs.IA;
using LegalManager.Application.Interfaces;
using LegalManager.Domain.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LegalManager.API.Controllers;

[ApiController]
[Route("api/ia")]
[Authorize]
public class IAController : ControllerBase
{
    private readonly ITraducaoService _traducaoService;
    private readonly IPecaJuridicaService _pecaJuridicaService;
    private readonly ICreditoService _creditoService;
    private readonly ITenantContext _tenantContext;

    public IAController(
        ITraducaoService traducaoService,
        IPecaJuridicaService pecaJuridicaService,
        ICreditoService creditoService,
        ITenantContext tenantContext)
    {
        _traducaoService = traducaoService;
        _pecaJuridicaService = pecaJuridicaService;
        _creditoService = creditoService;
        _tenantContext = tenantContext;
    }

    [HttpPost("traduzir-andamento")]
    public async Task<ActionResult<TraducaoResponseDto>> TraduzirAndamento(
        TraduzirAndamentoDto dto,
        CancellationToken ct)
    {
        try
        {
            var result = await _traducaoService.TraduzirAndamentoAsync(dto, _tenantContext.UserId, ct);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpGet("traduzir-andamento/{andamentoId:guid}")]
    public async Task<ActionResult<TraducaoResponseDto>> ObterTraducao(Guid andamentoId, CancellationToken ct)
    {
        var result = await _traducaoService.ObterTraducaoAsync(andamentoId, ct);
        return result == null ? NotFound() : Ok(result);
    }

    [HttpPost("gerar-peca")]
    public async Task<ActionResult<PecaGeradaResponseDto>> GerarPeca(
        GerarPecaDto dto,
        CancellationToken ct)
    {
        try
        {
            var result = await _pecaJuridicaService.GerarPecaAsync(dto, _tenantContext.UserId, ct);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpGet("pecas-geradas")]
    public async Task<ActionResult<IEnumerable<PecaGeradaResponseDto>>> ListarPecasGeradas(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] Guid? processoId = null,
        [FromQuery] string? tipo = null,
        CancellationToken ct = default)
    {
        Domain.Entities.TipoPecaJuridica? tipoEnum = null;
        if (tipo != null && Enum.TryParse<Domain.Entities.TipoPecaJuridica>(tipo, true, out var t))
            tipoEnum = t;
        var filtro = new ListPecasGeradasDto(page, pageSize, processoId, tipoEnum);
        var result = await _pecaJuridicaService.ListarAsync(filtro, ct);
        return Ok(result);
    }

    [HttpGet("pecas-geradas/{id:guid}")]
    public async Task<ActionResult<PecaGeradaResponseDto>> ObterPeca(Guid id, CancellationToken ct)
    {
        var result = await _pecaJuridicaService.ObterPecaAsync(id, ct);
        return result == null ? NotFound() : Ok(result);
    }
}

[ApiController]
[Route("api/creditos")]
[Authorize]
public class CreditosController : ControllerBase
{
    private readonly ICreditoService _creditoService;

    public CreditosController(ICreditoService creditoService)
    {
        _creditoService = creditoService;
    }

    [HttpGet]
    public async Task<ActionResult<CreditosTotaisDto>> GetCreditos(CancellationToken ct)
    {
        var result = await _creditoService.ObterCreditosAsync(ct);
        return Ok(result);
    }
}