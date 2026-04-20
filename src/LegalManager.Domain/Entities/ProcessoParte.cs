using LegalManager.Domain.Enums;

namespace LegalManager.Domain.Entities;

public class ProcessoParte
{
    public Guid Id { get; set; }
    public Guid ProcessoId { get; set; }
    public Guid ContatoId { get; set; }
    public TipoParteProcesso TipoParte { get; set; }

    public Processo Processo { get; set; } = null!;
    public Contato Contato { get; set; } = null!;
}
