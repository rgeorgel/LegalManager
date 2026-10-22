namespace LegalManager.Domain.Entities;

// Parceiro comercial (consultor, controladoria jurídica, contador...) que indica escritórios e
// recebe comissão sobre as assinaturas que eles pagam. Entidade global, cadastrada pelo super
// admin (superadmin/parceiros.html). Independente de CodigoPromocional: o código do parceiro não
// dá benefício ao escritório, só registra a indicação (Tenant.ParceiroId) — os dois podem ser
// usados juntos no cadastro.
//
// O parceiro tem login próprio no portal do parceiro (/parceiro/), com JWT de role "Parceiro". Não
// é um Usuario do Identity porque não pertence a nenhum tenant (mesmo modelo de AcessoCliente).
public class Parceiro
{
    public Guid Id { get; set; }
    public string Nome { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Telefone { get; set; }
    // CPF ou CNPJ, para a emissão do pagamento/nota.
    public string? Documento { get; set; }

    // Código de indicação, sempre normalizado (trim + minúsculas) — ver Normalizar. Link de
    // cadastro: /register.html?parceiro=CODIGO.
    public string Codigo { get; set; } = string.Empty;

    // % sobre cada pagamento de assinatura dos escritórios indicados (ver ComissoesParceiros).
    public decimal PercentualComissao { get; set; }
    // Por quantos meses, contados do primeiro pagamento comissionado de cada escritório, os
    // pagamentos geram comissão. Null = enquanto o escritório pagar.
    public int? MesesComissao { get; set; }

    // Chave Pix padrão para os pagamentos; o parceiro pode trocá-la ao solicitar um pagamento.
    public string? ChavePix { get; set; }
    // Anotações internas do super admin (contrato, condições combinadas). Não aparecem no portal.
    public string? Observacoes { get; set; }

    // Inativo: não entra no portal, o código deixa de valer no cadastro e novos pagamentos dos
    // escritórios indicados não geram comissão. As comissões já geradas continuam a pagar.
    public bool Ativo { get; set; } = true;

    // Null até o parceiro definir a senha pelo link de convite.
    public string? SenhaHash { get; set; }
    // Token de uso único do link de convite / redefinição de senha.
    public string? TokenSenha { get; set; }
    public DateTime? TokenSenhaExpiraEm { get; set; }

    public DateTime CriadoEm { get; set; }
    public Guid? CriadoPorId { get; set; }
    public DateTime? AtualizadoEm { get; set; }
    public DateTime? UltimoAcessoEm { get; set; }

    public ICollection<ComissaoParceiro> Comissoes { get; set; } = new List<ComissaoParceiro>();
    public ICollection<SolicitacaoPagamentoParceiro> Solicitacoes { get; set; } = new List<SolicitacaoPagamentoParceiro>();

    public static string Normalizar(string codigo) => codigo.Trim().ToLowerInvariant();
}
