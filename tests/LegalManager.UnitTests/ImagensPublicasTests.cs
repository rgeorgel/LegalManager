using System.Security.Claims;
using LegalManager.API.Controllers;
using LegalManager.Application.DTOs.Imagens;
using LegalManager.Application.Interfaces;
using LegalManager.Domain.Entities;
using LegalManager.Infrastructure.Persistence;
using LegalManager.Infrastructure.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace LegalManager.UnitTests;

public class NomeImagemPublicaTests
{
    [Theory]
    [InlineData("Tela do Portal (1).PNG", ".png", "tela-do-portal-1.png")]
    [InlineData("Captura.jpeg", ".jpeg", "captura.jpeg")]
    [InlineData("???.png", ".png", "imagem.png")]
    [InlineData(null, ".webp", "imagem.webp")]
    [InlineData("foto.png", ".JPG", "foto.jpg")]
    public void Gerar(string? nome, string ext, string esperado) => Assert.Equal(esperado, NomeImagemPublica.Gerar(nome, ext));

    [Fact]
    public void Gerar_CortaNomesLongos()
    {
        var nome = NomeImagemPublica.Gerar(new string('a', 300), ".png");
        Assert.True(NomeImagemPublica.NomeValido(nome));
        Assert.Equal(NomeImagemPublica.TamanhoMaximoBase + 4, nome.Length);
    }

    [Fact]
    public void ProximoLivre_SomaSufixo()
    {
        var existentes = new HashSet<string> { "tela.png", "tela-2.png" };
        Assert.Equal("nova.png", NomeImagemPublica.ProximoLivre("nova.png", existentes));
        Assert.Equal("tela-3.png", NomeImagemPublica.ProximoLivre("tela.png", existentes));
    }

    [Theory]
    [InlineData("tela.png", true)]
    [InlineData("tela-1.webp", true)]
    [InlineData("tela.svg", false)]
    [InlineData("Tela.png", false)]
    [InlineData("../tela.png", false)]
    [InlineData("tela", false)]
    [InlineData(null, false)]
    public void NomeValido(string? nome, bool esperado) => Assert.Equal(esperado, NomeImagemPublica.NomeValido(nome));

    [Fact]
    public void PastaEUrl()
    {
        Assert.Equal("portal-do-cliente", NomeImagemPublica.Pasta("Portal do Cliente"));
        Assert.True(NomeImagemPublica.PastaValida("guias"));
        Assert.False(NomeImagemPublica.PastaValida("Guias"));
        Assert.False(NomeImagemPublica.PastaValida(new string('a', 61)));
        Assert.Equal(60, NomeImagemPublica.Pasta(new string('a', 80)).Length);
        Assert.Equal("/imagens/guias/tela.png", NomeImagemPublica.Url("guias", "tela.png"));
    }
}

