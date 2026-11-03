using System.Security.Claims;
using LegalManager.API.Controllers;
using LegalManager.Application.DTOs.Publicidade;
using LegalManager.Application.Interfaces;
using LegalManager.Domain.Entities;
using LegalManager.Infrastructure;
using LegalManager.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace LegalManager.UnitTests;

/// <summary>Cadastro dos anúncios próprios do Causify (super admin).</summary>
public class AnunciosPropriosTests
{
    private static AppDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static SuperAdminAnunciosPropriosController CreateController(AppDbContext ctx, Mock<IAuditService>? audit = null)
    {
        var controller = new SuperAdminAnunciosPropriosController(ctx, (audit ?? new Mock<IAuditService>()).Object);
        var claims = new[] { new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()) };
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test")) }
        };
        return controller;
    }

    private static SalvarAnuncioProprioDto Dto(string link = "/pages/assinatura.html", bool ativo = true, string? icone = " 📣 ", string? cor = null) =>
        new(icone, " Trabalhe sem anúncios ", " Sem publicidade nos planos pagos. ", " Ver planos ", link, ativo, cor);

    private static T Valor<T>(ActionResult<T> resultado) =>
        Assert.IsType<T>(Assert.IsType<OkObjectResult>(resultado.Result).Value);

    [Theory]
    [InlineData("/pages/assinatura.html")]
    [InlineData("https://causify.com.br/blog/novidade")]
    public void Validar_AceitaCaminhoDoSistemaOuHttps(string link)
    {
        Assert.Null(SuperAdminAnunciosPropriosController.Validar(Dto(link)));
    }

    [Theory]
    [InlineData("//outro-site.com")]
    [InlineData("/\\outro-site.com")]
    [InlineData("http://causify.com.br")]
    [InlineData("javascript:alert(1)")]
    [InlineData("pages/assinatura.html")]
    public void Validar_RejeitaLinkInseguro(string link)
    {
        Assert.Contains("link", SuperAdminAnunciosPropriosController.Validar(Dto(link)));
    }

    [Theory]
    [InlineData("#1E3A8A")]
    [InlineData("#fbbf24")]
    [InlineData(null)]
    [InlineData(" ")]
    public void Validar_AceitaCorHexOuSemCor(string? cor)
    {
        Assert.Null(SuperAdminAnunciosPropriosController.Validar(Dto(cor: cor)));
    }

    [Theory]
    [InlineData("azul")]
    [InlineData("#fff")]
    [InlineData("#12345g")]
    [InlineData("red;background:url(x)")]
    public void Validar_RejeitaCorForaDoFormato(string cor)
    {
        Assert.Contains("cor", SuperAdminAnunciosPropriosController.Validar(Dto(cor: cor)));
    }

    [Fact]
    public async Task Criar_GuardaACorEmMinusculas()
    {
        using var ctx = CreateContext();

        var dto = Valor(await CreateController(ctx).Criar(Dto(cor: " #1E3A8A "), CancellationToken.None));

        Assert.Equal("#1e3a8a", dto.Cor);
        Assert.Equal("#1e3a8a", (await ctx.AnunciosProprios.SingleAsync()).Cor);
    }

    [Theory]
    [InlineData("", "t", "b", "/", "título")]
    [InlineData("t", " ", "b", "/", "texto")]
    [InlineData("t", "t", "", "/", "botão")]
    [InlineData("t", "t", "b", "", "link")]
    public void Validar_CamposObrigatorios(string titulo, string texto, string botao, string link, string esperado)
    {
        Assert.Contains(esperado, SuperAdminAnunciosPropriosController.Validar(new(null, titulo, texto, botao, link, true)));
    }

    [Fact]
    public async Task Criar_GravaSemEspacosEAudita()
    {
        using var ctx = CreateContext();
        var audit = new Mock<IAuditService>();

        var dto = Valor(await CreateController(ctx, audit).Criar(Dto(), CancellationToken.None));

        var salvo = await ctx.AnunciosProprios.SingleAsync();
        Assert.Equal(salvo.Id, dto.Id);
        Assert.Equal(("📣", "Trabalhe sem anúncios", "Sem publicidade nos planos pagos.", "Ver planos", true),
            (salvo.Icone, salvo.Titulo, salvo.Texto, salvo.TextoBotao, salvo.Ativo));
        Assert.NotNull(salvo.CriadoPorId);
        audit.Verify(a => a.LogAsync(It.IsAny<AuditLogEntry>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Criar_IconeEmBrancoViraNull()
    {
        using var ctx = CreateContext();

        await CreateController(ctx).Criar(Dto(icone: "  "), CancellationToken.None);

        Assert.Null((await ctx.AnunciosProprios.SingleAsync()).Icone);
    }

    [Fact]
    public async Task Criar_LinkInvalido_BadRequest()
    {
        using var ctx = CreateContext();

        var result = await CreateController(ctx).Criar(Dto("//outro-site.com"), CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Empty(ctx.AnunciosProprios);
    }

    [Fact]
    public async Task Listar_AtivosPrimeiroDepoisMaisRecentes()
    {
        using var ctx = CreateContext();
        var agora = DateTime.UtcNow;
        AnuncioProprio Novo(string titulo, bool ativo, int dias) => new()
        {
            Id = Guid.NewGuid(), Titulo = titulo, Texto = "t", TextoBotao = "b", LinkUrl = "/", Ativo = ativo, CriadoEm = agora.AddDays(-dias)
        };
        ctx.AnunciosProprios.AddRange(Novo("inativo novo", false, 0), Novo("ativo antigo", true, 5), Novo("ativo novo", true, 1));
        await ctx.SaveChangesAsync();

        var lista = Valor(await CreateController(ctx).Listar(ct: CancellationToken.None));

        Assert.Equal(new[] { "ativo novo", "ativo antigo", "inativo novo" }, lista.Select(a => a.Titulo));
    }

    [Fact]
    public async Task Listar_SomaImpressoesECliquesDoPeriodo()
    {
        using var ctx = CreateContext();
        var a = Guid.NewGuid();
        var semEventos = Guid.NewGuid();
        ctx.AnunciosProprios.AddRange(
            new AnuncioProprio { Id = a, Titulo = "com eventos", Texto = "t", TextoBotao = "b", LinkUrl = "/", Ativo = true, CriadoEm = DateTime.UtcNow },
            new AnuncioProprio { Id = semEventos, Titulo = "sem eventos", Texto = "t", TextoBotao = "b", LinkUrl = "/", Ativo = true, CriadoEm = DateTime.UtcNow.AddDays(-1) });
        var hoje = DateOnly.FromDateTime(BrasiliaTime.Hoje);
        ctx.AnunciosPropriosEstatisticas.AddRange(
            new AnuncioProprioEstatistica { AnuncioProprioId = a, Dia = hoje, Impressoes = 10, Cliques = 2 },
            new AnuncioProprioEstatistica { AnuncioProprioId = a, Dia = hoje.AddDays(-6), Impressoes = 5, Cliques = 1 },
            new AnuncioProprioEstatistica { AnuncioProprioId = a, Dia = hoje.AddDays(-7), Impressoes = 100, Cliques = 9 });
        await ctx.SaveChangesAsync();
        var controller = CreateController(ctx);

        var semana = Valor(await controller.Listar(7, CancellationToken.None));
        var tudo = Valor(await controller.Listar(0, CancellationToken.None));

        Assert.Equal((15, 3), (semana[0].Impressoes, semana[0].Cliques));
        Assert.Equal((115, 12), (tudo[0].Impressoes, tudo[0].Cliques));
        Assert.Equal((0, 0), (tudo[1].Impressoes, tudo[1].Cliques));
    }

    [Fact]
    public async Task Excluir_LevaJuntoAsEstatisticas()
    {
        using var ctx = CreateContext();
        var controller = CreateController(ctx);
        var criado = Valor(await controller.Criar(Dto(), CancellationToken.None));
        ctx.AnunciosPropriosEstatisticas.Add(new AnuncioProprioEstatistica { AnuncioProprioId = criado.Id, Dia = new DateOnly(2026, 11, 3), Impressoes = 1 });
        await ctx.SaveChangesAsync();

        await controller.Excluir(criado.Id, CancellationToken.None);

        Assert.Empty(ctx.AnunciosPropriosEstatisticas);
    }

    [Fact]
    public async Task Atualizar_TrocaCamposEMarcaAtualizadoEm()
    {
        using var ctx = CreateContext();
        var controller = CreateController(ctx);
        var criado = Valor(await controller.Criar(Dto(), CancellationToken.None));

        var dto = Valor(await controller.Atualizar(criado.Id, Dto("https://causify.com.br/blog", ativo: false), CancellationToken.None));

        Assert.False(dto.Ativo);
        Assert.Equal("https://causify.com.br/blog", dto.LinkUrl);
        Assert.NotNull((await ctx.AnunciosProprios.SingleAsync()).AtualizadoEm);
    }

    [Fact]
    public async Task Atualizar_Inexistente_NotFound()
    {
        using var ctx = CreateContext();

        var result = await CreateController(ctx).Atualizar(Guid.NewGuid(), Dto(), CancellationToken.None);

        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task Atualizar_LinkInvalido_BadRequest()
    {
        using var ctx = CreateContext();

        var result = await CreateController(ctx).Atualizar(Guid.NewGuid(), Dto("javascript:alert(1)"), CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task Excluir_RemoveEDepoisNotFound()
    {
        using var ctx = CreateContext();
        var controller = CreateController(ctx);
        var criado = Valor(await controller.Criar(Dto(), CancellationToken.None));

        Assert.IsType<NoContentResult>(await controller.Excluir(criado.Id, CancellationToken.None));
        Assert.Empty(ctx.AnunciosProprios);
        Assert.IsType<NotFoundResult>(await controller.Excluir(criado.Id, CancellationToken.None));
    }

    [Fact]
    public void Seed_TemCincoAnunciosAtivosQueLevamAAssinatura()
    {
        using var ctx = CreateContext();
        ctx.Database.EnsureCreated();

        var iniciais = ctx.AnunciosProprios.ToList();

        Assert.Equal(5, iniciais.Count);
        Assert.All(iniciais, a =>
        {
            Assert.True(a.Ativo);
            Assert.Equal("/pages/assinatura.html", a.LinkUrl);
            Assert.NotNull(a.Cor);
            Assert.Null(SuperAdminAnunciosPropriosController.Validar(new(a.Icone, a.Titulo, a.Texto, a.TextoBotao, a.LinkUrl, a.Ativo, a.Cor)));
        });
    }

    // ── Mistura com o Adsterra ───────────────────────────────────────────

    [Fact]
    public async Task ObterConfiguracao_SemLinha_Zero()
    {
        using var ctx = CreateContext();

        var config = Valor(await CreateController(ctx).ObterConfiguracao(CancellationToken.None));

        Assert.Equal(0, config.PercentualProprios);
        Assert.Null(config.AtualizadoEm);
    }

    [Fact]
    public async Task SalvarConfiguracao_CriaEDepoisAtualizaALinhaUnica()
    {
        using var ctx = CreateContext();
        var audit = new Mock<IAuditService>();
        var controller = CreateController(ctx, audit);

        Valor(await controller.SalvarConfiguracao(new(30), CancellationToken.None));
        var config = Valor(await controller.SalvarConfiguracao(new(75), CancellationToken.None));

        Assert.Equal(75, config.PercentualProprios);
        Assert.NotNull(config.AtualizadoEm);
        var linha = Assert.Single(ctx.ConfiguracoesPublicidade);
        Assert.Equal((ConfiguracaoPublicidade.IdUnico, 75), (linha.Id, linha.PercentualProprios));
        Assert.NotNull(linha.AtualizadoPorId);
        Assert.Equal(75, Valor(await controller.ObterConfiguracao(CancellationToken.None)).PercentualProprios);
        audit.Verify(a => a.LogAsync(It.Is<AuditLogEntry>(e => e.Entidade == "ConfiguracaoPublicidade"), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    public async Task SalvarConfiguracao_ForaDe0a100_BadRequest(int percentual)
    {
        using var ctx = CreateContext();

        var resultado = await CreateController(ctx).SalvarConfiguracao(new(percentual), CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(resultado.Result);
        Assert.Empty(ctx.ConfiguracoesPublicidade);
    }
}
