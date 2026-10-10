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

/// <summary>
/// Executa (ou descarta) as alterações propostas pelo assistente, só por quem fez a pergunta.
/// </summary>
public interface IAssistenteAcoesService
{
    /// <summary>Grava a alteração. Null se a ação não existir ou for de outra pessoa.</summary>
    Task<AssistenteAcaoDto?> ConfirmarAsync(Guid acaoId, string? ip, CancellationToken ct = default);

    /// <summary>Descarta a alteração. Null se a ação não existir ou for de outra pessoa.</summary>
    Task<AssistenteAcaoDto?> CancelarAsync(Guid acaoId, CancellationToken ct = default);

    /// <summary>
    /// Reverte uma alteração confirmada há pouco (volta os valores anteriores ou exclui o que foi
    /// criado). Null se a ação não existir ou for de outra pessoa.
    /// </summary>
    Task<AssistenteAcaoDto?> DesfazerAsync(Guid acaoId, string? ip, CancellationToken ct = default);
}
