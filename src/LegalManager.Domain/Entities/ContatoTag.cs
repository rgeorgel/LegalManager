namespace LegalManager.Domain.Entities;

public class ContatoTag
{
    public Guid Id { get; set; }
    public Guid ContatoId { get; set; }
    public string Tag { get; set; } = string.Empty;

    public Contato Contato { get; set; } = null!;
}
