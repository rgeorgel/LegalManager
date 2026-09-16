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

        var gruposNomes = await GetGruposNomesAsync(perguntas, ct);

        return Ok(perguntas.Select(p => ToDto(p, contagens.GetValueOrDefault(p.Id), gruposNomes.GetValueOrDefault(p.GrupoId ?? Guid.Empty))).ToList());
    }

    [HttpPost]
    public async Task<ActionResult<PerguntaAdminDto>> Criar([FromBody] SalvarPerguntaDto dto, CancellationToken ct)
    {
        var v = await ValidarAsync(dto, ct);
        if (v.Erro != null) return BadRequest(new { message = v.Erro });

        var pergunta = new Pergunta
        {
            Id = Guid.NewGuid(),
            Texto = dto.Texto.Trim(),
            Descricao = string.IsNullOrWhiteSpace(dto.Descricao) ? null : dto.Descricao.Trim(),
            TipoResposta = v.TipoResposta,
            OpcoesJson = v.OpcoesJson,
            Publico = v.Publico,
            Segmento = v.Publico == PublicoPergunta.Clientes ? SegmentoPergunta.Todos : v.Segmento,
            PlanoAlvo = v.Publico == PublicoPergunta.Clientes ? null : v.PlanoAlvo,
            GrupoId = v.Publico == PublicoPergunta.Clientes ? null : v.GrupoId,
            Ativa = dto.Ativa,
            Ordem = dto.Ordem,
            CriadoEm = DateTime.UtcNow,
            CriadoPorId = GetSuperAdminId(),
        };

        _context.Perguntas.Add(pergunta);
        await _context.SaveChangesAsync(ct);

        var grupoNome = pergunta.GrupoId.HasValue
            ? await _context.GruposPergunta.Where(g => g.Id == pergunta.GrupoId).Select(g => g.Nome).FirstOrDefaultAsync(ct)
            : null;
        return Ok(ToDto(pergunta, 0, grupoNome));
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<PerguntaAdminDto>> Atualizar(Guid id, [FromBody] SalvarPerguntaDto dto, CancellationToken ct)
    {
        var pergunta = await _context.Perguntas.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (pergunta == null) return NotFound();

        var v = await ValidarAsync(dto, ct);
        if (v.Erro != null) return BadRequest(new { message = v.Erro });

        pergunta.Texto = dto.Texto.Trim();
        pergunta.Descricao = string.IsNullOrWhiteSpace(dto.Descricao) ? null : dto.Descricao.Trim();
        pergunta.TipoResposta = v.TipoResposta;
        pergunta.OpcoesJson = v.OpcoesJson;
        pergunta.Publico = v.Publico;
        pergunta.Segmento = v.Publico == PublicoPergunta.Clientes ? SegmentoPergunta.Todos : v.Segmento;
        pergunta.PlanoAlvo = v.Publico == PublicoPergunta.Clientes ? null : v.PlanoAlvo;
        pergunta.GrupoId = v.Publico == PublicoPergunta.Clientes ? null : v.GrupoId;
        pergunta.Ativa = dto.Ativa;
        pergunta.Ordem = dto.Ordem;
        pergunta.AtualizadoEm = DateTime.UtcNow;

        await _context.SaveChangesAsync(ct);

        var total = await _context.RespostasPerguntas.CountAsync(r => r.PerguntaId == id, ct);
        var grupoNome = pergunta.GrupoId.HasValue
            ? await _context.GruposPergunta.Where(g => g.Id == pergunta.GrupoId).Select(g => g.Nome).FirstOrDefaultAsync(ct)
            : null;
        return Ok(ToDto(pergunta, total, grupoNome));
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
                r.RespostaTexto, r.OpcaoEscolhida, ParseOpcoes(r.OpcoesEscolhidasJson), r.RespondidoEm);
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
        else if (pergunta.TipoResposta == TipoRespostaPergunta.EscolhaMultipla)
        {
            contagens = respostas
                .SelectMany(r => ParseOpcoes(r.OpcoesEscolhidasJson))
                .GroupBy(o => o)
                .Select(g => new OpcaoContagemDto(g.Key, g.Count()))
                .OrderByDescending(c => c.Total)
                .ToList();
        }

        var grupoNome = pergunta.GrupoId.HasValue
            ? await _context.GruposPergunta.Where(g => g.Id == pergunta.GrupoId).Select(g => g.Nome).FirstOrDefaultAsync(ct)
            : null;
        return Ok(new PerguntaComRespostasDto(ToDto(pergunta, respostas.Count, grupoNome), contagens, respostasDto));
    }

    // Async não pode ter parâmetros `out` (CS1988) — os campos parseados voltam num
    // record em vez de out params, mas a checagem de erro no chamador continua igual
    // (`if (resultado.Erro != null) ...`).
    private record ValidacaoPergunta(
        string? Erro,
        TipoRespostaPergunta TipoResposta,
        PublicoPergunta Publico,
        SegmentoPergunta Segmento,
        PlanoTipo? PlanoAlvo,
        Guid? GrupoId,
        string? OpcoesJson);

    private async Task<ValidacaoPergunta> ValidarAsync(SalvarPerguntaDto dto, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(dto.Texto))
            return new ValidacaoPergunta("Informe o texto da pergunta.", default, default, default, null, null, null);
        if (!Enum.TryParse<TipoRespostaPergunta>(dto.TipoResposta, out var tipoResposta))
            return new ValidacaoPergunta("Tipo de resposta inválido.", default, default, default, null, null, null);
        if (!Enum.TryParse<PublicoPergunta>(dto.Publico, out var publico))
            return new ValidacaoPergunta("Público-alvo inválido.", default, default, default, null, null, null);
        if (!Enum.TryParse<SegmentoPergunta>(dto.Segmento, out var segmento))
            return new ValidacaoPergunta("Segmento inválido.", default, default, default, null, null, null);

        string? opcoesJson = null;
        if (tipoResposta == TipoRespostaPergunta.EscolhaUnica || tipoResposta == TipoRespostaPergunta.EscolhaMultipla)
        {
            var opcoes = (dto.Opcoes ?? []).Select(o => o.Trim()).Where(o => o.Length > 0).Distinct().ToList();
            if (opcoes.Count < 2)
                return new ValidacaoPergunta("Cadastre ao menos duas opções para perguntas de escolha única ou múltipla.", default, default, default, null, null, null);
            opcoesJson = JsonSerializer.Serialize(opcoes);
        }

        PlanoTipo? planoAlvo = null;
        if (segmento == SegmentoPergunta.PlanoEspecifico)
        {
            if (string.IsNullOrWhiteSpace(dto.PlanoAlvo) || !Enum.TryParse<PlanoTipo>(dto.PlanoAlvo, out var plano))
                return new ValidacaoPergunta("Selecione o plano-alvo.", default, default, default, null, null, null);
            planoAlvo = plano;
        }

        Guid? grupoId = null;
        if (segmento == SegmentoPergunta.GrupoEspecifico)
        {
            if (!dto.GrupoId.HasValue)
                return new ValidacaoPergunta("Selecione o grupo de usuários.", default, default, default, null, null, null);
            if (!await _context.GruposPergunta.AnyAsync(g => g.Id == dto.GrupoId.Value, ct))
                return new ValidacaoPergunta("Grupo de usuários não encontrado.", default, default, default, null, null, null);
            grupoId = dto.GrupoId;
        }

        return new ValidacaoPergunta(null, tipoResposta, publico, segmento, planoAlvo, grupoId, opcoesJson);
    }

    private async Task<Dictionary<Guid, string>> GetGruposNomesAsync(List<Pergunta> perguntas, CancellationToken ct)
    {
        var grupoIds = perguntas.Where(p => p.GrupoId.HasValue).Select(p => p.GrupoId!.Value).Distinct().ToList();
        if (grupoIds.Count == 0) return new Dictionary<Guid, string>();

        return await _context.GruposPergunta
            .Where(g => grupoIds.Contains(g.Id))
            .ToDictionaryAsync(g => g.Id, g => g.Nome, ct);
    }

    private static PerguntaAdminDto ToDto(Pergunta p, int totalRespostas, string? grupoNome = null) => new(
        p.Id, p.Texto, p.Descricao, p.TipoResposta.ToString(), ParseOpcoes(p.OpcoesJson),
        p.Publico.ToString(), p.Segmento.ToString(), p.PlanoAlvo?.ToString(), p.GrupoId, grupoNome,
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
