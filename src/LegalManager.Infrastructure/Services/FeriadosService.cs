using LegalManager.Domain.Enums;

namespace LegalManager.Infrastructure.Services;

public static class FeriadosService
{
    public static DateTime CalcularEaster(int year)
    {
        int a = year % 19, b = year / 100, c = year % 100,
            d = b / 4, e = b % 4, f = (b + 8) / 25,
            g = (b - f + 1) / 3,
            h = (19 * a + b - d - g + 15) % 30,
            i = c / 4, k = c % 4,
            l = (32 + 2 * e + 2 * i - h - k) % 7,
            m = (a + 11 * h + 22 * l) / 451,
            month = (h + l - 7 * m + 114) / 31,
            day = (h + l - 7 * m + 114) % 31 + 1;
        return new DateTime(year, month, day);
    }

    public static HashSet<DateTime> GetFeriadosNacionais(int year)
    {
        var easter = CalcularEaster(year);
        var feriados = new HashSet<DateTime>
        {
            new(year, 1, 1),   // Ano Novo
            new(year, 4, 21),  // Tiradentes
            new(year, 5, 1),   // Dia do Trabalho
            new(year, 9, 7),   // Independência
            new(year, 10, 12), // N. Sra. Aparecida
            new(year, 11, 2),  // Finados
            new(year, 11, 15), // Proclamação da República
            new(year, 11, 20), // Consciência Negra
            new(year, 12, 25), // Natal
            easter.AddDays(-48), // Carnaval segunda
            easter.AddDays(-47), // Carnaval terça
            easter.AddDays(-2),  // Sexta-Feira Santa
            easter.AddDays(60),  // Corpus Christi
        };
        return feriados;
    }

    public static bool IsDiaUtil(DateTime date, HashSet<DateTime>? feriados = null)
    {
        if (date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) return false;
        feriados ??= GetFeriadosNacionais(date.Year);
        return !feriados.Contains(date.Date);
    }

    public static DateTime AdicionarDiasUteis(DateTime inicio, int dias, IEnumerable<DateOnly>? feriadosAdicionais = null)
        => CalcularDataFinal(inicio, dias, TipoCalculo.DiasUteis, feriadosAdicionais, prazoProcessual: false);

    /// <summary>
    /// Recesso forense (CPC art. 220; CLT art. 775-A): de 20/12 a 20/01, inclusive, o curso dos
    /// prazos processuais fica suspenso.
    /// </summary>
    public static bool EmRecessoForense(DateTime data) =>
        (data.Month == 12 && data.Day >= 20) || (data.Month == 1 && data.Day <= 20);

    /// <summary>
    /// Data em que se considera feita a publicação no diário eletrônico: o primeiro dia útil
    /// seguinte ao da disponibilização (Lei 11.419/2006, art. 4º, § 3º). O prazo começa no dia
    /// útil seguinte a ela (§ 4º) — é o "dia do começo" que <see cref="CalcularPrazo"/> exclui.
    /// </summary>
    public static DateTime DataPublicacaoDiario(DateTime disponibilizacao, IEnumerable<DateOnly>? feriadosAdicionais = null)
    {
        var feriados = new CalendarioFeriados(feriadosAdicionais);
        var data = disponibilizacao.Date.AddDays(1);
        while (!IsDiaUtil(data, feriados.Do(data.Year))) data = data.AddDays(1);
        return data;
    }

