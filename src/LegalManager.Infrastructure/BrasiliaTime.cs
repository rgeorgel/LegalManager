namespace LegalManager.Infrastructure;

public static class BrasiliaTime
{
    private static readonly TimeZoneInfo _tz = Resolve();

    public static TimeZoneInfo Tz => _tz;

    public static DateTime Hoje =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, _tz).Date;

    public static DateTime Now =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, _tz);

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
