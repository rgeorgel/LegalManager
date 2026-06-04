using LegalManager.Application.DTOs.Contatos;
using LegalManager.Application.DTOs.Processos;
using LegalManager.Application.Interfaces;
using LegalManager.Domain.Entities;
using LegalManager.Domain.Enums;
using LegalManager.Domain.Interfaces;
using LegalManager.Infrastructure.Persistence;
using LegalManager.Infrastructure.Services;
using LegalManager.Infrastructure.Tribunais;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

// ReSharper disable RouteTemplates.ActionRoutePrefixCanBeExtractedToControllerRoute

namespace LegalManager.API.Controllers;

[ApiController]
[Route("api/processos")]
[Authorize]
public class ProcessosController : ControllerBase
{
    private readonly IProcessoService _service;
    private readonly IMonitoramentoService _monitoramento;
    private readonly IAuditService _audit;
    private readonly ITenantContext _tenantContext;
    private readonly EsajTjspProcessosAdapter _esaj;
    private readonly AppDbContext _context;

    public ProcessosController(
        IProcessoService service,
        IMonitoramentoService monitoramento,
        IAuditService audit,
        ITenantContext tenantContext,
        EsajTjspProcessosAdapter esaj,
        AppDbContext context)
    {
        _service = service;
        _monitoramento = monitoramento;
        _audit = audit;
        _tenantContext = tenantContext;
        _esaj = esaj;
        _context = context;
    }

