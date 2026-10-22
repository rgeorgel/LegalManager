namespace LegalManager.Domain.Entities;

// Comissão de um parceiro sobre um pagamento de assinatura (Faturamento) de um escritório que
// ele indicou — gerada junto com o Faturamento (ver ComissoesParceiros.RegistrarAsync).
//
// Sem FK para Tenant nem para Faturamento: o histórico do parceiro (e o que já lhe foi pago)
// precisa sobreviver à exclusão do escritório, por isso o nome fica copiado em NomeEscritorio.
//
// Situação: SolicitacaoId null = disponível para saque; com solicitação = segue o status dela.
public class ComissaoParceiro
{
    public Guid Id { get; set; }
    public Guid ParceiroId { get; set; }
    public Guid TenantId { get; set; }
    public string NomeEscritorio { get; set; } = string.Empty;

    // Faturamento que originou a comissão (único — um pagamento comissiona uma vez só).
    public Guid FaturamentoId { get; set; }
    public string? Descricao { get; set; }

    // Valor efetivamente pago pelo escritório (já com descontos).
    public decimal ValorPagamento { get; set; }
    // % vigente no momento do pagamento — mudar o % do parceiro não altera comissões já geradas.
    public decimal Percentual { get; set; }
    public decimal Valor { get; set; }

    // Quando o escritório pagou (UTC).
    public DateTime PagoEm { get; set; }

    public Guid? SolicitacaoId { get; set; }
    public DateTime CriadoEm { get; set; }

    public Parceiro Parceiro { get; set; } = null!;
    public SolicitacaoPagamentoParceiro? Solicitacao { get; set; }
}
