using LegalManager.Domain.Entities;
using LegalManager.Domain.Enums;
using LegalManager.Infrastructure.Persistence;
using LegalManager.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

namespace LegalManager.UnitTests;

public class TextoHtmlTests
{
    [Theory]
    // Trecho real do Escavador (fluxo OAB) que aparecia com as tags na tela.
    [InlineData("<article> <header> <div></div> </header> <section><b>PRODUÇÃO ANTECIPADA DA PROVA</b></section> <section><b><table><tr><td>REQUERENTE</td><td>: PIEMONTE LTDA</td></tr></table></b></section>",
        "PRODUÇÃO ANTECIPADA DA PROVA REQUERENTE: PIEMONTE LTDA")]
    [InlineData("PROCESSO CÍVEL -&gt; Processo de Conhecimento -&gt; Procedimento Comum", "PROCESSO CÍVEL -> Processo de Conhecimento -> Procedimento Comum")]
    [InlineData("Intime-se&nbsp;a&nbsp;parte", "Intime-se a parte")]
    [InlineData("&amp;lt;p&amp;gt;escapado duas vezes&amp;lt;/p&amp;gt;", "escapado duas vezes")]
    [InlineData("<p>Texto</p><style>.x{color:red}</style><script>alert(1)</script><!-- nota -->fim", "Texto fim")]
    [InlineData("<P ALIGN=\"center\">DESPACHO/DECISÃO</P><br/>Vistos.", "DESPACHO/DECISÃO Vistos.")]
    [InlineData("valor < 10 e > 5", "valor < 10 e > 5")]
    [InlineData("Silva & Souza Advogados", "Silva & Souza Advogados")]
    public void ParaTexto_RemoveTagsEEntidades(string entrada, string esperado) =>
        Assert.Equal(esperado, TextoHtml.ParaTexto(entrada));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("<div> </div>")]
    public void ParaTexto_SemTexto_Null(string? entrada) => Assert.Null(TextoHtml.ParaTexto(entrada));

    [Theory]
    [InlineData("<p>x</p>", true)]
    [InlineData("a -&gt; b", true)]
    [InlineData("Silva & Souza", false)]
    [InlineData("valor < 10", false)]
    [InlineData(null, false)]
    public void TemHtml(string? texto, bool esperado) => Assert.Equal(esperado, TextoHtml.TemHtml(texto));

    [Fact]
    public async Task Limpeza_TiraHtmlDoQueJaFoiGravado_EDepoisNaoAchaMaisNada()
    {
        var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var tenantId = Guid.NewGuid();
        Publicacao Pub(string conteudo, string? snippet) => new()
        {
            Id = Guid.NewGuid(), TenantId = tenantId, Diario = "DJ", Conteudo = conteudo, Snippet = snippet,
            DataPublicacao = DateTime.UtcNow, CapturaEm = DateTime.UtcNow
        };
        var comHtml = Pub("<p>Despacho -&gt; prazo</p>", "<b>Despacho</b>");
        var limpa = Pub("Silva & Souza", null);
        db.Publicacoes.AddRange(comHtml, limpa);
        Andamento And(string descricao, FonteAndamento fonte) => new()
        {
            Id = Guid.NewGuid(), TenantId = tenantId, ProcessoId = Guid.NewGuid(), Data = DateTime.UtcNow,
            Descricao = descricao, Fonte = fonte, CriadoEm = DateTime.UtcNow
        };
        var automatico = And("<p>Juntada</p>", FonteAndamento.Automatico);
        var manual = And("<b>digitado</b>", FonteAndamento.Manual);
        db.Andamentos.AddRange(automatico, manual);
        await db.SaveChangesAsync();

        Assert.Equal(2, await LimpezaHtmlPublicacoes.ExecutarAsync(db));
        Assert.Equal("Despacho -> prazo", comHtml.Conteudo);
        Assert.Equal("Despacho", comHtml.Snippet);
        Assert.Equal("Silva & Souza", limpa.Conteudo);
        Assert.Equal("Juntada", automatico.Descricao);
        Assert.Equal("<b>digitado</b>", manual.Descricao); // o que o usuário escreveu não é tocado

        Assert.Equal(0, await LimpezaHtmlPublicacoes.ExecutarAsync(db));
    }
}
