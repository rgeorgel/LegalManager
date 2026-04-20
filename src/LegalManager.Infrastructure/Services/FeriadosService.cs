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

    public static DateTime AdicionarDiasUteis(DateTime inicio, int dias)
    {
        var current = inicio.Date;
        var cache = new Dictionary<int, HashSet<DateTime>>();

        HashSet<DateTime> GetFeriados(int year)
        {
            if (!cache.TryGetValue(year, out var f)) cache[year] = f = GetFeriadosNacionais(year);
            return f;
        }

        int added = 0;
        while (added < dias)
        {
            current = current.AddDays(1);
            if (IsDiaUtil(current, GetFeriados(current.Year))) added++;
        }
        return current;
    }

    public static IReadOnlyList<string> ListarFeriadosNoIntervalo(DateTime inicio, DateTime fim)
    {
        var years = Enumerable.Range(inicio.Year, fim.Year - inicio.Year + 1);
        var todos = years.SelectMany(y => GetFeriadosNacionais(y)).ToHashSet();
        return todos
            .Where(f => f >= inicio.Date && f <= fim.Date)
            .OrderBy(f => f)
            .Select(f => f.ToString("dd/MM/yyyy"))
            .ToList();
    }
}
