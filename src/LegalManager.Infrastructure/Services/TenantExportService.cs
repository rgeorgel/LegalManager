using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using System.Text.Json;
using LegalManager.Application.DTOs.SuperAdmin;
using LegalManager.Application.Interfaces;
using LegalManager.Domain;
using LegalManager.Domain.Entities;
using LegalManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Logging;

namespace LegalManager.Infrastructure.Services;

public class TenantExportService : ITenantExportService
{
    private readonly AppDbContext _db;
    private readonly TenantAnonymizer _anonymizer;
    private readonly ILogger<TenantExportService> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.Never,
    };

    public TenantExportService(
        AppDbContext db,
        TenantAnonymizer anonymizer,
        ILogger<TenantExportService> logger)
    {
        _db = db;
        _anonymizer = anonymizer;
        _logger = logger;
    }

    public async Task<TenantExportResult> ExportAsync(Guid sourceTenantId, CancellationToken ct = default)
    {
        if (sourceTenantId == TenantConstants.SystemTenantId)
            throw new InvalidOperationException("Não é permitido exportar o tenant Sistema.");

        var tenant = await _db.Tenants.AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == sourceTenantId, ct)
            ?? throw new KeyNotFoundException($"Tenant {sourceTenantId} não encontrado.");

        var userIds = await _db.Users.AsNoTracking()
            .Where(u => u.TenantId == sourceTenantId)
            .Select(u => u.Id)
            .ToListAsync(ct);

        var tables = new Dictionary<string, List<Dictionary<string, object?>>>(StringComparer.Ordinal);
        var rowsByTable = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var spec in TenantTableSpecs.ExportOrder)
        {
            ct.ThrowIfCancellationRequested();
            var rows = await LoadTableAsync(spec, sourceTenantId, userIds, ct);
            tables[spec.JsonKey] = rows;
            rowsByTable[spec.JsonKey] = rows.Count;
        }

        foreach (var identitySpec in TenantTableSpecs.IdentityJoinTables)
        {
            ct.ThrowIfCancellationRequested();
            var rows = await LoadIdentityJoinAsync(identitySpec, userIds, ct);
            tables[identitySpec.JsonKey] = rows;
            rowsByTable[identitySpec.JsonKey] = rows.Count;
        }

        var envelope = new TenantExportEnvelope(
            Version: TenantExportConstants.SchemaVersion,
            ExportedAt: DateTime.UtcNow,
            SourceTenantId: sourceTenantId,
            SourceTenantNome: tenant.Nome,
            AppVersion: "1.0.0",
            Anonymization: new TenantExportAnonymizationMetadata(
                EmailsReplacedWith: $"*@{TenantExportConstants.ReplicaEmailDomain}",
                PhonesReplacedWith: "+551190000XXXX",
                PasswordsResetTo: TenantExportConstants.DefaultReplicaPassword,
                FieldsScanned: new List<string>
                {
                    "Email", "NormalizedEmail", "EmailSecundario",
                    "UserName", "NormalizedUserName",
                    "Telefone", "Celular", "WhatsApp", "Phone"
                }),
            Tables: tables
        );

        var json = JsonSerializer.Serialize(envelope, JsonOptions);
        var bytes = Encoding.UTF8.GetBytes(json);

        var totalRows = rowsByTable.Values.Sum();
        var fileName = $"tenant-{Slug(tenant.Nome)}-{DateTime.UtcNow:yyyyMMddHHmmss}.json";

        _logger.LogInformation(
            "Tenant {TenantId} ({Nome}) exportado: {TotalRows} linhas em {TableCount} tabelas",
            sourceTenantId, tenant.Nome, totalRows, rowsByTable.Count);

        return new TenantExportResult(
            FileName: fileName,
            ContentType: "application/json",
            Payload: bytes,
            TotalRows: totalRows,
            RowsByTable: rowsByTable
        );
    }

    private async Task<List<Dictionary<string, object?>>> LoadTableAsync(
        TenantTableSpec spec,
        Guid tenantId,
        List<Guid> userIds,
        CancellationToken ct)
    {
        var dbSet = GetDbSetTyped(spec.EntityType);
        var asNoTracking = typeof(Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions)
            .GetMethods()
            .First(m => m.Name == "AsNoTracking" && m.GetParameters().Length == 1 && m.IsGenericMethodDefinition)
            .MakeGenericMethod(spec.EntityType);
        var noTracking = (IQueryable)asNoTracking.Invoke(null, new object[] { dbSet })!;

        IQueryable<object> filtered;
        if (spec.TenantFilter == TenantFilterKind.ContatoIdInTenant)
        {
            var contatoIds = await _db.Contatos.AsNoTracking()
                .Where(c => c.TenantId == tenantId)
                .Select(c => c.Id)
                .ToListAsync(ct);
            filtered = FilterByGuidIn(noTracking, spec.EntityType, "ContatoId", contatoIds).Cast<object>();
        }
        else if (spec.TenantFilter == TenantFilterKind.TarefaIdInTenant)
        {
            var tarefaIds = await _db.Tarefas.AsNoTracking()
                .Where(t => t.TenantId == tenantId)
                .Select(t => t.Id)
                .ToListAsync(ct);
            filtered = FilterByGuidIn(noTracking, spec.EntityType, "TarefaId", tarefaIds).Cast<object>();
        }
        else if (spec.TenantFilter == TenantFilterKind.ProcessoIdInTenant)
        {
            var processoIds = await _db.Processos.AsNoTracking()
                .Where(p => p.TenantId == tenantId)
                .Select(p => p.Id)
                .ToListAsync(ct);
            filtered = FilterByGuidIn(noTracking, spec.EntityType, "ProcessoId", processoIds).Cast<object>();
        }
        else
        {
            filtered = spec.TenantFilter switch
            {
                TenantFilterKind.TenantId => FilterByTenantId(noTracking, spec.EntityType, tenantId).Cast<object>(),
                TenantFilterKind.NullableTenantId => FilterByNullableTenantId(noTracking, spec.EntityType, spec.NullableTenantProperty!, tenantId).Cast<object>(),
                TenantFilterKind.GlobalTable => noTracking.Cast<object>(),
                _ => throw new InvalidOperationException($"Unsupported filter kind for {spec.JsonKey}")
            };
        }

        var rawRows = await filtered.ToListAsync(ct);

        var rows = new List<Dictionary<string, object?>>(rawRows.Count);
        foreach (var row in rawRows)
        {
            _anonymizer.AnonymizeInPlace(row);
            rows.Add(EntityToDict(row));
        }
        return rows;
    }

    private async Task<List<Dictionary<string, object?>>> LoadIdentityJoinAsync(
        IdentityJoinSpec spec,
        List<Guid> userIds,
        CancellationToken ct)
    {
        if (userIds.Count == 0) return new List<Dictionary<string, object?>>();

        var dbSet = GetDbSetTyped(spec.EntityType);
        var asNoTracking = typeof(Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions)
            .GetMethods()
            .First(m => m.Name == "AsNoTracking" && m.GetParameters().Length == 1 && m.IsGenericMethodDefinition)
            .MakeGenericMethod(spec.EntityType);
        var noTracking = (IQueryable)asNoTracking.Invoke(null, new object[] { dbSet })!;

        var param = Expression.Parameter(spec.EntityType, "e");
        var userIdProp = Expression.Property(param, spec.UserIdProperty);
        var containsMethod = typeof(Enumerable).GetMethods()
            .First(m => m.Name == nameof(Enumerable.Contains) && m.GetParameters().Length == 2)
            .MakeGenericMethod(typeof(Guid));
        var containsExpr = Expression.Call(containsMethod, Expression.Constant(userIds), userIdProp);
        var lambda = Expression.Lambda(containsExpr, param);
        var query = QueryableWhere(noTracking, lambda);

        var rawRows = await CastToObject(query).ToListAsync(ct);

        var rows = new List<Dictionary<string, object?>>(rawRows.Count);
        foreach (var row in rawRows)
        {
            _anonymizer.AnonymizeInPlace(row);
            rows.Add(EntityToDict(row));
        }
        return rows;
    }

    private object GetDbSetTyped(Type entityType)
    {
        var setMethod = typeof(DbContext).GetMethods()
            .First(m => m.Name == nameof(DbContext.Set)
                && m.GetParameters().Length == 0
                && m.IsGenericMethodDefinition);
        var generic = setMethod.MakeGenericMethod(entityType);
        return generic.Invoke(_db, null)!;
    }

    private IQueryable GetDbSet(Type entityType)
    {
        var nonGeneric = typeof(DbContext).GetMethod("Set", new[] { typeof(Type) });
        if (nonGeneric != null)
            return (IQueryable)nonGeneric.Invoke(_db, new object[] { entityType })!;

        var setSource = _db.GetService<Microsoft.EntityFrameworkCore.Internal.IDbSetSource>();
        return (IQueryable)setSource.Create(_db, entityType);
    }

    private static IQueryable FilterByTenantId(IQueryable dbSet, Type entityType, Guid tenantId)
    {
        var param = Expression.Parameter(entityType, "e");
        var prop = Expression.Property(param, "TenantId");
        var body = Expression.Equal(prop, Expression.Constant(tenantId));
        var lambda = Expression.Lambda(body, param);
        return QueryableWhere(dbSet, lambda);
    }

    private static IQueryable FilterByGuidIn(IQueryable dbSet, Type entityType, string propertyName, List<Guid> ids)
    {
        if (ids.Count == 0)
        {
            var emptyParam = Expression.Parameter(entityType, "e");
            var falseConst = Expression.Constant(false);
            var emptyLambda = Expression.Lambda(falseConst, emptyParam);
            return QueryableWhere(dbSet, emptyLambda);
        }
        var param = Expression.Parameter(entityType, "e");
        var prop = Expression.Property(param, propertyName);
        var containsMethod = typeof(Enumerable).GetMethods()
            .First(m => m.Name == nameof(Enumerable.Contains) && m.GetParameters().Length == 2)
            .MakeGenericMethod(typeof(Guid));
        var body = Expression.Call(containsMethod, Expression.Constant(ids), prop);
        var lambda = Expression.Lambda(body, param);
        return QueryableWhere(dbSet, lambda);
    }

    private static IQueryable FilterByNullableTenantId(IQueryable dbSet, Type entityType, string propertyName, Guid tenantId)
    {
        var param = Expression.Parameter(entityType, "e");
        var prop = Expression.Property(param, propertyName);
        var converted = Expression.Convert(prop, typeof(Guid?));
        var body = Expression.Equal(converted, Expression.Constant((Guid?)tenantId, typeof(Guid?)));
        var lambda = Expression.Lambda(body, param);
        return QueryableWhere(dbSet, lambda);
    }

    private static IQueryable QueryableWhere(IQueryable dbSet, LambdaExpression lambda)
    {
        var where = typeof(Queryable).GetMethods()
            .First(m => m.Name == nameof(Queryable.Where) && m.GetParameters().Length == 2)
            .MakeGenericMethod(dbSet.ElementType);
        return (IQueryable)where.Invoke(null, new object[] { dbSet, lambda })!;
    }

    private static IQueryable<object> CastToObject(IQueryable source)
    {
        var cast = typeof(Queryable).GetMethods()
            .First(m => m.Name == nameof(Queryable.Cast) && m.IsGenericMethodDefinition && m.GetParameters().Length == 1)
            .MakeGenericMethod(typeof(object));
        return (IQueryable<object>)cast.Invoke(null, new object[] { source })!;
    }

    private static Dictionary<string, object?> EntityToDict(object entity)
    {
        var dict = new Dictionary<string, object?>(StringComparer.Ordinal);
        var type = entity.GetType();

        foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (prop.GetIndexParameters().Length > 0) continue;
            if (IsSensitive(prop.Name)) continue;

            object? value;
            try { value = prop.GetValue(entity); }
            catch { continue; }

            if (value is null) { dict[prop.Name] = null; continue; }

            if (IsNavigationCollection(value, prop))
            {
                var list = (System.Collections.IEnumerable)value;
                var arr = new List<object?>();
                foreach (var item in list) arr.Add(NormalizeValue(item));
                dict[prop.Name] = arr;
                continue;
            }

            if (IsNavigation(value, prop)) continue;

            dict[prop.Name] = NormalizeValue(value);
        }

        return dict;
    }

    private static bool IsSensitive(string propName) =>
        propName is "PasswordHash" or "SecurityStamp" or "ConcurrencyStamp"
            or "SenhaHash" or "TokenConvite" or "TokenRedefinicao"
            or "Token";

    private static bool IsNavigationCollection(object value, PropertyInfo prop)
    {
        var t = prop.PropertyType;
        if (!t.IsGenericType) return false;
        var gen = t.GetGenericTypeDefinition();
        return gen == typeof(ICollection<>) || gen == typeof(List<>);
    }

    private static bool IsNavigation(object value, PropertyInfo prop) =>
        value is Tenant or Usuario;

    private static object? NormalizeValue(object value) => value switch
    {
        DateTime dt => dt.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(dt, DateTimeKind.Utc) : dt,
        Enum e => e.ToString(),
        _ => value
    };

    private static string Slug(string s)
    {
        var lowered = s.ToLowerInvariant();
        var sb = new StringBuilder(lowered.Length);
        foreach (var c in lowered)
        {
            if (char.IsLetterOrDigit(c)) sb.Append(c);
            else if (c == ' ' || c == '-' || c == '_') sb.Append('-');
        }
        var slug = sb.ToString().Trim('-');
        return slug.Length > 40 ? slug[..40] : slug;
    }
}
