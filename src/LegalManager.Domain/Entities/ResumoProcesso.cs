namespace LegalManager.Domain.Entities;

public class ResumoProcesso
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid ProcessoId { get; set; }
    public Guid GeradoPorId { get; set; }
    public string Conteudo { get; set; } = string.Empty;
    public DateTime GeradoEm { get; set; }

    public Processo Processo { get; set; } = null!;
    public Usuario GeradoPor { get; set; } = null!;
}
