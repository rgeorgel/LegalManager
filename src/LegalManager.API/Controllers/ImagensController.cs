using LegalManager.Application.Interfaces;
using LegalManager.Infrastructure.Persistence;
using LegalManager.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Net.Http.Headers;

namespace LegalManager.API.Controllers;

// URL pública das imagens da biblioteca (superadmin → Imagens): /imagens/{pasta}/{nome}.
// A imagem pode ser substituída mantendo a URL, então o cache é curto (1 h) e validado por
// ETag — o navegador revalida sem baixar de novo, e o storage só é lido quando o arquivo mudou.
[ApiController]
[AllowAnonymous]
public class ImagensController(AppDbContext db, IStorageService storage, ILogger<ImagensController> logger) : ControllerBase
{
    public const string CachePublico = "public, max-age=3600";

    [HttpGet("/imagens/{pasta}/{nome}")]
    public async Task<IActionResult> Obter(string pasta, string nome, CancellationToken ct)
    {
        if (!NomeImagemPublica.PastaValida(pasta) || !NomeImagemPublica.NomeValido(nome)) return NotFound();

        var imagem = await db.ImagensPublicas.AsNoTracking()
            .Where(i => i.Pasta.Nome == pasta && i.Nome == nome)
            .Select(i => new { i.Id, i.ObjectKey, i.ContentType, i.AtualizadoEm })
            .FirstOrDefaultAsync(ct);
        if (imagem == null) return NotFound();

        var etag = new EntityTagHeaderValue($"\"{imagem.Id:N}-{imagem.AtualizadoEm.Ticks}\"");
        Response.Headers.CacheControl = CachePublico;
        Response.Headers.ETag = etag.ToString();

        var ifNoneMatch = Request.Headers.IfNoneMatch.ToString();
        if (!string.IsNullOrEmpty(ifNoneMatch) && EntityTagHeaderValue.TryParseList([.. ifNoneMatch.Split(',')], out var tags)
            && tags.Any(t => t.Compare(etag, useStrongComparison: false)))
            return StatusCode(StatusCodes.Status304NotModified);

        Stream stream;
        try
        {
            stream = await storage.DownloadAsync(imagem.ObjectKey, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Imagem {Pasta}/{Nome} não encontrada no storage ({ObjectKey})", pasta, nome, imagem.ObjectKey);
            Response.Headers.Remove("ETag");
            Response.Headers.CacheControl = "no-store";
            return NotFound();
        }

        return File(stream, imagem.ContentType);
    }
}
