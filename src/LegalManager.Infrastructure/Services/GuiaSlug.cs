using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace LegalManager.Infrastructure.Services;

// Slug da URL de um guia (/guias/{slug}): minúsculas, sem acento, palavras separadas por "-".
public static partial class GuiaSlug
{
    public const int TamanhoMaximo = 120;

    [GeneratedRegex("^[a-z0-9]+(?:-[a-z0-9]+)*$")]
    private static partial Regex Formato();

    [GeneratedRegex("[^a-z0-9]+")]
    private static partial Regex NaoAlfanumerico();

    public static bool Valido(string? slug) =>
        !string.IsNullOrEmpty(slug) && slug.Length <= TamanhoMaximo && Formato().IsMatch(slug);

    public static string Gerar(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto)) return string.Empty;

        var sb = new StringBuilder();
        foreach (var c in texto.Normalize(NormalizationForm.FormD))
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                sb.Append(c);

        var slug = NaoAlfanumerico().Replace(sb.ToString().ToLowerInvariant(), "-").Trim('-');
        if (slug.Length > TamanhoMaximo) slug = slug[..TamanhoMaximo].TrimEnd('-');
        return slug;
    }
}
