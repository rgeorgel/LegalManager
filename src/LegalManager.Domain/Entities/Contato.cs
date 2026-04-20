using LegalManager.Domain.Enums;

namespace LegalManager.Domain.Entities;

public class Contato
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public TipoPessoa Tipo { get; set; }
    public TipoContato TipoContato { get; set; }
    public string Nome { get; set; } = string.Empty;
    public string? CpfCnpj { get; set; }
    public string? Oab { get; set; }
    public string? Email { get; set; }
    public string? Telefone { get; set; }
    public string? Endereco { get; set; }
    public string? Cidade { get; set; }
    public string? Estado { get; set; }
    public string? Cep { get; set; }
    public DateTime? DataNascimento { get; set; }
    public string? Observacoes { get; set; }
    public bool NotificacaoHabilitada { get; set; }
    public bool Ativo { get; set; } = true;
    public DateTime CriadoEm { get; set; }
    public DateTime? AtualizadoEm { get; set; }

    public Tenant Tenant { get; set; } = null!;
    public ICollection<ContatoTag> Tags { get; set; } = new List<ContatoTag>();
    public ICollection<Atendimento> Atendimentos { get; set; } = new List<Atendimento>();
}
