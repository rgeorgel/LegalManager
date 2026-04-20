using LegalManager.Domain.Enums;
using Microsoft.AspNetCore.Identity;

namespace LegalManager.Domain.Entities;

public class Usuario : IdentityUser<Guid>
{
    public Guid TenantId { get; set; }
    public string Nome { get; set; } = string.Empty;
    public PerfilUsuario Perfil { get; set; }
    public bool Ativo { get; set; } = true;
    public DateTime CriadoEm { get; set; }

    public Tenant Tenant { get; set; } = null!;
    public ICollection<RefreshToken> RefreshTokens { get; set; } = new List<RefreshToken>();
}
