using System.Net;
using System.Text.RegularExpressions;

namespace LegalManager.Infrastructure.Services;

/// <summary>
/// Converte o HTML que os diários e o Escavador mandam (tags, entidades como <c>&amp;gt;</c>, às vezes
/// escapado duas vezes) em texto corrido. Única regra para todas as entradas de publicação/andamento,
/// e para a limpeza das já gravadas (<see cref="LimpezaHtmlPublicacoes"/>).
/// </summary>
public static partial class TextoHtml
{
    // Conteúdo de script/style e comentários some inteiro; o resto das tags vira espaço.
    [GeneratedRegex(@"<\s*(script|style)\b[^>]*>.*?<\s*/\s*\1\s*>|<!--.*?-->", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex BlocoInvisivel();

    // Só o que parece tag (letra ou "/" logo após o "<") — "valor < 10" fica como está.
    [GeneratedRegex(@"<\s*/?\s*[a-zA-Z][^<>]*>")]
    private static partial Regex Tag();

    [GeneratedRegex(@"&(#\d+|#x[0-9a-fA-F]+|[a-zA-Z]{2,8});")]
    private static partial Regex Entidade();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Espacos();

    // "</td><td>: valor" deixaria "REQUERENTE : valor".
    [GeneratedRegex(@"\s+([:;,.!?)\]])")]
    private static partial Regex EspacoAntesDePontuacao();

    /// <summary>Texto sem HTML, numa linha; null quando não sobra nada.</summary>
    public static string? ParaTexto(string? html)
    {
        if (string.IsNullOrWhiteSpace(html)) return null;
        var texto = html;
        // Entidades primeiro (HTML escapado vira tag e sai a seguir); até 3 voltas para "&amp;gt;".
        for (var i = 0; i < 3 && Entidade().IsMatch(texto); i++)
            texto = WebUtility.HtmlDecode(texto);
        texto = BlocoInvisivel().Replace(texto, " ");
        texto = Tag().Replace(texto, " ");
        texto = Espacos().Replace(texto, " ");
        texto = EspacoAntesDePontuacao().Replace(texto, "$1").Trim();
        return texto.Length == 0 ? null : texto;
    }

    /// <summary>Se o texto tem algo para <see cref="ParaTexto"/> limpar (tag ou entidade).</summary>
    public static bool TemHtml(string? texto) =>
        !string.IsNullOrEmpty(texto) && (Tag().IsMatch(texto) || Entidade().IsMatch(texto));
}
