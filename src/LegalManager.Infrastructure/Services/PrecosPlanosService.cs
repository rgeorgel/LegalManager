using LegalManager.Application.Interfaces;
using LegalManager.Domain;
using LegalManager.Domain.Enums;
using LegalManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace LegalManager.Infrastructure.Services;

public class PrecosPlanosService(AppDbContext db, IMemoryCache cache) : IPrecosPlanosService
{
    private const string ChaveCache = "precos-planos";

    // Lido em toda landing page; a expiração cobre outras instâncias da API, que não veem o Invalidar.
    private static readonly TimeSpan Duracao = TimeSpan.FromMinutes(5);

    public async Task<IReadOnlyDictionary<PlanoTipo, decimal>> ObterAsync(CancellationToken ct = default)
    {
        if (cache.TryGetValue(ChaveCache, out IReadOnlyDictionary<PlanoTipo, decimal>? precos) && precos is not null)
            return precos;

        var linhas = await db.PrecosPlanos.AsNoTracking().ToDictionaryAsync(p => p.Plano, p => p.ValorMensal, ct);
        // Plano sem linha (não deveria acontecer: a tabela nasce semeada) cai no preço padrão.
        precos = PlanoPrecos.Pagos.ToDictionary(p => p, p => linhas.GetValueOrDefault(p, PlanoPrecos.Padrao[p]));

        cache.Set(ChaveCache, precos, Duracao);
        return precos;
    }

    public void Invalidar() => cache.Remove(ChaveCache);
}
