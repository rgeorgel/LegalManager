using System.ComponentModel.DataAnnotations;

namespace LegalManager.Application.DTOs.Guias;

// Super admin (inclui rascunhos).
public record GuiaAdminDto(
    Guid Id,
    string Titulo,
    string Slug,
    string Resumo,
    string Conteudo,
    string Categoria,
    string? CapaUrl,
    string? LinkUrl,
    string? LinkTexto,
    bool Publicado,
    DateTime? PublicadoEm,
    DateTime CriadoEm,
    DateTime? AtualizadoEm,
    int Curtidas
);

public record SalvarGuiaDto(
    [Required, MaxLength(160)] string Titulo,
    // Vazio = gerado a partir do título.
    [MaxLength(120)] string? Slug,
    [Required, MaxLength(300)] string Resumo,
    [Required, MaxLength(100_000)] string Conteudo,
    [Required, MaxLength(40)] string Categoria,
    [MaxLength(500)] string? CapaUrl,
    [MaxLength(300)] string? LinkUrl,
    [MaxLength(40)] string? LinkTexto,
    bool Publicado
);

public record PreviaGuiaDto([MaxLength(100_000)] string? Conteudo);

public record PreviaGuiaResultadoDto(string Html, int MinutosLeitura);

public record ImagemGuiaDto(string Url);

public record CategoriaGuiaDto(string Slug, string Nome);

// Portal: estado do botão "Útil" de um guia para o usuário logado.
public record CurtidaGuiaDto(bool Curtiu, int Total);
