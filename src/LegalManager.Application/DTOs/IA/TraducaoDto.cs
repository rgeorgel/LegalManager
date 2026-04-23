namespace LegalManager.Application.DTOs.IA;

public record TraduzirAndamentoDto(
    Guid AndamentoId,
    Guid? ClienteId,
    bool Enviaremail,
    bool RevisaoPrevia
);

public record TraducaoResponseDto(
    Guid Id,
    Guid AndamentoId,
    string TextoOriginal,
    string TextoTraduzido,
    bool EnviadoAoCliente,
    bool RevisadoPreviamente,
    DateTime CriadoEm
);

public record EnviarTraducaoDto(
    bool ComRevisaoPrevia
);