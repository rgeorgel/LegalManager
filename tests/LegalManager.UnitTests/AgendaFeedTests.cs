using System.Text;
using LegalManager.API.Configuration;
using LegalManager.API.Controllers;
using LegalManager.Application.DTOs.Agenda;
using LegalManager.Application.Interfaces;
using LegalManager.Domain.Entities;
using LegalManager.Domain.Enums;
using LegalManager.Domain.Interfaces;
using LegalManager.Infrastructure;
using LegalManager.Infrastructure.Agenda;
using LegalManager.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Moq;

namespace LegalManager.UnitTests;

public class IcsBuilderTests
{
    private static string Desdobrar(string ics) => ics.Replace("\r\n ", "");

    [Fact]
    public void Escapar_TrataCaracteresEspeciaisEQuebras()
    {
        Assert.Equal(@"a\, b\; c\\d\ne\nf g", IcsBuilder.Escapar("a, b; c\\d\r\ne\nf\tg\u0007"));
    }

    [Theory]
    [InlineData(24 * 60, "P1D")]
    [InlineData(15 * 60, "PT15H")]
    [InlineData(90, "PT1H30M")]
    [InlineData(0, "PT0M")]
    [InlineData(-60, "PT1H")]
    public void Duracao_FormatoIcal(int minutos, string esperado)
    {
        Assert.Equal(esperado, IcsBuilder.Duracao(TimeSpan.FromMinutes(minutos)));
    }

    [Fact]
    public void Linhas_SaoDobradasEm75OctetosSemPartirCaracteres()
    {
        var titulo = string.Concat(Enumerable.Repeat("Audiência ção 😀 ", 20));
        var ics = new IcsBuilder("Cal", "America/Sao_Paulo")
            .Adicionar(new IcsEvento("u1", titulo, new DateTime(2026, 11, 20, 17, 0, 0), new DateTime(2026, 11, 20, 18, 0, 0),
                false, new DateTime(2026, 11, 1)))
            .Concluir();

        Assert.EndsWith("END:VCALENDAR\r\n", ics);
        foreach (var linha in ics.Split("\r\n"))
            Assert.True(Encoding.UTF8.GetByteCount(linha) <= 75, $"Linha com mais de 75 octetos: {linha}");
        Assert.DoesNotContain('�', ics);
        Assert.Contains("SUMMARY:" + IcsBuilder.Escapar(titulo), Desdobrar(ics));
    }

    [Fact]
    public void Evento_DiaInteiroComAlarmeEMetadados()
    {
        var ics = Desdobrar(new IcsBuilder("Minha agenda", "America/Manaus", TimeSpan.FromHours(1))
            .Adicionar(new IcsEvento("tarefa-1@x", "Prazo: contestação", new DateTime(2026, 11, 20), new DateTime(2026, 11, 21),
                true, new DateTime(2026, 11, 2, 10, 30, 0), "Linha 1\nLinha 2", "Fórum", "https://x/y", "Prazo", TimeSpan.FromHours(15)))
            .Concluir());

        Assert.Contains("X-WR-CALNAME:Minha agenda\r\n", ics);
        Assert.Contains("X-WR-TIMEZONE:America/Manaus\r\n", ics);
        Assert.Contains("REFRESH-INTERVAL;VALUE=DURATION:PT1H\r\n", ics);
        Assert.Contains("DTSTART;VALUE=DATE:20261120\r\n", ics);
        Assert.Contains("DTEND;VALUE=DATE:20261121\r\n", ics);
        Assert.Contains("TRANSP:TRANSPARENT\r\n", ics);
        Assert.Contains("DTSTAMP:20261102T103000Z\r\n", ics);
        Assert.Contains(@"DESCRIPTION:Linha 1\nLinha 2", ics);
        Assert.Contains("LOCATION:Fórum\r\n", ics);
        Assert.Contains("URL:https://x/y\r\n", ics);
        Assert.Contains("TRIGGER:-PT15H\r\n", ics);
    }

