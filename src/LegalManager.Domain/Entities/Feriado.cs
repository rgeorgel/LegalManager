namespace LegalManager.Domain.Entities;

public class Feriado
{
    public int Id { get; set; }
    public DateOnly Data { get; set; }
    public string Nome { get; set; } = string.Empty;
    public string Tipo { get; set; } = string.Empty; // nacional | estadual | municipal
    public string? Uf { get; set; }
    public string? Municipio { get; set; }
    public bool Ativo { get; set; } = true;
    public DateTime CriadoEm { get; set; }
    public DateTime AtualizadoEm { get; set; }

    // null = feriado global (nacional/estadual); not null = municipal criado pelo tenant
    public Guid? TenantId { get; set; }
    public Tenant? Tenant { get; set; }
}
