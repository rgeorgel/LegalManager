using System.ComponentModel.DataAnnotations;

namespace LegalManager.Application.DTOs.Assistente;

/// <summary>Uma mensagem do histórico da conversa. Papel: "user" ou "assistant".</summary>
public record AssistenteMensagemDto(
    [Required] string Papel,
    [Required, MaxLength(8000)] string Conteudo
);

/// <summary>
/// O histórico fica no navegador (versão de teste, sem persistência): o cliente reenvia as
/// mensagens anteriores a cada pergunta, e a última deve ser a do usuário.
/// </summary>
public record AssistentePerguntaDto(
    [Required, MinLength(1), MaxLength(40)] List<AssistenteMensagemDto> Mensagens,
    // Agrupa as perguntas da conversa na auditoria; sem ele, o servidor gera um novo.
    Guid? ConversaId = null
);

public record AssistenteRespostaDto(
    string Resposta,
    IReadOnlyList<string> FerramentasUsadas,
    Guid ConversaId,
    // Alterações propostas nesta resposta, aguardando o usuário confirmar no chat.
    IReadOnlyList<AssistenteAcaoDto>? Acoes = null
);

// ── Ações (alterações propostas pelo assistente) ────────────────────────────

public record AssistenteAlteracaoDto(string Campo, string? Antes, string? Depois);

/// <summary>
/// Alteração proposta pelo assistente. Status: Pendente, Confirmada, Cancelada, Falhou, Desfeita —
/// "Expirada" quando ficou pendente além do prazo de confirmação. DesfazerAte: até quando uma
/// ação confirmada pode ser desfeita (null se não pode).
/// </summary>
public record AssistenteAcaoDto(
    Guid Id,
    string Tipo,
    string Titulo,
    IReadOnlyList<AssistenteAlteracaoDto> Alteracoes,
    string Status,
    DateTime ExpiraEm,
    string? Link,
    string? Erro,
    DateTime? DesfazerAte = null
);

// ── Histórico do próprio usuário ────────────────────────────────────────────

public record AssistenteConversaResumoDto(
    Guid ConversaId,
    string Titulo,
    int Perguntas,
    DateTime IniciadaEm,
    DateTime UltimaEm
);

public record AssistenteConversaMensagemDto(
    DateTime CriadoEm,
    string Pergunta,
    string? Resposta,
    bool Sucesso,
    IReadOnlyList<string> FerramentasUsadas,
    IReadOnlyList<AssistenteAcaoDto>? Acoes = null
);

// ── Super Admin: monitoramento/auditoria ────────────────────────────────────

public record InteracaoAssistenteListItemDto(
    Guid Id,
    DateTime CriadoEm,
    Guid TenantId,
    string? TenantNome,
    Guid UsuarioId,
    string? UsuarioNome,
    bool Impersonado,
    Guid ConversaId,
    string Pergunta,
    string? Resposta,
    bool Sucesso,
    int QuantidadeFerramentas,
    int TokensEntrada,
    int TokensSaida,
    int DuracaoMs,
    // Alterações propostas na pergunta e quantas o usuário confirmou.
    int Acoes = 0,
    int AcoesConfirmadas = 0
);

public record InteracaoAssistentePagedResultDto(
    IReadOnlyList<InteracaoAssistenteListItemDto> Items, int Total, int Page, int PageSize);

public record InteracaoAssistenteDetalheDto(
    Guid Id,
    DateTime CriadoEm,
    Guid TenantId,
    string? TenantNome,
    Guid UsuarioId,
    string? UsuarioNome,
    Guid? ImpersonadoPorId,
    string? ImpersonadoPorNome,
    Guid ConversaId,
    string Pergunta,
    string? Resposta,
    bool Sucesso,
    string? MensagemErro,
    bool IdiomaCorrigido,
    string? FerramentasJson,
    string Provedor,
    string Modelo,
    int TokensEntrada,
    int TokensSaida,
    int DuracaoMs,
    IReadOnlyList<AssistenteAcaoDto>? Acoes = null
);

public record AssistenteResumoTenantDto(
    Guid TenantId,
    string? TenantNome,
    int Perguntas,
    int Conversas,
    int Usuarios,
    int Erros,
    long TokensEntrada,
    long TokensSaida,
    DateTime UltimaPergunta,
    int Acoes = 0,
    int AcoesConfirmadas = 0
);