    [Fact]
    public void Adicionar_DepoisDeConcluir_Falha()
    {
        var b = new IcsBuilder("C", null);
        b.Concluir();
        Assert.Equal(b.Concluir(), b.Concluir());
        Assert.Throws<InvalidOperationException>(() =>
            b.Adicionar(new IcsEvento("u", "t", DateTime.UtcNow, DateTime.UtcNow, false, DateTime.UtcNow)));
    }

    [Theory]
    [InlineData("America/Sao_Paulo", 17)]
    [InlineData("America/Manaus", 18)]
    [InlineData("America/Noronha", 16)]
    public void ParaUtc_UsaOFusoDoEscritorio(string fuso, int horaUtc)
    {
        var tz = FusoHorario.Resolver(fuso);
        var utc = FeedAgendaService.ParaUtc(new DateTime(2026, 11, 20, 14, 0, 0, DateTimeKind.Utc), tz);
        Assert.Equal(new DateTime(2026, 11, 20, horaUtc, 0, 0), utc);
        Assert.Equal(DateTimeKind.Utc, utc.Kind);
    }

    [Fact]
    public void ParaUtc_HoraInexistenteNoHorarioDeVerao_AvancaUmaHora()
    {
        var tz = FusoHorario.Resolver("America/New_York");
        // 08/03/2026 02:30 não existe em Nova York (relógio pula de 2h para 3h).
        var utc = FeedAgendaService.ParaUtc(new DateTime(2026, 3, 8, 2, 30, 0), tz);
        Assert.Equal(new DateTime(2026, 3, 8, 7, 30, 0), utc);
    }

    [Theory]
    [InlineData("/api/agenda/feed/AbC-_123.ics", "/api/agenda/feed/***.ics")]
    [InlineData("/API/Agenda/Feed/xyz.ics", "/API/Agenda/Feed/***.ics")]
    [InlineData("/api/agenda/feed", "/api/agenda/feed")]
    [InlineData("/api/eventos", "/api/eventos")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void MascararCaminho_EscondeOToken(string? caminho, string esperado)
    {
        Assert.Equal(esperado, FeedAgendaApi.MascararCaminho(caminho));
    }

    [Fact]
    public void MascaraLog_TrocaOTokenNasPropriedadesDoEvento()
    {
        var evento = new Serilog.Events.LogEvent(DateTimeOffset.UtcNow, Serilog.Events.LogEventLevel.Information, null,
            new Serilog.Parsing.MessageTemplateParser().Parse("Request starting {Path}"),
            [
                new Serilog.Events.LogEventProperty("Path", new Serilog.Events.ScalarValue("/api/agenda/feed/segredo.ics")),
                new Serilog.Events.LogEventProperty("RequestPath", new Serilog.Events.ScalarValue("/api/agenda/feed/segredo.ics")),
                new Serilog.Events.LogEventProperty("Outro", new Serilog.Events.ScalarValue("/api/agenda/feed/segredo.ics")),
            ]);

        new FeedAgendaApi.MascaraLog().Enrich(evento, null!);

        Assert.Equal("Request starting \"/api/agenda/feed/***.ics\"", evento.RenderMessage());
        Assert.Equal("\"/api/agenda/feed/***.ics\"", evento.Properties["RequestPath"].ToString());
        Assert.Equal("\"/api/agenda/feed/segredo.ics\"", evento.Properties["Outro"].ToString());
    }

    [Fact]
    public void Token_AleatorioEHashHex()
    {
        var a = TokenFeedAgenda.Gerar();
        var b = TokenFeedAgenda.Gerar();
        Assert.NotEqual(a, b);
        Assert.Equal(43, a.Length);
        Assert.Matches("^[A-Za-z0-9_-]+$", a);
        Assert.Matches("^[0-9a-f]{64}$", TokenFeedAgenda.Hash(a));
        Assert.Equal(TokenFeedAgenda.Hash(a), TokenFeedAgenda.Hash(a));
    }
}

public class AgendaFeedControllerTests
{
    private const string BaseUrl = "https://app.causify.test";
    private static readonly DateTime Hoje = FusoHorario.HojeParede(BrasiliaTime.Tz);

