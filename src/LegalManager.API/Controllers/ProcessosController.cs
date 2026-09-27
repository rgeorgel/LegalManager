using LegalManager.Application.DTOs.Contatos;
using LegalManager.Application.DTOs.Processos;
using LegalManager.Application.Interfaces;
using LegalManager.Domain;
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
    private readonly IContatoResolverService _contatoResolver;
    private readonly IEscavadorService? _escavador;
    private readonly IConsultaExternaLogService? _consultaLog;

    public ProcessosController(
        IProcessoService service,
        IMonitoramentoService monitoramento,
        IAuditService audit,
        ITenantContext tenantContext,
        EsajTjspProcessosAdapter esaj,
        AppDbContext context,
        IContatoResolverService contatoResolver,
        IEscavadorService? escavador = null,
        IConsultaExternaLogService? consultaLog = null)
    {
        _service = service;
        _monitoramento = monitoramento;
        _audit = audit;
        _tenantContext = tenantContext;
        _esaj = esaj;
        _context = context;
        _contatoResolver = contatoResolver;
        _escavador = escavador;
        _consultaLog = consultaLog;
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

    /// <summary>Processos ordenados pelo andamento mais recente, com o resumo desse andamento.</summary>
    [HttpGet("ultimos-andamentos")]
    public async Task<ActionResult<IEnumerable<ProcessoUltimoAndamentoDto>>> GetUltimosAndamentos(
        [FromQuery] int limite = 6, CancellationToken ct = default)
    {
        return Ok(await _service.GetUltimosAndamentosAsync(Math.Clamp(limite, 1, 30), ct));
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

        // Resolução/criação de Contato a partir de Partes encontradas na busca DataJud (preview)
        // acontece só aqui, no Salvar — nunca durante a busca — para não criar Contatos órfãos
        // caso o usuário busque e cancele o modal (docs/features/busca-processo-cadastro-manual.md).
        if (dto.PartesDataJud is { Count: > 0 })
        {
            var jaVinculadas = new HashSet<Guid>((dto.Partes ?? []).Select(p => p.ContatoId));
            var resolvidas = await _contatoResolver.ResolverPartesDataJudAsync(dto.PartesDataJud, ct);
            var adicionouAlguma = false;
            foreach (var parte in resolvidas)
            {
                if (!jaVinculadas.Add(parte.ContatoId)) continue; // já vinculada manualmente ou duplicada na própria busca
                await _service.AdicionarParteAsync(result.Id, parte.ContatoId, parte.TipoParte.ToString(), ct);
                adicionouAlguma = true;
            }

            if (adicionouAlguma)
                result = await _service.GetByIdAsync(result.Id, ct) ?? result;
        }

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
        var encontradoEsaj = false;

        if (processo.Tribunal == "TJSP" || processo.NumeroCNJ.EndsWith("8.26"))
        {
            var numeroCNJDigits = new string(processo.NumeroCNJ.Where(char.IsDigit).ToArray());
            var detalhe = await _esaj.ObterDetalhesAsync(numeroCNJDigits, processo.Grau ?? "G1", ct, null, null);
            encontradoEsaj = detalhe != null;
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

            // Último recurso, pago (R$ 0,05/consulta): só quando nem e-SAJ nem DataJud encontraram
            // o processo — processo encontrado sem andamento novo não gasta com o Escavador.
            if (resultado.Sucesso || encontradoEsaj) return Ok(resultado);

            var novosEscavador = await ImportarAndamentosEscavadorAsync(id, ct);
            if (novosEscavador == 0) return Ok(resultado);

            return Ok(new { Id = id, NumeroCNJ = processo.NumeroCNJ, Sucesso = true, NovosAndamentos = novosEscavador, Mensagem = $"{novosEscavador} novo(s) andamento(s) importado(s) do Escavador." });
        }

        return Ok(new { Id = id, NumeroCNJ = processo.NumeroCNJ, Sucesso = true, NovosAndamentos = novos, Mensagem = $"{novos} novo(s) andamento(s) importado(s) do ESAJ." });
    }

    /// <summary>
    /// Fallback do "Consultar" (plano pago; nunca no Free): importa do Escavador só as movimentações posteriores ao último
    /// andamento já gravado. O Escavador não compartilha chave nem texto com DataJud/e-SAJ, então
    /// comparar por descrição duplicaria o histórico inteiro — o corte por data evita isso.
    /// </summary>
    private async Task<int> ImportarAndamentosEscavadorAsync(Guid id, CancellationToken ct)
    {
        if (_escavador == null || _consultaLog == null) return 0;
        if (!PlanoRestricoes.PermiteBuscaExternaProcesso(_tenantContext.Plano)) return 0;

        var processo = await _context.Processos.FirstOrDefaultAsync(p => p.Id == id && p.TenantId == _tenantContext.TenantId, ct);
        if (processo == null) return 0;

        var resultado = await _consultaLog.RegistrarAsync(
            "Escavador", "BuscaMovimentacoesPorCnj", "Processos — Consultar andamentos (fallback Escavador)",
            new { cnj = processo.NumeroCNJ },
            () => _escavador.ListarMovimentacoesPorProcessoAsync(processo.NumeroCNJ, desde: null, pagina: 1, ct: ct),
            r => (r.Data.Count, r.Data.Select(m => new { m.Data, m.Tipo, m.Diario, m.Snippet })),
            ct: ct);
        if (resultado.Data.Count == 0) return 0;

        var ultimaData = await _context.Andamentos
            .Where(a => a.ProcessoId == id)
            .Select(a => (DateTime?)a.Data)
            .MaxAsync(ct);

        var agora = DateTime.UtcNow;
        var novos = resultado.Data
            .Where(m => m.Data.HasValue && (ultimaData == null || m.Data.Value > ultimaData.Value))
            .Select(m => new { Data = m.Data!.Value, Descricao = ResumirConteudoEscavador(m.Snippet ?? m.ConteudoHtml), m.Tipo })
            .DistinctBy(m => (m.Data, m.Descricao))
            .Select(m => new Andamento
            {
                Id = Guid.NewGuid(),
                ProcessoId = id,
                TenantId = _tenantContext.TenantId,
                Data = m.Data,
                Tipo = MapearTipoEscavador(m.Tipo, m.Descricao),
                Descricao = m.Descricao,
                Fonte = FonteAndamento.Automatico,
                CriadoEm = agora,
                VisivelCliente = true
            }).ToList();

        if (novos.Count == 0) return 0;

        _context.Andamentos.AddRange(novos);
        processo.UltimoMonitoramento = agora;
        await _context.SaveChangesAsync(ct);
        return novos.Count;
    }

    private static string ResumirConteudoEscavador(string? html)
    {
        if (string.IsNullOrWhiteSpace(html)) return "Movimentação via Escavador";
        var semTags = System.Text.RegularExpressions.Regex.Replace(html, "<[^>]+>", " ");
        var compacto = System.Text.RegularExpressions.Regex.Replace(semTags, @"\s+", " ").Trim();
        return compacto.Length > 2000 ? compacto[..2000] + "…" : compacto;
    }

    private static TipoAndamento MapearTipoEscavador(string? tipo, string? conteudo)
    {
        var texto = (tipo + " " + conteudo).ToLowerInvariant();
        return texto switch
        {
            var s when s.Contains("senten") => TipoAndamento.Sentenca,
            var s when s.Contains("acórd") || s.Contains("acord") => TipoAndamento.Acordao,
            var s when s.Contains("decis") => TipoAndamento.Decisao,
            var s when s.Contains("despacho") => TipoAndamento.Despacho,
            var s when s.Contains("audiên") || s.Contains("audien") => TipoAndamento.Audiencia,
            var s when s.Contains("intim") => TipoAndamento.Intimacao,
            var s when s.Contains("public") => TipoAndamento.Publicacao,
            var s when s.Contains("petiç") || s.Contains("petic") => TipoAndamento.Peticao,
            _ => TipoAndamento.Outro
        };
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
