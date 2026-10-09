using LegalManager.Application.DTOs.Assistente;

namespace LegalManager.Application.Interfaces;

public interface IAssistenteService
{
    Task<AssistenteRespostaDto> PerguntarAsync(AssistentePerguntaDto dto, CancellationToken ct = default);

    /// <summary>Conversas do usuário atual (mais recentes primeiro).</summary>
    Task<IReadOnlyList<AssistenteConversaResumoDto>> ListarConversasAsync(int limite = 30, CancellationToken ct = default);

    /// <summary>Mensagens de uma conversa do usuário atual; null se não existir ou for de outra pessoa.</summary>
    Task<IReadOnlyList<AssistenteConversaMensagemDto>?> ObterConversaAsync(Guid conversaId, CancellationToken ct = default);
}