    private readonly AppDbContext _db = new(new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private readonly Mock<IAuditService> _audit = new();
    private readonly Tenant _tenant;
    private readonly Usuario _eu;
    private readonly Usuario _colega;

    public AgendaFeedControllerTests()
    {
        _tenant = NovoTenant("Escritório Silva");
        _eu = NovoUsuario(_tenant, "Ana Souza", PerfilUsuario.Admin);
        _colega = NovoUsuario(_tenant, "Bruno Lima", PerfilUsuario.Advogado);
        _db.SaveChanges();
    }

    private Tenant NovoTenant(string nome, StatusTenant status = StatusTenant.Ativo)
    {
        var t = new Tenant { Id = Guid.NewGuid(), Nome = nome, Status = status, Plano = PlanoTipo.Free, CriadoEm = DateTime.UtcNow };
        _db.Tenants.Add(t);
        return t;
    }

    private Usuario NovoUsuario(Tenant tenant, string nome, PerfilUsuario perfil)
    {
        var email = $"{Guid.NewGuid():N}@x.com";
        var u = new Usuario { Id = Guid.NewGuid(), TenantId = tenant.Id, Nome = nome, Perfil = perfil, Email = email, UserName = email, CriadoEm = DateTime.UtcNow };
        _db.Users.Add(u);
        return u;
    }

    private AgendaFeedController Controller(Usuario? usuario = null, string? papel = null, Guid? impersonadoPor = null,
        string? ifNoneMatch = null)
    {
        usuario ??= _eu;
        var tenant = new Mock<ITenantContext>();
        tenant.Setup(t => t.TenantId).Returns(usuario.TenantId);
        tenant.Setup(t => t.UserId).Returns(usuario.Id);
        tenant.Setup(t => t.UserRole).Returns(papel ?? usuario.Perfil.ToString());
        tenant.Setup(t => t.ImpersonadoPorId).Returns(impersonadoPor);
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["App:FrontendUrl"] = BaseUrl + "/" })
            .Build();
        var http = new DefaultHttpContext();
        if (ifNoneMatch != null) http.Request.Headers.IfNoneMatch = ifNoneMatch;
        return new AgendaFeedController(_db, tenant.Object, new FeedAgendaService(_db, config), _audit.Object)
        {
            ControllerContext = new ControllerContext { HttpContext = http }
        };
    }

    private async Task<string> GerarTokenAsync(Usuario? usuario = null, EscopoFeedAgenda escopo = EscopoFeedAgenda.Meus, bool? ocultar = null)
    {
        var r = await Controller(usuario).Gerar(new ConfigurarFeedAgendaDto(escopo, ocultar), CancellationToken.None);
        var dto = Assert.IsType<FeedAgendaGeradoDto>(Assert.IsType<OkObjectResult>(r.Result).Value);
        Assert.StartsWith(FeedAgendaApi.Prefixo, dto.Caminho);
        return dto.Caminho[FeedAgendaApi.Prefixo.Length..^".ics".Length];
    }

    private async Task<(int Status, string Corpo, string? ETag)> BaixarAsync(string token, string? ifNoneMatch = null)
    {
        var c = Controller(ifNoneMatch: ifNoneMatch);
        var r = await c.Feed(token, CancellationToken.None);
        var etag = c.Response.Headers.ETag.ToString();
        return r switch
        {
            ContentResult cr => (200, cr.Content!.Replace("\r\n ", ""), etag),
            NotFoundResult => (404, "", null),
            StatusCodeResult s => (s.StatusCode, "", etag),
            _ => throw new InvalidOperationException(r.GetType().Name)
        };
    }

