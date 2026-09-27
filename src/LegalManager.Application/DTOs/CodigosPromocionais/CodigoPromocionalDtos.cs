using System.ComponentModel.DataAnnotations;
using LegalManager.Domain.Enums;

namespace LegalManager.Application.DTOs.CodigosPromocionais;

public record CodigoPromocionalDto(
    Guid Id,
    string Codigo,
    string? Descricao,
    PlanoTipo? Plano,
    int DiasGratuitos,
    int? DescontoPercentual,
    int? DescontoMeses,
    int? MaxUsos,
    int Usos,
    DateOnly? ValidoAte,
    bool Ativo,
    DateTime CriadoEm
);

public record SalvarCodigoPromocionalDto(
    [Required, MaxLength(50), RegularExpression("^[A-Za-z0-9_-]+$",
        ErrorMessage = "Use apenas letras, números, hífen e sublinhado.")] string Codigo,
    [MaxLength(200)] string? Descricao,
    PlanoTipo? Plano,
    [Range(0, 365)] int DiasGratuitos,
    [Range(1, 99)] int? DescontoPercentual,
    [Range(1, 36)] int? DescontoMeses,
    [Range(1, 1_000_000)] int? MaxUsos,
    DateOnly? ValidoAte,
    bool Ativo = true
);

public record CodigoPromocionalUsoDto(
    Guid TenantId, string NomeEscritorio, string? EmailAdmin, DateTime CriadoEm,
    DateTime? PlanoExpiraEm, DateTime? DescontoUsadoEm, bool NoCadastro);

// Resposta pública (cadastro) — sem contagem de usos nem limite.
public record CodigoPromocionalPublicoDto(
    string Codigo, string? Descricao, PlanoTipo? Plano, int DiasGratuitos,
    int? DescontoPercentual, int? DescontoMeses);
