using System.ComponentModel.DataAnnotations;
using LegalManager.Application.DTOs.Tenants;
using LegalManager.Domain.Enums;

namespace LegalManager.Application.DTOs.PortalCliente;

/// <param name="ProcessoId">
/// Processo que o cliente quer abrir (link compartilhado pelo escritório). Com o mesmo e-mail em
/// mais de um escritório, o login prefere o acesso que é parte desse processo.
/// </param>
public record LoginPortalDto(
    [Required] string Email,
    [Required] string Senha,
    Guid? ProcessoId = null
);

public record SolicitarRedefinicaoPortalDto([Required, EmailAddress] string Email);

public record RedefinirSenhaPortalDto([Required] string Token, [Required, MinLength(8)] string NovaSenha);

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
    string? Telefone,
    TenantThemeDto? Tema,
    // Nome exibido no portal ao lado (ou no lugar) da logo do escritório.
    string? NomeEscritorio = null
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

public record MeuResumoDto(
    Guid Id,
    string Conteudo,
    string GeradoPorNome,
    DateTime GeradoEm
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
    DateTime? UltimoAcessoEm,
    bool TemTokenConvite
);
