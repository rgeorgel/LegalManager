using LegalManager.Domain.Enums;

namespace LegalManager.Domain.Entities;

// Preço mensal de tabela de um plano pago, editado pelo super admin (superadmin/precos-planos.html).
// Entidade global, uma linha por plano. Vale para novos checkouts, trocas de plano e para os
// preços exibidos nas landing pages; assinaturas Stripe já criadas ficam presas ao Price do
// momento em que foram feitas, então quem já assina continua pagando o valor antigo nas
// renovações (ver StripeService.ObterOuCriarPrecoAsync).
public class PrecoPlano
{
    public PlanoTipo Plano { get; set; }

    public decimal ValorMensal { get; set; }

    public DateTime? AtualizadoEm { get; set; }
    public Guid? AtualizadoPorId { get; set; }
}