    private Evento NovoEvento(string titulo, DateTime dataHora, Guid? responsavel, Tenant? tenant = null,
        TipoEvento tipo = TipoEvento.Audiencia, DateTime? fim = null)
    {
        var e = new Evento
        {
            Id = Guid.NewGuid(), TenantId = (tenant ?? _tenant).Id, Titulo = titulo, Tipo = tipo, DataHora = dataHora,
            DataHoraFim = fim, ResponsavelId = responsavel, Local = "Fórum Central", CriadoEm = DateTime.UtcNow
        };
        _db.Eventos.Add(e);
        return e;
    }

    private Tarefa NovaTarefa(string titulo, DateTime? prazo, Guid? responsavel, StatusTarefa status = StatusTarefa.Pendente,
        TipoTarefa tipo = TipoTarefa.Prazo, int? duracao = null, Guid? criadoPor = null, Contato? contato = null)
    {
        var t = new Tarefa
        {
            Id = Guid.NewGuid(), TenantId = _tenant.Id, Titulo = titulo, Prazo = prazo, ResponsavelId = responsavel,
            CriadoPorId = criadoPor ?? _eu.Id, Status = status, Tipo = tipo, DuracaoMinutos = duracao,
            ContatoId = contato?.Id, Descricao = "Detalhe interno", Prioridade = PrioridadeTarefa.Urgente, CriadoEm = DateTime.UtcNow
        };
        _db.Tarefas.Add(t);
        return t;
    }

    [Fact]
    public async Task Obter_SemLink_RetornaInativo()
    {
        var r = await Controller().Obter(CancellationToken.None);
        var dto = Assert.IsType<FeedAgendaDto>(Assert.IsType<OkObjectResult>(r.Result).Value);
        Assert.False(dto.Ativo);
        Assert.True(dto.PodeEscritorio);
    }

    [Fact]
    public async Task Gerar_GuardaSoOHashDoToken()
    {
        var token = await GerarTokenAsync();

        var feed = await _db.FeedsAgenda.SingleAsync();
        Assert.Equal(TokenFeedAgenda.Hash(token), feed.TokenHash);
        Assert.NotEqual(token, feed.TokenHash);
        Assert.False(feed.OcultarClientes);
        _audit.Verify(a => a.LogAsync(It.Is<AuditLogEntry>(e => e.Acao == "CREATE" && e.Entidade == "FeedAgenda"), It.IsAny<CancellationToken>()), Times.Once);

        var obtido = Assert.IsType<FeedAgendaDto>(Assert.IsType<OkObjectResult>((await Controller().Obter(CancellationToken.None)).Result).Value);
        Assert.True(obtido.Ativo);
    }

