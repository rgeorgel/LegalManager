using LegalManager.Domain.Enums;

namespace LegalManager.Domain.Entities;

public class ParcelaHonorario
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid ContratoId { get; set; }
    public int Numero { get; set; }
    public bool IsEntrada { get; set; }
    public DateTime Vencimento { get; set; }
    public decimal ValorOriginal { get; set; }
    public DateTime? DataPagamento { get; set; }
    public decimal? ValorPago { get; set; }
    public string? Observacao { get; set; }
    public StatusParcelaHonorario Status { get; set; } = StatusParcelaHonorario.Pendente;
    public Guid? LancamentoFinanceiroId { get; set; }
    public DateTime CriadoEm { get; set; } = DateTime.UtcNow;

    public ContratoHonorario Contrato { get; set; } = null!;
}
