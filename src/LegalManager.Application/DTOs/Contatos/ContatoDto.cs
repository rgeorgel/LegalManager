using System.ComponentModel.DataAnnotations;
using LegalManager.Domain.Enums;

namespace LegalManager.Application.DTOs.Contatos;

public record CreateContatoDto(
    [Required] TipoPessoa Tipo,
    [Required] TipoContato TipoContato,
    [Required, MaxLength(300)] string Nome,
    string? CpfCnpj,
    string? Oab,
    [EmailAddress] string? Email,
    string? Telefone,
    string? Endereco,
    string? Cidade,
    string? Estado,
    string? Cep,
    DateTime? DataNascimento,
    string? Observacoes,
    bool NotificacaoHabilitada,
    List<string>? Tags
);

public record UpdateContatoDto(
    [Required] TipoPessoa Tipo,
    [Required] TipoContato TipoContato,
    [Required, MaxLength(300)] string Nome,
    string? CpfCnpj,
    string? Oab,
    [EmailAddress] string? Email,
    string? Telefone,
    string? Endereco,
    string? Cidade,
    string? Estado,
    string? Cep,
    DateTime? DataNascimento,
    string? Observacoes,
    bool NotificacaoHabilitada,
    List<string>? Tags
);

public record ContatoResponseDto(
    Guid Id,
    TipoPessoa Tipo,
    TipoContato TipoContato,
    string Nome,
    string? CpfCnpj,
    string? Oab,
    string? Email,
    string? Telefone,
    string? Endereco,
    string? Cidade,
    string? Estado,
    string? Cep,
    DateTime? DataNascimento,
    string? Observacoes,
    bool NotificacaoHabilitada,
    bool Ativo,
    List<string> Tags,
    DateTime CriadoEm
);

public record ContatoListItemDto(
    Guid Id,
    TipoPessoa Tipo,
    TipoContato TipoContato,
    string Nome,
    string? CpfCnpj,
    string? Email,
    string? Telefone,
    bool Ativo,
    List<string> Tags
);

public record ContatoFiltroDto(
    string? Busca,
    TipoContato? TipoContato,
    TipoPessoa? Tipo,
    string? Tag,
    bool? Ativo,
    int Page = 1,
    int PageSize = 20
);

public record CreateAtendimentoDto(
    [Required] string Descricao,
    [Required] DateTime Data
);

public record AtendimentoResponseDto(
    Guid Id,
    string Descricao,
    DateTime Data,
    Guid UsuarioId,
    string NomeUsuario,
    DateTime CriadoEm
);

public record PagedResultDto<T>(
    IEnumerable<T> Items,
    int Total,
    int Page,
    int PageSize,
    int TotalPages
);
