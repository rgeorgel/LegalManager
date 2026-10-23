using System.Text.Json;
using LegalManager.Application.DTOs.Publicacoes;
using LegalManager.Application.Interfaces;
using LegalManager.Domain.Entities;
using LegalManager.Domain.Enums;
using LegalManager.Domain.Interfaces;
using LegalManager.Infrastructure.Jobs;
using LegalManager.Infrastructure.Persistence;
using LegalManager.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace LegalManager.UnitTests;

public class CalculadoraPrazoProcessualTests
{
    private static DateTime D(string s) => DateTime.Parse(s);

    [Fact]
    public void DiasUteis_SemPrazoProcessual_MantemCalculoAntigo()
    {
        var (fim, _) = FeriadosService.CalcularPrazo(D("2026-12-15"), 10, TipoCalculo.DiasUteis);
        Assert.Equal(D("2026-12-30"), fim);
    }

    [Fact]
    public void DiasUteis_PrazoProcessual_NaoContaRecessoForense()
    {
        var (fim, feriados) = FeriadosService.CalcularPrazo(D("2026-12-15"), 10, TipoCalculo.DiasUteis, prazoProcessual: true);
        Assert.Equal(D("2027-01-29"), fim);
        Assert.Contains(feriados, f => f.StartsWith("recesso"));
    }

    [Fact]
    public void DiasCorridos_PrazoProcessual_ComecaNoDiaUtilEProrrogaVencimento()
    {
        // Sexta 30/10: o 1º dia contado é terça 03/11 (segunda é Finados); o 5º cai no sábado 07/11.
        var (fim, _) = FeriadosService.CalcularPrazo(D("2026-10-30"), 5, TipoCalculo.DiasCorridos, prazoProcessual: true);
        Assert.Equal(D("2026-11-09"), fim);

        var (antigo, _) = FeriadosService.CalcularPrazo(D("2026-10-30"), 5, TipoCalculo.DiasCorridos);
        Assert.Equal(D("2026-11-04"), antigo);
    }

    [Fact]
    public void DataPublicacaoDiario_EhOPrimeiroDiaUtilSeguinte()
    {
        // Sexta 09/10 → segunda 12/10 é feriado → terça 13/10.
        Assert.Equal(D("2026-10-13"), FeriadosService.DataPublicacaoDiario(D("2026-10-09")));
        Assert.Equal(D("2026-10-14"), FeriadosService.DataPublicacaoDiario(D("2026-10-09"), [new DateOnly(2026, 10, 13)]));
    }

    [Theory]
    [InlineData("1001885-87.2026.8.26.0077", "SP")]
    [InlineData("0800123-45.2025.8.19.0001", "RJ")]
    [InlineData("0000123-45.2025.5.02.0001", "SP")]
    [InlineData("0000123-45.2025.5.10.0001", null)] // TRT 10: DF e TO
    [InlineData("0000123-45.2025.4.03.6100", null)] // TRF 3: mais de um estado
    [InlineData("123", null)]
    [InlineData(null, null)]
    public void UfProcesso_PeloNumeroCnj(string? cnj, string? uf) => Assert.Equal(uf, UfProcesso.DoCnj(cnj));
}

public class TriagemPublicacaoParserTests
{
    [Fact]
    public void Parse_IgnoraRaciocinioECercaDeCodigo()
    {
        var resposta = "<think>vou analisar</think>\n```json\n" + JsonSerializer.Serialize(new
        {
            tipo = "Intimacao", resumo = "Intimação para contrarrazões", exigeAcao = true, ato = "Apresentar contrarrazões",
            prazo = new { quantidade = "15", unidade = "dias", trecho = "no prazo de 15 dias" },
            audiencia = (object?)null
        }) + "\n```";

        var r = TriagemPublicacaoParser.Parse(resposta)!;

        Assert.Equal(TipoPublicacao.Intimacao, r.Tipo);
        Assert.True(r.ExigeAcao);
        Assert.Equal("Apresentar contrarrazões", r.Ato);
        Assert.Equal(15, r.PrazoQuantidade);
        Assert.Equal("dias", r.PrazoUnidade);
        Assert.Null(r.AudienciaDataHora);
    }

