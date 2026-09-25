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
    List<string>? Tags,
    bool ImportadoAutomaticamente = false
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
    DateTime CriadoEm,
    bool ImportadoAutomaticamente = false
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
    List<string> Tags,
    bool ImportadoAutomaticamente
);

public record ContatoFiltroDto(
    string? Busca,
    TipoContato? TipoContato,
    TipoPessoa? Tipo,
    string? Tag,
    bool? Ativo,
    bool? ImportadoAutomaticamente = null,
    int Page = 1,
    int PageSize = 20,
    string? SortBy = null,
    string? SortDir = null
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

// ── Perfil (resumo 360° + timeline) ─────────────────────────────────────

public record ContatoResumoDto(
    int ProcessosAtivos,
    decimal SaldoFinanceiro,
    TimelineItemDto? ProximoPrazo,
    DateTime? UltimoAtendimentoEm
);

public record TimelineItemDto(
    string Tipo, // Atendimento | Tarefa | Financeiro | Processo
    DateTime Data,
    string Titulo,
    string? Descricao,
    string? Link = null // URL relativa da entidade de origem (processo, tarefa, contrato de honorário), se houver
);

public record ContatoPerfilDto(
    ContatoResumoDto Resumo,
    List<TimelineItemDto> Timeline
);

// ── Vínculos entre contatos ──────────────────────────────────────────────

public record CreateContatoVinculoDto(
    [Required] Guid ContatoRelacionadoId,
    [Required] TipoVinculoContato Tipo,
    string? Observacao
);

public record ContatoVinculoResponseDto(
    Guid Id,
    Guid ContatoId,
    string ContatoNome,
    Guid ContatoRelacionadoId,
    string ContatoRelacionadoNome,
    TipoVinculoContato Tipo,
    string? Observacao,
    DateTime CriadoEm
);

// ── Detecção de duplicados ───────────────────────────────────────────────

public record ContatoDuplicadoItemDto(
    Guid Id,
    string Nome,
    TipoContato TipoContato,
    string? Email,
    string? Telefone,
    string? CpfCnpj
);

public record ContatoDuplicadoGrupoDto(
    string Criterio, // CpfCnpj | Email | Telefone | Nome
    string Valor,
    List<ContatoDuplicadoItemDto> Contatos
);

// ── Aniversariantes ───────────────────────────────────────────────────────

public record ContatoAniversarianteDto(
    Guid Id,
    string Nome,
    DateTime DataNascimento,
    string? Email,
    string? Telefone
);

// ── Filtros salvos ────────────────────────────────────────────────────────

public record CreateContatoFiltroSalvoDto(
    [Required, MaxLength(100)] string Nome,
    string? Busca,
    TipoContato? TipoContato,
    TipoPessoa? Tipo,
    string? Tag
);

public record ContatoFiltroSalvoResponseDto(
    Guid Id,
    string Nome,
    string? Busca,
    TipoContato? TipoContato,
    TipoPessoa? Tipo,
    string? Tag,
    DateTime CriadoEm
);
