using System.Globalization;
using LegalManager.Application.DTOs.Pensao;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace LegalManager.API.Reports;

/// <summary>Demonstrativo do débito de pensão alimentícia (tabela mês a mês + resumo).</summary>
public static class PensaoPdfRenderer
{
    private const string CorNavy = "#0f2744";
    private const string CorGold = "#c9a84c";
    private const string CorMuted = "#5f5e5a";
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    public static byte[] Renderizar(CalcularPensaoDto dto, ResultadoPensaoDto r, string nomeEscritorio,
        string? logoUrl, DateTime geradoEm, string? webRootPath = null)
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

                page.Header().Element(c => Cabecalho(c, dto, nomeEscritorio, logo));
                page.Content().Column(col =>
                {
                    col.Spacing(10);
                    col.Item().Element(c => Parametros(c, dto, r));
                    col.Item().Element(c => Tabela(c, dto, r));
                    col.Item().Element(c => Resumo(c, dto, r));
                    if (r.Avisos.Count > 0)
                        col.Item().Column(a =>
                        {
                            a.Item().Text("Observações").Bold().FontColor(CorNavy);
                            foreach (var aviso in r.Avisos) a.Item().Text("• " + aviso).FontColor(CorMuted);
                        });
                    col.Item().Text(
                        "Metodologia: cada parcela é corrigida pelos índices mensais do mês do vencimento até o mês anterior ao do cálculo " +
                        "e recebe juros simples sobre o valor corrigido no mesmo período. Pagamentos parciais abatem a própria parcela. " +
                        "Fontes: SGS/Banco Central do Brasil (índices, Selic e salário mínimo). Cálculo de apoio — confira com o título executivo.")
                        .FontSize(7).FontColor(CorMuted);
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

    private static void Cabecalho(IContainer container, CalcularPensaoDto dto, string nomeEscritorio, byte[]? logo)
    {
        container.Column(col =>
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
                    c.Item().AlignRight().Text("DEMONSTRATIVO DE DÉBITO ALIMENTAR").Bold().FontSize(10).FontColor(CorNavy);
                });
            });
            col.Item().PaddingTop(4).BorderBottom(2).BorderColor(CorGold);
            var partes = new List<string>();
            if (!string.IsNullOrWhiteSpace(dto.Processo)) partes.Add("Processo: " + dto.Processo.Trim());
            if (!string.IsNullOrWhiteSpace(dto.Alimentando)) partes.Add("Alimentando(a): " + dto.Alimentando.Trim());
            if (!string.IsNullOrWhiteSpace(dto.Alimentante)) partes.Add("Alimentante: " + dto.Alimentante.Trim());
            if (partes.Count > 0)
                col.Item().PaddingTop(6).Text(string.Join("   ·   ", partes)).FontSize(8);
        });
    }

    private static void Parametros(IContainer container, CalcularPensaoDto dto, ResultadoPensaoDto r)
    {
        var forma = dto.Forma == FormaPensao.SalarioMinimo
            ? $"{(dto.PercentualSalarioMinimo ?? 0).ToString("0.##", PtBr)}% do salário mínimo vigente em cada mês"
            : $"Valor fixo de {Moeda(dto.ValorFixo ?? 0)} por mês";
        var itens = new List<(string, string)>
        {
            ("Data do cálculo", dto.DataCalculo.ToString("dd/MM/yyyy", PtBr)),
            ("Pensão", forma),
            ("Correção monetária", r.Indice),
            ("Juros de mora", r.Juros + (dto.DataCitacao is { } c ? $", a partir da citação ({c.ToString("dd/MM/yyyy", PtBr)})" : ", a partir de cada vencimento")),
        };
        if (dto.MultaPercentual > 0) itens.Add(("Multa (art. 523, §1º, CPC)", Pct(dto.MultaPercentual) + (dto.SepararRitos ? " — só no rito da penhora" : "")));
        if (dto.HonorariosPercentual > 0) itens.Add(("Honorários advocatícios", Pct(dto.HonorariosPercentual)));
        if (dto.SepararRitos) itens.Add(("Ajuizamento", (dto.DataAjuizamento ?? dto.DataCalculo).ToString("dd/MM/yyyy", PtBr)));

        container.Background("#F7F7F5").Padding(8).Column(col =>
        {
            foreach (var (rotulo, valor) in itens)
                col.Item().Text(t => { t.Span(rotulo + ": ").FontColor(CorMuted); t.Span(valor); });
        });
    }

    private static void Tabela(IContainer container, CalcularPensaoDto dto, ResultadoPensaoDto r)
    {
        var comSm = r.Linhas.Any(l => l.SalarioMinimo is not null);
        var comRito = dto.SepararRitos;
        container.Table(tab =>
        {
            tab.ColumnsDefinition(cols =>
            {
                cols.RelativeColumn(1.4f); // Parcela
                cols.RelativeColumn(1.3f); // Vencimento
                if (comSm) cols.RelativeColumn(1.3f);
                cols.RelativeColumn(1.4f); // Devido
                cols.RelativeColumn(1.3f); // Pago
                cols.RelativeColumn(1.4f); // Saldo
                cols.RelativeColumn(1.2f); // Fator
                cols.RelativeColumn(1.5f); // Corrigido
                cols.RelativeColumn(1.1f); // Juros %
                cols.RelativeColumn(1.4f); // Juros
                cols.RelativeColumn(1.6f); // Total
                if (comRito) cols.RelativeColumn(1.1f);
            });

            var titulos = new List<string> { "Parcela", "Vencimento" };
            if (comSm) titulos.Add("Salário mínimo");
            titulos.AddRange(["Valor devido", "Pago", "Saldo", "Fator correção", "Corrigido", "Juros %", "Juros", "Total"]);
            if (comRito) titulos.Add("Rito");
            tab.Header(h =>
            {
                for (var i = 0; i < titulos.Count; i++)
                {
                    var cel = h.Cell().Background(CorNavy).Padding(4);
                    (i < 2 || titulos[i] == "Rito" ? cel : cel.AlignRight()).Text(titulos[i]).FontColor("#fff").FontSize(7).Bold();
                }
            });

            foreach (var l in r.Linhas)
            {
                Celula(tab).Text(l.Descricao);
                Celula(tab).Text(l.Vencimento.ToString("dd/MM/yyyy", PtBr));
                if (comSm) Num(tab, l.SalarioMinimo is { } sm ? Moeda(sm) : "—");
                Num(tab, Moeda(l.ValorDevido));
                Num(tab, l.ValorPago > 0 ? Moeda(l.ValorPago) : "—");
                Num(tab, Moeda(l.Saldo));
                Num(tab, l.FatorCorrecao.ToString("0.000000", PtBr));
                Num(tab, Moeda(l.ValorCorrigido));
                Num(tab, l.JurosPercentual.ToString("0.00", PtBr) + "%");
                Num(tab, Moeda(l.Juros));
                Celula(tab).AlignRight().Text(Moeda(l.Total)).Bold();
                if (comRito) Celula(tab).Text(l.Rito switch { "prisao" => "Prisão", "penhora" => "Penhora", _ => "—" });
            }
        });
    }

    private static void Resumo(IContainer container, CalcularPensaoDto dto, ResultadoPensaoDto r)
    {
        container.Row(row =>
        {
            if (r.Prisao is not null && r.Penhora is not null)
            {
                row.RelativeItem().PaddingRight(8).Element(c => BlocoTotais(c, "Rito da prisão (art. 528, §3º, CPC)", r.Prisao));
                row.RelativeItem().PaddingRight(8).Element(c => BlocoTotais(c, "Rito da penhora (art. 528, §8º, CPC)", r.Penhora));
            }
            row.RelativeItem().Element(c => BlocoTotais(c, "Total do débito", r.Totais, destaque: true));
        });
    }

    private static void BlocoTotais(IContainer container, string titulo, TotaisPensaoDto t, bool destaque = false)
    {
        container.Border(1).BorderColor(destaque ? CorGold : "#DDD").Padding(8).Column(col =>
        {
            col.Item().PaddingBottom(4).Text(titulo).Bold().FontColor(CorNavy);
            void Linha(string rotulo, decimal valor, bool forte = false) => col.Item().Row(r =>
            {
                r.RelativeItem().Text(rotulo).FontColor(forte ? CorNavy : CorMuted).Bold();
                var txt = r.AutoItem().Text(Moeda(valor));
                if (forte) txt.Bold().FontSize(10).FontColor(CorNavy);
            });
            col.Item().Text($"{t.Parcelas} {(t.Parcelas == 1 ? "parcela em aberto" : "parcelas em aberto")}").FontColor(CorMuted);
            Linha("Saldo nominal", t.Saldo);
            Linha("Correção monetária", t.Correcao);
            Linha("Juros de mora", t.Juros);
            if (t.Multa > 0) Linha("Multa", t.Multa);
            if (t.Honorarios > 0) Linha("Honorários", t.Honorarios);
            Linha("Total", t.Total, forte: true);
        });
    }

    private static IContainer Celula(TableDescriptor tab) =>
        tab.Cell().BorderBottom(1).BorderColor("#EEE").PaddingVertical(3).PaddingHorizontal(3);

    private static void Num(TableDescriptor tab, string texto) => Celula(tab).AlignRight().Text(texto);

    private static string Moeda(decimal v) => v.ToString("C", PtBr);
    private static string Pct(decimal v) => v.ToString("0.##", PtBr) + "%";
}
