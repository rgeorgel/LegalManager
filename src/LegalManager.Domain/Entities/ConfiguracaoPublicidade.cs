namespace LegalManager.Domain.Entities;

// Ajustes globais da publicidade do plano Free, editados no super admin (tela Anúncios próprios).
// Linha única (Id = IdUnico). Ver PublicidadeController e /js/publicidade.js.
public class ConfiguracaoPublicidade
{
    public const int IdUnico = 1;

    public int Id { get; set; }

    // % dos espaços de publicidade que mostram direto um anúncio próprio (0–100). Os demais tentam
    // o Adsterra e só recebem um anúncio próprio se ele vier vazio.
    public int PercentualProprios { get; set; }

    public DateTime? AtualizadoEm { get; set; }
    public Guid? AtualizadoPorId { get; set; }
}
