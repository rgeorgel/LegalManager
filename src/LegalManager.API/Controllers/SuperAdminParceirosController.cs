using System.Security.Claims;
using LegalManager.Application.DTOs.Parceiros;
using LegalManager.Application.Interfaces;
using LegalManager.Domain;
using LegalManager.Domain.Entities;
using LegalManager.Domain.Enums;
using LegalManager.Infrastructure.Persistence;
using LegalManager.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LegalManager.API.Controllers;

// Programa de parceiros (ver Parceiro): cadastro, convite para o portal, vínculo manual de
// escritórios e o fluxo das solicitações de pagamento — o pagamento é feito por fora (Pix) e
// aqui só se registra que foi pago, ou se recusa a solicitação.
[ApiController]
[Route("api/superadmin/parceiros")]
[Authorize(Roles = "SuperAdmin")]
public class SuperAdminParceirosController(AppDbContext db, ParceiroService service, IAuditService audit) : ControllerBase
{
    private const string Entidade = "Parceiro";
    private const string EntidadeSolicitacao = "SolicitacaoPagamentoParceiro";

    [HttpGet]
    public async Task<ActionResult<List<ParceiroDto>>> Listar(CancellationToken ct)
    {
        var parceiros = await db.Parceiros.AsNoTracking().OrderBy(p => p.Nome).ToListAsync(ct);
        var lista = new List<ParceiroDto>();
        foreach (var p in parceiros) lista.Add(await ToDtoAsync(p, ct));
        return Ok(lista);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ParceiroDto>> Obter(Guid id, CancellationToken ct)
    {
        var parceiro = await db.Parceiros.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, ct);
        return parceiro is null ? NotFound() : Ok(await ToDtoAsync(parceiro, ct));
    }

    // Cria o parceiro e já envia o convite para ele definir a senha do portal.
    [HttpPost]
    public async Task<IActionResult> Criar([FromBody] SalvarParceiroDto dto, CancellationToken ct)
    {
        if (await ValidarUnicidadeAsync(dto, null, ct) is { } conflito) return conflito;

        var parceiro = new Parceiro
        {
            Id = Guid.NewGuid(),
            CriadoEm = DateTime.UtcNow,
            CriadoPorId = GetSuperAdminId()
        };
        Aplicar(parceiro, dto);
        db.Parceiros.Add(parceiro);
        await LogAsync(AuditActions.Create, Entidade, parceiro.Id, null, Snapshot(parceiro), ct);
        await db.SaveChangesAsync(ct);

        var link = await service.EnviarConviteAsync(parceiro, ct);
        return Ok(new { parceiro = await ToDtoAsync(parceiro, ct), link });
    }

