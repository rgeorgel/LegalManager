using System.Linq.Expressions;
using System.Reflection;
using System.Text.Json;
using LegalManager.Application.DTOs.SuperAdmin;
using LegalManager.Application.Interfaces;
using LegalManager.Domain;
using LegalManager.Domain.Entities;
using LegalManager.Domain.Enums;
using LegalManager.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Logging;

namespace LegalManager.Infrastructure.Services;

public class TenantImportService : ITenantImportService
{
    private readonly AppDbContext _db;
    private readonly TenantAnonymizer _anonymizer;
    private readonly IPasswordHasher<Usuario> _passwordHasher;
    private readonly ILogger<TenantImportService> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    public TenantImportService(
        AppDbContext db,
        TenantAnonymizer anonymizer,
        IPasswordHasher<Usuario> passwordHasher,
        ILogger<TenantImportService> logger)
    {
        _db = db;
        _anonymizer = anonymizer;
        _passwordHasher = passwordHasher;
        _logger = logger;
    }

    public Task WipeTenantDataAsync(Guid tenantId, CancellationToken ct = default) => WipeTenantAsync(tenantId, ct);

    public async Task<TenantImportOutcome> ImportAsync(
        TenantImportRequest request,
        CancellationToken ct = default)
    {
        if (request.Mode == TenantImportMode.Replace && request.TargetTenantId is null)
            throw new InvalidOperationException("TargetTenantId é obrigatório no modo Replace.");

        if (request.TargetTenantId == TenantConstants.SystemTenantId)
            throw new InvalidOperationException("Não é permitido importar para o tenant Sistema.");

        if (request.Mode == TenantImportMode.CreateNew)
        {
            if (string.IsNullOrWhiteSpace(request.NewTenantName))
                throw new InvalidOperationException("Nome do novo tenant é obrigatório no modo CreateNew.");
            if (request.NewTenantName!.Length > 200)
                throw new InvalidOperationException("Nome do novo tenant deve ter no máximo 200 caracteres.");

            var trimmedName = request.NewTenantName.Trim();
            var nameExists = await _db.Tenants
                .AnyAsync(t => t.Nome.ToLower() == trimmedName.ToLower(), ct);
            if (nameExists)
                throw new InvalidOperationException($"Já existe um tenant com o nome '{trimmedName}'.");
        }

        TenantExportEnvelope? envelope;
        try
        {
            envelope = await JsonSerializer.DeserializeAsync<TenantExportEnvelope>(request.Payload, JsonOptions, ct);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"Arquivo JSON inválido: {ex.Message}", ex);
        }

        if (envelope is null)
            throw new InvalidOperationException("Arquivo vazio ou inválido.");

        if (envelope.Version != TenantExportConstants.SchemaVersion)
            throw new InvalidOperationException(
                $"Versão do schema incompatível: arquivo é v{envelope.Version}, esperado v{TenantExportConstants.SchemaVersion}.");

        if (envelope.Tables is null || envelope.Tables.Count == 0)
            throw new InvalidOperationException("Arquivo não contém tabelas para importar.");

        var warnings = new List<string>();
        var rowsByTable = new Dictionary<string, int>(StringComparer.Ordinal);
        var totalInserted = 0;
        var usersReset = 0;

        Tenant target;

        // Pré-popula o remapeamento de Ids antes de qualquer insert. Isso garante que
        // toda coluna de FK receba o valor remapeado correto independentemente da ordem
        // de importação das tabelas — inclusive em referências circulares como
        // ParcelaHonorario.LancamentoFinanceiroId <-> LancamentoFinanceiro.ParcelaHonorarioId,
        // que de outra forma causariam violação de FK no Postgres (500) ao referenciar uma
        // linha ainda não inserida.
        var idRemap = new Dictionary<Guid, Guid>();
        PrePopulateIdRemap(envelope, idRemap);
        var committedIds = new HashSet<Guid>();
        var pendingPatches = new List<PendingFkPatch>();

        await using var transaction = await _db.Database.BeginTransactionAsync(ct);

