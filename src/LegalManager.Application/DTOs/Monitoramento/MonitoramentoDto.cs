namespace LegalManager.Application.DTOs.Monitoramento;

public record MonitoramentoResultDto(
    Guid ProcessoId,
    string NumeroCNJ,
    bool Sucesso,
    int NovosAndamentos,
    string? Erro,
    DateTime ExecutadoEm
);