    [Fact]
    public async Task Feed_PublicaSoOsItensDoUsuarioNoFusoDoEscritorio()
    {
        var contato = new Contato { Id = Guid.NewGuid(), TenantId = _tenant.Id, Nome = "Maria Cliente" };
        _db.Contatos.Add(contato);
        var audiencia = NovoEvento("Instrução", Hoje.AddDays(3).AddHours(14), _eu.Id);
        var reuniao = NovoEvento("Reunião com perito", Hoje.AddDays(4).AddHours(9), _eu.Id, tipo: TipoEvento.Reuniao,
            fim: Hoje.AddDays(4).AddHours(10).AddMinutes(30));
        NovoEvento("Do colega", Hoje.AddDays(3), _colega.Id);
        NovoEvento("Sem responsável", Hoje.AddDays(3), null);
        NovoEvento("Antigo demais", Hoje.AddDays(-31), _eu.Id);
        var outroTenant = NovoTenant("Outro");
        NovoEvento("De outro escritório", Hoje.AddDays(3), _eu.Id, outroTenant);

        var prazo = NovaTarefa("Contestação", Hoje.AddDays(5), _eu.Id, contato: contato);
        var reservada = NovaTarefa("Revisar minuta", Hoje.AddDays(6).AddHours(15), _eu.Id, tipo: TipoTarefa.Tarefa, duracao: 90);
        var criadaPorMim = NovaTarefa("Sem responsável", Hoje.AddDays(2).AddHours(10), null);
        NovaTarefa("Criada pelo colega", Hoje.AddDays(2), null, criadoPor: _colega.Id);
        NovaTarefa("Concluída", Hoje.AddDays(2), _eu.Id, StatusTarefa.Concluida);
        NovaTarefa("Sem prazo", null, _eu.Id);
        NovaTarefa("Do colega", Hoje.AddDays(2), _colega.Id);
        await _db.SaveChangesAsync();

        var (status, ics, _) = await BaixarAsync(await GerarTokenAsync());

        Assert.Equal(200, status);
        Assert.Contains("X-WR-CALNAME:Causify – Minha agenda\r\n", ics);
        Assert.Equal(5, ics.Split("BEGIN:VEVENT").Length - 1);
        Assert.DoesNotContain("colega", ics);
        Assert.DoesNotContain("De outro", ics);
        Assert.DoesNotContain("Concluída", ics);
        Assert.DoesNotContain("Antigo demais", ics);

        // Audiência às 14h de Brasília = 17h UTC, 1h de duração padrão, alarme 1 dia antes.
        var inicio = Hoje.AddDays(3).AddHours(17);
        Assert.Contains($"UID:evento-{audiencia.Id}@causify.com.br", ics);
        Assert.Contains("SUMMARY:Audiência: Instrução\r\n", ics);
        Assert.Contains($"DTSTART:{inicio:yyyyMMdd}T170000Z", ics);
        Assert.Contains($"DTEND:{inicio:yyyyMMdd}T180000Z", ics);
        Assert.Contains($"URL:{BaseUrl}/pages/agenda.html?date={Hoje.AddDays(3):yyyy-MM-dd}&abrirId={audiencia.Id}", ics);
        Assert.Contains("TRIGGER:-P1D", ics);

        // Reunião: título já começa com o tipo; fim explícito.
        Assert.Contains("SUMMARY:Reunião com perito\r\n", ics);
        Assert.Contains($"DTEND:{Hoje.AddDays(4):yyyyMMdd}T133000Z", ics);
        Assert.Contains($"UID:evento-{reuniao.Id}@causify.com.br", ics);

        // Prazo sem hora: dia inteiro, alarme às 9h da véspera, com contato e prioridade.
        Assert.Contains($"UID:tarefa-{prazo.Id}@causify.com.br", ics);
        Assert.Contains("SUMMARY:Prazo: Contestação\r\n", ics);
        Assert.Contains($"DTSTART;VALUE=DATE:{Hoje.AddDays(5):yyyyMMdd}", ics);
        Assert.Contains("TRIGGER:-PT15H", ics);
        Assert.Contains(@"Contato: Maria Cliente\nPrioridade: Urgente", ics);
        Assert.Contains($"URL:{BaseUrl}/pages/tarefas.html?abrirId={prazo.Id}", ics);

        // Tarefa com hora e duração reservada.
        Assert.Contains($"UID:tarefa-{reservada.Id}@causify.com.br", ics);
        Assert.Contains($"DTSTART:{Hoje.AddDays(6):yyyyMMdd}T180000Z", ics);
        Assert.Contains($"DTEND:{Hoje.AddDays(6):yyyyMMdd}T193000Z", ics);
        Assert.Contains($"UID:tarefa-{criadaPorMim.Id}@causify.com.br", ics);
        Assert.Contains($"DTEND:{Hoje.AddDays(2):yyyyMMdd}T133000Z", ics);

        Assert.NotNull((await _db.FeedsAgenda.SingleAsync()).UltimoAcessoEm);
    }

    [Fact]
    public async Task Feed_FusoDoEscritorioEmManaus()
    {
        _tenant.FusoHorario = "America/Manaus";
        FusoHorario.Invalidar(_tenant.Id);
        var hojeManaus = FusoHorario.HojeParede(FusoHorario.Resolver("America/Manaus"));
        NovoEvento("Instrução", hojeManaus.AddDays(1).AddHours(14), _eu.Id);
        await _db.SaveChangesAsync();

        var (_, ics, _) = await BaixarAsync(await GerarTokenAsync());

        Assert.Contains("X-WR-TIMEZONE:America/Manaus", ics);
        Assert.Contains($"DTSTART:{hojeManaus.AddDays(1):yyyyMMdd}T180000Z", ics);
    }

