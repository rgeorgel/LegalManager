using LegalManager.Domain.Enums;

namespace LegalManager.Domain.Entities;

// Código promocional aplicado no cadastro (register.html, campo "Código promocional").
// Gerenciado pelo super admin. Entidade global (não pertence a um tenant); o uso fica
// registrado em Tenant.VoucherUtilizado (cadastro) e/ou Tenant.CodigoDesconto (desconto,
// também aplicável depois na tela de assinatura). Dois benefícios, combináveis (ao menos um
// é obrigatório):
//  - Período grátis: DiasGratuitos > 0 → o tenant já nasce Ativo no Plano do código, com
//    PlanoExpiraEm = cadastro + DiasGratuitos.
//  - Desconto: DescontoPercentual → cupom Stripe aplicado no checkout da primeira
//    assinatura (ver AssinaturaController.IniciarCheckout), por DescontoMeses ciclos.
public class CodigoPromocional
{
    public Guid Id { get; set; }

    // Sempre normalizado (trim + minúsculas) — ver Normalizar.
    public string Codigo { get; set; } = string.Empty;

    // Texto mostrado ao visitante no cadastro quando o código é válido.
    public string? Descricao { get; set; }

    // Plano do período grátis. Só é usado (e obrigatório) quando DiasGratuitos > 0.
    public PlanoTipo? Plano { get; set; }
    public int DiasGratuitos { get; set; }

    // % de desconto na assinatura (1–99). Null = sem desconto.
    public int? DescontoPercentual { get; set; }

    // Por quantos meses de assinatura o desconto vale. Null = para sempre.
    public int? DescontoMeses { get; set; }

    // Null = ilimitado.
    public int? MaxUsos { get; set; }

    // Último dia (inclusive, no calendário de Brasília) em que o código vale. Null = sem validade.
    public DateOnly? ValidoAte { get; set; }

    public bool Ativo { get; set; } = true;

    public DateTime CriadoEm { get; set; }
    public Guid? CriadoPorId { get; set; }
    public DateTime? AtualizadoEm { get; set; }

    public bool TemPeriodoGratis => DiasGratuitos > 0 && Plano.HasValue;
    public bool TemDesconto => DescontoPercentual is > 0;

    public static string Normalizar(string codigo) => codigo.Trim().ToLowerInvariant();
}
