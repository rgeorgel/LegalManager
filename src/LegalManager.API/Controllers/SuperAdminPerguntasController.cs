using System.Security.Claims;
using System.Text.Json;
using LegalManager.Application.DTOs.Perguntas;
using LegalManager.Domain.Entities;
using LegalManager.Domain.Enums;
using LegalManager.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LegalManager.API.Controllers;

// Pesquisa de interesse/satisfação — cadastro e apuração de respostas pelo super admin.
// Perguntas são uma entidade global (não pertencem a um tenant): quem gerencia é o time
// Causify, para entender interesses/satisfação de todos os tenants.
[ApiController]
[Route("api/superadmin/perguntas")]
[Authorize(Roles = "SuperAdmin")]
public class SuperAdminPerguntasController : ControllerBase
{
    private readonly AppDbContext _context;

    public SuperAdminPerguntasController(AppDbContext context)
    {
        _context = context;
    }

    [HttpGet]
    public async Task<ActionResult<List<PerguntaAdminDto>>> Listar(CancellationToken ct)
    {
        var perguntas = await _context.Perguntas
            .OrderBy(p => p.Ordem).ThenByDescending(p => p.CriadoEm)
            .ToListAsync(ct);

        var ids = perguntas.Select(p => p.Id).ToList();
        var contagens = await _context.RespostasPerguntas
            .Where(r => ids.Contains(r.PerguntaId))
            .GroupBy(r => r.PerguntaId)
            .Select(g => new { PerguntaId = g.Key, Total = g.Count() })
            .ToDictionaryAsync(g => g.PerguntaId, g => g.Total, ct);

        return Ok(perguntas.Select(p => ToDto(p, contagens.GetValueOrDefault(p.Id))).ToList());
    }

