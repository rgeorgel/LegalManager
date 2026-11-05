using System.Security.Claims;
using LegalManager.Application.DTOs.Imagens;
using LegalManager.Application.Interfaces;
using LegalManager.Domain;
using LegalManager.Domain.Entities;
using LegalManager.Infrastructure.Persistence;
using LegalManager.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LegalManager.API.Controllers;

// Biblioteca de imagens públicas (ver ImagemPublica): pastas, envio, substituição, renomear e
// excluir. A URL pública /imagens/{pasta}/{nome} é servida por ImagensController.
[ApiController]
[Route("api/superadmin/imagens")]
[Authorize(Roles = "SuperAdmin")]
public class SuperAdminImagensController(AppDbContext db, IStorageService storage, IAuditService audit,
    ILogger<SuperAdminImagensController> logger) : ControllerBase
{
    private const string Entidade = "ImagemPublica";

    public const string ConflitoErro = "erro";
    public const string ConflitoSubstituir = "substituir";
    public const string ConflitoRenomear = "renomear";

    // ── Pastas ────────────────────────────────────────────────────────────────

    [HttpGet("pastas")]
    public async Task<ActionResult<List<PastaImagemDto>>> ListarPastas(CancellationToken ct) =>
        Ok(await db.PastasImagens
            .OrderBy(p => p.Nome)
            .Select(p => new PastaImagemDto(p.Id, p.Nome, p.Imagens.Count))
            .ToListAsync(ct));

    [HttpPost("pastas")]
    public async Task<ActionResult<PastaImagemDto>> CriarPasta([FromBody] CriarPastaImagemDto dto, CancellationToken ct)
    {
        var nome = NomeImagemPublica.Pasta(dto.Nome);
        if (!NomeImagemPublica.PastaValida(nome))
            return BadRequest(new { message = "Use letras, números e hífens no nome da pasta (ex: portal-do-cliente)." });
        if (await db.PastasImagens.AnyAsync(p => p.Nome == nome, ct))
            return BadRequest(new { message = $"A pasta \"{nome}\" já existe." });

        var pasta = await CriarPastaAsync(nome, ct);
        return Ok(new PastaImagemDto(pasta.Id, pasta.Nome, 0));
    }

    // Só pasta vazia: excluir as imagens junto apagaria arquivos usados em guias sem aviso.
    [HttpDelete("pastas/{id:guid}")]
    public async Task<IActionResult> ExcluirPasta(Guid id, CancellationToken ct)
    {
        var pasta = await db.PastasImagens.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (pasta == null) return NotFound();
        if (await db.ImagensPublicas.AnyAsync(i => i.PastaId == id, ct))
            return BadRequest(new { message = "Exclua as imagens da pasta antes de excluí-la." });

        db.PastasImagens.Remove(pasta);
        await LogAsync(AuditActions.Delete, "PastaImagem", pasta.Id, new { pasta.Nome }, null, ct);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    // ── Imagens ───────────────────────────────────────────────────────────────

    // Sem pastaId: todas (a busca do seletor no editor de guias filtra no navegador).
    [HttpGet]
    public async Task<ActionResult<List<ImagemPublicaDto>>> Listar([FromQuery] Guid? pastaId, CancellationToken ct)
    {
        var query = db.ImagensPublicas.Include(i => i.Pasta).AsQueryable();
        if (pastaId is { } id) query = query.Where(i => i.PastaId == id);
        var imagens = await query.OrderByDescending(i => i.AtualizadoEm).ToListAsync(ct);

        var usos = await UsosAsync(ct);
        return Ok(imagens.Select(i => ToDto(i, usos)).ToList());
    }

    /// <summary>
    /// Envia uma imagem para a pasta (criada se não existir). Nome já usado na pasta:
    /// <c>conflito=erro</c> (padrão) responde 409 com um nome livre sugerido;
    /// <c>substituir</c> troca o arquivo mantendo a URL; <c>renomear</c> salva como "nome-2".
    /// </summary>
    [HttpPost]
    [RequestSizeLimit(ImagemGuia.TamanhoMaximo + 64 * 1024)]
    public async Task<ActionResult<ImagemPublicaDto>> Enviar(
        IFormFile? arquivo, [FromForm] string? pasta, [FromForm] string? nome, [FromForm] string? conflito,
        CancellationToken ct)
    {
        if (arquivo == null) return BadRequest(new { message = "Selecione uma imagem." });

        var nomePasta = NomeImagemPublica.Pasta(pasta);
        if (!NomeImagemPublica.PastaValida(nomePasta)) return BadRequest(new { message = "Escolha uma pasta." });

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

        var nomeArquivo = NomeImagemPublica.Gerar(string.IsNullOrWhiteSpace(nome) ? arquivo.FileName : nome, extensao);
        var pastaEntidade = await db.PastasImagens.FirstOrDefaultAsync(p => p.Nome == nomePasta, ct)
            ?? await CriarPastaAsync(nomePasta, ct);

        var existente = await db.ImagensPublicas.Include(i => i.Pasta)
            .FirstOrDefaultAsync(i => i.PastaId == pastaEntidade.Id && i.Nome == nomeArquivo, ct);

        if (existente != null)
        {
            switch (conflito ?? ConflitoErro)
            {
                case ConflitoSubstituir:
                    return await SubstituirAsync(existente, stream, contentType, arquivo.Length, ct);
                case ConflitoRenomear:
                    nomeArquivo = NomeImagemPublica.ProximoLivre(nomeArquivo, await NomesDaPastaAsync(pastaEntidade.Id, ct));
                    break;
                default:
                    return Conflict(new ConflitoImagemDto(
                        $"Já existe \"{nomeArquivo}\" na pasta \"{nomePasta}\".", nomeArquivo,
                        NomeImagemPublica.ProximoLivre(nomeArquivo, await NomesDaPastaAsync(pastaEntidade.Id, ct))));
            }
        }

        var agora = DateTime.UtcNow;
        var imagem = new ImagemPublica
        {
            Id = Guid.NewGuid(),
            PastaId = pastaEntidade.Id,
            Pasta = pastaEntidade,
            Nome = nomeArquivo,
            ObjectKey = NovaChave(extensao),
            ContentType = contentType,
            Tamanho = arquivo.Length,
            CriadoEm = agora,
            AtualizadoEm = agora,
            CriadoPorId = GetSuperAdminId(),
        };
        if (!await SalvarNoStorageAsync(stream, imagem.ObjectKey, contentType, ct)) return FalhaNoStorage();

        db.ImagensPublicas.Add(imagem);
        await LogAsync(AuditActions.Create, Entidade, imagem.Id, null, Snapshot(imagem), ct);
        await db.SaveChangesAsync(ct);

        return Ok(ToDto(imagem, []));
    }

    // Troca o arquivo de uma imagem existente (mesma URL). A extensão faz parte da URL, então
    // o arquivo novo precisa ser do mesmo tipo.
    [HttpPut("{id:guid}/arquivo")]
    [RequestSizeLimit(ImagemGuia.TamanhoMaximo + 64 * 1024)]
    public async Task<ActionResult<ImagemPublicaDto>> Substituir(Guid id, IFormFile? arquivo, CancellationToken ct)
    {
        if (arquivo == null) return BadRequest(new { message = "Selecione uma imagem." });
        var imagem = await db.ImagensPublicas.Include(i => i.Pasta).FirstOrDefaultAsync(i => i.Id == id, ct);
        if (imagem == null) return NotFound();

        await using var stream = arquivo.OpenReadStream();
        string contentType;
        try
        {
            (_, contentType) = ImagemGuia.Validar(stream, arquivo.FileName, arquivo.Length);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }

        return await SubstituirAsync(imagem, stream, contentType, arquivo.Length, ct);
    }

    [HttpPatch("{id:guid}")]
    public async Task<ActionResult<ImagemPublicaDto>> Renomear(Guid id, [FromBody] RenomearImagemDto dto, CancellationToken ct)
    {
        var imagem = await db.ImagensPublicas.Include(i => i.Pasta).FirstOrDefaultAsync(i => i.Id == id, ct);
        if (imagem == null) return NotFound();

        // A extensão é sempre a do arquivo: o usuário renomeia só a parte antes do ponto.
        var novoNome = NomeImagemPublica.Gerar(dto.Nome, Path.GetExtension(imagem.Nome));
        if (novoNome == imagem.Nome) return Ok(ToDto(imagem, await UsosAsync(ct)));
        if (await db.ImagensPublicas.AnyAsync(i => i.PastaId == imagem.PastaId && i.Nome == novoNome, ct))
            return BadRequest(new { message = $"Já existe \"{novoNome}\" nesta pasta." });

        var anterior = Snapshot(imagem);
        imagem.Nome = novoNome;
        await LogAsync(AuditActions.Update, Entidade, imagem.Id, anterior, Snapshot(imagem), ct);
        await db.SaveChangesAsync(ct);

        return Ok(ToDto(imagem, await UsosAsync(ct)));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Excluir(Guid id, CancellationToken ct)
    {
        var imagem = await db.ImagensPublicas.Include(i => i.Pasta).FirstOrDefaultAsync(i => i.Id == id, ct);
        if (imagem == null) return NotFound();

        db.ImagensPublicas.Remove(imagem);
        await LogAsync(AuditActions.Delete, Entidade, imagem.Id, Snapshot(imagem), null, ct);
        await db.SaveChangesAsync(ct);

        await ApagarDoStorageAsync(imagem.ObjectKey);
        return NoContent();
    }

    // ── Apoio ─────────────────────────────────────────────────────────────────

    private async Task<ActionResult<ImagemPublicaDto>> SubstituirAsync(ImagemPublica imagem, Stream stream,
        string contentType, long tamanho, CancellationToken ct)
    {
        var extensaoNova = ExtensaoDe(contentType);
        if (ExtensaoDe(imagem.ContentType) != extensaoNova)
            return BadRequest(new { message = $"Para manter o endereço, envie um arquivo {Path.GetExtension(imagem.Nome).TrimStart('.').ToUpperInvariant()}." });

        var anterior = Snapshot(imagem);
        var chaveAntiga = imagem.ObjectKey;

        // Objeto novo (e não sobrescrever o antigo): se o upload falhar, a imagem atual segue intacta.
        var chaveNova = NovaChave(Path.GetExtension(imagem.Nome));
        if (!await SalvarNoStorageAsync(stream, chaveNova, contentType, ct)) return FalhaNoStorage();
        imagem.ObjectKey = chaveNova;
        imagem.ContentType = contentType;
        imagem.Tamanho = tamanho;
        imagem.AtualizadoEm = DateTime.UtcNow;

        await LogAsync(AuditActions.Update, Entidade, imagem.Id, anterior, Snapshot(imagem), ct);
        await db.SaveChangesAsync(ct);

        await ApagarDoStorageAsync(chaveAntiga);
        return Ok(ToDto(imagem, await UsosAsync(ct)));
    }

    private async Task<bool> SalvarNoStorageAsync(Stream stream, string objectKey, string contentType, CancellationToken ct)
    {
        try
        {
            await storage.UploadAsync(stream, objectKey, contentType, ct);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Falha ao gravar a imagem {ObjectKey} no storage", objectKey);
            return false;
        }
    }

    private ObjectResult FalhaNoStorage() => StatusCode(StatusCodes.Status502BadGateway,
        new { message = "Não foi possível salvar a imagem no armazenamento. Tente de novo em instantes." });

    // jpg e jpeg são o mesmo tipo; o resto é 1:1 com o Content-Type.
    private static string ExtensaoDe(string contentType) => contentType switch
    {
        "image/jpeg" => "jpg",
        _ => contentType.Replace("image/", ""),
    };

    private async Task<PastaImagem> CriarPastaAsync(string nome, CancellationToken ct)
    {
        var pasta = new PastaImagem { Id = Guid.NewGuid(), Nome = nome, CriadoEm = DateTime.UtcNow };
        db.PastasImagens.Add(pasta);
        await LogAsync(AuditActions.Create, "PastaImagem", pasta.Id, null, new { pasta.Nome }, ct);
        await db.SaveChangesAsync(ct);
        return pasta;
    }

    private async Task<IReadOnlySet<string>> NomesDaPastaAsync(Guid pastaId, CancellationToken ct) =>
        (await db.ImagensPublicas.Where(i => i.PastaId == pastaId).Select(i => i.Nome).ToListAsync(ct)).ToHashSet();

    private static string NovaChave(string extensao) => $"imagens/{Guid.NewGuid():N}{extensao.ToLowerInvariant()}";

    // Falha ao apagar o arquivo antigo só deixa um objeto órfão no bucket — não desfaz a operação.
    private async Task ApagarDoStorageAsync(string objectKey)
    {
        try
        {
            await storage.DeleteAsync(objectKey);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Não foi possível apagar {ObjectKey} do storage", objectKey);
        }
    }

    // Guias e novidades que citam a URL (no texto, na capa ou na imagem). Poucas dezenas de
    // registros: carregar e procurar em memória é mais simples que um LIKE por imagem.
    private sealed record Usos(List<(string Texto, UsoImagemDto Uso)> Itens);

    private async Task<Usos> UsosAsync(CancellationToken ct)
    {
        var guias = await db.Artigos
            .Select(a => new { a.Titulo, a.Slug, a.Conteudo, a.CapaUrl, a.Id })
            .ToListAsync(ct);
        var novidades = await db.Novidades
            .Select(n => new { n.Titulo, n.Descricao, n.ImagemUrl })
            .ToListAsync(ct);

        var itens = guias.Select(g => (g.Conteudo + "\n" + g.CapaUrl,
                new UsoImagemDto("Guia", g.Titulo, $"/superadmin/guias.html?id={g.Id}")))
            .Concat(novidades.Select(n => (n.Descricao + "\n" + n.ImagemUrl,
                new UsoImagemDto("Novidade", n.Titulo, "/superadmin/novidades.html"))))
            .ToList();
        return new Usos(itens);
    }

    private static ImagemPublicaDto ToDto(ImagemPublica i, Usos usos) => ToDto(i,
        usos.Itens.Where(u => u.Texto.Contains(NomeImagemPublica.Url(i.Pasta.Nome, i.Nome), StringComparison.Ordinal))
            .Select(u => u.Uso).ToList());

    private static ImagemPublicaDto ToDto(ImagemPublica i, IReadOnlyList<UsoImagemDto> usos) => new(
        i.Id, i.PastaId, i.Pasta.Nome, i.Nome, NomeImagemPublica.Url(i.Pasta.Nome, i.Nome),
        i.ContentType, i.Tamanho, i.CriadoEm, i.AtualizadoEm, usos);

    private static object Snapshot(ImagemPublica i) => new { Pasta = i.Pasta?.Nome, i.Nome, i.ObjectKey, i.Tamanho };

    private Task LogAsync(string acao, string entidade, Guid id, object? anterior, object? novo, CancellationToken ct) =>
        audit.LogAsync(new AuditLogEntry(
            TenantConstants.SystemTenantId, GetSuperAdminId(), acao, entidade, id.ToString(),
            anterior, novo, HttpContext.GetClientIpAddress()), ct);

    private Guid? GetSuperAdminId() =>
        Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;
}
