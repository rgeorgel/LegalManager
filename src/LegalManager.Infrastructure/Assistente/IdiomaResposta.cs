using System.Text.RegularExpressions;

namespace LegalManager.Infrastructure.Assistente;

/// <summary>
/// Heurística para detectar trechos fora do português na resposta do assistente (alguns modelos
/// compatíveis misturam inglês ou caracteres de outros alfabetos). Quando detecta, o
/// <see cref="AssistenteService"/> pede ao modelo uma reescrita em português do Brasil.
/// </summary>
public static partial class IdiomaResposta
{
    // Palavras frequentes do inglês que NÃO existem como palavra em português (ficam de fora
    // "a", "as", "do", "no", "se", "me", "for" — de "ir/ser" —, "some", "more" etc.). Duas ocorrências já bastam para pedir a correção.
    private static readonly HashSet<string> PalavrasIngles = new(StringComparer.OrdinalIgnoreCase)
    {
        "the", "and", "of", "to", "is", "are", "was", "were", "be", "been", "being", "have", "has", "had",
        "you", "your", "you're", "yours", "we", "we'd", "we're", "we'll", "our", "they", "their", "it's",
        "this", "that", "these", "those", "with", "without", "from", "would", "should", "could",
        "will", "can't", "don't", "doesn't", "please", "further", "regarding", "hope", "kind", "regards",
        "dear", "thank", "thanks", "sincerely", "payment", "overdue", "contract", "inform", "however",
        "which", "there", "about", "any", "if", "or", "not", "an", "at", "by", "into", "also", "only",
        "here", "what", "when", "where", "why", "how", "all", "other", "such", "than",
    };

    public const int LimiteOcorrencias = 2;

    [GeneratedRegex(@"\[([^\]]*)\]\([^)]*\)")] private static partial Regex LinkMarkdown();
    [GeneratedRegex(@"https?://\S+|[\w.+-]+@[\w-]+\.[\w.]+|`[^`]*`")] private static partial Regex UrlEmailCodigo();
    [GeneratedRegex(@"[\p{IsCJKUnifiedIdeographs}\p{IsCJKUnifiedIdeographsExtensionA}\p{IsCJKSymbolsandPunctuation}\p{IsHalfwidthandFullwidthForms}\p{IsHiragana}\p{IsKatakana}\p{IsHangulSyllables}\p{IsCyrillic}\p{IsArabic}\p{IsHebrew}\p{IsThai}]")]
    private static partial Regex OutroAlfabeto();
    [GeneratedRegex(@"[A-Za-zÀ-ÿ]+(?:'[A-Za-z]+)?")] private static partial Regex Palavra();
    [GeneratedRegex(@"(?<=\S)[ \t]{2,}(?=\S)")] private static partial Regex EspacosRepetidos();

    /// <summary>True se o texto parece conter trechos em outro idioma.</summary>
    public static bool PareceTerOutroIdioma(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto)) return false;

        // Links, URLs, e-mails e código ficam de fora (podem ter termos em inglês legítimos).
        var limpo = LinkMarkdown().Replace(texto, "$1");
        limpo = UrlEmailCodigo().Replace(limpo, " ");

        if (OutroAlfabeto().IsMatch(limpo)) return true;

        var ocorrencias = 0;
        foreach (Match m in Palavra().Matches(limpo))
        {
            var p = m.Value.Replace('’', '\'');
            if (PalavrasIngles.Contains(p) && ++ocorrencias >= LimiteOcorrencias) return true;
        }
        return false;
    }

    /// <summary>
    /// Última barreira quando a reescrita não resolve: tira os caracteres de outros alfabetos
    /// (chinês, japonês, cirílico...) que ainda sobraram no texto.
    /// </summary>
    public static string RemoverOutroAlfabeto(string texto) =>
        EspacosRepetidos().Replace(OutroAlfabeto().Replace(texto, ""), " ");

    public static bool TemOutroAlfabeto(string? texto) =>
        !string.IsNullOrEmpty(texto) && OutroAlfabeto().IsMatch(texto);

    /// <summary>Regra de idioma enviada como instrução de sistema nas chamadas de IA.</summary>
    public const string InstrucaoSistema = """
        Você escreve para advogados e clientes de um escritório de advocacia brasileiro.
        Escreva SEMPRE e EXCLUSIVAMENTE em português do Brasil, com ortografia correta.
        Nunca use palavras, expressões ou caracteres de outros idiomas ou alfabetos (inglês, espanhol,
        chinês, japonês, coreano, cirílico etc.), nem mesmo no meio de uma palavra ou frase em português.
        Se pensar em um termo de outro idioma, escreva o equivalente em português.
        """;

    public const string PromptCorrecao = """
        Você é um revisor de textos em português do Brasil. O texto que o usuário enviar foi escrito por
        um assistente de um escritório de advocacia e contém trechos em outro idioma (por exemplo, inglês
        ou caracteres chineses) misturados com português.

        Reescreva o texto INTEIRO exclusivamente em português do Brasil:
        - Traduza todo trecho em outro idioma, com o mesmo sentido e tom (formal, quando for um e-mail ou mensagem a cliente).
        - Corrija palavras emendadas ou truncadas que tenham ficado sem sentido.
        - Mantenha EXATAMENTE: nomes de pessoas e empresas, números, valores, datas, números de processo e de contrato, e-mails, telefones e links.
        - Mantenha a formatação Markdown (títulos, listas, negrito, tabelas, links).
        - Não acrescente nem remova informações. Não comente a revisão.

        Responda apenas com o texto revisado.
        """;
}
