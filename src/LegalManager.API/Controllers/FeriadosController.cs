using LegalManager.Application.DTOs.Feriados;
using LegalManager.Domain.Entities;
using LegalManager.Domain.Interfaces;
using LegalManager.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LegalManager.API.Controllers;

[ApiController]
[Route("api/feriados")]
[Authorize]
public class FeriadosController(AppDbContext db, ITenantContext tenantContext) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] bool? ativo = null,
        CancellationToken ct = default)
    {
        var tenantId = tenantContext.TenantId;
        var query = db.Feriados.AsNoTracking()
            .Where(f => f.TenantId == null || f.TenantId == tenantId);

        if (ativo.HasValue)
            query = query.Where(f => f.Ativo == ativo.Value);

        var result = await query
            .OrderBy(f => f.Data)
            .Select(f => new FeriadoDto(f.Id, f.Data, f.Nome, f.Tipo, f.Uf, f.Municipio, f.Ativo, f.TenantId == null))
            .ToListAsync(ct);

        return Ok(result);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateFeriadoDto dto, CancellationToken ct)
    {
        if (dto.Tipo != "municipal")
            return BadRequest(new { message = "Apenas feriados municipais podem ser cadastrados pelos escritórios." });
        if (string.IsNullOrWhiteSpace(dto.Uf))
            return BadRequest(new { message = "UF é obrigatória para feriados municipais." });
        if (string.IsNullOrWhiteSpace(dto.Municipio))
            return BadRequest(new { message = "Município é obrigatório para feriados municipais." });

        var tenantId = tenantContext.TenantId;
        var feriado = new Feriado
        {
            Data = dto.Data,
            Nome = dto.Nome.Trim(),
            Tipo = "municipal",
            Uf = dto.Uf.Trim().ToUpper(),
            Municipio = dto.Municipio.Trim(),
            Ativo = true,
            TenantId = tenantId,
            CriadoEm = DateTime.UtcNow,
            AtualizadoEm = DateTime.UtcNow,
        };

        db.Feriados.Add(feriado);
        await db.SaveChangesAsync(ct);

        return Ok(new FeriadoDto(feriado.Id, feriado.Data, feriado.Nome, feriado.Tipo, feriado.Uf, feriado.Municipio, feriado.Ativo, false));
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        var feriado = await db.Feriados.FindAsync([id], ct);
        if (feriado == null) return NotFound();
        if (feriado.TenantId != tenantContext.TenantId)
            return StatusCode(403, new { message = "Não é possível excluir feriados globais." });

        db.Feriados.Remove(feriado);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpPatch("{id:int}/ativo")]
    public async Task<IActionResult> ToggleAtivo(int id, CancellationToken ct)
    {
        var feriado = await db.Feriados.FindAsync([id], ct);
        if (feriado == null) return NotFound();
        if (feriado.TenantId != tenantContext.TenantId)
            return StatusCode(403, new { message = "Não é possível alterar feriados globais." });

        feriado.Ativo = !feriado.Ativo;
        feriado.AtualizadoEm = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        return Ok(new FeriadoDto(feriado.Id, feriado.Data, feriado.Nome, feriado.Tipo, feriado.Uf, feriado.Municipio, feriado.Ativo, false));
    }
}
