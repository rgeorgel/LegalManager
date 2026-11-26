using QuestPDF.Drawing;

namespace LegalManager.API.Reports;

// Fontes embutidas no assembly (pasta Fonts/, licença OFL em Fonts/OFL.txt) para os PDFs não dependerem
// das fontes instaladas no servidor — "Segoe UI" não existe no Linux e o QuestPDF caía numa fonte genérica.
public static class PdfFontes
{
    public const string SourceSans3 = "Source Sans 3";

    private static readonly Lazy<bool> Registradas = new(() =>
    {
        var assembly = typeof(PdfFontes).Assembly;
        foreach (var nome in assembly.GetManifestResourceNames().Where(n => n.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase)))
        {
            using var stream = assembly.GetManifestResourceStream(nome)!;
            FontManager.RegisterFont(stream);
        }
        return true;
    });

    /// <summary>Registra as fontes embutidas (uma vez só) e devolve a família para o DefaultTextStyle.</summary>
    public static string Familia()
    {
        _ = Registradas.Value;
        return SourceSans3;
    }
}
