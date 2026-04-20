using LegalManager.Domain.Enums;

namespace LegalManager.Domain.Entities;

public class Andamento
{
    public Guid Id { get; set; }
    public Guid ProcessoId { get; set; }
    public Guid TenantId { get; set; }
    public DateTime Data { get; set; }
    public TipoAndamento Tipo { get; set; }
    public string Descricao { get; set; } = string.Empty;
    public FonteAndamento Fonte { get; set; }
    public string? DescricaoTraduzidaIA { get; set; }
    public Guid? RegistradoPorId { get; set; }
    public DateTime CriadoEm { get; set; }

    public Processo Processo { get; set; } = null!;
    public Usuario? RegistradoPor { get; set; }
}
