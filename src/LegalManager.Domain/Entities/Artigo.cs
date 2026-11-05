namespace LegalManager.Domain.Entities;

// Guia/artigo da Central Causify (/guias): conteúdo sobre as funcionalidades do sistema
// escrito pelo super admin. Entidade global (não pertence a um tenant). A leitura é pública
// (páginas renderizadas no servidor, para SEO — ver GuiasController); curtir exige login.
public class Artigo
{
    public Guid Id { get; set; }

    public string Titulo { get; set; } = string.Empty;

    // Parte final da URL (/guias/{slug}). Única; trocar o slug quebra links já compartilhados.
    public string Slug { get; set; } = string.Empty;

    // Uma ou duas frases: aparece no card da listagem e vira a meta description.
    public string Resumo { get; set; } = string.Empty;

    // Markdown (renderizado no servidor por GuiaMarkdown, sem HTML bruto).
    public string Conteudo { get; set; } = string.Empty;

    // Slug de uma das categorias fixas de CategoriasArtigo.
    public string Categoria { get; set; } = string.Empty;

    // Imagem de capa (topo do artigo e og:image): caminho do site ("/guias/img/...") ou https.
    public string? CapaUrl { get; set; }

    // Botão "Abrir no Causify" ao fim do artigo: caminho interno do portal (ex: /pages/processos.html).
    public string? LinkUrl { get; set; }
    public string? LinkTexto { get; set; }

    // Rascunho = false. PublicadoEm é preenchida na primeira publicação e define a ordem;
    // despublicar e republicar não muda a data.
    public bool Publicado { get; set; }
    public DateTime? PublicadoEm { get; set; }

    public DateTime CriadoEm { get; set; }
    public Guid? CriadoPorId { get; set; }
    public DateTime? AtualizadoEm { get; set; }

    public ICollection<ArtigoCurtida> Curtidas { get; set; } = new List<ArtigoCurtida>();
}

// Uma curtida por usuário do portal em cada artigo (chave composta).
public class ArtigoCurtida
{
    public Guid ArtigoId { get; set; }
    public Guid UsuarioId { get; set; }
    public DateTime CriadoEm { get; set; }

    public Artigo Artigo { get; set; } = null!;
}
