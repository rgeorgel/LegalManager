using System.Text.RegularExpressions;

namespace LegalManager.Infrastructure.Services;

/// <summary>
/// Cor que o usuário escolhe para agrupar tarefas e prazos (aparece na listagem e na agenda).
/// Gravada como "#rrggbb" em minúsculas; a paleta oferecida fica no frontend (js/cores-tarefa.js).
/// </summary>
public static partial class CoresTarefa
{
    /// <summary>Valor do filtro para "tarefas sem cor".</summary>
    public const string SemCor = "sem";

    /// <summary>Cor da tarefa sem cor na agenda.</summary>
    public const string PadraoAgenda = "#6b7280";

    /// <summary>Cor da ocorrência prevista (série recorrente) sem cor na agenda.</summary>
    public const string PadraoPrevista = "#9ca3af";

    [GeneratedRegex("^#[0-9a-f]{6}$")]
    private static partial Regex Hex();

    /// <summary>Null ou vazio = sem cor; "#rrggbb" (qualquer caixa) é aceito; o resto é erro.</summary>
    public static string? Normalizar(string? cor)
    {
        if (string.IsNullOrWhiteSpace(cor)) return null;
        return NormalizarOuNull(cor) ?? throw new ArgumentException("Cor inválida: use o formato #rrggbb.");
    }

    public static string? NormalizarOuNull(string? cor)
    {
        var c = cor?.Trim().ToLowerInvariant();
        return c is not null && Hex().IsMatch(c) ? c : null;
    }
}
