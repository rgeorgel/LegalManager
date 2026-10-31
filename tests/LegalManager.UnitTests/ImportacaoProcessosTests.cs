using System.Text.Json;
using Hangfire;
using Hangfire.Common;
using Hangfire.States;
using LegalManager.API.Controllers;
using LegalManager.Application.DTOs.Importacoes;
using LegalManager.Application.DTOs.Onboarding;
using LegalManager.Application.Interfaces;
using LegalManager.Domain.Entities;
using LegalManager.Domain.Enums;
using LegalManager.Infrastructure.Identity;
using LegalManager.Infrastructure.Jobs;
using LegalManager.Infrastructure.Persistence;
using LegalManager.Infrastructure.Services;
using LegalManager.Infrastructure.Tribunais;
using LegalManager.UnitTests.TestHelpers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace LegalManager.UnitTests;

public class ImportacaoProcessosTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _usuarioId = Guid.NewGuid();

    private static AppDbContext CriarContexto() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private TenantContext CriarTenantContext()
    {
        var ctx = new TenantContext(Mock.Of<IHttpContextAccessor>());
        ctx.Assumir(_tenantId, _usuarioId, PlanoTipo.Pro);
        return ctx;
    }

    private async Task<AppDbContext> SeedAsync()
    {
        var db = CriarContexto();
        db.Tenants.Add(new Tenant { Id = _tenantId, Nome = "Escritório", Plano = PlanoTipo.Pro, Status = StatusTenant.Ativo, CriadoEm = DateTime.UtcNow });
        db.Users.Add(new Usuario
        {
            Id = _usuarioId, TenantId = _tenantId, Nome = "Advogada", Email = "adv@test.com", UserName = "adv@test.com",
            Perfil = PerfilUsuario.Advogado, Ativo = true, CriadoEm = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        return db;
    }

    private ImportacaoProcessos NovaImportacao(ModoImportacao modo, StatusImportacao status = StatusImportacao.Pendente) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = _tenantId,
        UsuarioId = _usuarioId,
        Modo = modo,
        NumeroOab = "116546",
        Uf = "MG",
        Status = status,
        CriadoEm = DateTime.UtcNow,
        AtualizadoEm = DateTime.UtcNow
    };

    private static ProcessoOabPreviewDto Preview(string cnj, bool jaCadastrado = false) =>
        new(cnj, "TJMG", null, null, null, null, JaCadastrado: jaCadastrado, Fonte: "escavador", SiglaTribunal: "TJMG");

    // ── Job ────────────────────────────────────────────────────────────────

    private ImportacaoProcessosJob CriarJob(
        AppDbContext db, Mock<IImportadorProcessosOab> importador, TenantContext? tenant = null, int? maxProcessos = null)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(maxProcessos == null
                ? new Dictionary<string, string?>()
                : new Dictionary<string, string?> { ["Escavador:MaxProcessosImportacaoOab"] = maxProcessos.ToString() })
            .Build();
        return new ImportacaoProcessosJob(db, tenant ?? new TenantContext(Mock.Of<IHttpContextAccessor>()), importador.Object,
            config, NullLogger<ImportacaoProcessosJob>.Instance)
        {
            Esperar = (_, _) => Task.CompletedTask
        };
    }

    /// <summary>Importador cuja pesquisa devolve as páginas dadas (Escavador) e nada nos tribunais.</summary>
    private static Mock<IImportadorProcessosOab> ImportadorComPaginas(params PaginaBuscaOab[] paginas)
    {
        var importador = new Mock<IImportadorProcessosOab>();
        for (var i = 0; i < paginas.Length; i++)
        {
            var cursor = i == 0 ? null : paginas[i - 1].ProximoCursor;
            var pagina = paginas[i];
            importador.Setup(m => m.BuscarPaginaEscavadorAsync("116546", "MG", cursor, It.IsAny<CancellationToken>()))
                .ReturnsAsync(pagina);
        }
        importador.Setup(m => m.BuscarTribunaisAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        importador.Setup(m => m.ImportarAsync(It.IsAny<ImportarProcessoItem>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ResultadoImportacaoItem(StatusItemImportacao.Importado, null, Guid.NewGuid()));
        return importador;
    }

    [Fact]
    public async Task Job_ModoTodos_BuscaImportaNovosEConclui()
    {
        var db = await SeedAsync();
        var importacao = NovaImportacao(ModoImportacao.Todos);
        db.ImportacoesProcessos.Add(importacao);
        await db.SaveChangesAsync();

        var tenant = new TenantContext(Mock.Of<IHttpContextAccessor>());
        var importador = new Mock<IImportadorProcessosOab>();
        importador.Setup(i => i.BuscarPaginaEscavadorAsync("116546", "MG", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PaginaBuscaOab([Preview("0001"), Preview("0003", jaCadastrado: true)], null));
        // DataJud acha de novo o 0001 (não duplica) e um processo que o Escavador não tinha.
        importador.Setup(i => i.BuscarTribunaisAsync("116546", "MG", It.IsAny<CancellationToken>()))
            .ReturnsAsync([Preview("0001") with { Fonte = "datajud" }, Preview("0002") with { Fonte = "datajud" }]);
        importador.Setup(i => i.ImportarAsync(It.Is<ImportarProcessoItem>(x => x.NumeroCNJ == "0001"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                // Os serviços tenant-scoped do job enxergam o tenant/usuário da importação.
                Assert.Equal(_tenantId, tenant.TenantId);
                Assert.Equal(_usuarioId, tenant.UserId);
                return new ResultadoImportacaoItem(StatusItemImportacao.Importado, null, Guid.NewGuid());
            });
        importador.Setup(i => i.ImportarAsync(It.Is<ImportarProcessoItem>(x => x.NumeroCNJ == "0002"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ResultadoImportacaoItem(StatusItemImportacao.Erro, "Erro ao importar"));

        await CriarJob(db, importador, tenant).ExecutarAsync(importacao.Id, CancellationToken.None);

        db.ChangeTracker.Clear();
        var final = await db.ImportacoesProcessos.Include(i => i.Itens).SingleAsync();
        Assert.Equal(StatusImportacao.Concluida, final.Status);
        Assert.NotNull(final.ConcluidoEm);
        Assert.Equal(2, final.Total); // o já cadastrado nem vira item
        Assert.Equal(2, final.Processados);
        Assert.Equal(1, final.Importados);
        Assert.Equal(1, final.Erros);
        Assert.Equal(new[] { "0001", "0002" }, final.Itens.OrderBy(i => i.Ordem).Select(i => i.NumeroCNJ));

        var notificacao = await db.Notificacoes.SingleAsync();
        Assert.Equal(_usuarioId, notificacao.UsuarioId);
        Assert.Equal($"/pages/importacao.html?id={importacao.Id}", notificacao.Url);
        Assert.Contains("1 processo(s) importado(s)", notificacao.Mensagem);
    }

    [Fact]
    public async Task Job_Retomado_ProcessaSoItensPendentes()
    {
        var db = await SeedAsync();
        var importacao = NovaImportacao(ModoImportacao.Selecionados, StatusImportacao.Importando);
        importacao.Total = 2;
        importacao.Processados = 1;
        importacao.Importados = 1;
        var feito = ImportacaoProcessosJob.NovoItem(importacao.Id, 0, new ImportarProcessoItem("0001"));
        feito.Status = StatusItemImportacao.Importado;
        importacao.Itens.Add(feito);
        importacao.Itens.Add(ImportacaoProcessosJob.NovoItem(importacao.Id, 1, new ImportarProcessoItem("0002", Codigo: "X1")));
        db.ImportacoesProcessos.Add(importacao);
        await db.SaveChangesAsync();

        var importador = new Mock<IImportadorProcessosOab>();
        importador.Setup(i => i.ImportarAsync(It.IsAny<ImportarProcessoItem>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ResultadoImportacaoItem(StatusItemImportacao.JaCadastrado, "Já cadastrado"));

        await CriarJob(db, importador).ExecutarAsync(importacao.Id, CancellationToken.None);

        // Só o pendente foi reimportado, com os dados enviados pela tela preservados.
        importador.Verify(i => i.ImportarAsync(It.Is<ImportarProcessoItem>(x => x.NumeroCNJ == "0002" && x.Codigo == "X1"), It.IsAny<CancellationToken>()), Times.Once);
        importador.Verify(i => i.ImportarAsync(It.IsAny<ImportarProcessoItem>(), It.IsAny<CancellationToken>()), Times.Once);
        importador.Verify(i => i.BuscarPaginaEscavadorAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);

        db.ChangeTracker.Clear();
        var final = await db.ImportacoesProcessos.SingleAsync();
        Assert.Equal(StatusImportacao.Concluida, final.Status);
        Assert.Equal(2, final.Processados);
        Assert.Equal(1, final.Importados);
        Assert.Equal(1, final.JaCadastrados);
    }

    [Fact]
    public async Task Job_ErroInesperadoNoItem_MarcaItemESegue()
    {
        var db = await SeedAsync();
        var importacao = NovaImportacao(ModoImportacao.Selecionados);
        importacao.Total = 2;
        importacao.Itens.Add(ImportacaoProcessosJob.NovoItem(importacao.Id, 0, new ImportarProcessoItem("0001")));
        importacao.Itens.Add(ImportacaoProcessosJob.NovoItem(importacao.Id, 1, new ImportarProcessoItem("0002")));
        db.ImportacoesProcessos.Add(importacao);
        await db.SaveChangesAsync();

        var importador = new Mock<IImportadorProcessosOab>();
        importador.Setup(i => i.ImportarAsync(It.Is<ImportarProcessoItem>(x => x.NumeroCNJ == "0001"), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));
        importador.Setup(i => i.ImportarAsync(It.Is<ImportarProcessoItem>(x => x.NumeroCNJ == "0002"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ResultadoImportacaoItem(StatusItemImportacao.Importado, null, Guid.NewGuid()));

        await CriarJob(db, importador).ExecutarAsync(importacao.Id, CancellationToken.None);

        db.ChangeTracker.Clear();
        var final = await db.ImportacoesProcessos.Include(i => i.Itens).SingleAsync();
        Assert.Equal(StatusImportacao.Concluida, final.Status);
        Assert.Equal(1, final.Importados);
        Assert.Equal(1, final.Erros);
        Assert.Equal(StatusItemImportacao.Erro, final.Itens.Single(i => i.NumeroCNJ == "0001").Status);
    }

    [Fact]
    public async Task Job_FalhaNaBusca_MarcaErroENotifica()
    {
        var db = await SeedAsync();
        var importacao = NovaImportacao(ModoImportacao.Todos);
        db.ImportacoesProcessos.Add(importacao);
        await db.SaveChangesAsync();

        var importador = new Mock<IImportadorProcessosOab>();
        importador.Setup(i => i.BuscarPaginaEscavadorAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("fora do ar"));

        await CriarJob(db, importador).ExecutarAsync(importacao.Id, CancellationToken.None);

        db.ChangeTracker.Clear();
        var final = await db.ImportacoesProcessos.SingleAsync();
        Assert.Equal(StatusImportacao.Erro, final.Status);
        Assert.NotNull(final.MensagemErro);
        Assert.Contains("interrompida", (await db.Notificacoes.SingleAsync()).Titulo);
    }

    [Fact]
    public async Task Job_ImportacaoJaConcluida_NaoFazNada()
    {
        var db = await SeedAsync();
        var importacao = NovaImportacao(ModoImportacao.Todos, StatusImportacao.Concluida);
        db.ImportacoesProcessos.Add(importacao);
        await db.SaveChangesAsync();

        var importador = new Mock<IImportadorProcessosOab>(MockBehavior.Strict);
        await CriarJob(db, importador).ExecutarAsync(importacao.Id, CancellationToken.None);

        Assert.Empty(await db.Notificacoes.ToListAsync());
    }

    [Fact]
    public async Task Job_PaginaOEscavadorAteOFim()
    {
        var db = await SeedAsync();
        var importacao = NovaImportacao(ModoImportacao.Todos);
        db.ImportacoesProcessos.Add(importacao);
        await db.SaveChangesAsync();

        var importador = ImportadorComPaginas(
            new PaginaBuscaOab([Preview("0001"), Preview("0002")], "c2"),
            new PaginaBuscaOab([Preview("0002"), Preview("0003")], "c3"),
            new PaginaBuscaOab([Preview("0004")], null));

        await CriarJob(db, importador).ExecutarAsync(importacao.Id, CancellationToken.None);

        db.ChangeTracker.Clear();
        var final = await db.ImportacoesProcessos.Include(i => i.Itens).SingleAsync();
        Assert.Equal(StatusImportacao.Concluida, final.Status);
        Assert.Equal(new[] { "0001", "0002", "0003", "0004" }, final.Itens.OrderBy(i => i.Ordem).Select(i => i.NumeroCNJ));
        Assert.Equal(4, final.Total);
        Assert.Equal(4, final.Importados);
        Assert.True(final.BuscaEscavadorConcluida);
        Assert.True(final.BuscaTribunaisConcluida);
        Assert.Null(final.CursorEscavador);
    }

    [Fact]
    public async Task Job_Retomado_ContinuaDoCursorDoEscavador()
    {
        var db = await SeedAsync();
        var importacao = NovaImportacao(ModoImportacao.Todos, StatusImportacao.Buscando);
        importacao.CursorEscavador = "c2";
        importacao.Total = 1;
        importacao.Itens.Add(ImportacaoProcessosJob.NovoItem(importacao.Id, 0, new ImportarProcessoItem("0001", Fonte: "escavador")));
        db.ImportacoesProcessos.Add(importacao);
        await db.SaveChangesAsync();

        var importador = ImportadorComPaginas(
            new PaginaBuscaOab([Preview("0001")], "c2"),
            new PaginaBuscaOab([Preview("0002")], null));

        await CriarJob(db, importador).ExecutarAsync(importacao.Id, CancellationToken.None);

        importador.Verify(m => m.BuscarPaginaEscavadorAsync(It.IsAny<string>(), It.IsAny<string>(), null, It.IsAny<CancellationToken>()), Times.Never);
        db.ChangeTracker.Clear();
        var final = await db.ImportacoesProcessos.Include(i => i.Itens).SingleAsync();
        Assert.Equal(new[] { "0001", "0002" }, final.Itens.OrderBy(i => i.Ordem).Select(i => i.NumeroCNJ));
    }

    [Fact]
    public async Task Job_RespeitaOTetoDeProcessos()
    {
        var db = await SeedAsync();
        var importacao = NovaImportacao(ModoImportacao.Todos);
        db.ImportacoesProcessos.Add(importacao);
        await db.SaveChangesAsync();

        var importador = ImportadorComPaginas(
            new PaginaBuscaOab([Preview("0001"), Preview("0002")], "c2"),
            new PaginaBuscaOab([Preview("0003"), Preview("0004")], "c3"),
            new PaginaBuscaOab([Preview("0005")], null));

        await CriarJob(db, importador, maxProcessos: 3).ExecutarAsync(importacao.Id, CancellationToken.None);

        db.ChangeTracker.Clear();
        var final = await db.ImportacoesProcessos.SingleAsync();
        Assert.Equal(3, final.Total);
        // Parou no teto: a terceira página (paga) nem foi pedida.
        importador.Verify(m => m.BuscarPaginaEscavadorAsync(It.IsAny<string>(), It.IsAny<string>(), "c3", It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Job_FalhaTransitoriaNaPagina_TentaDeNovo()
    {
        var db = await SeedAsync();
        var importacao = NovaImportacao(ModoImportacao.Todos);
        db.ImportacoesProcessos.Add(importacao);
        await db.SaveChangesAsync();

        var importador = ImportadorComPaginas(new PaginaBuscaOab([Preview("0001")], null));
        var chamadas = 0;
        importador.Setup(m => m.BuscarPaginaEscavadorAsync("116546", "MG", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => ++chamadas == 1
                ? throw new HttpRequestException("503")
                : new PaginaBuscaOab([Preview("0001")], null));

        await CriarJob(db, importador).ExecutarAsync(importacao.Id, CancellationToken.None);

        Assert.Equal(2, chamadas);
        db.ChangeTracker.Clear();
        Assert.Equal(StatusImportacao.Concluida, (await db.ImportacoesProcessos.SingleAsync()).Status);
    }

    // ── Controller ─────────────────────────────────────────────────────────

    private (ImportacoesController controller, Mock<IBackgroundJobClient> jobs) CriarController(AppDbContext db)
    {
        var jobs = new Mock<IBackgroundJobClient>();
        jobs.Setup(j => j.Create(It.IsAny<Job>(), It.IsAny<IState>())).Returns("1");
        return (new ImportacoesController(db, CriarTenantContext(), jobs.Object), jobs);
    }

    [Fact]
    public async Task Iniciar_CriaImportacaoRegistraOabEEnfileira()
    {
        var db = await SeedAsync();
        var (controller, jobs) = CriarController(db);

        var resposta = await controller.Iniciar(new IniciarImportacaoDto("116.546", "mg"), CancellationToken.None);

        var dto = Assert.IsType<ImportacaoResumoDto>(Assert.IsType<AcceptedResult>(resposta.Result).Value);
        Assert.Equal(ModoImportacao.Todos, dto.Modo);
        Assert.Equal("116546", dto.NumeroOab);
        Assert.Equal("MG", dto.Uf);
        Assert.Empty(await db.ImportacaoProcessoItens.ToListAsync());

        var usuario = await db.Users.SingleAsync();
        Assert.Equal("116546", usuario.OabImportadaNumero);
        Assert.Equal("MG", usuario.OabImportadaUf);

        jobs.Verify(j => j.Create(It.Is<Job>(job => job.Type == typeof(ImportacaoProcessosJob)), It.IsAny<EnqueuedState>()), Times.Once);
    }

    [Fact]
    public async Task Iniciar_ComImportacaoEmAndamento_Retorna409()
    {
        var db = await SeedAsync();
        db.ImportacoesProcessos.Add(NovaImportacao(ModoImportacao.Todos, StatusImportacao.Importando));
        await db.SaveChangesAsync();
        var (controller, jobs) = CriarController(db);

        var resposta = await controller.Iniciar(new IniciarImportacaoDto("116546", "MG"), CancellationToken.None);

        Assert.IsType<ConflictObjectResult>(resposta.Result);
        jobs.Verify(j => j.Create(It.IsAny<Job>(), It.IsAny<IState>()), Times.Never);
    }

    [Fact]
    public async Task Ativa_SemProgressoHaMuitoTempo_MarcaComoInterrompida()
    {
        var db = await SeedAsync();
        var travada = NovaImportacao(ModoImportacao.Todos, StatusImportacao.Importando);
        travada.AtualizadoEm = DateTime.UtcNow - ImportacaoProcessosJob.TempoMaximoSemProgresso - TimeSpan.FromMinutes(1);
        db.ImportacoesProcessos.Add(travada);
        await db.SaveChangesAsync();
        var (controller, _) = CriarController(db);

        var resposta = await controller.Ativa(CancellationToken.None);

        Assert.IsType<NoContentResult>(resposta.Result);
        Assert.Equal(StatusImportacao.Erro, (await db.ImportacoesProcessos.SingleAsync()).Status);
    }

    [Fact]
    public async Task Obter_ImportacaoDeOutroUsuario_Retorna404()
    {
        var db = await SeedAsync();
        var deOutro = NovaImportacao(ModoImportacao.Todos);
        deOutro.UsuarioId = Guid.NewGuid();
        db.ImportacoesProcessos.Add(deOutro);
        await db.SaveChangesAsync();
        var (controller, _) = CriarController(db);

        Assert.IsType<NotFoundResult>((await controller.Obter(deOutro.Id, CancellationToken.None)).Result);
        Assert.IsType<NotFoundResult>((await controller.Itens(deOutro.Id, CancellationToken.None)).Result);
    }

    // ── Importador ─────────────────────────────────────────────────────────

    private ImportadorProcessosOab CriarImportador(
        AppDbContext db, Mock<IEscavadorService> escavador, IContatoService? contatos = null,
        DataJudAdapter? dataJud = null, IProcessoService? processos = null, IContatoResolverService? resolver = null) =>
        new(db, CriarTenantContext(),
            dataJud ?? new DataJudAdapter(new HttpClient(), NullLogger<DataJudAdapter>.Instance),
            new EsajTjspProcessosAdapter(new HttpClient(), NullLogger<EsajTjspProcessosAdapter>.Instance),
            escavador.Object,
            processos ?? Mock.Of<IProcessoService>(),
            contatos ?? Mock.Of<IContatoService>(),
            resolver ?? Mock.Of<IContatoResolverService>(),
            FakeConsultaExternaLogService.Instance,
            NullLogger<ImportadorProcessosOab>.Instance);

    private static EscavadorProcessoDto ProcessoEscavador(string cnj, string json) =>
        new(1, cnj, "TRT3", "Tribunal Regional do Trabalho da 3ª Região", null, null, null, null, null, JsonBruto: json);

    [Fact]
    public async Task BuscarPagina_EscavadorComCnjRepetido_NaoPerdeResultadosEGravaCacheUmaVez()
    {
        // Regressão: CNJ repetido no retorno do Escavador violava o índice único do cache e
        // o catch descartava todos os resultados do Escavador.
        var db = await SeedAsync();
        db.Processos.Add(new Processo
        {
            Id = Guid.NewGuid(), TenantId = _tenantId, NumeroCNJ = "0003", AreaDireito = AreaDireito.Outro,
            Fase = FaseProcessual.Conhecimento, Status = StatusProcesso.Ativo, CriadoEm = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var escavador = new Mock<IEscavadorService>();
        escavador.Setup(e => e.BuscarPaginaPorOabAsync("116546", "MG", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EscavadorPaginaCursor<EscavadorProcessoDto>(
            [
                ProcessoEscavador("0001", "{\"v\":1}"),
                ProcessoEscavador("0001", "{\"v\":2}"),
                ProcessoEscavador("0003", "{\"v\":3}")
            ], "https://api.escavador.com/api/v2/advogado/processos?cursor=abc"));

        var pagina = await CriarImportador(db, escavador).BuscarPaginaEscavadorAsync("116546", "MG", null);
        var resultado = pagina.Processos;
        Assert.Equal("https://api.escavador.com/api/v2/advogado/processos?cursor=abc", pagina.ProximoCursor);

        Assert.Equal(new[] { "0001", "0003" }, resultado.Select(p => p.NumeroCNJ).OrderBy(c => c));
        Assert.True(resultado.Single(p => p.NumeroCNJ == "0003").JaCadastrado);
        Assert.All(resultado, p => Assert.Equal("escavador", p.Fonte));

        var cache = await db.ProcessosImportacaoCache.OrderBy(c => c.NumeroCNJ).ToListAsync();
        Assert.Equal(new[] { "0001", "0003" }, cache.Select(c => c.NumeroCNJ));
        Assert.Equal("{\"v\":1}", cache[0].DadosJson);
    }

    [Fact]
    public async Task Importar_Escavador_CriaProcessoComDadosDoCacheSemBuscarAndamentos()
    {
        var db = await SeedAsync();
        db.ProcessosImportacaoCache.Add(new ProcessoImportacaoCache
        {
            Id = Guid.NewGuid(), TenantId = _tenantId, NumeroCNJ = "0001", Fonte = "escavador",
            DadosJson = "{\"unidade_origem\":{\"tribunal_sigla\":\"TRT3\",\"nome\":\"TRT da 3ª Região\",\"cidade\":\"Belo Horizonte\"}}",
            ExpiraEm = DateTime.UtcNow.AddHours(1), CriadoEm = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var escavador = new Mock<IEscavadorService>(MockBehavior.Strict);

        var resultado = await CriarImportador(db, escavador)
            .ImportarAsync(new ImportarProcessoItem("0001", Fonte: "escavador"));

        Assert.Equal(StatusItemImportacao.Importado, resultado.Status);
        var processo = await db.Processos.SingleAsync(p => p.Id == resultado.ProcessoId);
        Assert.Equal(_tenantId, processo.TenantId);
        Assert.Equal(_usuarioId, processo.AdvogadoResponsavelId);
        Assert.Equal("TRT3", processo.SiglaTribunal);
        Assert.Equal("Belo Horizonte", processo.Comarca);
        Assert.Equal(AreaDireito.Trabalhista, processo.AreaDireito);
        // Andamentos ficam para a carga inicial (quando o processo for aberto): nenhuma
        // chamada ao Escavador na importação (o mock Strict falharia).
        Assert.True(processo.AndamentosPendentes);
        Assert.Empty(await db.Andamentos.ToListAsync());
    }

    [Fact]
    public async Task Importar_EscavadorJaCadastrado_RetornaJaCadastrado()
    {
        var db = await SeedAsync();
        var existenteId = Guid.NewGuid();
        db.Processos.Add(new Processo
        {
            Id = existenteId, TenantId = _tenantId, NumeroCNJ = "0001", AreaDireito = AreaDireito.Outro,
            Fase = FaseProcessual.Conhecimento, Status = StatusProcesso.Ativo, CriadoEm = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var resultado = await CriarImportador(db, new Mock<IEscavadorService>())
            .ImportarAsync(new ImportarProcessoItem("0001", Fonte: "escavador"));

        Assert.Equal(StatusItemImportacao.JaCadastrado, resultado.Status);
        Assert.Equal(existenteId, resultado.ProcessoId);
        Assert.Equal(1, await db.Processos.CountAsync());
    }

    // ── Categoria das partes importadas ────────────────────────────────────

    private async Task<Dictionary<string, TipoContato>> ImportarEscavadorComPartesAsync(
        AppDbContext db, string envolvidosJson, string? oab = "116546", string? uf = "MG")
    {
        db.ProcessosImportacaoCache.Add(new ProcessoImportacaoCache
        {
            Id = Guid.NewGuid(), TenantId = _tenantId, NumeroCNJ = "0001", Fonte = "escavador",
            DadosJson = $"{{\"fontes\":[{{\"envolvidos\":{envolvidosJson}}}]}}",
            ExpiraEm = DateTime.UtcNow.AddHours(1), CriadoEm = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var contatos = new ContatoService(db, CriarTenantContext(), new HonorarioService(db, Mock.Of<IAuditService>()));
        var resultado = await CriarImportador(db, new Mock<IEscavadorService>(), contatos)
            .ImportarAsync(new ImportarProcessoItem("0001", Fonte: "escavador", OabAdvogado: oab, UfAdvogado: uf));
        Assert.Equal(StatusItemImportacao.Importado, resultado.Status);

        db.ChangeTracker.Clear();
        return await db.Contatos.ToDictionaryAsync(c => c.Nome, c => c.TipoContato);
    }

    [Fact]
    public async Task Importar_Escavador_AdvogadoComoEnvolvido_SeparaClienteDaParteContraria()
    {
        var db = await SeedAsync();

        var contatos = await ImportarEscavadorComPartesAsync(db, """
            [{"nome":"Advogada","polo":"ATIVO","tipo_normalizado":"Advogado","oabs":[{"numero":116546,"uf":"MG"}]},
             {"nome":"Cliente Um","polo":"ATIVO"},
             {"nome":"Empresa Ré","polo":"PASSIVO"},
             {"nome":"Advogado da Ré","polo":"PASSIVO","oabs":[{"numero":999,"uf":"MG"}]}]
            """);

        Assert.Equal(TipoContato.Cliente, contatos["Cliente Um"]);
        Assert.Equal(TipoContato.ParteContraria, contatos["Empresa Ré"]);
        Assert.Equal(TipoContato.Outro, contatos["Advogado da Ré"]);
        Assert.False(contatos.ContainsKey("Advogada")); // a própria advogada não vira contato
    }

    [Fact]
    public async Task Importar_Escavador_AdvogadoAninhadoNaParte_UsaPoloDaParte()
    {
        var db = await SeedAsync();

        var contatos = await ImportarEscavadorComPartesAsync(db, """
            [{"nome":"Autor","polo":"ATIVO"},
             {"nome":"Réu Cliente","polo":"PASSIVO","advogados":[{"nome":"Advogada","oabs":[{"numero":"0116546","uf":"mg"}]}]}]
            """);

        Assert.Equal(TipoContato.Cliente, contatos["Réu Cliente"]);
        Assert.Equal(TipoContato.ParteContraria, contatos["Autor"]);
    }

    [Fact]
    public async Task Importar_Escavador_FormatoReal_AdvogadoAninhadoEPoloAdvogadoSemOab()
    {
        // Formato visto em produção: a parte traz o advogado (com OAB) em "advogados", e o advogado
        // aparece também como envolvido próprio com polo "ADVOGADO" — às vezes sem "oabs".
        var db = await SeedAsync();

        var contatos = await ImportarEscavadorComPartesAsync(db, """
            [{"nome":"Cliente Autora","polo":"ATIVO","tipo_normalizado":"Autor",
              "advogados":[{"nome":"Advogada da Importacao","polo":"ADVOGADO","tipo_normalizado":"Advogado",
                            "oabs":[{"uf":"MG","tipo":"ADVOGADO","numero":116546}]}]},
             {"nome":"Empresa Ré","polo":"PASSIVO","tipo_normalizado":"Réu",
              "advogados":[{"nome":"Advogado da Ré","oabs":[{"uf":"MG","tipo":"ADVOGADO","numero":999}]}]},
             {"nome":"Advogada da Importacao","polo":"ADVOGADO","tipo_normalizado":"Advogado","oabs":null},
             {"nome":"Advogado da Ré","polo":"ADVOGADO","tipo_normalizado":"Advogado","oabs":[{"uf":"MG","numero":999}]}]
            """);

        Assert.Equal(TipoContato.Cliente, contatos["Cliente Autora"]);
        Assert.Equal(TipoContato.ParteContraria, contatos["Empresa Ré"]);
        Assert.Equal(TipoContato.Outro, contatos["Advogado da Ré"]);
        Assert.False(contatos.ContainsKey("Advogada da Importacao"));
    }

    [Fact]
    public async Task Importar_Escavador_OabNaoEncontrada_NaoMarcaNinguemComoCliente()
    {
        var db = await SeedAsync();

        var contatos = await ImportarEscavadorComPartesAsync(db, """
            [{"nome":"Autor","polo":"ATIVO"},{"nome":"Réu","polo":"PASSIVO"}]
            """);

        Assert.All(contatos.Values, t => Assert.Equal(TipoContato.Outro, t));
    }

    [Fact]
    public async Task Importar_Escavador_ContatoImportadoDoLadoDaAdvogada_ViraClienteSemRebaixarOutros()
    {
        var db = await SeedAsync();
        Contato Existente(string nome, TipoContato tipo, bool importado) => new()
        {
            Id = Guid.NewGuid(), TenantId = _tenantId, Nome = nome, TipoContato = tipo,
            ImportadoAutomaticamente = importado, Ativo = true, CriadoEm = DateTime.UtcNow
        };
        db.Contatos.AddRange(
            Existente("Cliente Um", TipoContato.ParteContraria, importado: true),
            Existente("Perito Manual", TipoContato.Perito, importado: false),
            Existente("Cliente Antigo", TipoContato.Cliente, importado: true));
        await db.SaveChangesAsync();

        var contatos = await ImportarEscavadorComPartesAsync(db, """
            [{"nome":"Cliente Um","polo":"ATIVO","advogados":[{"oabs":[{"numero":116546,"uf":"MG"}]}]},
             {"nome":"Perito Manual","polo":"ATIVO"},
             {"nome":"Cliente Antigo","polo":"PASSIVO"}]
            """);

        Assert.Equal(TipoContato.Cliente, contatos["Cliente Um"]);
        Assert.Equal(TipoContato.Perito, contatos["Perito Manual"]); // cadastrado à mão: não mexe
        Assert.Equal(TipoContato.Cliente, contatos["Cliente Antigo"]); // nunca rebaixa
    }

    [Fact]
    public async Task Job_PassaOabENomeDoAdvogadoParaOImportador()
    {
        var db = await SeedAsync();
        var importacao = NovaImportacao(ModoImportacao.Todos);
        db.ImportacoesProcessos.Add(importacao);
        db.TenantOabs.Add(new TenantOab
        {
            Id = Guid.NewGuid(), TenantId = _tenantId, Numero = "116546", Uf = "MG", Nome = "Dra. Fulana de Tal", CriadoEm = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var importador = ImportadorComPaginas(new PaginaBuscaOab([Preview("0001")], null));

        await CriarJob(db, importador).ExecutarAsync(importacao.Id, CancellationToken.None);

        importador.Verify(i => i.ImportarAsync(
            It.Is<ImportarProcessoItem>(x => x.NumeroCNJ == "0001" && x.OabAdvogado == "116546"
                                             && x.UfAdvogado == "MG" && x.NomeAdvogado == "Dra. Fulana de Tal"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── Situação na fonte ───────────────────────────────────────────────────

    [Fact]
    public async Task Importar_EscavadorArquivadoNoTribunal_CriaArquivadoComMotivo()
    {
        var db = await SeedAsync();
        db.ProcessosImportacaoCache.Add(new ProcessoImportacaoCache
        {
            Id = Guid.NewGuid(), TenantId = _tenantId, NumeroCNJ = "0001", Fonte = "escavador",
            DadosJson = "{\"fontes_tribunais_estao_arquivadas\":true,\"fontes\":[{\"tipo\":\"TRIBUNAL\",\"arquivado\":true,\"capa\":{\"data_arquivamento\":\"2022-08-15\"}}]}",
            ExpiraEm = DateTime.UtcNow.AddHours(1), CriadoEm = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var resultado = await CriarImportador(db, new Mock<IEscavadorService>(MockBehavior.Strict))
            .ImportarAsync(new ImportarProcessoItem("0001", Fonte: "escavador"));

        Assert.Equal(StatusItemImportacao.Importado, resultado.Status);
        Assert.Equal("Arquivado no tribunal em 15/08/2022", resultado.Mensagem);
        Assert.Equal(StatusProcesso.Arquivado, (await db.Processos.SingleAsync()).Status);
    }

    [Fact]
    public async Task Importar_EscavadorArquivadoMasMovimentouDepois_FicaAtivoParaConferir()
    {
        var db = await SeedAsync();
        db.ProcessosImportacaoCache.Add(new ProcessoImportacaoCache
        {
            Id = Guid.NewGuid(), TenantId = _tenantId, NumeroCNJ = "0001", Fonte = "escavador",
            DadosJson = "{\"fontes\":[{\"tipo\":\"TRIBUNAL\",\"grau\":1,\"arquivado\":true,\"data_ultima_movimentacao\":\"2023-01-10\",\"capa\":{\"data_arquivamento\":\"2022-08-15\"}}]}",
            ExpiraEm = DateTime.UtcNow.AddHours(1), CriadoEm = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var resultado = await CriarImportador(db, new Mock<IEscavadorService>(MockBehavior.Strict))
            .ImportarAsync(new ImportarProcessoItem("0001", Fonte: "escavador"));

        Assert.Equal("Arquivado no tribunal em 15/08/2022. Mantido Ativo para conferir: movimentou em 10/01/2023, depois do arquivamento",
            resultado.Mensagem);
        Assert.Equal(StatusProcesso.Ativo, (await db.Processos.SingleAsync()).Status);
    }

    [Fact]
    public async Task Job_ResumoContaOsQueEntraramArquivados()
    {
        var db = await SeedAsync();
        var importacao = NovaImportacao(ModoImportacao.Todos);
        db.ImportacoesProcessos.Add(importacao);
        var arquivadoId = Guid.NewGuid();
        var ativoId = Guid.NewGuid();
        db.Processos.AddRange(
            new Processo { Id = arquivadoId, TenantId = _tenantId, NumeroCNJ = "0001", Status = StatusProcesso.Arquivado, CriadoEm = DateTime.UtcNow },
            new Processo { Id = ativoId, TenantId = _tenantId, NumeroCNJ = "0002", Status = StatusProcesso.Ativo, CriadoEm = DateTime.UtcNow });
        await db.SaveChangesAsync();

        var importador = ImportadorComPaginas(new PaginaBuscaOab([Preview("0001"), Preview("0002")], null));
        importador.Setup(i => i.ImportarAsync(It.Is<ImportarProcessoItem>(x => x.NumeroCNJ == "0001"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ResultadoImportacaoItem(StatusItemImportacao.Importado, "Arquivamento definitivo em 12/03/2021", arquivadoId));
        importador.Setup(i => i.ImportarAsync(It.Is<ImportarProcessoItem>(x => x.NumeroCNJ == "0002"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ResultadoImportacaoItem(StatusItemImportacao.Importado, null, ativoId));

        await CriarJob(db, importador).ExecutarAsync(importacao.Id, CancellationToken.None);

        var notificacao = await db.Notificacoes.SingleAsync();
        Assert.Contains("2 processo(s) importado(s), 1 já arquivado(s) ou suspenso(s) no tribunal", notificacao.Mensagem);

        var itens = Assert.IsType<List<ImportacaoItemDto>>(
            Assert.IsType<OkObjectResult>((await CriarController(db).controller.Itens(importacao.Id, CancellationToken.None)).Result).Value);
        Assert.Equal(StatusProcesso.Arquivado, itens.Single(i => i.NumeroCNJ == "0001").StatusProcesso);
        Assert.Equal("Arquivamento definitivo em 12/03/2021", itens.Single(i => i.NumeroCNJ == "0001").Mensagem);
        Assert.Equal(StatusProcesso.Ativo, itens.Single(i => i.NumeroCNJ == "0002").StatusProcesso);
    }

    private sealed class RespostaFixaHandler(string json) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
            });
    }

    [Fact]
    public async Task Importar_DataJudComArquivamentoDefinitivo_CriaArquivadoComMotivo()
    {
        var db = await SeedAsync();
        const string json = """
            {"hits": {"hits": [{"_source": {
              "numeroProcesso": "00100011120205030001", "grau": "G1", "siglaTribunal": "TRT3",
              "movimentos": [
                {"codigo": 26, "nome": "Distribuição", "dataHora": "2020-02-01T10:00:00"},
                {"codigo": 246, "nome": "Definitivo", "dataHora": "2023-05-10T09:00:00"}
              ]}}]}}
            """;
        var dataJud = new DataJudAdapter(
            new HttpClient(new RespostaFixaHandler(json)) { BaseAddress = new Uri("https://datajud.test") },
            NullLogger<DataJudAdapter>.Instance);
        var resolver = new Mock<IContatoResolverService>();
        resolver.Setup(r => r.ResolverPartesDataJudAsync(
                It.IsAny<IReadOnlyList<TribunalParte>>(), It.IsAny<Func<TribunalParte, TipoContato>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        var processos = new ProcessoService(db, CriarTenantContext(), Mock.Of<IEscavadorService>(), FakeConsultaExternaLogService.Instance);

        var resultado = await CriarImportador(db, new Mock<IEscavadorService>(), dataJud: dataJud, processos: processos, resolver: resolver.Object)
            .ImportarAsync(new ImportarProcessoItem("0010001-11.2020.5.03.0001"));

        Assert.Equal(StatusItemImportacao.Importado, resultado.Status);
        Assert.Equal("Arquivamento definitivo em 10/05/2023", resultado.Mensagem);
        var processo = await db.Processos.SingleAsync();
        Assert.Equal(StatusProcesso.Arquivado, processo.Status);
        Assert.Equal(2, await db.Andamentos.CountAsync());
    }
}
