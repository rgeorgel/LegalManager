using LegalManager.Application.DTOs.Contatos;

namespace LegalManager.Application.Interfaces;

public interface IContatoService
{
    Task<ContatoResponseDto> CreateAsync(CreateContatoDto dto, CancellationToken ct = default);
    Task<ContatoResponseDto> UpdateAsync(Guid id, UpdateContatoDto dto, CancellationToken ct = default);
    Task<ContatoResponseDto?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<ContatoResponseDto?> GetByNomeAsync(string nome, CancellationToken ct = default);
    Task<PagedResultDto<ContatoListItemDto>> GetAllAsync(ContatoFiltroDto filtro, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);

    /// <summary>Ids de todos os contatos do filtro (sem paginação), até <paramref name="limite"/>.</summary>
    Task<IReadOnlyList<Guid>> GetIdsAsync(ContatoFiltroDto filtro, int limite, CancellationToken ct = default);
    /// <summary>Desativa (ou reativa) os contatos do escritório; devolve os ids alterados.</summary>
    Task<IReadOnlyList<Guid>> DefinirAtivoEmMassaAsync(IReadOnlyList<Guid> ids, bool ativo, CancellationToken ct = default);
    /// <summary>Acrescenta as tags aos contatos, sem duplicar as que já têm; devolve quantos mudaram.</summary>
    Task<int> AdicionarTagsEmMassaAsync(IReadOnlyList<Guid> ids, IReadOnlyList<string> tags, CancellationToken ct = default);
    Task<AtendimentoResponseDto> AddAtendimentoAsync(Guid contatoId, CreateAtendimentoDto dto, CancellationToken ct = default);
    Task<AtendimentoResponseDto> UpdateAtendimentoAsync(Guid contatoId, Guid atendimentoId, UpdateAtendimentoDto dto, CancellationToken ct = default);
    Task DeleteAtendimentoAsync(Guid contatoId, Guid atendimentoId, CancellationToken ct = default);
    Task<IEnumerable<AtendimentoResponseDto>> GetAtendimentosAsync(Guid contatoId, CancellationToken ct = default);

    Task<ContatoPerfilDto> GetPerfilAsync(Guid contatoId, CancellationToken ct = default);

    Task<ContatoVinculoResponseDto> AddVinculoAsync(Guid contatoId, CreateContatoVinculoDto dto, CancellationToken ct = default);
    Task<IEnumerable<ContatoVinculoResponseDto>> GetVinculosAsync(Guid contatoId, CancellationToken ct = default);
    Task RemoveVinculoAsync(Guid contatoId, Guid vinculoId, CancellationToken ct = default);

    Task<IEnumerable<ContatoDuplicadoGrupoDto>> GetDuplicadosAsync(CancellationToken ct = default);

    Task<IEnumerable<ContatoAniversarianteDto>> GetAniversariantesAsync(int? mes, CancellationToken ct = default);

    Task<IEnumerable<string>> GetTagsAsync(CancellationToken ct = default);

    Task<ContatoFiltroSalvoResponseDto> AddFiltroSalvoAsync(CreateContatoFiltroSalvoDto dto, CancellationToken ct = default);
    Task<IEnumerable<ContatoFiltroSalvoResponseDto>> GetFiltrosSalvosAsync(CancellationToken ct = default);
    Task RemoveFiltroSalvoAsync(Guid filtroId, CancellationToken ct = default);
}
