namespace LegalManager.Domain.Entities;

public class RefreshToken
{
    public Guid Id { get; set; }
    public Guid UsuarioId { get; set; }
    public string Token { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
    public bool Revogado { get; set; }
    public DateTime CriadoEm { get; set; }

    public Usuario Usuario { get; set; } = null!;
}
