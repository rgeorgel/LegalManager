using System.Text.Json;
using LegalManager.Application.Interfaces;
using LegalManager.Domain.Entities;
using LegalManager.Domain.Enums;
using LegalManager.Infrastructure.Jobs;
using LegalManager.Infrastructure.Persistence;
using LegalManager.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;

namespace LegalManager.UnitTests;

/// <summary>Resumo diário "Seu dia": o que entra conforme o plano, a IA e o horário de envio.</summary>
public class ResumoSeuDiaTests
{
    private static readonly DateTime Segunda = new(2026, 10, 26);
    private static readonly DateTime Terca = new(2026, 10, 27);
    private const string Cnj = "5004321-11.2024.8.21.0001";

    private sealed record Cenario(AppDbContext Db, Guid TenantId, Guid AdvId, Guid ProcessoId);

    private static async Task<Cenario> SeedAsync(PlanoTipo plano, DateTime? andamentoCapturadoEm = null)
    {
        var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var tenant = new Tenant { Id = Guid.NewGuid(), Nome = "Escritório", Plano = plano, Status = StatusTenant.Ativo, CriadoEm = DateTime.UtcNow };
        var adv = new Usuario
        {
            Id = Guid.NewGuid(), TenantId = tenant.Id, Nome = "Ana", Email = "ana@esc.com", UserName = "ana@esc.com",
            Perfil = PerfilUsuario.Advogado, Ativo = true, CriadoEm = DateTime.UtcNow
        };
        var processo = new Processo
        {
            Id = Guid.NewGuid(), TenantId = tenant.Id, NumeroCNJ = Cnj, Classe = "Execução de Título",
            AreaDireito = AreaDireito.Civil, Fase = FaseProcessual.Execucao, Status = StatusProcesso.Ativo,
            AdvogadoResponsavelId = adv.Id, Monitorado = true, CriadoEm = DateTime.UtcNow
        };
        var andamento = new Andamento
        {
            Id = Guid.NewGuid(), TenantId = tenant.Id, ProcessoId = processo.Id, Data = new DateTime(2026, 10, 23),
            Descricao = "Defiro o pedido de penhora on-line via SISBAJUD.", Fonte = FonteAndamento.Automatico,
            CriadoEm = andamentoCapturadoEm ?? DateTime.UtcNow.AddHours(-1)
        };
        var pub = new Publicacao
        {
            Id = Guid.NewGuid(), TenantId = tenant.Id, ProcessoId = processo.Id, NumeroCNJ = Cnj, Diario = "DJRS",
            DataPublicacao = new DateTime(2026, 10, 23), Conteudo = "Intime-se para manifestação em 15 dias.",
            Status = StatusPublicacao.Nova, FonteCaptura = FonteCaptura.Escavador, CapturaEm = DateTime.UtcNow
        };
        var triagem = new TriagemPublicacao
        {
            Id = Guid.NewGuid(), TenantId = tenant.Id, PublicacaoId = pub.Id, Status = StatusTriagem.Sugerida,
            Ato = "Manifestar sobre o laudo", PrazoDias = 15, TipoCalculo = TipoCalculo.DiasUteis,
            Vencimento = new DateTime(2026, 11, 16), CriadoEm = DateTime.UtcNow
        };
        db.AddRange(tenant, adv, processo, andamento, pub, triagem);
        await db.SaveChangesAsync();
        return new Cenario(db, tenant.Id, adv.Id, processo.Id);
    }

    private static Mock<IPreferenciasNotificacaoService> Prefs(Guid tenantId, Guid userId, bool publicacoes = true)
    {
        var mock = new Mock<IPreferenciasNotificacaoService>();
        mock.Setup(p => p.PermiteEmailAsync(tenantId, userId, It.IsAny<string>())).ReturnsAsync(true);
        mock.Setup(p => p.PermiteEmailAsync(tenantId, userId, "Publicacoes")).ReturnsAsync(publicacoes);
        mock.Setup(p => p.PermiteInAppAsync(tenantId, userId, It.IsAny<string>())).ReturnsAsync(true);
        return mock;
    }

