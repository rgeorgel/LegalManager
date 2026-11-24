using LegalManager.API.Controllers;
using LegalManager.Application.DTOs.IA;
using LegalManager.Application.Interfaces;
using LegalManager.Domain.Entities;
using LegalManager.Domain.Interfaces;
using LegalManager.Infrastructure.Jurisprudencia;
using LegalManager.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace LegalManager.UnitTests;

/// <summary>Fase 3: número CNJ e DataJud (TJs), busca por significado com a IA atual e verificador público.</summary>
public class JurisprudenciaFase3Tests
{
    // Números com dígito verificador correto (módulo 97) e um com o dígito errado.
    private const string CnjTjspValido = "1004521-59.2023.8.26.0100";
    private const string CnjTrf3Valido = "5001234-03.2024.4.03.6100";
    private const string CnjTjmgValido = "0000001-02.2025.8.13.0024";
    private const string CnjDigitoErrado = "1004521-33.2023.8.26.0100";

    private static AppDbContext NovoDb() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static JurisprudenciaService Servico(AppDbContext db, IIAService? ia = null, ITribunalAdapter? tribunais = null) =>
        new(db, Mock.Of<ITenantContext>(), Mock.Of<IStorageService>(), ia is null ? null : new Lazy<IIAService>(() => ia), tribunais);

    // ── Número CNJ ──────────────────────────────────────────────────────────

    [Theory]
    [InlineData(CnjTjspValido, true)]
    [InlineData(CnjTrf3Valido, true)]
    [InlineData(CnjTjmgValido, true)]
    [InlineData("10045215920238260100", true)]
    [InlineData(CnjDigitoErrado, false)]
    [InlineData("1004521-59.2023.8.26", false)]
    [InlineData(null, false)]
    public void Cnj_DigitoVerificador(string? numero, bool valido) => Assert.Equal(valido, Cnj.Valido(numero));

    [Theory]
    [InlineData(CnjTjspValido, "TJSP")]
    [InlineData(CnjTjmgValido, "TJMG")]
    [InlineData(CnjTrf3Valido, "TRF3")]
    [InlineData("0000001-00.2025.5.02.0001", "TRT2")]
    [InlineData("0000001-00.2025.9.99.0001", null)]
    public void Cnj_Tribunal(string numero, string? tribunal) => Assert.Equal(tribunal, Cnj.Tribunal(numero));

    [Fact]
    public void Cnj_Formatar() => Assert.Equal(CnjTjspValido, Cnj.Formatar("10045215920238260100"));

    // ── Acórdãos de TJ: dígito verificador e DataJud ───────────────────────

    private static string Apelacao(string cnj) => $"(TJSP, Apelação Cível {cnj}, j. 10/02/2025)";

    [Fact]
    public async Task DigitoErrado_Inexistente_SemConsultarODataJud()
    {
        var tribunais = new Mock<ITribunalAdapter>(MockBehavior.Strict);

        var r = await Servico(NovoDb(), tribunais: tribunais.Object).VerificarTextoAsync(Apelacao(CnjDigitoErrado), true);

        var c = Assert.Single(r.Citacoes);
        Assert.Equal(StatusCitacao.Inexistente, c.Status);
        Assert.Contains("dígito verificador", c.Mensagem);
        Assert.Equal(1, r.Inexistentes);
    }

