using System.Security.Claims;
using LegalManager.Application.DTOs.Novidades;
using LegalManager.Application.Interfaces;
using LegalManager.Domain;
using LegalManager.Domain.Entities;
using LegalManager.Infrastructure.Persistence;
using LegalManager.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LegalManager.API.Controllers;

// Cadastro das novidades do produto (ver Novidade). O portal lê só as publicadas em
// NovidadesController.
[ApiController]
[Route("api/superadmin/novidades")]
[Authorize(Roles = "SuperAdmin")]
public class SuperAdminNovidadesController(AppDbContext db, IAuditService audit) : ControllerBase
{
    private const string Entidade = "Novidade";

    [HttpGet]
    public async Task<ActionResult<List<NovidadeAdminDto>>> Listar(CancellationToken ct)
    {
        // Rascunhos (sem PublicadaEm) primeiro, depois as publicadas da mais recente para a mais antiga.
        var novidades = await db.Novidades
            .OrderBy(n => n.PublicadaEm != null)
            .ThenByDescending(n => n.PublicadaEm)
            .ThenByDescending(n => n.CriadoEm)
            .ToListAsync(ct);

        return Ok(novidades.Select(ToDto).ToList());
    }

    [HttpPost]
    public async Task<ActionResult<NovidadeAdminDto>> Criar([FromBody] SalvarNovidadeDto dto, CancellationToken ct)
    {
        if (Validar(dto) is { } erro) return BadRequest(new { message = erro });

        var novidade = new Novidade
        {
            Id = Guid.NewGuid(),
            CriadoEm = DateTime.UtcNow,
            CriadoPorId = GetSuperAdminId()
        };
        Aplicar(novidade, dto);

        db.Novidades.Add(novidade);
        await LogAsync(AuditActions.Create, novidade, null, ct);
        await db.SaveChangesAsync(ct);

        return Ok(ToDto(novidade));
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<NovidadeAdminDto>> Atualizar(Guid id, [FromBody] SalvarNovidadeDto dto, CancellationToken ct)
    {
        if (Validar(dto) is { } erro) return BadRequest(new { message = erro });

        var novidade = await db.Novidades.FirstOrDefaultAsync(n => n.Id == id, ct);
        if (novidade == null) return NotFound();

        var anterior = Snapshot(novidade);
        Aplicar(novidade, dto);
        novidade.AtualizadoEm = DateTime.UtcNow;

        await LogAsync(AuditActions.Update, novidade, anterior, ct);
        await db.SaveChangesAsync(ct);

        return Ok(ToDto(novidade));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Excluir(Guid id, CancellationToken ct)
    {
        var novidade = await db.Novidades.FirstOrDefaultAsync(n => n.Id == id, ct);
        if (novidade == null) return NotFound();

        db.Novidades.Remove(novidade);
        await LogAsync(AuditActions.Delete, novidade, Snapshot(novidade), ct);
        await db.SaveChangesAsync(ct);

        return NoContent();
    }

    internal static string? Validar(SalvarNovidadeDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Titulo)) return "Informe o título.";
        if (string.IsNullOrWhiteSpace(dto.Descricao)) return "Informe a descrição.";

        // Imagem: arquivo do próprio site ou https. Link: só caminhos internos do portal —
        // o botão abre na mesma aba, então não deve levar o usuário para fora do sistema.
        if (Preenchido(dto.ImagemUrl) is { } img && !CaminhoLocal(img) && !img.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return "A imagem deve ser um caminho do site (ex: /images/novidade.gif) ou uma URL https.";
        if (Preenchido(dto.LinkUrl) is { } link && !CaminhoLocal(link))
            return "O link deve ser um caminho do sistema, ex: /pages/processos.html.";
        return null;
    }

    private static string? Preenchido(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    // "/pages/x.html" sim; "//outro-site.com" (URL relativa ao protocolo) e "/\..." não.
    private static bool CaminhoLocal(string s) => s.StartsWith('/') && !s.StartsWith("//") && !s.StartsWith("/\\");

    private static void Aplicar(Novidade n, SalvarNovidadeDto dto)
    {
        n.Titulo = dto.Titulo.Trim();
        n.Descricao = dto.Descricao.Trim();
        n.ImagemUrl = Preenchido(dto.ImagemUrl);
        n.LinkUrl = Preenchido(dto.LinkUrl);
        n.LinkTexto = n.LinkUrl == null ? null : Preenchido(dto.LinkTexto);
        n.PlanoMinimo = dto.PlanoMinimo;
        n.Destaque = dto.Destaque;
        n.TourId = Preenchido(dto.TourId);
        n.Publicada = dto.Publicada;
        if (dto.Publicada && n.PublicadaEm == null) n.PublicadaEm = DateTime.UtcNow;
    }

    private static NovidadeAdminDto ToDto(Novidade n) => new(
        n.Id, n.Titulo, n.Descricao, n.ImagemUrl, n.LinkUrl, n.LinkTexto, n.PlanoMinimo,
        n.Destaque, n.TourId, n.Publicada, n.PublicadaEm, n.CriadoEm);

    private static object Snapshot(Novidade n) => new
    {
        n.Titulo, n.Descricao, n.ImagemUrl, n.LinkUrl, n.LinkTexto,
        PlanoMinimo = n.PlanoMinimo?.ToString(), n.Destaque, n.TourId, n.Publicada, n.PublicadaEm
    };

    private Task LogAsync(string acao, Novidade n, object? anterior, CancellationToken ct) =>
        audit.LogAsync(new AuditLogEntry(
            TenantConstants.SystemTenantId, GetSuperAdminId(), acao, Entidade, n.Id.ToString(),
            anterior, acao == AuditActions.Delete ? null : Snapshot(n),
            HttpContext.GetClientIpAddress()), ct);

    private Guid? GetSuperAdminId() =>
        Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;
}
