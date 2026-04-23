using LegalManager.Domain.Enums;

namespace LegalManager.Domain.Entities;

public class CreditoAI
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public TipoCreditoAI Tipo { get; set; }
    public int QuantidadeTotal { get; set; }
    public int QuantidadeUsada { get; set; }
    public int QuantidadeDisponivel => QuantidadeTotal - QuantidadeUsada;
    public OrigemCreditoAI Origem { get; set; }
    public DateTime CriadoEm { get; set; }
    public DateTime? ExpiraEm { get; set; }

    public Tenant Tenant { get; set; } = null!;
}