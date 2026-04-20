using LegalManager.Application.DTOs.Contatos;

namespace LegalManager.Application.Interfaces;

public interface IContatoService
{
    Task<ContatoResponseDto> CreateAsync(CreateContatoDto dto, CancellationToken ct = default);
    Task<ContatoResponseDto> UpdateAsync(Guid id, UpdateContatoDto dto, CancellationToken ct = default);
    Task<ContatoResponseDto?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<PagedResultDto<ContatoListItemDto>> GetAllAsync(ContatoFiltroDto filtro, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
    Task<AtendimentoResponseDto> AddAtendimentoAsync(Guid contatoId, CreateAtendimentoDto dto, CancellationToken ct = default);
    Task<IEnumerable<AtendimentoResponseDto>> GetAtendimentosAsync(Guid contatoId, CancellationToken ct = default);
}