    [Fact]
    public async Task Feed_EscritorioInteiro_OcultaClientesPorPadrao()
    {
        var processo = new Processo { Id = Guid.NewGuid(), TenantId = _tenant.Id, NumeroCNJ = "0001234-56.2026.8.26.0100" };
        _db.Processos.Add(processo);
        var contato = new Contato { Id = Guid.NewGuid(), TenantId = _tenant.Id, Nome = "Maria Cliente" };
        _db.Contatos.Add(contato);
        var tarefa = NovaTarefa("Prazo da Maria Cliente", Hoje.AddDays(2), _colega.Id, contato: contato);
        tarefa.ProcessoId = processo.Id;
        var evento = NovoEvento("Audiência da Maria", Hoje.AddDays(2).AddHours(10), _colega.Id);
        evento.Observacoes = "Levar documentos da Maria";
        await _db.SaveChangesAsync();

        var (_, ics, _) = await BaixarAsync(await GerarTokenAsync(escopo: EscopoFeedAgenda.Escritorio));

        Assert.True((await _db.FeedsAgenda.SingleAsync()).OcultarClientes);
        Assert.Contains("X-WR-CALNAME:Causify – Escritório Silva", ics);
        Assert.Contains("SUMMARY:Prazo – Proc. 0001234-56.2026.8.26.0100\r\n", ics);
        Assert.Contains("SUMMARY:Audiência\r\n", ics);
        Assert.DoesNotContain("Maria", ics);
        Assert.DoesNotContain("Detalhe interno", ics);
    }

    [Fact]
    public async Task Feed_EscritorioInteiroComNomes_MostraResponsavel()
    {
        NovaTarefa("Recurso", Hoje.AddDays(2), _colega.Id);
        await _db.SaveChangesAsync();

        var (_, ics, _) = await BaixarAsync(await GerarTokenAsync(escopo: EscopoFeedAgenda.Escritorio, ocultar: false));

        Assert.Contains("SUMMARY:Prazo: Recurso", ics);
        Assert.Contains("Responsável: Bruno Lima", ics);
        Assert.Contains("Detalhe interno", ics);
    }

    [Fact]
    public async Task Feed_QuemDeixouDeSerAdmin_VoltaAVerSoOsSeus()
    {
        NovaTarefa("Do colega", Hoje.AddDays(2), _colega.Id);
        await _db.SaveChangesAsync();
        var token = await GerarTokenAsync(escopo: EscopoFeedAgenda.Escritorio);

        _eu.Perfil = PerfilUsuario.Advogado;
        await _db.SaveChangesAsync();
        var (_, ics, _) = await BaixarAsync(token);

        Assert.Contains("Causify – Minha agenda", ics);
        Assert.DoesNotContain("BEGIN:VEVENT", ics);
    }

    [Fact]
    public async Task Feed_TokenInvalidoRevogadoOuTrocado_Da404()
    {
        Assert.Equal(404, (await BaixarAsync("nao-existe")).Status);
        Assert.Equal(404, (await BaixarAsync(new string('a', 200))).Status);
        Assert.Equal(404, (await BaixarAsync(" ")).Status);

        var antigo = await GerarTokenAsync();
        var novo = await GerarTokenAsync();
        Assert.Equal(404, (await BaixarAsync(antigo)).Status);
        Assert.Equal(200, (await BaixarAsync(novo)).Status);
        Assert.Equal(1, await _db.FeedsAgenda.CountAsync());
        _audit.Verify(a => a.LogAsync(It.Is<AuditLogEntry>(e => e.Acao == "UPDATE"), It.IsAny<CancellationToken>()), Times.Once);

        Assert.IsType<NoContentResult>(await Controller().Revogar(CancellationToken.None));
        Assert.Equal(404, (await BaixarAsync(novo)).Status);
        Assert.IsType<NoContentResult>(await Controller().Revogar(CancellationToken.None));
    }

