using System.Globalization;
using LegalManager.API.Reports;
using LegalManager.Application.DTOs.Pensao;
using LegalManager.Domain;
using LegalManager.Domain.Entities;
using LegalManager.Domain.Enums;
using LegalManager.Domain.Interfaces;
using LegalManager.Infrastructure;
using LegalManager.Infrastructure.Persistence;
using LegalManager.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LegalManager.API.Controllers;

/// <summary>Calculadora de pensão alimentícia (débito em atraso e estimativa). Planos pagos.</summary>
[ApiController]
[Route("api/pensao")]
[Authorize]
public class PensaoController(AppDbContext db, ITenantContext tenantContext) : ControllerBase
{
    internal const string MensagemPlano = "Calculadora de pensão alimentícia disponível nos planos pagos, a partir do Plus.";

    private static readonly (IndicePensao Indice, TipoIndice Tipo)[] IndicesCorrecao =
    [
        (IndicePensao.INPC, TipoIndice.INPC),
        (IndicePensao.IPCA, TipoIndice.IPCA),
        (IndicePensao.IPCAE, TipoIndice.TJSP),
        (IndicePensao.IGPM, TipoIndice.IGPM),
    ];

    private bool PlanoPermite => PlanoRestricoes.PermiteCalculadoraPensao(tenantContext.Plano);

    /// <summary>Último mês disponível de cada índice e as vigências do salário mínimo.</summary>
    [HttpGet("indices")]
    public async Task<IActionResult> GetIndices(CancellationToken ct)
    {
        if (!PlanoPermite) return StatusCode(402, new { message = MensagemPlano });

        var ultimos = await db.IndicesCorrecaoMonetaria
            .GroupBy(i => i.Tipo)
            .Select(g => new { Tipo = g.Key, Chave = g.Max(i => i.Ano * 100 + i.Mes) })
            .ToListAsync(ct);

        IndiceDisponivelDto Disponivel(string codigo, string nome, TipoIndice tipo)
        {
            var u = ultimos.FirstOrDefault(x => x.Tipo == tipo);
            return new IndiceDisponivelDto(codigo, nome, u?.Chave / 100, u?.Chave % 100);
        }

        var indices = IndicesCorrecao
            .Select(x => Disponivel(x.Indice.ToString(), CalculadoraPensao.NomeIndice(x.Indice), x.Tipo))
            .Append(Disponivel("SELIC", "Selic (BCB)", TipoIndice.SELIC))
            .ToList();

        return Ok(new IndicesPensaoDto(indices, await VigenciasSalarioMinimoAsync(ct)));
    }

    [HttpPost("calcular")]
    public async Task<IActionResult> Calcular([FromBody] CalcularPensaoDto dto, CancellationToken ct)
    {
        if (!PlanoPermite) return StatusCode(402, new { message = MensagemPlano });

        var series = await CarregarSeriesAsync(dto, ct);
        var erro = CalculadoraPensao.Validar(dto, series);
        if (erro is not null) return BadRequest(new { message = erro });

        return Ok(CalculadoraPensao.Calcular(dto, series));
    }

    /// <summary>Demonstrativo do débito em PDF (para instruir a execução — art. 528/524 do CPC).</summary>
    [HttpPost("pdf")]
    public async Task<IActionResult> Pdf([FromBody] CalcularPensaoDto dto, [FromServices] IWebHostEnvironment env, CancellationToken ct)
    {
        if (!PlanoPermite) return StatusCode(402, new { message = MensagemPlano });

        var series = await CarregarSeriesAsync(dto, ct);
        var erro = CalculadoraPensao.Validar(dto, series);
        if (erro is not null) return BadRequest(new { message = erro });

        var resultado = CalculadoraPensao.Calcular(dto, series);
        var tenant = await db.Tenants.AsNoTracking()
            .Where(t => t.Id == tenantContext.TenantId)
            .Select(t => new { t.Nome, t.LogoUrl })
            .FirstOrDefaultAsync(ct);
        var geradoEm = FusoHorario.AgoraParede(await db.DoTenantAsync(tenantContext.TenantId, ct));

        var pdf = PensaoPdfRenderer.Renderizar(dto, resultado, tenant?.Nome ?? "Escritório de Advocacia",
            tenant?.LogoUrl, geradoEm, env.WebRootPath);
        var nome = $"calculo-pensao-{dto.DataCalculo.ToString("yyyyMMdd", CultureInfo.InvariantCulture)}.pdf";
        return File(pdf, "application/pdf", nome);
    }

    private async Task<SeriesPensao> CarregarSeriesAsync(CalcularPensaoDto dto, CancellationToken ct)
    {
        var primeira = dto.Parcelas is { Count: > 0 } ? dto.Parcelas.Min(p => p.Vencimento) : dto.DataCalculo;
        var desde = primeira.Year * 100 + primeira.Month;

        async Task<Dictionary<(int, int), decimal>> Serie(TipoIndice tipo, int desdeChave) =>
            (await db.IndicesCorrecaoMonetaria.AsNoTracking()
                .Where(i => i.Tipo == tipo && i.Ano * 100 + i.Mes >= desdeChave)
                .Select(i => new { i.Ano, i.Mes, i.Valor })
                .ToListAsync(ct))
            .ToDictionary(i => (i.Ano, i.Mes), i => i.Valor);

        var correcao = CalculadoraPensao.TipoDoIndice(dto.Indice) is { } tipo
            ? await Serie(tipo, desde)
            : [];

        Dictionary<(int, int), decimal> selic = [], ipca = [];
        if (dto.Juros == JurosPensao.TaxaLegal)
        {
            var inicioLegal = CalculadoraPensao.InicioTaxaLegal.Ano * 100 + CalculadoraPensao.InicioTaxaLegal.Mes;
            selic = await Serie(TipoIndice.SELIC, Math.Max(desde, inicioLegal));
            ipca = await Serie(TipoIndice.IPCA, Math.Max(desde, inicioLegal));
        }

        var salarios = dto.Forma == FormaPensao.SalarioMinimo
            ? await VigenciasSalarioMinimoAsync(ct)
            : [];

        return new SeriesPensao(correcao, selic, ipca, salarios);
    }

    private async Task<IReadOnlyList<SalarioMinimoVigenciaDto>> VigenciasSalarioMinimoAsync(CancellationToken ct)
    {
        var meses = await db.SalariosMinimos.AsNoTracking()
            .OrderBy(s => s.Ano).ThenBy(s => s.Mes)
            .Select(s => new { s.Ano, s.Mes, s.Valor })
            .ToListAsync(ct);
        return Vigencias(meses.Select(m => new SalarioMinimoVigenciaDto(m.Ano, m.Mes, m.Valor)));
    }

    /// <summary>Reduz a série mensal aos meses em que o valor mudou.</summary>
    public static List<SalarioMinimoVigenciaDto> Vigencias(IEnumerable<SalarioMinimoVigenciaDto> meses)
    {
        var resultado = new List<SalarioMinimoVigenciaDto>();
        foreach (var m in meses)
            if (resultado.Count == 0 || resultado[^1].Valor != m.Valor) resultado.Add(m);
        return resultado;
    }
}
