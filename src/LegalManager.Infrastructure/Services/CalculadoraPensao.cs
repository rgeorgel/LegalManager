using System.Globalization;
using LegalManager.Application.DTOs.Pensao;
using LegalManager.Domain.Enums;

namespace LegalManager.Infrastructure.Services;

/// <summary>Séries mensais já carregadas do banco (taxas fracionárias: 0,0042 = 0,42%).</summary>
public record SeriesPensao(
    IReadOnlyDictionary<(int Ano, int Mes), decimal> Correcao,
    IReadOnlyDictionary<(int Ano, int Mes), decimal> Selic,
    IReadOnlyDictionary<(int Ano, int Mes), decimal> Ipca,
    IReadOnlyList<SalarioMinimoVigenciaDto> SalariosMinimos);

/// <summary>
/// Débito de pensão alimentícia em atraso. Convenção "mês cheio": uma parcela que vence no mês M,
/// atualizada até a data do cálculo no mês N, recebe os índices (e os juros) dos meses M a N−1.
/// Juros simples sobre o valor corrigido. Pagamento parcial abate a própria parcela no vencimento.
/// Deflação acumulada não reduz o valor nominal (fator mínimo 1).
/// </summary>
public static class CalculadoraPensao
{
    /// <summary>1º mês da taxa legal Selic − IPCA (Lei 14.905/2024, em vigor desde 30/08/2024).</summary>
    public static readonly (int Ano, int Mes) InicioTaxaLegal = (2024, 9);

    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    public static TipoIndice? TipoDoIndice(IndicePensao indice) => indice switch
    {
        IndicePensao.INPC => TipoIndice.INPC,
        IndicePensao.IPCA => TipoIndice.IPCA,
        IndicePensao.IPCAE => TipoIndice.TJSP,
        IndicePensao.IGPM => TipoIndice.IGPM,
        _ => null
    };

    public static string NomeIndice(IndicePensao indice) => indice switch
    {
        IndicePensao.INPC => "INPC (IBGE)",
        IndicePensao.IPCA => "IPCA (IBGE)",
        IndicePensao.IPCAE => "IPCA-E (IBGE)",
        IndicePensao.IGPM => "IGP-M (FGV)",
        _ => "Sem correção monetária"
    };

    public static string NomeJuros(CalcularPensaoDto dto) => dto.Juros switch
    {
        JurosPensao.UmPorCentoAoMes => "1% ao mês (juros simples)",
        JurosPensao.TaxaLegal => "Taxa legal: 1% ao mês até ago/2024 e Selic − IPCA desde set/2024 (Lei 14.905/2024)",
        JurosPensao.Personalizado => $"{(dto.JurosPercentualMensal ?? 0).ToString("0.####", PtBr)}% ao mês (juros simples)",
        _ => "Sem juros de mora"
    };

    /// <summary>Mensagem de erro para o usuário, ou null se os dados permitem calcular.</summary>
    public static string? Validar(CalcularPensaoDto dto, SeriesPensao series)
    {
        if (dto.Parcelas is not { Count: > 0 }) return "Inclua ao menos uma parcela.";
        var futura = dto.Parcelas.FirstOrDefault(p => p.Vencimento > dto.DataCalculo);
        if (futura is not null)
            return $"A parcela de {futura.Vencimento.ToString("dd/MM/yyyy", PtBr)} vence depois da data do cálculo.";

        var semValor = dto.Parcelas.Any(p => p.ValorDevido is null);
        if (semValor && dto.Forma == FormaPensao.ValorFixo && dto.ValorFixo is not > 0)
            return "Informe o valor mensal da pensão.";
        if (semValor && dto.Forma == FormaPensao.SalarioMinimo)
        {
            if (dto.PercentualSalarioMinimo is not > 0) return "Informe o percentual do salário mínimo.";
            if (series.SalariosMinimos.Count == 0) return "A tabela do salário mínimo ainda não foi carregada. Tente de novo em alguns minutos.";
            var anterior = dto.Parcelas.FirstOrDefault(p => p.ValorDevido is null && SalarioMinimoEm(series, p.Vencimento) is null);
            if (anterior is not null)
                return $"Não há salário mínimo cadastrado para {anterior.Vencimento.ToString("MM/yyyy", PtBr)}. Informe o valor dessa parcela.";
        }
        if (dto.Juros == JurosPensao.Personalizado && dto.JurosPercentualMensal is null)
            return "Informe a taxa mensal dos juros.";
        return null;
    }

