using System.ComponentModel.DataAnnotations;

namespace LegalManager.Application.DTOs.Imagens;

public record PastaImagemDto(Guid Id, string Nome, int Quantidade);

public record CriarPastaImagemDto([Required, MaxLength(60)] string Nome);

// Onde a imagem aparece (guia ou novidade) — avisa antes de renomear/excluir.
public record UsoImagemDto(string Tipo, string Titulo, string? Link);

public record ImagemPublicaDto(
    Guid Id,
    Guid PastaId,
    string Pasta,
    string Nome,
    string Url,
    string ContentType,
    long Tamanho,
    DateTime CriadoEm,
    DateTime AtualizadoEm,
    IReadOnlyList<UsoImagemDto> Usos
);

public record RenomearImagemDto([Required, MaxLength(130)] string Nome);

// 409 do envio: já existe uma imagem com esse nome na pasta.
public record ConflitoImagemDto(string Message, string NomeExistente, string NomeSugerido);
