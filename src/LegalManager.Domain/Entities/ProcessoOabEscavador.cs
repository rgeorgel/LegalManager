namespace LegalManager.Domain.Entities;

/// <summary>
/// Processo que a busca por OAB no Escavador (paga, por página) já devolveu para uma OAB.
/// Global, sem tenant: sobrevive à exclusão do escritório, para que a mesma OAB importada
/// de novo — inclusive por quem se cadastrou outra vez — reaproveite o que já foi pago.
/// O JSON de cada processo fica em <see cref="ProcessoImportacaoCache"/>.
/// </summary>
public class ProcessoOabEscavador
{
    public Guid Id { get; set; }
    public string NumeroOab { get; set; } = string.Empty;
    public string Uf { get; set; } = string.Empty;
    public string NumeroCNJ { get; set; } = string.Empty;
    public DateTime CriadoEm { get; set; }
}
