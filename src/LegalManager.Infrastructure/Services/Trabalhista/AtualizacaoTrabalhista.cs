using LegalManager.Application.DTOs.Trabalhista;
using LegalManager.Domain.Enums;

namespace LegalManager.Infrastructure.Services.Trabalhista;

public readonly record struct ValorAtualizado(decimal Valor, decimal Corrigido, decimal Juros, decimal FatorCorrecao, decimal JurosPercentual)
{
    public decimal Total => Corrigido + Juros;
    public decimal Correcao => Corrigido - Valor;
}

/// <summary>
/// Atualização de créditos trabalhistas pela ADC 58/59 com a Lei 14.905/2024 (critério do TST):
/// fase pré-judicial (até o ajuizamento) IPCA-E + juros pela TR acumulada (art. 39, caput, da Lei 8.177);
/// fase judicial Selic até ago/2024 e, desde set/2024, IPCA + juros pela taxa legal (Selic − IPCA, mínimo 0).
/// Convenção "mês cheio": um crédito exigível no mês M recebe os índices dos meses M até o anterior ao do cálculo.
/// Juros simples sobre o valor corrigido; deflação acumulada não reduz o valor nominal.
/// </summary>
public sealed class AtualizacaoTrabalhista
{
    public static readonly (int Ano, int Mes) InicioLei14905 = (2024, 9);

    private readonly CriterioAtualizacao _criterio;
    private readonly (int Ano, int Mes) _fim;
    private readonly (int Ano, int Mes)? _ajuizamento;
    private readonly SeriesMensais _series;
    private readonly Dictionary<(int, int), (decimal Fator, decimal Juros)> _cache = [];
    private readonly SortedDictionary<string, SortedSet<(int, int)>> _faltantes = new(StringComparer.Ordinal);

    public AtualizacaoTrabalhista(CriterioAtualizacao criterio, DateOnly dataCalculo, DateOnly? ajuizamento, SeriesMensais series)
    {
        _criterio = criterio;
        _fim = (dataCalculo.Year, dataCalculo.Month);
        _ajuizamento = ajuizamento is { } a ? (a.Year, a.Month) : null;
        _series = series;
    }

    public static string Descricao(CriterioAtualizacao criterio, DateOnly? ajuizamento) => criterio switch
    {
        CriterioAtualizacao.Adc58 => ajuizamento is null
            ? "ADC 58/STF: IPCA-E + juros pela TR (fase pré-judicial, sem ajuizamento informado)"
            : "ADC 58/STF e Lei 14.905/2024: IPCA-E + TR até o ajuizamento; depois Selic até ago/2024 e IPCA + taxa legal desde set/2024",
        _ => "Sem atualização (valores nominais)"
    };

    /// <summary>Atualiza um valor exigível a partir da <paramref name="exigivel"/> (mês em que passa a contar).</summary>
    public ValorAtualizado Atualizar(decimal valor, DateOnly exigivel)
    {
        if (_criterio == CriterioAtualizacao.Nenhum || valor == 0)
            return new ValorAtualizado(valor, valor, 0, 1, 0);
        var (fator, juros) = Fator((exigivel.Year, exigivel.Month));
        var corrigido = Arredondar(valor * fator);
        return new ValorAtualizado(valor, corrigido, Arredondar(corrigido * juros), fator, Math.Round(juros * 100, 4));
    }

    private (decimal Fator, decimal Juros) Fator((int Ano, int Mes) inicio)
    {
        if (_cache.TryGetValue(inicio, out var c)) return c;
        var fator = 1m;
        var juros = 0m;
        foreach (var m in Meses(inicio, _fim))
        {
            if (_ajuizamento is null || m.CompareTo(_ajuizamento.Value) < 0)
            {
                fator *= 1 + Taxa(TipoIndice.TJSP, "IPCA-E", m);
                juros += Math.Max(0, Taxa(TipoIndice.TR, "TR", m));
            }
            else if (m.CompareTo(InicioLei14905) < 0)
            {
                juros += Taxa(TipoIndice.SELIC, "Selic", m);
            }
            else
            {
                var ipca = Taxa(TipoIndice.IPCA, "IPCA", m);
                fator *= 1 + ipca;
                juros += Math.Max(0, Taxa(TipoIndice.SELIC, "Selic", m) - ipca);
            }
        }
        fator = Math.Round(Math.Max(1, fator), 6, MidpointRounding.AwayFromZero);
        juros = Math.Round(juros, 6, MidpointRounding.AwayFromZero);
        return _cache[inicio] = (fator, juros);
    }

    private decimal Taxa(TipoIndice tipo, string nome, (int, int) mes)
    {
        if (_series.Taxa(tipo, mes, out var taxa)) return taxa;
        if (!_faltantes.TryGetValue(nome, out var set)) _faltantes[nome] = set = [];
        set.Add(mes);
        return 0;
    }

    public IEnumerable<string> Avisos() => _faltantes.Select(f =>
        $"{f.Key} ainda não publicado para {string.Join(", ", f.Value.Select(m => $"{m.Item2:00}/{m.Item1}"))} — considerado 0% nesses meses.");

    internal static IEnumerable<(int Ano, int Mes)> Meses((int Ano, int Mes) inicio, (int Ano, int Mes) fimExclusivo)
    {
        var (ano, mes) = inicio;
        while ((ano, mes).CompareTo(fimExclusivo) < 0)
        {
            yield return (ano, mes);
            if (++mes > 12) { mes = 1; ano++; }
        }
    }

    internal static decimal Arredondar(decimal v) => Math.Round(v, 2, MidpointRounding.AwayFromZero);
}
