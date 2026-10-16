namespace LegalManager.API.Reports;

/// <summary>Logo do escritório para os PDFs: a URL do tenant, ou o logo do Causify como padrão.</summary>
internal static class PdfLogo
{
    private const string LogoPadraoRelativePath = "images/causify-logo-3B-claro.png";

    private static readonly HttpClient LogoHttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(4)
    };

    private static byte[]? _logoPadraoCache;

    private static bool ImagemDecodificavel(byte[] bytes)
    {
        try
        {
            QuestPDF.Infrastructure.Image.FromBinaryData(bytes);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static byte[]? CarregarLogoRemoto(string? logoUrl)
    {
        if (string.IsNullOrWhiteSpace(logoUrl)) return null;
        if (!logoUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !logoUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return null;
        try
        {
            var bytes = LogoHttpClient.GetByteArrayAsync(logoUrl).GetAwaiter().GetResult();
            return ImagemDecodificavel(bytes) ? bytes : null;
        }
        catch
        {
            return null;
        }
    }

    private static byte[]? CarregarLogoPadrao(string? webRootPath)
    {
        if (_logoPadraoCache != null) return _logoPadraoCache;
        if (string.IsNullOrWhiteSpace(webRootPath)) return null;
        try
        {
            var caminho = Path.Combine(webRootPath, LogoPadraoRelativePath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(caminho)) return null;
            var bytes = File.ReadAllBytes(caminho);
            if (!ImagemDecodificavel(bytes)) return null;
            _logoPadraoCache = bytes;
            return bytes;
        }
        catch
        {
            return null;
        }
    }

    public static byte[]? Carregar(string? logoUrl, string? webRootPath) =>
        CarregarLogoRemoto(logoUrl) ?? CarregarLogoPadrao(webRootPath);
}
