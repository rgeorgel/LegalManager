namespace LegalManager.Domain.Entities;

public class HonorarioCalculo
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid UsuarioId { get; set; }
    public DateTime CriadoEm { get; set; } = DateTime.UtcNow;

    public string? Cliente { get; set; }
    public string Mode { get; set; } = string.Empty;
    public string Area { get; set; } = string.Empty;
    public string Tipo { get; set; } = string.Empty;
    public string ItemOAB { get; set; } = string.Empty;
    public decimal? ValorCausa { get; set; }
    public decimal HonorarioBase { get; set; }
    public decimal HonorarioAjustado { get; set; }
    public decimal TotalCorrigido { get; set; }
    public decimal Multiplicador { get; set; }
    public decimal TaxaCorrecao { get; set; }
    public string FormaPagamento { get; set; } = string.Empty;
    public string Complexidade { get; set; } = string.Empty;
    public string Risco { get; set; } = string.Empty;
    public string Urgencia { get; set; } = string.Empty;
    public string Capacidade { get; set; } = string.Empty;
    public string Exito { get; set; } = string.Empty;
    public string StateJson { get; set; } = "{}";

    public Tenant Tenant { get; set; } = null!;
    public Usuario Usuario { get; set; } = null!;
}
