using LegalManager.Application.DTOs.Assistente;
using LegalManager.Domain.Entities;
using LegalManager.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LegalManager.API.Controllers;

/// <summary>
/// Monitoramento/auditoria cross-tenant do assistente de IA: perguntas, respostas e ferramentas
/// chamadas (ver <see cref="InteracaoAssistente"/>). Consumido pela página Super Admin "Assistente IA".
/// </summary>
[ApiController]
[Route("api/superadmin/assistente")]
[Authorize(Roles = "SuperAdmin")]
public class SuperAdminAssistenteController(AppDbContext db) : ControllerBase
{
    private const int TamanhoResumo = 240;

    [HttpGet("interacoes")]
    public async Task<ActionResult<InteracaoAssistentePagedResultDto>> Listar(
        [FromQuery] Guid? tenantId,
        [FromQuery] string? tenantNome,
        [FromQuery] Guid? usuarioId,
        [FromQuery] string? busca,
        [FromQuery] bool? sucesso,
        [FromQuery] DateTime? de,
        [FromQuery] DateTime? ate,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 30,
        CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);

        var query = await FiltrarAsync(tenantId, tenantNome, de, ate, ct);
        if (usuarioId.HasValue) query = query.Where(i => i.UsuarioId == usuarioId.Value);
        if (sucesso.HasValue) query = query.Where(i => i.Sucesso == sucesso.Value);
        if (!string.IsNullOrWhiteSpace(busca))
        {
            var b = busca.Trim().ToLower();
            query = query.Where(i => i.Pergunta.ToLower().Contains(b) || (i.Resposta != null && i.Resposta.ToLower().Contains(b)));
        }

        var total = await query.CountAsync(ct);
        var itens = await query
            .OrderByDescending(i => i.CriadoEm)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(i => new
            {
                i.Id, i.CriadoEm, i.TenantId, i.UsuarioId, i.ImpersonadoPorId, i.ConversaId, i.Pergunta, i.Resposta,
                i.Sucesso, i.MensagemErro, i.QuantidadeFerramentas, i.TokensEntrada, i.TokensSaida, i.DuracaoMs
            })
            .ToListAsync(ct);

        var tenants = await NomesTenantsAsync(itens.Select(i => i.TenantId), ct);
        var usuarios = await NomesUsuariosAsync(itens.Select(i => i.UsuarioId), ct);

        var items = itens.Select(i => new InteracaoAssistenteListItemDto(
            i.Id, i.CriadoEm, i.TenantId, tenants.GetValueOrDefault(i.TenantId),
            i.UsuarioId, usuarios.GetValueOrDefault(i.UsuarioId), i.ImpersonadoPorId.HasValue, i.ConversaId,
            Resumir(i.Pergunta)!, Resumir(i.Sucesso ? i.Resposta : i.MensagemErro),
            i.Sucesso, i.QuantidadeFerramentas, i.TokensEntrada, i.TokensSaida, i.DuracaoMs)).ToList();

