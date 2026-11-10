using System.Globalization;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace LegalManager.API.Reports;

public record ColunaPdf(string Titulo, float Largura = 1, bool Direita = true);

public record TabelaPdf(string? Titulo, IReadOnlyList<ColunaPdf> Colunas, IReadOnlyList<IReadOnlyList<string>> Linhas,
    IReadOnlyList<string>? Rodape = null);

public record LinhaTotalPdf(string Rotulo, string Valor, bool Destaque = false);

public record BlocoTotaisPdf(string Titulo, IReadOnlyList<LinhaTotalPdf> Linhas, bool Destaque = false);

public record DemonstrativoPdf(
    string Titulo,
    IReadOnlyList<(string Rotulo, string Valor)> Identificacao,
    IReadOnlyList<(string Rotulo, string Valor)> Parametros,
    IReadOnlyList<TabelaPdf> Tabelas,
    IReadOnlyList<BlocoTotaisPdf> Totais,
    IReadOnlyList<string> Avisos,
    string Metodologia);

/// <summary>Demonstrativo de cálculo genérico (A4 paisagem): parâmetros, tabelas, totais e observações.</summary>
public static class DemonstrativoPdfRenderer
{
    private const string CorNavy = "#0f2744";
    private const string CorGold = "#c9a84c";
    private const string CorMuted = "#5f5e5a";
    public static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    public static string Moeda(decimal v) => v.ToString("C", PtBr);
    public static string Num(decimal v, int casas = 2) => v.ToString("N" + casas, PtBr);
    public static string Data(DateOnly d) => d.ToString("dd/MM/yyyy", PtBr);

    public static byte[] Renderizar(DemonstrativoPdf d, string nomeEscritorio, string? logoUrl, DateTime geradoEm, string? webRootPath = null)
    {
        QuestPDF.Settings.License = LicenseType.Community;
        var logo = PdfLogo.Carregar(logoUrl, webRootPath);

        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4.Landscape());
                page.Margin(30);
                page.DefaultTextStyle(x => x.FontSize(8).FontFamily("Segoe UI"));

                page.Header().Column(col =>
                {
                    col.Item().Row(row =>
                    {
                        row.RelativeItem().Column(c =>
                        {
                            if (logo != null) c.Item().Height(30).Image(logo).FitArea();
                            else c.Item().Text("⚖").FontSize(18);
                        });
                        row.RelativeItem().AlignRight().Column(c =>
                        {
                            c.Item().AlignRight().Text(nomeEscritorio).Bold().FontSize(12).FontColor(CorNavy);
                            c.Item().AlignRight().Text(d.Titulo.ToUpperInvariant()).Bold().FontSize(10).FontColor(CorNavy);
                        });
                    });
                    col.Item().PaddingTop(4).BorderBottom(2).BorderColor(CorGold);
                    var ident = d.Identificacao.Where(i => !string.IsNullOrWhiteSpace(i.Valor)).Select(i => $"{i.Rotulo}: {i.Valor.Trim()}").ToList();
                    if (ident.Count > 0) col.Item().PaddingTop(6).Text(string.Join("   ·   ", ident)).FontSize(8);
                });

                page.Content().PaddingTop(8).Column(col =>
                {
                    col.Spacing(10);
                    if (d.Parametros.Count > 0)
                        col.Item().Background("#F7F7F5").Padding(8).Column(p =>
                        {
                            foreach (var (rotulo, valor) in d.Parametros)
                                p.Item().Text(t => { t.Span(rotulo + ": ").FontColor(CorMuted); t.Span(valor); });
                        });

                    if (d.Totais.Count > 0)
                        col.Item().Row(row =>
                        {
                            for (var i = 0; i < d.Totais.Count; i++)
                            {
                                var bloco = d.Totais[i];
                                var item = row.RelativeItem();
                                if (i < d.Totais.Count - 1) item = item.PaddingRight(8);
                                item.Element(c => Bloco(c, bloco));
                            }
                        });

                    foreach (var tabela in d.Tabelas)
                        col.Item().Element(c => Tabela(c, tabela));

                    if (d.Avisos.Count > 0)
                        col.Item().Column(a =>
                        {
                            a.Item().Text("Observações").Bold().FontColor(CorNavy);
                            foreach (var aviso in d.Avisos) a.Item().Text("• " + aviso).FontColor(CorMuted);
                        });
                    col.Item().Text(d.Metodologia).FontSize(7).FontColor(CorMuted);
                });

                page.Footer().PaddingTop(8).Row(row =>
                {
                    row.RelativeItem().Text($"Gerado em {geradoEm.ToString("dd/MM/yyyy HH:mm", PtBr)} · Causify · {nomeEscritorio}")
                        .FontColor(CorMuted).FontSize(7);
                    row.AutoItem().Text(t =>
                    {
                        t.Span("Página ").FontColor(CorMuted).FontSize(7);
                        t.CurrentPageNumber().FontColor(CorMuted).FontSize(7);
                        t.Span(" de ").FontColor(CorMuted).FontSize(7);
                        t.TotalPages().FontColor(CorMuted).FontSize(7);
                    });
                });
            });
        }).GeneratePdf();
    }

    private static void Bloco(IContainer container, BlocoTotaisPdf b)
    {
        container.Border(1).BorderColor(b.Destaque ? CorGold : "#DDD").Padding(8).Column(col =>
        {
            col.Item().PaddingBottom(4).Text(b.Titulo).Bold().FontColor(CorNavy);
            foreach (var l in b.Linhas)
                col.Item().Row(r =>
                {
                    r.RelativeItem().Text(l.Rotulo).FontColor(l.Destaque ? CorNavy : CorMuted).Bold();
                    var txt = r.AutoItem().Text(l.Valor);
                    if (l.Destaque) txt.Bold().FontSize(10).FontColor(CorNavy);
                });
        });
    }

    private static void Tabela(IContainer container, TabelaPdf t)
    {
        container.Column(col =>
        {
            if (!string.IsNullOrWhiteSpace(t.Titulo))
                col.Item().PaddingBottom(4).Text(t.Titulo).Bold().FontSize(9).FontColor(CorNavy);
            col.Item().Table(tab =>
            {
                tab.ColumnsDefinition(cols => { foreach (var c in t.Colunas) cols.RelativeColumn(c.Largura); });
                tab.Header(h =>
                {
                    foreach (var c in t.Colunas)
                    {
                        var cel = h.Cell().Background(CorNavy).Padding(4);
                        (c.Direita ? cel.AlignRight() : cel).Text(c.Titulo).FontColor("#fff").FontSize(7).Bold();
                    }
                });
                foreach (var linha in t.Linhas)
                    for (var i = 0; i < t.Colunas.Count; i++)
                    {
                        var cel = tab.Cell().BorderBottom(1).BorderColor("#EEE").PaddingVertical(3).PaddingHorizontal(3);
                        (t.Colunas[i].Direita ? cel.AlignRight() : cel).Text(i < linha.Count ? linha[i] : "");
                    }
                if (t.Rodape is { Count: > 0 })
                    for (var i = 0; i < t.Colunas.Count; i++)
                    {
                        var cel = tab.Cell().BorderTop(1).BorderColor(CorNavy).PaddingVertical(4).PaddingHorizontal(3);
                        (t.Colunas[i].Direita ? cel.AlignRight() : cel).Text(i < t.Rodape.Count ? t.Rodape[i] : "").Bold();
                    }
            });
        });
    }
}
