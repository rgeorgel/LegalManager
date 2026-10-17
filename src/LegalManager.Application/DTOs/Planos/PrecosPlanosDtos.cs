using System.ComponentModel.DataAnnotations;
using LegalManager.Domain.Enums;

namespace LegalManager.Application.DTOs.Planos;

public record PrecoPlanoDto(PlanoTipo Plano, decimal ValorMensal, DateTime? AtualizadoEm);

public record SalvarPrecoPlanoDto(PlanoTipo Plano, [Range(1, 100_000)] decimal ValorMensal);
