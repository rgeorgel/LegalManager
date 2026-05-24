namespace LegalManager.Domain.Entities;

public class WaitlistEntry
{
    public Guid Id { get; set; }
    public string Nome { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Plano { get; set; } = string.Empty;
    public Guid? UserId { get; set; }
    public DateTime CriadoEm { get; set; }
}
