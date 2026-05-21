using System.ComponentModel.DataAnnotations;
using LegalManager.Application.DTOs.Feriados;
using LegalManager.Domain.Entities;
using LegalManager.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LegalManager.API.Controllers;

[ApiController]
[Route("api/feriados")]
[Authorize]
public class FeriadosController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] bool? ativo = null,
        CancellationToken ct = default)
    {
        var query = db.Feriados.AsNoTracking();

        if (ativo.HasValue)
            query = query.Where(f => f.Ativo == ativo.Value);

        var result = await query
            .OrderBy(f => f.Data)
            .Select(f => new FeriadoDto(f.Id, f.Data, f.Nome, f.Tipo, f.Uf, f.Municipio, f.Ativo))
            .ToListAsync(ct);

        return Ok(result);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateFeriadoDto dto, CancellationToken ct)
    {
        var tiposValidos = new[] { "nacional", "estadual", "municipal" };
        if (!tiposValidos.Contains(dto.Tipo))
            return BadRequest(new { message = "Tipo inválido. Use: nacional, estadual ou municipal." });
        if (dto.Tipo != "nacional" && string.IsNullOrWhiteSpace(dto.Uf))
            return BadRequest(new { message = "UF é obrigatória para feriados estaduais e municipais." });
        if (dto.Tipo == "municipal" && string.IsNullOrWhiteSpace(dto.Municipio))
            return BadRequest(new { message = "Município é obrigatório para feriados municipais." });

        var feriado = new Feriado
        {
            Data = dto.Data,
            Nome = dto.Nome.Trim(),
            Tipo = dto.Tipo,
            Uf = dto.Tipo == "nacional" ? null : dto.Uf?.Trim().ToUpper(),
            Municipio = dto.Tipo == "municipal" ? dto.Municipio?.Trim() : null,
            Ativo = true,
            CriadoEm = DateTime.UtcNow,
            AtualizadoEm = DateTime.UtcNow,
        };

        db.Feriados.Add(feriado);
        await db.SaveChangesAsync(ct);

        return Ok(new FeriadoDto(feriado.Id, feriado.Data, feriado.Nome, feriado.Tipo, feriado.Uf, feriado.Municipio, feriado.Ativo));
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        var feriado = await db.Feriados.FindAsync([id], ct);
        if (feriado == null) return NotFound();
        db.Feriados.Remove(feriado);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpPatch("{id:int}/ativo")]
    public async Task<IActionResult> ToggleAtivo(int id, CancellationToken ct)
    {
        var feriado = await db.Feriados.FindAsync([id], ct);
        if (feriado == null) return NotFound();
        feriado.Ativo = !feriado.Ativo;
        feriado.AtualizadoEm = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return Ok(new FeriadoDto(feriado.Id, feriado.Data, feriado.Nome, feriado.Tipo, feriado.Uf, feriado.Municipio, feriado.Ativo));
    }
}