        try
        {
            if (request.Mode == TenantImportMode.CreateNew)
            {
                target = new Tenant
                {
                    Id = Guid.NewGuid(),
                    Nome = request.NewTenantName!.Trim(),
                    Plano = PlanoTipo.Free,
                    Status = StatusTenant.Ativo,
                    CriadoEm = DateTime.UtcNow
                };
                _db.Tenants.Add(target);
                await _db.SaveChangesAsync(ct);
            }
            else
            {
                target = await _db.Tenants.FirstOrDefaultAsync(t => t.Id == request.TargetTenantId!.Value, ct)
                    ?? throw new KeyNotFoundException($"Tenant destino {request.TargetTenantId} não encontrado.");
            }

            var effectiveTargetId = target.Id;

            await WipeTenantAsync(effectiveTargetId, ct);

            foreach (var spec in TenantTableSpecs.ImportOrder)
            {
                ct.ThrowIfCancellationRequested();
                if (!envelope.Tables.TryGetValue(spec.JsonKey, out var rows)) continue;

                if (spec.TenantFilter == TenantFilterKind.GlobalTable)
                {
                    // Dados globais (ex.: índices de correção monetária) não pertencem ao
                    // tenant e nunca são apagados em WipeTenantAsync — reinseri-los aqui
                    // duplicaria linhas já existentes e violaria a unique constraint da tabela.
                    if (rows.Count > 0)
                        warnings.Add($"Tabela global '{spec.JsonKey}' não foi reimportada (dados já existem no ambiente de destino).");
                    continue;
                }

                var inserted = await InsertTenantTableAsync(spec, rows, effectiveTargetId, idRemap, committedIds, pendingPatches, ct);
                rowsByTable[spec.JsonKey] = inserted;
                totalInserted += inserted;
            }

            foreach (var identitySpec in TenantTableSpecs.IdentityJoinTables)
            {
                ct.ThrowIfCancellationRequested();
                if (!envelope.Tables.TryGetValue(identitySpec.JsonKey, out var rows)) continue;

                var inserted = await InsertIdentityJoinAsync(identitySpec, rows, idRemap, committedIds, pendingPatches, ct);
                rowsByTable[identitySpec.JsonKey] = inserted;
                totalInserted += inserted;
            }

            await ApplyPendingPatchesAsync(pendingPatches, ct);

            await transaction.CommitAsync(ct);

            _logger.LogInformation(
                "Import mode={Mode} → Tenant {TargetId} ({Nome}) recebeu dados de {SourceId}: {TotalRows} linhas em {TableCount} tabelas",
                request.Mode, effectiveTargetId, target.Nome, envelope.SourceTenantId, totalInserted, rowsByTable.Count);

            return new TenantImportOutcome(
                TargetTenantId: effectiveTargetId,
                TenantNome: target.Nome,
                Mode: request.Mode,
                TablesImported: rowsByTable.Count,
                RowsImported: totalInserted,
                UsersResetPasswords: usersReset,
                RowsByTable: rowsByTable,
                Warnings: warnings
            );
        }
        catch
        {
            await transaction.RollbackAsync(ct);
            throw;
        }
    }

    private async Task WipeTenantAsync(Guid tenantId, CancellationToken ct)
    {
        var userIds = await _db.Users.AsNoTracking()
            .Where(u => u.TenantId == tenantId)
            .Select(u => u.Id)
            .ToListAsync(ct);

        await BreakParcelaLancamentoCycleAsync(tenantId, ct);

        foreach (var jsonKey in TenantTableSpecs.TableKeysForDeletionReversed)
        {
            ct.ThrowIfCancellationRequested();

            var tenantSpec = TenantTableSpecs.ExportOrder.FirstOrDefault(s => s.JsonKey == jsonKey);
            if (tenantSpec != null)
            {
                await DeleteByTenantIdAsync(tenantSpec, tenantId, ct);
                continue;
            }

            var identitySpec = TenantTableSpecs.IdentityJoinTables.FirstOrDefault(s => s.JsonKey == jsonKey);
            if (identitySpec != null && userIds.Count > 0)
            {
                await DeleteIdentityJoinAsync(identitySpec, userIds, ct);
            }
        }
    }

    /// <summary>
    /// ParcelaHonorario.LancamentoFinanceiroId e LancamentoFinanceiro.ParcelaHonorarioId
    /// formam um ciclo de FK genuíno (ver FinanceiroService/HonorarioService, que
    /// preenchem as duas direções ao registrar um pagamento). Nenhuma ordem de DELETE
    /// resolve um ciclo — é preciso zerar um dos lados antes de apagar qualquer uma das
    /// duas tabelas, senão o Postgres bloqueia o DELETE por violação de FK independente
    /// da ordem escolhida.
    /// </summary>
    private async Task BreakParcelaLancamentoCycleAsync(Guid tenantId, CancellationToken ct)
    {
        var parcelas = await _db.ParcelasHonorarios
            .Where(p => p.TenantId == tenantId && p.LancamentoFinanceiroId != null)
            .ToListAsync(ct);
        if (parcelas.Count == 0) return;

        foreach (var parcela in parcelas) parcela.LancamentoFinanceiroId = null;
        await _db.SaveChangesAsync(ct);
    }

    private async Task DeleteByTenantIdAsync(TenantTableSpec spec, Guid tenantId, CancellationToken ct)
    {
        if (spec.TenantFilter == TenantFilterKind.GlobalTable) return;

        var dbSet = GetDbSet(spec.EntityType);

        IQueryable query;
        if (spec.TenantFilter == TenantFilterKind.ContatoIdInTenant)
        {
            var contatoIds = await _db.Contatos.AsNoTracking()
                .Where(c => c.TenantId == tenantId)
                .Select(c => c.Id)
                .ToListAsync(ct);
            query = FilterByGuidIn(dbSet, spec.EntityType, "ContatoId", contatoIds);
        }
        else if (spec.TenantFilter == TenantFilterKind.TarefaIdInTenant)
        {
            var tarefaIds = await _db.Tarefas.AsNoTracking()
                .Where(t => t.TenantId == tenantId)
                .Select(t => t.Id)
                .ToListAsync(ct);
            query = FilterByGuidIn(dbSet, spec.EntityType, "TarefaId", tarefaIds);
        }
        else if (spec.TenantFilter == TenantFilterKind.ProcessoIdInTenant)
        {
            var processoIds = await _db.Processos.AsNoTracking()
                .Where(p => p.TenantId == tenantId)
                .Select(p => p.Id)
                .ToListAsync(ct);
            query = FilterByGuidIn(dbSet, spec.EntityType, "ProcessoId", processoIds);
        }
        else
        {
            var param = Expression.Parameter(spec.EntityType, "e");
            Expression propAccess;
            if (spec.TenantFilter == TenantFilterKind.TenantId)
            {
                propAccess = Expression.Property(param, "TenantId");
            }
            else
            {
                propAccess = Expression.Convert(Expression.Property(param, spec.NullableTenantProperty!), typeof(Guid?));
            }

            var body = Expression.Equal(propAccess, spec.TenantFilter == TenantFilterKind.TenantId
                ? (Expression)Expression.Constant(tenantId)
                : Expression.Constant((Guid?)tenantId, typeof(Guid?)));
            var lambda = Expression.Lambda(body, param);
            query = QueryableWhere(dbSet, lambda);
        }

        var rows = await CastToObjectAsync(query).ToListAsync(ct);

        if (rows.Count == 0) return;
        await RemoveRangeAsync(spec.EntityType, rows, ct);
    }

    private async Task DeleteIdentityJoinAsync(IdentityJoinSpec spec, List<Guid> userIds, CancellationToken ct)
    {
        if (userIds.Count == 0) return;

        var dbSet = GetDbSet(spec.EntityType);
        var param = Expression.Parameter(spec.EntityType, "e");
        var userIdProp = Expression.Property(param, spec.UserIdProperty);
        var containsMethod = typeof(Enumerable).GetMethods()
            .First(m => m.Name == nameof(Enumerable.Contains) && m.GetParameters().Length == 2)
            .MakeGenericMethod(typeof(Guid));
        var body = Expression.Call(containsMethod, Expression.Constant(userIds), userIdProp);
        var lambda = Expression.Lambda(body, param);
        var query = QueryableWhere(dbSet, lambda);

        var rows = await CastToObjectAsync(query).ToListAsync(ct);

        if (rows.Count == 0) return;
        await RemoveRangeAsync(spec.EntityType, rows, ct);
    }

    private static IQueryable<object> CastToObjectAsync(IQueryable source)
    {
        var cast = typeof(Queryable).GetMethods()
            .First(m => m.Name == nameof(Queryable.Cast) && m.IsGenericMethodDefinition && m.GetParameters().Length == 1)
            .MakeGenericMethod(typeof(object));
        return (IQueryable<object>)cast.Invoke(null, new object[] { source })!;
    }

    private async Task RemoveRangeAsync(Type entityType, List<object> entities, CancellationToken ct)
    {
        var dbSet = GetDbSet(entityType);
        var removeMethod = typeof(DbSet<>).MakeGenericType(entityType).GetMethods()
            .First(m => m.Name == "Remove" && m.GetParameters().Length == 1 && !m.IsGenericMethodDefinition);
        foreach (var e in entities)
        {
            removeMethod.Invoke(dbSet, new[] { e });
        }
        await _db.SaveChangesAsync(ct);
    }

    private IQueryable GetDbSet(Type entityType)
    {
        var nonGeneric = typeof(DbContext).GetMethod("Set", new[] { typeof(Type) });
        if (nonGeneric != null)
            return (IQueryable)nonGeneric.Invoke(_db, new object[] { entityType })!;

        var setSource = _db.GetService<Microsoft.EntityFrameworkCore.Internal.IDbSetSource>();
        return (IQueryable)setSource.Create(_db, entityType);
    }

    private async Task<int> InsertTenantTableAsync(
        TenantTableSpec spec,
        List<Dictionary<string, object?>> rows,
        Guid targetTenantId,
        Dictionary<Guid, Guid> idRemap,
        HashSet<Guid> committedIds,
        List<PendingFkPatch> pendingPatches,
        CancellationToken ct)
    {
        if (rows.Count == 0) return 0;

        var entities = new List<object>(rows.Count);
        var originalIds = new List<Guid>(rows.Count);
        foreach (var row in rows)
        {
            var entity = DictToEntity(row, spec.EntityType, targetTenantId, spec, idRemap, committedIds, pendingPatches);
            if (entity is null) continue;
            _anonymizer.AnonymizeInPlace(entity);
            entities.Add(entity);
            if (TryGetRowId(row, out var originalId)) originalIds.Add(originalId);
        }

        if (entities.Count == 0) return 0;

        await AddRangeAsync(spec.EntityType, entities, ct);

        // Só depois que as linhas desta tabela foram efetivamente persistidas é que outras
        // linhas podem referenciá-las por FK sem violar a constraint no Postgres.
        foreach (var id in originalIds) committedIds.Add(id);

        return entities.Count;
    }

    private async Task<int> InsertIdentityJoinAsync(
        IdentityJoinSpec spec,
        List<Dictionary<string, object?>> rows,
        Dictionary<Guid, Guid> idRemap,
        HashSet<Guid> committedIds,
        List<PendingFkPatch> pendingPatches,
        CancellationToken ct)
    {
        if (rows.Count == 0) return 0;

        // RoleId não é estável entre ambientes (cada ambiente gera seu próprio Guid ao
        // fazer seed das roles). O export anota o nome da role em "RoleName" — resolvemos
        // aqui para o Id real da role no ambiente de destino, em vez de reusar o Id de
        // origem, que não existe na tabela AspNetRoles daqui e violaria a FK.
        Dictionary<string, Guid>? roleIdByName = null;
        if (spec.JsonKey == "AspNetUserRoles")
        {
            roleIdByName = await _db.Roles.AsNoTracking()
                .Where(r => r.Name != null)
                .ToDictionaryAsync(r => r.Name!, r => r.Id, StringComparer.OrdinalIgnoreCase, ct);
        }

        var entities = new List<object>(rows.Count);
        foreach (var row in rows)
        {
            if (roleIdByName is not null)
            {
                var roleName = row.TryGetValue("RoleName", out var rn) ? rn?.ToString()
                    : row.TryGetValue("roleName", out rn) ? rn?.ToString() : null;
                if (roleName is null || !roleIdByName.TryGetValue(roleName, out var targetRoleId))
                {
                    // role sem correspondente por nome no ambiente de destino — não há
                    // Id seguro para usar, então a atribuição desta role é ignorada.
                    continue;
                }
                row["RoleId"] = targetRoleId;
            }

            var entity = DictToEntity(row, spec.EntityType, targetTenantId: Guid.Empty, spec: null, idRemap, committedIds, pendingPatches);
            if (entity is null) continue;
            entities.Add(entity);
        }

        if (entities.Count == 0) return 0;

        await AddRangeAsync(spec.EntityType, entities, ct);
        return entities.Count;
    }

    /// <summary>
    /// Aplica, após todas as tabelas terem sido inseridas, as colunas de FK que tiveram de
    /// ser adiadas (referenciavam uma linha ainda não persistida no momento do insert).
    /// </summary>
    private async Task ApplyPendingPatchesAsync(List<PendingFkPatch> patches, CancellationToken ct)
    {
        if (patches.Count == 0) return;

        foreach (var group in patches.GroupBy(p => p.EntityType))
        {
            var entityType = group.Key;
            var idProp = entityType.GetProperty("Id");
            if (idProp is null) continue;

            var ids = group.Select(p => p.EntityId).Distinct().ToList();
            var dbSet = GetDbSet(entityType);
            var query = FilterByGuidIn(dbSet, entityType, "Id", ids);
            var entities = await CastToObjectAsync(query).ToListAsync(ct);

            var byId = entities.ToDictionary(e => (Guid)idProp.GetValue(e)!);
            foreach (var patch in group)
            {
                if (!byId.TryGetValue(patch.EntityId, out var entity)) continue;
                var prop = entityType.GetProperty(patch.PropertyName);
                try { prop?.SetValue(entity, patch.TargetId); } catch { /* skip */ }
            }
        }

        await _db.SaveChangesAsync(ct);
    }

    private sealed record PendingFkPatch(Type EntityType, Guid EntityId, string PropertyName, Guid TargetId);

    private static void PrePopulateIdRemap(TenantExportEnvelope envelope, Dictionary<Guid, Guid> idRemap)
    {
        foreach (var spec in TenantTableSpecs.ImportOrder)
        {
            if (spec.TenantFilter == TenantFilterKind.GlobalTable) continue;
            if (!envelope.Tables.TryGetValue(spec.JsonKey, out var rows)) continue;
            foreach (var row in rows)
            {
                if (!TryGetRowId(row, out var originalId)) continue;
                if (!idRemap.ContainsKey(originalId)) idRemap[originalId] = Guid.NewGuid();
            }
        }
    }

    private static bool TryGetRowId(Dictionary<string, object?> row, out Guid id)
    {
        id = Guid.Empty;
        if ((row.TryGetValue("Id", out var raw) || row.TryGetValue("id", out raw)) && raw != null)
        {
            return Guid.TryParse(raw.ToString(), out id);
        }
        return false;
    }

    private async Task AddRangeAsync(Type entityType, List<object> entities, CancellationToken ct)
    {
        var dbSet = GetDbSet(entityType);
        var addMethod = typeof(DbSet<>).MakeGenericType(entityType).GetMethods()
            .First(m => m.Name == "Add" && m.GetParameters().Length == 1 && !m.IsGenericMethodDefinition);
        foreach (var e in entities)
        {
            addMethod.Invoke(dbSet, new[] { e });
        }
        await _db.SaveChangesAsync(ct);
    }

    private object? DictToEntity(
        Dictionary<string, object?> row,
        Type entityType,
        Guid targetTenantId,
        TenantTableSpec? spec,
        Dictionary<Guid, Guid> idRemap,
        HashSet<Guid> committedIds,
        List<PendingFkPatch> pendingPatches)
    {
        var entity = Activator.CreateInstance(entityType);
        if (entity is null) return null;

        Guid? ownEntityId = null;
        if (TryGetRowId(row, out var ownOriginalId))
        {
            ownEntityId = idRemap.TryGetValue(ownOriginalId, out var mappedOwn) ? mappedOwn : ownOriginalId;
        }

        var props = entityType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanWrite && p.GetIndexParameters().Length == 0)
            .ToArray();

        foreach (var prop in props)
        {
            if (prop.Name is "PasswordHash" or "SecurityStamp" or "ConcurrencyStamp"
                or "SenhaHash" or "TokenConvite" or "TokenRedefinicao" or "Token")
            {
                continue;
            }

            object? value = null;
            bool found = row.TryGetValue(prop.Name, out var raw);
            if (!found)
            {
                var pascal = prop.Name;
                var camel = char.ToLowerInvariant(pascal[0]) + pascal[1..];
                if (row.TryGetValue(camel, out raw)) found = true;
            }
            if (!found) continue;

            try { value = ConvertValue(raw, prop.PropertyType); }
            catch { continue; }

            if (value is Guid guidVal)
            {
                if (prop.Name == "Id")
                {
                    if (!idRemap.ContainsKey(guidVal))
                        idRemap[guidVal] = Guid.NewGuid();
                    value = idRemap[guidVal];
                }
                else if (IsForeignKeyProperty(prop) && idRemap.TryGetValue(guidVal, out var remapped))
                {
                    var isNullableGuid = Nullable.GetUnderlyingType(prop.PropertyType) != null;
                    if (!committedIds.Contains(guidVal) && isNullableGuid && ownEntityId.HasValue)
                    {
                        // A linha referenciada ainda não foi persistida (referência para
                        // frente, ou ciclo entre tabelas). Adia o preenchimento desta FK
                        // para depois de todas as tabelas serem inseridas, evitando uma
                        // violação de FK no Postgres.
                        value = null;
                        pendingPatches.Add(new PendingFkPatch(entityType, ownEntityId.Value, prop.Name, remapped));
                    }
                    else
                    {
                        value = remapped;
                    }
                }
            }

            if (prop.Name == "TenantId" && prop.PropertyType == typeof(Guid) && value is null && targetTenantId != Guid.Empty)
            {
                value = targetTenantId;
            }

            try { prop.SetValue(entity, value); }
            catch { /* skip FK types we cannot satisfy */ }

            if (prop.Name == "TenantId" && prop.PropertyType == typeof(Guid) && targetTenantId != Guid.Empty)
            {
                try { prop.SetValue(entity, targetTenantId); }
                catch { /* skip */ }
            }
        }

        if (entity is Usuario usuario)
        {
            usuario.PasswordHash = _passwordHasher.HashPassword(usuario, TenantExportConstants.DefaultReplicaPassword);
            usuario.SecurityStamp = Guid.NewGuid().ToString();
            usuario.ConcurrencyStamp = Guid.NewGuid().ToString();
            usuario.EmailConfirmed = false;
            usuario.PhoneNumberConfirmed = false;
            usuario.TwoFactorEnabled = false;
            usuario.LockoutEnabled = true;
            usuario.AccessFailedCount = 0;
        }

        if (entity is Notificacao notificacao)
        {
            // ChaveDedup tem uma unique index GLOBAL (não escopada por TenantId — ver
            // NotificacaoConfiguration) e embute o Id original (pré-remapeamento) do
            // usuário/entidade de origem (ex.: "digest-tarefas-{usuarioId}-{data}").
            // Reimportá-la como veio pode colidir com uma chave já existente no ambiente
            // de destino (violação de unique constraint) e, mesmo sem colisão, faria o
            // AlertasJob do destino tratar alertas futuros legítimos como já enviados.
            notificacao.ChaveDedup = null;
        }

        return entity;
    }

    private static bool IsForeignKeyProperty(PropertyInfo prop)
    {
        if (!prop.Name.EndsWith("Id", StringComparison.Ordinal)) return false;
        if (prop.Name == "Id") return false;
        var t = Nullable.GetUnderlyingType(prop.PropertyType) ?? prop.PropertyType;
        return t == typeof(Guid) || t == typeof(int) || t == typeof(long);
    }

    private static object? ConvertValue(object? raw, Type targetType)
    {
        if (raw is null) return null;

        // O import desserializa "Dictionary<string, object?>" a partir do JSON, então todo
        // valor chega como JsonElement — não como int/decimal/bool/DateTime nativos.
        // JsonElement não implementa IConvertible, então Convert.ToXxx/ChangeType (usados
        // abaixo) lançam exceção silenciosamente engolida pelo catch em DictToEntity,
        // deixando a propriedade com o valor default. Isso mascarava status (enum),
        // datas e valores decimais inteiros após o import — o Guid só "funcionava" por
        // acidente, pois Guid.Parse(raw.ToString()) tolera o ToString() de um JsonElement
        // string. Extraímos o valor primitivo do JsonElement explicitamente aqui.
        if (raw is JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.Null) return null;

            var underlying = Nullable.GetUnderlyingType(targetType) ?? targetType;

            if (underlying == typeof(string)) return element.ValueKind == JsonValueKind.String ? element.GetString() : element.GetRawText();
            if (underlying.IsEnum)
            {
                if (element.ValueKind == JsonValueKind.String)
                {
                    var s = element.GetString();
                    if (s != null && Enum.TryParse(underlying, s, true, out var enumVal)) return enumVal;
                }
                return Enum.ToObject(underlying, element.GetInt32());
            }
            if (underlying == typeof(Guid)) return Guid.Parse(element.GetString()!);
            if (underlying == typeof(DateTime))
            {
                var dt = element.GetDateTime();
                return dt.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(dt, DateTimeKind.Utc) : dt;
            }
            if (underlying == typeof(DateTimeOffset)) return element.GetDateTimeOffset();
            if (underlying == typeof(decimal)) return element.GetDecimal();
            if (underlying == typeof(bool)) return element.ValueKind == JsonValueKind.True || (element.ValueKind != JsonValueKind.False && element.GetBoolean());
            if (underlying == typeof(int)) return element.GetInt32();
            if (underlying == typeof(long)) return element.GetInt64();
            if (underlying == typeof(double)) return element.GetDouble();
            if (underlying == typeof(float)) return element.GetSingle();
            if (underlying == typeof(short)) return element.GetInt16();
            if (underlying == typeof(byte)) return element.GetByte();

            // Tipo não mapeado explicitamente: cai para o texto bruto e deixa o restante
            // do método (abaixo) tentar Convert.ChangeType normalmente.
            raw = element.ValueKind switch
            {
                JsonValueKind.String => element.GetString(),
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Number => element.GetRawText(),
                _ => element.GetRawText(),
            };
            if (raw is null) return null;
        }

        if (targetType == raw.GetType()) return raw;

        if (targetType == typeof(string)) return raw.ToString();

        if (targetType.IsEnum)
        {
            if (raw is string s && Enum.TryParse(targetType, s, true, out var enumVal))
                return enumVal;
            return Enum.ToObject(targetType, Convert.ToInt32(raw));
        }

        if (targetType == typeof(Guid) || targetType == typeof(Guid?))
        {
            return Guid.Parse(raw.ToString()!);
        }

        if (targetType == typeof(DateTime) || targetType == typeof(DateTime?))
        {
            var dt = Convert.ToDateTime(raw);
            return dt.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(dt, DateTimeKind.Utc) : dt;
        }

        if (targetType == typeof(decimal) || targetType == typeof(decimal?))
            return Convert.ToDecimal(raw, System.Globalization.CultureInfo.InvariantCulture);

        if (targetType == typeof(bool) || targetType == typeof(bool?))
            return Convert.ToBoolean(raw);

        return Convert.ChangeType(raw, targetType);
    }

    private static IQueryable QueryableWhere(IQueryable dbSet, LambdaExpression lambda)
    {
        var where = typeof(Queryable).GetMethods()
            .First(m => m.Name == nameof(Queryable.Where) && m.GetParameters().Length == 2)
            .MakeGenericMethod(dbSet.ElementType);
        return (IQueryable)where.Invoke(null, new object[] { dbSet, lambda })!;
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
}
