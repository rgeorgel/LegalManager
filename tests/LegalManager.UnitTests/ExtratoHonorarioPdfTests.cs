using System.Text;
using LegalManager.API.Reports;
using LegalManager.Application.DTOs.Honorarios;

namespace LegalManager.UnitTests;

public class ExtratoHonorarioPdfTests
{
    private static string? WebRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            var candidato = Path.Combine(dir.FullName, "src", "LegalManager.API", "wwwroot");
            if (Directory.Exists(candidato)) return candidato;
        }
        return null;
    }

    private static ExtratoPdfDadosDto Dados(decimal emAtraso, string? advogado = "Ana Advogada", string? oab = "OAB/SP 12345",
        string? telefone = "(11) 9999-0000", string? email = "ana@x.com", string? logoUrl = null) => new(
        "Escritório Modelo", advogado, oab, "Rua A, 1 — São Paulo/SP", telefone, email, logoUrl,
        "HON-2026/0001", "Ação de cobrança", "Maria Cliente", "123.456.789-00", "0001234-56.2024.8.26.0100",
        3000m, "Parcelado", "Boleto/PIX", 0.02m, 0.015m, new DateTime(2026, 1, 10),
        1000m, 1000m, emAtraso,
        [
            new ExtratoParcelaPdfDto("1/3", new DateTime(2026, 2, 10), 1000m, null, 1000m, "Pago", new DateTime(2026, 2, 9)),
            new ExtratoParcelaPdfDto("2/3", new DateTime(2026, 3, 10), 1000m, 45.5m, 1045.5m, "Em atraso", null),
            new ExtratoParcelaPdfDto("3/3", new DateTime(2026, 4, 10), 1000m, null, 1000m, "Pendente", null),
            new ExtratoParcelaPdfDto("Entrada", new DateTime(2026, 1, 10), 0m, null, 0m, "Cancelado", null),
        ],
        new DateTime(2026, 3, 20, 14, 30, 0));

    private static void AssertPdf(byte[] pdf) => Assert.StartsWith("%PDF", Encoding.ASCII.GetString(pdf[..4]));

    [Fact]
    public void Renderiza_ComAtrasoELogoPadrao()
    {
        AssertPdf(ExtratoHonorarioPdfRenderer.Renderizar(Dados(1045.5m), WebRoot()));
    }

    [Fact]
    public void Renderiza_SemAtrasoSemContatoESemLogo()
    {
        AssertPdf(ExtratoHonorarioPdfRenderer.Renderizar(Dados(0m, advogado: null, oab: null, telefone: null, email: null)));
    }

    [Fact]
    public void Renderiza_LogoUrlInvalida_UsaPadrao()
    {
        AssertPdf(ExtratoHonorarioPdfRenderer.Renderizar(Dados(0m, logoUrl: "ftp://nao-suportado/logo.png"), "/caminho/inexistente"));
    }
}
