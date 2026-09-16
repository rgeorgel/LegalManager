using System.Text.Json;
using LegalManager.Application.DTOs.Perguntas;
using LegalManager.Domain.Entities;
using LegalManager.Domain.Enums;
using LegalManager.Domain.Interfaces;
using LegalManager.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LegalManager.API.Controllers;

// Pesquisa de interesse/satisfação — endpoints do usuário final (advogado/equipe do
// escritório OU cliente do portal, ambos autenticados via [Authorize] — o token do
// portal do cliente também carrega tenantId + role "Cliente", ver PortalClienteService).
// Mostradas no login (modal) e no menu flutuante enquanto não respondidas (mesmo padrão
// de ToursController: servidor é fonte da verdade sobre o que já foi respondido).
[ApiController]
[Route("api/perguntas")]
[Authorize]
public class PerguntasController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly ITenantContext _tenantContext;

    public PerguntasController(AppDbContext context, ITenantContext tenantContext)
    {
        _context = context;
        _tenantContext = tenantContext;
    }

    [HttpGet("pendentes")]
    public async Task<ActionResult<List<PerguntaPendenteDto>>> GetPendentes(CancellationToken ct)
    {
        var isCliente = _tenantContext.UserRole == PerfilUsuario.Cliente.ToString();
        var respondenteTipo = isCliente ? TipoRespondente.Cliente : TipoRespondente.Usuario;
        var publicoAlvo = isCliente ? PublicoPergunta.Clientes : PublicoPergunta.Advogados;

        IQueryable<Pergunta> query = _context.Perguntas.Where(p => p.Ativa && p.Publico == publicoAlvo);

        if (!isCliente)
        {
            var tenant = await _context.Tenants.AsNoTracking()
                .FirstOrDefaultAsync(t => t.Id == _tenantContext.TenantId, ct);
            if (tenant == null) return Ok(new List<PerguntaPendenteDto>());

            var pagante = tenant.Status == StatusTenant.Ativo && tenant.Plano != PlanoTipo.Free;
            var emTrial = tenant.Status == StatusTenant.Trial;
            var plano = tenant.Plano;

            query = query.Where(p =>
                p.Segmento == SegmentoPergunta.Todos ||
                (p.Segmento == SegmentoPergunta.PlanoEspecifico && p.PlanoAlvo == plano) ||
                (p.Segmento == SegmentoPergunta.Pagantes && pagante) ||
                (p.Segmento == SegmentoPergunta.EmTrial && emTrial));
        }

        var perguntas = await query.OrderBy(p => p.Ordem).ThenBy(p => p.CriadoEm).ToListAsync(ct);
        if (perguntas.Count == 0) return Ok(new List<PerguntaPendenteDto>());

        var perguntaIds = perguntas.Select(p => p.Id).ToList();
        var respondidasIds = await _context.RespostasPerguntas
            .Where(r => perguntaIds.Contains(r.PerguntaId)
                && r.RespondenteTipo == respondenteTipo
                && r.RespondenteId == _tenantContext.UserId)
            .Select(r => r.PerguntaId)
            .ToListAsync(ct);

        var pendentes = perguntas
            .Where(p => !respondidasIds.Contains(p.Id))
            .Select(p => new PerguntaPendenteDto(p.Id, p.Texto, p.Descricao, p.TipoResposta.ToString(), ParseOpcoes(p.OpcoesJson)))
            .ToList();

        return Ok(pendentes);
    }

    [HttpPost("{id:guid}/responder")]
    public async Task<IActionResult> Responder(Guid id, [FromBody] ResponderPerguntaDto dto, CancellationToken ct)
    {
        var pergunta = await _context.Perguntas.FirstOrDefaultAsync(p => p.Id == id && p.Ativa, ct);
        if (pergunta == null) return NotFound();

        if (pergunta.TipoResposta == TipoRespostaPergunta.EscolhaUnica)
        {
            var opcoes = ParseOpcoes(pergunta.OpcoesJson);
            if (string.IsNullOrWhiteSpace(dto.OpcaoEscolhida) || !opcoes.Contains(dto.OpcaoEscolhida))
                return BadRequest(new { message = "Selecione uma das opções apresentadas." });
        }
        else if (string.IsNullOrWhiteSpace(dto.RespostaTexto))
        {
            return BadRequest(new { message = "Escreva uma resposta." });
        }

        var isCliente = _tenantContext.UserRole == PerfilUsuario.Cliente.ToString();
        var respondenteTipo = isCliente ? TipoRespondente.Cliente : TipoRespondente.Usuario;

        var jaRespondeu = await _context.RespostasPerguntas.AnyAsync(r =>
            r.PerguntaId == id && r.RespondenteTipo == respondenteTipo && r.RespondenteId == _tenantContext.UserId, ct);
        if (jaRespondeu) return NoContent(); // idempotente — evita erro se o usuário reenviar

        _context.RespostasPerguntas.Add(new RespostaPergunta
        {
            Id = Guid.NewGuid(),
            PerguntaId = id,
            TenantId = _tenantContext.TenantId,
            RespondenteTipo = respondenteTipo,
            RespondenteId = _tenantContext.UserId,
            RespostaTexto = string.IsNullOrWhiteSpace(dto.RespostaTexto) ? null : dto.RespostaTexto.Trim(),
            OpcaoEscolhida = dto.OpcaoEscolhida,
            RespondidoEm = DateTime.UtcNow,
        });
        await _context.SaveChangesAsync(ct);

        // 204, não 200: apiFetch/perguntasFetch tratam 204 como sucesso sem corpo;
        // Ok() sem valor volta com content-type vazio, que os dois interpretam como
        // "resposta não-JSON" e lançam erro — mesmo com o POST already persistido
        // (por isso a pergunta sumia só depois de recarregar a página).
        return NoContent();
    }

    private static List<string> ParseOpcoes(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try
        {
            return JsonSerializer.Deserialize<List<string>>(json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
