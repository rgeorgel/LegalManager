using LegalManager.Infrastructure.Escavador;

namespace LegalManager.UnitTests;

public class FiltroPublicacaoOabTests
{
    // Trechos reais que chegaram para uma OAB "54731" monitorada sem UF.
    private const string Rs = "AUTOR: CHAPEACAO E PINTURAS PALANDI LTDA ADVOGADO(A): LUCIANO HUTTEN CORREA (OAB RS054731) DESPACHO/DECISÃO Vistos.";
    private const string Pe = "AUTOR: ANGELA GOMES DOS SANTOS ADVOGADO do(a) AUTOR: JORGE SEVERINO LUCENA DE SOUSA - PE54731 REU: INSS";

    [Theory]
    [InlineData(Rs, "54731", "SP", true)]
    [InlineData(Pe, "54731", "SP", true)]
    [InlineData(Rs, "54731", "RS", false)]
    [InlineData(Pe, "54731", "PE", false)]
    [InlineData("Advogada: Ana Souza - OAB/SP 357.386", "357386", "SP", false)]
    [InlineData("Advogada: Ana Souza - OAB/SP nº 357386", "357386", "SP", false)]
    [InlineData("Advogada: Ana Souza (357386/SP)", "357386", "SP", false)]
    [InlineData("Advogada: Fulana (357386/RJ)", "357386", "SP", true)]
    [InlineData("Advogada: Fulana (OAB RJ 357386) e Ana (OAB SP 357386)", "357386", "SP", false)]
    public void Uf(string texto, string numero, string uf, bool descarta) =>
        Assert.Equal(descarta, FiltroPublicacaoOab.EhDeOutraOab(texto, numero, uf, null));

    [Fact]
    public void NomeDoAdvogadoNoTexto_Fica() =>
        Assert.False(FiltroPublicacaoOab.EhDeOutraOab(Pe, "54731", "SP", "Jorge Severino Lucena de Sousa"));

    [Fact]
    public void NomeSemAcentoNoDiario_Fica() =>
        Assert.False(FiltroPublicacaoOab.EhDeOutraOab("ADVOGADA NAHANE LETICIA DE MARCHI - RJ357386", "357386", "SP", "Nahane Letícia de Marchi"));

    [Theory]
    // Sem o número ao lado de uma UF não dá para afirmar que é de outro advogado.
    [InlineData("TRIBUNAL REGIONAL DO TRABALHO DA 4ª REGIÃO ... Intimado(s) - FULANO")]
    [InlineData("Processo 0054731-12.2026.8.26.0100 distribuído")]
    [InlineData("")]
    [InlineData(null)]
    public void SemUfAoLadoDoNumero_Fica(string? texto) =>
        Assert.False(FiltroPublicacaoOab.EhDeOutraOab(texto, "54731", "SP", null));
}
