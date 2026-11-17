namespace LegalManager.Domain.Entities;

/// <summary>
/// Conexão do escritório com o Contratos (assinatura eletrônica): uma por tenant. A chave de API e o
/// segredo do webhook ficam cifrados (AES-GCM) — o Causify precisa deles em claro para chamar a API e
/// conferir os webhooks, então não dá para guardar só o hash. Não entra no export do tenant.
/// </summary>
public class IntegracaoAssinatura
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string ApiKeyCifrada { get; set; } = string.Empty;
    /// <summary>Últimos caracteres da chave, para o Admin reconhecer qual está em uso.</summary>
    public string ApiKeyFinal { get; set; } = string.Empty;
    public string? WebhookSecretCifrado { get; set; }
    public string OrganizacaoNome { get; set; } = string.Empty;
    public string? PlanoNome { get; set; }
    public Guid? ConectadoPorId { get; set; }
    public DateTime ConectadoEm { get; set; }
    public DateTime? UltimoWebhookEm { get; set; }
    /// <summary>Id do webhook que o Causify cadastrou sozinho no Contratos (null = cadastrado à mão, ou nenhum).</summary>
    public string? WebhookExternoId { get; set; }
    /// <summary>Site do Causify que o Causify cadastrou sozinho no embed de templates do Contratos.</summary>
    public string? EmbedOrigem { get; set; }
    /// <summary>
    /// Até onde a conferência periódica já leu (maior updatedAt visto em GET /contracts?updatedFrom).
    /// A próxima rodada começa daqui, com uma folga.
    /// </summary>
    public DateTime? ConferidoAte { get; set; }

    public Tenant Tenant { get; set; } = null!;
    public Usuario? ConectadoPor { get; set; }
}
