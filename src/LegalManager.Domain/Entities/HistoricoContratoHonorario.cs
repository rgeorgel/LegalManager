using LegalManager.Domain.Enums;

namespace LegalManager.Domain.Entities;

public class HistoricoContratoHonorario
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid ContratoId { get; set; }
    public EventoContratoHonorario TipoEvento { get; set; }
    public string Descricao { get; set; } = string.Empty;
    public Guid? UsuarioId { get; set; }
    public DateTime CriadoEm { get; set; } = DateTime.UtcNow;
    public string? DadosAnterioresJson { get; set; }
    public string? DadosNovosJson { get; set; }

    public ContratoHonorario Contrato { get; set; } = null!;
}
