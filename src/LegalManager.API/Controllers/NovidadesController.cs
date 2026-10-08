using LegalManager.Application.DTOs.Novidades;
using LegalManager.Domain;
using LegalManager.Domain.Entities;
using LegalManager.Domain.Interfaces;
using LegalManager.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LegalManager.API.Controllers;

// Painel "Novidades" (✨) do portal admin. Mesmo padrão do sino de notificações:
// GET .../count no carregamento de cada página; GET lista ao abrir o painel; POST .../visto
// zera o contador (grava Usuario.NovidadesVistasEm). O dashboard também consulta
// GET .../destaque para o modal da novidade em destaque (ver Novidade.Destaque).
[ApiController]
[Route("api/novidades")]
[Authorize]
public class NovidadesController(AppDbContext db, ITenantContext tenantContext) : ControllerBase
{
    private const int Limite = 30;

    [HttpGet]
    public async Task<ActionResult<List<NovidadeDto>>> Listar(CancellationToken ct)
    {
        var vistasEm = await VistasEmAsync(ct);

        var novidades = await db.Novidades
            .Where(n => n.Publicada && n.PublicadaEm != null)
            .OrderByDescending(n => n.PublicadaEm)
            .Take(Limite)
            .ToListAsync(ct);

        return Ok(novidades.Select(n => ToDto(n, vistasEm != null && n.PublicadaEm > vistasEm)).ToList());
    }

    [HttpGet("count")]
    public async Task<ActionResult<int>> Contar(CancellationToken ct)
    {
        var vistasEm = await VistasEmAsync(ct);
        if (vistasEm == null) return Ok(0);

        return Ok(await db.Novidades.CountAsync(n => n.Publicada && n.PublicadaEm > vistasEm, ct));
    }

    [HttpPost("visto")]
    public async Task<IActionResult> MarcarVisto(CancellationToken ct)
    {
        var usuario = await db.Users.FirstOrDefaultAsync(u => u.Id == tenantContext.UserId, ct);
        if (usuario == null) return NotFound();

        usuario.NovidadesVistasEm = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    // A novidade em destaque mais recente que o usuário ainda não viu — nem no modal, nem
    // abrindo o painel — e cujo plano mínimo o escritório tem (o "Me mostra" abre um tour
    // da funcionalidade, que não faria sentido bloqueado). 204 = nada a mostrar.
    [HttpGet("destaque")]
    public async Task<ActionResult<NovidadeDto>> Destaque(CancellationToken ct)
    {
        var usuario = await db.Users
            .Where(u => u.Id == tenantContext.UserId)
            .Select(u => new { u.CriadoEm, u.NovidadesVistasEm, u.DestaqueVistoEm })
            .FirstOrDefaultAsync(ct);
        if (usuario == null) return NoContent();

        var desde = new[] { usuario.CriadoEm, usuario.NovidadesVistasEm ?? default, usuario.DestaqueVistoEm ?? default }.Max();

        var candidatas = await db.Novidades
            .Where(n => n.Publicada && n.Destaque && n.PublicadaEm > desde)
            .OrderByDescending(n => n.PublicadaEm)
            .ToListAsync(ct);
        if (candidatas.Count == 0) return NoContent();

        var plano = await db.Tenants
            .Where(t => t.Id == tenantContext.TenantId)
            .Select(t => t.Plano)
            .FirstOrDefaultAsync(ct);

        var destaque = candidatas.FirstOrDefault(n => PlanoRestricoes.Atende(plano, n.PlanoMinimo));
        return destaque == null ? NoContent() : Ok(ToDto(destaque, nova: true));
    }

    [HttpPost("destaque/visto")]
    public async Task<IActionResult> MarcarDestaqueVisto(CancellationToken ct)
    {
        var usuario = await db.Users.FirstOrDefaultAsync(u => u.Id == tenantContext.UserId, ct);
        if (usuario == null) return NotFound();

        usuario.DestaqueVistoEm = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    private static NovidadeDto ToDto(Novidade n, bool nova) => new(
        n.Id, n.Titulo, n.Descricao, n.ImagemUrl, n.LinkUrl, n.LinkTexto, n.PlanoMinimo, n.TourId,
        n.PublicadaEm!.Value, nova);

    // Quem nunca abriu o painel conta a partir do cadastro — uma conta nova não nasce com
    // o histórico inteiro marcado como não lido.
    private async Task<DateTime?> VistasEmAsync(CancellationToken ct) =>
        await db.Users
            .Where(u => u.Id == tenantContext.UserId)
            .Select(u => (DateTime?)(u.NovidadesVistasEm ?? u.CriadoEm))
            .FirstOrDefaultAsync(ct);
}
