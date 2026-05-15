using System.Text.RegularExpressions;
using LegalManager.Domain.Entities;
using LegalManager.Domain.Enums;
using LegalManager.Domain.Interfaces;
using LegalManager.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

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

    [HttpGet("indices")]
    public async Task<IActionResult> GetIndices(CancellationToken ct)
    {
        var tipos = new[] { TipoIndice.IPCA, TipoIndice.IGPM, TipoIndice.TJSP };
        var result = new List<object>();

        foreach (var tipo in tipos)
        {
            var indice = await db.IndicesCorrecaoMonetaria
                .Where(i => i.Tipo == tipo)
                .OrderByDescending(i => i.Ano).ThenByDescending(i => i.Mes)
                .FirstOrDefaultAsync(ct);

            result.Add(new
            {
                tipo = tipo.ToString(),
                valor = indice?.Valor,
                ano = indice?.Ano,
                mes = indice?.Mes,
                atualizadoEm = indice?.AtualizadoEm
            });
        }

        return Ok(result);
    }

    [HttpGet("configuracao")]
    public async Task<IActionResult> GetConfiguracao(CancellationToken ct)
    {
        var cfg = await db.ConfiguracoesCalculadora
            .FirstOrDefaultAsync(c => c.TenantId == tenantContext.TenantId, ct);

        return Ok(new { adicionalEspecialidade = cfg?.AdicionalEspecialidade ?? 0.10m });
    }

    [HttpPut("configuracao")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> SalvarConfiguracao([FromBody] ConfiguracaoCalculadoraDto dto, CancellationToken ct)
    {
        if (dto.AdicionalEspecialidade < 0 || dto.AdicionalEspecialidade > 1)
            return BadRequest(new { error = "Percentual deve estar entre 0% e 100%." });

        var cfg = await db.ConfiguracoesCalculadora
            .FirstOrDefaultAsync(c => c.TenantId == tenantContext.TenantId, ct);

        if (cfg == null)
        {
            cfg = new ConfiguracaoCalculadora
            {
                Id = Guid.NewGuid(),
                TenantId = tenantContext.TenantId,
                AdicionalEspecialidade = dto.AdicionalEspecialidade,
                AtualizadoEm = DateTime.UtcNow
            };
            db.ConfiguracoesCalculadora.Add(cfg);
        }
        else
        {
            cfg.AdicionalEspecialidade = dto.AdicionalEspecialidade;
            cfg.AtualizadoEm = DateTime.UtcNow;
        }

        await db.SaveChangesAsync(ct);
        return Ok(new { cfg.AdicionalEspecialidade });
    }

    [HttpPost("proposta/pdf")]
    public IActionResult GerarPropostaPdf([FromBody] PropostaPdfDto dto, CancellationToken ct)
    {
        QuestPDF.Settings.License = LicenseType.Community;

        var tenant = db.Tenants.FirstOrDefault(t => t.Id == tenantContext.TenantId);
        var nomeEscritorio = tenant?.Nome ?? "Escritório de Advocacia";
        var corNavy = "#0f2744";
        var corGold = "#c9a84c";

        try
        {
            var document = Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(50);
                    page.DefaultTextStyle(x => x.FontSize(11).FontFamily("Segoe UI"));

                    page.Header().Element(c => Container(c, nomeEscritorio, dto.Cliente ?? "[NOME DO CLIENTE]", dto.DataProposta, corNavy, corGold));
                    page.Content().Element(c => ContainerContent(c, dto, corNavy, corGold));
                    page.Footer().AlignCenter().Text(x =>
                    {
                        x.Span("Calculadora de Honorários — Tabela OAB/SP 2026 (CAASP | ESA | PREV)");
                        x.Span(" · ");
                        x.Span(dto.DataProposta);
                        x.Span(" · ");
                        x.Span("Documento gerado pelo sistema Causify");
                    });
                });
            });

            var pdfBytes = document.GeneratePdf();
            var filename = $"proposta-honorarios-{DateTime.Now:yyyyMMdd-HHmmss}.pdf";
            return File(pdfBytes, "application/pdf", filename);
        }
        catch (Exception ex)
        {
            return BadRequest(new { error = ex.Message, stack = ex.StackTrace });
        }
    }

    private static void Container(IContainer container, string nomeEscritorio, string cliente, string dataProposta, string corNavy, string corGold)
    {
        container.Column(col =>
        {
            col.Item().Row(row =>
            {
                row.RelativeItem().Column(c =>
                {
                    c.Item().Text("⚖️").FontSize(28);
                });
                row.RelativeItem().AlignRight().Column(c =>
                {
                    c.Item().Text(nomeEscritorio).Bold().FontSize(14).FontColor(corNavy);
                    c.Item().Text("Tabela OAB/SP 2026 (CAASP | ESA | PREV)").FontSize(9).FontColor("#5f5e5a");
                });
            });
            col.Item().PaddingTop(5).BorderBottom(1).BorderColor(corGold);
            col.Item().PaddingTop(8).AlignCenter().Text("PROPOSTA DE HONORÁRIOS ADVOCATÍCIOS").Bold().FontSize(13).FontColor(corNavy);
            col.Item().PaddingTop(4).AlignCenter().Text(x =>
            {
                x.Span("Destinatário: ").FontColor("#5f5e5a");
                x.Span(cliente).Bold();
                x.Span("  ·  Data: ").FontColor("#5f5e5a");
                x.Span(dataProposta);
            });
        });
    }

    private static void ContainerContent(IContainer container, PropostaPdfDto dto, string corNavy, string corGold)
    {
        container.PaddingTop(20).Column(col =>
        {
            col.Item().Text("QUALIFICAÇÃO E OBJETO").FontSize(8).Bold().FontColor("#888780");

            if (dto.IsMultiplos && dto.Itens != null && dto.Itens.Count > 0)
            {
                col.Item().PaddingTop(4).Text("Proposta de prestação de serviços advocatícios nos seguintes objetos:");

                col.Item().PaddingTop(8).Table(tab =>
                {
                    tab.ColumnsDefinition(cols =>
                    {
                        cols.ConstantColumn(30);
                        cols.RelativeColumn(3);
                        cols.RelativeColumn(2);
                        cols.RelativeColumn(1);
                    });

                    tab.Header(header =>
                    {
                        header.Cell().Background(corNavy).Padding(5).Text("#").FontColor("#fff").FontSize(9).Bold();
                        header.Cell().Background(corNavy).Padding(5).Text("Serviço").FontColor("#fff").FontSize(9).Bold();
                        header.Cell().Background(corNavy).Padding(5).Text("Natureza / Área").FontColor("#fff").FontSize(9).Bold();
                        header.Cell().Background(corNavy).Padding(5).AlignRight().Text("Honorário").FontColor("#fff").FontSize(9).Bold();
                    });

                    foreach (var item in dto.Itens)
                    {
                        tab.Cell().BorderBottom(1).BorderColor("#ddd").Padding(5).Text(item.Numero.ToString()).FontSize(10);
                        tab.Cell().BorderBottom(1).BorderColor("#ddd").Padding(5).Text(item.Tipo).FontSize(10);
                        tab.Cell().BorderBottom(1).BorderColor("#ddd").Padding(5).Text($"{item.ModeLabel} · {item.Area}").FontSize(10).FontColor("#5f5e5a");
                        tab.Cell().BorderBottom(1).BorderColor("#ddd").Padding(5).AlignRight().Text(item.ValorFormatado).FontSize(10).Bold();
                    }

                    tab.Cell().BorderBottom(1).BorderColor("#ddd").Padding(5).AlignRight().Text("Total").FontSize(10).Bold();
                    tab.Cell().BorderBottom(1).BorderColor("#ddd").Padding(5).AlignRight().Text(dto.ValorFormatado).FontSize(10).Bold();
                });
            }
            else
            {
                col.Item().PaddingTop(4).Text(x =>
                {
                    x.Span("Proposta de prestação de serviços advocatícios de natureza ");
                    x.Span(dto.ModeLabel ?? "judicial").Bold();
                    x.Span(" na área de ");
                    x.Span(dto.Area ?? "[ÁREA]").Bold();
                    x.Span(", especificamente referente a: ");
                    x.Span(dto.Tipo ?? "[TIPO]").Bold();
                    x.Span(" (Tabela OAB/SP 2026, item ");
                    x.Span(dto.ItemOAB ?? "[ITEM]");
                    x.Span(").");
                });
            }

            col.Item().PaddingTop(12).Text("HONORÁRIOS CONTRATUAIS").FontSize(8).Bold().FontColor("#888780");
            col.Item().PaddingTop(4).Text(
                "Os honorários advocatícios ora propostos foram calculados com base na Tabela de Honorários Advocatícios da OAB/SP 2026 (CAASP | ESA | PREV), aprovada nos termos do art. 22 da Lei nº 8.906/1994 (Estatuto da OAB) e do art. 48 do Código de Ética e Disciplina da OAB, e ajustados em conformidade com os critérios do item 17 da referida Tabela, considerando a complexidade da causa, o trabalho e o tempo necessários, o lugar da prestação dos serviços e demais fatores de equidade e moderação pertinentes ao caso concreto."
            );

            if (dto.IsMultiplos)
            {
                col.Item().PaddingTop(6).Text(x =>
                {
                    x.Span("O valor total dos honorários contratuais é de ");
                    x.Span($"{dto.ValorFormatado}").Bold();
                    x.Span(" (");
                    x.Span(dto.ValorExtenso ?? "valor em reais conforme acima");
                    x.Span("), conforme discriminado na tabela acima.");
                });
            }
            else
            {
                col.Item().PaddingTop(6).Text(x =>
                {
                    x.Span("O valor dos honorários contratuais é de ");
                    x.Span($"{dto.ValorFormatado}").Bold();
                    x.Span(" (");
                    x.Span(dto.ValorExtenso ?? "valor em reais conforme acima");
                    x.Span(")");
                    if (dto.HasPerc && dto.ValorCausa > 0)
                    {
                        x.Span($", correspondendo ao percentual de {dto.PercFormatado} sobre o valor da causa de {dto.ValorCausaFormatado}, observado o valor mínimo tabela de {dto.MinFormatado}");
                    }
                    x.Span(".");
                });
            }

            if (dto.ExisteExito && dto.ExitoValor > 0)
            {
                col.Item().PaddingTop(12).Text("HONORÁRIOS DE ÊXITO").FontSize(8).Bold().FontColor("#888780");
                col.Item().PaddingTop(4).Text(x =>
                {
                    x.Span("Em caso de êxito na demanda, serão devidos honorários contingenciais adicionais no percentual de ");
                    x.Span($"{dto.ExitoPctFormatado}").Bold();
                    x.Span(" sobre o valor da condenação ou proveito econômico obtido, correspondendo ao montante estimado de ");
                    x.Span($"{dto.ExitoValorFormatado}").Bold();
                    x.Span(". Os honorários de êxito são independentes dos honorários contratuais fixos acima estabelecidos e somente serão exigíveis com a efetiva satisfação da condição (art. 22, §2º, Lei 8.906/94).");
                });
            }

            col.Item().PaddingTop(12).Text("FORMA E CONDIÇÕES DE PAGAMENTO").FontSize(8).Bold().FontColor("#888780");
            col.Item().PaddingTop(4).Text(dto.FormaPagamentoTexto ?? "A definir.");

            col.Item().PaddingTop(12).Text("DISPOSIÇÕES GERAIS").FontSize(8).Bold().FontColor("#888780");
            col.Item().PaddingTop(4).Text(
                "Os honorários ora pactuados compreendem exclusivamente o patrocínio das causas em primeiro grau de jurisdição, sendo devidos independentemente do resultado das demandas (art. 8º, Tabela OAB/SP 2026). A interposição de recursos, sustentação oral e atuação em instâncias superiores constituem atos próprios e serão objeto de contratação específica (item 7, Tabela OAB/SP 2026)."
            );
            col.Item().PaddingTop(6).Text(
                "Os honorários de sucumbência eventualmente fixados pelo juízo pertencem ao advogado, nos termos do art. 85, §14, do CPC, e não serão abatidos dos honorários contratuais ora ajustados (item 9, Tabela OAB/SP 2026)."
            );
            col.Item().PaddingTop(6).Text(
                "Em caso de revogação do mandato sem culpa do advogado, os honorários serão devidos em sua totalidade (item 10, Tabela OAB/SP 2026)."
            );

            col.Item().PaddingTop(12).Text("ACEITE").FontSize(8).Bold().FontColor("#888780");
            col.Item().PaddingTop(4).Text(x =>
            {
                x.Span("A presente proposta tem validade de ");
                x.Span("15 (quinze) dias").Bold();
                x.Span(" a contar da data acima. O aceite se formaliza pela assinatura do contrato de honorários e outorga de procuração.");
            });
        });
    }
}

public record ConfiguracaoCalculadoraDto(decimal AdicionalEspecialidade);

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

public record PropostaPdfDto
{
    public string? Cliente { get; init; }
    public string? ModeLabel { get; init; }
    public string? Area { get; init; }
    public string? Tipo { get; init; }
    public string? ItemOAB { get; init; }
    public decimal? ValorCausa { get; init; }
    public string ValorFormatado { get; init; } = "";
    public string? ValorExtenso { get; init; }
    public bool HasPerc { get; init; }
    public string? PercFormatado { get; init; }
    public string? ValorCausaFormatado { get; init; }
    public string? MinFormatado { get; init; }
    public bool ExisteExito { get; init; }
    public decimal ExitoValor { get; init; }
    public string? ExitoPctFormatado { get; init; }
    public string? ExitoValorFormatado { get; init; }
    public string? FormaPagamentoTexto { get; init; }
    public string DataProposta { get; init; } = "";
    public bool IsMultiplos { get; init; }
    public List<PropostaPdfItemDto>? Itens { get; init; }
}

public record PropostaPdfItemDto(
    int Numero,
    string Tipo,
    string ModeLabel,
    string Area,
    string ValorFormatado
);
