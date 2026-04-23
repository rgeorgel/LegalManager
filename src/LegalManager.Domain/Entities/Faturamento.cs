namespace LegalManager.Domain.Entities;

public class Faturamento
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string BillingId { get; set; } = string.Empty;
    public string Periodo { get; set; } = string.Empty;
    public decimal Valor { get; set; }
    public string Moeda { get; set; } = "BRL";
    public StatusFaturamento Status { get; set; }
    public DateTime? DataPagamento { get; set; }
    public DateTime DataCriacao { get; set; }
    public string? Descricao { get; set; }

    public Tenant Tenant { get; set; } = null!;
}

public enum StatusFaturamento
{
    Pendente,
    Pago,
    Cancelado,
    Expirado
}