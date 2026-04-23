using LegalManager.Domain.Enums;

namespace LegalManager.Application.DTOs.IA;

public record CreditoAIResponseDto(
    Guid Id,
    TipoCreditoAI Tipo,
    int QuantidadeTotal,
    int QuantidadeUsada,
    int QuantidadeDisponivel,
    OrigemCreditoAI Origem,
    DateTime? ExpiraEm
);

public record CreditosTotaisDto(
    List<CreditoAIResponseDto> Creditos,
    int TotalDisponivelGeral
);