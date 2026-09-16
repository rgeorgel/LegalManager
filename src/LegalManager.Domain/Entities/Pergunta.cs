using LegalManager.Domain.Enums;

namespace LegalManager.Domain.Entities;

// Pergunta de pesquisa (interesses, satisfação, recursos que sentem falta) cadastrada
// pelo super admin e mostrada aos usuários do sistema — no login (modal) e, enquanto não
// respondida, no menu flutuante lateral (mesmo padrão do tutorial guiado / tarefas, ver
// tour.js/tarefas-widget.js). Entidade global (não tenant-scoped): quem a gerencia é o
// super admin, não um tenant específico.
public class Pergunta
{
    public Guid Id { get; set; }
    public string Texto { get; set; } = string.Empty;
    public string? Descricao { get; set; }
    public TipoRespostaPergunta TipoResposta { get; set; }

    // JSON com a lista de opções (só usado quando TipoResposta == EscolhaUnica), ex:
    // ["Peças com IA","Integração com WhatsApp","Relatórios avançados"].
    public string? OpcoesJson { get; set; }

    public PublicoPergunta Publico { get; set; }

    // Só relevante quando Publico == Advogados — ver comentário em SegmentoPergunta.
    public SegmentoPergunta Segmento { get; set; }
    public PlanoTipo? PlanoAlvo { get; set; } // usado quando Segmento == PlanoEspecifico
    public Guid? GrupoId { get; set; } // usado quando Segmento == GrupoEspecifico (ver GrupoPergunta)

    public bool Ativa { get; set; } = true;
    public int Ordem { get; set; }

    public DateTime CriadoEm { get; set; }
    public Guid CriadoPorId { get; set; }
    public DateTime? AtualizadoEm { get; set; }
}
