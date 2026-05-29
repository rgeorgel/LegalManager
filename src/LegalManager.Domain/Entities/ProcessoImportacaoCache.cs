namespace LegalManager.Domain.Entities;

public class ProcessoImportacaoCache
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string NumeroCNJ { get; set; } = string.Empty;
    public string Fonte { get; set; } = string.Empty;
    public string DadosJson { get; set; } = string.Empty;
    public DateTime ExpiraEm { get; set; }
    public DateTime CriadoEm { get; set; }
}
