using System.Text;
using LegalManager.API.Controllers;
using LegalManager.Application.DTOs.IA;
using LegalManager.Application.Interfaces;
using LegalManager.Domain.Entities;
using LegalManager.Domain.Enums;
using LegalManager.Domain.Interfaces;
using LegalManager.Infrastructure.Jurisprudencia;
using LegalManager.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace LegalManager.UnitTests;

public class JurisprudenciaServiceTests
{
    private static readonly Guid TenantId = Guid.NewGuid();

    private static (AppDbContext db, JurisprudenciaService svc, Mock<IStorageService> storage) Montar()
    {
        var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var storage = new Mock<IStorageService>();
        var tenant = Mock.Of<ITenantContext>(t => t.TenantId == TenantId);
        return (db, new JurisprudenciaService(db, tenant, storage.Object), storage);
    }

    private static Precedente Acordao(string classe, string numero, string relator = "NANCY ANDRIGHI", string texto = "Ementa.") => new()
    {
        Id = Guid.NewGuid(), Tipo = TipoPrecedente.Acordao, Tribunal = "STJ", Classe = classe,
        ClasseBase = ExtratorCitacoes.ClasseBase(classe), Numero = numero, Relator = relator,
        OrgaoJulgador = "TERCEIRA TURMA", DataJulgamento = new DateTime(2026, 5, 12), Texto = texto,
        UrlFonte = "https://processo.stj.jus.br/processo/pesquisa/?num_registro=1", Fonte = "stj-espelhos-terceira-turma",
        ExternoId = Guid.NewGuid().ToString("N")[..10], ImportadoEm = DateTime.UtcNow,
    };

    private static Precedente Tema(string numero, bool cancelado = false) => new()
    {
        Id = Guid.NewGuid(), Tipo = TipoPrecedente.TemaRepetitivo, Tribunal = "STJ", Numero = numero,
        Texto = "Tese firmada.", Situacao = cancelado ? "Cancelado" : "Trânsito em Julgado", Cancelado = cancelado,
        UrlFonte = "https://x", Fonte = StjDadosAbertos.FonteTemas, ExternoId = numero, ImportadoEm = DateTime.UtcNow,
    };

    private static async Task<AppDbContext> ComBaseAsync(AppDbContext db)
    {
        db.Precedentes.AddRange(
            Acordao("AgInt no AREsp", "2104552"),
            Acordao("REsp", "1811334", relator: "RAUL ARAÚJO"),
            Tema("1076"), Tema("56", cancelado: true));
        db.FontesPrecedentes.AddRange(
            new FontePrecedentes { Chave = StjDadosAbertos.FonteTemas, ListaCompleta = true, Total = 2 },
            new FontePrecedentes { Chave = "stj-espelhos-terceira-turma", Total = 2 });
        await db.SaveChangesAsync();
        return db;
    }

    private static CitacaoVerificadaDto Status(VerificacaoCitacoesDto r, string numero) =>
        Assert.Single(r.Citacoes, c => c.Numero == numero);

    [Fact]
    public async Task Conferencia_CadaStatus()
    {
        var (db, svc, _) = Montar();
        await ComBaseAsync(db);
        const string texto = """
            Conforme o AgInt no AREsp 2.104.552/SP, Rel. Min. Nancy Andrighi; o REsp 1.811.334/MG,
            Rel. Min. Nancy Andrighi; o REsp 9.999.999/SP; o Tema 1.076/STJ; o Tema 56/STJ; o Tema 9.876/STJ;
            a Súmula 7/STJ; o RE 1.010.606 (STF); e a Apelação 1004521-33.2023.8.26.0100.
            """;

        var r = await svc.VerificarTextoAsync(texto, consultarBase: true);

        Assert.Equal(StatusCitacao.Verificada, Status(r, "2104552").Status);
        Assert.Equal("https://processo.stj.jus.br/processo/pesquisa/?num_registro=1", Status(r, "2104552").Precedente!.UrlFonte);
        var divergente = Status(r, "1811334");
        Assert.Equal(StatusCitacao.Divergente, divergente.Status);
        Assert.Contains("Raul Araújo", divergente.Mensagem);
        Assert.Equal(StatusCitacao.NaoEncontrada, Status(r, "9999999").Status);
        Assert.Equal(StatusCitacao.Verificada, Status(r, "1076").Status);
        Assert.Equal(StatusCitacao.Cancelada, Status(r, "56").Status);
        Assert.Equal(StatusCitacao.Inexistente, Status(r, "9876").Status);
        Assert.Equal(StatusCitacao.NaoVerificada, Status(r, "7").Status);       // súmulas: ainda sem fonte
        Assert.Equal(StatusCitacao.NaoVerificada, Status(r, "1010606").Status); // STF: ainda sem fonte
        Assert.Equal(StatusCitacao.NaoVerificada, Status(r, "10045213320238260100").Status);

        Assert.Equal(2, r.Verificadas);
        Assert.Equal(1, r.Inexistentes);
        Assert.Equal(6, r.Alertas);
        Assert.Contains("temas repetitivos do STJ", r.Cobertura);
    }

