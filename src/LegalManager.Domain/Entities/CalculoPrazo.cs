namespace LegalManager.Domain.Entities;

/// <summary>
/// Registro leve de uso da calculadora de prazos (POST /api/prazos/calcular).
/// Não guarda o cálculo em si (isso é stateless na tela) — só marca "quem usou, quando",
/// para permitir ao superadmin enxergar adoção do recurso por tenant.
/// </summary>
public class CalculoPrazo
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid UsuarioId { get; set; }
    public DateTime CriadoEm { get; set; } = DateTime.UtcNow;

    public Tenant Tenant { get; set; } = null!;
    public Usuario Usuario { get; set; } = null!;
}
