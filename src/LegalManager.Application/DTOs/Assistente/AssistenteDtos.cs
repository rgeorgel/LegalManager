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
    Guid ConversaId
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
    IReadOnlyList<string> FerramentasUsadas
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
    int DuracaoMs
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
    int DuracaoMs
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
    DateTime UltimaPergunta
);
