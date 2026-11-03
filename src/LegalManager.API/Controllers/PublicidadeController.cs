using LegalManager.API.Configuration;
using LegalManager.Application.DTOs.Publicidade;
using LegalManager.Domain.Enums;
using LegalManager.Domain.Interfaces;
using LegalManager.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LegalManager.API.Controllers;

/// <param name="Origem">De onde carregar o iframe (host próprio dos anúncios); null = a do app.</param>
/// <param name="Proprios">Anúncios do Causify para o espaço que o Adsterra deixar vazio.</param>
public record PublicidadeDto(
    bool Ativo, IReadOnlyList<string> Formatos, string? Origem = null, IReadOnlyList<AnuncioProprioDto>? Proprios = null);

// Publicidade do Adsterra para escritórios no plano Free (planos pagos não têm anúncio).
// Liga/desliga pelo .env (ADSTERRA_ENABLED); ver Configuration/Adsterra.cs.
//
// O anúncio nunca roda na página do app: cada um é um iframe de /api/publicidade/anuncio/{formato},
// isolado por sandbox (no iframe e no CSP da resposta). Com ADSTERRA_FRAME_ORIGIN o iframe vem de
// um host próprio (outra origem: sem acesso ao sessionStorage com o token nem ao DOM do app); sem
// ele, de uma origem opaca. Ver Configuration/Adsterra.cs.
//
// Espaço que o Adsterra deixa vazio (bloqueador de anúncios, sem campanha) recebe um anúncio
// próprio do Causify (AnuncioProprio) — decidido no navegador, em /js/publicidade.js.
[ApiController]
[Route("api/publicidade")]
[Authorize]
public class PublicidadeController : ControllerBase
{
    private const string CspAnuncio =
        "default-src 'none'; " +
        "script-src 'unsafe-inline' 'unsafe-eval' https:; " +
        "style-src 'unsafe-inline' https:; " +
        "img-src https: data: blob:; " +
        "font-src https: data:; " +
        "media-src https: blob:; " +
        "connect-src https:; " +
        "frame-src https: data: blob:; " +
        "form-action https:; " +
        "base-uri 'none';";

    private readonly AppDbContext _context;
    private readonly ITenantContext _tenantContext;
    private readonly Adsterra _adsterra;

    public PublicidadeController(AppDbContext context, ITenantContext tenantContext, Adsterra adsterra)
    {
        _context = context;
        _tenantContext = tenantContext;
        _adsterra = adsterra;
    }

    // Plano lido do banco, não do token: quem acabou de assinar deixa de ver anúncio na hora.
    [HttpGet]
    public async Task<ActionResult<PublicidadeDto>> Get(CancellationToken ct)
    {
        var formatos = _adsterra.Formatos;
        if (formatos.Count == 0 || _tenantContext.UserRole == PerfilUsuario.Cliente.ToString())
            return Ok(new PublicidadeDto(false, []));

        var plano = await _context.Tenants.AsNoTracking()
            .Where(t => t.Id == _tenantContext.TenantId)
            .Select(t => (PlanoTipo?)t.Plano)
            .FirstOrDefaultAsync(ct);

        if (plano != PlanoTipo.Free) return Ok(new PublicidadeDto(false, []));

        var proprios = await _context.AnunciosProprios.AsNoTracking()
            .Where(a => a.Ativo)
            .Select(a => new AnuncioProprioDto(a.Id, a.Icone, a.Titulo, a.Texto, a.TextoBotao, a.LinkUrl, a.Cor))
            .ToListAsync(ct);
        return Ok(new PublicidadeDto(true, formatos, _adsterra.Origem, proprios));
    }

    // Anônimo: o iframe não leva o token (que fica no sessionStorage). Só serve o script já
    // configurado — quem vê ou não o anúncio é decidido no GET acima. Com host próprio, só nele:
    // allow-same-origin na origem do app daria ao anunciante o token da sessão.
    [HttpGet("anuncio/{formato}")]
    [AllowAnonymous]
    public IActionResult Anuncio(string formato)
    {
        if (_adsterra.OrigemFrame is not null && !_adsterra.EhHostDoFrame(Request.Host)) return NotFound();
        var html = _adsterra.Html(formato);
        if (html is null) return NotFound();

        var headers = Response.Headers;
        headers.Remove("X-Frame-Options"); // frame-ancestors abaixo cobre o embed
        headers.ContentSecurityPolicy =
            $"sandbox {_adsterra.Sandbox}; {CspAnuncio} frame-ancestors {_adsterra.FrameAncestors()};";
        headers.CacheControl = "no-cache";
        return Content(html, "text/html; charset=utf-8");
    }
}
