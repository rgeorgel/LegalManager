using LegalManager.Domain.Enums;

namespace LegalManager.Domain.Entities;

public class ConviteUsuario
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string Email { get; set; } = string.Empty;
    public PerfilUsuario Perfil { get; set; }
    public string Token { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
    public bool Usado { get; set; }
    public DateTime CriadoEm { get; set; }

    public Tenant Tenant { get; set; } = null!;
}