        return Ok(new InteracaoAssistentePagedResultDto(items, total, page, pageSize));
    }

    [HttpGet("interacoes/{id:guid}")]
    public async Task<ActionResult<InteracaoAssistenteDetalheDto>> Detalhe(Guid id, CancellationToken ct)
    {
        var i = await db.InteracoesAssistente.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (i is null) return NotFound();
        return Ok((await DetalhesAsync([i], ct))[0]);
    }

    /// <summary>Todas as perguntas/respostas de uma conversa, em ordem cronológica.</summary>
    [HttpGet("conversas/{conversaId:guid}")]
    public async Task<ActionResult<List<InteracaoAssistenteDetalheDto>>> Conversa(Guid conversaId, CancellationToken ct)
    {
        var lista = await db.InteracoesAssistente.AsNoTracking()
            .Where(x => x.ConversaId == conversaId)
            .OrderBy(x => x.CriadoEm)
            .Take(200)
            .ToListAsync(ct);
        if (lista.Count == 0) return NotFound();
        return Ok(await DetalhesAsync(lista, ct));
    }

    /// <summary>Uso agregado por tenant no período (padrão: últimos 30 dias).</summary>
    [HttpGet("resumo")]
    public async Task<ActionResult<List<AssistenteResumoTenantDto>>> Resumo(
        [FromQuery] string? tenantNome, [FromQuery] DateTime? de, [FromQuery] DateTime? ate, CancellationToken ct)
    {
        var query = await FiltrarAsync(null, tenantNome, de ?? DateTime.UtcNow.Date.AddDays(-30), ate, ct);

        // Agregação em memória: GroupBy com Count(distinct) não traduz bem em todos os provedores
        // (e o volume de uma tabela de log por período é pequeno para isso).
        var linhas = await query
            .Select(i => new { i.TenantId, i.ConversaId, i.UsuarioId, i.Sucesso, i.TokensEntrada, i.TokensSaida, i.CriadoEm })
            .ToListAsync(ct);

        var tenants = await NomesTenantsAsync(linhas.Select(l => l.TenantId), ct);
        var resumo = linhas
            .GroupBy(l => l.TenantId)
            .Select(g => new AssistenteResumoTenantDto(
                g.Key,
                tenants.GetValueOrDefault(g.Key),
                g.Count(),
                g.Select(x => x.ConversaId).Distinct().Count(),
                g.Select(x => x.UsuarioId).Distinct().Count(),
                g.Count(x => !x.Sucesso),
                g.Sum(x => (long)x.TokensEntrada),
                g.Sum(x => (long)x.TokensSaida),
                g.Max(x => x.CriadoEm)))
            .OrderByDescending(r => r.Perguntas)
            .ToList();

        return Ok(resumo);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private async Task<IQueryable<InteracaoAssistente>> FiltrarAsync(
        Guid? tenantId, string? tenantNome, DateTime? de, DateTime? ate, CancellationToken ct)
    {
        var query = db.InteracoesAssistente.AsNoTracking().AsQueryable();
        if (tenantId.HasValue) query = query.Where(i => i.TenantId == tenantId.Value);
        if (!string.IsNullOrWhiteSpace(tenantNome))
        {
            var nome = tenantNome.Trim().ToLower();
            var ids = await db.Tenants.Where(t => t.Nome.ToLower().Contains(nome)).Select(t => t.Id).ToListAsync(ct);
            query = query.Where(i => ids.Contains(i.TenantId));
        }
        if (de.HasValue) query = query.Where(i => i.CriadoEm >= de.Value);
        // "ate" vem como data (AAAA-MM-DD): inclui o dia inteiro.
        if (ate.HasValue) { var lim = ate.Value.TimeOfDay == TimeSpan.Zero ? ate.Value.AddDays(1) : ate.Value; query = query.Where(i => i.CriadoEm < lim); }
        return query;
    }

    private async Task<List<InteracaoAssistenteDetalheDto>> DetalhesAsync(List<InteracaoAssistente> lista, CancellationToken ct)
    {
        var tenants = await NomesTenantsAsync(lista.Select(i => i.TenantId), ct);
        var usuarios = await NomesUsuariosAsync(
            lista.Select(i => i.UsuarioId).Concat(lista.Where(i => i.ImpersonadoPorId.HasValue).Select(i => i.ImpersonadoPorId!.Value)), ct);

        return lista.Select(i => new InteracaoAssistenteDetalheDto(
            i.Id, i.CriadoEm, i.TenantId, tenants.GetValueOrDefault(i.TenantId),
            i.UsuarioId, usuarios.GetValueOrDefault(i.UsuarioId),
            i.ImpersonadoPorId, i.ImpersonadoPorId is { } sa ? usuarios.GetValueOrDefault(sa) : null,
            i.ConversaId, i.Pergunta, i.Resposta, i.Sucesso, i.MensagemErro, i.IdiomaCorrigido, i.FerramentasJson,
            i.Provedor, i.Modelo, i.TokensEntrada, i.TokensSaida, i.DuracaoMs)).ToList();
    }

    private async Task<Dictionary<Guid, string>> NomesTenantsAsync(IEnumerable<Guid> ids, CancellationToken ct)
    {
        var lista = ids.Distinct().ToList();
        return lista.Count == 0
            ? []
            : await db.Tenants.Where(t => lista.Contains(t.Id)).ToDictionaryAsync(t => t.Id, t => t.Nome, ct);
    }

    private async Task<Dictionary<Guid, string>> NomesUsuariosAsync(IEnumerable<Guid> ids, CancellationToken ct)
    {
        var lista = ids.Distinct().ToList();
        return lista.Count == 0
            ? []
            : await db.Users.Where(u => lista.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.Nome, ct);
    }

    private static string? Resumir(string? s) =>
        string.IsNullOrWhiteSpace(s) ? s : s.Length <= TamanhoResumo ? s : s[..TamanhoResumo] + "…";
}
