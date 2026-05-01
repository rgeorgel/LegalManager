using LegalManager.Domain.Entities;
using LegalManager.Domain.Interfaces;
using LegalManager.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LegalManager.API.Controllers;

[ApiController]
[Route("api/honorarios")]
[Authorize]
public class HonorariosController(AppDbContext db, ITenantContext tenantContext) : ControllerBase
{
    [HttpGet("historico")]
    public async Task<IActionResult> GetHistorico(CancellationToken ct)
    {
        var items = await db.HonorariosCalculos
            .Where(h => h.TenantId == tenantContext.TenantId && h.UsuarioId == tenantContext.UserId)
            .OrderByDescending(h => h.CriadoEm)
            .Take(15)
            .Select(h => new
            {
                h.Id,
                ts = h.CriadoEm.ToLocalTime().ToString("dd/MM/yyyy HH:mm"),
                h.Cliente,
                h.Mode,
                h.Area,
                h.Tipo,
                h.ItemOAB,
                h.ValorCausa,
                h.HonorarioBase,
                h.HonorarioAjustado,
                h.TotalCorrigido,
                h.Multiplicador,
                h.TaxaCorrecao,
                h.FormaPagamento,
                h.Complexidade,
                h.Risco,
                h.Urgencia,
                h.Capacidade,
                h.Exito,
                h.StateJson
            })
            .ToListAsync(ct);

        return Ok(items);
    }

    [HttpPost("historico")]
    public async Task<IActionResult> SalvarHistorico([FromBody] SalvarHistoricoDto dto, CancellationToken ct)
    {
        // Keep max 15 per user — remove oldest if needed
        var count = await db.HonorariosCalculos
            .CountAsync(h => h.TenantId == tenantContext.TenantId && h.UsuarioId == tenantContext.UserId, ct);

        if (count >= 15)
        {
            var oldest = await db.HonorariosCalculos
                .Where(h => h.TenantId == tenantContext.TenantId && h.UsuarioId == tenantContext.UserId)
                .OrderBy(h => h.CriadoEm)
                .FirstAsync(ct);
            db.HonorariosCalculos.Remove(oldest);
        }

        var item = new HonorarioCalculo
        {
            Id = Guid.NewGuid(),
            TenantId = tenantContext.TenantId,
            UsuarioId = tenantContext.UserId,
            CriadoEm = DateTime.UtcNow,
            Cliente = dto.Cliente,
            Mode = dto.Mode,
            Area = dto.Area,
            Tipo = dto.Tipo,
            ItemOAB = dto.ItemOAB,
            ValorCausa = dto.ValorCausa,
            HonorarioBase = dto.HonorarioBase,
            HonorarioAjustado = dto.HonorarioAjustado,
            TotalCorrigido = dto.TotalCorrigido,
            Multiplicador = dto.Multiplicador,
            TaxaCorrecao = dto.TaxaCorrecao,
            FormaPagamento = dto.FormaPagamento,
            Complexidade = dto.Complexidade,
            Risco = dto.Risco,
            Urgencia = dto.Urgencia,
            Capacidade = dto.Capacidade,
            Exito = dto.Exito,
            StateJson = dto.StateJson
        };

        db.HonorariosCalculos.Add(item);
        await db.SaveChangesAsync(ct);

        return Ok(new { item.Id });
    }

    [HttpDelete("historico/{id:guid}")]
    public async Task<IActionResult> DeletarHistorico(Guid id, CancellationToken ct)
    {
        var item = await db.HonorariosCalculos
            .FirstOrDefaultAsync(h => h.Id == id && h.TenantId == tenantContext.TenantId && h.UsuarioId == tenantContext.UserId, ct);

        if (item == null) return NotFound();

        db.HonorariosCalculos.Remove(item);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }
}

public record SalvarHistoricoDto(
    string? Cliente,
    string Mode,
    string Area,
    string Tipo,
    string ItemOAB,
    decimal? ValorCausa,
    decimal HonorarioBase,
    decimal HonorarioAjustado,
    decimal TotalCorrigido,
    decimal Multiplicador,
    decimal TaxaCorrecao,
    string FormaPagamento,
    string Complexidade,
    string Risco,
    string Urgencia,
    string Capacidade,
    string Exito,
    string StateJson
);
