using System.Globalization;
using LegalManager.API.Reports;
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

/// <summary>Calculadora de FGTS: depósitos devidos + multa e revisão TR × índice (ADI 5090). Planos pagos.</summary>
[ApiController]
[Route("api/fgts")]
[Authorize]
public class FgtsController(AppDbContext db, ITenantContext tenantContext) : ControllerBase
{
    internal const string MensagemPlano = "Calculadoras trabalhistas disponíveis nos planos pagos, a partir do Plus.";
    internal static readonly TipoIndice[] IndicesAtualizacao = [TipoIndice.TJSP, TipoIndice.TR, TipoIndice.SELIC, TipoIndice.IPCA];

    private bool PlanoPermite => PlanoRestricoes.PermiteCalculadorasTrabalhistas(tenantContext.Plano);

    /// <summary>Gera as competências do contrato (salário proporcional, 13º por avos e aviso indenizado).</summary>
    [HttpPost("competencias")]
    public IActionResult Competencias([FromBody] GerarCompetenciasFgtsDto dto)
    {
        if (!PlanoPermite) return StatusCode(402, new { message = MensagemPlano });
        if (dto.Saida < dto.Admissao) return BadRequest(new { message = "A data de saída é anterior à admissão." });
        if (dto.Saida.DayNumber - dto.Admissao.DayNumber > 366 * 50) return BadRequest(new { message = "Período longo demais (máximo de 50 anos)." });
        return Ok(CalculadoraFgts.GerarCompetencias(dto));
    }

    [HttpPost("devido")]
    public async Task<IActionResult> Devido([FromBody] CalcularFgtsDevidoDto dto, CancellationToken ct)
    {
        if (!PlanoPermite) return StatusCode(402, new { message = MensagemPlano });
        var erro = CalculadoraFgts.ValidarDevido(dto);
        if (erro is not null) return BadRequest(new { message = erro });
        return Ok(CalculadoraFgts.CalcularDevido(dto, await SeriesDevidoAsync(dto, ct)));
    }

    [HttpPost("devido/pdf")]
    public async Task<IActionResult> DevidoPdf([FromBody] CalcularFgtsDevidoDto dto, [FromServices] IWebHostEnvironment env, CancellationToken ct)
    {
        if (!PlanoPermite) return StatusCode(402, new { message = MensagemPlano });
        var erro = CalculadoraFgts.ValidarDevido(dto);
        if (erro is not null) return BadRequest(new { message = erro });
        var r = CalculadoraFgts.CalcularDevido(dto, await SeriesDevidoAsync(dto, ct));
        return await PdfAsync(DemonstrativosTrabalhistas.FgtsDevido(dto, r), "fgts-devido", dto.DataCalculo, env, ct);
    }

    [HttpPost("revisao")]
    public async Task<IActionResult> Revisao([FromBody] CalcularRevisaoFgtsDto dto, CancellationToken ct)
    {
        if (!PlanoPermite) return StatusCode(402, new { message = MensagemPlano });
        var erro = CalculadoraFgts.ValidarRevisao(dto);
        if (erro is not null) return BadRequest(new { message = erro });
        return Ok(CalculadoraFgts.CalcularRevisao(dto, await SeriesRevisaoAsync(dto, ct)));
    }

    [HttpPost("revisao/pdf")]
    public async Task<IActionResult> RevisaoPdf([FromBody] CalcularRevisaoFgtsDto dto, [FromServices] IWebHostEnvironment env, CancellationToken ct)
    {
        if (!PlanoPermite) return StatusCode(402, new { message = MensagemPlano });
        var erro = CalculadoraFgts.ValidarRevisao(dto);
        if (erro is not null) return BadRequest(new { message = erro });
        var r = CalculadoraFgts.CalcularRevisao(dto, await SeriesRevisaoAsync(dto, ct));
        return await PdfAsync(DemonstrativosTrabalhistas.RevisaoFgts(dto, r), "revisao-fgts", dto.DataCalculo, env, ct);
    }

    private Task<SeriesMensais> SeriesDevidoAsync(CalcularFgtsDevidoDto dto, CancellationToken ct)
    {
        var primeira = dto.Competencias.Min(c => (c.Ano, c.Mes));
        return SeriesMensais.CarregarAsync(db, primeira, IndicesAtualizacao, false, ct);
    }

    private Task<SeriesMensais> SeriesRevisaoAsync(CalcularRevisaoFgtsDto dto, CancellationToken ct)
    {
        var primeira = dto.Movimentos.Min(m => (m.Ano, m.Mes));
        return SeriesMensais.CarregarAsync(db, primeira, [TipoIndice.TR, CalculadoraFgts.TipoDoIndice(dto.Indice)], false, ct);
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
