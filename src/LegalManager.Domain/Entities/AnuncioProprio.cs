namespace LegalManager.Domain.Entities;

// Anúncio do próprio Causify, cadastrado pelo super admin. Ocupa os espaços de publicidade do
// plano Free quando o Adsterra não entrega nada (bloqueador de anúncios, sem campanha). Entidade
// global (não pertence a um tenant). Ver PublicidadeController e /js/publicidade.js.
public class AnuncioProprio
{
    public Guid Id { get; set; }

    // Emoji opcional mostrado ao lado do título.
    public string? Icone { get; set; }

    public string Titulo { get; set; } = string.Empty;
    public string Texto { get; set; } = string.Empty;
    public string TextoBotao { get; set; } = string.Empty;

    // Caminho do sistema (abre na mesma aba) ou URL https (abre em outra aba).
    public string LinkUrl { get; set; } = string.Empty;

    // Cor de fundo (#RRGGBB) para chamar atenção; o texto fica claro ou escuro conforme o
    // contraste. Null = visual neutro do bloco de publicidade.
    public string? Cor { get; set; }

    // Inativo = guardado, mas não exibido.
    public bool Ativo { get; set; }

    public DateTime CriadoEm { get; set; }
    public Guid? CriadoPorId { get; set; }
    public DateTime? AtualizadoEm { get; set; }
}
