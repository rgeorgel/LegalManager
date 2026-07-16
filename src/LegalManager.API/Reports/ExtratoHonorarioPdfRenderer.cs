using LegalManager.Application.DTOs.Honorarios;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace LegalManager.API.Reports;

public static class ExtratoHonorarioPdfRenderer
{
    private const string CorNavy = "#0f2744";
    private const string CorGold = "#c9a84c";
    private const string CorMuted = "#5f5e5a";

    private static readonly HttpClient LogoHttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(4)
    };

    private static byte[]? CarregarLogo(string? logoUrl)
    {
        if (string.IsNullOrWhiteSpace(logoUrl)) return null;
        if (!logoUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !logoUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return null;
        try
        {
            return LogoHttpClient.GetByteArrayAsync(logoUrl).GetAwaiter().GetResult();
        }
        catch
        {
            return null;
        }
    }

    public static byte[] Renderizar(ExtratoPdfDadosDto d)
    {
        QuestPDF.Settings.License = LicenseType.Community;

        var logoBytes = CarregarLogo(d.LogoUrl);

        var document = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(50);
                page.DefaultTextStyle(x => x.FontSize(10).FontFamily("Segoe UI"));

                page.Header().Element(c => Cabecalho(c, d, logoBytes));
                page.Content().Element(c => Conteudo(c, d));
                page.Footer().AlignCenter().PaddingTop(20).Text(t =>
                {
                    t.Span($"Emitido em {d.EmitidoEm:dd/MM/yyyy HH:mm} · Causify — Gestão de Honorários · ").FontColor(CorMuted).FontSize(8);
                    t.Span(d.NomeEscritorio).FontColor(CorMuted).FontSize(8);
                });
            });
        });

        return document.GeneratePdf();
    }

    private static void Cabecalho(IContainer container, ExtratoPdfDadosDto d, byte[]? logoBytes)
    {
        container.Column(col =>
        {
            col.Item().Row(row =>
            {
                row.RelativeItem().Column(c =>
                {
                    if (logoBytes != null)
                        c.Item().Height(35).Image(logoBytes).FitArea();
                    else
                        c.Item().Text("⚖").FontSize(20);
                });
                row.RelativeItem().AlignRight().Column(c =>
                {
                    c.Item().Text(d.NomeEscritorio).Bold().FontSize(14).FontColor(CorNavy);
                    if (!string.IsNullOrEmpty(d.Advogado) || !string.IsNullOrEmpty(d.Oab))
                        c.Item().Text($"{d.Advogado}{(string.IsNullOrEmpty(d.Oab) ? "" : " — " + d.Oab)}").FontSize(9).FontColor(CorMuted);
                    if (!string.IsNullOrEmpty(d.Endereco)) c.Item().Text(d.Endereco).FontSize(9).FontColor(CorMuted);
                    if (!string.IsNullOrEmpty(d.Telefone) || !string.IsNullOrEmpty(d.Email))
                        c.Item().Text($"{(string.IsNullOrEmpty(d.Telefone) ? "" : "Tel: " + d.Telefone)}{(string.IsNullOrEmpty(d.Email) ? "" : (!string.IsNullOrEmpty(d.Telefone) ? " · " : "") + d.Email)}").FontSize(9).FontColor(CorMuted);
                });
            });
            col.Item().PaddingTop(4).BorderBottom(3).BorderColor(CorGold);
            col.Item().PaddingTop(10).AlignCenter().Text("EXTRATO DE HONORÁRIOS").Bold().FontSize(14).FontColor(CorNavy);
        });
    }

    private static void Conteudo(IContainer container, ExtratoPdfDadosDto d)
    {
        container.PaddingVertical(20).Column(col =>
        {
            col.Item().Text("DADOS DO CLIENTE").FontSize(8).Bold().FontColor(CorMuted);
            col.Item().PaddingTop(4).Grid(g =>
            {
                g.Columns(2);
                g.Item().Text($"Nome: {d.NomeCliente ?? "—"}");
                g.Item().Text($"CPF/CNPJ: {d.CpfCnpj ?? "—"}");
            });

            col.Item().PaddingTop(14).Text("DADOS DO CONTRATO").FontSize(8).Bold().FontColor(CorMuted);
            col.Item().PaddingTop(4).Grid(g =>
            {
                g.Columns(2);
                g.Item().Text($"Nº Contrato: {d.NumeroContrato}");
                g.Item().Text($"Data Início: {d.DataInicio:dd/MM/yyyy}");
                g.Item().Text($"Serviço: {d.Objeto ?? "—"}");
                g.Item().Text($"Valor Total: {FormatBRL(d.ValorTotal)}");
                g.Item().Text($"Forma: {d.FormaPagamento}");
                g.Item().Text($"Tipo de Cobrança: {d.TipoCobranca}");
                g.Item().Text($"Processo: {d.NumeroProcesso ?? "—"}");
                g.Item().Text($"Encargos: Multa {d.PercentualMulta:P0} + Juros {d.PercentualJurosMensal:P1}/mês");
            });

            if (d.TotalEmAtraso > 0)
            {
                col.Item().PaddingTop(12).Background("#FFF5F5").Border(1).BorderColor("#FECACA").Padding(8).Column(cw =>
                {
                    cw.Item().Text("⚠ ENCARGOS DE ATRASO").FontSize(8).Bold().FontColor("#DC2626");
                    cw.Item().PaddingTop(2).Text($"Sobre os valores em atraso incidem multa de {d.PercentualMulta:P0} (incidência única) e juros de mora de {d.PercentualJurosMensal:P1} ao mês pro rata die, conforme contrato de honorários. Total atualizado: {FormatBRL(d.TotalEmAtraso)}");
                });
            }

            col.Item().PaddingTop(14).Text("DEMONSTRATIVO DE PARCELAS").FontSize(8).Bold().FontColor(CorMuted);
            col.Item().PaddingTop(4).Table(tab =>
            {
                tab.ColumnsDefinition(cols =>
                {
                    cols.RelativeColumn(2);
                    cols.RelativeColumn(2);
                    cols.RelativeColumn(2);
                    cols.RelativeColumn(2);
                    cols.RelativeColumn(1);
                    cols.RelativeColumn(2);
                    cols.RelativeColumn(2);
                });
                tab.Header(h =>
                {
                    h.Cell().Background(CorNavy).Padding(5).Text("Parc.").FontColor("#fff").FontSize(8).Bold();
                    h.Cell().Background(CorNavy).Padding(5).Text("Vencimento").FontColor("#fff").FontSize(8).Bold();
                    h.Cell().Background(CorNavy).Padding(5).Text("Valor").FontColor("#fff").FontSize(8).Bold();
                    h.Cell().Background(CorNavy).Padding(5).Text("Juros/Multa").FontColor("#fff").FontSize(8).Bold();
                    h.Cell().Background(CorNavy).Padding(5).Text("Status").FontColor("#fff").FontSize(8).Bold();
                    h.Cell().Background(CorNavy).Padding(5).Text("Pago em").FontColor("#fff").FontSize(8).Bold();
                    h.Cell().Background(CorNavy).Padding(5).Text("Total").FontColor("#fff").FontSize(8).Bold();
                });
                foreach (var r in d.Parcelas)
                {
                    var statusColor = r.Status switch
                    {
                        "Pago" => "#059669",
                        "Cancelado" => "#999",
                        "Em Atraso" => "#DC2626",
                        _ => "#B45309"
                    };
                    tab.Cell().BorderBottom(1).BorderColor("#EEE").Padding(5).Text(r.Label).FontSize(9);
                    tab.Cell().BorderBottom(1).BorderColor("#EEE").Padding(5).Text(r.Vencimento.ToString("dd/MM/yyyy")).FontSize(9);
                    tab.Cell().BorderBottom(1).BorderColor("#EEE").Padding(5).Text(FormatBRL(r.Valor)).FontSize(9);
                    tab.Cell().BorderBottom(1).BorderColor("#EEE").Padding(5).Text(r.JurosMulta.HasValue ? FormatBRL(r.JurosMulta.Value) : "—").FontColor(r.JurosMulta.HasValue ? "#DC2626" : "#999").FontSize(9);
                    tab.Cell().BorderBottom(1).BorderColor("#EEE").Padding(5).Text(r.Status).FontColor(statusColor).FontSize(9).Bold();
                    tab.Cell().BorderBottom(1).BorderColor("#EEE").Padding(5).Text(r.PagoEm?.ToString("dd/MM/yyyy") ?? "—").FontSize(9);
                    tab.Cell().BorderBottom(1).BorderColor("#EEE").Padding(5).Text(FormatBRL(r.ValorAtualizado)).FontSize(9).Bold();
                }
            });

            col.Item().PaddingTop(14).Background("#F4F6FA").Padding(10).Row(r =>
            {
                r.RelativeItem().Column(cc =>
                {
                    cc.Item().Text("TOTAL PAGO").FontSize(8).Bold().FontColor(CorMuted);
                    cc.Item().PaddingTop(3).Text(FormatBRL(d.TotalPago)).FontSize(15).Bold().FontColor("#059669");
                });
                r.RelativeItem().Column(cc =>
                {
                    cc.Item().Text("PENDENTE").FontSize(8).Bold().FontColor(CorMuted);
                    cc.Item().PaddingTop(3).Text(FormatBRL(d.TotalPendente)).FontSize(15).Bold().FontColor("#B45309");
                });
                r.RelativeItem().Column(cc =>
                {
                    cc.Item().Text("EM ATRASO (c/ encargos)").FontSize(8).Bold().FontColor(CorMuted);
                    cc.Item().PaddingTop(3).Text(FormatBRL(d.TotalEmAtraso)).FontSize(15).Bold().FontColor("#DC2626");
                });
            });
        });
    }

    private static string FormatBRL(decimal v) => v.ToString("C", System.Globalization.CultureInfo.GetCultureInfo("pt-BR"));
}
