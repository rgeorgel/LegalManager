using System.Text.RegularExpressions;

namespace LegalManager.Infrastructure.Services;

// Nomes da biblioteca de imagens (/imagens/{pasta}/{nome}): pasta e nome são slugs
// (minúsculas, sem acento, hífens); o nome leva a extensão da imagem.
public static partial class NomeImagemPublica
{
    public const int TamanhoMaximoPasta = 60;
    public const int TamanhoMaximoBase = 120;

    [GeneratedRegex(@"^[a-z0-9]+(?:-[a-z0-9]+)*\.(png|jpg|jpeg|gif|webp)$")]
    private static partial Regex FormatoNome();

    public static bool PastaValida(string? pasta) =>
        GuiaSlug.Valido(pasta) && pasta!.Length <= TamanhoMaximoPasta;

    public static bool NomeValido(string? nome) =>
        nome != null && nome.Length <= TamanhoMaximoBase + 5 && FormatoNome().IsMatch(nome);

    public static string Pasta(string? texto)
    {
        var slug = GuiaSlug.Gerar(texto);
        return slug.Length > TamanhoMaximoPasta ? slug[..TamanhoMaximoPasta].TrimEnd('-') : slug;
    }

    /// <summary>
    /// "Tela do Portal (1).PNG" + ".png" → "tela-do-portal-1.png". A extensão vem da validação do
    /// arquivo (não do nome digitado); um nome que vira vazio usa "imagem".
    /// </summary>
    public static string Gerar(string? nomeDesejado, string extensao)
    {
        var semExtensao = Path.GetFileNameWithoutExtension(nomeDesejado ?? string.Empty);
        var base_ = GuiaSlug.Gerar(semExtensao);
        if (base_.Length > TamanhoMaximoBase) base_ = base_[..TamanhoMaximoBase].TrimEnd('-');
        if (base_.Length == 0) base_ = "imagem";
        return base_ + extensao.ToLowerInvariant();
    }

    /// <summary>Próximo nome livre: "tela.png" → "tela-2.png" → "tela-3.png"...</summary>
    public static string ProximoLivre(string nome, IReadOnlySet<string> existentes)
    {
        if (!existentes.Contains(nome)) return nome;

        var ext = Path.GetExtension(nome);
        var base_ = Path.GetFileNameWithoutExtension(nome);
        for (var n = 2; ; n++)
        {
            var sufixo = "-" + n;
            var raiz = base_.Length + sufixo.Length > TamanhoMaximoBase ? base_[..(TamanhoMaximoBase - sufixo.Length)].TrimEnd('-') : base_;
            var candidato = raiz + sufixo + ext;
            if (!existentes.Contains(candidato)) return candidato;
        }
    }

    public static string Url(string pasta, string nome) => $"/imagens/{pasta}/{nome}";
}
