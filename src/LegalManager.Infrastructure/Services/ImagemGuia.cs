using System.Text;
using System.Text.RegularExpressions;

namespace LegalManager.Infrastructure.Services;

/// <summary>
/// Imagens dos guias (capa e corpo). Ficam no storage em "guias/{guid}{ext}" e são servidas
/// publicamente em /guias/img/{guid}{ext} (ver GuiasController). Como no upload de documentos,
/// o tipo vem da extensão e precisa bater com os primeiros bytes — o Content-Type enviado pelo
/// navegador é ignorado (um SVG/HTML disfarçado de imagem rodaria script na nossa origem).
/// </summary>
public static partial class ImagemGuia
{
    public const long TamanhoMaximo = 5 * 1024 * 1024;
    public const string PrefixoUrl = "/guias/img/";

    private sealed record Tipo(string ContentType, Func<byte[], bool> Confere);

    private static readonly Dictionary<string, Tipo> Tipos = new(StringComparer.OrdinalIgnoreCase)
    {
        [".png"] = new("image/png", h => ComecaCom(h, [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A])),
        [".jpg"] = new("image/jpeg", h => ComecaCom(h, [0xFF, 0xD8, 0xFF])),
        [".jpeg"] = new("image/jpeg", h => ComecaCom(h, [0xFF, 0xD8, 0xFF])),
        [".gif"] = new("image/gif", h => ComecaCom(h, "GIF87a"u8.ToArray()) || ComecaCom(h, "GIF89a"u8.ToArray())),
        [".webp"] = new("image/webp", h => h.Length >= 12
            && ComecaCom(h, "RIFF"u8.ToArray()) && Encoding.ASCII.GetString(h, 8, 4) == "WEBP"),
    };

    // Nome servido: 32 hex (Guid "N") + extensão aceita. Qualquer outra coisa é 404.
    [GeneratedRegex(@"^[0-9a-f]{32}\.(png|jpg|jpeg|gif|webp)$")]
    private static partial Regex NomeValido();

    /// <summary>
    /// Confere extensão + assinatura e devolve a extensão normalizada (minúscula) e o
    /// Content-Type. Lança <see cref="ArgumentException"/> se não for uma imagem aceita.
    /// </summary>
    public static (string Extensao, string ContentType) Validar(Stream stream, string? fileName, long tamanho)
    {
        if (tamanho <= 0) throw new ArgumentException("Arquivo vazio.");
        if (tamanho > TamanhoMaximo) throw new ArgumentException("A imagem pode ter no máximo 5 MB.");

        var ext = Path.GetExtension(fileName ?? string.Empty).ToLowerInvariant();
        if (!Tipos.TryGetValue(ext, out var tipo))
            throw new ArgumentException("Envie uma imagem PNG, JPG, GIF ou WEBP.");

        var cabecalho = new byte[16];
        var posicao = stream.Position;
        var lidos = stream.ReadAtLeast(cabecalho, cabecalho.Length, throwOnEndOfStream: false);
        stream.Position = posicao;

        if (!tipo.Confere(cabecalho[..lidos]))
            throw new ArgumentException("O conteúdo do arquivo não corresponde a uma imagem " + ext.TrimStart('.').ToUpperInvariant() + ".");

        return (ext, tipo.ContentType);
    }

    public static string ObjectKey(string nome) => "guias/" + nome;

    public static bool NomeServivel(string? nome) => nome != null && NomeValido().IsMatch(nome);

    public static string ContentTypeDe(string nome) => Tipos[Path.GetExtension(nome)].ContentType;

    private static bool ComecaCom(byte[] dados, byte[] prefixo) =>
        dados.Length >= prefixo.Length && dados.AsSpan(0, prefixo.Length).SequenceEqual(prefixo);
}
