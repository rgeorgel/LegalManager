namespace LegalManager.Application.DTOs.Processos;

public class AdicionarParteDto
{
    public Guid ContatoId { get; set; }
    public string TipoParte { get; set; } = string.Empty;
}
