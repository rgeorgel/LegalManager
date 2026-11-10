using System.Globalization;
using LegalManager.Application.DTOs.Aluguel;
using LegalManager.Application.DTOs.Pensao;
using LegalManager.Domain.Enums;

namespace LegalManager.Infrastructure.Services.Trabalhista;

/// <summary>
/// Aluguel: reajuste pelo índice do contrato e débito de aluguéis em atraso (cobrança e despejo, Lei 8.245/91).
/// Reajuste no aniversário do contrato pelo índice acumulado dos meses do período anterior (ex.: contrato de março,
/// reajuste anual em março pelo acumulado de março a fevereiro). Débito: mês cheio, juros simples sobre o corrigido,
/// multa moratória sobre o corrigido e honorários sobre o montante.
/// </summary>
public static class CalculadoraAluguel
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    public static TipoIndice? TipoDoIndice(IndiceAluguel indice) => indice switch
    {
        IndiceAluguel.IGPM => TipoIndice.IGPM,
        IndiceAluguel.IPCA => TipoIndice.IPCA,
        IndiceAluguel.INPC => TipoIndice.INPC,
        IndiceAluguel.IGPDI => TipoIndice.IGPDI,
        IndiceAluguel.IPCAE => TipoIndice.TJSP,
        _ => null
    };

    public static string NomeIndice(IndiceAluguel indice) => indice switch
    {
        IndiceAluguel.IGPM => "IGP-M (FGV)",
        IndiceAluguel.IPCA => "IPCA (IBGE)",
        IndiceAluguel.INPC => "INPC (IBGE)",
        IndiceAluguel.IGPDI => "IGP-DI (FGV)",
        IndiceAluguel.IPCAE => "IPCA-E (IBGE)",
        _ => "Sem índice"
    };

    // ── Reajuste ────────────────────────────────────────────────────────

    public static string? ValidarContrato(ContratoAluguelDto c) =>
        c.PeriodicidadeMeses is not (3 or 6 or 12) ? "A periodicidade do reajuste deve ser anual, semestral ou trimestral." : null;

    /// <summary>Reajustes do início do contrato até <paramref name="ate"/> (inclusive).</summary>
    public static ResultadoReajusteAluguelDto Reajustes(ContratoAluguelDto c, DateOnly ate, SeriesMensais series)
    {
        var avisos = new List<string>();
        var linhas = new List<ReajusteAluguelLinhaDto>();
        var faltantes = new SortedSet<(int, int)>();
        var tipo = TipoDoIndice(c.Indice);
        var valor = c.ValorInicial;
        var deflacao = false;

        var k = 1;
        for (; ; k++)
        {
            var data = c.InicioContrato.AddMonths(k * c.PeriodicidadeMeses);
            if (data > ate) break;
            if (tipo is null) continue;
            var inicioPeriodo = data.AddMonths(-c.PeriodicidadeMeses);
            var fator = 1m;
            var estimado = false;
            foreach (var m in AtualizacaoTrabalhista.Meses((inicioPeriodo.Year, inicioPeriodo.Month), (data.Year, data.Month)))
            {
                if (series.Taxa(tipo.Value, m, out var taxa)) fator *= 1 + taxa;
                else { faltantes.Add(m); estimado = true; }
            }
            var acumulado = Math.Round((fator - 1) * 100, 4);
            var anterior = valor;
            if (fator < 1 && c.NaoReduzir) deflacao = true;
            else valor = Arredondar(valor * fator);
            var fimPeriodo = data.AddMonths(-1);
            linhas.Add(new ReajusteAluguelLinhaDto(data, $"{inicioPeriodo.ToString("MM/yyyy", PtBr)} a {fimPeriodo.ToString("MM/yyyy", PtBr)}", acumulado, anterior, valor, estimado));
        }
        var proximo = c.InicioContrato.AddMonths(k * c.PeriodicidadeMeses);

        if (c.PeriodicidadeMeses < 12)
            avisos.Add("A Lei 10.192/2001 (art. 2º, §1º) considera nula a cláusula de reajuste com periodicidade inferior a um ano.");
        if (faltantes.Count > 0)
            avisos.Add($"{NomeIndice(c.Indice)} ainda não publicado para {Lista(faltantes)} — considerado 0% nesses meses.");
        if (deflacao)
            avisos.Add("Em algum reajuste o índice acumulado foi negativo; o aluguel foi mantido (não reduzir em caso de deflação).");
        if (tipo is null)
            avisos.Add("Contrato sem índice de reajuste: o aluguel permanece o valor inicial.");

        return new ResultadoReajusteAluguelDto(linhas, valor, proximo, NomeIndice(c.Indice), avisos);
    }

    /// <summary>Aluguel vigente numa data, a partir dos reajustes já calculados.</summary>
    public static decimal ValorEm(ContratoAluguelDto c, IReadOnlyList<ReajusteAluguelLinhaDto> reajustes, DateOnly data)
    {
        var valor = c.ValorInicial;
        foreach (var r in reajustes)
        {
            if (r.Data > data) break;
            valor = r.ValorNovo;
        }
        return valor;
    }

    public static string? ValidarParcelas(GerarParcelasAluguelDto dto)
    {
        if (ValidarContrato(dto.Contrato) is { } erro) return erro;
        if (dto.UltimoMes < dto.PrimeiroMes) return "O último mês em atraso é anterior ao primeiro.";
        if ((dto.UltimoMes.Year - dto.PrimeiroMes.Year) * 12 + dto.UltimoMes.Month - dto.PrimeiroMes.Month >= 600)
            return "Período longo demais (máximo de 600 parcelas).";
        return null;
    }

    /// <summary>Parcelas mensais do período em atraso com o aluguel vigente em cada vencimento.</summary>
    public static List<ParcelaAluguelDto> GerarParcelas(GerarParcelasAluguelDto dto, SeriesMensais series)
    {
        var ultimo = new DateOnly(dto.UltimoMes.Year, dto.UltimoMes.Month, DateTime.DaysInMonth(dto.UltimoMes.Year, dto.UltimoMes.Month));
        var reajustes = Reajustes(dto.Contrato, ultimo, series).Reajustes;
        var lista = new List<ParcelaAluguelDto>();
        foreach (var (ano, mes) in AtualizacaoTrabalhista.Meses((dto.PrimeiroMes.Year, dto.PrimeiroMes.Month),
                     dto.UltimoMes.Month == 12 ? (dto.UltimoMes.Year + 1, 1) : (dto.UltimoMes.Year, dto.UltimoMes.Month + 1)))
        {
            var venc = new DateOnly(ano, mes, Math.Min(dto.DiaVencimento, DateTime.DaysInMonth(ano, mes)));
            lista.Add(new ParcelaAluguelDto(venc, ValorEm(dto.Contrato, reajustes, venc), dto.Encargos, 0, $"{mes:00}/{ano}"));
        }
        return lista;
    }

    // ── Débito ──────────────────────────────────────────────────────────

    public static string? ValidarDebito(CalcularDebitoAluguelDto dto)
    {
        if (dto.Parcelas is not { Count: > 0 }) return "Inclua ao menos uma parcela.";
        var futura = dto.Parcelas.FirstOrDefault(p => p.Vencimento > dto.DataCalculo);
        if (futura is not null) return $"A parcela de {futura.Vencimento.ToString("dd/MM/yyyy", PtBr)} vence depois da data do cálculo.";
        if (dto.Juros == JurosPensao.Personalizado && dto.JurosPercentualMensal is null) return "Informe a taxa mensal dos juros.";
        return null;
    }

    public static string DescricaoJuros(CalcularDebitoAluguelDto dto) => AtualizacaoCivel.DescricaoJuros(dto.Juros, dto.JurosPercentualMensal);

    public static ResultadoDebitoAluguelDto CalcularDebito(CalcularDebitoAluguelDto dto, SeriesMensais series)
    {
        var atualizacao = new AtualizacaoCivel(dto.IndiceCorrecao, dto.Juros, dto.JurosPercentualMensal, dto.DataCalculo, series);
        var pagoAMaior = new List<DateOnly>();
        var linhas = new List<LinhaDebitoAluguelDto>();

        foreach (var p in dto.Parcelas.OrderBy(p => p.Vencimento))
        {
            var devido = p.Aluguel + p.Encargos;
            if (p.ValorPago > devido) pagoAMaior.Add(p.Vencimento);
            var saldo = Math.Max(0, devido - p.ValorPago);
            var a = atualizacao.Atualizar(saldo, p.Vencimento);
            var multa = Arredondar(a.Corrigido * dto.MultaMoratoria / 100m);

            linhas.Add(new LinhaDebitoAluguelDto(p.Vencimento,
                string.IsNullOrWhiteSpace(p.Descricao) ? p.Vencimento.ToString("MM/yyyy", PtBr) : p.Descricao.Trim(),
                p.Aluguel, p.Encargos, p.ValorPago, saldo, a.FatorCorrecao, a.Corrigido, a.JurosPercentual, a.Juros, multa,
                a.Corrigido + a.Juros + multa));
        }

        var saldoTotal = linhas.Sum(l => l.Saldo);
        var corrigidoTotal = linhas.Sum(l => l.Corrigido);
        var subtotal = linhas.Sum(l => l.Total);
        var honorarios = Arredondar(subtotal * dto.HonorariosPercentual / 100m);

        var avisos = atualizacao.Avisos().ToList();
        if (pagoAMaior.Count > 0)
            avisos.Add($"Pagamento acima do valor devido em {string.Join(", ", pagoAMaior.Select(d => d.ToString("MM/yyyy", PtBr)))} — o excedente não foi compensado em outras parcelas.");
        if (dto.MultaMoratoria > 10)
            avisos.Add("Multa moratória acima de 10% pode ser reduzida pelo juiz quando manifestamente excessiva (art. 413 do CC).");

        return new ResultadoDebitoAluguelDto(linhas, linhas.Count(l => l.Saldo > 0), saldoTotal, corrigidoTotal - saldoTotal,
            linhas.Sum(l => l.Juros), linhas.Sum(l => l.Multa), subtotal, honorarios, subtotal + honorarios,
            NomeIndice(dto.IndiceCorrecao), DescricaoJuros(dto), avisos);
    }

    private static string Lista(IEnumerable<(int Ano, int Mes)> meses) => string.Join(", ", meses.Select(m => $"{m.Mes:00}/{m.Ano}"));
    private static decimal Arredondar(decimal v) => Math.Round(v, 2, MidpointRounding.AwayFromZero);
}