    [Fact]
    public async Task ProcessoNoDataJud_ProcessoEncontradoComTribunalEClasse()
    {
        var tribunais = new Mock<ITribunalAdapter>();
        tribunais.Setup(t => t.ConsultarAsync(CnjTjspValido, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TribunalConsultaResult(true, "Tribunal de Justiça de São Paulo", "3ª Câmara de Direito Privado", null, [],
                Classe: "Apelação Cível", SiglaTribunal: "TJSP"));

        var r = await Servico(NovoDb(), tribunais: tribunais.Object).VerificarTextoAsync(Apelacao(CnjTjspValido), true);

        var c = Assert.Single(r.Citacoes);
        Assert.Equal(StatusCitacao.ProcessoEncontrado, c.Status);
        Assert.Contains("TJSP, Apelação Cível, 3ª Câmara de Direito Privado", c.Mensagem);
        Assert.Contains("teor do acórdão não é conferido", c.Mensagem);
        Assert.Equal(1, r.Alertas);
    }

    [Fact]
    public async Task ProcessoForaDoDataJud_NaoEncontrada()
    {
        var tribunais = new Mock<ITribunalAdapter>();
        tribunais.Setup(t => t.ConsultarAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TribunalConsultaResult(false, null, null, null, []));

        var r = await Servico(NovoDb(), tribunais: tribunais.Object).VerificarTextoAsync(Apelacao(CnjTjspValido), true);

        Assert.Equal(StatusCitacao.NaoEncontrada, Assert.Single(r.Citacoes).Status);
    }

    [Fact]
    public async Task DataJudComErro_NaoVerificada()
    {
        var tribunais = new Mock<ITribunalAdapter>();
        tribunais.Setup(t => t.ConsultarAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("fora do ar"));

        var r = await Servico(NovoDb(), tribunais: tribunais.Object).VerificarTextoAsync(Apelacao(CnjTjspValido), true);

        var c = Assert.Single(r.Citacoes);
        Assert.Equal(StatusCitacao.NaoVerificada, c.Status);
        Assert.Contains("não respondeu", c.Mensagem);
    }

    [Fact]
    public async Task DataJudRecusouAConsulta_NaoVerificadaENaoNaoEncontrada()
    {
        var tribunais = new Mock<ITribunalAdapter>();
        tribunais.Setup(t => t.ConsultarAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TribunalConsultaResult(false, null, null, null, [], Falhou: true));

        var r = await Servico(NovoDb(), tribunais: tribunais.Object).VerificarTextoAsync(Apelacao(CnjTjspValido), true);

        var c = Assert.Single(r.Citacoes);
        Assert.Equal(StatusCitacao.NaoVerificada, c.Status);
        Assert.Contains("não respondeu", c.Mensagem);
    }

    [Fact]
    public async Task Publico_ConsultaNoMaximoTresProcessosNoDataJud()
    {
        var tribunais = new Mock<ITribunalAdapter>();
        tribunais.Setup(t => t.ConsultarAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TribunalConsultaResult(true, null, null, null, [], SiglaTribunal: "TJSP"));
        var texto = string.Join("; ", new[] { CnjTjspValido, CnjTjmgValido, CnjTrf3Valido, "1004521-88.2025.8.26.0100" }
            .Select(n => $"Apelação {n}"));

        var r = await Servico(NovoDb(), tribunais: tribunais.Object).VerificarTextoPublicoAsync(texto);

        Assert.Equal(4, r.Citacoes.Count);
        tribunais.Verify(t => t.ConsultarAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Exactly(JurisprudenciaService.ConsultasDataJudPublico));
        Assert.Equal(StatusCitacao.NaoVerificada, r.Citacoes[^1].Status);
    }

    // ── Busca por significado com a IA atual ───────────────────────────────

    private static Precedente Acordao(string numero, string texto) => new()
    {
        Id = Guid.NewGuid(), Tipo = TipoPrecedente.Acordao, Tribunal = "STJ", Classe = "REsp", ClasseBase = "REsp",
        Numero = numero, Texto = texto, UrlFonte = "https://x", Fonte = "stj-espelhos-x", ExternoId = numero,
        DataJulgamento = new DateTime(2026, 1, 1), ImportadoEm = DateTime.UtcNow,
    };

    private static async Task<AppDbContext> BaseAsync()
    {
        var db = NovoDb();
        db.Precedentes.AddRange(
            Acordao("1", "Alienação fiduciária. Busca e apreensão de veículo. Mora comprovada."),
            Acordao("2", "Alienação fiduciária de automóvel. Purgação da mora."),
            Acordao("3", "Dano moral. Transporte aéreo."));
        await db.SaveChangesAsync();
        return db;
    }

    [Fact]
    public async Task Significado_AIAAmpliaABuscaEEscolheAOrdem()
    {
        var db = await BaseAsync();
        var ia = new Mock<IIAService>();
        // O caso fala em "carro"; só a ampliação traz "veículo"/"alienação fiduciária".
        ia.Setup(i => i.ExpandirBuscaJurisprudenciaAsync("meu cliente atrasou as parcelas do carro", It.IsAny<CancellationToken>()))
            .ReturnsAsync("veículo, automóvel, alienação fiduciária, busca e apreensão, mora");
        string? listaRecebida = null;
        ia.Setup(i => i.EscolherPrecedentesAsync(It.IsAny<string>(), It.IsAny<string>(), 5, It.IsAny<CancellationToken>()))
            .Callback((string _, string lista, int _, CancellationToken _) => listaRecebida = lista)
            .ReturnsAsync("2, 1");

        var r = await Servico(db, ia.Object).BuscarPrecedentesPorSignificadoAsync("meu cliente atrasou as parcelas do carro", 5);

        Assert.Equal(["2", "1"], r.Select(p => p.Numero));
        Assert.Contains("1. REsp", listaRecebida);
        Assert.DoesNotContain("Transporte aéreo", listaRecebida);
    }

    [Fact]
    public async Task Significado_IAResponderNenhum_SemPrecedentes()
    {
        var db = await BaseAsync();
        var ia = new Mock<IIAService>();
        ia.Setup(i => i.ExpandirBuscaJurisprudenciaAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync("alienação fiduciária");
        ia.Setup(i => i.EscolherPrecedentesAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("Nenhum.");

        Assert.Empty(await Servico(db, ia.Object).BuscarPrecedentesPorSignificadoAsync("alienação fiduciária", 5));
    }

    [Theory]
    [InlineData("não sei")]   // resposta sem números válidos
    [InlineData("99, 42")]    // fora da lista
    public async Task Significado_RespostaInvalida_FicaAOrdemDaBusca(string resposta)
    {
        var db = await BaseAsync();
        var ia = new Mock<IIAService>();
        ia.Setup(i => i.ExpandirBuscaJurisprudenciaAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync("");
        ia.Setup(i => i.EscolherPrecedentesAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(resposta);

        var r = await Servico(db, ia.Object).BuscarPrecedentesPorSignificadoAsync("alienação fiduciária mora", 5);

        Assert.Equal(["1", "2"], r.Select(p => p.Numero));
    }

    [Fact]
    public async Task Significado_IAFora_FicaABuscaPorPalavras()
    {
        var db = await BaseAsync();
        var ia = new Mock<IIAService>();
        ia.Setup(i => i.ExpandirBuscaJurisprudenciaAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ThrowsAsync(new TimeoutException());
        ia.Setup(i => i.EscolherPrecedentesAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException());

        var r = await Servico(db, ia.Object).BuscarPrecedentesPorSignificadoAsync("alienação fiduciária mora", 1);

        Assert.Equal("1", Assert.Single(r).Numero);
    }

    [Fact]
    public async Task Significado_SemIA_IgualABuscaPorPalavras()
    {
        var db = await BaseAsync();
        var svc = Servico(db);

        var porSignificado = await svc.BuscarPrecedentesPorSignificadoAsync("alienação fiduciária mora", 5);
        var porPalavras = await svc.BuscarPrecedentesAsync("alienação fiduciária mora", 5);

        Assert.Equal(porPalavras.Select(p => p.Id), porSignificado.Select(p => p.Id));
    }

    [Fact]
    public async Task Significado_UmCandidatoSo_NaoChamaAIAParaOrdenar()
    {
        var db = await BaseAsync();
        var ia = new Mock<IIAService>();
        ia.Setup(i => i.ExpandirBuscaJurisprudenciaAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync("");

        var r = await Servico(db, ia.Object).BuscarPrecedentesPorSignificadoAsync("transporte aéreo", 5);

        Assert.Equal("3", Assert.Single(r).Numero);
        ia.Verify(i => i.EscolherPrecedentesAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task IANaoConfigurada_NaoQuebraNemAConferenciaNemABusca()
    {
        var db = await BaseAsync();
        var semIA = new Lazy<IIAService>(() => throw new InvalidOperationException("IA:ApiKey não configurado"));
        var svc = new JurisprudenciaService(db, Mock.Of<ITenantContext>(), Mock.Of<IStorageService>(), semIA);

        Assert.Single((await svc.VerificarTextoPublicoAsync("Tema 1.076/STJ")).Citacoes);
        Assert.Equal(["1", "2"], (await svc.BuscarPrecedentesPorSignificadoAsync("alienação fiduciária mora", 5)).Select(p => p.Numero));
    }

    // ── Verificador público ─────────────────────────────────────────────────

    [Fact]
    public async Task Publico_ValidaTextoEUsaAVerificacaoPublica()
    {
        var svc = new Mock<IJurisprudenciaService>(MockBehavior.Strict);
        var vazio = new VerificacaoCitacoesDto([], 0, 0, 0, "");
        svc.Setup(s => s.VerificarTextoPublicoAsync("Tema 1.076/STJ", It.IsAny<CancellationToken>())).ReturnsAsync(vazio);
        var controller = new JurisprudenciaController(svc.Object);

        Assert.IsType<BadRequestObjectResult>((await controller.VerificarPublico(new VerificarCitacoesDto(null, Guid.NewGuid()), default)).Result);
        Assert.IsType<BadRequestObjectResult>((await controller.VerificarPublico(
            new VerificarCitacoesDto(new string('a', JurisprudenciaController.TamanhoMaximoTextoPublico + 1), null), default)).Result);
        var ok = Assert.IsType<OkObjectResult>((await controller.VerificarPublico(new VerificarCitacoesDto("Tema 1.076/STJ", null), default)).Result);
        Assert.Same(vazio, ok.Value);
    }

    [Fact]
    public void Publico_AnonimoComLimitePorIp()
    {
        var metodo = typeof(JurisprudenciaController).GetMethod(nameof(JurisprudenciaController.VerificarPublico))!;
        Assert.NotNull(metodo.GetCustomAttributes(typeof(Microsoft.AspNetCore.Authorization.AllowAnonymousAttribute), false).SingleOrDefault());
        var limite = (Microsoft.AspNetCore.RateLimiting.EnableRateLimitingAttribute)metodo
            .GetCustomAttributes(typeof(Microsoft.AspNetCore.RateLimiting.EnableRateLimitingAttribute), false).Single();
        Assert.Equal(JurisprudenciaController.PoliticaRateLimitPublico, limite.PolicyName);
        // O endpoint do escritório continua exigindo login (sem [AllowAnonymous]).
        Assert.Empty(typeof(JurisprudenciaController).GetMethod(nameof(JurisprudenciaController.Verificar))!
            .GetCustomAttributes(typeof(Microsoft.AspNetCore.Authorization.AllowAnonymousAttribute), false));
    }
}
