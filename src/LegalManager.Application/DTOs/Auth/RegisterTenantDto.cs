using System.ComponentModel.DataAnnotations;
using LegalManager.Domain.Enums;

namespace LegalManager.Application.DTOs.Auth;

public record RegisterTenantDto(
    [Required, MaxLength(200)] string NomeEscritorio,
    string? Cnpj,
    [Required, MaxLength(200)] string NomeAdmin,
    [Required, EmailAddress] string Email,
    [Required, MinLength(8)] string Senha,
    PlanoTipo Plano = PlanoTipo.Free
);
