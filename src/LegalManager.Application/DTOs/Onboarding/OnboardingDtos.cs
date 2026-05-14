using System.ComponentModel.DataAnnotations;

namespace LegalManager.Application.DTOs.Onboarding;

public record OnboardingStatusDto(bool Completo);

public record BuscarPorOabDto(
    [Required] string NumeroOAB,
    [Required] string Uf
);

public record ProcessoOabPreviewDto(
    string NumeroCNJ,
    string Tribunal,
    string? Vara,
    string? Classe,
    DateTime? DataAjuizamento,
    string? Grau,
    bool JaCadastrado = false,
    string? Codigo = null,
    string? Foro = null
);

public record ImportarProcessosDto(
    [Required, MinLength(1)] List<ImportarProcessoItem> Processos
);

public record ImportarProcessoItem(
    string NumeroCNJ,
    string? Tribunal = null,
    string? Codigo = null,
    string? Foro = null
);

public record ImportarResultadoDto(
    int Importados,
    int Erros,
    List<string> Mensagens
);
