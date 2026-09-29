using LegalManager.Application.DTOs.Importacoes;
using LegalManager.Application.DTOs.Onboarding;

namespace LegalManager.Application.Interfaces;

/// <summary>
/// Pesquisa processos de uma OAB (Escavador paginado, DataJud e e-SAJ/TJSP) e importa um
/// processo por vez. Tenant-scoped: usa o <c>ITenantContext</c> do escopo (requisição ou job).
/// </summary>
public interface IImportadorProcessosOab
{
    /// <summary>
    /// Uma página da busca no Escavador (<paramref name="cursor"/> null = primeira). Grava o
    /// JSON bruto no cache de importação. Lança em falha, para quem pagina poder tentar de novo.
    /// </summary>
    Task<PaginaBuscaOab> BuscarPaginaEscavadorAsync(string numeroOab, string uf, string? cursor, CancellationToken ct = default);

    /// <summary>Busca completa nas fontes gratuitas: DataJud e, em SP, e-SAJ/TJSP.</summary>
    Task<List<ProcessoOabPreviewDto>> BuscarTribunaisAsync(string numeroOab, string uf, CancellationToken ct = default);

    /// <summary>Importa um processo. Não lança (exceto cancelamento): falhas viram <c>StatusItemImportacao.Erro</c>.</summary>
    Task<ResultadoImportacaoItem> ImportarAsync(ImportarProcessoItem item, CancellationToken ct = default);
}