    public static decimal? SalarioMinimoEm(SeriesPensao series, DateOnly data)
    {
        decimal? valor = null;
        foreach (var s in series.SalariosMinimos)
        {
            if ((s.Ano, s.Mes).CompareTo((data.Year, data.Month)) > 0) break;
            valor = s.Valor;
        }
        return valor;
    }

    public static ResultadoPensaoDto Calcular(CalcularPensaoDto dto, SeriesPensao series)
    {
        var avisos = new List<string>();
        var indiceFaltante = new SortedSet<(int, int)>();
        var jurosFaltante = new SortedSet<(int, int)>();
        var deflacao = false;
        var pagoAMaior = new List<DateOnly>();

        var fimExclusivo = (dto.DataCalculo.Year, dto.DataCalculo.Month);
        var parcelas = dto.Parcelas.OrderBy(p => p.Vencimento).ToList();

        var calculadas = new List<(ParcelaPensaoDto P, LinhaPensaoDto L)>();
        foreach (var p in parcelas)
        {
            decimal? sm = null;
            decimal devido;
            if (p.ValorDevido is { } informado) devido = informado;
            else if (dto.Forma == FormaPensao.SalarioMinimo)
            {
                sm = SalarioMinimoEm(series, p.Vencimento) ?? 0;
                devido = Arredondar(sm.Value * (dto.PercentualSalarioMinimo ?? 0) / 100m);
            }
            else devido = dto.ValorFixo ?? 0;

            var pago = p.ValorPago;
            if (pago > devido) pagoAMaior.Add(p.Vencimento);
            var saldo = Math.Max(0, devido - pago);

            // Correção: índices dos meses do vencimento até o mês anterior ao do cálculo.
            var fator = 1m;
            if (dto.Indice != IndicePensao.Nenhum)
            {
                foreach (var m in Meses((p.Vencimento.Year, p.Vencimento.Month), fimExclusivo))
                {
                    if (series.Correcao.TryGetValue(m, out var taxa)) fator *= 1 + taxa;
                    else indiceFaltante.Add(m);
                }
                if (fator < 1) { fator = 1; deflacao = true; }
            }
            fator = Math.Round(fator, 6, MidpointRounding.AwayFromZero);
            var corrigido = Arredondar(saldo * fator);

            // Juros: do vencimento (ou da citação, se posterior) até o mês anterior ao do cálculo.
            var inicioJuros = (p.Vencimento.Year, p.Vencimento.Month);
            if (dto.DataCitacao is { } cit && (cit.Year, cit.Month).CompareTo(inicioJuros) > 0)
                inicioJuros = (cit.Year, cit.Month);
            var taxaJuros = 0m;
            foreach (var m in Meses(inicioJuros, fimExclusivo))
                taxaJuros += TaxaJurosMes(dto, series, m, jurosFaltante);
            taxaJuros = Math.Round(taxaJuros, 6, MidpointRounding.AwayFromZero);
            var juros = Arredondar(corrigido * taxaJuros);

            var linha = new LinhaPensaoDto(
                p.Vencimento,
                string.IsNullOrWhiteSpace(p.Descricao) ? p.Vencimento.ToString("MM/yyyy", PtBr) : p.Descricao.Trim(),
                sm, devido, pago, saldo, fator, corrigido,
                Math.Round(taxaJuros * 100m, 4), juros, corrigido + juros, "");
            calculadas.Add((p, linha));
        }

        TotaisPensaoDto? prisao = null, penhora = null;
        List<LinhaPensaoDto> linhas;
        TotaisPensaoDto totais;
        if (dto.SepararRitos)
        {
            var ajuizamento = dto.DataAjuizamento ?? dto.DataCalculo;
            // Índices (não as linhas): duas parcelas iguais (mensal e 13ª no mesmo dia) são distintas.
            var emAberto = Enumerable.Range(0, calculadas.Count).Where(i => calculadas[i].L.Saldo > 0).ToList();
            var noRitoPrisao = emAberto.Where(i => calculadas[i].L.Vencimento < ajuizamento)
                .OrderByDescending(i => calculadas[i].L.Vencimento).ThenByDescending(i => i)
                .Take(3).ToHashSet();
            noRitoPrisao.UnionWith(emAberto.Where(i => calculadas[i].L.Vencimento >= ajuizamento));

            linhas = calculadas.Select((c, i) => c.L.Saldo <= 0 ? c.L
                : c.L with { Rito = noRitoPrisao.Contains(i) ? "prisao" : "penhora" }).ToList();
            // A multa do art. 523, §1º, só cabe no cumprimento pela penhora (art. 528, §8º, do CPC).
            prisao = Totalizar(linhas.Where(l => l.Rito == "prisao").ToList(), 0, dto.HonorariosPercentual);
            penhora = Totalizar(linhas.Where(l => l.Rito != "prisao").ToList(), dto.MultaPercentual, dto.HonorariosPercentual);
            totais = Somar(prisao, penhora);
        }
        else
        {
            linhas = calculadas.Select(c => c.L).ToList();
            totais = Totalizar(linhas, dto.MultaPercentual, dto.HonorariosPercentual);
        }

        if (indiceFaltante.Count > 0)
            avisos.Add($"{NomeIndice(dto.Indice)} ainda não publicado para {Lista(indiceFaltante)} — considerado 0% nesses meses.");
        if (jurosFaltante.Count > 0)
            avisos.Add($"Selic/IPCA ainda não publicados para {Lista(jurosFaltante)} — taxa legal considerada 0% nesses meses.");
        if (deflacao)
            avisos.Add("Em algumas parcelas o índice acumulado foi negativo; a correção foi considerada zero para não reduzir o valor nominal.");
        if (pagoAMaior.Count > 0)
            avisos.Add($"Pagamento acima do valor devido em {string.Join(", ", pagoAMaior.Select(d => d.ToString("MM/yyyy", PtBr)))} — o excedente não foi compensado em outras parcelas.");

        return new ResultadoPensaoDto(dto.DataCalculo, NomeIndice(dto.Indice), NomeJuros(dto),
            linhas, totais, prisao, penhora, avisos);
    }

