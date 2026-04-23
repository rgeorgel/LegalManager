using System.ComponentModel.DataAnnotations;
using LegalManager.Domain.Entities;
using LegalManager.Domain.Interfaces;
using LegalManager.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LegalManager.API.Controllers;

[ApiController]
[Route("api/configuracoes")]
[Authorize]
public class ConfiguracoesExtrasController(
    AppDbContext context,
    ITenantContext tenantContext) : ControllerBase
{
    [HttpGet("areas-atuacao")]
    public async Task<IActionResult> GetAreas(CancellationToken ct)
    {
        var areas = await context.AreasAtuacao
            .Where(a => a.TenantId == tenantContext.TenantId)
            .OrderBy(a => a.Nome)
            .Select(a => new { a.Id, a.Nome, a.Descricao, a.Ativo })
            .ToListAsync(ct);
        return Ok(areas);
    }

    [HttpPost("areas-atuacao")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> CreateArea([FromBody] CreateAreaDto dto, CancellationToken ct)
    {
        var area = new AreaAtuacao
        {
            Id = Guid.NewGuid(),
            TenantId = tenantContext.TenantId,
            Nome = dto.Nome.Trim(),
            Descricao = dto.Descricao?.Trim(),
            Ativo = true,
            CriadoEm = DateTime.UtcNow
        };
        context.AreasAtuacao.Add(area);
        await context.SaveChangesAsync(ct);
        return Created("", new { area.Id, area.Nome, area.Descricao, area.Ativo });
    }

    [HttpPut("areas-atuacao/{id:guid}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> UpdateArea(Guid id, [FromBody] UpdateAreaDto dto, CancellationToken ct)
    {
        var area = await context.AreasAtuacao
            .FirstOrDefaultAsync(a => a.Id == id && a.TenantId == tenantContext.TenantId, ct);
        if (area == null) return NotFound();

        area.Nome = dto.Nome.Trim();
        area.Descricao = dto.Descricao?.Trim();
        await context.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpDelete("areas-atuacao/{id:guid}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> DeleteArea(Guid id, CancellationToken ct)
    {
        var area = await context.AreasAtuacao
            .FirstOrDefaultAsync(a => a.Id == id && a.TenantId == tenantContext.TenantId, ct);
        if (area == null) return NotFound();

        context.AreasAtuacao.Remove(area);
        await context.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpGet("categorias-financeiras")]
    public async Task<IActionResult> GetCategorias(CancellationToken ct)
    {
        var cats = await context.CategoriasFinanceiras
            .Where(c => c.TenantId == tenantContext.TenantId)
            .OrderBy(c => c.Tipo).ThenBy(c => c.Nome)
            .Select(c => new { c.Id, c.Nome, c.Tipo, c.Ativo })
            .ToListAsync(ct);
        return Ok(cats);
    }

    [HttpPost("categorias-financeiras")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> CreateCategoria([FromBody] CreateCategoriaDto dto, CancellationToken ct)
    {
        if (!Enum.TryParse<TipoCategoriaFinanceira>(dto.Tipo, true, out var tipo))
            return BadRequest(new { message = "Tipo inválido. Use 'Receita' ou 'Despesa'." });

        var cat = new CategoriaFinanceira
        {
            Id = Guid.NewGuid(),
            TenantId = tenantContext.TenantId,
            Nome = dto.Nome.Trim(),
            Tipo = tipo,
            Ativo = true,
            CriadoEm = DateTime.UtcNow
        };
        context.CategoriasFinanceiras.Add(cat);
        await context.SaveChangesAsync(ct);
        return Created("", new { cat.Id, cat.Nome, Tipo = cat.Tipo.ToString(), cat.Ativo });
    }

    [HttpPut("categorias-financeiras/{id:guid}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> UpdateCategoria(Guid id, [FromBody] UpdateCategoriaDto dto, CancellationToken ct)
    {
        var cat = await context.CategoriasFinanceiras
            .FirstOrDefaultAsync(c => c.Id == id && c.TenantId == tenantContext.TenantId, ct);
        if (cat == null) return NotFound();

        if (!Enum.TryParse<TipoCategoriaFinanceira>(dto.Tipo, true, out var tipo))
            return BadRequest(new { message = "Tipo inválido." });

        cat.Nome = dto.Nome.Trim();
        cat.Tipo = tipo;
        await context.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpDelete("categorias-financeiras/{id:guid}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> DeleteCategoria(Guid id, CancellationToken ct)
    {
        var cat = await context.CategoriasFinanceiras
            .FirstOrDefaultAsync(c => c.Id == id && c.TenantId == tenantContext.TenantId, ct);
        if (cat == null) return NotFound();

        context.CategoriasFinanceiras.Remove(cat);
        await context.SaveChangesAsync(ct);
        return NoContent();
    }
}

public record CreateAreaDto([Required, MaxLength(100)] string Nome, string? Descricao);
public record UpdateAreaDto([Required, MaxLength(100)] string Nome, string? Descricao);
public record CreateCategoriaDto([Required, MaxLength(100)] string Nome, [Required] string Tipo);
public record UpdateCategoriaDto([Required, MaxLength(100)] string Nome, [Required] string Tipo);