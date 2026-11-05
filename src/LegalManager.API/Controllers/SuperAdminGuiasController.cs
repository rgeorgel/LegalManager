using System.Security.Claims;
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

// Cadastro dos guias da Central Causify (ver Artigo). As páginas públicas que mostram os
// publicados ficam em GuiasController.
[ApiController]
[Route("api/superadmin/guias")]
[Authorize(Roles = "SuperAdmin")]
public class SuperAdminGuiasController(AppDbContext db, IAuditService audit, IStorageService storage) : ControllerBase
{
    private const string Entidade = "Artigo";

    [HttpGet]
    public async Task<ActionResult<List<GuiaAdminDto>>> Listar(CancellationToken ct)
    {
        // Rascunhos (sem PublicadoEm) primeiro, depois os publicados do mais recente ao mais antigo.
        var guias = await db.Artigos
            .OrderBy(a => a.PublicadoEm != null)
            .ThenByDescending(a => a.PublicadoEm)
            .ThenByDescending(a => a.CriadoEm)
            .Select(a => new { Artigo = a, Curtidas = a.Curtidas.Count })
            .ToListAsync(ct);

        return Ok(guias.Select(g => ToDto(g.Artigo, g.Curtidas)).ToList());
    }

    [HttpGet("categorias")]
    public ActionResult<List<CategoriaGuiaDto>> Categorias() =>
        Ok(CategoriasArtigo.Todas.Select(c => new CategoriaGuiaDto(c.Slug, c.Nome)).ToList());

