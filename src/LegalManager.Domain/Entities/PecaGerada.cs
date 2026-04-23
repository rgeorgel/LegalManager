using LegalManager.Domain.Enums;

namespace LegalManager.Domain.Entities;

public enum TipoPecaJuridica
{
    PeticaoInicial,
    Contestacao,
    Recurso,
    AlegacoesFinais,
    Pedido,
    Manifestacao,
    Memoriais
}

public class PecaGerada
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid? ProcessoId { get; set; }
    public Guid GeradoPorId { get; set; }
    public TipoPecaJuridica Tipo { get; set; }
    public string DescricaoSolicitacao { get; set; } = string.Empty;
    public string ConteudoGerado { get; set; } = string.Empty;
    public string? JurisprudenciaCitada { get; set; }
    public string? TesesSugeridas { get; set; }
    public DateTime CriadoEm { get; set; }

    public Processo? Processo { get; set; }
    public Usuario GeradoPor { get; set; } = null!;
}