    private static decimal TaxaJurosMes(CalcularPensaoDto dto, SeriesPensao series, (int Ano, int Mes) m, SortedSet<(int, int)> faltantes)
    {
        switch (dto.Juros)
        {
            case JurosPensao.UmPorCentoAoMes: return 0.01m;
            case JurosPensao.Personalizado: return (dto.JurosPercentualMensal ?? 0) / 100m;
            case JurosPensao.TaxaLegal:
                if (m.CompareTo(InicioTaxaLegal) < 0) return 0.01m;
                if (series.Selic.TryGetValue(m, out var selic) && series.Ipca.TryGetValue(m, out var ipca))
                    return Math.Max(0, selic - ipca); // art. 406, §3º: taxa negativa vale zero
                faltantes.Add(m);
                return 0;
            default: return 0;
        }
    }

    private static TotaisPensaoDto Totalizar(IReadOnlyList<LinhaPensaoDto> linhas, decimal multaPct, decimal honorariosPct)
    {
        var saldo = linhas.Sum(l => l.Saldo);
        var corrigido = linhas.Sum(l => l.ValorCorrigido);
        var juros = linhas.Sum(l => l.Juros);
        var subtotal = corrigido + juros;
        var multa = Arredondar(subtotal * multaPct / 100m);
        var honorarios = Arredondar(subtotal * honorariosPct / 100m);
        return new TotaisPensaoDto(
            linhas.Count(l => l.Saldo > 0),
            linhas.Sum(l => l.ValorDevido), linhas.Sum(l => l.ValorPago), saldo,
            corrigido - saldo, corrigido, juros, subtotal, multa, honorarios, subtotal + multa + honorarios);
    }

    private static TotaisPensaoDto Somar(TotaisPensaoDto a, TotaisPensaoDto b) => new(
        a.Parcelas + b.Parcelas, a.Devido + b.Devido, a.Pago + b.Pago, a.Saldo + b.Saldo,
        a.Correcao + b.Correcao, a.Corrigido + b.Corrigido, a.Juros + b.Juros, a.Subtotal + b.Subtotal,
        a.Multa + b.Multa, a.Honorarios + b.Honorarios, a.Total + b.Total);

    /// <summary>Meses de <paramref name="inicio"/> (inclusive) a <paramref name="fimExclusivo"/>.</summary>
    internal static IEnumerable<(int Ano, int Mes)> Meses((int Ano, int Mes) inicio, (int Ano, int Mes) fimExclusivo)
    {
        var (ano, mes) = inicio;
        while ((ano, mes).CompareTo(fimExclusivo) < 0)
        {
            yield return (ano, mes);
            if (++mes > 12) { mes = 1; ano++; }
        }
    }

    private static string Lista(IEnumerable<(int Ano, int Mes)> meses) =>
        string.Join(", ", meses.Select(m => $"{m.Mes:00}/{m.Ano}"));

    private static decimal Arredondar(decimal v) => Math.Round(v, 2, MidpointRounding.AwayFromZero);
}
