using LegalManager.Domain.Enums;

namespace LegalManager.Domain.Entities;

// Pedido de pagamento feito pelo parceiro no portal: reúne todas as comissões disponíveis
// naquele momento. O pagamento em si é feito fora do sistema (Pix) pelo super admin, que marca a
// solicitação como paga — ou a recusa, e aí as comissões voltam a ficar disponíveis.
// Só pode haver uma solicitação pendente por parceiro.
public class SolicitacaoPagamentoParceiro
{
    public Guid Id { get; set; }
    public Guid ParceiroId { get; set; }
    public decimal Valor { get; set; }
    public StatusSolicitacaoPagamento Status { get; set; }

    // Chave Pix informada na solicitação (cópia — o parceiro pode trocar a padrão depois).
    public string ChavePix { get; set; } = string.Empty;
    public string? ObservacaoParceiro { get; set; }

    public DateTime SolicitadaEm { get; set; }
    public DateTime? ResolvidaEm { get; set; }
    public Guid? ResolvidaPorId { get; set; }
    // Paga: comprovante / id da transação. Recusada: motivo. Visível ao parceiro.
    public string? Resposta { get; set; }

    public Parceiro Parceiro { get; set; } = null!;
    public ICollection<ComissaoParceiro> Comissoes { get; set; } = new List<ComissaoParceiro>();
}