    [Fact]
    public void Parse_LeAudienciaETipoDesconhecidoViraOutro()
    {
        var resposta = JsonSerializer.Serialize(new
        {
            tipo = "Qualquer", resumo = "Audiência designada", exigeAcao = true,
            audiencia = new { dataHora = "2026-11-12T14:30", trecho = "dia 12/11/2026, às 14h30" }
        });

        var r = TriagemPublicacaoParser.Parse(resposta)!;

        Assert.Equal(TipoPublicacao.Outro, r.Tipo);
        Assert.Equal(new DateTime(2026, 11, 12, 14, 30, 0), r.AudienciaDataHora);
    }

    [Theory]
    [InlineData("isso não é json")]
    [InlineData("")]
    [InlineData("{ quebrado")]
    public void Parse_RespostaInvalida_DevolveNull(string resposta) => Assert.Null(TriagemPublicacaoParser.Parse(resposta));

    [Fact]
    public void TrechoConfere_IgnoraMaiusculasEQuebrasDeLinha()
    {
        const string conteudo = "Intime-se a parte apelada para, no prazo de\n15 (quinze)  dias, apresentar contrarrazões.";
        Assert.True(TriagemPublicacaoParser.TrechoConfere("No prazo de 15 (quinze) dias", conteudo));
        Assert.True(TriagemPublicacaoParser.TrechoConfere("“no prazo de 15 (quinze) dias...”", conteudo));
        Assert.False(TriagemPublicacaoParser.TrechoConfere("no prazo de 5 (cinco) dias", conteudo));
        Assert.False(TriagemPublicacaoParser.TrechoConfere("15", conteudo));
        Assert.False(TriagemPublicacaoParser.TrechoConfere(null, conteudo));
    }
}

public class TriagemPublicacoesJobTests
{
    private const string Cnj = "1001885-87.2026.8.26.0077";
    private const string Texto =
        "Intime-se a parte apelada para, no prazo de 15 (quinze) dias, apresentar contrarrazões ao recurso de apelação.";

    private sealed record Cenario(AppDbContext Db, Tenant Tenant, Processo Processo, Usuario Adv, Publicacao Pub);

    private static async Task<Cenario> SeedAsync(
        PlanoTipo plano = PlanoTipo.Pro, AreaDireito area = AreaDireito.Civil, DateTime? dataDiario = null, string texto = Texto)
    {
        var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var tenant = new Tenant { Id = Guid.NewGuid(), Nome = "Escritório", Plano = plano, Status = StatusTenant.Ativo, CriadoEm = DateTime.UtcNow };
        var adv = new Usuario
        {
            Id = Guid.NewGuid(), TenantId = tenant.Id, Nome = "Adv", Email = "adv@esc.com", UserName = "adv@esc.com",
            Perfil = PerfilUsuario.Advogado, Ativo = true, CriadoEm = DateTime.UtcNow
        };
        var processo = new Processo
        {
            Id = Guid.NewGuid(), TenantId = tenant.Id, NumeroCNJ = Cnj, AreaDireito = area, Comarca = "Campinas",
            Fase = FaseProcessual.Conhecimento, Status = StatusProcesso.Ativo, AdvogadoResponsavelId = adv.Id, CriadoEm = DateTime.UtcNow
        };
        var pub = new Publicacao
        {
            Id = Guid.NewGuid(), TenantId = tenant.Id, ProcessoId = processo.Id, NumeroCNJ = Cnj, Diario = "DJSP",
            DataPublicacao = DateTime.SpecifyKind(dataDiario ?? new DateTime(2026, 10, 21), DateTimeKind.Utc),
            Conteudo = texto, Tipo = TipoPublicacao.Outro, Status = StatusPublicacao.Nova,
            FonteCaptura = FonteCaptura.Escavador, CapturaEm = DateTime.UtcNow
        };
        db.AddRange(tenant, adv, processo, pub);
        await db.SaveChangesAsync();
        return new Cenario(db, tenant, processo, adv, pub);
    }

