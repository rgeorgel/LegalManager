using LegalManager.Application.DTOs.Processos;

namespace LegalManager.Application.Interfaces;

/// <summary>
/// Resolve Partes retornadas por adapters de tribunal (ex: DataJud) em Contatos,
/// reaproveitando Contatos já cadastrados (por nome) ou criando novos quando necessário.
/// Extraído de OnboardingController para ser reusado também no cadastro manual de
/// processos (ver docs/features/busca-processo-cadastro-manual.md).
/// </summary>
public interface IContatoResolverService
{
    Task<List<ProcessoParteDto>> ResolverPartesDataJudAsync(
        IReadOnlyList<TribunalParte> partes, CancellationToken ct = default);
}