    [HttpPost]
    public async Task<ActionResult<PerguntaAdminDto>> Criar([FromBody] SalvarPerguntaDto dto, CancellationToken ct)
    {
        var erro = Validar(dto, out var tipoResposta, out var publico, out var segmento, out var planoAlvo, out var opcoes);
        if (erro != null) return BadRequest(new { message = erro });

        var pergunta = new Pergunta
        {
            Id = Guid.NewGuid(),
            Texto = dto.Texto.Trim(),
            Descricao = string.IsNullOrWhiteSpace(dto.Descricao) ? null : dto.Descricao.Trim(),
            TipoResposta = tipoResposta,
            OpcoesJson = opcoes,
            Publico = publico,
            Segmento = publico == PublicoPergunta.Clientes ? SegmentoPergunta.Todos : segmento,
            PlanoAlvo = publico == PublicoPergunta.Clientes ? null : planoAlvo,
            Ativa = dto.Ativa,
            Ordem = dto.Ordem,
            CriadoEm = DateTime.UtcNow,
            CriadoPorId = GetSuperAdminId(),
        };

        _context.Perguntas.Add(pergunta);
        await _context.SaveChangesAsync(ct);

        return Ok(ToDto(pergunta, 0));
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<PerguntaAdminDto>> Atualizar(Guid id, [FromBody] SalvarPerguntaDto dto, CancellationToken ct)
    {
        var pergunta = await _context.Perguntas.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (pergunta == null) return NotFound();

        var erro = Validar(dto, out var tipoResposta, out var publico, out var segmento, out var planoAlvo, out var opcoes);
        if (erro != null) return BadRequest(new { message = erro });

        pergunta.Texto = dto.Texto.Trim();
        pergunta.Descricao = string.IsNullOrWhiteSpace(dto.Descricao) ? null : dto.Descricao.Trim();
        pergunta.TipoResposta = tipoResposta;
        pergunta.OpcoesJson = opcoes;
        pergunta.Publico = publico;
        pergunta.Segmento = publico == PublicoPergunta.Clientes ? SegmentoPergunta.Todos : segmento;
        pergunta.PlanoAlvo = publico == PublicoPergunta.Clientes ? null : planoAlvo;
        pergunta.Ativa = dto.Ativa;
        pergunta.Ordem = dto.Ordem;
        pergunta.AtualizadoEm = DateTime.UtcNow;

        await _context.SaveChangesAsync(ct);

        var total = await _context.RespostasPerguntas.CountAsync(r => r.PerguntaId == id, ct);
        return Ok(ToDto(pergunta, total));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Excluir(Guid id, CancellationToken ct)
    {
        var pergunta = await _context.Perguntas.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (pergunta == null) return NotFound();

        var respostas = await _context.RespostasPerguntas.Where(r => r.PerguntaId == id).ToListAsync(ct);
        _context.RespostasPerguntas.RemoveRange(respostas);
        _context.Perguntas.Remove(pergunta);
        await _context.SaveChangesAsync(ct);

        return NoContent();
    }

    [HttpGet("{id:guid}/respostas")]
    public async Task<ActionResult<PerguntaComRespostasDto>> Respostas(Guid id, CancellationToken ct)
    {
        var pergunta = await _context.Perguntas.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (pergunta == null) return NotFound();

        var respostas = await _context.RespostasPerguntas
            .Where(r => r.PerguntaId == id)
            .OrderByDescending(r => r.RespondidoEm)
            .ToListAsync(ct);

        var tenantIds = respostas.Select(r => r.TenantId).Distinct().ToList();
        var tenants = await _context.Tenants.AsNoTracking()
            .Where(t => tenantIds.Contains(t.Id))
            .ToDictionaryAsync(t => t.Id, t => t.Nome, ct);

        var usuarioIds = respostas.Where(r => r.RespondenteTipo == TipoRespondente.Usuario)
            .Select(r => r.RespondenteId).Distinct().ToList();
        var usuarios = await _context.Users.AsNoTracking()
            .Where(u => usuarioIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => new { u.Nome, u.Email }, ct);

        var clienteIds = respostas.Where(r => r.RespondenteTipo == TipoRespondente.Cliente)
            .Select(r => r.RespondenteId).Distinct().ToList();
        var clientes = await _context.AcessosCliente.AsNoTracking()
            .Include(a => a.Contato)
            .Where(a => clienteIds.Contains(a.Id))
            .ToDictionaryAsync(a => a.Id, a => new { Nome = a.Contato.Nome, a.Email }, ct);

        var respostasDto = respostas.Select(r =>
        {
            var nome = "—";
            string? email = null;

            if (r.RespondenteTipo == TipoRespondente.Usuario && usuarios.TryGetValue(r.RespondenteId, out var u))
            {
                nome = u.Nome;
                email = u.Email;
            }
            else if (r.RespondenteTipo == TipoRespondente.Cliente && clientes.TryGetValue(r.RespondenteId, out var c))
            {
                nome = c.Nome;
                email = c.Email;
            }

            return new RespostaAdminDto(
                r.Id, nome, email, r.RespondenteTipo.ToString(),
                tenants.GetValueOrDefault(r.TenantId, "—"),
                r.RespostaTexto, r.OpcaoEscolhida, r.RespondidoEm);
        }).ToList();

        var contagens = new List<OpcaoContagemDto>();
        if (pergunta.TipoResposta == TipoRespostaPergunta.EscolhaUnica)
        {
            contagens = respostas
                .Where(r => r.OpcaoEscolhida != null)
                .GroupBy(r => r.OpcaoEscolhida!)
                .Select(g => new OpcaoContagemDto(g.Key, g.Count()))
                .OrderByDescending(c => c.Total)
                .ToList();
        }

        return Ok(new PerguntaComRespostasDto(ToDto(pergunta, respostas.Count), contagens, respostasDto));
    }

    private static string? Validar(
        SalvarPerguntaDto dto,
        out TipoRespostaPergunta tipoResposta,
        out PublicoPergunta publico,
        out SegmentoPergunta segmento,
        out PlanoTipo? planoAlvo,
        out string? opcoesJson)
    {
        tipoResposta = default;
        publico = default;
        segmento = default;
        planoAlvo = null;
        opcoesJson = null;

        if (string.IsNullOrWhiteSpace(dto.Texto)) return "Informe o texto da pergunta.";
        if (!Enum.TryParse(dto.TipoResposta, out tipoResposta)) return "Tipo de resposta inválido.";
        if (!Enum.TryParse(dto.Publico, out publico)) return "Público-alvo inválido.";
        if (!Enum.TryParse(dto.Segmento, out segmento)) return "Segmento inválido.";

        if (tipoResposta == TipoRespostaPergunta.EscolhaUnica)
        {
            var opcoes = (dto.Opcoes ?? []).Select(o => o.Trim()).Where(o => o.Length > 0).Distinct().ToList();
            if (opcoes.Count < 2) return "Cadastre ao menos duas opções para perguntas de escolha única.";
            opcoesJson = JsonSerializer.Serialize(opcoes);
        }

        if (segmento == SegmentoPergunta.PlanoEspecifico)
        {
            if (string.IsNullOrWhiteSpace(dto.PlanoAlvo) || !Enum.TryParse<PlanoTipo>(dto.PlanoAlvo, out var plano))
                return "Selecione o plano-alvo.";
            planoAlvo = plano;
        }

        return null;
    }

    private static PerguntaAdminDto ToDto(Pergunta p, int totalRespostas) => new(
        p.Id, p.Texto, p.Descricao, p.TipoResposta.ToString(), ParseOpcoes(p.OpcoesJson),
        p.Publico.ToString(), p.Segmento.ToString(), p.PlanoAlvo?.ToString(),
        p.Ativa, p.Ordem, p.CriadoEm, totalRespostas);

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

    private Guid GetSuperAdminId() =>
        Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : Guid.Empty;
}
