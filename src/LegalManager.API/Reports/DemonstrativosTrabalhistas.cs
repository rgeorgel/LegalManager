using LegalManager.Application.DTOs.Trabalhista;
using LegalManager.Infrastructure.Services.Trabalhista;
using static LegalManager.API.Reports.DemonstrativoPdfRenderer;

namespace LegalManager.API.Reports;

/// <summary>Monta os demonstrativos em PDF das calculadoras de FGTS e trabalhista.</summary>
public static class DemonstrativosTrabalhistas
{
    private const string MetodologiaAtualizacao =
        "Atualização pela convenção de mês cheio: cada valor recebe os índices do mês em que se torna exigível até o mês anterior ao do cálculo; " +
        "juros simples sobre o valor corrigido. Fontes: SGS/Banco Central do Brasil. Cálculo de apoio — valores brutos, sem descontos de INSS e IRRF.";

    public static DemonstrativoPdf FgtsDevido(CalcularFgtsDevidoDto dto, ResultadoFgtsDevidoDto r) => new(
        "Demonstrativo de FGTS devido",
        [("Processo", dto.Processo ?? ""), ("Reclamante", dto.Reclamante ?? ""), ("Reclamada", dto.Reclamada ?? "")],
        [
            ("Data do cálculo", Data(dto.DataCalculo)),
            ("Alíquota", Num(dto.Aliquota) + "%"),
            ("Multa rescisória", dto.MultaPercentual > 0 ? Num(dto.MultaPercentual, 0) + "% sobre " + Moeda(r.BaseMulta) : "Não se aplica"),
            ("Atualização", r.Atualizacao),
            ("Ajuizamento", dto.DataAjuizamento is { } a ? Data(a) : "—"),
        ],
        [new TabelaPdf(null,
            [new("Competência", 1, false), new("Descrição", 1.8f, false), new("Remuneração"), new("FGTS devido"), new("Depositado"),
             new("Diferença"), new("Fator", 0.9f), new("Juros %", 0.8f), new("Juros"), new("Total")],
            r.Linhas.Select(l => (IReadOnlyList<string>)[
                l.Competencia, l.Prescrita ? l.Descricao + " (prescrita)" : l.Descricao, Num(l.Remuneracao), Num(l.Devido),
                Num(l.Depositado), Num(l.Diferenca), Num(l.FatorCorrecao, 6), Num(l.JurosPercentual) + "%", Num(l.Juros), Num(l.Total)]).ToList(),
            ["Total", "", "", Num(r.TotalDevido), Num(r.TotalDepositado), Num(r.Diferencas), "", "", "", Num(r.DiferencasAtualizadas)])],
        [new BlocoTotaisPdf("Resumo", [
            new("Diferenças de depósitos", Moeda(r.Diferencas)),
            new("Diferenças atualizadas", Moeda(r.DiferencasAtualizadas)),
            new("Multa rescisória", Moeda(r.Multa)),
            new("Multa atualizada", Moeda(r.MultaAtualizada)),
            new("Total devido", Moeda(r.Total), true)], true)],
        r.Avisos,
        "FGTS devido = alíquota × remuneração de cada competência (inclusive 13º e aviso prévio indenizado — Súmula 305 do TST), menos o depositado. " +
        "Cada diferença é exigível no mês seguinte à competência; a multa, 10 dias após a rescisão. " + MetodologiaAtualizacao);

    public static DemonstrativoPdf RevisaoFgts(CalcularRevisaoFgtsDto dto, ResultadoRevisaoFgtsDto r) => new(
        "Revisão do FGTS — TR × " + r.Indice,
        [("Titular", dto.Titular ?? "")],
        [
            ("Data do cálculo", Data(dto.DataCalculo)),
            ("Índice de comparação", r.Indice),
            ("Juros da conta", Num(dto.JurosAnuais) + "% ao ano"),
            ("Saldo inicial", Moeda(dto.SaldoInicial)),
        ],
        [new TabelaPdf(null,
            [new("Competência", 1, false), new("Depósito"), new("Saque"), new("TR %", 0.8f), new("Índice %", 0.8f),
             new("Saldo com TR"), new("Saldo revisado"), new("Diferença")],
            r.Linhas.Select(l => (IReadOnlyList<string>)[
                l.Competencia, Num(l.Deposito), Num(l.Saque), Num(l.Tr, 4), Num(l.Indice, 4),
                Num(l.SaldoTr), Num(l.SaldoRevisado), Num(l.Diferenca)]).ToList())],
        [new BlocoTotaisPdf("Resultado", [
            new("Saldo com TR", Moeda(r.SaldoTr)),
            new("Saldo revisado", Moeda(r.SaldoRevisado)),
            new("Diferença no período", Moeda(r.DiferencaNoPeriodo)),
            new("Fator até a data do cálculo", Num(r.FatorAteCalculo, 6)),
            new("Diferença atualizada", Moeda(r.DiferencaAtualizada), true)], true)],
        r.Avisos,
        "Em cada mês, o saldo anterior rende (1 + taxa) × (1 + juros mensais) e depois entram o depósito e o saque do mês; " +
        "o saldo com TR reproduz a remuneração da conta e o revisado troca a TR pelo índice escolhido. Fontes: SGS/Banco Central do Brasil.");

