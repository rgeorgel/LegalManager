using LegalManager.Application.DTOs.Publicacoes;

namespace LegalManager.Application.Interfaces;

/// <summary>Decisão do advogado sobre a sugestão da IA para uma publicação (ver TriagemPublicacoesJob).</summary>
public interface ITriagemPublicacaoService
{
    /// <summary>Cria o prazo (ou a audiência) e marca a publicação como lida. Devolve a triagem atualizada.</summary>
    Task<TriagemPublicacaoDto> AceitarAsync(Guid publicacaoId, AceitarTriagemDto dto, string? ip, CancellationToken ct = default);
    Task<TriagemPublicacaoDto> IgnorarAsync(Guid publicacaoId, CancellationToken ct = default);
    /// <summary>Põe a publicação de volta na fila da IA (também para publicações antigas, que não entraram).</summary>
    Task<TriagemPublicacaoDto> RefazerAsync(Guid publicacaoId, CancellationToken ct = default);
}
