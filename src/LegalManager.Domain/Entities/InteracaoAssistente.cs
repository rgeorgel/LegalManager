namespace LegalManager.Domain.Entities;

/// <summary>
/// Uma pergunta feita ao assistente de IA e a resposta dada, com as ferramentas chamadas
/// (argumentos e resumo do retorno). Usado na tela de Super Admin "Assistente IA" para
/// monitoramento e auditoria cross-tenant. Tabela de log: sem FK, como ConsultaExterna.
/// </summary>
public class InteracaoAssistente
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid UsuarioId { get; set; }
    /// <summary>Super admin que fez a pergunta impersonando o usuário, se for o caso.</summary>
    public Guid? ImpersonadoPorId { get; set; }
    /// <summary>Agrupa as perguntas de uma mesma conversa (gerado pelo navegador).</summary>
    public Guid ConversaId { get; set; }
    public DateTime CriadoEm { get; set; }

    public string Pergunta { get; set; } = string.Empty;
    public string? Resposta { get; set; }
    public bool Sucesso { get; set; }
    public string? MensagemErro { get; set; }
    /// <summary>A resposta veio com trechos em outro idioma e foi reescrita em português.</summary>
    public bool IdiomaCorrigido { get; set; }

    /// <summary>JSON: [{ nome, argumentos, erro, resultado (truncado) }], na ordem das chamadas.</summary>
    public string? FerramentasJson { get; set; }
    public int QuantidadeFerramentas { get; set; }

    public string Provedor { get; set; } = string.Empty;
    public string Modelo { get; set; } = string.Empty;
    public int TokensEntrada { get; set; }
    public int TokensSaida { get; set; }
    public int DuracaoMs { get; set; }
}
