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

// Super admin (inclui os inativos).
public record AnuncioProprioAdminDto(
    Guid Id,
    string? Icone,
    string Titulo,
    string Texto,
    string TextoBotao,
    string LinkUrl,
    string? Cor,
    bool Ativo,
    DateTime CriadoEm
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
