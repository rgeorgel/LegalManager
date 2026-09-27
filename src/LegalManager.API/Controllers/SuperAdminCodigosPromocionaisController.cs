using System.Security.Claims;
using LegalManager.Application.DTOs.CodigosPromocionais;
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

// Códigos promocionais (ver CodigoPromocional). Os usos são contados pelos tenants que
// referenciam o Codigo (ver CodigosPromocionais.UsadoPor), por isso um código já usado não pode ser
// renomeado nem excluído — só desativado. Desativar impede novos cadastros, mas quem já
// se cadastrou com o código mantém o desconto pendente para a primeira assinatura.
[ApiController]
[Route("api/superadmin/codigos-promocionais")]
[Authorize(Roles = "SuperAdmin")]
public class SuperAdminCodigosPromocionaisController(AppDbContext db, IAuditService audit) : ControllerBase
{
    private const string Entidade = "CodigoPromocional";

    [HttpGet]
    public async Task<ActionResult<List<CodigoPromocionalDto>>> Listar(CancellationToken ct)
    {
        var codigos = await db.CodigosPromocionais
            .OrderByDescending(c => c.CriadoEm)
            .ToListAsync(ct);

        var usos = await ContarUsosAsync(codigos.Select(c => c.Codigo).ToList(), ct);

        return Ok(codigos.Select(c => ToDto(c, usos.GetValueOrDefault(c.Codigo))).ToList());
    }

