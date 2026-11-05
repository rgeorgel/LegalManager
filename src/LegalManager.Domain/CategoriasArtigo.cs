namespace LegalManager.Domain;

// Categorias dos guias da Central Causify. Lista fixa no código: viram filtros na listagem
// pública (/guias?categoria=slug) e o slug é o que fica gravado em Artigo.Categoria —
// renomear o Nome é seguro; trocar o Slug exige migrar os artigos.
public static class CategoriasArtigo
{
    public sealed record Categoria(string Slug, string Nome);

    public static readonly IReadOnlyList<Categoria> Todas =
    [
        new("primeiros-passos", "Primeiros passos"),
        new("processos", "Processos"),
        new("prazos-e-tarefas", "Prazos e tarefas"),
        new("publicacoes", "Publicações"),
        new("honorarios", "Honorários"),
        new("financeiro", "Financeiro"),
        new("portal-do-cliente", "Portal do cliente"),
        new("documentos", "Documentos"),
        new("inteligencia-artificial", "Inteligência artificial"),
        new("novidades", "Novidades"),
    ];

    public static Categoria? Buscar(string? slug) =>
        string.IsNullOrWhiteSpace(slug) ? null : Todas.FirstOrDefault(c => c.Slug == slug);

    public static string NomeDe(string slug) => Buscar(slug)?.Nome ?? slug;
}
