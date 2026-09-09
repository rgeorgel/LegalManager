using LegalManager.Application.Interfaces;
using LegalManager.Domain;
using LegalManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LegalManager.Infrastructure.Services;

public class TenantDeletionService : ITenantDeletionService
{
    private readonly AppDbContext _db;
    private readonly ITenantImportService _importService;

    public TenantDeletionService(AppDbContext db, ITenantImportService importService)
    {
        _db = db;
        _importService = importService;
    }

    public async Task<TenantDeletionOutcome> DeleteTenantAsync(Guid tenantId, CancellationToken ct = default)
    {
        if (tenantId == TenantConstants.SystemTenantId)
            throw new InvalidOperationException("Não é permitido excluir o tenant Sistema.");

        var tenant = await _db.Tenants.FirstOrDefaultAsync(t => t.Id == tenantId, ct)
            ?? throw new KeyNotFoundException($"Tenant {tenantId} não encontrado.");

        var tenantNome = tenant.Nome;

        await using var transaction = await _db.Database.BeginTransactionAsync(ct);
        try
        {
            // Reaproveita a mesma lógica de wipe usada pelo import em modo Replace — ela já
            // resolve a ordem correta de FK entre as ~35 tabelas do tenant e quebra o ciclo
            // ParcelaHonorario<->LancamentoFinanceiro. Reimplementar isso aqui arriscaria
            // reintroduzir os mesmos bugs de violação de FK já corrigidos naquele fluxo.
            await _importService.WipeTenantDataAsync(tenantId, ct);

            // CreditosAI não faz parte do export/import de tenant (não é replicável entre
            // ambientes, é crédito de IA consumido de fato), então WipeTenantDataAsync não
            // o toca — mas ele tem FK Restrict para Tenants (a única tabela do schema com
            // FK direta para Tenants que fica de fora do wipe; as demais são Cascade ou já
            // cobertas pelo wipe), então precisa ser apagado aqui antes de remover o Tenant.
            var creditos = await _db.CreditosAI.Where(c => c.TenantId == tenantId).ToListAsync(ct);
            if (creditos.Count > 0) _db.CreditosAI.RemoveRange(creditos);

            _db.Tenants.Remove(tenant);
            await _db.SaveChangesAsync(ct);

            await transaction.CommitAsync(ct);
        }
        catch
        {
            await transaction.RollbackAsync(ct);
            throw;
        }

        return new TenantDeletionOutcome(tenantId, tenantNome);
    }
}
