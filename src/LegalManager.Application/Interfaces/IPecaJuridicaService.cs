using LegalManager.Application.DTOs.IA;

namespace LegalManager.Application.Interfaces;

public interface IPecaJuridicaService
{
    Task<PecaGeradaResponseDto> GerarPecaAsync(GerarPecaDto dto, Guid usuarioId, CancellationToken ct = default);
    Task<PecaGeradaResponseDto?> ObterPecaAsync(Guid id, CancellationToken ct = default);
    Task<IEnumerable<PecaGeradaResponseDto>> ListarAsync(ListPecasGeradasDto filtro, CancellationToken ct = default);
}