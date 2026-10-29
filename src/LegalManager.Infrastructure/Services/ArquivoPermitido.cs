using System.Text;

namespace LegalManager.Infrastructure.Services;

/// <summary>
/// Lista de tipos aceitos no upload de documentos. A extensão escolhe o tipo e os primeiros
/// bytes do arquivo precisam bater com ela — o Content-Type do navegador é ignorado, porque
/// quem envia o arquivo escolhe o valor (um HTML enviado como "contrato.pdf" com
/// Content-Type text/html rodaria dentro do app na pré-visualização).
/// </summary>
public static class ArquivoPermitido
{
    private static readonly byte[] Pdf = "%PDF-"u8.ToArray();
    private static readonly byte[] Zip = [0x50, 0x4B, 0x03, 0x04];
    private static readonly byte[] Ole = [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1];
    private static readonly byte[] Rtf = "{\\rtf"u8.ToArray();
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF];

    private sealed record Tipo(string ContentType, Func<byte[], bool> Confere);

    private static readonly Dictionary<string, Tipo> Tipos = new(StringComparer.OrdinalIgnoreCase)
    {
        // O cabeçalho do PDF pode vir depois de alguns bytes de lixo (o leitor procura no 1º KB).
        [".pdf"] = new("application/pdf", h => IndexOf(h, Pdf) >= 0),
        [".doc"] = new("application/msword", h => ComecaCom(h, Ole)),
        [".xls"] = new("application/vnd.ms-excel", h => ComecaCom(h, Ole)),
        [".docx"] = new("application/vnd.openxmlformats-officedocument.wordprocessingml.document", h => ComecaCom(h, Zip)),
        [".xlsx"] = new("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", h => ComecaCom(h, Zip)),
        [".odt"] = new("application/vnd.oasis.opendocument.text", h => ComecaCom(h, Zip)),
        [".ods"] = new("application/vnd.oasis.opendocument.spreadsheet", h => ComecaCom(h, Zip)),
        [".rtf"] = new("application/rtf", h => ComecaCom(h, Rtf)),
        [".txt"] = new("text/plain; charset=utf-8", PareceTexto),
        [".csv"] = new("text/csv; charset=utf-8", PareceTexto),
        [".png"] = new("image/png", h => ComecaCom(h, Png)),
        [".jpg"] = new("image/jpeg", h => ComecaCom(h, Jpeg)),
        [".jpeg"] = new("image/jpeg", h => ComecaCom(h, Jpeg)),
        [".webp"] = new("image/webp", h => h.Length >= 12
            && ComecaCom(h, "RIFF"u8.ToArray()) && Encoding.ASCII.GetString(h, 8, 4) == "WEBP"),
    };

    public static IReadOnlyCollection<string> Extensoes => Tipos.Keys;

    /// <summary>
    /// Confere extensão + assinatura e devolve o Content-Type definido pelo servidor.
    /// Lança <see cref="ArgumentException"/> se o arquivo não for de um tipo aceito.
    /// O stream precisa permitir Seek (IFormFile.OpenReadStream permite); volta para o início.
    /// </summary>
    public static string Validar(Stream stream, string fileName)
    {
        var ext = Path.GetExtension(fileName ?? string.Empty);
        if (string.IsNullOrEmpty(ext) || !Tipos.TryGetValue(ext, out var tipo))
            throw new ArgumentException(
                $"Tipo de arquivo não permitido. Envie: {string.Join(", ", Tipos.Keys.Select(e => e.TrimStart('.').ToUpperInvariant()))}.");

        var cabecalho = new byte[1024];
        var posicao = stream.Position;
        var lidos = stream.ReadAtLeast(cabecalho, cabecalho.Length, throwOnEndOfStream: false);
        stream.Position = posicao;

        if (!tipo.Confere(cabecalho[..lidos]))
            throw new ArgumentException($"O conteúdo do arquivo não corresponde à extensão {ext.ToLowerInvariant()}.");

        return tipo.ContentType;
    }

    /// <summary>Tipos que o navegador pode exibir com segurança no modal de pré-visualização.</summary>
    public static bool PodePreVisualizar(string? contentType) =>
        contentType is "application/pdf" or "image/png" or "image/jpeg" or "image/webp";

    private static bool ComecaCom(byte[] h, byte[] assinatura) =>
        h.Length >= assinatura.Length && h.AsSpan(0, assinatura.Length).SequenceEqual(assinatura);

    private static int IndexOf(byte[] h, byte[] assinatura) => h.AsSpan().IndexOf(assinatura);

    // Texto puro não tem assinatura: basta não ter bytes nulos (que denunciam binário).
    private static bool PareceTexto(byte[] h) => h.Length > 0 && Array.IndexOf(h, (byte)0) < 0;
}
