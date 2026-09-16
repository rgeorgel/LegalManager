using System.Security.Claims;
using LegalManager.Application.DTOs.Grupos;
using LegalManager.Domain.Entities;
using LegalManager.Domain.Enums;
using LegalManager.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LegalManager.API.Controllers;

// Grupos de usuários usados para segmentar Perguntas de pesquisa (Segmento ==
// GrupoEspecifico) além dos critérios automáticos (plano, pagante, trial). A busca de
// usuários para adicionar como membro reaproveita o SuperAdminController existente
// (GET /api/superadmin/users?search=...) — não duplicada aqui.
[ApiController]
[Route("api/superadmin/grupos")]
[Authorize(Roles = "SuperAdmin")]
public class SuperAdminGruposController : ControllerBase
{
    private readonly AppDbContext _context;

    public SuperAdminGruposController(AppDbContext context)
    {
        _context = context;
    }

    [HttpGet]
    public async Task<ActionResult<List<GrupoDto>>> Listar(CancellationToken ct)
    {
        var grupos = await _context.GruposPergunta
            .OrderByDescending(g => g.CriadoEm)
            .ToListAsync(ct);

        var ids = grupos.Select(g => g.Id).ToList();
        var contagens = await _context.GruposPerguntaMembros
            .Where(m => ids.Contains(m.GrupoId))
            .GroupBy(m => m.GrupoId)
            .Select(g => new { GrupoId = g.Key, Total = g.Count() })
            .ToDictionaryAsync(g => g.GrupoId, g => g.Total, ct);

        return Ok(grupos.Select(g => new GrupoDto(
            g.Id, g.Nome, g.Descricao, contagens.GetValueOrDefault(g.Id), g.CriadoEm)).ToList());
    }

    [HttpPost]
    public async Task<ActionResult<GrupoDto>> Criar([FromBody] SalvarGrupoDto dto, CancellationToken ct)
    {
        var grupo = new GrupoPergunta
        {
            Id = Guid.NewGuid(),
            Nome = dto.Nome.Trim(),
            Descricao = string.IsNullOrWhiteSpace(dto.Descricao) ? null : dto.Descricao.Trim(),
            CriadoEm = DateTime.UtcNow,
            CriadoPorId = GetSuperAdminId(),
        };

        _context.GruposPergunta.Add(grupo);
        await _context.SaveChangesAsync(ct);

        return Ok(new GrupoDto(grupo.Id, grupo.Nome, grupo.Descricao, 0, grupo.CriadoEm));
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<GrupoDto>> Atualizar(Guid id, [FromBody] SalvarGrupoDto dto, CancellationToken ct)
    {
        var grupo = await _context.GruposPergunta.FirstOrDefaultAsync(g => g.Id == id, ct);
        if (grupo == null) return NotFound();

        grupo.Nome = dto.Nome.Trim();
        grupo.Descricao = string.IsNullOrWhiteSpace(dto.Descricao) ? null : dto.Descricao.Trim();
        grupo.AtualizadoEm = DateTime.UtcNow;
        await _context.SaveChangesAsync(ct);

        var total = await _context.GruposPerguntaMembros.CountAsync(m => m.GrupoId == id, ct);
        return Ok(new GrupoDto(grupo.Id, grupo.Nome, grupo.Descricao, total, grupo.CriadoEm));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Excluir(Guid id, CancellationToken ct)
    {
        var grupo = await _context.GruposPergunta.FirstOrDefaultAsync(g => g.Id == id, ct);
        if (grupo == null) return NotFound();

        var membros = await _context.GruposPerguntaMembros.Where(m => m.GrupoId == id).ToListAsync(ct);
        _context.GruposPerguntaMembros.RemoveRange(membros);
        _context.GruposPergunta.Remove(grupo);

        // Perguntas que apontavam pra este grupo voltam a "Todos" em vez de ficar com uma
        // referência morta (o que as faria silenciosamente parar de alcançar qualquer um).
        var perguntas = await _context.Perguntas.Where(p => p.GrupoId == id).ToListAsync(ct);
        foreach (var pergunta in perguntas)
        {
            pergunta.Segmento = SegmentoPergunta.Todos;
            pergunta.GrupoId = null;
        }

        await _context.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpGet("{id:guid}/membros")]
    public async Task<ActionResult<List<GrupoMembroDto>>> Membros(Guid id, CancellationToken ct)
    {
        var grupo = await _context.GruposPergunta.FirstOrDefaultAsync(g => g.Id == id, ct);
        if (grupo == null) return NotFound();

        var membros = await _context.GruposPerguntaMembros
            .Where(m => m.GrupoId == id)
            .OrderByDescending(m => m.AdicionadoEm)
            .ToListAsync(ct);

        var usuarioIds = membros.Select(m => m.UsuarioId).ToList();
        var usuarios = await _context.Users.Include(u => u.Tenant)
            .Where(u => usuarioIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, ct);

        var dtos = membros.Select(m =>
        {
            usuarios.TryGetValue(m.UsuarioId, out var u);
            return new GrupoMembroDto(
                m.UsuarioId,
                u?.Nome ?? "(usuário removido)",
                u?.Email,
                u?.Tenant?.Nome ?? "—");
        }).ToList();

        return Ok(dtos);
    }

    [HttpPost("{id:guid}/membros")]
    public async Task<IActionResult> AdicionarMembros(Guid id, [FromBody] AdicionarMembrosDto dto, CancellationToken ct)
    {
        var grupo = await _context.GruposPergunta.FirstOrDefaultAsync(g => g.Id == id, ct);
        if (grupo == null) return NotFound();

        var existentes = await _context.GruposPerguntaMembros
            .Where(m => m.GrupoId == id)
            .Select(m => m.UsuarioId)
            .ToListAsync(ct);

        var novos = dto.UsuarioIds.Distinct().Except(existentes)
            .Select(usuarioId => new GrupoPerguntaMembro
            {
                Id = Guid.NewGuid(),
                GrupoId = id,
                UsuarioId = usuarioId,
                AdicionadoEm = DateTime.UtcNow,
            })
            .ToList();

        if (novos.Count > 0)
        {
            _context.GruposPerguntaMembros.AddRange(novos);
            await _context.SaveChangesAsync(ct);
        }

        return NoContent();
    }

    [HttpDelete("{id:guid}/membros/{usuarioId:guid}")]
    public async Task<IActionResult> RemoverMembro(Guid id, Guid usuarioId, CancellationToken ct)
    {
        var membro = await _context.GruposPerguntaMembros
            .FirstOrDefaultAsync(m => m.GrupoId == id && m.UsuarioId == usuarioId, ct);
        if (membro == null) return NotFound();

        _context.GruposPerguntaMembros.Remove(membro);
        await _context.SaveChangesAsync(ct);

        return NoContent();
    }

    private Guid GetSuperAdminId() =>
        Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : Guid.Empty;
}
