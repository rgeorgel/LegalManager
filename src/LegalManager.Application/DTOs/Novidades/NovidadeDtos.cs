using System.ComponentModel.DataAnnotations;
using LegalManager.Domain.Enums;

namespace LegalManager.Application.DTOs.Novidades;

// Super admin (inclui rascunhos).
public record NovidadeAdminDto(
    Guid Id,
    string Titulo,
    string Descricao,
    string? ImagemUrl,
    string? LinkUrl,
    string? LinkTexto,
    PlanoTipo? PlanoMinimo,
    bool Destaque,
    string? TourId,
    bool Publicada,
    DateTime? PublicadaEm,
    DateTime CriadoEm
);

public record SalvarNovidadeDto(
    [Required, MaxLength(120)] string Titulo,
    [Required, MaxLength(4000)] string Descricao,
    [MaxLength(500)] string? ImagemUrl,
    [MaxLength(300)] string? LinkUrl,
    [MaxLength(40)] string? LinkTexto,
    PlanoTipo? PlanoMinimo,
    bool Publicada,
    bool Destaque = false,
    [MaxLength(50), RegularExpression("^[a-z0-9-]+$")] string? TourId = null
);

// Portal admin — Nova = publicada depois da última vez que o usuário abriu o painel.
public record NovidadeDto(
    Guid Id,
    string Titulo,
    string Descricao,
    string? ImagemUrl,
    string? LinkUrl,
    string? LinkTexto,
    PlanoTipo? PlanoMinimo,
    string? TourId,
    DateTime PublicadaEm,
    bool Nova
);
