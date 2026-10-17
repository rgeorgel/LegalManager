using System.Security.Claims;
using LegalManager.Application.DTOs.Planos;
using LegalManager.Application.Interfaces;
using LegalManager.Domain;
using LegalManager.Domain.Entities;
using LegalManager.Infrastructure.Persistence;
using LegalManager.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LegalManager.API.Controllers;

// Preços de tabela dos planos (ver PrecoPlano). Mudar um preço vale para novos checkouts, trocas
// de plano e landing pages; quem já assina continua pagando o valor antigo nas renovações.
[ApiController]
[Route("api/superadmin/precos-planos")]
[Authorize(Roles = "SuperAdmin")]
public class SuperAdminPrecosPlanosController(AppDbContext db, IPrecosPlanosService precosPlanos, IAuditService audit)
    : ControllerBase
{
    private const string Entidade = "PrecoPlano";

    [HttpGet]
    public async Task<ActionResult<List<PrecoPlanoDto>>> Listar(CancellationToken ct)
    {
        var linhas = await db.PrecosPlanos.AsNoTracking().ToDictionaryAsync(p => p.Plano, ct);
        var precos = await precosPlanos.ObterAsync(ct);

        return Ok(PlanoPrecos.Pagos
            .Select(p => new PrecoPlanoDto(p, precos[p], linhas.GetValueOrDefault(p)?.AtualizadoEm))
            .ToList());
    }

    [HttpPut]
    public async Task<ActionResult<List<PrecoPlanoDto>>> Salvar([FromBody] List<SalvarPrecoPlanoDto> dto, CancellationToken ct)
    {
        if (dto.Any(p => !PlanoPrecos.Pagos.Contains(p.Plano)))
            return BadRequest(new { message = "Só os planos Plus, Pro e Max têm preço." });
        if (dto.Select(p => p.Plano).Distinct().Count() != dto.Count)
            return BadRequest(new { message = "Plano repetido." });
        if (dto.Any(p => decimal.Round(p.ValorMensal, 2) != p.ValorMensal))
            return BadRequest(new { message = "Use no máximo dois dígitos de centavos." });

        var linhas = await db.PrecosPlanos.ToDictionaryAsync(p => p.Plano, ct);
        var adminId = GetSuperAdminId();
        var agora = DateTime.UtcNow;

        foreach (var item in dto)
        {
            linhas.TryGetValue(item.Plano, out var linha);
            if (linha?.ValorMensal == item.ValorMensal) continue;

            var anterior = linha is null ? null : new { Plano = item.Plano.ToString(), linha.ValorMensal };
            if (linha is null)
            {
                linha = new PrecoPlano { Plano = item.Plano };
                db.PrecosPlanos.Add(linha);
            }
            linha.ValorMensal = item.ValorMensal;
            linha.AtualizadoEm = agora;
            linha.AtualizadoPorId = adminId;

            await audit.LogAsync(new AuditLogEntry(
                TenantConstants.SystemTenantId, adminId, AuditActions.Update, Entidade, item.Plano.ToString(),
                anterior, new { Plano = item.Plano.ToString(), item.ValorMensal },
                HttpContext.GetClientIpAddress()), ct);
        }

        await db.SaveChangesAsync(ct);
        precosPlanos.Invalidar();

        return await Listar(ct);
    }

    private Guid? GetSuperAdminId() =>
        Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;
}
