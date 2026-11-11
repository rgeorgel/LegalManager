using System.Security.Claims;
using LegalManager.API.Guias;
using LegalManager.Application.DTOs.Guias;
using LegalManager.Application.Interfaces;
using LegalManager.Domain;
using LegalManager.Domain.Entities;
using LegalManager.Infrastructure.Persistence;
using LegalManager.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LegalManager.API.Controllers;

// Central Causify: guias públicos sobre as funcionalidades (ver Artigo). As páginas saem
// em HTML pronto do servidor (SEO e preview de links); só rascunhos ficam de fora. Curtir
// ("Útil") exige login no portal — o botão da página chama a API abaixo com o token da sessão.
[ApiController]
public class GuiasController(AppDbContext db, IStorageService storage, IConfiguration config, ILogger<GuiasController> logger) : ControllerBase
{
    private const int Relacionados = 3;

    // URL pública do site (canonical, og:url, sitemap). Mesma config dos links dos e-mails.
    private string UrlBase => (config["App:FrontendUrl"] ?? "https://causify.com.br").TrimEnd('/');

    // Lançada em nov/2026 (Guias:Lancado = true no appsettings): páginas indexáveis, sitemap no
    // robots.txt e link "Guias" no menu da home e do blog. Com Guias__Lancado=false (ex.: voltar
    // a esconder) as páginas seguem abrindo por link direto, mas com noindex e sem sitemap.
    private bool Lancado => config.GetValue("Guias:Lancado", false);

    [HttpGet("/guias")]
    [AllowAnonymous]
    public async Task<IActionResult> Indice([FromQuery] string? categoria, CancellationToken ct)
    {
        var publicados = db.Artigos.Where(a => a.Publicado && a.PublicadoEm != null);

        var categoriasComGuias = await publicados.Select(a => a.Categoria).Distinct().ToListAsync(ct);

        // Categoria desconhecida mostra todos (link antigo não vira página vazia).
        var cat = CategoriasArtigo.Buscar(categoria);
        var filtrados = cat == null ? publicados : publicados.Where(a => a.Categoria == cat.Slug);
        var itens = await ListarAsync(filtrados, null, ct);

        return Html(GuiasHtml.Indice(UrlBase, itens, cat?.Slug, categoriasComGuias, Lancado));
    }

    [HttpGet("/guias/{slug:regex(^[[a-z0-9-]]+$)}")]
    [AllowAnonymous]
    public async Task<IActionResult> Artigo(string slug, CancellationToken ct)
    {
        var artigo = await db.Artigos.AsNoTracking()
            .FirstOrDefaultAsync(a => a.Slug == slug && a.Publicado && a.PublicadoEm != null, ct);
        if (artigo == null) return Html(GuiasHtml.NaoEncontrado(UrlBase), StatusCodes.Status404NotFound);

        var curtidas = await db.ArtigosCurtidas.CountAsync(c => c.ArtigoId == artigo.Id, ct);

        // Mesma categoria primeiro; completa com os mais recentes das outras.
        var outros = db.Artigos.Where(a => a.Publicado && a.PublicadoEm != null && a.Id != artigo.Id);
        var relacionados = await ListarAsync(outros.Where(a => a.Categoria == artigo.Categoria), Relacionados, ct);
        if (relacionados.Count < Relacionados)
        {
            var extras = await ListarAsync(outros.Where(a => a.Categoria != artigo.Categoria), Relacionados - relacionados.Count, ct);
            relacionados = [.. relacionados, .. extras];
        }

        var corpo = GuiaMarkdown.Renderizar(artigo.Conteudo);
        return Html(GuiasHtml.Artigo(UrlBase, artigo, corpo, curtidas, relacionados, Lancado));
    }

    [HttpGet("/guias/sitemap.xml")]
    [AllowAnonymous]
    public async Task<IActionResult> Sitemap(CancellationToken ct)
    {
        if (!Lancado) return NotFound();

        var guias = await db.Artigos
            .Where(a => a.Publicado && a.PublicadoEm != null)
            .OrderByDescending(a => a.PublicadoEm)
            .Select(a => new { a.Slug, Modificado = a.AtualizadoEm ?? a.PublicadoEm!.Value })
            .ToListAsync(ct);

        Response.Headers.CacheControl = "public, max-age=3600";
        return Content(GuiasHtml.Sitemap(UrlBase, guias.Select(g => (g.Slug, g.Modificado)).ToList()),
            "application/xml; charset=utf-8");
    }

