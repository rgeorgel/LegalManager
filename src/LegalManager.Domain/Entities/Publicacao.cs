using LegalManager.Domain.Enums;

namespace LegalManager.Domain.Entities;

public class Publicacao
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid? ProcessoId { get; set; }
    public string? NumeroCNJ { get; set; }
    public string Diario { get; set; } = string.Empty;
    public DateTime DataPublicacao { get; set; }
    public string Conteudo { get; set; } = string.Empty;
    public TipoPublicacao Tipo { get; set; }
    public StatusPublicacao Status { get; set; }
    public bool Urgente { get; set; }
    public string? ClassificacaoIA { get; set; }
    public string? IdExterno { get; set; }
    public string? HashDje { get; set; }
    public string? Secao { get; set; }
    public string? Pagina { get; set; }
    public DateTime CapturaEm { get; set; }

    public Tenant Tenant { get; set; } = null!;
    public Processo? Processo { get; set; }
}
