namespace LegalManager.Domain.Entities;

// Grupo de usuários (advogados/equipe do escritório — Usuario) montado à mão pelo super
// admin para segmentar perguntas de pesquisa além dos critérios automáticos (plano,
// pagante, trial) — ex.: "beta testers", "clientes que pediram feature X". Entidade
// global, como Pergunta: não pertence a um tenant específico, já que reúne usuários de
// tenants potencialmente diferentes.
public class GrupoPergunta
{
    public Guid Id { get; set; }
    public string Nome { get; set; } = string.Empty;
    public string? Descricao { get; set; }

    public DateTime CriadoEm { get; set; }
    public Guid CriadoPorId { get; set; }
    public DateTime? AtualizadoEm { get; set; }
}
