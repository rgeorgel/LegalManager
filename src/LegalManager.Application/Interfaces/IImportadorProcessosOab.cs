using LegalManager.Application.DTOs.Importacoes;
using LegalManager.Application.DTOs.Onboarding;

namespace LegalManager.Application.Interfaces;

/// <summary>
/// Busca processos de uma OAB (DataJud, e-SAJ/TJSP e Escavador) e importa um processo por vez.
/// Tenant-scoped: usa o <c>ITenantContext</c> do escopo (requisição ou job).
/// </summary>
public interface IImportadorProcessosOab
{
    Task<List<ProcessoOabPreviewDto>> BuscarAsync(string numeroOab, string uf, CancellationToken ct = default);

    /// <summary>Importa um processo. Não lança (exceto cancelamento): falhas viram <c>StatusItemImportacao.Erro</c>.</summary>
    Task<ResultadoImportacaoItem> ImportarAsync(ImportarProcessoItem item, CancellationToken ct = default);
}
