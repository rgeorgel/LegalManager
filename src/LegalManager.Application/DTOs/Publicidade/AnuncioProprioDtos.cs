using System.ComponentModel.DataAnnotations;

namespace LegalManager.Application.DTOs.Publicidade;

// Portal (plano Free): só os ativos.
public record AnuncioProprioDto(
    Guid Id,
    string? Icone,
    string Titulo,
    string Texto,
    string TextoBotao,
    string LinkUrl,
    string? Cor
);

// Super admin (inclui os inativos). Impressões e cliques somados no período pedido.
public record AnuncioProprioAdminDto(
    Guid Id,
    string? Icone,
    string Titulo,
    string Texto,
    string TextoBotao,
    string LinkUrl,
    string? Cor,
    bool Ativo,
    DateTime CriadoEm,
    int Impressoes = 0,
    int Cliques = 0
);

public record SalvarAnuncioProprioDto(
    [MaxLength(16)] string? Icone,
    [Required, MaxLength(80)] string Titulo,
    [Required, MaxLength(200)] string Texto,
    [Required, MaxLength(30)] string TextoBotao,
    [Required, MaxLength(300)] string LinkUrl,
    bool Ativo,
    [MaxLength(7)] string? Cor = null
);

// Mistura entre anúncios próprios e Adsterra (ver ConfiguracaoPublicidade).
public record ConfiguracaoPublicidadeDto(int PercentualProprios, DateTime? AtualizadoEm);

public record SalvarConfiguracaoPublicidadeDto([Range(0, 100)] int PercentualProprios);

// Portal: anúncios próprios que apareceram na tela / foram clicados desde o último envio.
// Um id repetido conta mais de uma vez (o mesmo anúncio em dois blocos).
public record RegistrarEventosAnunciosDto(List<Guid>? Impressoes, List<Guid>? Cliques);
