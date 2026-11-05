using System.Security.Claims;
using System.Text;
using LegalManager.API.Controllers;
using LegalManager.Application.DTOs.Guias;
using LegalManager.Application.Interfaces;
using LegalManager.Domain.Entities;
using LegalManager.Domain.Enums;
using LegalManager.Infrastructure.Persistence;
using LegalManager.Infrastructure.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace LegalManager.UnitTests;

public class GuiaMarkdownTests
{
    [Fact]
    public void Renderizar_EscapaHtmlBruto()
    {
        var r = GuiaMarkdown.Renderizar("Olá <script>alert(1)</script> <img src=x onerror=alert(1)>");

        Assert.DoesNotContain("<script", r.Html);
        Assert.DoesNotContain("<img", r.Html);
        Assert.Contains("&lt;script&gt;", r.Html);
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("JavaScript:alert(1)")]
    [InlineData("data:text/html;base64,PHNjcmlwdD4=")]
    [InlineData("//outro-site.com")]
    [InlineData("vbscript:msgbox")]
    public void Renderizar_TrocaLinkPerigosoPorAncora(string url)
    {
        var r = GuiaMarkdown.Renderizar($"[clique]({url})");

        Assert.Contains("href=\"#\"", r.Html);
        Assert.DoesNotContain(url, r.Html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Renderizar_LinkExternoAbreEmNovaAba_EInternoNao()
    {
        var r = GuiaMarkdown.Renderizar("[site](https://oab.org.br) e [processos](/pages/processos.html)");

        Assert.Contains("<a href=\"https://oab.org.br\" target=\"_blank\" rel=\"noopener noreferrer\">site</a>", r.Html);
        Assert.Contains("<a href=\"/pages/processos.html\">processos</a>", r.Html);
    }

    [Theory]
    [InlineData("http://site.com/a.png", false)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("https://site.com/a.png", true)]
    [InlineData("/guias/img/abc.png", true)]
    public void Renderizar_ImagemSoDoSiteOuHttps(string url, bool aceita)
    {
        var r = GuiaMarkdown.Renderizar($"![tela]({url})");

        Assert.Contains("loading=\"lazy\"", r.Html);
        if (aceita) Assert.Contains($"src=\"{url}\"", r.Html);
        else Assert.Contains("src=\"\"", r.Html);
    }

    [Fact]
    public void Renderizar_SecoesDoIndiceSaoOsH2ComId()
    {
        var r = GuiaMarkdown.Renderizar("# Título\n\n## Liberar o acesso\n\ntexto\n\n### Detalhe\n\n## Enviar o `link` ao cliente\n");

        Assert.Equal(2, r.Secoes.Count);
        Assert.Equal("Liberar o acesso", r.Secoes[0].Titulo);
        Assert.Equal("liberar-o-acesso", r.Secoes[0].Id);
        Assert.Equal("Enviar o link ao cliente", r.Secoes[1].Titulo);
        Assert.Contains("id=\"liberar-o-acesso\"", r.Html);
    }

    [Fact]
    public void Renderizar_IdDaSecaoSemAcento()
    {
        var r = GuiaMarkdown.Renderizar("## Escolher o que o cliente vê");
        Assert.Equal("escolher-o-que-o-cliente-ve", Assert.Single(r.Secoes).Id);
    }

    [Fact]
    public void Renderizar_TabelaGanhaClasse()
    {
        var r = GuiaMarkdown.Renderizar("| Plano | Preço |\n|---|---|\n| Plus | 29 |\n");

        Assert.Contains("<table class=\"tabela\">", r.Html);
    }

    [Fact]
    public void MinutosDeLeitura_PeloMenosUm_E200PalavrasPorMinuto()
    {
        Assert.Equal(1, GuiaMarkdown.MinutosDeLeitura(null));
        Assert.Equal(1, GuiaMarkdown.MinutosDeLeitura("poucas palavras"));
        Assert.Equal(3, GuiaMarkdown.MinutosDeLeitura(string.Join(' ', Enumerable.Repeat("palavra", 600))));
    }

    [Fact]
    public void Renderizar_ConteudoNulo_DevolveVazio()
    {
        var r = GuiaMarkdown.Renderizar(null);

        Assert.Equal(string.Empty, r.Html);
        Assert.Empty(r.Secoes);
    }
}

public class GuiaSlugTests
{
    [Theory]
    [InlineData("Como liberar o Portal do Cliente?", "como-liberar-o-portal-do-cliente")]
    [InlineData("  Honorários & Ação — 2026 ", "honorarios-acao-2026")]
    [InlineData("", "")]
    [InlineData("!!!", "")]
    public void Gerar_TiraAcentosEPontuacao(string titulo, string esperado) =>
        Assert.Equal(esperado, GuiaSlug.Gerar(titulo));

    [Fact]
    public void Gerar_CortaEm120SemHifenNoFim()
    {
        var slug = GuiaSlug.Gerar(string.Join(' ', Enumerable.Repeat("abcdefghi", 30)));

        Assert.True(slug.Length <= GuiaSlug.TamanhoMaximo);
        Assert.False(slug.EndsWith('-'));
        Assert.True(GuiaSlug.Valido(slug));
    }

    [Theory]
    [InlineData("portal-do-cliente", true)]
    [InlineData("ia2", true)]
    [InlineData("Portal", false)]
    [InlineData("portal--cliente", false)]
    [InlineData("-portal", false)]
    [InlineData("portal_cliente", false)]
    [InlineData(null, false)]
    public void Valido(string? slug, bool esperado) => Assert.Equal(esperado, GuiaSlug.Valido(slug));
}

public class ImagemGuiaTests
{
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0];

    [Theory]
    [InlineData("tela.png", new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, ".png", "image/png")]
    [InlineData("Foto.JPG", new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 }, ".jpg", "image/jpeg")]
    [InlineData("anim.gif", new byte[] { 0x47, 0x49, 0x46, 0x38, 0x39, 0x61 }, ".gif", "image/gif")]
    [InlineData("a.webp", new byte[] { 0x52, 0x49, 0x46, 0x46, 0, 0, 0, 0, 0x57, 0x45, 0x42, 0x50 }, ".webp", "image/webp")]
    public void Validar_AceitaImagensConhecidas(string nome, byte[] bytes, string ext, string tipo)
    {
        using var s = new MemoryStream(bytes);
        var r = ImagemGuia.Validar(s, nome, bytes.Length);

        Assert.Equal(ext, r.Extensao);
        Assert.Equal(tipo, r.ContentType);
        Assert.Equal(0, s.Position);
    }

