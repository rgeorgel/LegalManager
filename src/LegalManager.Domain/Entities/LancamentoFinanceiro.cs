using LegalManager.Domain.Enums;

namespace LegalManager.Domain.Entities;

public class LancamentoFinanceiro
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid? ProcessoId { get; set; }
    public Guid? ContatoId { get; set; }
    public TipoLancamento Tipo { get; set; }
    public string Categoria { get; set; } = string.Empty;
    public decimal Valor { get; set; }
    public string? Descricao { get; set; }
    public DateTime DataVencimento { get; set; }
    public DateTime? DataPagamento { get; set; }
    public StatusLancamento Status { get; set; } = StatusLancamento.Pendente;
    public DateTime CriadoEm { get; set; } = DateTime.UtcNow;

    public Tenant Tenant { get; set; } = null!;
    public Processo? Processo { get; set; }
    public Contato? Contato { get; set; }
}