    [HttpPost]
    public async Task<ActionResult<GuiaAdminDto>> Criar([FromBody] SalvarGuiaDto dto, CancellationToken ct)
    {
        var slug = SlugDe(dto);
        if (await ValidarAsync(dto, slug, null, ct) is { } erro) return BadRequest(new { message = erro });

        var artigo = new Artigo
        {
            Id = Guid.NewGuid(),
            CriadoEm = DateTime.UtcNow,
            CriadoPorId = GetSuperAdminId()
        };
        Aplicar(artigo, dto, slug);

        db.Artigos.Add(artigo);
        await LogAsync(AuditActions.Create, artigo, null, ct);
        await db.SaveChangesAsync(ct);

        return Ok(ToDto(artigo, 0));
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<GuiaAdminDto>> Atualizar(Guid id, [FromBody] SalvarGuiaDto dto, CancellationToken ct)
    {
        var artigo = await db.Artigos.FirstOrDefaultAsync(a => a.Id == id, ct);
        if (artigo == null) return NotFound();

        var slug = SlugDe(dto);
        if (await ValidarAsync(dto, slug, id, ct) is { } erro) return BadRequest(new { message = erro });

        var anterior = Snapshot(artigo);
        Aplicar(artigo, dto, slug);
        artigo.AtualizadoEm = DateTime.UtcNow;

        await LogAsync(AuditActions.Update, artigo, anterior, ct);
        await db.SaveChangesAsync(ct);

        var curtidas = await db.ArtigosCurtidas.CountAsync(c => c.ArtigoId == id, ct);
        return Ok(ToDto(artigo, curtidas));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Excluir(Guid id, CancellationToken ct)
    {
        var artigo = await db.Artigos.FirstOrDefaultAsync(a => a.Id == id, ct);
        if (artigo == null) return NotFound();

        db.Artigos.Remove(artigo);
        await LogAsync(AuditActions.Delete, artigo, Snapshot(artigo), ct);
        await db.SaveChangesAsync(ct);

        return NoContent();
    }

    // Mesmo renderizador da página pública: a pré-visualização do editor sai igual ao publicado.
    [HttpPost("previa")]
    public ActionResult<PreviaGuiaResultadoDto> Previa([FromBody] PreviaGuiaDto dto)
    {
        var r = GuiaMarkdown.Renderizar(dto.Conteudo);
        return Ok(new PreviaGuiaResultadoDto(r.Html, r.MinutosLeitura));
    }

    [HttpPost("imagens")]
    [RequestSizeLimit(ImagemGuia.TamanhoMaximo + 64 * 1024)]
    public async Task<ActionResult<ImagemGuiaDto>> EnviarImagem(IFormFile? arquivo, CancellationToken ct)
    {
        if (arquivo == null) return BadRequest(new { message = "Selecione uma imagem." });

        await using var stream = arquivo.OpenReadStream();
        string extensao, contentType;
        try
        {
            (extensao, contentType) = ImagemGuia.Validar(stream, arquivo.FileName, arquivo.Length);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }

        var nome = Guid.NewGuid().ToString("N") + extensao;
        await storage.UploadAsync(stream, ImagemGuia.ObjectKey(nome), contentType, ct);

        return Ok(new ImagemGuiaDto(ImagemGuia.PrefixoUrl + nome));
    }

    private static string SlugDe(SalvarGuiaDto dto) =>
        string.IsNullOrWhiteSpace(dto.Slug) ? GuiaSlug.Gerar(dto.Titulo) : dto.Slug.Trim();

    internal async Task<string?> ValidarAsync(SalvarGuiaDto dto, string slug, Guid? id, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(dto.Titulo)) return "Informe o título.";
        if (string.IsNullOrWhiteSpace(dto.Resumo)) return "Informe o resumo.";
        if (string.IsNullOrWhiteSpace(dto.Conteudo)) return "Escreva o conteúdo do guia.";
        if (CategoriasArtigo.Buscar(dto.Categoria) == null) return "Escolha uma categoria.";
        if (!GuiaSlug.Valido(slug))
            return "O endereço só pode ter letras minúsculas sem acento, números e hífens (ex: portal-do-cliente).";

        if (Preenchido(dto.CapaUrl) is { } capa && !GuiaMarkdown.ImagemPermitida(capa))
            return "A capa deve ser um caminho do site (ex: /guias/img/...) ou uma URL https.";
        // O botão abre na mesma aba: só caminhos internos do sistema.
        if (Preenchido(dto.LinkUrl) is { } link && !GuiaMarkdown.CaminhoLocal(link))
            return "O link do botão deve ser um caminho do sistema, ex: /pages/processos.html.";

        if (await db.Artigos.AnyAsync(a => a.Slug == slug && a.Id != id, ct))
            return $"Já existe um guia com o endereço /guias/{slug}.";

        return null;
    }

    private static string? Preenchido(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private static void Aplicar(Artigo a, SalvarGuiaDto dto, string slug)
    {
        a.Titulo = dto.Titulo.Trim();
        a.Slug = slug;
        a.Resumo = dto.Resumo.Trim();
        a.Conteudo = dto.Conteudo.Trim();
        a.Categoria = dto.Categoria;
        a.CapaUrl = Preenchido(dto.CapaUrl);
        a.LinkUrl = Preenchido(dto.LinkUrl);
        a.LinkTexto = a.LinkUrl == null ? null : Preenchido(dto.LinkTexto);
        a.Publicado = dto.Publicado;
        if (dto.Publicado && a.PublicadoEm == null) a.PublicadoEm = DateTime.UtcNow;
    }

    private static GuiaAdminDto ToDto(Artigo a, int curtidas) => new(
        a.Id, a.Titulo, a.Slug, a.Resumo, a.Conteudo, a.Categoria, a.CapaUrl, a.LinkUrl, a.LinkTexto,
        a.Publicado, a.PublicadoEm, a.CriadoEm, a.AtualizadoEm, curtidas);

    // Sem o Conteudo (pode ter 100 mil caracteres): o log guarda só o que identifica a versão.
    private static object Snapshot(Artigo a) => new
    {
        a.Titulo, a.Slug, a.Resumo, a.Categoria, a.CapaUrl, a.LinkUrl, a.LinkTexto,
        a.Publicado, a.PublicadoEm, TamanhoConteudo = a.Conteudo.Length
    };

    private Task LogAsync(string acao, Artigo a, object? anterior, CancellationToken ct) =>
        audit.LogAsync(new AuditLogEntry(
            TenantConstants.SystemTenantId, GetSuperAdminId(), acao, Entidade, a.Id.ToString(),
            anterior, acao == AuditActions.Delete ? null : Snapshot(a),
            HttpContext.GetClientIpAddress()), ct);

    private Guid? GetSuperAdminId() =>
        Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;
}
