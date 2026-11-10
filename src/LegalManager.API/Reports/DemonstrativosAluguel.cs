using LegalManager.Application.DTOs.Aluguel;
using LegalManager.Infrastructure.Services.Trabalhista;
using static LegalManager.API.Reports.DemonstrativoPdfRenderer;

namespace LegalManager.API.Reports;

/// <summary>Demonstrativos em PDF da calculadora de aluguel.</summary>
public static class DemonstrativosAluguel
{
    public static DemonstrativoPdf Debito(CalcularDebitoAluguelDto dto, ResultadoDebitoAluguelDto r) => new(
        "Demonstrativo de débito de aluguéis",
        [("Processo", dto.Processo ?? ""), ("Locador", dto.Locador ?? ""), ("Locatário", dto.Locatario ?? ""), ("Imóvel", dto.Imovel ?? "")],
        [
            ("Data do cálculo", Data(dto.DataCalculo)),
            ("Correção monetária", r.Indice),
            ("Juros de mora", r.JurosDescricao + ", a partir de cada vencimento"),
            ("Multa moratória", Num(dto.MultaMoratoria) + "% sobre o valor corrigido"),
            ("Honorários", dto.HonorariosPercentual > 0 ? Num(dto.HonorariosPercentual) + "% sobre o montante" : "—"),
        ],
        [new TabelaPdf(null,
            [new("Parcela", 1, false), new("Vencimento", 1, false), new("Aluguel"), new("Encargos"), new("Pago"), new("Saldo"),
             new("Fator", 0.9f), new("Corrigido"), new("Juros %", 0.8f), new("Juros"), new("Multa"), new("Total")],
            r.Linhas.Select(l => (IReadOnlyList<string>)[
                l.Descricao, Data(l.Vencimento), Num(l.Aluguel), Num(l.Encargos), l.ValorPago > 0 ? Num(l.ValorPago) : "—", Num(l.Saldo),
                Num(l.FatorCorrecao, 6), Num(l.Corrigido), Num(l.JurosPercentual) + "%", Num(l.Juros), Num(l.Multa), Num(l.Total)]).ToList(),
            ["Total", "", "", "", "", Num(r.Saldo), "", Num(r.Saldo + r.Correcao), "", Num(r.Juros), Num(r.Multa), Num(r.Subtotal)])],
        [new BlocoTotaisPdf("Resumo", [
            new($"Saldo nominal ({r.Parcelas} {(r.Parcelas == 1 ? "parcela" : "parcelas")})", Moeda(r.Saldo)),
            new("Correção monetária", Moeda(r.Correcao)),
            new("Juros de mora", Moeda(r.Juros)),
            new("Multa moratória", Moeda(r.Multa)),
            new("Honorários", Moeda(r.Honorarios)),
            new("Total do débito", Moeda(r.Total), true)], true)],
        r.Avisos,
        "Cada parcela (aluguel + encargos − pagamentos) é corrigida pelos índices do mês do vencimento até o mês anterior ao do cálculo; " +
        "juros simples sobre o valor corrigido; multa moratória sobre o valor corrigido; honorários sobre o montante. " +
        "Fontes: SGS/Banco Central do Brasil. Cálculo de apoio — confira com o contrato de locação.");

    public static DemonstrativoPdf Reajuste(CalcularReajusteAluguelDto dto, ResultadoReajusteAluguelDto r) => new(
        "Reajuste de aluguel",
        [],
        [
            ("Início do contrato", Data(dto.Contrato.InicioContrato)),
            ("Aluguel inicial", Moeda(dto.Contrato.ValorInicial)),
            ("Índice", r.Indice),
            ("Periodicidade", dto.Contrato.PeriodicidadeMeses switch { 12 => "Anual", 6 => "Semestral", _ => "Trimestral" }),
            ("Calculado até", Data(dto.Ate)),
        ],
        [new TabelaPdf(null,
            [new("Reajuste em", 1, false), new("Período do índice", 1.6f, false), new("Índice acumulado"), new("Aluguel anterior"), new("Aluguel reajustado")],
            r.Reajustes.Select(l => (IReadOnlyList<string>)[
                Data(l.Data), l.Periodo + (l.Estimado ? " (parcial)" : ""), Num(l.IndiceAcumulado, 4) + "%", Moeda(l.ValorAnterior), Moeda(l.ValorNovo)]).ToList())],
        [new BlocoTotaisPdf("Resultado", [
            new("Aluguel vigente", Moeda(r.ValorAtual), true),
            new("Próximo reajuste", Data(r.ProximoReajuste))], true)],
        r.Avisos,
        "Reajuste no aniversário do contrato pelo índice acumulado nos meses do período anterior (ex.: contrato de março, reajuste anual em março " +
        "pelo acumulado de março a fevereiro). Fontes: SGS/Banco Central do Brasil.");
}