    /// <summary>
    /// Cálculo da calculadora de prazos: usado pela tela (POST /api/prazos/calcular) e pelo
    /// "recalcular" da timeline do superadmin, para os dois darem sempre o mesmo resultado.
    /// O dia do começo é excluído e a contagem começa no dia seguinte (CPC art. 224).
    /// <paramref name="prazoProcessual"/> aplica as regras do CPC para prazos processuais: os dias
    /// do recesso forense não contam (art. 220) e, em dias corridos, a contagem começa no primeiro
    /// dia útil e o vencimento em dia sem expediente passa para o dia útil seguinte (art. 224, §§ 1º e 3º).
    /// </summary>
    public static (DateTime DataFinal, IReadOnlyList<string> Feriados) CalcularPrazo(
        DateTime dataInicio, int quantidadeDias, TipoCalculo tipo, IEnumerable<DateOnly>? feriadosAdicionais = null,
        bool prazoProcessual = false)
    {
        var extras = feriadosAdicionais?.ToList();
        var dataFinal = CalcularDataFinal(dataInicio, quantidadeDias, tipo, extras, prazoProcessual);
        var feriados = ListarFeriadosNoIntervalo(dataInicio, dataFinal, extras).ToList();
        if (prazoProcessual && TocaRecesso(dataInicio, dataFinal))
            feriados.Add("recesso forense (20/12 a 20/01)");
        return (dataFinal, feriados);
    }

    private static DateTime CalcularDataFinal(
        DateTime inicio, int dias, TipoCalculo tipo, IEnumerable<DateOnly>? feriadosAdicionais, bool prazoProcessual)
    {
        var feriados = new CalendarioFeriados(feriadosAdicionais);
        bool Util(DateTime d) => IsDiaUtil(d, feriados.Do(d.Year)) && !(prazoProcessual && EmRecessoForense(d));

        var current = inicio.Date;
        if (tipo == TipoCalculo.DiasUteis)
        {
            int added = 0;
            while (added < dias)
            {
                current = current.AddDays(1);
                if (Util(current)) added++;
            }
            return current;
        }

        if (!prazoProcessual) return current.AddDays(dias);

        // Dias corridos de prazo processual: o primeiro dia contado é o primeiro dia útil depois
        // do começo; os dias do recesso não contam; o vencimento cai sempre em dia útil.
        if (dias <= 0) return current;
        do current = current.AddDays(1); while (!Util(current));
        int contados = 1;
        while (contados < dias)
        {
            current = current.AddDays(1);
            if (!EmRecessoForense(current)) contados++;
        }
        while (!Util(current)) current = current.AddDays(1);
        return current;
    }

    private static bool TocaRecesso(DateTime inicio, DateTime fim)
    {
        for (var d = inicio.Date; d <= fim.Date; d = d.AddDays(1))
            if (EmRecessoForense(d)) return true;
        return false;
    }

    /// <summary>Feriados nacionais mais os adicionais (estaduais/municipais), por ano, com cache.</summary>
    private sealed class CalendarioFeriados(IEnumerable<DateOnly>? adicionais)
    {
        private readonly HashSet<DateTime> _extras =
            adicionais?.Select(f => f.ToDateTime(TimeOnly.MinValue).Date).ToHashSet() ?? [];
        private readonly Dictionary<int, HashSet<DateTime>> _cache = new();

        public HashSet<DateTime> Do(int ano)
        {
            if (!_cache.TryGetValue(ano, out var f))
            {
                f = GetFeriadosNacionais(ano);
                foreach (var e in _extras.Where(e => e.Year == ano)) f.Add(e);
                _cache[ano] = f;
            }
            return f;
        }
    }

    public static IReadOnlyList<string> ListarFeriadosNoIntervalo(DateTime inicio, DateTime fim, IEnumerable<DateOnly>? feriadosAdicionais = null)
    {
        var years = Enumerable.Range(inicio.Year, fim.Year - inicio.Year + 1);
        var todos = years.SelectMany(y => GetFeriadosNacionais(y)).ToHashSet();
        if (feriadosAdicionais != null)
            foreach (var f in feriadosAdicionais) todos.Add(f.ToDateTime(TimeOnly.MinValue).Date);
        return todos
            .Where(f => f >= inicio.Date && f <= fim.Date)
            .OrderBy(f => f)
            .Select(f => f.ToString("dd/MM/yyyy", BrasiliaTime.PtBr))
            .ToList();
    }
}
