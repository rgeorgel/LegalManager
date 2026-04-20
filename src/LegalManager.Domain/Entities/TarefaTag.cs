namespace LegalManager.Domain.Entities;

public class TarefaTag
{
    public Guid Id { get; set; }
    public Guid TarefaId { get; set; }
    public string Tag { get; set; } = string.Empty;

    public Tarefa Tarefa { get; set; } = null!;
}
