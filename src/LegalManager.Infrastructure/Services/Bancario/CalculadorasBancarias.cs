using System.Globalization;
using LegalManager.Application.DTOs.Bancario;
using LegalManager.Domain.Enums;
using LegalManager.Infrastructure.Services.Trabalhista;

namespace LegalManager.Infrastructure.Services.Bancario;

/// <summary>Revisional de contratos bancários, RMC/RCC (cartão consignado do INSS) e superendividamento.</summary>
public static class CalculadorasBancarias
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    /// <summary>Publicação do acórdão do EAREsp 676.608/STJ: daí em diante, devolução em dobro independe de má-fé.</summary>
    public static readonly DateOnly ModulacaoDobro = new(2021, 3, 30);

    /// <summary>Critério do STJ (REsp 1.061.530/RS): taxa acima de uma vez e meia a média de mercado é abusiva.</summary>
    public const decimal FatorAbusividade = 1.5m;

    public static TipoIndice TipoTaxaMedia(ModalidadeCredito m) => m switch
    {
        ModalidadeCredito.ConsignadoINSS => TipoIndice.TxINSS,
        ModalidadeCredito.ConsignadoPublico => TipoIndice.TxPublico,
        ModalidadeCredito.ConsignadoPrivado => TipoIndice.TxPrivado,
        ModalidadeCredito.Veiculo => TipoIndice.TxVeiculo,
        ModalidadeCredito.ChequeEspecial => TipoIndice.TxCheque,
        _ => TipoIndice.TxPessoal
    };

    public static string NomeModalidade(ModalidadeCredito m) => m switch
    {
        ModalidadeCredito.ConsignadoINSS => "Crédito consignado — aposentados e pensionistas do INSS",
        ModalidadeCredito.ConsignadoPublico => "Crédito consignado — servidores públicos",
        ModalidadeCredito.ConsignadoPrivado => "Crédito consignado — trabalhadores do setor privado",
        ModalidadeCredito.Veiculo => "Financiamento de veículos",
        ModalidadeCredito.ChequeEspecial => "Cheque especial",
        _ => "Crédito pessoal não consignado"
    };

    /// <summary>Taxa média (fração a.m.) do mês da contratação; se ainda não publicada, a mais recente até 3 meses antes.</summary>
    public static (decimal Taxa, (int Ano, int Mes) Mes)? TaxaMedia(SeriesMensais series, TipoIndice tipo, DateOnly data)
    {
        var m = (data.Year, data.Month);
        for (var i = 0; i < 4; i++)
        {
            if (series.Taxa(tipo, m, out var t)) return (t, m);
            m = m.Item2 == 1 ? (m.Item1 - 1, 12) : (m.Item1, m.Item2 - 1);
        }
        return null;
    }

    public static decimal Anual(decimal mensal) => (decimal)(Math.Pow(1 + (double)mensal, 12) - 1);

    public static decimal Pmt(decimal taxa, int n, decimal principal) =>
        taxa == 0 ? principal / n : principal * taxa / (1 - (decimal)Math.Pow(1 + (double)taxa, -n));

    /// <summary>Taxa mensal implícita de uma parcela Price (bissecção).</summary>
    public static decimal TaxaImplicita(decimal principal, int n, decimal parcela)
    {
        if (parcela * n <= principal) return 0;
        double lo = 0, hi = 1;
        for (var i = 0; i < 200; i++)
        {
            var mid = (lo + hi) / 2;
            var pmt = (double)principal * mid / (1 - Math.Pow(1 + mid, -n));
            if (pmt > (double)parcela) hi = mid; else lo = mid;
        }
        return (decimal)((lo + hi) / 2);
    }

    /// <summary>Parcelas e saldo devedor após cada uma (Price ou SAC).</summary>
    public static List<(decimal Parcela, decimal Saldo)> Tabela(decimal principal, decimal taxa, int n, SistemaAmortizacao sistema, decimal? parcelaFixa = null)
    {
        var lista = new List<(decimal, decimal)>();
        var saldo = principal;
        var pmt = parcelaFixa ?? Pmt(taxa, n, principal);
        var amortSac = principal / n;
        for (var k = 1; k <= n; k++)
        {
            var juros = saldo * taxa;
            var parcela = sistema == SistemaAmortizacao.SAC ? amortSac + juros : pmt;
            saldo = Math.Max(0, saldo + juros - parcela);
            lista.Add((Arredondar(parcela), Arredondar(saldo)));
        }
        return lista;
    }

    // ── Revisional ──────────────────────────────────────────────────────

    public static string? ValidarRevisional(CalcularRevisionalDto d)
    {
        if (d.TaxaContratadaMensal is null && d.ValorParcela is not > 0) return "Informe a taxa contratada ou o valor da parcela.";
        if (d.TaxaContratadaMensal is null && d.Sistema == SistemaAmortizacao.SAC) return "No SAC, informe a taxa contratada.";
        if (d.ParcelasPagas > d.Prazo) return "Parcelas pagas não podem passar do prazo.";
        if (d.TarifasExcluidas >= d.ValorFinanciado) return "As tarifas excluídas precisam ser menores que o valor financiado.";
        return null;
    }

    public static ResultadoRevisionalDto CalcularRevisional(CalcularRevisionalDto d, SeriesMensais series)
    {
        var avisos = new List<string>();
        var implicita = d.TaxaContratadaMensal is null;
        var taxaContratada = implicita
            ? TaxaImplicita(d.ValorFinanciado, d.Prazo, d.ValorParcela!.Value)
            : d.TaxaContratadaMensal!.Value / 100m;

        var media = TaxaMedia(series, TipoTaxaMedia(d.Modalidade), d.DataContratacao);
        decimal? taxaMedia = media?.Taxa;
        decimal? limite = taxaMedia * FatorAbusividade;
        var abusiva = limite is { } lim && taxaContratada > lim;
        if (media is { } mm && mm.Mes != (d.DataContratacao.Year, d.DataContratacao.Month))
            avisos.Add($"Taxa média de {d.DataContratacao.ToString("MM/yyyy", PtBr)} ainda não publicada — usada a de {mm.Mes.Mes:00}/{mm.Mes.Ano}.");
        if (media is null)
            avisos.Add(d.DataContratacao < new DateOnly(2011, 3, 1)
                ? "O BCB só publica a taxa média por modalidade desde março de 2011 — informe a taxa para o recálculo."
                : "Taxa média do BCB indisponível para a data da contratação — informe a taxa para o recálculo.");

        var taxaRevisada = d.TaxaRevisadaMensal is { } tr ? tr / 100m : abusiva ? taxaMedia!.Value : taxaContratada;
        if (!abusiva && taxaMedia is not null && d.TaxaRevisadaMensal is null)
            avisos.Add("A taxa contratada não passa de 1,5 vez a média do BCB: pelo critério do STJ (REsp 1.061.530) não há abusividade; o recálculo mantém a taxa e só exclui as tarifas informadas.");

        var original = Tabela(d.ValorFinanciado, taxaContratada, d.Prazo, d.Sistema, implicita ? d.ValorParcela : null);
        var revisada = Tabela(d.ValorFinanciado - d.TarifasExcluidas, taxaRevisada, d.Prazo, d.Sistema);
        var primeiro = d.PrimeiroVencimento ?? d.DataContratacao.AddMonths(1);
        var atualizacao = new AtualizacaoCivel(d.Correcao, d.Juros, d.JurosPercentualMensal, d.DataCalculo, series);

        var linhas = new List<LinhaRevisionalDto>();
        for (var k = 1; k <= d.Prazo; k++)
        {
            var venc = primeiro.AddMonths(k - 1);
            var (po, _) = original[k - 1];
            var (pr, _) = revisada[k - 1];
            var dif = po - pr;
            var paga = k <= d.ParcelasPagas;
            var dobro = paga && d.RepeticaoEmDobro && venc > ModulacaoDobro && dif > 0;
            var restituir = paga && dif > 0 ? dif * (dobro ? 2 : 1) : 0;
            var a = restituir > 0 && venc <= d.DataCalculo ? atualizacao.Atualizar(restituir, venc) : new ValorAtualizado(restituir, restituir, 0, 1, 0);
            linhas.Add(new LinhaRevisionalDto(k, venc, po, pr, dif, paga, dobro, restituir, a.FatorCorrecao, a.Juros, a.Total));
        }

        var pagas = linhas.Where(l => l.Paga).ToList();
        if (d.RepeticaoEmDobro && pagas.Any(l => l.Diferenca > 0 && !l.EmDobro))
            avisos.Add("Parcelas pagas até 30/03/2021 voltam de forma simples: a devolução em dobro sem prova de má-fé vale depois da publicação do EAREsp 676.608/STJ.");
        if (d.TarifasExcluidas > 0)
            avisos.Add("Tarifas excluídas do valor financiado: confira a legalidade de cada uma (TAC e TEC só em contratos até 30/04/2008 — Súmulas 565 e 566 do STJ; seguro prestamista sem livre escolha — Tema 972).");
        avisos.AddRange(atualizacao.Avisos());

        var saldoOrig = d.ParcelasPagas > 0 ? original[d.ParcelasPagas - 1].Saldo : d.ValorFinanciado;
        var saldoRev = d.ParcelasPagas > 0 ? revisada[d.ParcelasPagas - 1].Saldo : d.ValorFinanciado - d.TarifasExcluidas;

        return new ResultadoRevisionalDto(
            NomeModalidade(d.Modalidade), Pct(taxaContratada), implicita, taxaMedia is { } tm ? Pct(tm) : null,
            media is { } mm2 ? $"{mm2.Mes.Mes:00}/{mm2.Mes.Ano}" : null,
            limite is { } l2 ? Pct(l2) : null, taxaMedia is > 0 ? Math.Round(taxaContratada / taxaMedia.Value, 2) : null,
            abusiva, Pct(taxaRevisada), Pct(Anual(taxaContratada)), taxaMedia is { } tm2 ? Pct(Anual(tm2)) : null,
            linhas,
            pagas.Sum(l => l.ParcelaOriginal), pagas.Sum(l => l.ParcelaRevisada), pagas.Sum(l => Math.Max(0, l.Diferenca)),
            pagas.Sum(l => l.Restituir), pagas.Sum(l => l.Total),
            linhas.Where(l => !l.Paga).Sum(l => l.Diferenca), saldoOrig, saldoRev,
            AtualizacaoCivel.DescricaoJuros(d.Juros, d.JurosPercentualMensal), avisos);
    }

    // ── RMC / RCC ───────────────────────────────────────────────────────

    public static string? ValidarRmc(CalcularRmcDto d)
    {
        if (d.Descontos is not { Count: > 0 }) return "Inclua os descontos do benefício.";
        if (d.Descontos.Any(x => x.Data < d.DataContratacao)) return "Há desconto anterior à data da contratação.";
        if (d.Descontos.Any(x => x.Data > d.DataCalculo)) return "Há desconto posterior à data do cálculo.";
        return null;
    }

    /// <summary>
    /// Trata o saque do cartão como empréstimo consignado comum à taxa escolhida: o saldo rende juros compostos mês a mês
    /// e cada desconto amortiza; o que for descontado depois de quitado (e o excesso do mês da quitação) é indébito.
    /// </summary>
    public static ResultadoRmcDto CalcularRmc(CalcularRmcDto d, SeriesMensais series)
    {
        var avisos = new List<string>();
        decimal taxa;
        string fonte;
        if (d.TaxaMensal is { } informada) { taxa = informada / 100m; fonte = "informada"; }
        else if (TaxaMedia(series, TipoIndice.TxINSS, d.DataContratacao) is { } media)
        {
            taxa = media.Taxa;
            fonte = $"taxa média do BCB para consignado INSS em {media.Mes.Mes:00}/{media.Mes.Ano}";
        }
        else
        {
            taxa = 0;
            fonte = "taxa média indisponível — calculado sem juros";
            avisos.Add("Taxa média do consignado INSS indisponível para a data (o BCB publica desde março de 2011). Informe a taxa para um resultado realista.");
        }

        var atualizacao = new AtualizacaoCivel(d.Correcao, d.Juros, d.JurosPercentualMensal, d.DataCalculo, series);
        var saldo = d.ValorLiberado;
        var anterior = d.DataContratacao;
        int? parcelasQuitar = null;
        DateOnly? quitacao = null;
        var linhas = new List<LinhaRmcDto>();
        var n = 0;
        foreach (var desc in d.Descontos.OrderBy(x => x.Data))
        {
            n++;
            var meses = Math.Max(0, (desc.Data.Year - anterior.Year) * 12 + desc.Data.Month - anterior.Month);
            var jurosMes = saldo > 0 ? Arredondar(saldo * ((decimal)Math.Pow(1 + (double)taxa, meses) - 1)) : 0;
            var devido = saldo + jurosMes;
            decimal amort, indebito;
            if (saldo <= 0) { amort = 0; indebito = desc.Valor; jurosMes = 0; }
            else if (desc.Valor >= devido)
            {
                amort = saldo; indebito = desc.Valor - devido; saldo = 0;
                parcelasQuitar = n; quitacao = desc.Data;
            }
            else { amort = desc.Valor - jurosMes; indebito = 0; saldo = devido - desc.Valor; }
            anterior = desc.Data;

            var dobro = d.RepeticaoEmDobro && indebito > 0 && desc.Data > ModulacaoDobro;
            var restituir = indebito * (dobro ? 2 : 1);
            var a = atualizacao.Atualizar(restituir, desc.Data);
            linhas.Add(new LinhaRmcDto(desc.Data, desc.Valor, jurosMes, Arredondar(amort), Arredondar(saldo), Arredondar(indebito),
                dobro, Arredondar(restituir), a.FatorCorrecao, a.Juros, a.Total));
        }

        if (parcelasQuitar is null)
            avisos.Add($"Com os descontos informados o empréstimo ainda não estaria quitado: saldo de {Arredondar(saldo).ToString("C", PtBr)}. Não há indébito a restituir até aqui.");
        if (d.RepeticaoEmDobro && linhas.Any(l => l.Indebito > 0 && !l.EmDobro))
            avisos.Add("Descontos até 30/03/2021 voltam de forma simples: a devolução em dobro sem prova de má-fé vale depois da publicação do EAREsp 676.608/STJ.");
        avisos.AddRange(atualizacao.Avisos());

        var restituicaoAtualizada = linhas.Sum(l => l.Total);
        return new ResultadoRmcDto(Pct(taxa), fonte, parcelasQuitar, quitacao, linhas.Sum(l => l.Desconto),
            linhas.Sum(l => l.Indebito), linhas.Sum(l => l.Restituir), restituicaoAtualizada, Arredondar(saldo),
            d.DanosMorais, restituicaoAtualizada + d.DanosMorais, linhas,
            AtualizacaoCivel.DescricaoJuros(d.Juros, d.JurosPercentualMensal), avisos);
    }

    // ── Superendividamento ──────────────────────────────────────────────

    public static string? ValidarSuperendividamento(CalcularSuperendividamentoDto d)
    {
        if (d.Dividas is not { Count: > 0 }) return "Inclua ao menos uma dívida.";
        if (d.Dividas.All(x => x.Excluida || x.Saldo <= 0)) return "Nenhuma dívida entra na repactuação.";
        if (d.Dividas.Any(x => string.IsNullOrWhiteSpace(x.Credor))) return "Informe o credor de cada dívida.";
        return null;
    }

    /// <summary>
    /// Plano de repactuação (art. 104-A e 104-B do CDC): preserva o maior entre o mínimo existencial e as despesas
    /// básicas, usa o resto da renda (até o teto opcional) e quita o principal em até 60 meses; o que não couber fica
    /// como remanescente para negociação. Parcela distribuída entre credores na proporção de cada saldo.
    /// </summary>
    public static ResultadoSuperendividamentoDto CalcularSuperendividamento(CalcularSuperendividamentoDto d)
    {
        var avisos = new List<string>();
        var repactuaveis = d.Dividas.Where(x => !x.Excluida && x.Saldo > 0).ToList();
        var totalDividas = d.Dividas.Sum(x => x.Saldo);
        var total = repactuaveis.Sum(x => x.Saldo);
        var parcelasAtuais = d.Dividas.Sum(x => x.ParcelaAtual);
        var preservado = Math.Max(d.MinimoExistencial, d.DespesasBasicas);
        var capacidade = Math.Max(0, d.RendaLiquida - preservado);
        if (d.ComprometimentoMaximoPercentual is { } teto)
            capacidade = Math.Min(capacidade, Arredondar(d.RendaLiquida * teto / 100m));
        var superendividado = parcelasAtuais > capacidade;

        decimal parcela;
        int meses;
        decimal remanescente;
        if (capacidade <= 0)
        {
            parcela = 0; meses = 0; remanescente = total;
            avisos.Add("A renda não cobre o valor preservado: não há capacidade de pagamento para um plano. Avalie o plano judicial compulsório com carência (art. 104-B, §4º).");
        }
        else
        {
            var necessarios = (int)Math.Ceiling(total / capacidade);
            if (necessarios <= d.PrazoMaximoMeses)
            {
                meses = Math.Max(1, necessarios);
                parcela = Arredondar(total / meses);
                remanescente = 0;
            }
            else
            {
                meses = d.PrazoMaximoMeses;
                parcela = Arredondar(capacidade);
                remanescente = Arredondar(total - parcela * meses);
                avisos.Add($"O total não cabe em {meses} meses sem ferir o valor preservado: {remanescente.ToString("C", PtBr)} ficam para negociação com os credores ou discussão em juízo.");
            }
        }

        var credores = d.Dividas.Select(x =>
        {
            if (x.Excluida || x.Saldo <= 0 || total == 0)
                return new CredorPlanoDto(x.Credor.Trim(), x.Tipo, x.Excluida, x.Saldo, 0, x.ParcelaAtual, 0, 0, x.Excluida ? x.Saldo : 0);
            var participacao = x.Saldo / total;
            var parcelaCredor = Arredondar(parcela * participacao);
            var totalCredor = Math.Min(x.Saldo, Arredondar(parcelaCredor * meses));
            return new CredorPlanoDto(x.Credor.Trim(), x.Tipo, false, x.Saldo, Math.Round(participacao * 100, 2), x.ParcelaAtual,
                parcelaCredor, totalCredor, Arredondar(x.Saldo - totalCredor));
        }).ToList();

        if (d.Dividas.Any(x => x.Excluida))
            avisos.Add("Dívidas marcadas como excluídas (garantia real, financiamento imobiliário, crédito rural) ficam fora do plano (art. 104-A, §1º, do CDC).");
        avisos.Add("No plano judicial compulsório o principal é corrigido monetariamente por índices oficiais e a primeira parcela vence em até 180 dias da homologação (art. 104-B, §4º, do CDC).");

        return new ResultadoSuperendividamentoDto(totalDividas, total, parcelasAtuais,
            d.RendaLiquida > 0 ? Math.Round(parcelasAtuais / d.RendaLiquida * 100, 2) : 0,
            preservado, capacidade, superendividado, parcela, meses,
            d.RendaLiquida > 0 ? Math.Round(parcela / d.RendaLiquida * 100, 2) : 0,
            remanescente == 0 && capacidade > 0, remanescente, credores, avisos);
    }

    private static decimal Pct(decimal fracao) => Math.Round(fracao * 100, 4);
    private static decimal Arredondar(decimal v) => Math.Round(v, 2, MidpointRounding.AwayFromZero);
}
