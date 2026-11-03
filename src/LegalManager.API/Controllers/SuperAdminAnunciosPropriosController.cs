using System.Security.Claims;
using System.Text.RegularExpressions;
using LegalManager.Application.DTOs.Publicidade;
using LegalManager.Application.Interfaces;
using LegalManager.Domain;
using LegalManager.Domain.Entities;
using LegalManager.Infrastructure.Persistence;
using LegalManager.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LegalManager.API.Controllers;

// Cadastro dos anúncios do próprio Causify (ver AnuncioProprio) e da mistura com o Adsterra
// (ConfiguracaoPublicidade). O portal recebe os dois em PublicidadeController.Get.
[ApiController]
[Route("api/superadmin/anuncios-proprios")]
[Authorize(Roles = "SuperAdmin")]
public partial class SuperAdminAnunciosPropriosController(AppDbContext db, IAuditService audit) : ControllerBase
{
    private const string Entidade = "AnuncioProprio";

    [HttpGet]
    public async Task<ActionResult<List<AnuncioProprioAdminDto>>> Listar(CancellationToken ct)
    {
        // Ativos primeiro, depois do mais recente para o mais antigo.
        var anuncios = await db.AnunciosProprios
            .OrderByDescending(a => a.Ativo)
            .ThenByDescending(a => a.CriadoEm)
            .ToListAsync(ct);

        return Ok(anuncios.Select(ToDto).ToList());
    }

    [HttpPost]
    public async Task<ActionResult<AnuncioProprioAdminDto>> Criar([FromBody] SalvarAnuncioProprioDto dto, CancellationToken ct)
    {
        if (Validar(dto) is { } erro) return BadRequest(new { message = erro });

        var anuncio = new AnuncioProprio
        {
            Id = Guid.NewGuid(),
            CriadoEm = DateTime.UtcNow,
            CriadoPorId = GetSuperAdminId()
        };
        Aplicar(anuncio, dto);

        db.AnunciosProprios.Add(anuncio);
        await LogAsync(AuditActions.Create, anuncio, null, ct);
        await db.SaveChangesAsync(ct);

        return Ok(ToDto(anuncio));
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<AnuncioProprioAdminDto>> Atualizar(Guid id, [FromBody] SalvarAnuncioProprioDto dto, CancellationToken ct)
    {
        if (Validar(dto) is { } erro) return BadRequest(new { message = erro });

        var anuncio = await db.AnunciosProprios.FirstOrDefaultAsync(a => a.Id == id, ct);
        if (anuncio == null) return NotFound();

        var anterior = Snapshot(anuncio);
        Aplicar(anuncio, dto);
        anuncio.AtualizadoEm = DateTime.UtcNow;

        await LogAsync(AuditActions.Update, anuncio, anterior, ct);
        await db.SaveChangesAsync(ct);

        return Ok(ToDto(anuncio));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Excluir(Guid id, CancellationToken ct)
    {
        var anuncio = await db.AnunciosProprios.FirstOrDefaultAsync(a => a.Id == id, ct);
        if (anuncio == null) return NotFound();

        db.AnunciosProprios.Remove(anuncio);
        await LogAsync(AuditActions.Delete, anuncio, Snapshot(anuncio), ct);
        await db.SaveChangesAsync(ct);

        return NoContent();
    }

    // Quantos % dos espaços mostram direto um anúncio próprio, em vez de tentar o Adsterra.
    [HttpGet("configuracao")]
    public async Task<ActionResult<ConfiguracaoPublicidadeDto>> ObterConfiguracao(CancellationToken ct)
    {
        var config = await db.ConfiguracoesPublicidade.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == ConfiguracaoPublicidade.IdUnico, ct);
        return Ok(new ConfiguracaoPublicidadeDto(config?.PercentualProprios ?? 0, config?.AtualizadoEm));
    }

    [HttpPut("configuracao")]
    public async Task<ActionResult<ConfiguracaoPublicidadeDto>> SalvarConfiguracao(
        [FromBody] SalvarConfiguracaoPublicidadeDto dto, CancellationToken ct)
    {
        if (dto.PercentualProprios is < 0 or > 100)
            return BadRequest(new { message = "O percentual deve ficar entre 0 e 100." });

        var config = await db.ConfiguracoesPublicidade.FirstOrDefaultAsync(c => c.Id == ConfiguracaoPublicidade.IdUnico, ct);
        var anterior = config is null ? null : new { config.PercentualProprios };
        if (config is null)
        {
            config = new ConfiguracaoPublicidade { Id = ConfiguracaoPublicidade.IdUnico };
            db.ConfiguracoesPublicidade.Add(config);
        }
        config.PercentualProprios = dto.PercentualProprios;
        config.AtualizadoEm = DateTime.UtcNow;
        config.AtualizadoPorId = GetSuperAdminId();

        await audit.LogAsync(new AuditLogEntry(
            TenantConstants.SystemTenantId, GetSuperAdminId(), AuditActions.Update, "ConfiguracaoPublicidade",
            config.Id.ToString(), anterior, new { config.PercentualProprios },
            HttpContext.GetClientIpAddress()), ct);
        await db.SaveChangesAsync(ct);

        return Ok(new ConfiguracaoPublicidadeDto(config.PercentualProprios, config.AtualizadoEm));
    }

    public static string? Validar(SalvarAnuncioProprioDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Titulo)) return "Informe o título.";
        if (string.IsNullOrWhiteSpace(dto.Texto)) return "Informe o texto.";
        if (string.IsNullOrWhiteSpace(dto.TextoBotao)) return "Informe o texto do botão.";
        if (string.IsNullOrWhiteSpace(dto.LinkUrl)) return "Informe o link.";

