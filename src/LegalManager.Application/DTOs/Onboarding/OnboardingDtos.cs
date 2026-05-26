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
    string? Foro = null,
    // Fonte identifica a origem: "datajud", "esaj" ou "escavador"
    string Fonte = "datajud",
    string? SiglaTribunal = null,
    string? Comarca = null,
    string? Assuntos = null
);

public record ImportarProcessosDto(
    [Required, MinLength(1)] List<ImportarProcessoItem> Processos
);

public record ImportarProcessoItem(
    string NumeroCNJ,
    string? Tribunal = null,
    string? Codigo = null,
    string? Foro = null,
    string Fonte = "datajud",
    // Campos extras para itens de origem Escavador
    string? SiglaTribunal = null,
    string? NomeTribunal = null,
    string? Vara = null,
    string? Comarca = null,
    string? Classe = null,
    string? Assuntos = null,
    DateTime? DataAjuizamento = null
);

public record ImportarResultadoDto(
    int Importados,
    int Erros,
    List<string> Mensagens
);
