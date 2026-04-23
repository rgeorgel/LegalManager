namespace LegalManager.Domain.Entities;

public class TraducaoAndamento
{
    public Guid Id { get; set; }
    public Guid AndamentoId { get; set; }
    public Guid TenantId { get; set; }
    public Guid SolicitadoPorId { get; set; }
    public Guid? ClienteId { get; set; }
    public string TextoOriginal { get; set; } = string.Empty;
    public string TextoTraduzido { get; set; } = string.Empty;
    public bool EnviadoAoCliente { get; set; }
    public bool RevisadoPreviamente { get; set; }
    public DateTime CriadoEm { get; set; }

    public Andamento Andamento { get; set; } = null!;
    public Usuario SolicitadoPor { get; set; } = null!;
    public Contato? Cliente { get; set; }
}