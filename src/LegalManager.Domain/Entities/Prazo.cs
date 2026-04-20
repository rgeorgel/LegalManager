using LegalManager.Domain.Enums;

namespace LegalManager.Domain.Entities;

public class Prazo
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid? ProcessoId { get; set; }
    public Guid? AndamentoId { get; set; }
    public string Descricao { get; set; } = string.Empty;
    public DateTime DataInicio { get; set; }
    public int QuantidadeDias { get; set; }
    public TipoCalculo TipoCalculo { get; set; }
    public DateTime DataFinal { get; set; }
    public StatusPrazo Status { get; set; }
    public Guid? ResponsavelId { get; set; }
    public string? Observacoes { get; set; }
    public DateTime CriadoEm { get; set; }
    public DateTime? AtualizadoEm { get; set; }

    public Tenant Tenant { get; set; } = null!;
    public Processo? Processo { get; set; }
    public Andamento? Andamento { get; set; }
    public Usuario? Responsavel { get; set; }
}
