using System.ComponentModel.DataAnnotations;

namespace LegalManager.Application.DTOs.Grupos;

public record GrupoDto(Guid Id, string Nome, string? Descricao, int TotalMembros, DateTime CriadoEm);

public record SalvarGrupoDto(
    [Required, MaxLength(150)] string Nome,
    [MaxLength(500)] string? Descricao
);

public record GrupoMembroDto(Guid UsuarioId, string Nome, string? Email, string TenantNome);

public record AdicionarMembrosDto([Required] List<Guid> UsuarioIds);
