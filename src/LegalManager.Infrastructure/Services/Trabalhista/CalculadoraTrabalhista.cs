using System.Globalization;
using LegalManager.Application.DTOs.Trabalhista;

namespace LegalManager.Infrastructure.Services.Trabalhista;

/// <summary>
/// Estimativa de verbas rescisórias e pedidos de reclamação trabalhista (liquidação simplificada para a inicial).
/// Valores brutos, sem descontos de INSS e IRRF. Pedidos mensais usam a mesma média em todo o período não prescrito
/// e o último salário; reflexos de 13º, férias + 1/3 e aviso pelo duodécimo (Σ ÷ 12) e pela média dos últimos 12 meses.
/// </summary>
public static class CalculadoraTrabalhista
{
    public const string GrupoRescisorias = "Verbas rescisórias";
    public const string GrupoMultas = "Multas e indenizações";
    private const decimal AliquotaFgts = 0.08m;
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    private sealed record Parte(decimal Valor, DateOnly Exigivel);

    private sealed class Verba(string grupo, string descricao, string memoria, bool atualiza = true)
    {
        public string Grupo { get; } = grupo;
        public string Descricao { get; } = descricao;
        public string Memoria { get; } = memoria;
        public bool Atualiza { get; } = atualiza;
        public List<Parte> Partes { get; } = [];
        public decimal Valor => Partes.Sum(p => p.Valor);
    }

    public static string NomeRescisao(TipoRescisao r) => r switch
    {
        TipoRescisao.SemJustaCausa => "Dispensa sem justa causa",
        TipoRescisao.PedidoDemissao => "Pedido de demissão",
        TipoRescisao.JustaCausa => "Dispensa por justa causa",
        TipoRescisao.Acordo => "Acordo (art. 484-A da CLT)",
        TipoRescisao.RescisaoIndireta => "Rescisão indireta (art. 483 da CLT)",
        _ => "Término de contrato por prazo determinado"
    };

    public static decimal PercentualMultaFgts(TipoRescisao r) => r switch
    {
        TipoRescisao.SemJustaCausa or TipoRescisao.RescisaoIndireta => 40,
        TipoRescisao.Acordo => 20,
        _ => 0
    };

    public static string? Validar(CalcularTrabalhistaDto d)
    {
        if (d.Saida < d.Admissao) return "A data de saída é anterior à admissão.";
        if (d.DataCalculo < d.Saida) return "A data do cálculo precisa ser igual ou posterior à saída.";
        if (d.DataAjuizamento is { } aj && aj > d.DataCalculo) return "O ajuizamento não pode ser depois da data do cálculo.";
        if (d.FeriasVencidasEmDobro > d.FeriasVencidas) return "Os períodos de férias em dobro não podem passar do total de férias vencidas.";
        if (d.InsalubridadeGrau is not (0 or 10 or 20 or 40)) return "O grau de insalubridade deve ser 10%, 20% ou 40%.";
        return null;
    }

