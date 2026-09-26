using System.Collections.Concurrent;
using LegalManager.Domain;
using LegalManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LegalManager.Infrastructure;

/// <summary>
/// "Agora" e "hoje" no fuso do escritório (Tenant.FusoHorario; padrão: Brasília), no mesmo
/// referencial "de parede" em que prazos e eventos são gravados — ver <see cref="BrasiliaTime.AgoraParede"/>.
/// </summary>
public static class FusoHorario
{
    private static readonly TimeSpan Validade = TimeSpan.FromMinutes(5);
    private static readonly ConcurrentDictionary<Guid, (TimeZoneInfo Tz, DateTime Expira)> Cache = new();

    /// <summary>TimeZoneInfo do id IANA; id ausente ou desconhecido → Brasília.</summary>
    public static TimeZoneInfo Resolver(string? id)
    {
        if (string.IsNullOrWhiteSpace(id) || id == FusosHorarios.Padrao) return BrasiliaTime.Tz;
        try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
        catch (Exception e) when (e is TimeZoneNotFoundException or InvalidTimeZoneException) { return BrasiliaTime.Tz; }
    }

    /// <summary>"Agora" no fuso dado, com Kind=Utc (referencial "de parede" das colunas de prazo/evento).</summary>
    public static DateTime AgoraParede(TimeZoneInfo tz) =>
        DateTime.SpecifyKind(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz), DateTimeKind.Utc);

    /// <summary>Início do dia de hoje no fuso dado, no referencial "de parede".</summary>
    public static DateTime HojeParede(TimeZoneInfo tz) => AgoraParede(tz).Date;

    /// <summary>
    /// Fuso do escritório, com cache de <see cref="Validade"/> por tenant. A tela de configurações
    /// chama <see cref="Invalidar"/> ao salvar; com várias instâncias da API, as demais passam a
    /// usar o novo fuso quando o cache delas expira.
    /// </summary>
    public static async Task<TimeZoneInfo> DoTenantAsync(this AppDbContext db, Guid tenantId, CancellationToken ct = default)
    {
        if (Cache.TryGetValue(tenantId, out var c) && c.Expira > DateTime.UtcNow) return c.Tz;
        var id = await db.Tenants.Where(t => t.Id == tenantId).Select(t => t.FusoHorario).FirstOrDefaultAsync(ct);
        var tz = Resolver(id);
        Cache[tenantId] = (tz, DateTime.UtcNow + Validade);
        return tz;
    }

    public static void Invalidar(Guid tenantId) => Cache.TryRemove(tenantId, out _);
}
