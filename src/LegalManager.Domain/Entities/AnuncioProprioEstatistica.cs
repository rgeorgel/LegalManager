namespace LegalManager.Domain.Entities;

// Desempenho de um anúncio próprio num dia (data de Brasília): quantas vezes apareceu na tela e
// quantas vezes foi clicado. Uma linha por anúncio e dia, somada pelo portal (POST
// /api/publicidade/proprios/eventos) e lida na tela Anúncios próprios do super admin.
public class AnuncioProprioEstatistica
{
    public Guid AnuncioProprioId { get; set; }
    public DateOnly Dia { get; set; }
    public int Impressoes { get; set; }
    public int Cliques { get; set; }
}
