namespace LegalManager.Domain;

/// <summary>
/// "Indique e ganhe": qualquer usuário de escritório gera o próprio link de indicação e recebe
/// comissão sobre os pagamentos de assinatura dos escritórios que se cadastrarem por ele. Por
/// baixo é um Parceiro (Parceiro.UsuarioId) — mesmas regras de comissão e de solicitação de
/// pagamento do portal do parceiro. Estes são os valores de quem entra no programa; o super admin
/// pode ajustar um usuário específico em superadmin/parceiros.html, como qualquer parceiro.
/// </summary>
public static class ProgramaIndicacao
{
    public const decimal PercentualComissao = 25m;

    // Null = comissiona enquanto o escritório indicado pagar (ver Parceiro.MesesComissao).
    public static readonly int? MesesComissao = null;
}
