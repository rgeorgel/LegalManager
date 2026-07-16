using LegalManager.Domain.Enums;

namespace LegalManager.Domain.Entities;

public class ContratoHonorario
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid ContatoId { get; set; }
    public Guid? ProcessoId { get; set; }
    public string NumeroContrato { get; set; } = string.Empty;
    public string? Objeto { get; set; }
    public decimal ValorTotal { get; set; }
    public FormaPagamentoContrato FormaPagamento { get; set; }
    public PeriodicidadeParcela? Periodicidade { get; set; }
    public int? NumeroParcelas { get; set; }
    public DateTime? DataPrimeiraParcela { get; set; }
    public decimal? ValorEntrada { get; set; }
    public DateTime? VencimentoEntrada { get; set; }
    public decimal PercentualMulta { get; set; } = 0.02m;
    public decimal PercentualJurosMensal { get; set; } = 0.015m;
    public string TipoCobranca { get; set; } = "Boleto/PIX";
    public string? Observacoes { get; set; }
    public StatusContratoHonorario Status { get; set; } = StatusContratoHonorario.Ativo;
    public Guid CriadoPorId { get; set; }
    public DateTime CriadoEm { get; set; } = DateTime.UtcNow;
    public DateTime? AtualizadoEm { get; set; }
    public DateTime? DistratoEm { get; set; }
    public string? DistratoMotivo { get; set; }
    public DateTime DataInicio { get; set; }
    public DateTime? DataFim { get; set; }

    public Tenant Tenant { get; set; } = null!;
    public Contato Contato { get; set; } = null!;
    public Processo? Processo { get; set; }
    public Usuario CriadoPor { get; set; } = null!;
    public ICollection<ParcelaHonorario> Parcelas { get; set; } = new List<ParcelaHonorario>();
    public ICollection<HistoricoContratoHonorario> Historicos { get; set; } = new List<HistoricoContratoHonorario>();
}
