using LegalManager.Domain.Enums;

namespace LegalManager.Domain.Entities;

public enum StatusTriagem
{
    /// <summary>Aguardando a IA (ou nova tentativa depois de uma falha).</summary>
    Pendente,
    /// <summary>Há um prazo ou audiência a criar, esperando o advogado.</summary>
    Sugerida,
    /// <summary>A IA leu e não há nada a criar (ex.: mero expediente).</summary>
    SemAcao,
    Aceita,
    Ignorada,
    /// <summary>A IA falhou em todas as tentativas.</summary>
    Falhou
}

/// <summary>
/// Triagem de uma publicação pela IA: o que ela pede (ato, prazo, audiência) e a sugestão de
/// tarefa/evento que o advogado confirma, edita ou ignora na tela de Publicações. A IA só extrai
/// o texto ("15 dias úteis"); a data de vencimento é calculada pelo código (FeriadosService).
/// Fica guardada também como trilha do que foi sugerido e do que o usuário decidiu.
/// </summary>
public class TriagemPublicacao
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid PublicacaoId { get; set; }
    public StatusTriagem Status { get; set; }
    public int Tentativas { get; set; }
    public string? Erro { get; set; }
    public DateTime CriadoEm { get; set; }
    public DateTime? ProcessadaEm { get; set; }

    // ── Extraído pela IA ────────────────────────────────────────────────────
    public string? Resumo { get; set; }
    /// <summary>Providência pedida ao advogado, ex.: "Apresentar contrarrazões".</summary>
    public string? Ato { get; set; }
    public int? PrazoDias { get; set; }
    public TipoCalculo? TipoCalculo { get; set; }
    /// <summary>Trecho literal da publicação de onde saiu o prazo (conferido contra o texto).</summary>
    public string? PrazoTrecho { get; set; }
    public DateTime? AudienciaDataHora { get; set; }
    public string? AudienciaTrecho { get; set; }

    // ── Calculado pelo código ───────────────────────────────────────────────
    /// <summary>Data considerada da publicação (1º dia útil após a disponibilização).</summary>
    public DateTime? DataPublicacao { get; set; }
    public DateTime? Vencimento { get; set; }
    /// <summary>Como o vencimento foi calculado, em texto para o advogado conferir.</summary>
    public string? MemoriaCalculo { get; set; }

    // ── Decisão do usuário ──────────────────────────────────────────────────
    public Guid? TarefaId { get; set; }
    public Guid? EventoId { get; set; }
    public Guid? ResolvidaPorId { get; set; }
    public DateTime? ResolvidaEm { get; set; }

    public Publicacao Publicacao { get; set; } = null!;
}