    private static string RespostaPrazo(object? prazo, bool exigeAcao = true, object? audiencia = null) =>
        JsonSerializer.Serialize(new
        {
            tipo = "Intimacao", resumo = "Intimação para contrarrazões", exigeAcao, ato = "Apresentar contrarrazões",
            prazo, audiencia
        });

    private static (TriagemPublicacoesJob job, Mock<IIAService> ia, Mock<IEmailService> email) Job(AppDbContext db, string resposta)
    {
        var ia = new Mock<IIAService>();
        ia.Setup(i => i.TriarPublicacaoAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(resposta);
        var email = new Mock<IEmailService>();
        return (new TriagemPublicacoesJob(db, ia.Object, email.Object, NullLogger<TriagemPublicacoesJob>.Instance), ia, email);
    }

    private static readonly DateTime HojeLonge = new(2026, 10, 1);

    [Fact]
    public async Task Prazo_EhCalculadoPeloCodigo_ComFeriadosDaUfDoProcesso()
    {
        var c = await SeedAsync();
        c.Db.Feriados.Add(new Feriado { Data = new DateOnly(2026, 11, 9), Nome = "Feriado SP", Tipo = "estadual", Uf = "SP", Ativo = true, CriadoEm = DateTime.UtcNow, AtualizadoEm = DateTime.UtcNow });
        await c.Db.SaveChangesAsync();
        var (job, _, _) = Job(c.Db, RespostaPrazo(new { quantidade = 15, unidade = "dias", trecho = "no prazo de 15 (quinze) dias" }));

        await job.ExecutarAsync(CancellationToken.None, HojeLonge);

        var t = await c.Db.TriagensPublicacao.SingleAsync();
        Assert.Equal(StatusTriagem.Sugerida, t.Status);
        Assert.Equal(15, t.PrazoDias);
        Assert.Equal(TipoCalculo.DiasUteis, t.TipoCalculo);
        // Disponibilizada qua 21/10 → publicação qui 22/10 → 15 dias úteis (Finados e o feriado de SP fora).
        Assert.Equal(new DateTime(2026, 10, 22), t.DataPublicacao);
        Assert.Equal(new DateTime(2026, 11, 16), t.Vencimento);
        Assert.Contains("SP", t.MemoriaCalculo);
        var pub = await c.Db.Publicacoes.SingleAsync();
        Assert.Equal(TipoPublicacao.Intimacao, pub.Tipo);
        Assert.Equal("Intimação para contrarrazões", pub.ClassificacaoIA);
        Assert.False(pub.Urgente);
    }

    [Fact]
    public async Task TrechoInventado_DescartaOPrazo_MasMantemASugestao()
    {
        var c = await SeedAsync();
        var (job, _, _) = Job(c.Db, RespostaPrazo(new { quantidade = 5, unidade = "dias", trecho = "no prazo de 5 (cinco) dias" }));

        await job.ExecutarAsync(CancellationToken.None, HojeLonge);

        var t = await c.Db.TriagensPublicacao.SingleAsync();
        Assert.Equal(StatusTriagem.Sugerida, t.Status);
        Assert.Null(t.PrazoDias);
        Assert.Null(t.Vencimento);
    }

    [Fact]
    public async Task SemProvidencia_FicaSemAcao()
    {
        var c = await SeedAsync(texto: "Juntada de petição. Aguarde-se.");
        var (job, _, _) = Job(c.Db, RespostaPrazo(null, exigeAcao: false));

        await job.ExecutarAsync(CancellationToken.None, HojeLonge);

        Assert.Equal(StatusTriagem.SemAcao, (await c.Db.TriagensPublicacao.SingleAsync()).Status);
    }

    [Fact]
    public async Task Criminal_DiasSemEspecificar_ContaCorridos()
    {
        var c = await SeedAsync(area: AreaDireito.Criminal);
        var (job, _, _) = Job(c.Db, RespostaPrazo(new { quantidade = 15, unidade = "dias", trecho = "no prazo de 15 (quinze) dias" }));

        await job.ExecutarAsync(CancellationToken.None, HojeLonge);

        Assert.Equal(TipoCalculo.DiasCorridos, (await c.Db.TriagensPublicacao.SingleAsync()).TipoCalculo);
    }

    [Fact]
    public async Task PrazoUrgente_AvisaOResponsavel()
    {
        var c = await SeedAsync();
        var (job, _, email) = Job(c.Db, RespostaPrazo(new { quantidade = 15, unidade = "dias", trecho = "no prazo de 15 (quinze) dias" }));

        // Vence em 13/11; "hoje" em 10/11 → 3 dias.
        await job.ExecutarAsync(CancellationToken.None, new DateTime(2026, 11, 10));

        Assert.True((await c.Db.Publicacoes.SingleAsync()).Urgente);
        var n = await c.Db.Notificacoes.SingleAsync();
        Assert.Equal(c.Adv.Id, n.UsuarioId);
        email.Verify(e => e.EnviarAlertaPrazoProcessualAsync("adv@esc.com", "Adv", Cnj, It.IsAny<string>(),
            It.IsAny<DateTime>(), 3, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task FalhaDaIA_TentaTresVezesEDesiste()
    {
        var c = await SeedAsync();
        var ia = new Mock<IIAService>();
        ia.Setup(i => i.TriarPublicacaoAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("timeout"));
        var job = new TriagemPublicacoesJob(c.Db, ia.Object, new Mock<IEmailService>().Object, NullLogger<TriagemPublicacoesJob>.Instance);

        for (var i = 0; i < 4; i++) await job.ExecutarAsync(CancellationToken.None, HojeLonge);

        var t = await c.Db.TriagensPublicacao.SingleAsync();
        Assert.Equal(StatusTriagem.Falhou, t.Status);
        Assert.Equal(TriagemPublicacoesJob.MaxTentativas, t.Tentativas);
        ia.Verify(i => i.TriarPublicacaoAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Exactly(3));
    }

    [Fact]
    public async Task PlanoSemPublicacoes_NaoEntraNaFila()
    {
        var c = await SeedAsync(plano: PlanoTipo.Free);
        var (job, ia, _) = Job(c.Db, RespostaPrazo(null));

        await job.ExecutarAsync(CancellationToken.None, HojeLonge);

        Assert.Empty(c.Db.TriagensPublicacao);
        ia.Verify(i => i.TriarPublicacaoAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── Aceitar / ignorar / refazer ─────────────────────────────────────────

    private static TriagemPublicacaoService Servico(Cenario c, Mock<IAuditService>? audit = null)
    {
        var tenant = new Mock<ITenantContext>();
        tenant.Setup(t => t.TenantId).Returns(c.Tenant.Id);
        tenant.Setup(t => t.UserId).Returns(c.Adv.Id);
        tenant.Setup(t => t.Plano).Returns(c.Tenant.Plano);
        return new TriagemPublicacaoService(c.Db, tenant.Object, new TarefaService(c.Db, tenant.Object),
            new EventoService(c.Db, tenant.Object), (audit ?? new Mock<IAuditService>()).Object);
    }

    private static async Task<Cenario> TriadaAsync(object? audiencia = null)
    {
        var c = await SeedAsync();
        var (job, _, _) = Job(c.Db, RespostaPrazo(new { quantidade = 15, unidade = "dias", trecho = "no prazo de 15 (quinze) dias" }, audiencia: audiencia));
        await job.ExecutarAsync(CancellationToken.None, HojeLonge);
        return c;
    }

    [Fact]
    public async Task Aceitar_CriaPrazoSoComADataFinal_EMarcaPublicacaoComoLida()
    {
        var c = await TriadaAsync();
        var audit = new Mock<IAuditService>();

        var dto = await Servico(c, audit).AceitarAsync(c.Pub.Id, new AceitarTriagemDto(null, null, null), "1.2.3.4");

        Assert.Equal(StatusTriagem.Aceita, dto.Status);
        var tarefa = await c.Db.Tarefas.SingleAsync();
        Assert.Equal(dto.TarefaId, tarefa.Id);
        Assert.Equal(TipoTarefa.Prazo, tarefa.Tipo);
        Assert.Equal("Apresentar contrarrazões", tarefa.Titulo);
        Assert.Equal(new DateTime(2026, 11, 13, 12, 0, 0), tarefa.Prazo);
        Assert.Null(tarefa.QuantidadeDias); // sem início + dias: editar a tarefa não recalcula o prazo
        Assert.Equal(c.Processo.Id, tarefa.ProcessoId);
        Assert.Equal(c.Adv.Id, tarefa.ResponsavelId);
        Assert.Contains("Confira o prazo", tarefa.Descricao);
        Assert.Equal(StatusPublicacao.Lida, (await c.Db.Publicacoes.SingleAsync()).Status);
        audit.Verify(a => a.LogAsync(It.IsAny<AuditLogEntry>(), It.IsAny<CancellationToken>()), Times.Once);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Servico(c).AceitarAsync(c.Pub.Id, new AceitarTriagemDto(null, null, null), null));
        Assert.Single(c.Db.Tarefas);
    }

    [Fact]
    public async Task Aceitar_ComDataEditada_UsaADataDoUsuario()
    {
        var c = await TriadaAsync();

        await Servico(c).AceitarAsync(c.Pub.Id, new AceitarTriagemDto("Contrarrazões", new DateTime(2026, 11, 10, 9, 0, 0), null), null);

        var tarefa = await c.Db.Tarefas.SingleAsync();
        Assert.Equal("Contrarrazões", tarefa.Titulo);
        Assert.Equal(new DateTime(2026, 11, 10, 9, 0, 0), tarefa.Prazo);
    }

    [Fact]
    public async Task Aceitar_Evento_CriaAudienciaNaAgenda()
    {
        var c = await TriadaAsync(new { dataHora = "2026-11-25T14:00", trecho = "apresentar contrarrazões ao recurso" });

        var dto = await Servico(c).AceitarAsync(c.Pub.Id, new AceitarTriagemDto(null, null, null, CriarEvento: true), null);

        var evento = await c.Db.Eventos.SingleAsync();
        Assert.Equal(dto.EventoId, evento.Id);
        Assert.Equal(TipoEvento.Audiencia, evento.Tipo);
        Assert.Equal(new DateTime(2026, 11, 25, 14, 0, 0), evento.DataHora);
        Assert.Empty(c.Db.Tarefas);
    }

    [Fact]
    public async Task Ignorar_E_Refazer()
    {
        var c = await TriadaAsync();
        var svc = Servico(c);

        Assert.Equal(StatusTriagem.Ignorada, (await svc.IgnorarAsync(c.Pub.Id)).Status);
        var refeita = await svc.RefazerAsync(c.Pub.Id);

        Assert.Equal(StatusTriagem.Pendente, refeita.Status);
        Assert.Null(refeita.Vencimento);
        Assert.Equal(0, (await c.Db.TriagensPublicacao.SingleAsync()).Tentativas);
    }

    [Fact]
    public async Task Refazer_PublicacaoSemTriagem_PoeNaFila()
    {
        var c = await SeedAsync();

        var dto = await Servico(c).RefazerAsync(c.Pub.Id);

        Assert.Equal(StatusTriagem.Pendente, dto.Status);
        Assert.Single(c.Db.TriagensPublicacao);
    }

    [Fact]
    public async Task OutroEscritorio_NaoEncontraASugestao()
    {
        var c = await TriadaAsync();
        var outro = new Mock<ITenantContext>();
        outro.Setup(t => t.TenantId).Returns(Guid.NewGuid());
        var svc = new TriagemPublicacaoService(c.Db, outro.Object, new TarefaService(c.Db, outro.Object),
            new EventoService(c.Db, outro.Object), new Mock<IAuditService>().Object);

        await Assert.ThrowsAsync<KeyNotFoundException>(() => svc.AceitarAsync(c.Pub.Id, new AceitarTriagemDto(null, null, null), null));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => svc.RefazerAsync(c.Pub.Id));
    }
}
