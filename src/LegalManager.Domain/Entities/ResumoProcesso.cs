using LegalManager.Domain.Enums;

namespace LegalManager.Domain.Entities;

public class ResumoProcesso
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid ProcessoId { get; set; }
    public Guid GeradoPorId { get; set; }
    public string Conteudo { get; set; } = string.Empty;
    // Pedido do resumo enquanto Gerando; momento da conclusão depois.
    public DateTime GeradoEm { get; set; }
    public bool VisivelCliente { get; set; }
    public StatusResumoProcesso Status { get; set; } = StatusResumoProcesso.Concluido;
    public string? MensagemErro { get; set; }

    public Processo Processo { get; set; } = null!;
    public Usuario GeradoPor { get; set; } = null!;
}
