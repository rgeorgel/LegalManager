using LegalManager.Domain.Enums;

namespace LegalManager.Domain.Entities;

/// <summary>
/// Link privado que publica a agenda do usuário em iCal (.ics), para assinar no Google Agenda,
/// Outlook ou Apple. Um por usuário; gerar outro troca o token e derruba o anterior.
/// Só o hash (SHA-256) do token fica no banco — o link aparece inteiro uma vez, ao ser gerado.
/// </summary>
public class FeedAgenda
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid UsuarioId { get; set; }
    public string TokenHash { get; set; } = string.Empty;
    public EscopoFeedAgenda Escopo { get; set; }
    /// <summary>Títulos e descrições sem nomes de clientes/partes (só tipo, nº do processo e link).</summary>
    public bool OcultarClientes { get; set; }
    public DateTime CriadoEm { get; set; }
    /// <summary>Última leitura do feed pelo aplicativo de calendário (UTC; gravada no máximo a cada 10 min).</summary>
    public DateTime? UltimoAcessoEm { get; set; }

    public Tenant Tenant { get; set; } = null!;
    public Usuario Usuario { get; set; } = null!;
}
