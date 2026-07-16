namespace LegalManager.Domain.Entities;

public class ConfiguracaoHonorario
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string? NomeEscritorio { get; set; }
    public string? AdvogadoResponsavel { get; set; }
    public string? OAB { get; set; }
    public string? Endereco { get; set; }
    public string? Telefone { get; set; }
    public string? Email { get; set; }
    public string? LogoUrl { get; set; }
    public decimal MetaMensalPadrao { get; set; }
    public decimal PercentualMultaDefault { get; set; } = 0.02m;
    public decimal PercentualJurosMensalDefault { get; set; } = 0.015m;
    public int DiasAvisoVencimento { get; set; } = 3;
    public DateTime AtualizadoEm { get; set; } = DateTime.UtcNow;

    public Tenant Tenant { get; set; } = null!;
}
