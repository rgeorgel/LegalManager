using LegalManager.Domain.Enums;
using LegalManager.Infrastructure.Services;

namespace LegalManager.UnitTests;

public class CategoriaPartesImportacaoTests
{
    [Theory]
    [InlineData(TipoParteProcesso.Autor, TipoParteProcesso.Autor, TipoContato.Cliente)]
    [InlineData(TipoParteProcesso.Reu, TipoParteProcesso.Autor, TipoContato.ParteContraria)]
    [InlineData(TipoParteProcesso.Interessado, TipoParteProcesso.Autor, TipoContato.Outro)]
    [InlineData(TipoParteProcesso.Autor, null, TipoContato.Outro)]
    public void Categoria_PeloLadoDoAdvogado(TipoParteProcesso parte, TipoParteProcesso? lado, TipoContato esperado) =>
        Assert.Equal(esperado, CategoriaPartesImportacao.Categoria(parte, lado));

    [Fact]
    public void LadoUnico_AdvogadoNosDoisPolos_Indefinido() =>
        Assert.Null(CategoriaPartesImportacao.LadoUnico([TipoParteProcesso.Autor, TipoParteProcesso.Reu]));

    [Theory]
    [InlineData("123456/SP", true)]
    [InlineData("OAB/SP 123.456", true)]
    [InlineData("SP0123456", true)]
    [InlineData("123456/RJ", false)]
    [InlineData("1234567/SP", false)]
    [InlineData(null, false)]
    public void MesmaOab_TextoLivre(string? texto, bool esperado) =>
        Assert.Equal(esperado, CategoriaPartesImportacao.MesmaOab(texto, "123456", "SP"));

    [Theory]
    [InlineData("FULANA DE TÁL", "Fulana de Tal", true)]
    [InlineData("Fulana de Tal Advogada: Ciclana Souza", "Ciclana Souza", true)]
    [InlineData("Fulana de Tal", "Fulana", false)] // só o prenome: ambíguo
    [InlineData("Fulana de Tal Junior", "Fulana de Ta", false)]
    public void CitaNome_ExigeNomeCompletoIgnorandoAcentoECaixa(string texto, string nome, bool esperado) =>
        Assert.Equal(esperado, CategoriaPartesImportacao.CitaNome(texto, nome));
}