    // Imagens dos guias (upload do super admin, ver SuperAdminGuiasController.EnviarImagem).
    // O nome é um Guid novo a cada upload, então o conteúdo nunca muda: cache de 1 ano.
    [HttpGet("/guias/img/{nome}")]
    [AllowAnonymous]
    public async Task<IActionResult> Imagem(string nome, CancellationToken ct)
    {
        if (!ImagemGuia.NomeServivel(nome)) return NotFound();

        Stream stream;
        try
        {
            stream = await storage.DownloadAsync(ImagemGuia.ObjectKey(nome), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Imagem de guia não encontrada no storage: {Nome}", nome);
            return NotFound();
        }

        Response.Headers.CacheControl = "public, max-age=31536000, immutable";
        return File(stream, ImagemGuia.ContentTypeDe(nome));
    }

    // ── "Útil" (curtida) ──────────────────────────────────────────────────────

    [HttpGet("/api/guias/{id:guid}/curtida")]
    [Authorize]
    public async Task<ActionResult<CurtidaGuiaDto>> Curtida(Guid id, CancellationToken ct)
    {
        if (await UsuarioDoPortalAsync(ct) is not { } usuarioId) return Forbid();
        if (!await PublicadoAsync(id, ct)) return NotFound();
        return Ok(await EstadoAsync(id, usuarioId, ct));
    }

    [HttpPut("/api/guias/{id:guid}/curtida")]
    [Authorize]
    public async Task<ActionResult<CurtidaGuiaDto>> Curtir(Guid id, CancellationToken ct)
    {
        if (await UsuarioDoPortalAsync(ct) is not { } usuarioId) return Forbid();
        if (!await PublicadoAsync(id, ct)) return NotFound();

        if (!await db.ArtigosCurtidas.AnyAsync(c => c.ArtigoId == id && c.UsuarioId == usuarioId, ct))
        {
            db.ArtigosCurtidas.Add(new ArtigoCurtida { ArtigoId = id, UsuarioId = usuarioId, CriadoEm = DateTime.UtcNow });
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException)
            {
                // Dois cliques simultâneos: a chave composta barra a segunda — o estado final é o mesmo.
            }
        }
        return Ok(await EstadoAsync(id, usuarioId, ct));
    }

    [HttpDelete("/api/guias/{id:guid}/curtida")]
    [Authorize]
    public async Task<ActionResult<CurtidaGuiaDto>> Descurtir(Guid id, CancellationToken ct)
    {
        if (await UsuarioDoPortalAsync(ct) is not { } usuarioId) return Forbid();

        var curtida = await db.ArtigosCurtidas.FirstOrDefaultAsync(c => c.ArtigoId == id && c.UsuarioId == usuarioId, ct);
        if (curtida != null)
        {
            db.ArtigosCurtidas.Remove(curtida);
            await db.SaveChangesAsync(ct);
        }
        return Ok(await EstadoAsync(id, usuarioId, ct));
    }

    // ── Apoio ─────────────────────────────────────────────────────────────────

    private async Task<List<GuiasHtml.ItemLista>> ListarAsync(IQueryable<Artigo> query, int? limite, CancellationToken ct)
    {
        var ordenada = query.OrderByDescending(a => a.PublicadoEm);
        var linhas = await (limite is { } n ? ordenada.Take(n) : ordenada)
            .Select(a => new { a.Titulo, a.Slug, a.Resumo, a.Categoria, a.CapaUrl, PublicadoEm = a.PublicadoEm!.Value, a.Conteudo })
            .ToListAsync(ct);

        return linhas.Select(a => new GuiasHtml.ItemLista(a.Titulo, a.Slug, a.Resumo, a.Categoria, a.CapaUrl,
            a.PublicadoEm, GuiaMarkdown.MinutosDeLeitura(a.Conteudo))).ToList();
    }

    private Task<bool> PublicadoAsync(Guid id, CancellationToken ct) =>
        db.Artigos.AnyAsync(a => a.Id == id && a.Publicado, ct);

    private async Task<CurtidaGuiaDto> EstadoAsync(Guid id, Guid usuarioId, CancellationToken ct)
    {
        var total = await db.ArtigosCurtidas.CountAsync(c => c.ArtigoId == id, ct);
        var curtiu = await db.ArtigosCurtidas.AnyAsync(c => c.ArtigoId == id && c.UsuarioId == usuarioId, ct);
        return new CurtidaGuiaDto(curtiu, total);
    }

    // Só usuários do portal (AspNetUsers). O token do portal do cliente também é JWT, mas o id
    // é de um AcessoCliente — não pode curtir (e quebraria a FK).
    private async Task<Guid?> UsuarioDoPortalAsync(CancellationToken ct)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id)) return null;
        if (User.IsInRole("Cliente")) return null;
        return await db.Users.AnyAsync(u => u.Id == id, ct) ? id : null;
    }

    private ContentResult Html(string html, int status = StatusCodes.Status200OK)
    {
        // Curto: uma edição no super admin aparece em minutos, sem invalidar CDN.
        Response.Headers.CacheControl = "public, max-age=300";
        if (!Lancado) Response.Headers["X-Robots-Tag"] = "noindex";
        return new ContentResult { Content = html, ContentType = "text/html; charset=utf-8", StatusCode = status };
    }
}
