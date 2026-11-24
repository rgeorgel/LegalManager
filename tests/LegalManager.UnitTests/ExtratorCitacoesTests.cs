using LegalManager.Application.DTOs.IA;
using LegalManager.Infrastructure.Jurisprudencia;

namespace LegalManager.UnitTests;

public class ExtratorCitacoesTests
{
    private static CitacaoDto Unica(string texto)
    {
        var lista = ExtratorCitacoes.Extrair(texto);
        return Assert.Single(lista);
    }

    [Theory]
    [InlineData("conforme o REsp 1.234.567/SP, julgado", "REsp", "1234567", "STJ")]
    [InlineData("REsp n. 1234567 - RJ", "REsp", "1234567", "STJ")]
    [InlineData("no AgInt no AREsp 2.104.552/SP", "AgInt no AREsp", "2104552", "STJ")]
    [InlineData("AgInt nos EDcl no REsp 1.987.321/RJ", "AgInt nos EDcl no REsp", "1987321", "STJ")]
    [InlineData("EDcl no AgInt no AREsp n.º 2.000.111/MG", "EDcl no AgInt no AREsp", "2000111", "STJ")]
    [InlineData("AgRg no HC 654.321/SP (STJ)", "AgRg no HC", "654321", "STJ")]
    [InlineData("RE 1.010.606 (STF)", "RE", "1010606", "STF")]
    [InlineData("ARE 843.989", "ARE", "843989", "STF")]
    [InlineData("ADI 5.766", "ADI", "5766", "STF")]
    [InlineData("o RESP 1.111.222/PR", "REsp", "1111222", "STJ")]
    public void Acordaos(string texto, string classe, string numero, string tribunal)
    {
        var c = Unica(texto);
        Assert.Equal(TipoCitacao.Acordao, c.Tipo);
        Assert.Equal(classe, c.Classe);
        Assert.Equal(numero, c.Numero);
        Assert.Equal(tribunal, c.Tribunal);
        Assert.Equal(texto.Substring(c.Inicio, c.Fim - c.Inicio).Trim(), c.Trecho);
    }

    [Fact]
    public void HcSemTribunal_FicaSemTribunal()
    {
        var c = Unica("Nesse sentido, o HC 123.456/SP.");
        Assert.Equal("HC", c.Classe);
        Assert.Null(c.Tribunal);
    }

    [Theory]
    [InlineData("REsp 1.234.567/SP, Rel. Min. Nancy Andrighi, Terceira Turma", "Nancy Andrighi")]
    [InlineData("AREsp 2.104.552/SP, relatora Ministra Maria Isabel Gallotti", "Maria Isabel Gallotti")]
    [InlineData("REsp 1.234.567/SP (Rel. Ministro Raul Araújo, j. 10/10/2025)", "Raul Araújo")]
    public void Relator(string texto, string relator) =>
        Assert.Equal(relator, Unica(texto).Relator);

    [Fact]
    public void RelatorDaCitacaoSeguinte_NaoEhAtribuidoAAnterior()
    {
        var lista = ExtratorCitacoes.Extrair("REsp 1.111.111/SP e REsp 2.222.222/RJ, Rel. Min. Nancy Andrighi");
        Assert.Equal(2, lista.Count);
        Assert.Null(lista[0].Relator);
        Assert.Equal("Nancy Andrighi", lista[1].Relator);
    }

    [Theory]
    [InlineData("incide a Súmula 7/STJ", TipoCitacao.Sumula, "7", "STJ")]
    [InlineData("Súmula n.º 7 do Superior Tribunal de Justiça", TipoCitacao.Sumula, "7", "STJ")]
    [InlineData("Enunciado 331 do TST", TipoCitacao.Sumula, "331", "TST")]
    [InlineData("Súmula Vinculante 13", TipoCitacao.SumulaVinculante, "13", "STF")]
    [InlineData("a SV 13 veda", TipoCitacao.SumulaVinculante, "13", "STF")]
    [InlineData("Sumula 283 do STF", TipoCitacao.Sumula, "283", "STF")]
    public void Sumulas(string texto, TipoCitacao tipo, string numero, string tribunal)
    {
        var c = Unica(texto);
        Assert.Equal(tipo, c.Tipo);
        Assert.Equal(numero, c.Numero);
        Assert.Equal(tribunal, c.Tribunal);
    }