    [Fact]
    public void Validar_RejeitaSvg()
    {
        using var s = new MemoryStream("<svg onload=alert(1)>"u8.ToArray());
        Assert.Throws<ArgumentException>(() => ImagemGuia.Validar(s, "x.svg", s.Length));
    }

    [Fact]
    public void Validar_RejeitaConteudoQueNaoBateComAExtensao()
    {
        using var s = new MemoryStream("<html><script>"u8.ToArray());
        var ex = Assert.Throws<ArgumentException>(() => ImagemGuia.Validar(s, "x.png", s.Length));
        Assert.Contains("PNG", ex.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(ImagemGuia.TamanhoMaximo + 1)]
    public void Validar_RejeitaVazioOuGrandeDemais(long tamanho)
    {
        using var s = new MemoryStream(Png);
        Assert.Throws<ArgumentException>(() => ImagemGuia.Validar(s, "x.png", tamanho));
    }

    [Theory]
    [InlineData("0123456789abcdef0123456789abcdef.png", true)]
    [InlineData("0123456789abcdef0123456789abcdef.webp", true)]
    [InlineData("0123456789abcdef0123456789abcdef.svg", false)]
    [InlineData("../segredo.png", false)]
    [InlineData("abc.png", false)]
    [InlineData(null, false)]
    public void NomeServivel(string? nome, bool esperado) => Assert.Equal(esperado, ImagemGuia.NomeServivel(nome));

    [Fact]
    public void ObjectKeyEContentType()
    {
        Assert.Equal("guias/a.png", ImagemGuia.ObjectKey("a.png"));
        Assert.Equal("image/gif", ImagemGuia.ContentTypeDe("0123456789abcdef0123456789abcdef.gif"));
    }
}

public class SuperAdminGuiasTests
{
    private static AppDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static SuperAdminGuiasController CreateController(AppDbContext ctx, IStorageService? storage = null)
    {
        var controller = new SuperAdminGuiasController(ctx, new Mock<IAuditService>().Object, storage ?? new Mock<IStorageService>().Object);
        var claims = new[] { new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()) };
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test")) }
        };
        return controller;
    }

    private static SalvarGuiaDto Dto(string titulo = "Como liberar o Portal do Cliente", string? slug = null, bool publicado = true,
        string categoria = "portal-do-cliente", string? capa = null, string? link = null, string? linkTexto = null) =>
        new(titulo, slug, "Resumo curto.", "## Passo 1\n\nAbra a tela.", categoria, capa, link, linkTexto, publicado);

    [Fact]
    public async Task Criar_GeraSlugPeloTitulo_EPublicaComData()
    {
        using var ctx = CreateContext();
        var result = await CreateController(ctx).Criar(Dto(), CancellationToken.None);

        var dto = Assert.IsType<GuiaAdminDto>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Equal("como-liberar-o-portal-do-cliente", dto.Slug);
        var a = Assert.Single(ctx.Artigos);
        Assert.True(a.Publicado);
        Assert.NotNull(a.PublicadoEm);
        Assert.NotNull(a.CriadoPorId);
    }

    [Fact]
    public async Task Criar_RejeitaSlugRepetido()
    {
        using var ctx = CreateContext();
        var controller = CreateController(ctx);
        await controller.Criar(Dto(), CancellationToken.None);

        var result = await controller.Criar(Dto(titulo: "Outro", slug: "como-liberar-o-portal-do-cliente"), CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Single(ctx.Artigos);
    }

    [Theory]
    [InlineData("Com Espaço")]
    [InlineData("acentuação")]
    public async Task Criar_RejeitaSlugInvalido(string slug)
    {
        using var ctx = CreateContext();
        var result = await CreateController(ctx).Criar(Dto(slug: slug), CancellationToken.None);
        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task Criar_RejeitaCategoriaDesconhecida()
    {
        using var ctx = CreateContext();
        var result = await CreateController(ctx).Criar(Dto(categoria: "inexistente"), CancellationToken.None);
        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Theory]
    [InlineData("https://outro-site.com")]
    [InlineData("//outro-site.com")]
    [InlineData("javascript:alert(1)")]
    public async Task Criar_RejeitaLinkForaDoSistema(string link)
    {
        using var ctx = CreateContext();
        var result = await CreateController(ctx).Criar(Dto(link: link), CancellationToken.None);
        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Theory]
    [InlineData("http://site.com/capa.png")]
    [InlineData("javascript:alert(1)")]
    public async Task Criar_RejeitaCapaSemHttpsNemCaminhoLocal(string capa)
    {
        using var ctx = CreateContext();
        var result = await CreateController(ctx).Criar(Dto(capa: capa), CancellationToken.None);
        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Theory]
    [InlineData("", "Resumo", "Conteúdo")]
    [InlineData("Título", " ", "Conteúdo")]
    [InlineData("Título", "Resumo", "")]
    public async Task Criar_ExigeTituloResumoEConteudo(string titulo, string resumo, string conteudo)
    {
        using var ctx = CreateContext();
        var dto = new SalvarGuiaDto(titulo, "slug-ok", resumo, conteudo, "processos", null, null, null, false);
        var result = await CreateController(ctx).Criar(dto, CancellationToken.None);
        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task Criar_RascunhoSemData_ETextoDoBotaoSemLinkEDescartado()
    {
        using var ctx = CreateContext();
        await CreateController(ctx).Criar(Dto(publicado: false, linkTexto: "Abrir"), CancellationToken.None);

        var a = Assert.Single(ctx.Artigos);
        Assert.False(a.Publicado);
        Assert.Null(a.PublicadoEm);
        Assert.Null(a.LinkTexto);
    }

    [Fact]
    public async Task Atualizar_RepublicarNaoMudaData_EPodeManterOProprioSlug()
    {
        using var ctx = CreateContext();
        var controller = CreateController(ctx);
        var criado = (GuiaAdminDto)((OkObjectResult)(await controller.Criar(Dto(), CancellationToken.None)).Result!).Value!;
        var a = ctx.Artigos.Single();
        var publicadoEm = a.PublicadoEm;

        await controller.Atualizar(criado.Id, Dto(slug: criado.Slug, publicado: false), CancellationToken.None);
        var result = await controller.Atualizar(criado.Id, Dto(slug: criado.Slug, link: "/pages/processos.html"), CancellationToken.None);

        Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal(publicadoEm, a.PublicadoEm);
        Assert.Equal("/pages/processos.html", a.LinkUrl);
        Assert.NotNull(a.AtualizadoEm);
    }

    [Fact]
    public async Task Atualizar_Inexistente_404()
    {
        using var ctx = CreateContext();
        var result = await CreateController(ctx).Atualizar(Guid.NewGuid(), Dto(), CancellationToken.None);
        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task Listar_RascunhosPrimeiro_ComContagemDeCurtidas()
    {
        using var ctx = CreateContext();
        var publicado = new Artigo { Id = Guid.NewGuid(), Titulo = "P", Slug = "p", Resumo = "r", Conteudo = "c", Categoria = "processos", Publicado = true, PublicadoEm = DateTime.UtcNow, CriadoEm = DateTime.UtcNow };
        var rascunho = new Artigo { Id = Guid.NewGuid(), Titulo = "R", Slug = "r", Resumo = "r", Conteudo = "c", Categoria = "processos", CriadoEm = DateTime.UtcNow };
        ctx.Artigos.AddRange(publicado, rascunho);
        ctx.ArtigosCurtidas.Add(new ArtigoCurtida { ArtigoId = publicado.Id, UsuarioId = Guid.NewGuid(), CriadoEm = DateTime.UtcNow });
        await ctx.SaveChangesAsync();

        var result = await CreateController(ctx).Listar(CancellationToken.None);

        var lista = Assert.IsType<List<GuiaAdminDto>>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Equal(["R", "P"], lista.Select(g => g.Titulo));
        Assert.Equal(1, lista[1].Curtidas);
    }

    [Fact]
    public async Task Excluir_RemoveO_Guia()
    {
        using var ctx = CreateContext();
        var controller = CreateController(ctx);
        await controller.Criar(Dto(), CancellationToken.None);

        Assert.IsType<NoContentResult>(await controller.Excluir(ctx.Artigos.Single().Id, CancellationToken.None));
        Assert.Empty(ctx.Artigos);
        Assert.IsType<NotFoundResult>(await controller.Excluir(Guid.NewGuid(), CancellationToken.None));
    }

    [Fact]
    public void Categorias_ListaTodas()
    {
        using var ctx = CreateContext();
        var result = CreateController(ctx).Categorias();
        var lista = Assert.IsType<List<CategoriaGuiaDto>>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Contains(lista, c => c.Slug == "inteligencia-artificial");
    }

    [Fact]
    public void Previa_RenderizaComOMesmoMarkdownDaPagina()
    {
        using var ctx = CreateContext();
        var result = CreateController(ctx).Previa(new PreviaGuiaDto("**oi** <b>x</b>"));

        var dto = Assert.IsType<PreviaGuiaResultadoDto>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Contains("<strong>oi</strong>", dto.Html);
        Assert.Contains("&lt;b&gt;", dto.Html);
        Assert.Equal(1, dto.MinutosLeitura);
    }

    private static IFormFile Arquivo(string nome, byte[] bytes) =>
        new FormFile(new MemoryStream(bytes), 0, bytes.Length, "arquivo", nome);

    [Fact]
    public async Task EnviarImagem_ValidaESobeNoStorage()
    {
        using var ctx = CreateContext();
        var storage = new Mock<IStorageService>();
        string? chave = null;
        storage.Setup(s => s.UploadAsync(It.IsAny<Stream>(), It.IsAny<string>(), "image/png", It.IsAny<CancellationToken>()))
            .Callback<Stream, string, string, CancellationToken>((_, k, _, _) => chave = k)
            .ReturnsAsync("ok");

        var result = await CreateController(ctx, storage.Object)
            .EnviarImagem(Arquivo("tela.png", [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2]), CancellationToken.None);

        var dto = Assert.IsType<ImagemGuiaDto>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.StartsWith("/guias/img/", dto.Url);
        Assert.EndsWith(".png", dto.Url);
        Assert.Equal("guias/" + dto.Url["/guias/img/".Length..], chave);
        Assert.True(ImagemGuia.NomeServivel(dto.Url["/guias/img/".Length..]));
    }

    [Fact]
    public async Task EnviarImagem_RejeitaArquivoInvalidoOuAusente()
    {
        using var ctx = CreateContext();
        var storage = new Mock<IStorageService>();
        var controller = CreateController(ctx, storage.Object);

        Assert.IsType<BadRequestObjectResult>((await controller.EnviarImagem(Arquivo("x.png", "<html>"u8.ToArray()), CancellationToken.None)).Result);
        Assert.IsType<BadRequestObjectResult>((await controller.EnviarImagem(null, CancellationToken.None)).Result);
        storage.VerifyNoOtherCalls();
    }
}

public class GuiasPublicosTests
{
    private static AppDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static GuiasController CreateController(AppDbContext ctx, ClaimsPrincipal? user = null, IStorageService? storage = null,
        bool lancado = true)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["App:FrontendUrl"] = "https://causify.com.br/",
                ["Guias:Lancado"] = lancado.ToString(),
            })
            .Build();
        var controller = new GuiasController(ctx, storage ?? new Mock<IStorageService>().Object, config, NullLogger<GuiasController>.Instance);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = user ?? new ClaimsPrincipal(new ClaimsIdentity()) }
        };
        return controller;
    }

    private static Artigo AddArtigo(AppDbContext ctx, string slug, bool publicado = true, string categoria = "processos",
        string titulo = "Guia", DateTime? publicadoEm = null, string? link = null)
    {
        var a = new Artigo
        {
            Id = Guid.NewGuid(), Titulo = titulo, Slug = slug, Resumo = "Resumo de " + slug,
            Conteudo = "## Primeiro\n\ntexto\n\n## Segundo\n\n## Terceiro\n", Categoria = categoria,
            Publicado = publicado, PublicadoEm = publicado ? publicadoEm ?? DateTime.UtcNow : null,
            CriadoEm = DateTime.UtcNow, LinkUrl = link, LinkTexto = link == null ? null : "Abrir contratos"
        };
        ctx.Artigos.Add(a);
        return a;
    }

    private static Usuario AddUsuario(AppDbContext ctx)
    {
        var tenant = new Tenant { Id = Guid.NewGuid(), Nome = "Escritório", Plano = PlanoTipo.Free, Status = StatusTenant.Ativo, CriadoEm = DateTime.UtcNow };
        ctx.Tenants.Add(tenant);
        var u = new Usuario { Id = Guid.NewGuid(), TenantId = tenant.Id, Nome = "Ana", Email = "ana@a.com", UserName = "ana@a.com", CriadoEm = DateTime.UtcNow };
        ctx.Users.Add(u);
        return u;
    }

    private static ClaimsPrincipal Logado(Guid id, string? role = null)
    {
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, id.ToString()) };
        if (role != null) claims.Add(new Claim(ClaimTypes.Role, role));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
    }

    private static string Html(IActionResult result) => Assert.IsType<ContentResult>(result).Content!;

    [Fact]
    public async Task Indice_MostraSoPublicados_ComFiltroDeCategoriasExistentes()
    {
        using var ctx = CreateContext();
        AddArtigo(ctx, "publicado-processos", titulo: "Cadastrar processo");
        AddArtigo(ctx, "publicado-ia", categoria: "inteligencia-artificial", titulo: "Resumo com IA");
        AddArtigo(ctx, "rascunho", publicado: false, titulo: "Rascunho secreto");
        await ctx.SaveChangesAsync();

        var result = await CreateController(ctx).Indice(null, CancellationToken.None);
        var html = Html(result);

        Assert.Equal(200, ((ContentResult)result).StatusCode);
        Assert.Contains("Cadastrar processo", html);
        Assert.Contains("Resumo com IA", html);
        Assert.DoesNotContain("Rascunho secreto", html);
        Assert.Contains("href=\"/guias?categoria=inteligencia-artificial\"", html);
        Assert.DoesNotContain("categoria=honorarios", html);
        Assert.Contains("<link rel=\"canonical\" href=\"https://causify.com.br/guias\">", html);
    }

    [Fact]
    public async Task Indice_FiltraPorCategoria_EDesconhecidaMostraTodos()
    {
        using var ctx = CreateContext();
        AddArtigo(ctx, "a", titulo: "Guia de processos");
        AddArtigo(ctx, "b", categoria: "honorarios", titulo: "Guia de honorários");
        await ctx.SaveChangesAsync();
        var controller = CreateController(ctx);

        var filtrado = Html(await controller.Indice("honorarios", CancellationToken.None));
        Assert.Contains("Guia de honorários", filtrado);
        Assert.DoesNotContain("Guia de processos", filtrado);
        Assert.Contains("aria-current=\"page\">Honorários", filtrado);

        var todos = Html(await controller.Indice("nao-existe", CancellationToken.None));
        Assert.Contains("Guia de processos", todos);
        Assert.Contains("Guia de honorários", todos);
    }

    [Fact]
    public async Task Indice_Vazio_MostraMensagem()
    {
        using var ctx = CreateContext();
        var html = Html(await CreateController(ctx).Indice(null, CancellationToken.None));
        Assert.Contains("Nenhum guia publicado ainda", html);
    }

    [Fact]
    public async Task Artigo_RascunhoOuInexistente_404()
    {
        using var ctx = CreateContext();
        AddArtigo(ctx, "rascunho", publicado: false);
        await ctx.SaveChangesAsync();
        var controller = CreateController(ctx);

        var rascunho = Assert.IsType<ContentResult>(await controller.Artigo("rascunho", CancellationToken.None));
        Assert.Equal(404, rascunho.StatusCode);
        Assert.Contains("noindex", rascunho.Content);
        Assert.Equal(404, Assert.IsType<ContentResult>(await controller.Artigo("nada", CancellationToken.None)).StatusCode);
    }

    [Fact]
    public async Task Artigo_RenderizaComSeoIndiceEscapeERelacionados()
    {
        using var ctx = CreateContext();
        var a = AddArtigo(ctx, "contratos", categoria: "honorarios", titulo: "Contratos <b>&</b> parcelas", link: "/pages/honorarios-contratos.html");
        a.CapaUrl = "/guias/img/capa.png";
        AddArtigo(ctx, "mesma-categoria", categoria: "honorarios", titulo: "Mesma categoria", publicadoEm: DateTime.UtcNow.AddDays(-1));
        AddArtigo(ctx, "outra-categoria", categoria: "processos", titulo: "Outra categoria", publicadoEm: DateTime.UtcNow.AddDays(-2));
        AddArtigo(ctx, "rascunho", publicado: false, titulo: "Rascunho");
        ctx.ArtigosCurtidas.Add(new ArtigoCurtida { ArtigoId = a.Id, UsuarioId = Guid.NewGuid(), CriadoEm = DateTime.UtcNow });
        await ctx.SaveChangesAsync();

        var html = Html(await CreateController(ctx).Artigo("contratos", CancellationToken.None));

        Assert.Contains("<h1>Contratos &lt;b&gt;&amp;&lt;/b&gt; parcelas</h1>", html);
        Assert.DoesNotContain("<b>&</b>", html);
        Assert.Contains("<link rel=\"canonical\" href=\"https://causify.com.br/guias/contratos\">", html);
        Assert.Contains("<meta property=\"og:image\" content=\"https://causify.com.br/guias/img/capa.png\">", html);
        Assert.Contains("\"@type\":\"TechArticle\"", html);
        Assert.Contains("Neste guia", html);
        Assert.Contains("href=\"#primeiro\"", html);
        Assert.Contains("data-logado=\"/pages/honorarios-contratos.html\"", html);
        Assert.Contains("id=\"contadorUtil\">1<", html);
        // Mesma categoria antes das outras; rascunho nunca.
        Assert.True(html.IndexOf("Mesma categoria", StringComparison.Ordinal) < html.IndexOf("Outra categoria", StringComparison.Ordinal));
        Assert.DoesNotContain(">Rascunho<", html);
    }

    [Fact]
    public async Task Artigo_SemLinkNemSecoes_MostraCadastroESemIndice()
    {
        using var ctx = CreateContext();
        var a = AddArtigo(ctx, "simples");
        a.Conteudo = "Só um parágrafo.";
        await ctx.SaveChangesAsync();

        var html = Html(await CreateController(ctx).Artigo("simples", CancellationToken.None));

        Assert.DoesNotContain("Neste guia", html);
        Assert.Contains("Ainda não usa o Causify?", html);
        Assert.Contains("images/blog/og-default.png", html);
    }

    [Fact]
    public async Task Sitemap_ListaSoPublicados()
    {
        using var ctx = CreateContext();
        AddArtigo(ctx, "publicado");
        AddArtigo(ctx, "rascunho", publicado: false);
        await ctx.SaveChangesAsync();

        var result = Assert.IsType<ContentResult>(await CreateController(ctx).Sitemap(CancellationToken.None));

        Assert.StartsWith("application/xml", result.ContentType);
        Assert.Contains("<loc>https://causify.com.br/guias/publicado</loc>", result.Content);
        Assert.Contains("<loc>https://causify.com.br/guias</loc>", result.Content);
        Assert.DoesNotContain("rascunho", result.Content);
    }

    [Fact]
    public async Task Lancado_PaginasIndexaveisESemCabecalhoNoindex()
    {
        using var ctx = CreateContext();
        AddArtigo(ctx, "guia");
        await ctx.SaveChangesAsync();
        var controller = CreateController(ctx);

        var html = Html(await controller.Artigo("guia", CancellationToken.None));

        Assert.Contains("content=\"index, follow", html);
        Assert.False(controller.Response.Headers.ContainsKey("X-Robots-Tag"));
    }

    [Fact]
    public async Task PreLancamento_AbrePorLinkDireto_MasComNoindexESemSitemap()
    {
        using var ctx = CreateContext();
        AddArtigo(ctx, "guia", titulo: "Guia em revisão");
        await ctx.SaveChangesAsync();
        var controller = CreateController(ctx, lancado: false);

        var artigo = Assert.IsType<ContentResult>(await controller.Artigo("guia", CancellationToken.None));
        Assert.Equal(200, artigo.StatusCode);
        Assert.Contains("Guia em revisão", artigo.Content);
        Assert.Contains("<meta name=\"robots\" content=\"noindex\">", artigo.Content);
        Assert.Equal("noindex", controller.Response.Headers["X-Robots-Tag"].ToString());

        var indice = Html(await controller.Indice(null, CancellationToken.None));
        Assert.Contains("<meta name=\"robots\" content=\"noindex\">", indice);

        Assert.IsType<NotFoundResult>(await controller.Sitemap(CancellationToken.None));
    }

    [Fact]
    public async Task Imagem_NomeInvalido_404_SemTocarNoStorage()
    {
        using var ctx = CreateContext();
        var storage = new Mock<IStorageService>();
        var result = await CreateController(ctx, storage: storage.Object).Imagem("../x.png", CancellationToken.None);

        Assert.IsType<NotFoundResult>(result);
        storage.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Imagem_ServeDoStorageComCacheLongo_EFaltaVira404()
    {
        using var ctx = CreateContext();
        const string nome = "0123456789abcdef0123456789abcdef.png";
        var storage = new Mock<IStorageService>();
        storage.Setup(s => s.DownloadAsync("guias/" + nome, It.IsAny<CancellationToken>())).ReturnsAsync(new MemoryStream([1, 2, 3]));
        storage.Setup(s => s.DownloadAsync("guias/fedcba9876543210fedcba9876543210.png", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("404 do storage"));
        var controller = CreateController(ctx, storage: storage.Object);

        var ok = Assert.IsType<FileStreamResult>(await controller.Imagem(nome, CancellationToken.None));
        Assert.Equal("image/png", ok.ContentType);
        Assert.Contains("immutable", controller.Response.Headers.CacheControl.ToString());

        Assert.IsType<NotFoundResult>(await controller.Imagem("fedcba9876543210fedcba9876543210.png", CancellationToken.None));
    }

    [Fact]
    public async Task Curtir_EIdempotente_EDescurtirRemove()
    {
        using var ctx = CreateContext();
        var u = AddUsuario(ctx);
        var a = AddArtigo(ctx, "g");
        await ctx.SaveChangesAsync();
        var controller = CreateController(ctx, Logado(u.Id));

        var antes = Assert.IsType<CurtidaGuiaDto>(Assert.IsType<OkObjectResult>((await controller.Curtida(a.Id, CancellationToken.None)).Result).Value);
        Assert.Equal(new CurtidaGuiaDto(false, 0), antes);

        await controller.Curtir(a.Id, CancellationToken.None);
        var curtido = Assert.IsType<CurtidaGuiaDto>(Assert.IsType<OkObjectResult>((await controller.Curtir(a.Id, CancellationToken.None)).Result).Value);
        Assert.Equal(new CurtidaGuiaDto(true, 1), curtido);

        var descurtido = Assert.IsType<CurtidaGuiaDto>(Assert.IsType<OkObjectResult>((await controller.Descurtir(a.Id, CancellationToken.None)).Result).Value);
        Assert.Equal(new CurtidaGuiaDto(false, 0), descurtido);
        Assert.Empty(ctx.ArtigosCurtidas);
    }

    [Fact]
    public async Task Curtir_GuiaNaoPublicado_404()
    {
        using var ctx = CreateContext();
        var u = AddUsuario(ctx);
        var a = AddArtigo(ctx, "g", publicado: false);
        await ctx.SaveChangesAsync();
        var controller = CreateController(ctx, Logado(u.Id));

        Assert.IsType<NotFoundResult>((await controller.Curtir(a.Id, CancellationToken.None)).Result);
        Assert.IsType<NotFoundResult>((await controller.Curtida(a.Id, CancellationToken.None)).Result);
    }

    [Fact]
    public async Task Curtir_ClienteDoPortalOuUsuarioDesconhecido_Proibido()
    {
        using var ctx = CreateContext();
        var u = AddUsuario(ctx);
        var a = AddArtigo(ctx, "g");
        await ctx.SaveChangesAsync();

        Assert.IsType<ForbidResult>((await CreateController(ctx, Logado(u.Id, "Cliente")).Curtir(a.Id, CancellationToken.None)).Result);
        Assert.IsType<ForbidResult>((await CreateController(ctx, Logado(Guid.NewGuid())).Curtir(a.Id, CancellationToken.None)).Result);
        Assert.IsType<ForbidResult>((await CreateController(ctx).Curtida(a.Id, CancellationToken.None)).Result);
        Assert.IsType<ForbidResult>((await CreateController(ctx).Descurtir(a.Id, CancellationToken.None)).Result);
        Assert.Empty(ctx.ArtigosCurtidas);
    }
}
