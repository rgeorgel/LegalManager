namespace LegalManager.Domain.Entities;

/// <summary>
/// Processo marcado como favorito por um usuário. É por usuário (não do escritório): cada
/// advogado tem os seus. Favoritos aparecem primeiro na listagem e num bloco do dashboard.
/// </summary>
public class ProcessoFavorito
{
    public Guid UsuarioId { get; set; }
    public Guid ProcessoId { get; set; }
    public DateTime CriadoEm { get; set; }

    public Usuario Usuario { get; set; } = null!;
    public Processo Processo { get; set; } = null!;
}
