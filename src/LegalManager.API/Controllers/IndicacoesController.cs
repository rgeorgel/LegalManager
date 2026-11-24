using LegalManager.Application.DTOs.Parceiros;
using LegalManager.Domain.Interfaces;
using LegalManager.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LegalManager.API.Controllers;

// "Indique e ganhe" (pages/indicacoes.html): o usuário do escritório indica outros escritórios e
// recebe comissão sobre as assinaturas deles. Por baixo é um Parceiro do próprio usuário (ver
// ProgramaIndicacao), então as consultas e a solicitação de pagamento são as do portal do
// parceiro (ParceiroService) — o super admin paga pelo mesmo fluxo de superadmin/parceiros.html.
[ApiController]
[Route("api/indicacoes")]
[Authorize(Roles = "Admin,Advogado,Colaborador")]
public class IndicacoesController(ParceiroService service, ITenantContext tenantContext) : ControllerBase
{
    internal const string MensagemImpersonacao =
        "Durante o acesso como suporte não é possível alterar o programa de indicações do usuário.";
    internal const string MensagemNaoParticipa = "Gere seu link de indicação primeiro.";

    [HttpGet]
    public async Task<ActionResult<IndicacoesStatusDto>> Status(CancellationToken ct) =>
        Ok(await service.GetIndicacoesAsync(tenantContext.UserId, ct));

    [HttpPost("participar")]
    public async Task<ActionResult<IndicacoesStatusDto>> Participar(CancellationToken ct)
    {
        if (tenantContext.ImpersonadoPorId.HasValue) return StatusCode(403, new { message = MensagemImpersonacao });
        try { return Ok(await service.ParticiparIndicacoesAsync(tenantContext.UserId, ct)); }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }

    [HttpGet("escritorios")]
    public async Task<ActionResult<List<EscritorioIndicadoDto>>> Escritorios(CancellationToken ct) =>
        await ParceiroIdAsync(ct) is { } id ? Ok(await service.ListarEscritoriosAsync(id, ct)) : Ok(new List<EscritorioIndicadoDto>());

    [HttpGet("comissoes")]
    public async Task<ActionResult<List<ComissaoParceiroDto>>> Comissoes(CancellationToken ct) =>
        await ParceiroIdAsync(ct) is { } id ? Ok(await service.ListarComissoesAsync(id, ct)) : Ok(new List<ComissaoParceiroDto>());

    [HttpGet("solicitacoes")]
    public async Task<ActionResult<List<SolicitacaoPagamentoDto>>> Solicitacoes(CancellationToken ct) =>
        await ParceiroIdAsync(ct) is { } id ? Ok(await service.ListarSolicitacoesAsync(id, ct: ct)) : Ok(new List<SolicitacaoPagamentoDto>());

    [HttpPost("solicitacoes")]
    public async Task<ActionResult<SolicitacaoPagamentoDto>> Solicitar([FromBody] SolicitarPagamentoDto dto, CancellationToken ct)
    {
        if (tenantContext.ImpersonadoPorId.HasValue) return StatusCode(403, new { message = MensagemImpersonacao });
        if (await ParceiroIdAsync(ct) is not { } id) return BadRequest(new { message = MensagemNaoParticipa });
        try { return Ok(await service.SolicitarPagamentoAsync(id, dto, ct)); }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }

    private Task<Guid?> ParceiroIdAsync(CancellationToken ct) => service.ParceiroDoUsuarioAsync(tenantContext.UserId, ct);
}
