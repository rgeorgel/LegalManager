using LegalManager.Infrastructure.Services;

namespace LegalManager.UnitTests;

public class ArquivoPermitidoTests
{
    private static MemoryStream Bytes(params byte[] b) => new(b);

    [Theory]
    [InlineData("peticao.pdf", "application/pdf")]
    [InlineData("PETICAO.PDF", "application/pdf")]
    public void Validar_PdfVerdadeiro_DevolveContentTypeDoServidor(string nome, string esperado)
    {
        var stream = new MemoryStream("%PDF-1.7\n..."u8.ToArray());
        Assert.Equal(esperado, ArquivoPermitido.Validar(stream, nome));
    }

    [Fact]
    public void Validar_PdfComLixoAntesDoCabecalho_Aceita()
    {
        var bytes = new byte[300];
        "%PDF-1.4"u8.CopyTo(bytes.AsSpan(120));
        Assert.Equal("application/pdf", ArquivoPermitido.Validar(new MemoryStream(bytes), "a.pdf"));
    }

    [Fact]
    public void Validar_HtmlRenomeadoParaPdf_Recusa()
    {
        var stream = new MemoryStream("<!doctype html><script>fetch('//x')</script>"u8.ToArray());
        Assert.Throws<ArgumentException>(() => ArquivoPermitido.Validar(stream, "contrato.pdf"));
    }

    [Theory]
    [InlineData("pagina.html")]
    [InlineData("imagem.svg")]
    [InlineData("script.js")]
    [InlineData("programa.exe")]
    [InlineData("sem-extensao")]
    public void Validar_ExtensaoForaDaLista_Recusa(string nome)
    {
        Assert.Throws<ArgumentException>(() => ArquivoPermitido.Validar(Bytes(1, 2, 3), nome));
    }

    [Fact]
    public void Validar_DocxComAssinaturaZip_Aceita()
    {
        var tipo = ArquivoPermitido.Validar(Bytes(0x50, 0x4B, 0x03, 0x04, 0, 0), "contrato.docx");
        Assert.Equal("application/vnd.openxmlformats-officedocument.wordprocessingml.document", tipo);
    }

    [Fact]
    public void Validar_PngComAssinaturaDeJpeg_Recusa()
    {
        Assert.Throws<ArgumentException>(() => ArquivoPermitido.Validar(Bytes(0xFF, 0xD8, 0xFF, 0xE0), "foto.png"));
    }

    [Fact]
    public void Validar_TxtBinario_Recusa()
    {
        Assert.Throws<ArgumentException>(() => ArquivoPermitido.Validar(Bytes(0x41, 0x00, 0x42), "nota.txt"));
    }

    [Fact]
    public void Validar_VoltaOStreamParaOInicio()
    {
        var stream = new MemoryStream("%PDF-1.7\n"u8.ToArray());
        ArquivoPermitido.Validar(stream, "a.pdf");
        Assert.Equal(0, stream.Position);
    }

    [Theory]
    [InlineData("application/pdf", true)]
    [InlineData("image/png", true)]
    [InlineData("text/html", false)]
    [InlineData("image/svg+xml", false)]
    [InlineData(null, false)]
    public void PodePreVisualizar_SoPdfEImagens(string? contentType, bool esperado)
    {
        Assert.Equal(esperado, ArquivoPermitido.PodePreVisualizar(contentType));
    }
}