public class SuperAdminImagensTests
{
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3];
    private static readonly byte[] Jpg = [0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3];

    private readonly Mock<IStorageService> _storage = new();

    private static AppDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private SuperAdminImagensController CreateController(AppDbContext ctx)
    {
        _storage.Setup(s => s.UploadAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("ok");
        var controller = new SuperAdminImagensController(ctx, _storage.Object, new Mock<IAuditService>().Object,
            NullLogger<SuperAdminImagensController>.Instance);
        var claims = new[] { new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()) };
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test")) }
        };
        return controller;
    }

    private static IFormFile Arquivo(string nome, byte[] bytes) =>
        new FormFile(new MemoryStream(bytes), 0, bytes.Length, "arquivo", nome);

    private static T Ok<T>(ActionResult<T> r) => Assert.IsType<T>(Assert.IsType<OkObjectResult>(r.Result).Value);

    [Fact]
    public async Task Enviar_CriaAPastaENormalizaONome()
    {
        using var ctx = CreateContext();
        var img = Ok(await CreateController(ctx).Enviar(Arquivo("Tela Inicial.PNG", Png), "Portal do Cliente", null, null, CancellationToken.None));

        Assert.Equal("tela-inicial.png", img.Nome);
        Assert.Equal("portal-do-cliente", img.Pasta);
        Assert.Equal("/imagens/portal-do-cliente/tela-inicial.png", img.Url);
        Assert.Equal("image/png", img.ContentType);
        var salva = Assert.Single(ctx.ImagensPublicas);
        Assert.StartsWith("imagens/", salva.ObjectKey);
        _storage.Verify(s => s.UploadAsync(It.IsAny<Stream>(), salva.ObjectKey, "image/png", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Enviar_NomeInformadoTemPrioridadeSobreOArquivo()
    {
        using var ctx = CreateContext();
        var img = Ok(await CreateController(ctx).Enviar(Arquivo("image.png", Png), "guias", "portal-do-cliente", null, CancellationToken.None));
        Assert.Equal("portal-do-cliente.png", img.Nome);
    }

    [Fact]
    public async Task Enviar_NomeRepetido_409ComSugestao_ERenomearSalvaComSufixo()
    {
        using var ctx = CreateContext();
        var controller = CreateController(ctx);
        await controller.Enviar(Arquivo("tela.png", Png), "guias", null, null, CancellationToken.None);

        var conflito = Assert.IsType<ConflictObjectResult>((await controller.Enviar(Arquivo("tela.png", Png), "guias", null, null, CancellationToken.None)).Result);
        var dto = Assert.IsType<ConflitoImagemDto>(conflito.Value);
        Assert.Equal("tela-2.png", dto.NomeSugerido);

        var renomeada = Ok(await controller.Enviar(Arquivo("tela.png", Png), "guias", null, SuperAdminImagensController.ConflitoRenomear, CancellationToken.None));
        Assert.Equal("tela-2.png", renomeada.Nome);
        Assert.Equal(2, ctx.ImagensPublicas.Count());
    }

    [Fact]
    public async Task Enviar_Substituir_MantemAUrlETrocaOArquivo()
    {
        using var ctx = CreateContext();
        var controller = CreateController(ctx);
        var original = Ok(await controller.Enviar(Arquivo("tela.png", Png), "guias", null, null, CancellationToken.None));
        var chaveAntiga = ctx.ImagensPublicas.Single().ObjectKey;

        var nova = Ok(await controller.Enviar(Arquivo("tela.png", Png), "guias", null, SuperAdminImagensController.ConflitoSubstituir, CancellationToken.None));

        Assert.Equal(original.Id, nova.Id);
        Assert.Equal(original.Url, nova.Url);
        Assert.NotEqual(chaveAntiga, ctx.ImagensPublicas.Single().ObjectKey);
        _storage.Verify(s => s.DeleteAsync(chaveAntiga, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Substituir_ExigeOMesmoTipo()
    {
        using var ctx = CreateContext();
        var controller = CreateController(ctx);
        var img = Ok(await controller.Enviar(Arquivo("tela.png", Png), "guias", null, null, CancellationToken.None));

        var result = await controller.Substituir(img.Id, Arquivo("tela.jpg", Jpg), CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result.Result);
        _storage.Verify(s => s.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Substituir_MesmoTipo_AtualizaETrocaChave()
    {
        using var ctx = CreateContext();
        var controller = CreateController(ctx);
        var img = Ok(await controller.Enviar(Arquivo("foto.jpg", Jpg), "guias", null, null, CancellationToken.None));
        var antes = ctx.ImagensPublicas.Single().AtualizadoEm;

        var nova = Ok(await controller.Substituir(img.Id, Arquivo("outra.jpeg", Jpg), CancellationToken.None));

        Assert.Equal("foto.jpg", nova.Nome);
        Assert.True(nova.AtualizadoEm >= antes);
        Assert.IsType<NotFoundResult>((await controller.Substituir(Guid.NewGuid(), Arquivo("x.png", Png), CancellationToken.None)).Result);
        Assert.IsType<BadRequestObjectResult>((await controller.Substituir(img.Id, null, CancellationToken.None)).Result);
        Assert.IsType<BadRequestObjectResult>((await controller.Substituir(img.Id, Arquivo("x.png", "<svg>"u8.ToArray()), CancellationToken.None)).Result);
    }

    [Fact]
    public async Task Enviar_RejeitaArquivoInvalidoPastaVaziaOuAusente()
    {
        using var ctx = CreateContext();
        var controller = CreateController(ctx);

        Assert.IsType<BadRequestObjectResult>((await controller.Enviar(Arquivo("x.svg", "<svg/>"u8.ToArray()), "guias", null, null, CancellationToken.None)).Result);
        Assert.IsType<BadRequestObjectResult>((await controller.Enviar(Arquivo("x.png", Png), "!!!", null, null, CancellationToken.None)).Result);
        Assert.IsType<BadRequestObjectResult>((await controller.Enviar(null, "guias", null, null, CancellationToken.None)).Result);
        _storage.Verify(s => s.UploadAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Enviar_FalhaNoStorage_502SemGravarNoBanco_ESubstituirMantemOArquivoAtual()
    {
        using var ctx = CreateContext();
        var controller = CreateController(ctx);
        var img = Ok(await controller.Enviar(Arquivo("tela.png", Png), "guias", null, null, CancellationToken.None));
        var chave = ctx.ImagensPublicas.Single().ObjectKey;
        _storage.Setup(s => s.UploadAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("storage fora"));

        var nova = Assert.IsType<ObjectResult>((await controller.Enviar(Arquivo("outra.png", Png), "guias", null, null, CancellationToken.None)).Result);
        Assert.Equal(StatusCodes.Status502BadGateway, nova.StatusCode);
        Assert.Single(ctx.ImagensPublicas);

        var troca = Assert.IsType<ObjectResult>((await controller.Substituir(img.Id, Arquivo("t.png", Png), CancellationToken.None)).Result);
        Assert.Equal(StatusCodes.Status502BadGateway, troca.StatusCode);
        Assert.Equal(chave, ctx.ImagensPublicas.Single().ObjectKey);
        _storage.Verify(s => s.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Renomear_MantemExtensao_ERecusaNomeRepetido()
    {
        using var ctx = CreateContext();
        var controller = CreateController(ctx);
        var a = Ok(await controller.Enviar(Arquivo("a.png", Png), "guias", null, null, CancellationToken.None));
        await controller.Enviar(Arquivo("b.png", Png), "guias", null, null, CancellationToken.None);

        var renomeada = Ok(await controller.Renomear(a.Id, new RenomearImagemDto("Nova Tela.jpg"), CancellationToken.None));
        Assert.Equal("nova-tela.png", renomeada.Nome);
        Assert.Equal("/imagens/guias/nova-tela.png", renomeada.Url);

        Assert.IsType<BadRequestObjectResult>((await controller.Renomear(a.Id, new RenomearImagemDto("b"), CancellationToken.None)).Result);
        Assert.Equal("nova-tela.png", Ok(await controller.Renomear(a.Id, new RenomearImagemDto("nova-tela"), CancellationToken.None)).Nome);
        Assert.IsType<NotFoundResult>((await controller.Renomear(Guid.NewGuid(), new RenomearImagemDto("x"), CancellationToken.None)).Result);
    }

    [Fact]
    public async Task Excluir_RemoveDoBancoEDoStorage_MesmoSeOStorageFalhar()
    {
        using var ctx = CreateContext();
        var controller = CreateController(ctx);
        var img = Ok(await controller.Enviar(Arquivo("a.png", Png), "guias", null, null, CancellationToken.None));
        var chave = ctx.ImagensPublicas.Single().ObjectKey;
        _storage.Setup(s => s.DeleteAsync(chave, It.IsAny<CancellationToken>())).ThrowsAsync(new Exception("storage fora"));

        Assert.IsType<NoContentResult>(await controller.Excluir(img.Id, CancellationToken.None));
        Assert.Empty(ctx.ImagensPublicas);
        _storage.Verify(s => s.DeleteAsync(chave, It.IsAny<CancellationToken>()), Times.Once);
        Assert.IsType<NotFoundResult>(await controller.Excluir(img.Id, CancellationToken.None));
    }

    [Fact]
    public async Task Pastas_CriarListarEExcluirSoVazia()
    {
        using var ctx = CreateContext();
        var controller = CreateController(ctx);

        var pasta = Ok(await controller.CriarPasta(new CriarPastaImagemDto("Marketing 2026"), CancellationToken.None));
        Assert.Equal("marketing-2026", pasta.Nome);
        Assert.IsType<BadRequestObjectResult>((await controller.CriarPasta(new CriarPastaImagemDto("marketing 2026"), CancellationToken.None)).Result);
        Assert.IsType<BadRequestObjectResult>((await controller.CriarPasta(new CriarPastaImagemDto("???"), CancellationToken.None)).Result);

        await controller.Enviar(Arquivo("a.png", Png), "marketing-2026", null, null, CancellationToken.None);
        var lista = Ok(await controller.ListarPastas(CancellationToken.None));
        Assert.Equal(1, Assert.Single(lista).Quantidade);

        Assert.IsType<BadRequestObjectResult>(await controller.ExcluirPasta(pasta.Id, CancellationToken.None));
        await controller.Excluir(ctx.ImagensPublicas.Single().Id, CancellationToken.None);
        Assert.IsType<NoContentResult>(await controller.ExcluirPasta(pasta.Id, CancellationToken.None));
        Assert.Empty(ctx.PastasImagens);
        Assert.IsType<NotFoundResult>(await controller.ExcluirPasta(pasta.Id, CancellationToken.None));
    }

    [Fact]
    public async Task Listar_FiltraPorPasta_EMostraOndeAImagemEUsada()
    {
        using var ctx = CreateContext();
        var controller = CreateController(ctx);
        var usada = Ok(await controller.Enviar(Arquivo("usada.png", Png), "guias", null, null, CancellationToken.None));
        await controller.Enviar(Arquivo("solta.png", Png), "guias", null, null, CancellationToken.None);
        await controller.Enviar(Arquivo("outra.png", Png), "marketing", null, null, CancellationToken.None);
        ctx.Artigos.Add(new Artigo { Id = Guid.NewGuid(), Titulo = "Guia do portal", Slug = "g", Resumo = "r", Categoria = "processos",
            Conteudo = $"Veja: ![tela]({usada.Url})", CriadoEm = DateTime.UtcNow });
        ctx.Novidades.Add(new Novidade { Id = Guid.NewGuid(), Titulo = "Novidade X", Descricao = "d", ImagemUrl = usada.Url, CriadoEm = DateTime.UtcNow });
        await ctx.SaveChangesAsync();

        var daPasta = Ok(await controller.Listar(usada.PastaId, CancellationToken.None));
        Assert.Equal(2, daPasta.Count);
        var comUso = daPasta.Single(i => i.Nome == "usada.png");
        Assert.Equal(["Guia", "Novidade"], comUso.Usos.Select(u => u.Tipo).OrderBy(t => t));
        Assert.Empty(daPasta.Single(i => i.Nome == "solta.png").Usos);

        Assert.Equal(3, Ok(await controller.Listar(null, CancellationToken.None)).Count);
    }
}

public class ImagensPublicasRotaTests
{
    private static AppDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static (ImagensController Controller, Mock<IStorageService> Storage) Criar(AppDbContext ctx, string? ifNoneMatch = null)
    {
        var storage = new Mock<IStorageService>();
        var controller = new ImagensController(ctx, storage.Object, NullLogger<ImagensController>.Instance);
        var http = new DefaultHttpContext();
        if (ifNoneMatch != null) http.Request.Headers.IfNoneMatch = ifNoneMatch;
        controller.ControllerContext = new ControllerContext { HttpContext = http };
        return (controller, storage);
    }

    private static ImagemPublica Seed(AppDbContext ctx)
    {
        var pasta = new PastaImagem { Id = Guid.NewGuid(), Nome = "guias", CriadoEm = DateTime.UtcNow };
        var img = new ImagemPublica
        {
            Id = Guid.NewGuid(), PastaId = pasta.Id, Pasta = pasta, Nome = "tela.png", ObjectKey = "imagens/abc.png",
            ContentType = "image/png", Tamanho = 3, CriadoEm = DateTime.UtcNow, AtualizadoEm = DateTime.UtcNow
        };
        ctx.PastasImagens.Add(pasta);
        ctx.ImagensPublicas.Add(img);
        ctx.SaveChanges();
        return img;
    }

    [Fact]
    public async Task Obter_ServeDoStorageComCacheCurtoEETag()
    {
        using var ctx = CreateContext();
        Seed(ctx);
        var (controller, storage) = Criar(ctx);
        storage.Setup(s => s.DownloadAsync("imagens/abc.png", It.IsAny<CancellationToken>())).ReturnsAsync(new MemoryStream([1, 2, 3]));

        var file = Assert.IsType<FileStreamResult>(await controller.Obter("guias", "tela.png", CancellationToken.None));

        Assert.Equal("image/png", file.ContentType);
        Assert.Equal(ImagensController.CachePublico, controller.Response.Headers.CacheControl.ToString());
        Assert.False(string.IsNullOrEmpty(controller.Response.Headers.ETag.ToString()));
    }

    [Fact]
    public async Task Obter_ETagIgual_304SemLerOStorage()
    {
        using var ctx = CreateContext();
        var img = Seed(ctx);
        var etag = $"\"{img.Id:N}-{img.AtualizadoEm.Ticks}\"";
        var (controller, storage) = Criar(ctx, ifNoneMatch: etag);

        var result = Assert.IsType<StatusCodeResult>(await controller.Obter("guias", "tela.png", CancellationToken.None));

        Assert.Equal(StatusCodes.Status304NotModified, result.StatusCode);
        storage.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("guias", "outra.png")]
    [InlineData("outra", "tela.png")]
    [InlineData("guias", "tela.svg")]
    [InlineData("Guias", "tela.png")]
    public async Task Obter_Inexistente404(string pasta, string nome)
    {
        using var ctx = CreateContext();
        Seed(ctx);
        var (controller, storage) = Criar(ctx);

        Assert.IsType<NotFoundResult>(await controller.Obter(pasta, nome, CancellationToken.None));
        storage.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Obter_ArquivoSumiuDoStorage_404SemCache()
    {
        using var ctx = CreateContext();
        Seed(ctx);
        var (controller, storage) = Criar(ctx, ifNoneMatch: "\"outra-versao\"");
        storage.Setup(s => s.DownloadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ThrowsAsync(new Exception("404"));

        Assert.IsType<NotFoundResult>(await controller.Obter("guias", "tela.png", CancellationToken.None));
        Assert.Equal("no-store", controller.Response.Headers.CacheControl.ToString());
    }
}
