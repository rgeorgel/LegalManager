using LegalManager.Domain.Enums;

namespace LegalManager.Application.Interfaces;

/// <summary>Preços mensais de tabela vigentes dos planos pagos (tabela PrecosPlanos), com cache.</summary>
public interface IPrecosPlanosService
{
    /// <summary>Plus, Pro e Max → valor mensal em reais.</summary>
    Task<IReadOnlyDictionary<PlanoTipo, decimal>> ObterAsync(CancellationToken ct = default);

    /// <summary>Descarta o cache depois que o super admin altera os preços.</summary>
    void Invalidar();
}
