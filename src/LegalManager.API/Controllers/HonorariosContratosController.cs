using LegalManager.Application.DTOs.Honorarios;
using LegalManager.Application.Interfaces;
using LegalManager.Domain;
using LegalManager.Domain.Enums;
using LegalManager.Domain.Interfaces;
using LegalManager.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LegalManager.API.Controllers;

[ApiController]
[Route("api/honorarios/contratos")]
[Authorize]
public class HonorariosContratosController(IHonorarioService service, ITenantContext tenantContext, IAuditService audit) : ControllerBase
{
    private ActionResult? CheckPlano() =>
        !PlanoRestricoes.PermiteHonorariosContratos(tenantContext.Plano)
            ? StatusCode(402, new { message = "Gestão de Honorários disponível a partir do plano Plus." })
            : null;

    [HttpGet("dashboard")]
    public async Task<ActionResult<DashboardHonorariosDto>> GetDashboard(CancellationToken ct)
    {
        if (CheckPlano() is { } err) return err;
        return Ok(await service.GetDashboardAsync(tenantContext.TenantId, ct));
    }

    [HttpGet]
    public async Task<ActionResult<ContratosPagedDto>> GetAll(
        [FromQuery] string? status,
        [FromQuery] Guid? contatoId,
        [FromQuery] Guid? processoId,
        [FromQuery] string? busca,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        if (CheckPlano() is { } err) return err;
        var result = await service.ListarAsync(tenantContext.TenantId,
            new FiltroContratoHonorario(status, contatoId, processoId, busca, page, pageSize), ct);
        return Ok(result);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ContratoHonorarioDto>> GetById(Guid id, CancellationToken ct)
    {
        if (CheckPlano() is { } err) return err;
        var item = await service.ObterAsync(id, tenantContext.TenantId, ct);
        return item == null ? NotFound() : Ok(item);
    }

    [HttpPost]
    public async Task<ActionResult<ContratoHonorarioDto>> Criar([FromBody] CriarContratoHonorarioDto dto, CancellationToken ct)
    {
        if (CheckPlano() is { } err) return err;
        try
        {
            var result = await service.CriarAsync(tenantContext.TenantId, tenantContext.UserId, dto, ct);
            await audit.LogAsync(tenantContext.CreateEntry(AuditActions.Create, AuditEntities.ContratoHonorario, result.Id, null, result), ct);
            return CreatedAtAction(nameof(GetById), new { id = result.Id }, result);
        }
        catch (KeyNotFoundException ex) { return NotFound(new { message = ex.Message }); }
        catch (ArgumentException ex) { return BadRequest(new { message = ex.Message }); }
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<ContratoHonorarioDto>> Atualizar(Guid id, [FromBody] AtualizarContratoHonorarioDto dto, CancellationToken ct)
    {
        if (CheckPlano() is { } err) return err;
        try
        {
            var existing = await service.ObterAsync(id, tenantContext.TenantId, ct);
            var result = await service.AtualizarAsync(id, tenantContext.TenantId, tenantContext.UserId, dto, ct);
            await audit.LogAsync(tenantContext.CreateEntry(AuditActions.Update, AuditEntities.ContratoHonorario, id, existing, result, HttpContext.GetClientIpAddress()), ct);
            return Ok(result);
        }
        catch (KeyNotFoundException ex) { return NotFound(new { message = ex.Message }); }
        catch (ArgumentException ex) { return BadRequest(new { message = ex.Message }); }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Excluir(Guid id, CancellationToken ct)
    {
        if (CheckPlano() is { } err) return err;
        try
        {
            await service.ExcluirAsync(id, tenantContext.TenantId, tenantContext.UserId, ct);
            return NoContent();
        }
        catch (KeyNotFoundException ex) { return NotFound(new { message = ex.Message }); }
    }

    [HttpPost("{id:guid}/suspender")]
    public async Task<ActionResult<ContratoHonorarioDto>> Suspender(Guid id, CancellationToken ct)
    {
        if (CheckPlano() is { } err) return err;
        try { return Ok(await service.SuspenderAsync(id, tenantContext.TenantId, tenantContext.UserId, ct)); }
        catch (KeyNotFoundException ex) { return NotFound(new { message = ex.Message }); }
    }

    [HttpPost("{id:guid}/reativar")]
    public async Task<ActionResult<ContratoHonorarioDto>> Reativar(Guid id, CancellationToken ct)
    {
        if (CheckPlano() is { } err) return err;
        try { return Ok(await service.ReativarAsync(id, tenantContext.TenantId, tenantContext.UserId, ct)); }
        catch (KeyNotFoundException ex) { return NotFound(new { message = ex.Message }); }
    }

    [HttpPost("{id:guid}/distrato")]
    public async Task<ActionResult<ContratoHonorarioDto>> Distrato(Guid id, [FromBody] DistratoContratoDto dto, CancellationToken ct)
    {
        if (CheckPlano() is { } err) return err;
        if (string.IsNullOrWhiteSpace(dto.Motivo))
            return BadRequest(new { message = "Motivo do distrato é obrigatório." });
        try { return Ok(await service.DistratoAsync(id, tenantContext.TenantId, tenantContext.UserId, dto.Motivo, ct)); }
        catch (KeyNotFoundException ex) { return NotFound(new { message = ex.Message }); }
    }

    [HttpGet("{id:guid}/parcelas")]
    public async Task<ActionResult<ParcelasContratoDto>> GetParcelas(Guid id, CancellationToken ct)
    {
        if (CheckPlano() is { } err) return err;
        try { return Ok(await service.ListarParcelasAsync(id, tenantContext.TenantId, ct)); }
        catch (KeyNotFoundException ex) { return NotFound(new { message = ex.Message }); }
    }

    [HttpPost("{id:guid}/parcelas/{parcelaId:guid}/pagar")]
    public async Task<ActionResult<ParcelaHonorarioDto>> Pagar(Guid id, Guid parcelaId, [FromBody] QuitarParcelaDto dto, CancellationToken ct)
    {
        if (CheckPlano() is { } err) return err;
        try
        {
            var result = await service.QuitarParcelaAsync(id, parcelaId, tenantContext.TenantId, dto, ct);
            return Ok(result);
        }
        catch (KeyNotFoundException ex) { return NotFound(new { message = ex.Message }); }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }

    [HttpPost("{id:guid}/parcelas/{parcelaId:guid}/cancelar")]
    public async Task<ActionResult<ParcelaHonorarioDto>> Cancelar(Guid id, Guid parcelaId, [FromBody] CancelarParcelaDto dto, CancellationToken ct)
    {
        if (CheckPlano() is { } err) return err;
        try
        {
            var result = await service.CancelarParcelaAsync(id, parcelaId, tenantContext.TenantId, tenantContext.UserId, dto.Motivo ?? "", ct);
            return Ok(result);
        }
        catch (KeyNotFoundException ex) { return NotFound(new { message = ex.Message }); }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }

    [HttpPost("{id:guid}/parcelas/{parcelaId:guid}/estornar")]
    public async Task<IActionResult> Estornar(Guid id, Guid parcelaId, CancellationToken ct)
    {
        if (CheckPlano() is { } err) return err;
        try
        {
            await service.EstornarPagamentoParcelaAsync(id, parcelaId, tenantContext.TenantId, ct);
            return NoContent();
        }
        catch (KeyNotFoundException ex) { return NotFound(new { message = ex.Message }); }
    }

    [HttpGet("{id:guid}/historico")]
    public async Task<ActionResult<IEnumerable<HistoricoContratoDto>>> GetHistorico(Guid id, CancellationToken ct)
    {
        if (CheckPlano() is { } err) return err;
        try { return Ok(await service.ListarHistoricoAsync(id, tenantContext.TenantId, ct)); }
        catch (KeyNotFoundException ex) { return NotFound(new { message = ex.Message }); }
    }

    [HttpPost("{id:guid}/extrato/pdf")]
    public async Task<IActionResult> GerarExtrato(Guid id, [FromBody] ExtratoPdfRequestDto? dto, CancellationToken ct)
    {
        if (CheckPlano() is { } err) return err;
        try
        {
            var dados = await service.ObterDadosExtratoAsync(id, tenantContext.TenantId, dto, ct);
            var bytes = LegalManager.API.Reports.ExtratoHonorarioPdfRenderer.Renderizar(dados);
            var filename = $"extrato-honorarios-{DateTime.Now:yyyyMMdd-HHmmss}.pdf";
            return File(bytes, "application/pdf", filename);
        }
        catch (KeyNotFoundException ex) { return NotFound(new { message = ex.Message }); }
    }
}

[ApiController]
[Route("api/honorarios/contratos")]
[Authorize]
public class HonorariosConfiguracaoController(IConfiguracaoHonorarioService service, ITenantContext tenantContext) : ControllerBase
{
    private ActionResult? CheckPlano() =>
        !PlanoRestricoes.PermiteHonorariosContratos(tenantContext.Plano)
            ? StatusCode(402, new { message = "Gestão de Honorários disponível a partir do plano Plus." })
            : null;

    [HttpGet("configuracao")]
    public async Task<ActionResult<ConfiguracaoHonorarioDto>> Get(CancellationToken ct)
    {
        if (CheckPlano() is { } err) return err;
        return Ok(await service.ObterOuCriarPadraoAsync(tenantContext.TenantId, ct));
    }

    [HttpPut("configuracao")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<ConfiguracaoHonorarioDto>> Put([FromBody] ConfiguracaoHonorarioDto dto, CancellationToken ct)
    {
        if (CheckPlano() is { } err) return err;
        try
        {
            var result = await service.SalvarAsync(tenantContext.TenantId, dto, ct);
            return Ok(result);
        }
        catch (ArgumentException ex) { return BadRequest(new { message = ex.Message }); }
    }
}
