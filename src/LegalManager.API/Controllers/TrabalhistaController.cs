using System.Globalization;
using LegalManager.API.Reports;
using LegalManager.Application.DTOs.Pensao;
using LegalManager.Application.DTOs.Trabalhista;
using LegalManager.Domain;
using LegalManager.Domain.Enums;
using LegalManager.Domain.Interfaces;
using LegalManager.Infrastructure;
using LegalManager.Infrastructure.Persistence;
using LegalManager.Infrastructure.Services.Trabalhista;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LegalManager.API.Controllers;

/// <summary>Calculadora trabalhista: verbas rescisórias e pedidos da reclamação. Planos pagos.</summary>
[ApiController]
[Route("api/trabalhista")]
[Authorize]
public class TrabalhistaController(AppDbContext db, ITenantContext tenantContext) : ControllerBase
{
    private static readonly (string Codigo, string Nome, TipoIndice Tipo)[] IndicesExibidos =
    [
        ("IPCAE", "IPCA-E", TipoIndice.TJSP),
        ("IPCA", "IPCA", TipoIndice.IPCA),
        ("SELIC", "Selic", TipoIndice.SELIC),
        ("TR", "TR", TipoIndice.TR),
        ("INPC", "INPC", TipoIndice.INPC),
    ];

    private bool PlanoPermite => PlanoRestricoes.PermiteCalculadorasTrabalhistas(tenantContext.Plano);

    /// <summary>Último mês de cada índice usado nas calculadoras trabalhista e de FGTS, e as vigências do salário mínimo.</summary>
    [HttpGet("indices")]
    public async Task<IActionResult> GetIndices(CancellationToken ct)
    {
        if (!PlanoPermite) return StatusCode(402, new { message = FgtsController.MensagemPlano });

        var ultimos = await db.IndicesCorrecaoMonetaria
            .GroupBy(i => i.Tipo)
            .Select(g => new { Tipo = g.Key, Chave = g.Max(i => i.Ano * 100 + i.Mes) })
            .ToListAsync(ct);
        var indices = IndicesExibidos.Select(x =>
        {
            var u = ultimos.FirstOrDefault(y => y.Tipo == x.Tipo);
            return new IndiceDisponivelDto(x.Codigo, x.Nome, u?.Chave / 100, u?.Chave % 100);
        }).ToList();
        var series = await SeriesMensais.CarregarAsync(db, (9999, 12), [], true, ct);
        return Ok(new IndicesTrabalhistasDto(indices, series.SalariosMinimos));
    }

    [HttpPost("calcular")]
    public async Task<IActionResult> Calcular([FromBody] CalcularTrabalhistaDto dto, CancellationToken ct)
    {
        if (!PlanoPermite) return StatusCode(402, new { message = FgtsController.MensagemPlano });
        var erro = CalculadoraTrabalhista.Validar(dto);
        if (erro is not null) return BadRequest(new { message = erro });
        return Ok(CalculadoraTrabalhista.Calcular(dto, await SeriesAsync(dto, ct)));
    }

    [HttpPost("pdf")]
    public async Task<IActionResult> Pdf([FromBody] CalcularTrabalhistaDto dto, [FromServices] IWebHostEnvironment env, CancellationToken ct)
    {
        if (!PlanoPermite) return StatusCode(402, new { message = FgtsController.MensagemPlano });
        var erro = CalculadoraTrabalhista.Validar(dto);
        if (erro is not null) return BadRequest(new { message = erro });
        var r = CalculadoraTrabalhista.Calcular(dto, await SeriesAsync(dto, ct));

        var tenant = await db.Tenants.AsNoTracking()
            .Where(t => t.Id == tenantContext.TenantId)
            .Select(t => new { t.Nome, t.LogoUrl })
            .FirstOrDefaultAsync(ct);
        var geradoEm = FusoHorario.AgoraParede(await db.DoTenantAsync(tenantContext.TenantId, ct));
        var pdf = DemonstrativoPdfRenderer.Renderizar(DemonstrativosTrabalhistas.Trabalhista(dto, r),
            tenant?.Nome ?? "Escritório de Advocacia", tenant?.LogoUrl, geradoEm, env.WebRootPath);
        return File(pdf, "application/pdf", $"calculo-trabalhista-{dto.DataCalculo.ToString("yyyyMMdd", CultureInfo.InvariantCulture)}.pdf");
    }

    private Task<SeriesMensais> SeriesAsync(CalcularTrabalhistaDto dto, CancellationToken ct) =>
        SeriesMensais.CarregarAsync(db, (dto.Admissao.Year, dto.Admissao.Month), FgtsController.IndicesAtualizacao,
            dto.InsalubridadeGrau > 0, ct);
}
