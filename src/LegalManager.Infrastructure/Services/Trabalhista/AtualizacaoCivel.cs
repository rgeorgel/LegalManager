using System.Globalization;
using LegalManager.Application.DTOs.Aluguel;
using LegalManager.Application.DTOs.Pensao;
using LegalManager.Domain.Enums;

namespace LegalManager.Infrastructure.Services.Trabalhista;

/// <summary>
/// Atualização de dívidas cíveis: correção pelo índice escolhido e juros simples (1% a.m., taxa legal da
/// Lei 14.905/2024 ou taxa informada) pela convenção de mês cheio — um valor exigível no mês M recebe os
/// índices e os juros dos meses M até o anterior ao do cálculo. Deflação acumulada não reduz o nominal.
/// </summary>
public sealed class AtualizacaoCivel(IndiceAluguel indice, JurosPensao juros, decimal? jurosPercentualMensal,
    DateOnly dataCalculo, SeriesMensais series)
{
    public static readonly (int Ano, int Mes) InicioTaxaLegal = (2024, 9);
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    private readonly (int Ano, int Mes) _fim = (dataCalculo.Year, dataCalculo.Month);
    private readonly TipoIndice? _tipo = CalculadoraAluguel.TipoDoIndice(indice);
    private readonly Dictionary<(int, int), (decimal Fator, decimal Juros)> _cache = [];
    private readonly SortedSet<(int, int)> _faltaIndice = [];
    private readonly SortedSet<(int, int)> _faltaJuros = [];

    public static IEnumerable<TipoIndice> TiposNecessarios(IndiceAluguel indice, JurosPensao juros)
    {
        if (CalculadoraAluguel.TipoDoIndice(indice) is { } t) yield return t;
        if (juros == JurosPensao.TaxaLegal) { yield return TipoIndice.SELIC; yield return TipoIndice.IPCA; }
    }

    public static string DescricaoJuros(JurosPensao juros, decimal? pct) => juros switch
    {
        JurosPensao.UmPorCentoAoMes => "1% ao mês (juros simples)",
        JurosPensao.TaxaLegal => "Taxa legal: 1% ao mês até ago/2024 e Selic − IPCA desde set/2024 (Lei 14.905/2024)",
        JurosPensao.Personalizado => $"{(pct ?? 0).ToString("0.####", PtBr)}% ao mês (juros simples)",
        _ => "Sem juros de mora"
    };

    /// <summary>Fator de correção e taxa de juros acumulada (fração) de um valor exigível na data informada.</summary>
    public (decimal Fator, decimal Juros) Fatores(DateOnly exigivel)
    {
        var inicio = (exigivel.Year, exigivel.Month);
        if (_cache.TryGetValue(inicio, out var c)) return c;
        var fator = 1m;
        var taxa = 0m;
        foreach (var m in AtualizacaoTrabalhista.Meses(inicio, _fim))
        {
            if (_tipo is { } tipo)
            {
                if (series.Taxa(tipo, m, out var t)) fator *= 1 + t;
                else _faltaIndice.Add(m);
            }
            taxa += juros switch
            {
                JurosPensao.UmPorCentoAoMes => 0.01m,
                JurosPensao.Personalizado => (jurosPercentualMensal ?? 0) / 100m,
                JurosPensao.TaxaLegal when m.CompareTo(InicioTaxaLegal) < 0 => 0.01m,
                JurosPensao.TaxaLegal => series.Taxa(TipoIndice.SELIC, m, out var selic) && series.Taxa(TipoIndice.IPCA, m, out var ipca)
                    ? Math.Max(0, selic - ipca)
                    : FaltaJuros(m),
                _ => 0
            };
        }
        return _cache[inicio] = (Math.Round(Math.Max(1, fator), 6, MidpointRounding.AwayFromZero),
                                 Math.Round(taxa, 6, MidpointRounding.AwayFromZero));
    }

    public ValorAtualizado Atualizar(decimal valor, DateOnly exigivel)
    {
        if (valor == 0) return new ValorAtualizado(0, 0, 0, 1, 0);
        var (fator, taxa) = Fatores(exigivel);
        var corrigido = Arredondar(valor * fator);
        return new ValorAtualizado(valor, corrigido, Arredondar(corrigido * taxa), fator, Math.Round(taxa * 100, 4));
    }

    public IEnumerable<string> Avisos()
    {
        if (_faltaIndice.Count > 0)
            yield return $"{CalculadoraAluguel.NomeIndice(indice)} ainda não publicado para {Lista(_faltaIndice)} — considerado 0% nesses meses.";
        if (_faltaJuros.Count > 0)
            yield return $"Selic/IPCA ainda não publicados para {Lista(_faltaJuros)} — taxa legal considerada 0% nesses meses.";
    }

    private decimal FaltaJuros((int, int) m) { _faltaJuros.Add(m); return 0; }
    private static string Lista(IEnumerable<(int Ano, int Mes)> meses) => string.Join(", ", meses.Select(m => $"{m.Mes:00}/{m.Ano}"));
    private static decimal Arredondar(decimal v) => Math.Round(v, 2, MidpointRounding.AwayFromZero);
}