    [HttpPost]
    public async Task<ActionResult<CodigoPromocionalDto>> Criar([FromBody] SalvarCodigoPromocionalDto dto, CancellationToken ct)
    {
        if (Validar(dto) is { } erro) return BadRequest(new { message = erro });

        var codigo = CodigoPromocional.Normalizar(dto.Codigo);
        if (await db.CodigosPromocionais.AnyAsync(c => c.Codigo == codigo, ct))
            return Conflict(new { message = "Já existe um código promocional com esse nome." });

        var promo = new CodigoPromocional
        {
            Id = Guid.NewGuid(),
            Codigo = codigo,
            CriadoEm = DateTime.UtcNow,
            CriadoPorId = GetSuperAdminId()
        };
        Aplicar(promo, dto);

        db.CodigosPromocionais.Add(promo);
        await LogAsync(AuditActions.Create, promo, null, ct);
        await db.SaveChangesAsync(ct);

        var usosPrevios = await db.Tenants.CountAsync(CodigosPromocionais.UsadoPor(codigo), ct);
        return Ok(ToDto(promo, usosPrevios));
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<CodigoPromocionalDto>> Atualizar(Guid id, [FromBody] SalvarCodigoPromocionalDto dto, CancellationToken ct)
    {
        if (Validar(dto) is { } erro) return BadRequest(new { message = erro });

        var promo = await db.CodigosPromocionais.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (promo == null) return NotFound();

        var anterior = Snapshot(promo);
        var usos = await db.Tenants.CountAsync(CodigosPromocionais.UsadoPor(promo.Codigo), ct);

        var codigo = CodigoPromocional.Normalizar(dto.Codigo);
        if (codigo != promo.Codigo)
        {
            if (usos > 0)
                return BadRequest(new { message = "Este código já foi usado e não pode ser renomeado. Crie um novo código." });
            if (await db.CodigosPromocionais.AnyAsync(c => c.Codigo == codigo && c.Id != id, ct))
                return Conflict(new { message = "Já existe um código promocional com esse nome." });
            promo.Codigo = codigo;
        }

        Aplicar(promo, dto);
        promo.AtualizadoEm = DateTime.UtcNow;

        await LogAsync(AuditActions.Update, promo, anterior, ct);
        await db.SaveChangesAsync(ct);

        return Ok(ToDto(promo, usos));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Excluir(Guid id, CancellationToken ct)
    {
        var promo = await db.CodigosPromocionais.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (promo == null) return NotFound();

        if (await db.Tenants.AnyAsync(CodigosPromocionais.UsadoPor(promo.Codigo), ct))
            return BadRequest(new { message = "Este código já foi usado e não pode ser excluído. Desative-o para impedir novos usos." });

        db.CodigosPromocionais.Remove(promo);
        await LogAsync(AuditActions.Delete, promo, Snapshot(promo), ct);
        await db.SaveChangesAsync(ct);

        return NoContent();
    }

    [HttpGet("{id:guid}/usos")]
    public async Task<ActionResult<List<CodigoPromocionalUsoDto>>> Usos(Guid id, CancellationToken ct)
    {
        var promo = await db.CodigosPromocionais.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (promo == null) return NotFound();

        var usos = await db.Tenants
            .Where(CodigosPromocionais.UsadoPor(promo.Codigo))
            .OrderByDescending(t => t.CriadoEm)
            .Select(t => new CodigoPromocionalUsoDto(
                t.Id,
                t.Nome,
                db.Users
                    .Where(u => u.TenantId == t.Id && u.Perfil == PerfilUsuario.Admin)
                    .Select(u => u.Email)
                    .FirstOrDefault(),
                t.CriadoEm,
                t.VoucherUtilizado == promo.Codigo ? t.PlanoExpiraEm : null,
                t.CodigoDesconto == promo.Codigo ? t.DescontoPromocionalUsadoEm : null,
                t.VoucherUtilizado == promo.Codigo))
            .ToListAsync(ct);

        return Ok(usos);
    }

    private static string? Validar(SalvarCodigoPromocionalDto dto)
    {
        if (dto.DiasGratuitos <= 0 && dto.DescontoPercentual is null)
            return "Defina ao menos um benefício: dias grátis ou desconto.";

        // Free não é benefício; Enterprise não é oferecido no cadastro direto (ver AuthService).
        if (dto.DiasGratuitos > 0 && dto.Plano is not (PlanoTipo.Plus or PlanoTipo.Pro or PlanoTipo.Max))
            return "O plano do período grátis deve ser Plus, Pro ou Max.";
        return null;
    }

    private static void Aplicar(CodigoPromocional promo, SalvarCodigoPromocionalDto dto)
    {
        promo.Descricao = string.IsNullOrWhiteSpace(dto.Descricao) ? null : dto.Descricao.Trim();
        promo.DiasGratuitos = dto.DiasGratuitos;
        promo.Plano = dto.DiasGratuitos > 0 ? dto.Plano : null;
        promo.DescontoPercentual = dto.DescontoPercentual;
        promo.DescontoMeses = dto.DescontoPercentual.HasValue ? dto.DescontoMeses : null;
        promo.MaxUsos = dto.MaxUsos;
        promo.ValidoAte = dto.ValidoAte;
        promo.Ativo = dto.Ativo;
    }

    // Um tenant conta uma vez por código, mesmo que o tenha no cadastro e no desconto.
    private async Task<Dictionary<string, int>> ContarUsosAsync(List<string> codigos, CancellationToken ct)
    {
        var refs = await db.Tenants
            .Where(t => (t.VoucherUtilizado != null && codigos.Contains(t.VoucherUtilizado))
                     || (t.CodigoDesconto != null && codigos.Contains(t.CodigoDesconto)))
            .Select(t => new { t.VoucherUtilizado, t.CodigoDesconto })
            .ToListAsync(ct);

        return refs
            .SelectMany(r => new[] { r.VoucherUtilizado, r.CodigoDesconto }.Where(c => c != null).Distinct())
            .GroupBy(c => c!)
            .ToDictionary(g => g.Key, g => g.Count());
    }

    private static CodigoPromocionalDto ToDto(CodigoPromocional c, int usos) => new(
        c.Id, c.Codigo, c.Descricao, c.Plano, c.DiasGratuitos, c.DescontoPercentual, c.DescontoMeses,
        c.MaxUsos, usos, c.ValidoAte, c.Ativo, c.CriadoEm);

    private static object Snapshot(CodigoPromocional c) => new
    {
        c.Codigo, c.Descricao, Plano = c.Plano?.ToString(), c.DiasGratuitos, c.DescontoPercentual, c.DescontoMeses,
        c.MaxUsos, c.ValidoAte, c.Ativo
    };

    private Task LogAsync(string acao, CodigoPromocional promo, object? anterior, CancellationToken ct) =>
        audit.LogAsync(new AuditLogEntry(
            TenantConstants.SystemTenantId, GetSuperAdminId(), acao, Entidade, promo.Id.ToString(),
            anterior, acao == AuditActions.Delete ? null : Snapshot(promo),
            HttpContext.GetClientIpAddress()), ct);

    private Guid? GetSuperAdminId() =>
        Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;
}
