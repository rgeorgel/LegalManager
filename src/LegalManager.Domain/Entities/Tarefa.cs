using LegalManager.Domain.Enums;

namespace LegalManager.Domain.Entities;

public class Tarefa
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid? ProcessoId { get; set; }
    public Guid? ContatoId { get; set; }
    public string Titulo { get; set; } = string.Empty;
    public string? Descricao { get; set; }
    public Guid? ResponsavelId { get; set; }
    public Guid CriadoPorId { get; set; }
    public DateTime? Prazo { get; set; }
    public PrioridadeTarefa Prioridade { get; set; }
    public StatusTarefa Status { get; set; }
    public DateTime CriadoEm { get; set; }
    public DateTime? AtualizadoEm { get; set; }
    public DateTime? ConcluidaEm { get; set; }

    public Tenant Tenant { get; set; } = null!;
    public Processo? Processo { get; set; }
    public Contato? Contato { get; set; }
    public Usuario? Responsavel { get; set; }
    public Usuario CriadoPor { get; set; } = null!;
    public ICollection<TarefaTag> Tags { get; set; } = new List<TarefaTag>();
}