    // Trocar o código não desvincula ninguém (o vínculo é por ParceiroId), mas o link antigo
    // de indicação deixa de funcionar.
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Atualizar(Guid id, [FromBody] SalvarParceiroDto dto, CancellationToken ct)
    {
        var parceiro = await db.Parceiros.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (parceiro is null) return NotFound();
        if (await ValidarUnicidadeAsync(dto, id, ct) is { } conflito) return conflito;

        var anterior = Snapshot(parceiro);
        Aplicar(parceiro, dto);
        parceiro.AtualizadoEm = DateTime.UtcNow;
        await LogAsync(AuditActions.Update, Entidade, parceiro.Id, anterior, Snapshot(parceiro), ct);
        await db.SaveChangesAsync(ct);

        return Ok(await ToDtoAsync(parceiro, ct));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Excluir(Guid id, CancellationToken ct)
    {
        var parceiro = await db.Parceiros.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (parceiro is null) return NotFound();

        if (await db.Tenants.AnyAsync(t => t.ParceiroId == id, ct)
            || await db.ComissoesParceiros.AnyAsync(c => c.ParceiroId == id, ct)
            || await db.SolicitacoesPagamentoParceiros.AnyAsync(s => s.ParceiroId == id, ct))
            return BadRequest(new { message = "Este parceiro já tem escritórios ou comissões e não pode ser excluído. Desative-o." });

        db.Parceiros.Remove(parceiro);
        await LogAsync(AuditActions.Delete, Entidade, parceiro.Id, Snapshot(parceiro), null, ct);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    // Reenvia o convite / gera um link novo de senha (invalida o anterior).
    [HttpPost("{id:guid}/convite")]
    public async Task<ActionResult<LinkSenhaParceiroDto>> EnviarConvite(Guid id, CancellationToken ct)
    {
        var parceiro = await db.Parceiros.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (parceiro is null) return NotFound();
        if (!parceiro.Ativo) return BadRequest(new { message = "Ative o parceiro antes de enviar o acesso." });

        return Ok(await service.EnviarConviteAsync(parceiro, ct));
    }

    // ── Escritórios ──────────────────────────────────────────────────────────

    [HttpGet("{id:guid}/escritorios")]
    public async Task<ActionResult<List<EscritorioIndicadoDto>>> Escritorios(Guid id, CancellationToken ct) =>
        Ok(await service.ListarEscritoriosAsync(id, ct));

    // Vínculo manual de um escritório que já tinha conta (ex.: indicado antes do programa existir).
    // Só pagamentos feitos a partir de agora geram comissão.
    [HttpPost("{id:guid}/escritorios")]
    public async Task<IActionResult> VincularEscritorio(Guid id, [FromBody] VincularEscritorioDto dto, CancellationToken ct)
    {
        var parceiro = await db.Parceiros.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (parceiro is null) return NotFound();

        var busca = dto.Busca.Trim();
        Guid? tenantId = Guid.TryParse(busca, out var gid) ? gid
            : await db.Users.Where(u => u.NormalizedEmail == busca.ToUpperInvariant())
                .Select(u => (Guid?)u.TenantId).FirstOrDefaultAsync(ct);

        var tenant = tenantId is null || tenantId == TenantConstants.SystemTenantId
            ? null
            : await db.Tenants.FirstOrDefaultAsync(t => t.Id == tenantId, ct);
        if (tenant is null)
            return NotFound(new { message = "Escritório não encontrado. Informe o ID do tenant ou o e-mail de um usuário dele." });

        if (tenant.ParceiroId == id)
            return BadRequest(new { message = $"{tenant.Nome} já está vinculado a este parceiro." });
        if (tenant.ParceiroId is not null)
            return BadRequest(new { message = $"{tenant.Nome} já está vinculado a outro parceiro. Desvincule-o primeiro." });

        tenant.ParceiroId = id;
        tenant.ParceiroVinculadoEm = DateTime.UtcNow;
        await LogAsync(AuditActions.Update, Entidade, id, null, new { Vinculou = tenant.Id, tenant.Nome }, ct);
        await db.SaveChangesAsync(ct);
        return Ok(new { tenantId = tenant.Id, nome = tenant.Nome });
    }

    // As comissões já geradas continuam do parceiro; só os próximos pagamentos deixam de comissionar.
    [HttpDelete("{id:guid}/escritorios/{tenantId:guid}")]
    public async Task<IActionResult> DesvincularEscritorio(Guid id, Guid tenantId, CancellationToken ct)
    {
        var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.Id == tenantId && t.ParceiroId == id, ct);
        if (tenant is null) return NotFound();

        tenant.ParceiroId = null;
        tenant.ParceiroVinculadoEm = null;
        await LogAsync(AuditActions.Update, Entidade, id, new { Desvinculou = tenant.Id, tenant.Nome }, null, ct);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    // ── Comissões e pagamentos ───────────────────────────────────────────────

    [HttpGet("{id:guid}/comissoes")]
    public async Task<ActionResult<List<ComissaoParceiroDto>>> Comissoes(Guid id, CancellationToken ct) =>
        Ok(await service.ListarComissoesAsync(id, ct));

    [HttpGet("{id:guid}/solicitacoes")]
    public async Task<ActionResult<List<SolicitacaoPagamentoDto>>> SolicitacoesDoParceiro(Guid id, CancellationToken ct) =>
        Ok(await service.ListarSolicitacoesAsync(id, ct: ct));

    [HttpGet("solicitacoes")]
    public async Task<ActionResult<List<SolicitacaoPagamentoDto>>> Solicitacoes(
        [FromQuery] StatusSolicitacaoPagamento? status, CancellationToken ct) =>
        Ok(await service.ListarSolicitacoesAsync(null, status, ct));

    // Registra que o Pix foi feito. Resposta: comprovante / id da transação (o parceiro vê).
    [HttpPost("solicitacoes/{solicitacaoId:guid}/pagar")]
    public async Task<IActionResult> MarcarPaga(Guid solicitacaoId, [FromBody] ResolverSolicitacaoDto dto, CancellationToken ct)
    {
        var solicitacao = await db.SolicitacoesPagamentoParceiros.FirstOrDefaultAsync(s => s.Id == solicitacaoId, ct);
        if (solicitacao is null) return NotFound();
        if (solicitacao.Status != StatusSolicitacaoPagamento.Pendente)
            return BadRequest(new { message = "Esta solicitação já foi resolvida." });

        Resolver(solicitacao, StatusSolicitacaoPagamento.Paga, dto.Resposta);
        await LogAsync(AuditActions.Update, EntidadeSolicitacao, solicitacao.Id,
            new { Status = nameof(StatusSolicitacaoPagamento.Pendente) },
            new { Status = solicitacao.Status.ToString(), solicitacao.Valor, solicitacao.ParceiroId, solicitacao.Resposta }, ct);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    // Recusa: as comissões voltam a ficar disponíveis para uma nova solicitação.
    [HttpPost("solicitacoes/{solicitacaoId:guid}/recusar")]
    public async Task<IActionResult> Recusar(Guid solicitacaoId, [FromBody] ResolverSolicitacaoDto dto, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(dto.Resposta))
            return BadRequest(new { message = "Informe o motivo da recusa — o parceiro verá esta mensagem." });

        var solicitacao = await db.SolicitacoesPagamentoParceiros
            .Include(s => s.Comissoes)
            .FirstOrDefaultAsync(s => s.Id == solicitacaoId, ct);
        if (solicitacao is null) return NotFound();
        if (solicitacao.Status != StatusSolicitacaoPagamento.Pendente)
            return BadRequest(new { message = "Esta solicitação já foi resolvida." });

        Resolver(solicitacao, StatusSolicitacaoPagamento.Recusada, dto.Resposta);
        foreach (var c in solicitacao.Comissoes) c.SolicitacaoId = null;
        await LogAsync(AuditActions.Update, EntidadeSolicitacao, solicitacao.Id,
            new { Status = nameof(StatusSolicitacaoPagamento.Pendente) },
            new { Status = solicitacao.Status.ToString(), solicitacao.Valor, solicitacao.ParceiroId, solicitacao.Resposta }, ct);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    private void Resolver(SolicitacaoPagamentoParceiro s, StatusSolicitacaoPagamento status, string? resposta)
    {
        s.Status = status;
        s.ResolvidaEm = DateTime.UtcNow;
        s.ResolvidaPorId = GetSuperAdminId();
        s.Resposta = string.IsNullOrWhiteSpace(resposta) ? null : resposta.Trim();
    }

    // ── Auxiliares ───────────────────────────────────────────────────────────

    private async Task<ObjectResult?> ValidarUnicidadeAsync(SalvarParceiroDto dto, Guid? id, CancellationToken ct)
    {
        var codigo = Parceiro.Normalizar(dto.Codigo);
        if (await db.Parceiros.AnyAsync(p => p.Codigo == codigo && p.Id != id, ct))
            return Conflict(new { message = "Já existe um parceiro com esse código." });

        var email = dto.Email.Trim().ToLowerInvariant();
        if (await db.Parceiros.AnyAsync(p => p.Email == email && p.Id != id, ct))
            return Conflict(new { message = "Já existe um parceiro com esse e-mail." });
        return null;
    }

    private static void Aplicar(Parceiro p, SalvarParceiroDto dto)
    {
        static string? Limpo(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();
        p.Nome = dto.Nome.Trim();
        p.Email = dto.Email.Trim().ToLowerInvariant();
        p.Telefone = Limpo(dto.Telefone);
        p.Documento = Limpo(dto.Documento);
        p.Codigo = Parceiro.Normalizar(dto.Codigo);
        p.PercentualComissao = dto.PercentualComissao;
        p.MesesComissao = dto.MesesComissao;
        p.ChavePix = Limpo(dto.ChavePix);
        p.Observacoes = Limpo(dto.Observacoes);
        p.Ativo = dto.Ativo;
    }

    private async Task<ParceiroDto> ToDtoAsync(Parceiro p, CancellationToken ct)
    {
        var totais = await ComissoesParceiros.CalcularTotaisAsync(db, p.Id, ct);
        var escritorios = await db.Tenants.AsNoTracking().Where(t => t.ParceiroId == p.Id).ToListAsync(ct);
        return new ParceiroDto(
            p.Id, p.Nome, p.Email, p.Telefone, p.Documento, p.Codigo, p.PercentualComissao, p.MesesComissao,
            p.ChavePix, p.Observacoes, p.Ativo, p.SenhaHash != null, p.CriadoEm, p.UltimoAcessoEm,
            escritorios.Count, escritorios.Count(t => ComissoesParceiros.SituacaoEscritorio(t) == "Assinante"),
            totais.Total, totais.Disponivel, totais.Solicitado, totais.Pago);
    }

    private static object Snapshot(Parceiro p) => new
    {
        p.Nome, p.Email, p.Telefone, p.Documento, p.Codigo, p.PercentualComissao, p.MesesComissao, p.ChavePix, p.Ativo
    };

    private Task LogAsync(string acao, string entidade, Guid id, object? anterior, object? novo, CancellationToken ct) =>
        audit.LogAsync(new AuditLogEntry(
            TenantConstants.SystemTenantId, GetSuperAdminId(), acao, entidade, id.ToString(),
            anterior, novo, HttpContext.GetClientIpAddress()), ct);

    private Guid? GetSuperAdminId() =>
        Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;
}
