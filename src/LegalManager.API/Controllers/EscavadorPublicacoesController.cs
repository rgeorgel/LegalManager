using LegalManager.Application.Interfaces;
using LegalManager.Domain;
using LegalManager.Domain.Enums;
using LegalManager.Domain.Interfaces;
using LegalManager.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LegalManager.API.Controllers;

/// <summary>
/// Endpoints de busca de publicações via Escavador.
/// "Minha caixa de publicações" — busca por OAB cadastrada ou OAB avulsa.
/// </summary>
[ApiController]
[Route("api/escavador/publicacoes")]
[Authorize]
public class EscavadorPublicacoesController : ControllerBase
{
    private readonly ITenantContext _tenant;
    private readonly AppDbContext _context;
    private readonly IEscavadorService _escavador;
    private readonly ITenantOabService _tenantOabService;

    public EscavadorPublicacoesController(
        ITenantContext tenant,
        AppDbContext context,
        IEscavadorService escavador,
        ITenantOabService tenantOabService)
    {
        _tenant = tenant;
        _context = context;
        _escavador = escavador;
        _tenantOabService = tenantOabService;
    }

    /// <summary>
    /// Busca publicações para uma OAB (ou para todas as OABs do tenant, se oab/uf não forem passadas).
    /// </summary>
    [HttpGet("busca")]
    public async Task<IActionResult> BuscarPorOab(
        [FromQuery] string? oab = null,
        [FromQuery] string? uf = null,
        [FromQuery] DateTime? de = null,
        [FromQuery] DateTime? ate = null,
        CancellationToken ct = default)
    {
        // Plan-gate
        if (!PlanoRestricoes.PermiteCapturacaoPublicacoes(_tenant.Plano))
            return StatusCode(402, new { error = "captura_disabled_for_plan", message = "A busca de publicações está disponível a partir do plano Pro." });

        // Resolve a lista de OABs a consultar
        List<(string Uf, string Numero, Guid? OabId)> oabs;
        if (!string.IsNullOrWhiteSpace(oab) && !string.IsNullOrWhiteSpace(uf))
        {
            oabs = new List<(string, string, Guid?)> { (uf.ToUpperInvariant(), oab, null) };
        }
        else
        {
            var oabsTenant = await _tenantOabService.ListarAsync(ct);
            oabs = oabsTenant.Where(o => o.Ativo).Select(o => (o.Uf, o.Numero, (Guid?)o.Id)).ToList();
            if (oabs.Count == 0)
                return Ok(new { items = Array.Empty<object>(), total = 0 });
        }

        var ateDate = (ate ?? DateTime.UtcNow).Date;
        var deDate = (de ?? ateDate.AddDays(-30)).Date;

        var tasks = oabs.Select(async o =>
        {
            try
            {
                var pagina = await _escavador.BuscarPublicacoesPorOabAsync(o.Numero, o.Uf, deDate, ateDate, 1, ct);
                return pagina.Data.Select(p => new
                {
                    p.Id,
                    p.Uuid,
                    p.NumeroCnj,
                    p.Data,
                    p.Diario,
                    p.Snippet,
                    p.LinkPdf,
                    OabUf = o.Uf,
                    OabNumero = o.Numero
                });
            }
            catch (Exception)
            {
                return Enumerable.Empty<object>();
            }
        });

        var results = await Task.WhenAll(tasks);
        var items = results.SelectMany(r => r)
            .GroupBy(x => x.GetType().GetProperty("Uuid")?.GetValue(x)?.ToString() ?? Guid.NewGuid().ToString())
            .Select(g => g.First())
            .ToList();

        // Atualiza UltimaVerificacao das OABs do tenant
        if (oabs.Any(o => o.OabId.HasValue))
        {
            var ids = oabs.Where(o => o.OabId.HasValue).Select(o => o.OabId!.Value);
            await _tenantOabService.MarcarVerificadasAsync(ids, DateTime.UtcNow, ct);
        }

        return Ok(new { items, total = items.Count });
    }
}