    [Fact]
    public async Task SemConsultarBase_TudoNaoVerificado()
    {
        var (db, svc, _) = Montar();
        await ComBaseAsync(db);

        var r = await svc.VerificarTextoAsync("o AgInt no AREsp 2.104.552/SP e o Tema 9.876/STJ", consultarBase: false);

        Assert.Equal(2, r.Citacoes.Count);
        Assert.All(r.Citacoes, c => Assert.Equal(StatusCitacao.NaoVerificada, c.Status));
        Assert.Equal(0, r.Inexistentes);
    }

    [Fact]
    public async Task TemaInexistente_SoQuandoAListaEstaCompleta()
    {
        var (db, svc, _) = Montar();
        db.Precedentes.Add(Tema("1076"));
        await db.SaveChangesAsync(); // sem FontePrecedentes marcada como completa

        var r = await svc.VerificarTextoAsync("Tema 9.876/STJ", consultarBase: true);

        Assert.Equal(StatusCitacao.NaoVerificada, Assert.Single(r.Citacoes).Status);
        Assert.Contains("ainda está sendo carregada", r.Cobertura);
    }

    [Fact]
    public async Task AcordaoSemTribunalNoTexto_EncontradoNoStj()
    {
        var (db, svc, _) = Montar();
        var hc = Acordao("AgRg no HC", "654321");
        db.Precedentes.Add(hc);
        await db.SaveChangesAsync();

        var r = await svc.VerificarTextoAsync("o AgRg no HC 654.321/SP e o HC 111.222/RJ.", consultarBase: true);

        Assert.Equal(StatusCitacao.Verificada, Status(r, "654321").Status);
        Assert.Equal(StatusCitacao.NaoVerificada, Status(r, "111222").Status); // sem tribunal e fora da base
    }

    [Fact]
    public async Task BuscarPrecedentes_OrdenaPelosTermosESemCancelados()
    {
        var (db, svc, _) = Montar();
        db.Precedentes.AddRange(
            Acordao("REsp", "1", texto: "Busca e apreensão. Alienação fiduciária. Mora do devedor."),
            Acordao("REsp", "2", texto: "Alienação fiduciária de imóvel."),
            Acordao("REsp", "3", texto: "Dano moral em transporte aéreo."),
            Tema("5", cancelado: true) is var t ? Ajustar(t, "Busca e apreensão em alienação fiduciária.") : null!);
        await db.SaveChangesAsync();

        var r = await svc.BuscarPrecedentesAsync("Preciso de uma contestação em busca e apreensão de veículo com alienação fiduciária", 5);

        Assert.Equal(["1", "2"], r.Select(p => p.Numero));
        Assert.Equal("REsp 1/STJ", r[0].Identificacao);
        Assert.Equal("Nancy Andrighi", r[0].Relator);
        Assert.Equal("Terceira Turma", r[0].OrgaoJulgador);
    }

    private static Precedente Ajustar(Precedente p, string texto) { p.Texto = texto; return p; }

    [Theory]
    [InlineData("", 0)]
    [InlineData("para o processo do cliente", 0)]
    [InlineData("Contratação de serviços bancários", 3)]
    public void TermosDeBusca_IgnoraPalavrasVaziasEMantemAcentos(string texto, int quantos)
    {
        var termos = JurisprudenciaService.TermosDeBusca(texto);
        Assert.Equal(quantos, termos.Count);
        if (quantos > 0) Assert.Contains("contratação", termos);
    }

