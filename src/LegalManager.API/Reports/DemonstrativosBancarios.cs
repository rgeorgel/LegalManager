using LegalManager.Application.DTOs.Bancario;
using LegalManager.Infrastructure.Services.Bancario;
using static LegalManager.API.Reports.DemonstrativoPdfRenderer;

namespace LegalManager.API.Reports;

/// <summary>Demonstrativos em PDF das calculadoras bancárias.</summary>
public static class DemonstrativosBancarios
{
    private const string Fontes = "Fontes: SGS/Banco Central do Brasil (taxas médias de juros por modalidade e índices). Cálculo de apoio — confira com o contrato.";

    private static string Pct(decimal? v, int casas = 2) => v is { } x ? Num(x, casas) + "%" : "—";

    public static DemonstrativoPdf Revisional(CalcularRevisionalDto d, ResultadoRevisionalDto r) => new(
        "Cálculo revisional de contrato bancário",
        [("Processo", d.Processo ?? ""), ("Contratante", d.Contratante ?? ""), ("Instituição", d.Instituicao ?? ""), ("Contrato", d.Contrato ?? "")],
        [
            ("Modalidade", r.Modalidade),
            ("Contratação", $"{Data(d.DataContratacao)} · {Moeda(d.ValorFinanciado)} em {d.Prazo} parcelas ({d.Sistema})"),
            ("Taxa contratada", $"{Pct(r.TaxaContratada, 4)} a.m. ({Pct(r.TaxaContratadaAnual)} a.a.){(r.TaxaImplicita ? " — implícita na parcela" : "")}"),
            ("Taxa média BCB", r.TaxaMedia is null ? "indisponível" : $"{Pct(r.TaxaMedia, 4)} a.m. ({Pct(r.TaxaMediaAnual)} a.a.) em {r.CompetenciaTaxaMedia}"),
            ("Limite de abusividade (1,5 × média)", r.LimiteAbusividade is null ? "—" : $"{Pct(r.LimiteAbusividade, 4)} a.m."),
            ("Resultado", r.Abusiva ? $"Taxa ABUSIVA — {Num(r.RazaoMedia ?? 0)} vezes a média (REsp 1.061.530/STJ)" : "Taxa dentro do critério do STJ"),
            ("Taxa do recálculo", Pct(r.TaxaRevisada, 4) + " a.m."),
            ("Atualização das diferenças", r.Atualizacao),
        ],
        [new TabelaPdf(null,
            [new("Nº", 0.5f, false), new("Vencimento", 1, false), new("Parcela contratada"), new("Parcela revisada"), new("Diferença"),
             new("Situação", 0.9f, false), new("A restituir"), new("Fator", 0.9f), new("Juros"), new("Total")],
            r.Linhas.Select(l => (IReadOnlyList<string>)[
                l.Numero.ToString(), Data(l.Vencimento), Num(l.ParcelaOriginal), Num(l.ParcelaRevisada), Num(l.Diferenca),
                l.Paga ? (l.EmDobro ? "Paga (dobro)" : "Paga") : "A vencer", Num(l.Restituir), Num(l.FatorCorrecao, 6), Num(l.Juros), Num(l.Total)]).ToList())],
        [
            new BlocoTotaisPdf("Parcelas pagas", [
                new("Pago pelo contrato", Moeda(r.TotalPagoOriginal)),
                new("Devido pelo recálculo", Moeda(r.TotalPagoRevisado)),
                new("Pago a mais", Moeda(r.PagoAMais)),
                new("A restituir", Moeda(r.Restituicao)),
                new("A restituir atualizado", Moeda(r.RestituicaoAtualizada), true)], true),
            new BlocoTotaisPdf("Parcelas a vencer", [
                new("Saldo devedor contratado", Moeda(r.SaldoDevedorOriginal)),
                new("Saldo devedor revisado", Moeda(r.SaldoDevedorRevisado)),
                new("Redução nas parcelas a vencer", Moeda(r.EconomiaAVencer), true)]),
        ],
        r.Avisos,
        "Parcelas recalculadas pelo mesmo sistema de amortização com a taxa do recálculo sobre o valor financiado sem as tarifas excluídas. " +
        "Diferenças das parcelas pagas atualizadas desde cada vencimento (mês cheio, juros simples). " + Fontes);

