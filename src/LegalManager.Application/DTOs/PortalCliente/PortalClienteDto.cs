using System.ComponentModel.DataAnnotations;
using LegalManager.Domain.Enums;

namespace LegalManager.Application.DTOs.PortalCliente;

public record LoginPortalDto(
    [Required] string Email,
    [Required] string Senha
);

public record PortalAuthResponseDto(
    string AccessToken,
    DateTime ExpiresAt,
    ClientePerfilDto Perfil
);

public record ClientePerfilDto(
    Guid AcessoId,
    Guid ContatoId,
    string Nome,
    string Email,
    string? Telefone
);

public record MeuProcessoDto(
    Guid Id,
    string NumeroCNJ,
    string? Tribunal,
    string? Comarca,
    AreaDireito AreaDireito,
    FaseProcessual Fase,
    StatusProcesso Status,
    TipoParteProcesso TipoParte,
    int TotalAndamentos,
    DateTime CriadoEm
);

public record MeuAndamentoDto(
    Guid Id,
    DateTime Data,
    TipoAndamento Tipo,
    string Descricao,
    string? DescricaoTraduzidaIA,
    FonteAndamento Fonte,
    DateTime CriadoEm
);

public record CriarAcessoPortalDto(
    [Required][MaxLength(256)] string Email,
    [Required][MinLength(8)] string Senha
);

public record AcessoPortalInfoDto(
    Guid Id,
    Guid ContatoId,
    string Email,
    bool Ativo,
    DateTime CriadoEm,
    DateTime? UltimoAcessoEm
);