    [Fact]
    public async Task VerificarDocumento_TextoDoEscritorio()
    {
        var (db, svc, storage) = Montar();
        await ComBaseAsync(db);
        var doc = new Documento
        {
            Id = Guid.NewGuid(), TenantId = TenantId, Nome = "peca.txt", ContentType = "text/plain",
            ObjectKey = "k", TamanhoBytes = 100, CriadoEm = DateTime.UtcNow,
        };
        db.Documentos.Add(doc);
        await db.SaveChangesAsync();
        storage.Setup(s => s.DownloadAsync("k", It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new MemoryStream(Encoding.UTF8.GetBytes("Aplica-se o Tema 1.076/STJ.")));

        var r = await svc.VerificarDocumentoAsync(doc.Id);

        Assert.Equal(StatusCitacao.Verificada, Assert.Single(r!.Citacoes).Status);
    }

    [Fact]
    public async Task VerificarDocumento_DeOutroEscritorio_Nulo()
    {
        var (db, svc, _) = Montar();
        var doc = new Documento { Id = Guid.NewGuid(), TenantId = Guid.NewGuid(), Nome = "a.pdf", ContentType = "application/pdf", ObjectKey = "k" };
        db.Documentos.Add(doc);
        await db.SaveChangesAsync();

        Assert.Null(await svc.VerificarDocumentoAsync(doc.Id));
    }

    [Theory]
    [InlineData("foto.jpg", "image/jpeg", 10, "PDF ou texto")]
    [InlineData("grande.pdf", "application/pdf", 40L * 1024 * 1024, "grande demais")]
    [InlineData("vazio.pdf", "application/pdf", 10, "ler o texto")]
    public async Task VerificarDocumento_Recusas(string nome, string tipo, long tamanho, string mensagem)
    {
        var (db, svc, storage) = Montar();
        var doc = new Documento { Id = Guid.NewGuid(), TenantId = TenantId, Nome = nome, ContentType = tipo, ObjectKey = "k", TamanhoBytes = tamanho };
        db.Documentos.Add(doc);
        await db.SaveChangesAsync();
        storage.Setup(s => s.DownloadAsync("k", It.IsAny<CancellationToken>())).ReturnsAsync(() => new MemoryStream([1, 2, 3]));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => svc.VerificarDocumentoAsync(doc.Id));
        Assert.Contains(mensagem, ex.Message);
    }

    [Theory]
    [InlineData("MARIA ISABEL GALLOTTI", "Maria Isabel Gallotti")]
    [InlineData("SEGUNDA SEÇÃO", "Segunda Seção")]
    [InlineData("ANTONIO CARLOS FERREIRA DE SOUZA", "Antonio Carlos Ferreira de Souza")]
    [InlineData(null, null)]
    public void Titulo(string? entrada, string? saida) => Assert.Equal(saida, JurisprudenciaService.Titulo(entrada));

    // ── Controller ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Controller_ValidaEntradaEDelegaAoServico()
    {
        var svc = new Mock<IJurisprudenciaService>();
        var vazio = new VerificacaoCitacoesDto([], 0, 0, 0, "");
        svc.Setup(s => s.VerificarTextoAsync("Súmula 7/STJ", true, It.IsAny<CancellationToken>())).ReturnsAsync(vazio);
        svc.Setup(s => s.VerificarDocumentoAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync((VerificacaoCitacoesDto?)null);
        var controller = new JurisprudenciaController(svc.Object);

        Assert.IsType<BadRequestObjectResult>((await controller.Verificar(new VerificarCitacoesDto("  ", null), default)).Result);
        Assert.IsType<BadRequestObjectResult>((await controller.Verificar(new VerificarCitacoesDto(new string('a', JurisprudenciaController.TamanhoMaximoTexto + 1), null), default)).Result);
        Assert.IsType<NotFoundObjectResult>((await controller.Verificar(new VerificarCitacoesDto(null, Guid.NewGuid()), default)).Result);
        var ok = Assert.IsType<OkObjectResult>((await controller.Verificar(new VerificarCitacoesDto("Súmula 7/STJ", null), default)).Result);
        Assert.Same(vazio, ok.Value);

        svc.Setup(s => s.VerificarDocumentoAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Só dá para verificar PDF ou texto."));
        Assert.IsType<BadRequestObjectResult>((await controller.Verificar(new VerificarCitacoesDto(null, Guid.NewGuid()), default)).Result);
    }
}
