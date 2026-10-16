using LegalManager.Application.DTOs.Processos;
using LegalManager.Domain.Enums;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace LegalManager.API.Reports;

public static class ProcessosRelatorioPdfRenderer
{
    private const string CorNavy = "#0f2744";
    private const string CorGold = "#c9a84c";
    private const string CorMuted = "#5f5e5a";

    // No PDF a descrição do andamento é só um resumo; a íntegra (até 500 caracteres) vai no CSV.
    private const int MaxDescricao = 110;

    public static byte[] Renderizar(ProcessoRelatorioDto d, string? webRootPath = null)
    {
        QuestPDF.Settings.License = LicenseType.Community;

        var logoBytes = PdfLogo.Carregar(d.LogoUrl, webRootPath);

        var document = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4.Landscape());
                page.Margin(30);
                page.DefaultTextStyle(x => x.FontSize(8).FontFamily("Segoe UI"));

                page.Header().Element(c => Cabecalho(c, d, logoBytes));
                page.Content().Element(c => Conteudo(c, d));
                page.Footer().PaddingTop(8).Row(r =>
                {
                    r.RelativeItem().Text($"Gerado em {d.GeradoEm.ToString("dd/MM/yyyy HH:mm", PtBr)} · Causify · {d.NomeEscritorio}")
                        .FontColor(CorMuted).FontSize(7);
                    r.AutoItem().Text(t =>
                    {
                        t.Span("Página ").FontColor(CorMuted).FontSize(7);
                        t.CurrentPageNumber().FontColor(CorMuted).FontSize(7);
                        t.Span(" de ").FontColor(CorMuted).FontSize(7);
                        t.TotalPages().FontColor(CorMuted).FontSize(7);
                    });
                });
            });
        });

        return document.GeneratePdf();
    }

    private static void Cabecalho(IContainer container, ProcessoRelatorioDto d, byte[]? logoBytes)
    {
        container.Column(col =>
        {
            col.Item().Row(row =>
            {
                row.RelativeItem().Column(c =>
                {
                    if (logoBytes != null)
                        c.Item().Height(30).Image(logoBytes).FitArea();
                    else
                        c.Item().Text("⚖").FontSize(18);
                });
                row.RelativeItem().AlignRight().Column(c =>
                {
                    c.Item().AlignRight().Text(d.NomeEscritorio).Bold().FontSize(12).FontColor(CorNavy);
                    c.Item().AlignRight().Text("RELATÓRIO DE PROCESSOS").Bold().FontSize(10).FontColor(CorNavy);
                });
            });
            col.Item().PaddingTop(4).BorderBottom(2).BorderColor(CorGold);
            col.Item().PaddingTop(6).PaddingBottom(8).Row(r =>
            {
                r.RelativeItem().Text(d.Filtros.Count > 0 ? "Filtros: " + string.Join(" · ", d.Filtros) : "Todos os processos")
                    .FontSize(8).FontColor(CorMuted);
                r.AutoItem().Text($"{d.Itens.Count} {(d.Itens.Count == 1 ? "processo" : "processos")}")
                    .FontSize(8).Bold().FontColor(CorNavy);
            });
        });
    }

    private static void Conteudo(IContainer container, ProcessoRelatorioDto d)
    {
        if (d.Itens.Count == 0)
        {
            container.PaddingTop(40).AlignCenter().Text("Nenhum processo encontrado com esses filtros.").FontColor(CorMuted).FontSize(10);
            return;
        }

        container.Table(tab =>
        {
            tab.ColumnsDefinition(cols =>
            {
                cols.RelativeColumn(2.6f); // Processo
                cols.RelativeColumn(3.2f); // Partes
                cols.RelativeColumn(2.6f); // Tribunal / Vara
                cols.RelativeColumn(2f);   // Área / Fase
                cols.RelativeColumn(1.2f); // Status
                cols.RelativeColumn(1.8f); // Responsável
                cols.RelativeColumn(1.6f); // Valor
                cols.RelativeColumn(4f);   // Última movimentação
            });

            tab.Header(h =>
            {
                foreach (var titulo in new[] { "Processo", "Partes", "Tribunal / Vara", "Área / Fase", "Status", "Responsável", "Valor da causa", "Última movimentação" })
                    h.Cell().Background(CorNavy).Padding(4).Text(titulo).FontColor("#fff").FontSize(7).Bold();
            });

            foreach (var p in d.Itens)
            {
                Celula(tab).Column(c =>
                {
                    c.Item().Text(p.NumeroCNJ).Bold();
                    if (!string.IsNullOrWhiteSpace(p.TipoAcao)) c.Item().Text(p.TipoAcao).FontColor(CorMuted);
                });
                Celula(tab).Column(c =>
                {
                    c.Item().Text(t => { t.Span("Autor: ").FontColor(CorMuted); t.Span(p.Autores ?? "—"); });
                    c.Item().Text(t => { t.Span("Réu: ").FontColor(CorMuted); t.Span(p.Reus ?? "—"); });
                });
                Celula(tab).Column(c =>
                {
                    c.Item().Text(p.Tribunal ?? "—");
                    var local = string.Join(" · ", new[] { p.Vara, p.Comarca }.Where(x => !string.IsNullOrWhiteSpace(x)));
                    if (local.Length > 0) c.Item().Text(local).FontColor(CorMuted);
                });
                Celula(tab).Column(c =>
                {
                    c.Item().Text(ProcessoRotulos.Area(p.AreaDireito));
                    c.Item().Text(ProcessoRotulos.Fase(p.Fase)).FontColor(CorMuted);
                });
                Celula(tab).Text(ProcessoRotulos.Status(p.Status)).FontColor(CorStatus(p.Status)).Bold();
                Celula(tab).Text(p.AdvogadoResponsavel ?? "—");
                Celula(tab).Text(p.ValorCausa?.ToString("C", PtBr) ?? "—");
                Celula(tab).Column(c =>
                {
                    if (p.UltimoAndamentoData is null)
                    {
                        c.Item().Text("—").FontColor(CorMuted);
                        return;
                    }
                    c.Item().Text(p.UltimoAndamentoData.Value.ToString("dd/MM/yyyy", PtBr)).Bold();
                    if (!string.IsNullOrWhiteSpace(p.UltimoAndamentoDescricao))
                        c.Item().Text(Resumo(p.UltimoAndamentoDescricao)).FontColor(CorMuted);
                });
            }
        });
    }

    private static IContainer Celula(TableDescriptor tab) =>
        tab.Cell().BorderBottom(1).BorderColor("#EEE").PaddingVertical(4).PaddingHorizontal(3);

    private static string CorStatus(StatusProcesso status) => status switch
    {
        StatusProcesso.Ativo => "#059669",
        StatusProcesso.Suspenso => "#B45309",
        _ => CorMuted
    };

    private static string Resumo(string texto)
    {
        texto = string.Join(' ', texto.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return texto.Length > MaxDescricao ? texto[..MaxDescricao].TrimEnd() + "…" : texto;
    }

    // Formatação fixa em pt-BR (datas, moeda) — independente da cultura do servidor.
    private static readonly System.Globalization.CultureInfo PtBr = System.Globalization.CultureInfo.GetCultureInfo("pt-BR");
}