        // Caminho do sistema ou https. "//outro-site.com" (relativa ao protocolo) e "/\..." não.
        var link = dto.LinkUrl.Trim();
        var caminhoLocal = link.StartsWith('/') && !link.StartsWith("//") && !link.StartsWith("/\\");
        var https = Uri.TryCreate(link, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps;
        if (!caminhoLocal && !https)
            return "O link deve ser um caminho do sistema (ex: /pages/assinatura.html) ou uma URL https.";
        if (Preenchido(dto.Cor) is { } cor && !CorHex().IsMatch(cor))
            return "A cor deve estar no formato #RRGGBB.";
        return null;
    }

    [GeneratedRegex("^#[0-9A-Fa-f]{6}$")]
    private static partial Regex CorHex();

    private static string? Preenchido(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private static void Aplicar(AnuncioProprio a, SalvarAnuncioProprioDto dto)
    {
        a.Icone = Preenchido(dto.Icone);
        a.Titulo = dto.Titulo.Trim();
        a.Texto = dto.Texto.Trim();
        a.TextoBotao = dto.TextoBotao.Trim();
        a.LinkUrl = dto.LinkUrl.Trim();
        a.Cor = Preenchido(dto.Cor)?.ToLowerInvariant();
        a.Ativo = dto.Ativo;
    }

    private static AnuncioProprioAdminDto ToDto(AnuncioProprio a) => new(
        a.Id, a.Icone, a.Titulo, a.Texto, a.TextoBotao, a.LinkUrl, a.Cor, a.Ativo, a.CriadoEm);

    private static object Snapshot(AnuncioProprio a) => new
    {
        a.Icone, a.Titulo, a.Texto, a.TextoBotao, a.LinkUrl, a.Cor, a.Ativo
    };

    private Task LogAsync(string acao, AnuncioProprio a, object? anterior, CancellationToken ct) =>
        audit.LogAsync(new AuditLogEntry(
            TenantConstants.SystemTenantId, GetSuperAdminId(), acao, Entidade, a.Id.ToString(),
            anterior, acao == AuditActions.Delete ? null : Snapshot(a),
            HttpContext.GetClientIpAddress()), ct);

    private Guid? GetSuperAdminId() =>
        Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;
}
