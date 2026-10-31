using LegalManager.Application.DTOs.Contatos;
using LegalManager.Application.DTOs.Processos;

namespace LegalManager.Application.Interfaces;

public interface IProcessoService
{
    Task<ProcessoResponseDto> CreateAsync(CreateProcessoDto dto, CancellationToken ct = default);
    Task<ProcessoResponseDto> UpdateAsync(Guid id, UpdateProcessoDto dto, CancellationToken ct = default);
    Task<ProcessoResponseDto?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<PagedResultDto<ProcessoListItemDto>> GetAllAsync(ProcessoFiltroDto filtro, CancellationToken ct = default);
    /// <summary>Todos os processos que atendem o filtro (paginação ignorada), para o relatório CSV/PDF.</summary>
    Task<ProcessoRelatorioDto> GetRelatorioAsync(ProcessoFiltroDto filtro, CancellationToken ct = default);
    Task<IEnumerable<ProcessoUltimoAndamentoDto>> GetUltimosAndamentosAsync(int limite, CancellationToken ct = default);
    Task EncerrarAsync(Guid id, EncerrarProcessoDto dto, CancellationToken ct = default);
    /// <summary>Processos Ativos que a fonte indica como arquivados ou suspensos (sem consulta externa).</summary>
    Task<List<SugestaoSituacaoDto>> SugerirSituacaoNaFonteAsync(CancellationToken ct = default);
    /// <summary>Aplica a situação da fonte aos processos indicados, recalculada aqui; devolve os alterados.</summary>
    Task<List<SugestaoSituacaoDto>> AplicarSituacaoNaFonteAsync(IReadOnlyCollection<Guid> processoIds, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
    /// <summary>Marca/desmarca o processo como favorito do usuário logado (idempotente).</summary>
    Task DefinirFavoritoAsync(Guid processoId, bool favorito, CancellationToken ct = default);

    Task<AndamentoResponseDto> AddAndamentoAsync(Guid processoId, CreateAndamentoDto dto, CancellationToken ct = default);
    Task<IEnumerable<AndamentoResponseDto>> GetAndamentosAsync(Guid processoId, CancellationToken ct = default);
    Task<AndamentoResponseDto> UpdateAndamentoAsync(Guid processoId, Guid andamentoId, UpdateAndamentoDto dto, CancellationToken ct = default);
    Task DeleteAndamentoAsync(Guid processoId, Guid andamentoId, CancellationToken ct = default);
    Task<AndamentoResponseDto> SetAndamentoVisivelClienteAsync(Guid processoId, Guid andamentoId, bool visivel, CancellationToken ct = default);

    Task AdicionarParteAsync(Guid processoId, Guid contatoId, string tipoParte, CancellationToken ct = default);
    Task RemoverParteAsync(Guid processoId, Guid contatoId, CancellationToken ct = default);
}
