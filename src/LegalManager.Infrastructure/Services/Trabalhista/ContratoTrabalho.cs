namespace LegalManager.Infrastructure.Services.Trabalhista;

/// <summary>Contagens de contrato de trabalho: anos completos, avos (fração ≥ 15 dias conta um mês) e aviso prévio.</summary>
public static class ContratoTrabalho
{
    public static int AnosCompletos(DateOnly inicio, DateOnly fim)
    {
        var anos = fim.Year - inicio.Year;
        if (Aniversario(inicio, inicio.Year + anos) > fim) anos--;
        return Math.Max(0, anos);
    }

    /// <summary>Aniversário do contrato no ano informado (29/02 vira 28/02 em ano não bissexto).</summary>
    public static DateOnly Aniversario(DateOnly inicio, int ano) =>
        new(ano, inicio.Month, Math.Min(inicio.Day, DateTime.DaysInMonth(ano, inicio.Month)));

    /// <summary>Aviso prévio proporcional (Lei 12.506/2011): 30 dias + 3 por ano completo, até 90.</summary>
    public static int DiasAvisoProporcional(int anosCompletos) => Math.Min(90, 30 + 3 * anosCompletos);

    /// <summary>Dias do mês dentro de [inicio, fim].</summary>
    public static int DiasTrabalhadosNoMes(int ano, int mes, DateOnly inicio, DateOnly fim)
    {
        var primeiro = new DateOnly(ano, mes, 1);
        var ultimo = primeiro.AddMonths(1).AddDays(-1);
        var de = inicio > primeiro ? inicio : primeiro;
        var ate = fim < ultimo ? fim : ultimo;
        return ate < de ? 0 : ate.DayNumber - de.DayNumber + 1;
    }

    /// <summary>Avos de 13º do ano: meses do ano com 15 dias ou mais trabalhados (Lei 4.090/62, art. 1º, §2º).</summary>
    public static int Avos13(int ano, DateOnly inicio, DateOnly fim)
    {
        var avos = 0;
        for (var mes = 1; mes <= 12; mes++)
            if (DiasTrabalhadosNoMes(ano, mes, inicio, fim) >= 15) avos++;
        return avos;
    }

    /// <summary>
    /// Avos de férias proporcionais do período aquisitivo em curso em <paramref name="fim"/>: meses completos
    /// desde o último aniversário do contrato, mais um se a fração restante tiver 15 dias ou mais (art. 146 da CLT).
    /// </summary>
    public static int AvosFerias(DateOnly admissao, DateOnly fim)
    {
        var inicioPeriodo = Aniversario(admissao, admissao.Year + AnosCompletos(admissao, fim));
        var avos = 0;
        var cursor = inicioPeriodo;
        while (cursor.AddMonths(1).AddDays(-1) <= fim && avos < 12)
        {
            avos++;
            cursor = cursor.AddMonths(1);
        }
        if (avos < 12 && fim.DayNumber - cursor.DayNumber + 1 >= 15) avos++;
        return avos;
    }

    /// <summary>
    /// Domingos e feriados nacionais (repousos) e demais dias (úteis, sábado incluso) do mês — base do DSR.
    /// Carnaval e Corpus Christi são pontos facultativos (não entram); Consciência Negra é feriado nacional desde 2024.
    /// </summary>
    public static (int Uteis, int Repousos) DiasDsr(int ano, int mes)
    {
        var pascoa = FeriadosService.CalcularEaster(ano);
        var feriados = FeriadosService.GetFeriadosNacionais(ano);
        feriados.ExceptWith([pascoa.AddDays(-48), pascoa.AddDays(-47), pascoa.AddDays(60)]);
        if (ano < 2024) feriados.Remove(new DateTime(ano, 11, 20));
        var dias = DateTime.DaysInMonth(ano, mes);
        var repousos = 0;
        for (var d = 1; d <= dias; d++)
        {
            var data = new DateTime(ano, mes, d);
            if (data.DayOfWeek == DayOfWeek.Sunday || feriados.Contains(data.Date)) repousos++;
        }
        return (dias - repousos, repousos);
    }
}
