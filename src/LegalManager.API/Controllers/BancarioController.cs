using System.Globalization;
using LegalManager.API.Reports;
using LegalManager.Application.DTOs.Bancario;
using LegalManager.Application.DTOs.Pensao;
using LegalManager.Domain;
using LegalManager.Domain.Enums;
using LegalManager.Domain.Interfaces;
using LegalManager.Infrastructure;
using LegalManager.Infrastructure.Persistence;
using LegalManager.Infrastructure.Services.Bancario;
using LegalManager.Infrastructure.Services.Trabalhista;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LegalManager.API.Controllers;

/// <summary>Calculadoras bancárias: revisional, RMC/RCC e superendividamento. Planos pagos.</summary>
[ApiController]
[Route("api/bancario")]
[Authorize]
public class BancarioController(AppDbContext db, ITenantContext tenantContext) : ControllerBase
{
    internal const string MensagemPlano = "Calculadoras bancárias disponíveis nos planos pagos, a partir do Plus.";

    private bool PlanoPermite => PlanoRestricoes.PermiteCalculadorasBancarias(tenantContext.Plano);

    /// <summary>Último mês publicado da taxa média de cada modalidade.</summary>
    [HttpGet("indices")]
    public async Task<IActionResult> GetIndices(CancellationToken ct)
    {
        if (!PlanoPermite) return StatusCode(402, new { message = MensagemPlano });
        var ultimos = await db.IndicesCorrecaoMonetaria
            .GroupBy(i => i.Tipo)
            .Select(g => new { Tipo = g.Key, Chave = g.Max(i => i.Ano * 100 + i.Mes) })
            .ToListAsync(ct);
        return Ok(Enum.GetValues<ModalidadeCredito>().Select(m =>
        {
            var u = ultimos.FirstOrDefault(x => x.Tipo == CalculadorasBancarias.TipoTaxaMedia(m));
            return new IndiceDisponivelDto(m.ToString(), CalculadorasBancarias.NomeModalidade(m), u?.Chave / 100, u?.Chave % 100);
        }).ToList());
    }

    [HttpPost("revisional")]
    public async Task<IActionResult> Revisional([FromBody] CalcularRevisionalDto dto, CancellationToken ct)
    {
        if (!PlanoPermite) return StatusCode(402, new { message = MensagemPlano });
        if (CalculadorasBancarias.ValidarRevisional(dto) is { } erro) return BadRequest(new { message = erro });
        return Ok(CalculadorasBancarias.CalcularRevisional(dto, await SeriesRevisionalAsync(dto, ct)));
    }

    [HttpPost("revisional/pdf")]
    public async Task<IActionResult> RevisionalPdf([FromBody] CalcularRevisionalDto dto, [FromServices] IWebHostEnvironment env, CancellationToken ct)
    {
        if (!PlanoPermite) return StatusCode(402, new { message = MensagemPlano });
        if (CalculadorasBancarias.ValidarRevisional(dto) is { } erro) return BadRequest(new { message = erro });
        var r = CalculadorasBancarias.CalcularRevisional(dto, await SeriesRevisionalAsync(dto, ct));
        return await PdfAsync(DemonstrativosBancarios.Revisional(dto, r), "revisional", dto.DataCalculo, env, ct);
    }

    [HttpPost("rmc")]
    public async Task<IActionResult> Rmc([FromBody] CalcularRmcDto dto, CancellationToken ct)
    {
        if (!PlanoPermite) return StatusCode(402, new { message = MensagemPlano });
        if (CalculadorasBancarias.ValidarRmc(dto) is { } erro) return BadRequest(new { message = erro });
        return Ok(CalculadorasBancarias.CalcularRmc(dto, await SeriesRmcAsync(dto, ct)));
    }

    [HttpPost("rmc/pdf")]
    public async Task<IActionResult> RmcPdf([FromBody] CalcularRmcDto dto, [FromServices] IWebHostEnvironment env, CancellationToken ct)
    {
        if (!PlanoPermite) return StatusCode(402, new { message = MensagemPlano });
        if (CalculadorasBancarias.ValidarRmc(dto) is { } erro) return BadRequest(new { message = erro });
        var r = CalculadorasBancarias.CalcularRmc(dto, await SeriesRmcAsync(dto, ct));
        return await PdfAsync(DemonstrativosBancarios.Rmc(dto, r), dto.Tipo.ToString().ToLowerInvariant(), dto.DataCalculo, env, ct);
    }

    [HttpPost("superendividamento")]
    public IActionResult Superendividamento([FromBody] CalcularSuperendividamentoDto dto)
    {
        if (!PlanoPermite) return StatusCode(402, new { message = MensagemPlano });
        if (CalculadorasBancarias.ValidarSuperendividamento(dto) is { } erro) return BadRequest(new { message = erro });
        return Ok(CalculadorasBancarias.CalcularSuperendividamento(dto));
    }

    [HttpPost("superendividamento/pdf")]
    public async Task<IActionResult> SuperendividamentoPdf([FromBody] CalcularSuperendividamentoDto dto, [FromServices] IWebHostEnvironment env, CancellationToken ct)
    {
        if (!PlanoPermite) return StatusCode(402, new { message = MensagemPlano });
        if (CalculadorasBancarias.ValidarSuperendividamento(dto) is { } erro) return BadRequest(new { message = erro });
        var r = CalculadorasBancarias.CalcularSuperendividamento(dto);
        var hoje = DateOnly.FromDateTime(FusoHorario.AgoraParede(await db.DoTenantAsync(tenantContext.TenantId, ct)));
        return await PdfAsync(DemonstrativosBancarios.Superendividamento(dto, r), "superendividamento", hoje, env, ct);
    }

    private Task<SeriesMensais> SeriesRevisionalAsync(CalcularRevisionalDto dto, CancellationToken ct)
    {
        // Três meses antes da contratação: a taxa média do mês pode ainda não ter sido publicada.
        var desde = dto.DataContratacao.AddMonths(-3);
        var tipos = AtualizacaoCivel.TiposNecessarios(dto.Correcao, dto.Juros).Append(CalculadorasBancarias.TipoTaxaMedia(dto.Modalidade));
        return SeriesMensais.CarregarAsync(db, (desde.Year, desde.Month), tipos, false, ct);
    }

    private Task<SeriesMensais> SeriesRmcAsync(CalcularRmcDto dto, CancellationToken ct)
    {
        var desde = dto.DataContratacao.AddMonths(-3);
        var tipos = AtualizacaoCivel.TiposNecessarios(dto.Correcao, dto.Juros).Append(TipoIndice.TxINSS);
        return SeriesMensais.CarregarAsync(db, (desde.Year, desde.Month), tipos, false, ct);
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
