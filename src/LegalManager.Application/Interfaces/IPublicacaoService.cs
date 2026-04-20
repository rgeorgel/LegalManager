using LegalManager.Application.DTOs.Publicacoes;
using LegalManager.Application.DTOs.Processos;

namespace LegalManager.Application.Interfaces;

public interface IPublicacaoService
{
    Task<IEnumerable<PublicacaoResponseDto>> GetAllAsync(PublicacaoFiltroDto filtro, CancellationToken ct = default);
    Task<PublicacaoResponseDto?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task MarcarLidaAsync(Guid id, CancellationToken ct = default);
    Task ArquivarAsync(Guid id, CancellationToken ct = default);
    Task<int> GetNaoLidasCountAsync(CancellationToken ct = default);
}
