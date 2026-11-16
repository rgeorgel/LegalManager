using LegalManager.API.Configuration;
using LegalManager.Application.DTOs.Agenda;
using LegalManager.Application.Interfaces;
using LegalManager.Domain.Entities;
using LegalManager.Domain.Enums;
using LegalManager.Domain.Interfaces;
using LegalManager.Infrastructure.Agenda;
using LegalManager.Infrastructure.Persistence;
using LegalManager.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace LegalManager.API.Controllers;

/// <summary>
/// Agenda no Google/Outlook/Apple: um link .ics privado por usuário (todos os planos). As rotas
/// autenticadas geram, ajustam e revogam o link; GET {token}.ics é o feed que o app de calendário
/// assina — anônimo, porque esses apps não mandam JWT. Token errado, revogado, usuário inativo
/// ou escritório suspenso: sempre 404, sem dizer qual.
/// </summary>
[ApiController]
[Route("api/agenda/feed")]
public class AgendaFeedController(
    AppDbContext db,
    ITenantContext tenantContext,
    FeedAgendaService feedService,
    IAuditService audit) : ControllerBase
{
    internal const string MensagemCliente = "O calendário é do portal do escritório.";
    internal const string MensagemImpersonacao = "Não é possível gerar o link do calendário durante a impersonação.";
    internal const string MensagemEscritorio = "Só administradores podem publicar a agenda do escritório inteiro.";

    private bool EhCliente => tenantContext.UserRole == PerfilUsuario.Cliente.ToString();
    private bool EhAdmin => tenantContext.UserRole == PerfilUsuario.Admin.ToString();

    [HttpGet]
    [Authorize]
    public async Task<ActionResult<FeedAgendaDto>> Obter(CancellationToken ct)
    {
        if (EhCliente) return StatusCode(403, new { message = MensagemCliente });
        var feed = await FeedDoUsuarioAsync(ct);
        return Ok(ToDto(feed));
    }

    /// <summary>Gera o link (ou um novo, derrubando o anterior). O caminho com o token só vem aqui.</summary>
    [HttpPost]
    [Authorize]
    public async Task<ActionResult<FeedAgendaGeradoDto>> Gerar([FromBody] ConfigurarFeedAgendaDto? dto, CancellationToken ct)
    {
        dto ??= new ConfigurarFeedAgendaDto();
        if (Bloqueio(dto.Escopo) is { } bloqueio) return bloqueio;

        // Um feed por usuário: gerar de novo troca o token na mesma linha (o link antigo morre).
        var feed = await FeedDoUsuarioAsync(ct);
        var anterior = feed == null ? null : new { feed.Escopo, feed.OcultarClientes };
        if (feed == null)
        {
            feed = new FeedAgenda { Id = Guid.NewGuid(), TenantId = tenantContext.TenantId, UsuarioId = tenantContext.UserId };
            db.FeedsAgenda.Add(feed);
        }

        var token = TokenFeedAgenda.Gerar();
        feed.TokenHash = TokenFeedAgenda.Hash(token);
        feed.Escopo = dto.Escopo;
        feed.OcultarClientes = dto.OcultarClientes ?? dto.Escopo == EscopoFeedAgenda.Escritorio;
        feed.CriadoEm = DateTime.UtcNow;
        feed.UltimoAcessoEm = null;
        await db.SaveChangesAsync(ct);

        await audit.LogAsync(tenantContext.CreateEntry(
            anterior == null ? AuditActions.Create : AuditActions.Update, AuditEntities.FeedAgenda, feed.Id,
            anterior,
            new { feed.Escopo, feed.OcultarClientes, TokenNovo = true }, HttpContext.GetClientIpAddress()), ct);

        return Ok(new FeedAgendaGeradoDto(FeedAgendaApi.Caminho(token), ToDto(feed)));
    }

    /// <summary>Muda o escopo ou a privacidade sem trocar o link já assinado.</summary>
    [HttpPut]
    [Authorize]
    public async Task<ActionResult<FeedAgendaDto>> Atualizar([FromBody] ConfigurarFeedAgendaDto dto, CancellationToken ct)
    {
        if (Bloqueio(dto.Escopo, exigeSemImpersonacao: false) is { } bloqueio) return bloqueio;

        var feed = await FeedDoUsuarioAsync(ct);
        if (feed == null) return NotFound();

        var antes = new { feed.Escopo, feed.OcultarClientes };
        feed.Escopo = dto.Escopo;
        feed.OcultarClientes = dto.OcultarClientes ?? dto.Escopo == EscopoFeedAgenda.Escritorio;
        await db.SaveChangesAsync(ct);

        await audit.LogAsync(tenantContext.CreateEntry(AuditActions.Update, AuditEntities.FeedAgenda, feed.Id,
            antes, new { feed.Escopo, feed.OcultarClientes }, HttpContext.GetClientIpAddress()), ct);
        return Ok(ToDto(feed));
    }

    [HttpDelete]
    [Authorize]
    public async Task<IActionResult> Revogar(CancellationToken ct)
    {
        if (EhCliente) return StatusCode(403, new { message = MensagemCliente });
        var feed = await FeedDoUsuarioAsync(ct);
        if (feed == null) return NoContent();

        db.FeedsAgenda.Remove(feed);
        await db.SaveChangesAsync(ct);
        await audit.LogAsync(tenantContext.CreateEntry(AuditActions.Delete, AuditEntities.FeedAgenda, feed.Id,
            new { feed.Escopo, feed.OcultarClientes }, null, HttpContext.GetClientIpAddress()), ct);
        return NoContent();
    }

    /// <summary>O feed .ics que o Google/Outlook/Apple buscam periodicamente.</summary>
    [HttpGet("{token}.ics")]
    [AllowAnonymous]
    [EnableRateLimiting(FeedAgendaApi.PoliticaRateLimit)]
    public async Task<IActionResult> Feed(string token, CancellationToken ct)
    {
        var arquivo = await feedService.MontarAsync(token, ct);
        Response.Headers["X-Robots-Tag"] = "noindex, nofollow";
        if (arquivo == null) return NotFound();

        Response.Headers.ETag = arquivo.ETag;
        Response.Headers.CacheControl = "private, no-cache";
        if (Request.Headers.IfNoneMatch.Any(v => v != null && v.Split(',').Any(e => e.Trim() == arquivo.ETag)))
            return StatusCode(StatusCodes.Status304NotModified);

        Response.Headers.ContentDisposition = "inline; filename=\"causify-agenda.ics\"";
        return Content(arquivo.Conteudo, "text/calendar; charset=utf-8");
    }

    private ActionResult? Bloqueio(EscopoFeedAgenda escopo, bool exigeSemImpersonacao = true)
    {
        if (EhCliente) return StatusCode(403, new { message = MensagemCliente });
        if (exigeSemImpersonacao && tenantContext.ImpersonadoPorId.HasValue)
            return StatusCode(403, new { message = MensagemImpersonacao });
        if (!Enum.IsDefined(escopo)) return BadRequest(new { message = "Escopo inválido." });
        if (escopo == EscopoFeedAgenda.Escritorio && !EhAdmin)
            return StatusCode(403, new { message = MensagemEscritorio });
        return null;
    }

    private Task<FeedAgenda?> FeedDoUsuarioAsync(CancellationToken ct) =>
        db.FeedsAgenda.FirstOrDefaultAsync(f => f.UsuarioId == tenantContext.UserId && f.TenantId == tenantContext.TenantId, ct);

    private FeedAgendaDto ToDto(FeedAgenda? feed) => feed == null
        ? new FeedAgendaDto(false, EscopoFeedAgenda.Meus, false, null, null, EhAdmin)
        : new FeedAgendaDto(true, feed.Escopo, feed.OcultarClientes, feed.CriadoEm, feed.UltimoAcessoEm, EhAdmin);
}