    [Fact]
    public void SumulasEmLista_ViramUmaCitacaoPorNumero()
    {
        var lista = ExtratorCitacoes.Extrair("óbice das Súmulas 5 e 7 do STJ");
        Assert.Equal(["5", "7"], lista.Select(c => c.Numero));
        Assert.All(lista, c => Assert.Equal("STJ", c.Tribunal));
    }

    [Theory]
    [InlineData("Tema 1.076/STJ", "1076", "STJ")]
    [InlineData("Tema repetitivo n. 1076", "1076", "STJ")]
    [InlineData("Tema 69 da repercussão geral", "69", "STF")]
    [InlineData("Tema de repercussão geral 69", "69", "STF")]
    [InlineData("o Tema 990 do STJ", "990", "STJ")]
    public void Temas(string texto, string numero, string tribunal)
    {
        var c = Unica(texto);
        Assert.Equal(TipoCitacao.Tema, c.Tipo);
        Assert.Equal(numero, c.Numero);
        Assert.Equal(tribunal, c.Tribunal);
    }

    [Fact]
    public void Oj_DoTst()
    {
        var c = Unica("nos termos da OJ 394 da SBDI-1");
        Assert.Equal(TipoCitacao.OrientacaoJurisprudencial, c.Tipo);
        Assert.Equal("394", c.Numero);
        Assert.Equal("TST", c.Tribunal);
    }

    [Fact]
    public void AcordaoDeTribunalLocal_PeloCnjComClasse()
    {
        var c = Unica("(TJSP, Apelação Cível 1004521-33.2023.8.26.0100, j. 10/02/2025)");
        Assert.Equal(TipoCitacao.AcordaoTribunalLocal, c.Tipo);
        Assert.Equal("TJ", c.Tribunal);
        Assert.Equal("10045213320238260100", c.Numero);
    }

    [Theory]
    [InlineData("Processo nº 1004521-33.2023.8.26.0100")] // o número do próprio processo, sem classe de recurso
    [InlineData("conforme o art. 186 do CC 2002")]
    [InlineData("telefone (11) 3333-4444")]
    [InlineData("valor de R$ 1.234.567,00")]
    [InlineData("o ms 123 da planilha")]
    [InlineData("Código Civil, art. 927")]
    [InlineData("o RE 12 do item")]
    public void NaoSaoCitacoes(string texto) =>
        Assert.Empty(ExtratorCitacoes.Extrair(texto));

    [Fact]
    public void VariasCitacoes_EmOrdemESemDuplicar()
    {
        const string texto = """
            A revisão de cláusula esbarra na Súmula 5/STJ, conforme o AgInt no AREsp 2.104.552/SP,
            Rel. Min. Maria Isabel Gallotti. Ver ainda o Tema 1.076/STJ e o RE 1.010.606 (STF).
            """;
        var lista = ExtratorCitacoes.Extrair(texto);

        Assert.Equal(
            [TipoCitacao.Sumula, TipoCitacao.Acordao, TipoCitacao.Tema, TipoCitacao.Acordao],
            lista.Select(c => c.Tipo));
        Assert.True(lista.Zip(lista.Skip(1)).All(p => p.First.Fim <= p.Second.Inicio));
        Assert.Equal("Maria Isabel Gallotti", lista[1].Relator);
    }

    [Fact]
    public void TextoVazio_SemCitacoes()
    {
        Assert.Empty(ExtratorCitacoes.Extrair(null));
        Assert.Empty(ExtratorCitacoes.Extrair("   "));
    }

    [Theory]
    [InlineData("AgInt nos EDcl no REsp", "REsp")]
    [InlineData("agint no aresp", "AREsp")]
    [InlineData("SLS", null)]
    [InlineData("", null)]
    public void ClasseBase(string cadeia, string? esperada) =>
        Assert.Equal(esperada, ExtratorCitacoes.ClasseBase(cadeia));

    [Fact]
    public void NormalizarCadeia_MantemSiglasDesconhecidas() =>
        Assert.Equal("AgInt na SLS", ExtratorCitacoes.NormalizarCadeia("AGINT NA SLS"));
}
