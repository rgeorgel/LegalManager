using System.ComponentModel.DataAnnotations;

namespace LegalManager.Application.DTOs.Parceiros;

// ── Super admin ─────────────────────────────────────────────────────────────

public record ParceiroDto(
    Guid Id,
    string Nome,
    string Email,
    string? Telefone,
    string? Documento,
    string Codigo,
    decimal PercentualComissao,
    int? MesesComissao,
    string? ChavePix,
    string? Observacoes,
    bool Ativo,
    bool SenhaDefinida,
    DateTime CriadoEm,
    DateTime? UltimoAcessoEm,
    int Escritorios,
    int Assinantes,
    decimal TotalComissoes,
    decimal Disponivel,
    decimal Solicitado,
    decimal Pago
);

public record SalvarParceiroDto(
    [Required, MaxLength(200)] string Nome,
    [Required, EmailAddress, MaxLength(256)] string Email,
    [MaxLength(30)] string? Telefone,
    [MaxLength(20)] string? Documento,
    [Required, MaxLength(50), RegularExpression("^[A-Za-z0-9_-]+$",
        ErrorMessage = "Use apenas letras, números, hífen e sublinhado.")] string Codigo,
    [Range(0.01, 100)] decimal PercentualComissao,
    [Range(1, 120)] int? MesesComissao,
    [MaxLength(200)] string? ChavePix,
    [MaxLength(2000)] string? Observacoes,
    bool Ativo = true
);

// Link de definição de senha gerado pelo super admin (convite ou nova senha). O link também vai
// por e-mail, mas é devolvido para poder ser enviado por outro canal se o e-mail falhar.
public record LinkSenhaParceiroDto(string Link, DateTime ExpiraEm);

// Vincula um escritório já cadastrado ao parceiro: id do tenant ou e-mail de um usuário dele.
public record VincularEscritorioDto([Required, MaxLength(256)] string Busca);

public record ResolverSolicitacaoDto([MaxLength(1000)] string? Resposta);

// ── Compartilhados (portal e super admin) ───────────────────────────────────

public record ParceiroResumoDto(
    string Nome,
    string Email,
    string Codigo,
    decimal PercentualComissao,
    int? MesesComissao,
    string? ChavePix,
    int Escritorios,
    int Assinantes,
    decimal TotalComissoes,
    decimal Disponivel,
    decimal Solicitado,
    decimal Pago,
    bool SolicitacaoPendente
);

public record EscritorioIndicadoDto(
    Guid TenantId,
    string Nome,
    DateTime CadastradoEm,
    DateTime? VinculadoEm,
    string Plano,
    string? Periodo,
    string Situacao,
    decimal? ValorMensal,
    DateTime? PrimeiroPagamento,
    DateTime? UltimoPagamento,
    decimal TotalPago,
    decimal TotalComissao,
    // Último dia em que pagamentos ainda comissionam (MesesComissao). Null = sem limite ou sem pagamento.
    DateTime? ComissaoAte
);

public record ComissaoParceiroDto(
    Guid Id,
    Guid TenantId,
    string NomeEscritorio,
    string? Descricao,
    decimal ValorPagamento,
    decimal Percentual,
    decimal Valor,
    DateTime PagoEm,
    string Situacao,
    Guid? SolicitacaoId
);

public record SolicitacaoPagamentoDto(
    Guid Id,
    Guid ParceiroId,
    string NomeParceiro,
    decimal Valor,
    string Status,
    string ChavePix,
    string? ObservacaoParceiro,
    DateTime SolicitadaEm,
    DateTime? ResolvidaEm,
    string? Resposta,
    int QuantidadeComissoes
);

// ── Portal do parceiro ──────────────────────────────────────────────────────

public record LoginParceiroDto([Required, EmailAddress] string Email, [Required] string Senha);

public record DefinirSenhaParceiroDto([Required] string Token, [Required, MinLength(8)] string Senha);

public record EsqueciSenhaParceiroDto([Required, EmailAddress] string Email);

public record ParceiroPerfilDto(Guid Id, string Nome, string Email, string Codigo);

public record ParceiroAuthResponseDto(string AccessToken, DateTime ExpiraEm, ParceiroPerfilDto Parceiro);

public record SolicitarPagamentoDto(
    [Required, MaxLength(200)] string ChavePix,
    [MaxLength(1000)] string? Observacao
);

// Resposta pública (cadastro): só o nome, para o visitante confirmar quem o indicou.
public record ParceiroPublicoDto(string Codigo, string Nome);
