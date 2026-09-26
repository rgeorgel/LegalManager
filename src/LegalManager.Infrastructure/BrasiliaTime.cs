using System.Globalization;

namespace LegalManager.Infrastructure;

public static class BrasiliaTime
{
    private static readonly TimeZoneInfo _tz = Resolve();

    public static TimeZoneInfo Tz => _tz;

    /// <summary>
    /// Cultura para texto exibido a pessoas (datas dd/MM/yyyy, moeda R$). Use sempre explícita:
    /// em formatos customizados a "/" e o ":" viram os separadores da cultura do servidor.
    /// Para formatos de máquina (URL, hash, chave), use <see cref="CultureInfo.InvariantCulture"/>.
    /// </summary>
    public static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    /// <summary>
    /// Converte um instante para o horário de Brasília. Unspecified (como vem do banco) é tratado
    /// como UTC — mesma regra de <see cref="DateTime.ToLocalTime"/>, mas sem depender do fuso do
    /// servidor (containers rodam em UTC).
    /// </summary>
    public static DateTime DeUtc(DateTime instante) =>
        instante.Kind == DateTimeKind.Local
            ? TimeZoneInfo.ConvertTime(instante, _tz)
            : TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(instante, DateTimeKind.Utc), _tz);

    public static DateTime Hoje =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, _tz).Date;

    public static DateTime Now =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, _tz);

    /// <summary>
    /// "Agora" em Brasília no mesmo referencial em que prazos e eventos são gravados. Em código
    /// que atende um escritório, prefira o fuso dele: FusoHorario.AgoraParede(await db.DoTenantAsync(...)).
    /// Detalhe do referencial: Os formulários
    /// (tarefas, agenda) enviam a hora de Brasília digitada sem fuso ("2026-09-28T17:00") e ela
    /// é persistida sem conversão — o valor no banco é a hora "de parede" de Brasília. Por isso,
    /// compare Tarefa.Prazo / Evento.DataHora com este valor, e não com DateTime.UtcNow (que
    /// adiantaria "atrasada" em 3h). Para carimbos gerados no servidor (CriadoEm, ConcluidaEm…),
    /// continue usando DateTime.UtcNow.
    /// </summary>
    public static DateTime AgoraParede => DateTime.SpecifyKind(Now, DateTimeKind.Utc);

    /// <summary>Início do dia de hoje (Brasília) no referencial "de parede" — ver <see cref="AgoraParede"/>.</summary>
    public static DateTime HojeParede => DateTime.SpecifyKind(Hoje, DateTimeKind.Utc);

    private static TimeZoneInfo Resolve()
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo"); }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.FindSystemTimeZoneById("E. South America Standard Time");
        }
        catch (InvalidTimeZoneException)
        {
            return TimeZoneInfo.FindSystemTimeZoneById("E. South America Standard Time");
        }
    }
}