    public static DemonstrativoPdf Rmc(CalcularRmcDto d, ResultadoRmcDto r) => new(
        $"Cálculo de {(d.Tipo == TipoCartaoConsignado.RMC ? "RMC" : "RCC")} — cartão consignado",
        [("Processo", d.Processo ?? ""), ("Beneficiário", d.Beneficiario ?? ""), ("Benefício", d.Beneficio ?? ""), ("Banco", d.Banco ?? "")],
        [
            ("Contratação", $"{Data(d.DataContratacao)} · valor liberado {Moeda(d.ValorLiberado)}"),
            ("Taxa do empréstimo consignado", $"{Pct(r.TaxaAplicada, 4)} a.m. ({r.TaxaFonte})"),
            ("Quitação", r.ParcelasParaQuitar is { } q ? $"{q} descontos — em {Data(r.DataQuitacao!.Value)}" : "não quitado com os descontos informados"),
            ("Devolução", d.RepeticaoEmDobro ? "Em dobro após 30/03/2021 (art. 42, parágrafo único, do CDC; EAREsp 676.608/STJ)" : "Simples"),
            ("Atualização", r.Atualizacao),
        ],
        [new TabelaPdf(null,
            [new("Data", 1, false), new("Desconto"), new("Juros do mês"), new("Amortização"), new("Saldo"), new("Indébito"),
             new("A restituir"), new("Fator", 0.9f), new("Juros"), new("Total")],
            r.Linhas.Select(l => (IReadOnlyList<string>)[
                Data(l.Data), Num(l.Desconto), Num(l.JurosMes), Num(l.Amortizacao), Num(l.Saldo), Num(l.Indebito),
                Num(l.Restituir) + (l.EmDobro ? " (2×)" : ""), Num(l.FatorCorrecao, 6), Num(l.Juros), Num(l.Total)]).ToList(),
            ["Total", Num(r.TotalDescontado), "", "", "", Num(r.PagoAMais), Num(r.Restituicao), "", "", Num(r.RestituicaoAtualizada)])],
        [new BlocoTotaisPdf("Resultado", [
            new("Total descontado", Moeda(r.TotalDescontado)),
            new("Pago a mais", Moeda(r.PagoAMais)),
            new("A restituir", Moeda(r.Restituicao)),
            new("A restituir atualizado", Moeda(r.RestituicaoAtualizada)),
            new("Danos morais", Moeda(r.DanosMorais)),
            new("Total", Moeda(r.Total), true)], true)],
        r.Avisos,
        "O saque do cartão é tratado como empréstimo consignado comum: o saldo rende juros compostos mês a mês pela taxa indicada e cada desconto amortiza; " +
        "o que é descontado depois da quitação é indébito, atualizado desde cada desconto. " + Fontes);

    public static DemonstrativoPdf Superendividamento(CalcularSuperendividamentoDto d, ResultadoSuperendividamentoDto r) => new(
        "Plano de pagamento — superendividamento",
        [("Processo", d.Processo ?? ""), ("Consumidor(a)", d.Consumidor ?? "")],
        [
            ("Renda líquida mensal", Moeda(d.RendaLiquida)),
            ("Valor preservado", $"{Moeda(r.ValorPreservado)} (maior entre o mínimo existencial de {Moeda(d.MinimoExistencial)} e as despesas básicas)"),
            ("Parcelas atuais", $"{Moeda(r.TotalParcelasAtuais)} — {Num(r.ComprometimentoAtual)}% da renda"),
            ("Diagnóstico", r.Superendividado ? "Superendividamento: as parcelas atuais comprometem o mínimo existencial (art. 54-A, §1º, do CDC)" : "As parcelas atuais cabem na renda preservando o mínimo existencial"),
            ("Plano", r.MesesPlano > 0 ? $"{r.MesesPlano} parcelas de {Moeda(r.ParcelaPlano)} — {Num(r.ComprometimentoPlano)}% da renda" : "Sem capacidade de pagamento"),
        ],
        [new TabelaPdf("Plano por credor",
            [new("Credor", 2, false), new("Tipo", 1.2f, false), new("Saldo"), new("Participação"), new("Parcela atual"), new("Parcela no plano"), new("Total no plano"), new("Remanescente")],
            r.Credores.Select(c => (IReadOnlyList<string>)[
                c.Credor, c.Excluida ? (c.Tipo ?? "") + " (fora do plano)" : c.Tipo ?? "", Num(c.Saldo), c.Excluida ? "—" : Num(c.Participacao) + "%",
                Num(c.ParcelaAtual), Num(c.ParcelaPlano), Num(c.TotalPlano), Num(c.Remanescente)]).ToList(),
            ["Total", "", Num(r.TotalDividas), "", Num(r.TotalParcelasAtuais), Num(r.ParcelaPlano), Num(r.Credores.Sum(c => c.TotalPlano)), Num(r.Remanescente)])],
        [new BlocoTotaisPdf("Resumo", [
            new("Dívidas repactuáveis", Moeda(r.TotalRepactuavel)),
            new("Capacidade de pagamento", Moeda(r.CapacidadePagamento)),
            new("Parcela mensal do plano", Moeda(r.ParcelaPlano), true),
            new("Prazo", $"{r.MesesPlano} meses"),
            new("Remanescente", Moeda(r.Remanescente))], true)],
        r.Avisos,
        "Lei 14.181/2021 (arts. 54-A e 104-A a 104-C do CDC). Mínimo existencial do Decreto 11.150/2022, com a redação do Decreto 11.567/2023. " +
        "Parcela do plano dividida entre os credores na proporção do saldo de cada um.");
}