    public static ResultadoTrabalhistaDto Calcular(CalcularTrabalhistaDto d, SeriesMensais series)
    {
        var atualizacao = new AtualizacaoTrabalhista(d.Atualizacao, d.DataCalculo, d.DataAjuizamento, series);
        var verbas = new List<Verba>();
        var avisos = new List<string>();

        Verba Add(string grupo, string descricao, string memoria, decimal valor, DateOnly exigivel, bool atualiza = true)
        {
            var v = new Verba(grupo, descricao, memoria, atualiza);
            v.Partes.Add(new Parte(Arredondar(valor), exigivel));
            verbas.Add(v);
            return v;
        }

        // ── Contrato e aviso ──────────────────────────────────────────
        var anos = ContratoTrabalho.AnosCompletos(d.Admissao, d.Saida);
        var diasAviso = ContratoTrabalho.DiasAvisoProporcional(anos);
        var diasIndenizados = d.Rescisao switch
        {
            TipoRescisao.SemJustaCausa or TipoRescisao.RescisaoIndireta => d.Aviso == AvisoPrevioTipo.Trabalhado ? 0 : diasAviso,
            TipoRescisao.Acordo => d.Aviso == AvisoPrevioTipo.Trabalhado ? 0 : diasAviso / 2, // art. 484-A, I, "a": metade
            _ => 0
        };
        if (d.Rescisao is TipoRescisao.PedidoDemissao or TipoRescisao.JustaCausa or TipoRescisao.TerminoContrato) diasAviso = 0;
        // OJ 82 da SDI-1: a data de saída é a do término do aviso prévio, ainda que indenizado.
        var projetada = d.Saida.AddDays(diasIndenizados);
        var exigivelRescisao = d.Saida.AddDays(10); // art. 477, §6º
        var multaPct = PercentualMultaFgts(d.Rescisao);

        // ── Verbas rescisórias ────────────────────────────────────────
        var baseRem = d.Salario + d.MediaVariaveis;
        var diaria = baseRem / 30m;
        var diasSaldo = Math.Min(30, d.Saida.Day);
        var saldoSalario = Add(GrupoRescisorias, "Saldo de salário", $"{diasSaldo} dias × {Moeda(diaria)}", diaria * diasSaldo, exigivelRescisao);

        decimal avisoValor = 0;
        if (diasIndenizados > 0)
            avisoValor = Add(GrupoRescisorias, "Aviso prévio indenizado",
                $"{diasIndenizados} dias{(d.Rescisao == TipoRescisao.Acordo ? $" (metade de {diasAviso})" : "")} — {anos} {(anos == 1 ? "ano completo" : "anos completos")} (Lei 12.506/2011)",
                diaria * diasIndenizados, exigivelRescisao).Valor;
        if (d.Rescisao == TipoRescisao.PedidoDemissao && d.Aviso == AvisoPrevioTipo.NaoCumprido)
            Add(GrupoRescisorias, "Desconto do aviso prévio não cumprido", "30 dias (art. 487, §2º, da CLT)", -baseRem, exigivelRescisao);

        decimal valor13 = 0;
        if (d.Rescisao != TipoRescisao.JustaCausa)
            for (var ano = d.Saida.Year; ano <= projetada.Year; ano++)
            {
                var avos = ContratoTrabalho.Avos13(ano, d.Admissao, projetada);
                if (avos > 0)
                    valor13 += Add(GrupoRescisorias, $"13º salário proporcional {ano}", $"{avos}/12 × {Moeda(baseRem)}", baseRem * avos / 12m, exigivelRescisao).Valor;
            }

        decimal valorFerias = 0;
        if (d.FeriasVencidas > 0)
        {
            var vf = Add(GrupoRescisorias, "Férias vencidas", $"{d.FeriasVencidas} {(d.FeriasVencidas == 1 ? "período" : "períodos")} × {Moeda(baseRem)}", baseRem * d.FeriasVencidas, exigivelRescisao).Valor;
            valorFerias += vf + Add(GrupoRescisorias, "1/3 constitucional sobre férias vencidas", $"{Moeda(vf)} ÷ 3", vf / 3m, exigivelRescisao).Valor;
            if (d.FeriasVencidasEmDobro > 0)
            {
                var dobra = Add(GrupoRescisorias, "Dobra das férias vencidas (art. 137 da CLT)",
                    $"{d.FeriasVencidasEmDobro} {(d.FeriasVencidasEmDobro == 1 ? "período" : "períodos")} fora do prazo concessivo", baseRem * d.FeriasVencidasEmDobro, exigivelRescisao).Valor;
                valorFerias += dobra + Add(GrupoRescisorias, "1/3 sobre a dobra das férias", $"{Moeda(dobra)} ÷ 3", dobra / 3m, exigivelRescisao).Valor;
            }
        }
        // Súmulas 171 e 261 do TST: férias proporcionais em toda rescisão, salvo justa causa.
        if (d.Rescisao != TipoRescisao.JustaCausa)
        {
            var completados = ContratoTrabalho.AnosCompletos(d.Admissao, projetada) - anos;
            if (completados > 0)
            {
                var fi = Add(GrupoRescisorias, "Férias integrais (período completado na projeção do aviso)", $"{completados} × {Moeda(baseRem)}", baseRem * completados, exigivelRescisao).Valor;
                valorFerias += fi + Add(GrupoRescisorias, "1/3 sobre férias integrais", $"{Moeda(fi)} ÷ 3", fi / 3m, exigivelRescisao).Valor;
            }
            var avosFerias = ContratoTrabalho.AvosFerias(d.Admissao, projetada);
            if (avosFerias > 0)
            {
                var fp = Add(GrupoRescisorias, "Férias proporcionais", $"{avosFerias}/12 × {Moeda(baseRem)}", baseRem * avosFerias / 12m, exigivelRescisao).Valor;
                valorFerias += fp + Add(GrupoRescisorias, "1/3 sobre férias proporcionais", $"{Moeda(fp)} ÷ 3", fp / 3m, exigivelRescisao).Valor;
            }
        }

        // FGTS não incide sobre férias indenizadas (OJ 195 da SDI-1).
        var baseFgtsRescisorio = saldoSalario.Valor + avisoValor + valor13;
        var fgtsRescisorio = Add(GrupoRescisorias, "FGTS sobre saldo de salário, aviso e 13º (8%)", $"8% × {Moeda(baseFgtsRescisorio)}",
            baseFgtsRescisorio * AliquotaFgts, exigivelRescisao).Valor;

        decimal multaFgts = 0;
        if (multaPct > 0)
        {
            decimal saldoFgts;
            if (d.SaldoFgts is { } informado) saldoFgts = informado;
            else
            {
                var mesesContrato = (d.Saida.DayNumber - d.Admissao.DayNumber + 1) / 30.4375m;
                saldoFgts = Arredondar(baseRem * AliquotaFgts * mesesContrato * 13m / 12m);
                avisos.Add($"Saldo do FGTS estimado em {Moeda(saldoFgts)} (8% da remuneração e do 13º em {mesesContrato.ToString("0.#", PtBr)} meses, sem a correção da conta). Informe o saldo para fins rescisórios do extrato para um valor exato.");
            }
            var baseMulta = saldoFgts + fgtsRescisorio;
            multaFgts = Add(GrupoRescisorias, $"Multa de {multaPct:0}% do FGTS", $"{multaPct:0}% × {Moeda(baseMulta)}", baseMulta * multaPct / 100m, exigivelRescisao).Valor;
        }

        if (d.ValoresPagos > 0)
            Add(GrupoRescisorias, "Valores já pagos na rescisão", "abatidos", -d.ValoresPagos, exigivelRescisao);

        // ── Pedidos mensais ───────────────────────────────────────────
        var meses = new List<MesTrabalhistaDto>();
        DateOnly? inicioPedidos = null;
        var temMensais = d.HorasExtrasMes > 0 || d.HorasNoturnasMes > 0 || d.InsalubridadeGrau > 0 || d.Periculosidade;
        if (temMensais)
        {
            var referencia = d.DataAjuizamento ?? d.DataCalculo;
            var inicio = d.Admissao;
            if (d.LimitarPrescricao && referencia.AddYears(-5) > inicio)
            {
                inicio = referencia.AddYears(-5);
                avisos.Add($"Pedidos mensais limitados a partir de {inicio.ToString("dd/MM/yyyy", PtBr)} (prescrição quinquenal, art. 7º, XXIX, da CF).");
            }
            if (inicio <= d.Saida)
            {
                inicioPedidos = inicio;
                CalcularMensais(d, series, inicio, diasIndenizados, multaPct, exigivelRescisao, verbas, meses);
            }
            else avisos.Add("Todo o período dos pedidos mensais está prescrito.");
            if (d.Periculosidade && d.InsalubridadeGrau > 0)
                avisos.Add("Insalubridade e periculosidade não se acumulam (art. 193, §2º, da CLT): o empregado opta por um dos adicionais.");
        }

        // ── Multas e indenizações ─────────────────────────────────────
        if (d.Multa477)
            Add(GrupoMultas, "Multa do art. 477, §8º, da CLT", $"1 salário ({Moeda(d.Salario)})", d.Salario, exigivelRescisao);
        if (d.Multa467)
        {
            var incontroversas = Math.Max(0, saldoSalario.Valor + avisoValor + valor13 + valorFerias + multaFgts);
            Add(GrupoMultas, "Multa do art. 467 da CLT", $"50% × {Moeda(incontroversas)} (rescisórias incontroversas)", incontroversas / 2m, exigivelRescisao);
        }
        if (d.DanoMoral > 0)
            Add(GrupoMultas, "Indenização por danos morais", "valor pedido — atualização a partir do arbitramento (Súmula 439 do TST)", d.DanoMoral, d.DataCalculo, atualiza: false);
        if (d.OutrosPedidos > 0)
            Add(GrupoMultas, string.IsNullOrWhiteSpace(d.OutrosDescricao) ? "Outros pedidos" : d.OutrosDescricao.Trim(), "valor informado", d.OutrosPedidos, d.DataCalculo, atualiza: false);

        // ── Atualização e totais ──────────────────────────────────────
        var resultado = verbas.Select(v =>
        {
            decimal correcao = 0, juros = 0, total = 0;
            foreach (var p in v.Partes)
            {
                var a = v.Atualiza ? atualizacao.Atualizar(p.Valor, p.Exigivel) : new ValorAtualizado(p.Valor, p.Valor, 0, 1, 0);
                correcao += a.Correcao; juros += a.Juros; total += a.Total;
            }
            return new VerbaTrabalhistaDto(v.Grupo, v.Descricao, v.Memoria, v.Valor, correcao, juros, total);
        }).ToList();

        var grupos = resultado.GroupBy(v => v.Grupo)
            .Select(g => new GrupoTrabalhistaDto(g.Key, g.Sum(v => v.Valor), g.Sum(v => v.Total))).ToList();
        var totalNominal = resultado.Sum(v => v.Valor);
        var totalAtualizado = resultado.Sum(v => v.Total);
        var honorarios = Arredondar(totalAtualizado * d.HonorariosPercentual / 100m);

        var referenciaBienal = d.DataAjuizamento ?? d.DataCalculo;
        if (referenciaBienal > d.Saida.AddYears(2))
            avisos.Add($"{(d.DataAjuizamento is null ? "Já se passaram" : "O ajuizamento ocorreu")} mais de 2 anos após a saída — atenção à prescrição bienal (art. 7º, XXIX, da CF).");
        avisos.AddRange(atualizacao.Avisos());

        return new ResultadoTrabalhistaDto(anos, diasAviso, diasIndenizados, projetada, inicioPedidos,
            resultado, grupos, meses, totalNominal, totalAtualizado, honorarios, totalAtualizado + honorarios,
            AtualizacaoTrabalhista.Descricao(d.Atualizacao, d.DataAjuizamento), avisos);
    }

    private static void CalcularMensais(CalcularTrabalhistaDto d, SeriesMensais series, DateOnly inicio, int diasIndenizados,
        decimal multaPct, DateOnly exigivelRescisao, List<Verba> verbas, List<MesTrabalhistaDto> meses)
    {
        var hora = d.Salario / d.Divisor;
        var horasNoturnas = d.HorasNoturnasMes * (d.HoraNoturnaReduzida ? 8m / 7m : 1m);

        // Por grupo: principal e DSR mês a mês.
        var grupos = new (string Nome, string Principal, string Memoria, bool ComDsr, List<Parte> Valores, List<Parte> Dsr)[]
        {
            ("Horas extras", $"Horas extras ({d.AdicionalHoraExtra:0.##}%)",
                $"{d.HorasExtrasMes:0.##} h/mês × {Moeda(hora * (1 + d.AdicionalHoraExtra / 100m))} (divisor {d.Divisor})", true, [], []),
            ("Adicional noturno", $"Adicional noturno ({d.AdicionalNoturno:0.##}%)",
                $"{d.HorasNoturnasMes:0.##} h/mês{(d.HoraNoturnaReduzida ? " × 8/7 (hora reduzida)" : "")} × {Moeda(hora * d.AdicionalNoturno / 100m)}", true, [], []),
            ("Adicional de insalubridade", $"Adicional de insalubridade ({d.InsalubridadeGrau:0}%)",
                $"{d.InsalubridadeGrau:0}% do salário mínimo do mês", false, [], []),
            ("Adicional de periculosidade", "Adicional de periculosidade (30%)",
                $"30% × {Moeda(d.Salario)} (art. 193, §1º, da CLT)", false, [], []),
        };

        foreach (var (ano, mes) in AtualizacaoTrabalhista.Meses((inicio.Year, inicio.Month), ((d.Saida.Month == 12 ? d.Saida.Year + 1 : d.Saida.Year), d.Saida.Month == 12 ? 1 : d.Saida.Month + 1)))
        {
            var dias = ContratoTrabalho.DiasTrabalhadosNoMes(ano, mes, inicio, d.Saida);
            if (dias == 0) continue;
            var fracao = dias >= DateTime.DaysInMonth(ano, mes) ? 1m : Math.Min(30, dias) / 30m;
            var (uteis, repousos) = ContratoTrabalho.DiasDsr(ano, mes);
            var exigivel = new DateOnly(ano, mes, 1).AddMonths(1);

            var he = Arredondar(hora * (1 + d.AdicionalHoraExtra / 100m) * d.HorasExtrasMes * fracao);
            var an = Arredondar(hora * d.AdicionalNoturno / 100m * horasNoturnas * fracao);
            var dsrHe = uteis > 0 ? Arredondar(he / uteis * repousos) : 0;
            var dsrAn = uteis > 0 ? Arredondar(an / uteis * repousos) : 0;
            var insal = d.InsalubridadeGrau > 0 ? Arredondar(d.InsalubridadeGrau / 100m * (series.SalarioMinimoEm(ano, mes) ?? 0) * fracao) : 0;
            var peric = d.Periculosidade ? Arredondar(0.30m * d.Salario * fracao) : 0;

            grupos[0].Valores.Add(new(he, exigivel)); grupos[0].Dsr.Add(new(dsrHe, exigivel));
            grupos[1].Valores.Add(new(an, exigivel)); grupos[1].Dsr.Add(new(dsrAn, exigivel));
            grupos[2].Valores.Add(new(insal, exigivel));
            grupos[3].Valores.Add(new(peric, exigivel));
            meses.Add(new MesTrabalhistaDto($"{mes:00}/{ano}", he, dsrHe, an, dsrAn, insal, peric));
        }

        foreach (var g in grupos)
        {
            if (g.Valores.Sum(p => p.Valor) == 0) continue;

            var principal = new Verba(g.Nome, g.Principal, g.Memoria);
            principal.Partes.AddRange(g.Valores);
            verbas.Add(principal);
            if (g.ComDsr)
            {
                var dsr = new Verba(g.Nome, "Reflexo em DSR (domingos e feriados)", "por mês: valor ÷ dias úteis × repousos");
                dsr.Partes.AddRange(g.Dsr);
                verbas.Add(dsr);
            }

            // Base dos reflexos: principal + DSR de cada mês.
            var mensal = g.Valores.Select((p, i) => p.Valor + (g.ComDsr ? g.Dsr[i].Valor : 0)).ToList();
            var soma = mensal.Sum();
            var refl13 = Arredondar(soma / 12m);
            verbas.Add(Unica(g.Nome, "Reflexo em 13º salário", $"{Moeda(soma)} ÷ 12", refl13, exigivelRescisao));
            verbas.Add(Unica(g.Nome, "Reflexo em férias + 1/3", $"{Moeda(soma)} ÷ 12 × 4/3", Arredondar(soma / 12m * 4m / 3m), exigivelRescisao));

            decimal reflAviso = 0;
            if (diasIndenizados > 0)
            {
                var ultimos = mensal.TakeLast(12).ToList();
                var media = ultimos.Average();
                reflAviso = Arredondar(media * diasIndenizados / 30m);
                verbas.Add(Unica(g.Nome, "Reflexo em aviso prévio", $"média de {ultimos.Count} meses ({Moeda(media)}) × {diasIndenizados}/30", reflAviso, exigivelRescisao));
            }

            var fgts = new Verba(g.Nome, "FGTS (8%) sobre as parcelas e reflexos", "8% do principal, DSR, 13º e aviso");
            fgts.Partes.AddRange(g.Valores.Select((p, i) => new Parte(Arredondar(mensal[i] * AliquotaFgts), p.Exigivel)));
            fgts.Partes.Add(new Parte(Arredondar((refl13 + reflAviso) * AliquotaFgts), exigivelRescisao));
            verbas.Add(fgts);
            if (multaPct > 0)
                verbas.Add(Unica(g.Nome, $"Multa de {multaPct:0}% sobre o FGTS", $"{multaPct:0}% × {Moeda(fgts.Valor)}", Arredondar(fgts.Valor * multaPct / 100m), exigivelRescisao));
        }
    }

    private static Verba Unica(string grupo, string descricao, string memoria, decimal valor, DateOnly exigivel)
    {
        var v = new Verba(grupo, descricao, memoria);
        v.Partes.Add(new Parte(valor, exigivel));
        return v;
    }

    private static string Moeda(decimal v) => v.ToString("C", PtBr);
    private static decimal Arredondar(decimal v) => Math.Round(v, 2, MidpointRounding.AwayFromZero);
}