    [Theory]
    [InlineData(StatusTenant.Suspenso)]
    [InlineData(StatusTenant.Cancelado)]
    public async Task Feed_EscritorioSuspensoOuCancelado_Da404(StatusTenant status)
    {
        var token = await GerarTokenAsync();
        _tenant.Status = status;
        await _db.SaveChangesAsync();
        Assert.Equal(404, (await BaixarAsync(token)).Status);
    }

    [Fact]
    public async Task Feed_UsuarioInativo_Da404()
    {
        var token = await GerarTokenAsync();
        _eu.Ativo = false;
        await _db.SaveChangesAsync();
        Assert.Equal(404, (await BaixarAsync(token)).Status);
    }

    [Fact]
    public async Task Feed_ETagIgual_Responde304()
    {
        NovoEvento("Instrução", Hoje.AddDays(3).AddHours(14), _eu.Id);
        await _db.SaveChangesAsync();
        var token = await GerarTokenAsync();

        var (_, _, etag) = await BaixarAsync(token);
        Assert.False(string.IsNullOrEmpty(etag));

        Assert.Equal(304, (await BaixarAsync(token, $"\"outro\", {etag}")).Status);
        Assert.Equal(200, (await BaixarAsync(token, "\"outro\"")).Status);
    }

    [Fact]
    public async Task Gerar_ClienteImpersonacaoOuEscritorioSemSerAdmin_Da403()
    {
        var cliente = await Controller(papel: "Cliente").Gerar(new ConfigurarFeedAgendaDto(), CancellationToken.None);
        Assert.Equal(403, Assert.IsType<ObjectResult>(cliente.Result).StatusCode);

        var impersonado = await Controller(impersonadoPor: Guid.NewGuid()).Gerar(null, CancellationToken.None);
        Assert.Equal(403, Assert.IsType<ObjectResult>(impersonado.Result).StatusCode);

        var naoAdmin = await Controller(_colega).Gerar(new ConfigurarFeedAgendaDto(EscopoFeedAgenda.Escritorio), CancellationToken.None);
        Assert.Equal(403, Assert.IsType<ObjectResult>(naoAdmin.Result).StatusCode);

        var invalido = await Controller().Gerar(new ConfigurarFeedAgendaDto((EscopoFeedAgenda)9), CancellationToken.None);
        Assert.IsType<BadRequestObjectResult>(invalido.Result);

        Assert.Equal(403, Assert.IsType<ObjectResult>((await Controller(papel: "Cliente").Obter(CancellationToken.None)).Result).StatusCode);
        Assert.Equal(403, Assert.IsType<ObjectResult>(await Controller(papel: "Cliente").Revogar(CancellationToken.None)).StatusCode);
        Assert.Empty(_db.FeedsAgenda);
    }

    [Fact]
    public async Task Atualizar_MudaOpcoesSemTrocarOLink()
    {
        Assert.IsType<NotFoundResult>((await Controller().Atualizar(new ConfigurarFeedAgendaDto(), CancellationToken.None)).Result);

        var token = await GerarTokenAsync();
        var hash = (await _db.FeedsAgenda.SingleAsync()).TokenHash;

        var r = await Controller().Atualizar(new ConfigurarFeedAgendaDto(EscopoFeedAgenda.Escritorio), CancellationToken.None);
        var dto = Assert.IsType<FeedAgendaDto>(Assert.IsType<OkObjectResult>(r.Result).Value);

        Assert.Equal(EscopoFeedAgenda.Escritorio, dto.Escopo);
        Assert.True(dto.OcultarClientes);
        Assert.Equal(hash, (await _db.FeedsAgenda.SingleAsync()).TokenHash);
        Assert.Equal(200, (await BaixarAsync(token)).Status);

        var naoAdmin = await Controller(_colega).Atualizar(new ConfigurarFeedAgendaDto(EscopoFeedAgenda.Escritorio), CancellationToken.None);
        Assert.Equal(403, Assert.IsType<ObjectResult>(naoAdmin.Result).StatusCode);
    }
}