    public static DemonstrativoPdf Trabalhista(CalcularTrabalhistaDto dto, ResultadoTrabalhistaDto r)
    {
        var tabelas = new List<TabelaPdf>();
        foreach (var grupo in r.Verbas.GroupBy(v => v.Grupo))
            tabelas.Add(new TabelaPdf(grupo.Key,
                [new("Verba", 2.6f, false), new("Memória de cálculo", 3.2f, false), new("Valor"), new("Correção"), new("Juros"), new("Total")],
                grupo.Select(v => (IReadOnlyList<string>)[v.Descricao, v.Memoria, Num(v.Valor), Num(v.Correcao), Num(v.Juros), Num(v.Total)]).ToList(),
                ["Subtotal", "", Num(grupo.Sum(v => v.Valor)), Num(grupo.Sum(v => v.Correcao)), Num(grupo.Sum(v => v.Juros)), Num(grupo.Sum(v => v.Total))]));
        if (r.Meses.Count > 0)
            tabelas.Add(new TabelaPdf("Memória mensal dos pedidos",
                [new("Competência", 1, false), new("Horas extras"), new("DSR HE"), new("Ad. noturno"), new("DSR noturno"), new("Insalubridade"), new("Periculosidade")],
                r.Meses.Select(m => (IReadOnlyList<string>)[m.Competencia, Num(m.HorasExtras), Num(m.DsrHorasExtras), Num(m.Noturno),
                    Num(m.DsrNoturno), Num(m.Insalubridade), Num(m.Periculosidade)]).ToList()));

        var resumo = r.Grupos.Select(g => new LinhaTotalPdf(g.Grupo, Moeda(g.Total))).ToList();
        if (r.Honorarios > 0) resumo.Add(new("Honorários sucumbenciais", Moeda(r.Honorarios)));
        resumo.Add(new("Total", Moeda(r.TotalGeral), true));

        return new DemonstrativoPdf(
            "Cálculo trabalhista",
            [("Processo", dto.Processo ?? ""), ("Reclamante", dto.Reclamante ?? ""), ("Reclamada", dto.Reclamada ?? "")],
            [
                ("Contrato", $"{Data(dto.Admissao)} a {Data(dto.Saida)} — {r.AnosServico} {(r.AnosServico == 1 ? "ano completo" : "anos completos")}"),
                ("Rescisão", CalculadoraTrabalhista.NomeRescisao(dto.Rescisao)),
                ("Remuneração", Moeda(dto.Salario) + (dto.MediaVariaveis > 0 ? $" + média de variáveis {Moeda(dto.MediaVariaveis)}" : "")),
                ("Aviso prévio", r.DiasAvisoIndenizados > 0 ? $"{r.DiasAvisoIndenizados} dias indenizados — projeção até {Data(r.DataProjetada)}" : "Sem aviso indenizado"),
                ("Data do cálculo", Data(dto.DataCalculo) + (dto.DataAjuizamento is { } a ? $" · ajuizamento {Data(a)}" : "")),
                ("Atualização", r.Atualizacao),
            ],
            tabelas,
            [
                new BlocoTotaisPdf("Por grupo (atualizado)", resumo, true),
                new BlocoTotaisPdf("Valores", [
                    new("Total nominal (valor da causa)", Moeda(r.TotalNominal)),
                    new("Total atualizado", Moeda(r.TotalAtualizado)),
                    new("Honorários", Moeda(r.Honorarios)),
                    new("Total geral", Moeda(r.TotalGeral), true)]),
            ],
            r.Avisos,
            "Pedidos mensais com a mesma média em todo o período e o último salário; DSR pelos domingos e feriados nacionais do mês; " +
            "reflexos em 13º e férias + 1/3 pelo duodécimo e em aviso prévio pela média dos últimos 12 meses. Parcelas mensais exigíveis no mês seguinte; " +
            "rescisórias e reflexos, 10 dias após a saída. " + MetodologiaAtualizacao);
    }
}
