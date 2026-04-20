namespace LegalManager.Domain.Entities;

public class Atendimento
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid ContatoId { get; set; }
    public Guid UsuarioId { get; set; }
    public string Descricao { get; set; } = string.Empty;
    public DateTime Data { get; set; }
    public DateTime CriadoEm { get; set; }

    public Contato Contato { get; set; } = null!;
    public Usuario Usuario { get; set; } = null!;
}