    [HttpGet]
    public async Task<ActionResult<PagedResultDto<ProcessoListItemDto>>> GetAll(
        [FromQuery] string? busca,
        [FromQuery] string? status,
        [FromQuery] string? areaDireito,
        [FromQuery] Guid? advogadoResponsavelId,
        [FromQuery] Guid? contatoId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        var filtro = new ProcessoFiltroDto(
            busca,
            status != null && Enum.TryParse<StatusProcesso>(status, true, out var s) ? s : null,
            areaDireito != null && Enum.TryParse<AreaDireito>(areaDireito, true, out var a) ? a : null,
            advogadoResponsavelId,
            contatoId,
            page, pageSize);

        return Ok(await _service.GetAllAsync(filtro, ct));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ProcessoResponseDto>> GetById(Guid id, CancellationToken ct)
    {
        var result = await _service.GetByIdAsync(id, ct);
        return result == null ? NotFound() : Ok(result);
    }

    [HttpGet("{id:guid}/andamentos")]
    public async Task<ActionResult<IEnumerable<AndamentoResponseDto>>> GetAndamentos(Guid id, CancellationToken ct)
    {
        try
        {
            var result = await _service.GetAndamentosAsync(id, ct);
            return Ok(result);
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (OperationCanceledException)
        {
            return StatusCode(499);
        }
    }

[HttpPost]
    public async Task<ActionResult<ProcessoResponseDto>> Create(CreateProcessoDto dto, CancellationToken ct)
    {
        var result = await _service.CreateAsync(dto, ct);
        await _audit.LogAsync(_tenantContext.CreateEntry(AuditActions.Create, AuditEntities.Processo, result.Id, null, dto), ct);
        return CreatedAtAction(nameof(GetById), new { id = result.Id }, result);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<ProcessoResponseDto>> Update(Guid id, UpdateProcessoDto dto, CancellationToken ct)
    {
        try
        {
            var existing = await _service.GetByIdAsync(id, ct);
            var result = await _service.UpdateAsync(id, dto, ct);
            await _audit.LogAsync(_tenantContext.CreateEntry(AuditActions.Update, AuditEntities.Processo, id, existing, result, HttpContext.GetClientIpAddress()), ct);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = "monitoramento_error", message = ex.Message });
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
    }

    [HttpPost("{id:guid}/encerrar")]
    public async Task<IActionResult> Encerrar(Guid id, EncerrarProcessoDto dto, CancellationToken ct)
    {
        var existing = await _service.GetByIdAsync(id, ct);
        await _service.EncerrarAsync(id, dto, ct);
        await _audit.LogAsync(_tenantContext.CreateEntry(AuditActions.Update, AuditEntities.Processo, id, existing, dto, HttpContext.GetClientIpAddress()), ct);
        return NoContent();
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Roles = "Admin,Advogado")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var existing = await _service.GetByIdAsync(id, ct);
        await _service.DeleteAsync(id, ct);
        await _audit.LogAsync(_tenantContext.CreateEntry(AuditActions.Delete, AuditEntities.Processo, id, existing, null, HttpContext.GetClientIpAddress()), ct);
        return NoContent();
    }

    [HttpPost("{id:guid}/andamentos")]
    public async Task<ActionResult<AndamentoResponseDto>> AddAndamento(Guid id, CreateAndamentoDto dto, CancellationToken ct)
    {
        var result = await _service.AddAndamentoAsync(id, dto, ct);
        await _audit.LogAsync(_tenantContext.CreateEntry(AuditActions.Create, AuditEntities.Processo + ".Andamento", result.Id, null, dto), ct);
        return Ok(result);
    }

    [HttpDelete("{id:guid}/andamentos/{andamentoId:guid}")]
    public async Task<IActionResult> DeleteAndamento(Guid id, Guid andamentoId, CancellationToken ct)
    {
        await _service.DeleteAndamentoAsync(id, andamentoId, ct);
        return NoContent();
    }

    [HttpPatch("{id:guid}/andamentos/{andamentoId:guid}/visivel-cliente")]
    public async Task<ActionResult<AndamentoResponseDto>> SetAndamentoVisivelCliente(Guid id, Guid andamentoId, [FromBody] SetVisivelClienteDto dto, CancellationToken ct)
    {
        var result = await _service.SetAndamentoVisivelClienteAsync(id, andamentoId, dto.Visivel, ct);
        return Ok(result);
    }

    [HttpPost("{id:guid}/monitoramento/alternar")]
    public async Task<IActionResult> AlternarMonitoramento(Guid id, CancellationToken ct)
    {
        var ativo = await _monitoramento.AlternarMonitoramentoAsync(id, ct);
        return Ok(new { monitorado = ativo });
    }

    [HttpPost("{id:guid}/monitoramento/executar")]
    [Authorize(Roles = "Admin,Advogado")]
    public async Task<IActionResult> ExecutarMonitoramento(Guid id, CancellationToken ct)
    {
        var processo = await _service.GetByIdAsync(id, ct);
        if (processo == null) return NotFound("Processo não encontrado.");

        int novos = 0;

        if (processo.Tribunal == "TJSP" || processo.NumeroCNJ.EndsWith("8.26"))
        {
            var numeroCNJDigits = new string(processo.NumeroCNJ.Where(char.IsDigit).ToArray());
            var detalhe = await _esaj.ObterDetalhesAsync(numeroCNJDigits, processo.Grau ?? "G1", ct, null, null);
            if (detalhe != null && !detalhe.Sigiloso && detalhe.Movimentos.Count > 0)
            {
                var processoEntity = await _context.Processos.FirstOrDefaultAsync(p => p.Id == id && p.TenantId == _tenantContext.TenantId, ct);
                if (processoEntity != null)
                {
                    var existentes = await _context.Andamentos
                        .Where(a => a.ProcessoId == id)
                        .Select(a => new { a.Data, a.Descricao })
                        .ToListAsync(ct);

                    var andamentosImportados = detalhe.Movimentos
                        .Where(m => m.Data.HasValue)
                        .Select(m => new
                        {
                            Data = m.Data,
                            Descricao = string.IsNullOrEmpty(m.Complemento) ? m.Titulo : $"{m.Titulo} — {m.Complemento}",
                            OrgaoJulgador = m.OrgaoJulgador
                        })
                        .Where(x => x.Data.HasValue && !existentes.Any(e => e.Data == x.Data.Value && e.Descricao == x.Descricao))
                        .Select(x => new Andamento
                        {
                            Id = Guid.NewGuid(),
                            ProcessoId = id,
                            TenantId = _tenantContext.TenantId,
                            Data = x.Data ?? DateTime.UtcNow,
                            Tipo = Domain.Enums.TipoAndamento.Outro,
                            Descricao = x.Descricao,
                            Fonte = Domain.Enums.FonteAndamento.Automatico,
                            CriadoEm = DateTime.UtcNow,
                            OrgaoJulgador = x.OrgaoJulgador,
                            VisivelCliente = true
                        }).ToList();

                    if (andamentosImportados.Count > 0)
                    {
                        _context.Andamentos.AddRange(andamentosImportados);
                        await _context.SaveChangesAsync(ct);
                    }
                    novos = andamentosImportados.Count;
                }
            }
        }

        if (novos == 0)
        {
            var resultado = await _monitoramento.MonitorarProcessoAsync(id, ct);
            return Ok(resultado);
        }

        return Ok(new { Id = id, NumeroCNJ = processo.NumeroCNJ, Sucesso = true, NovosAndamentos = novos, Mensagem = $"{novos} novo(s) andamento(s) importado(s) do ESAJ." });
    }

    [HttpPost("{id:guid}/partes")]
    public async Task<IActionResult> AdicionarParte(Guid id, [FromBody] AdicionarParteDto dto, CancellationToken ct)
    {
        await _service.AdicionarParteAsync(id, dto.ContatoId, dto.TipoParte, ct);
        return Ok(new { message = "Parte adicionada." });
    }

    [HttpDelete("{id:guid}/partes/{contatoId:guid}")]
    public async Task<IActionResult> RemoverParte(Guid id, Guid contatoId, CancellationToken ct)
    {
        await _service.RemoverParteAsync(id, contatoId, ct);
        return NoContent();
    }
}
