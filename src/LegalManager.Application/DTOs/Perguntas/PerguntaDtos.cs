using System.ComponentModel.DataAnnotations;

namespace LegalManager.Application.DTOs.Perguntas;

// ── Usuário final (widget flutuante + modal de login) ──────────────────────

public record PerguntaPendenteDto(
    Guid Id,
    string Texto,
    string? Descricao,
    string TipoResposta,
    List<string> Opcoes
);

public record ResponderPerguntaDto(string? RespostaTexto, string? OpcaoEscolhida, List<string>? OpcoesEscolhidas);

// ── Super admin: cadastro ───────────────────────────────────────────────────

public record PerguntaAdminDto(
    Guid Id,
    string Texto,
    string? Descricao,
    string TipoResposta,
    List<string> Opcoes,
    string Publico,
    string Segmento,
    string? PlanoAlvo,
    Guid? GrupoId,
    string? GrupoNome,
    bool Ativa,
    int Ordem,
    DateTime CriadoEm,
    int TotalRespostas
);

public record SalvarPerguntaDto(
    [Required, MaxLength(500)] string Texto,
    [MaxLength(1000)] string? Descricao,
    [Required] string TipoResposta,
    List<string>? Opcoes,
    [Required] string Publico,
    [Required] string Segmento,
    string? PlanoAlvo,
    Guid? GrupoId,
    bool Ativa,
    int Ordem
);

// ── Super admin: respostas ───────────────────────────────────────────────────

public record RespostaAdminDto(
    Guid Id,
    string RespondenteNome,
    string? RespondenteEmail,
    string RespondenteTipo,
    string TenantNome,
    string? RespostaTexto,
    string? OpcaoEscolhida,
    List<string> OpcoesEscolhidas,
    DateTime RespondidoEm
);

public record OpcaoContagemDto(string Opcao, int Total);

public record PerguntaComRespostasDto(
    PerguntaAdminDto Pergunta,
    List<OpcaoContagemDto> Contagens,
    List<RespostaAdminDto> Respostas
);
