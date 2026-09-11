using LegalManager.Domain.Enums;
using Microsoft.AspNetCore.Identity;

namespace LegalManager.Domain.Entities;

public class Usuario : IdentityUser<Guid>
{
    public Guid TenantId { get; set; }
    public string Nome { get; set; } = string.Empty;
    public PerfilUsuario Perfil { get; set; }
    public bool Ativo { get; set; } = true;
    public DateTime CriadoEm { get; set; }
    public bool OnboardingImportacaoCompleto { get; set; } = false;
    public string? OabImportadaNumero { get; set; }
    public string? OabImportadaUf { get; set; }
    public DateTime? UltimoAcessoEm { get; set; }

    // Array JSON com os ids dos tutoriais guiados (product tour) já concluídos
    // pelo usuário, ex: ["geral","processos"]. Um único campo em vez de uma
    // coluna por tour evita migration a cada novo tutorial adicionado.
    public string? ToursConcluidos { get; set; }

    public string? OrigemCadastro { get; set; }
    public string? UtmSource { get; set; }
    public string? UtmMedium { get; set; }
    public string? UtmCampaign { get; set; }
    public string? Referrer { get; set; }
    public string? LandingPage { get; set; }
    public string? Fbclid { get; set; }
    public string? Gclid { get; set; }

    public Tenant Tenant { get; set; } = null!;
    public ICollection<RefreshToken> RefreshTokens { get; set; } = new List<RefreshToken>();
}
