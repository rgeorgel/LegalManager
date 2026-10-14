using LegalManager.Domain.Enums;

namespace LegalManager.Domain.Entities;

/// <summary>
/// Registro de uso da calculadora de prazos (POST /api/prazos/calcular): quem usou, quando e
/// com quais parâmetros. Os parâmetros permitem ao superadmin recalcular depois (timeline do
/// tenant) e ver se o resultado mudaria com a regra de feriados atual. Registros anteriores a
/// essa mudança só têm quem/quando (parâmetros nulos).
/// </summary>
public class CalculoPrazo
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid UsuarioId { get; set; }
    public DateTime CriadoEm { get; set; } = DateTime.UtcNow;

    public DateTime? DataInicio { get; set; }
    public int? QuantidadeDias { get; set; }
    public TipoCalculo? TipoCalculo { get; set; }
    /// <summary>Feriados extras informados na tela, JSON de datas "yyyy-MM-dd".</summary>
    public string? FeriadosAdicionaisJson { get; set; }
    public DateTime? DataFinal { get; set; }

    public Tenant Tenant { get; set; } = null!;
    public Usuario Usuario { get; set; } = null!;
}