    private static (Mock<IServiceProvider> services, Mock<IIAService> ia) IA(string? resposta = null, Exception? erro = null)
    {
        var ia = new Mock<IIAService>();
        var setup = ia.Setup(i => i.ResumirDiaAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()));
        if (erro is not null) setup.ThrowsAsync(erro);
        else setup.ReturnsAsync(resposta ?? JsonSerializer.Serialize(new
        {
            visaoGeral = "Dia tranquilo, com uma novidade na execução.",
            processos = new[] { new { id = 1, texto = "O juiz aceitou bloquear valores da devedora.", proximoPasso = "Acompanhar o bloqueio." } }
        }));
        var services = new Mock<IServiceProvider>();
        services.Setup(s => s.GetService(typeof(IIAService))).Returns(ia.Object);
        return (services, ia);
    }

    private static async Task<ResumoDiarioExtras?> ExecutarAsync(Cenario c, DateTime hoje, Mock<IServiceProvider>? services,
        Mock<IEmailService>? email = null, bool publicacoes = true)
    {
        email ??= new Mock<IEmailService>();
        ResumoDiarioExtras? extras = null;
        email.Setup(e => e.EnviarResumoDiarioAsync(It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<IReadOnlyList<ResumoTarefaItem>>(), It.IsAny<IReadOnlyList<ResumoEventoItem>>(),
                It.IsAny<ResumoDiarioExtras?>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, IReadOnlyList<ResumoTarefaItem>, IReadOnlyList<ResumoEventoItem>, ResumoDiarioExtras?, CancellationToken>(
                (_, _, _, _, x, _) => extras = x)
            .Returns(Task.CompletedTask);
        var job = new AlertasJob(c.Db, email.Object, Prefs(c.TenantId, c.AdvId, publicacoes).Object,
            Mock.Of<ILogger<AlertasJob>>(), services?.Object);
        await job.ExecutarAsync(hoje);
        return extras;
    }

    [Fact]
    public async Task PlanoPro_TemSugestoesETextosDaIA_SemConvite()
    {
        var c = await SeedAsync(PlanoTipo.Pro);
        var (services, ia) = IA();

        var x = await ExecutarAsync(c, Segunda, services);

        Assert.NotNull(x);
        Assert.Equal("Dia tranquilo, com uma novidade na execução.", x!.VisaoGeral);
        var sugestao = Assert.Single(x.Sugestoes!);
        Assert.Equal("Manifestar sobre o laudo", sugestao.Titulo);
        Assert.Equal(new DateTime(2026, 11, 16), sugestao.Vencimento);
        var processo = Assert.Single(x.Processos!);
        Assert.True(processo.TextoIA);
        Assert.Equal("O juiz aceitou bloquear valores da devedora.", processo.Texto);
        Assert.Equal("Acompanhar o bloqueio.", processo.ProximoPasso);
        Assert.True(x.TemPublicacoes);
        Assert.Equal(ConviteResumo.Nenhum, x.Convite);
        // A IA recebe só fatos do banco.
        ia.Verify(i => i.ResumirDiaAsync(It.Is<string>(s => s.Contains("SISBAJUD") && s.Contains("Manifestar sobre o laudo")),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task PlanoFree_SemIA_TextoDoTribunal_ConviteNaSegunda()
    {
        var c = await SeedAsync(PlanoTipo.Free);
        var (services, ia) = IA();

        var x = await ExecutarAsync(c, Segunda, services);

        Assert.Null(x!.VisaoGeral);
        var processo = Assert.Single(x.Processos!);
        Assert.False(processo.TextoIA);
        Assert.Equal("Defiro o pedido de penhora on-line via SISBAJUD.", processo.Texto);
        Assert.Equal("Execução de Título", processo.Titulo);
        Assert.Empty(x.Sugestoes!); // Free não tem publicações
        Assert.False(x.TemPublicacoes);
        Assert.Equal(ConviteResumo.Plus, x.Convite);
        ia.Verify(i => i.ResumirDiaAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task PlanoPlus_ComIA_SemPublicacoes_ConviteSoNaSegunda()
    {
        var c = await SeedAsync(PlanoTipo.Plus);
        var (services, _) = IA();

        var x = await ExecutarAsync(c, Segunda, services);
        Assert.NotNull(x!.VisaoGeral);
        Assert.True(Assert.Single(x.Processos!).TextoIA);
        Assert.Empty(x.Sugestoes!);
        Assert.Equal(ConviteResumo.Pro, x.Convite);

        var c2 = await SeedAsync(PlanoTipo.Plus);
        var naTerca = await ExecutarAsync(c2, Terca, IA().services);
        Assert.Equal(ConviteResumo.Nenhum, naTerca!.Convite);
    }

    [Fact]
    public async Task FalhaDaIA_EnviaComOTextoDoTribunal()
    {
        var c = await SeedAsync(PlanoTipo.Pro);
        var (services, _) = IA(erro: new HttpRequestException("timeout"));

        var x = await ExecutarAsync(c, Segunda, services);

        Assert.Null(x!.VisaoGeral);
        Assert.False(Assert.Single(x.Processos!).TextoIA);
        Assert.Single(x.Sugestoes!);
    }

    [Fact]
    public async Task RespostaDaIAForaDoFormato_EnviaSemTextosDaIA()
    {
        var c = await SeedAsync(PlanoTipo.Pro);

        var x = await ExecutarAsync(c, Segunda, IA(resposta: "desculpe, não consegui").services);

        Assert.Null(x!.VisaoGeral);
        Assert.False(Assert.Single(x.Processos!).TextoIA);
    }

    [Fact]
    public async Task PreferenciaDePublicacoesDesligada_SemNadaMais_NaoEnvia()
    {
        var c = await SeedAsync(PlanoTipo.Pro);
        var email = new Mock<IEmailService>();

        var x = await ExecutarAsync(c, Segunda, IA().services, email, publicacoes: false);

        Assert.Null(x);
        email.Verify(e => e.EnviarResumoDiarioAsync(It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<IReadOnlyList<ResumoTarefaItem>>(), It.IsAny<IReadOnlyList<ResumoEventoItem>>(),
            It.IsAny<ResumoDiarioExtras?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AndamentoAntigo_NaoEntra()
    {
        var c = await SeedAsync(PlanoTipo.Free, andamentoCapturadoEm: DateTime.UtcNow.AddDays(-2));

        var x = await ExecutarAsync(c, Segunda, null);

        Assert.Null(x); // Free: sem publicações, sem andamento novo e sem tarefas → nada a enviar
    }

    [Theory]
    [InlineData(9, false)]   // 06:00 em Brasília
    [InlineData(10, true)]   // 07:00 em Brasília
    public async Task SemDataFixa_SoEnviaAPartirDas7hNoFusoDoEscritorio(int horaUtc, bool envia)
    {
        var agora = new DateTime(2026, 10, 26, horaUtc, 0, 0, DateTimeKind.Utc);
        var c = await SeedAsync(PlanoTipo.Free, andamentoCapturadoEm: agora.AddHours(-1));
        var email = new Mock<IEmailService>();
        var job = new AlertasJob(c.Db, email.Object, Prefs(c.TenantId, c.AdvId).Object, Mock.Of<ILogger<AlertasJob>>())
        {
            RelogioUtc = () => agora
        };

        await job.ExecutarAsync();
        await job.ExecutarAsync(); // de hora em hora: não repete no mesmo dia

        email.Verify(e => e.EnviarResumoDiarioAsync("ana@esc.com", "Ana",
            It.IsAny<IReadOnlyList<ResumoTarefaItem>>(), It.IsAny<IReadOnlyList<ResumoEventoItem>>(),
            It.Is<ResumoDiarioExtras?>(x => x!.Hoje == new DateTime(2026, 10, 26)), It.IsAny<CancellationToken>()),
            envia ? Times.Once() : Times.Never());
    }
}

public class ResumoDiaParserTests
{
    [Fact]
    public void Parse_LeVisaoGeralEProcessos_IgnorandoRaciocinio()
    {
        var r = ResumoDiaParser.Parse("<think>...</think>```json\n" + JsonSerializer.Serialize(new
        {
            visaoGeral = "Dia calmo.",
            processos = new object[]
            {
                new { id = 1, texto = "Penhora deferida.", proximoPasso = (string?)null },
                new { id = "x", texto = "id inválido" },
                new { id = 2 }
            }
        }) + "\n```")!;

        Assert.Equal("Dia calmo.", r.VisaoGeral);
        var p = Assert.Single(r.Processos);
        Assert.Equal(1, p.Key);
        Assert.Equal("Penhora deferida.", p.Value.Texto);
        Assert.Null(p.Value.ProximoPasso);
    }

    [Theory]
    [InlineData("")]
    [InlineData("sem json")]
    [InlineData("{ quebrado")]
    public void Parse_Invalido_DevolveNull(string resposta) => Assert.Null(ResumoDiaParser.Parse(resposta));
}
