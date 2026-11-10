using System.Globalization;
using LegalManager.API.Reports;
using LegalManager.Application.DTOs.Aluguel;
using LegalManager.Application.DTOs.Pensao;
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

/// <summary>Calculadora de aluguel: reajuste e débito de aluguéis (cobrança e despejo). Planos pagos.</summary>
[ApiController]
[Route("api/aluguel")]
[Authorize]
public class AluguelController(AppDbContext db, ITenantContext tenantContext) : ControllerBase
{
    internal const string MensagemPlano = "Calculadora de aluguel disponível nos planos pagos, a partir do Plus.";

    private static readonly (string Codigo, IndiceAluguel Indice)[] IndicesExibidos =
        [("IGPM", IndiceAluguel.IGPM), ("IPCA", IndiceAluguel.IPCA), ("INPC", IndiceAluguel.INPC), ("IGPDI", IndiceAluguel.IGPDI), ("IPCAE", IndiceAluguel.IPCAE)];

    private bool PlanoPermite => PlanoRestricoes.PermiteCalculadoraAluguel(tenantContext.Plano);

    /// <summary>Último mês publicado de cada índice de aluguel.</summary>
    [HttpGet("indices")]
    public async Task<IActionResult> GetIndices(CancellationToken ct)
    {
        if (!PlanoPermite) return StatusCode(402, new { message = MensagemPlano });
        var ultimos = await db.IndicesCorrecaoMonetaria
            .GroupBy(i => i.Tipo)
            .Select(g => new { Tipo = g.Key, Chave = g.Max(i => i.Ano * 100 + i.Mes) })
            .ToListAsync(ct);
        return Ok(IndicesExibidos.Select(x =>
        {
            var tipo = CalculadoraAluguel.TipoDoIndice(x.Indice);
            var u = ultimos.FirstOrDefault(y => y.Tipo == tipo);
            return new IndiceDisponivelDto(x.Codigo, CalculadoraAluguel.NomeIndice(x.Indice).Split(' ')[0], u?.Chave / 100, u?.Chave % 100);
        }).ToList());
    }

    [HttpPost("reajuste")]
    public async Task<IActionResult> Reajuste([FromBody] CalcularReajusteAluguelDto dto, CancellationToken ct)
    {
        if (!PlanoPermite) return StatusCode(402, new { message = MensagemPlano });
        if (CalculadoraAluguel.ValidarContrato(dto.Contrato) is { } erro) return BadRequest(new { message = erro });
        return Ok(CalculadoraAluguel.Reajustes(dto.Contrato, dto.Ate, await SeriesContratoAsync(dto.Contrato, ct)));
    }

    [HttpPost("reajuste/pdf")]
    public async Task<IActionResult> ReajustePdf([FromBody] CalcularReajusteAluguelDto dto, [FromServices] IWebHostEnvironment env, CancellationToken ct)
    {
        if (!PlanoPermite) return StatusCode(402, new { message = MensagemPlano });
        if (CalculadoraAluguel.ValidarContrato(dto.Contrato) is { } erro) return BadRequest(new { message = erro });
        var r = CalculadoraAluguel.Reajustes(dto.Contrato, dto.Ate, await SeriesContratoAsync(dto.Contrato, ct));
        return await PdfAsync(DemonstrativosAluguel.Reajuste(dto, r), "reajuste-aluguel", dto.Ate, env, ct);
    }

    /// <summary>Gera as parcelas em atraso com o aluguel vigente (reajustado) em cada vencimento.</summary>
    [HttpPost("parcelas")]
    public async Task<IActionResult> Parcelas([FromBody] GerarParcelasAluguelDto dto, CancellationToken ct)
    {
        if (!PlanoPermite) return StatusCode(402, new { message = MensagemPlano });
        if (CalculadoraAluguel.ValidarParcelas(dto) is { } erro) return BadRequest(new { message = erro });
        return Ok(CalculadoraAluguel.GerarParcelas(dto, await SeriesContratoAsync(dto.Contrato, ct)));
    }

    [HttpPost("debito")]
    public async Task<IActionResult> Debito([FromBody] CalcularDebitoAluguelDto dto, CancellationToken ct)
    {
        if (!PlanoPermite) return StatusCode(402, new { message = MensagemPlano });
        if (CalculadoraAluguel.ValidarDebito(dto) is { } erro) return BadRequest(new { message = erro });
        return Ok(CalculadoraAluguel.CalcularDebito(dto, await SeriesDebitoAsync(dto, ct)));
    }

    [HttpPost("debito/pdf")]
    public async Task<IActionResult> DebitoPdf([FromBody] CalcularDebitoAluguelDto dto, [FromServices] IWebHostEnvironment env, CancellationToken ct)
    {
        if (!PlanoPermite) return StatusCode(402, new { message = MensagemPlano });
        if (CalculadoraAluguel.ValidarDebito(dto) is { } erro) return BadRequest(new { message = erro });
        var r = CalculadoraAluguel.CalcularDebito(dto, await SeriesDebitoAsync(dto, ct));
        return await PdfAsync(DemonstrativosAluguel.Debito(dto, r), "debito-aluguel", dto.DataCalculo, env, ct);
    }

    private Task<SeriesMensais> SeriesContratoAsync(ContratoAluguelDto c, CancellationToken ct) =>
        CalculadoraAluguel.TipoDoIndice(c.Indice) is { } tipo
            ? SeriesMensais.CarregarAsync(db, (c.InicioContrato.Year, c.InicioContrato.Month), [tipo], false, ct)
            : Task.FromResult(SeriesMensais.Vazia);

    private Task<SeriesMensais> SeriesDebitoAsync(CalcularDebitoAluguelDto dto, CancellationToken ct)
    {
        var primeira = dto.Parcelas.Min(p => p.Vencimento);
        return SeriesMensais.CarregarAsync(db, (primeira.Year, primeira.Month),
            AtualizacaoCivel.TiposNecessarios(dto.IndiceCorrecao, dto.Juros), false, ct);
    }

    private async Task<IActionResult> PdfAsync(DemonstrativoPdf demonstrativo, string prefixo, DateOnly data, IWebHostEnvironment env, CancellationToken ct)
    {
        var tenant = await db.Tenants.AsNoTracking()
            .Where(t => t.Id == tenantContext.TenantId)
            .Select(t => new { t.Nome, t.LogoUrl })
            .FirstOrDefaultAsync(ct);
        var geradoEm = FusoHorario.AgoraParede(await db.DoTenantAsync(tenantContext.TenantId, ct));
        var pdf = DemonstrativoPdfRenderer.Renderizar(demonstrativo, tenant?.Nome ?? "Escritório de Advocacia", tenant?.LogoUrl, geradoEm, env.WebRootPath);
        return File(pdf, "application/pdf", $"{prefixo}-{data.ToString("yyyyMMdd", CultureInfo.InvariantCulture)}.pdf");
    }
}
