using LegalManager.Domain.Enums;

namespace LegalManager.Domain.Entities;

// Novidade do produto ("O que há de novo"), cadastrada pelo super admin e mostrada a todos
// os usuários do portal admin no painel ✨ do cabeçalho. Entidade global (não pertence a um
// tenant). O "não lida" é por usuário, mas sem tabela de leitura: compara PublicadaEm com
// Usuario.NovidadesVistasEm (ver NovidadesController).
public class Novidade
{
    public Guid Id { get; set; }

    public string Titulo { get; set; } = string.Empty;

    // Markdown simples (renderizado por /js/markdown.js).
    public string Descricao { get; set; } = string.Empty;

    // Imagem/GIF opcional: caminho local ("/images/...") ou URL https.
    public string? ImagemUrl { get; set; }

    // Botão "Experimentar": caminho interno do portal (ex: "/pages/processos.html").
    public string? LinkUrl { get; set; }
    public string? LinkTexto { get; set; }

    // Só informativo (selo "Plus"/"Pro" no painel): a novidade aparece para todos os planos,
    // o que também serve de vitrine para quem ainda não tem acesso.
    public PlanoTipo? PlanoMinimo { get; set; }

    // Destaque: além do painel, abre um modal no dashboard (uma vez por usuário, só para
    // quem tem o PlanoMinimo). Ver NovidadesController.Destaque.
    public bool Destaque { get; set; }

    // Tour guiado (id em /js/tours-config.js) aberto pelo botão "Me mostra". Os tours vivem
    // no frontend, então o id não é validado aqui.
    public string? TourId { get; set; }

    // Rascunho = false. PublicadaEm é preenchida na primeira publicação e define a ordem e o
    // "não lida"; despublicar e republicar não muda a data (não volta a acender o selo).
    public bool Publicada { get; set; }
    public DateTime? PublicadaEm { get; set; }

    public DateTime CriadoEm { get; set; }
    public Guid? CriadoPorId { get; set; }
    public DateTime? AtualizadoEm { get; set; }
}
