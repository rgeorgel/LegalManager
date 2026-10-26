using LegalManager.Application.Interfaces;
using LegalManager.Domain.Entities;
using LegalManager.Domain.Enums;
using LegalManager.Infrastructure;
using LegalManager.Infrastructure.Jobs;
using LegalManager.Infrastructure.Persistence;
using LegalManager.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;

namespace LegalManager.UnitTests;

public class AlertasJobTests
{
    private AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options);
    }

    private async Task<(AppDbContext ctx, Guid tenantId, Guid responsavelId)> SeedAsync()
    {
        var ctx = CreateContext();
        var tenantId = Guid.NewGuid();
        var responsavelId = Guid.NewGuid();

        ctx.Tenants.Add(new Tenant
        {
            Id = tenantId, Nome = "Test Tenant", Plano = PlanoTipo.Pro,
            Status = StatusTenant.Ativo, CriadoEm = DateTime.UtcNow
        });
        ctx.Users.Add(new Usuario
        {
            Id = responsavelId, TenantId = tenantId, Nome = "Responsável",
            Email = "resp@test.com", UserName = "resp@test.com",
            Perfil = PerfilUsuario.Advogado, Ativo = true, CriadoEm = DateTime.UtcNow
        });
        await ctx.SaveChangesAsync();
        return (ctx, tenantId, responsavelId);
    }

    private static Mock<IPreferenciasNotificacaoService> PrefAberto(Guid tenantId, Guid userId)
    {
        var mock = new Mock<IPreferenciasNotificacaoService>();
        mock.Setup(p => p.PermiteEmailAsync(tenantId, userId, It.IsAny<string>())).ReturnsAsync(true);
        mock.Setup(p => p.PermiteInAppAsync(tenantId, userId, It.IsAny<string>())).ReturnsAsync(true);
        return mock;
    }

    [Fact]
    public async Task ExecutarAsync_DeveAlertarTarefaVencendoHoje()
    {
        var (ctx, tenantId, responsavelId) = await SeedAsync();
        var hoje = BrasiliaTime.Hoje;
        ctx.Tarefas.Add(new Tarefa
        {
            Id = Guid.NewGuid(), TenantId = tenantId, Titulo = "Tarefa Urgente",
            Status = StatusTarefa.Pendente, Prioridade = PrioridadeTarefa.Alta,
            Prazo = hoje, ResponsavelId = responsavelId,
            CriadoEm = DateTime.UtcNow, CriadoPorId = responsavelId
        });
        await ctx.SaveChangesAsync();

        var mockEmail = new Mock<IEmailService>();
        var job = new AlertasJob(ctx, mockEmail.Object, PrefAberto(tenantId, responsavelId).Object, Mock.Of<ILogger<AlertasJob>>());
        await job.ExecutarAsync(hoje);

        mockEmail.Verify(e => e.EnviarResumoDiarioAsync(
            "resp@test.com", "Responsável",
            It.Is<IReadOnlyList<ResumoTarefaItem>>(l => l.Count == 1 && l[0].Dias == 0 && l[0].Titulo == "Tarefa Urgente"), It.IsAny<IReadOnlyList<ResumoEventoItem>>(), It.IsAny<ResumoDiarioExtras?>()),
            Times.Once);

        var notif = await ctx.Notificacoes.FirstOrDefaultAsync(n => n.Tipo == TipoNotificacao.PrazoTarefa);
        Assert.NotNull(notif);
    }

    [Fact]
    public async Task ExecutarAsync_NaoDeveEnviarEmail_QuandoPreferenciasDesabilitadas()
    {
        var (ctx, tenantId, responsavelId) = await SeedAsync();
        var hoje = BrasiliaTime.Hoje;
        ctx.Tarefas.Add(new Tarefa
        {
            Id = Guid.NewGuid(), TenantId = tenantId, Titulo = "Tarefa",
            Status = StatusTarefa.Pendente, Prioridade = PrioridadeTarefa.Media,
            Prazo = hoje, ResponsavelId = responsavelId,
            CriadoEm = DateTime.UtcNow, CriadoPorId = responsavelId
        });
        await ctx.SaveChangesAsync();

        var mockEmail = new Mock<IEmailService>();
        var mockPrefs = new Mock<IPreferenciasNotificacaoService>();
        mockPrefs.Setup(p => p.PermiteEmailAsync(tenantId, responsavelId, It.IsAny<string>())).ReturnsAsync(false);
        mockPrefs.Setup(p => p.PermiteInAppAsync(tenantId, responsavelId, It.IsAny<string>())).ReturnsAsync(false);

        var job = new AlertasJob(ctx, mockEmail.Object, mockPrefs.Object, Mock.Of<ILogger<AlertasJob>>());
        await job.ExecutarAsync(hoje);

        mockEmail.Verify(e => e.EnviarResumoDiarioAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IReadOnlyList<ResumoTarefaItem>>(), It.IsAny<IReadOnlyList<ResumoEventoItem>>(), It.IsAny<ResumoDiarioExtras?>()),
            Times.Never);
    }

    [Fact]
    public async Task ExecutarAsync_DeveAlertarEventoAmanha()
    {
        var (ctx, tenantId, responsavelId) = await SeedAsync();
        var hoje = BrasiliaTime.Hoje;
        var amanha = hoje.AddDays(1);
        ctx.Eventos.Add(new Evento
        {
            Id = Guid.NewGuid(), TenantId = tenantId, Titulo = "Audiência",
            Tipo = TipoEvento.Audiencia, DataHora = amanha.AddHours(14),
            ResponsavelId = responsavelId, CriadoEm = DateTime.UtcNow
        });
        await ctx.SaveChangesAsync();

        var mockEmail = new Mock<IEmailService>();
        var mockPrefs = new Mock<IPreferenciasNotificacaoService>();
        mockPrefs.Setup(p => p.PermiteEmailAsync(tenantId, responsavelId, "PrazoEvento")).ReturnsAsync(true);
        mockPrefs.Setup(p => p.PermiteInAppAsync(tenantId, responsavelId, "PrazoEvento")).ReturnsAsync(true);

        var job = new AlertasJob(ctx, mockEmail.Object, mockPrefs.Object, Mock.Of<ILogger<AlertasJob>>());
        await job.ExecutarAsync(hoje);

        // Eventos vão no mesmo resumo diário das tarefas (um e-mail por pessoa).
        mockEmail.Verify(e => e.EnviarResumoDiarioAsync(
            "resp@test.com", "Responsável",
            It.Is<IReadOnlyList<ResumoTarefaItem>>(l => l.Count == 0),
            It.Is<IReadOnlyList<ResumoEventoItem>>(itens => itens.Single().Titulo == "Audiência" && itens.Single().Dias == 1), It.IsAny<ResumoDiarioExtras?>()),
            Times.Once);

        // O alerta in-app do evento continua; o registro do e-mail já nasce lido.
        var notifs = await ctx.Notificacoes.ToListAsync();
        Assert.Contains(notifs, n => n.Titulo == "Evento amanhã" && !n.Lida);
        Assert.Contains(notifs, n => n.ChaveDedup!.StartsWith("digest-") && n.Lida);
    }

    [Fact]
    public async Task ResumoDiario_DeveJuntarTarefasEEventosNumUnicoEmail()
    {
        var (ctx, tenantId, responsavelId) = await SeedAsync();
        var hoje = BrasiliaTime.Hoje;
        ctx.Tarefas.Add(NovaTarefa(tenantId, responsavelId, "Recurso", hoje.AddDays(1)));
        ctx.Eventos.AddRange(
            new Evento
            {
                Id = Guid.NewGuid(), TenantId = tenantId, Titulo = "Reunião hoje",
                Tipo = TipoEvento.Reuniao, DataHora = hoje.AddHours(15),
                ResponsavelId = responsavelId, CriadoEm = DateTime.UtcNow
            },
            new Evento
            {
                Id = Guid.NewGuid(), TenantId = tenantId, Titulo = "Audiência amanhã",
                Tipo = TipoEvento.Audiencia, DataHora = hoje.AddDays(1).AddHours(10),
                ResponsavelId = responsavelId, CriadoEm = DateTime.UtcNow
            },
            new Evento
            {
                Id = Guid.NewGuid(), TenantId = tenantId, Titulo = "Audiência depois",
                Tipo = TipoEvento.Audiencia, DataHora = hoje.AddDays(2).AddHours(10),
                ResponsavelId = responsavelId, CriadoEm = DateTime.UtcNow
            });
        await ctx.SaveChangesAsync();

        var mockEmail = new Mock<IEmailService>();
        var job = new AlertasJob(ctx, mockEmail.Object, PrefAberto(tenantId, responsavelId).Object, Mock.Of<ILogger<AlertasJob>>());
        await job.ExecutarAsync(hoje);
        await job.ExecutarAsync(hoje);

        mockEmail.Verify(e => e.EnviarResumoDiarioAsync(
            "resp@test.com", "Responsável",
            It.Is<IReadOnlyList<ResumoTarefaItem>>(l => l.Single().Titulo == "Recurso"),
            It.Is<IReadOnlyList<ResumoEventoItem>>(l => l.Count == 3 &&
                l.Any(i => i.Titulo == "Reunião hoje" && i.Dias == 0) &&
                l.Any(i => i.Titulo == "Audiência amanhã" && i.Dias == 1) &&
                l.Any(i => i.Titulo == "Audiência depois" && i.Dias == 2)), It.IsAny<ResumoDiarioExtras?>()),
            Times.Once);
        mockEmail.VerifyNoOtherCalls();

        // Alerta in-app só na véspera do evento.
        var alertasEvento = await ctx.Notificacoes.Where(n => n.Titulo == "Evento amanhã").ToListAsync();
        Assert.Equal("\"Audiência amanhã\" amanhã às 10:00.", Assert.Single(alertasEvento).Mensagem);
    }

    [Fact]
    public async Task ResumoDiario_EventoSemPreferenciaDeEmail_NaoEntraNoEmail()
    {
        var (ctx, tenantId, responsavelId) = await SeedAsync();
        var hoje = BrasiliaTime.Hoje;
        ctx.Tarefas.Add(NovaTarefa(tenantId, responsavelId, "Recurso", hoje.AddDays(1)));
        ctx.Eventos.Add(new Evento
        {
            Id = Guid.NewGuid(), TenantId = tenantId, Titulo = "Audiência",
            Tipo = TipoEvento.Audiencia, DataHora = hoje.AddDays(1).AddHours(10),
            ResponsavelId = responsavelId, CriadoEm = DateTime.UtcNow
        });
        await ctx.SaveChangesAsync();

        var mockEmail = new Mock<IEmailService>();
        var prefs = Prefs(tenantId, responsavelId, ("PrazoTarefa", true, true), ("PrazoEvento", false, true));
        var job = new AlertasJob(ctx, mockEmail.Object, prefs.Object, Mock.Of<ILogger<AlertasJob>>());
        await job.ExecutarAsync(hoje);

        mockEmail.Verify(e => e.EnviarResumoDiarioAsync(
            It.IsAny<string>(), It.IsAny<string>(),
            It.Is<IReadOnlyList<ResumoTarefaItem>>(l => l.Single().Titulo == "Recurso"),
            It.Is<IReadOnlyList<ResumoEventoItem>>(l => l.Count == 0), It.IsAny<ResumoDiarioExtras?>()),
            Times.Once);
        Assert.True(await ctx.Notificacoes.AnyAsync(n => n.Titulo == "Evento amanhã"));
    }

    [Fact]
    public async Task ExecutarAsync_NaoDeveAlertarTarefaConcluida()
    {
        var (ctx, tenantId, responsavelId) = await SeedAsync();
        var hoje = BrasiliaTime.Hoje;
        ctx.Tarefas.Add(new Tarefa
        {
            Id = Guid.NewGuid(), TenantId = tenantId, Titulo = "Tarefa Concluida",
            Status = StatusTarefa.Concluida, Prioridade = PrioridadeTarefa.Alta,
            Prazo = hoje, ResponsavelId = responsavelId,
            CriadoEm = DateTime.UtcNow, CriadoPorId = responsavelId
        });
        await ctx.SaveChangesAsync();

        var mockEmail = new Mock<IEmailService>();
        var job = new AlertasJob(ctx, mockEmail.Object, PrefAberto(tenantId, responsavelId).Object, Mock.Of<ILogger<AlertasJob>>());
        await job.ExecutarAsync(hoje);

        mockEmail.Verify(e => e.EnviarResumoDiarioAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IReadOnlyList<ResumoTarefaItem>>(), It.IsAny<IReadOnlyList<ResumoEventoItem>>(), It.IsAny<ResumoDiarioExtras?>()),
            Times.Never);
    }

    [Fact]
    public async Task ExecutarAsync_DeveAlertarTrialExpirando()
    {
        var ctx = CreateContext();
        var tenantId = Guid.NewGuid();
        var adminId = Guid.NewGuid();
        var hoje = BrasiliaTime.Hoje;
        var expiraEm = hoje.AddDays(3);

        ctx.Tenants.Add(new Tenant
        {
            Id = tenantId, Nome = "Trial Tenant", Plano = PlanoTipo.Free,
            Status = StatusTenant.Trial, TrialExpiraEm = expiraEm,
            CriadoEm = DateTime.UtcNow
        });
        ctx.Users.Add(new Usuario
        {
            Id = adminId, TenantId = tenantId, Nome = "Admin",
            Email = "admin@test.com", UserName = "admin@test.com",
            Perfil = PerfilUsuario.Admin, Ativo = true, CriadoEm = DateTime.UtcNow
        });
        await ctx.SaveChangesAsync();

        var mockEmail = new Mock<IEmailService>();
        var mockPrefs = new Mock<IPreferenciasNotificacaoService>();
        mockPrefs.Setup(p => p.PermiteInAppAsync(tenantId, adminId, "TrialExpirando")).ReturnsAsync(true);

        var job = new AlertasJob(ctx, mockEmail.Object, mockPrefs.Object, Mock.Of<ILogger<AlertasJob>>());
        await job.ExecutarAsync(hoje);

        mockEmail.Verify(e => e.EnviarTrialExpirandoAsync("admin@test.com", "Trial Tenant", 3), Times.Once);
        var notif = await ctx.Notificacoes.FirstOrDefaultAsync();
        Assert.NotNull(notif);
        Assert.Equal(TipoNotificacao.TrialExpirando, notif.Tipo);
        var alerta = await ctx.Notificacoes.SingleAsync(n => n.Titulo == "Trial expira em 3 dia(s)");
        Assert.Equal("/pages/assinatura.html", alerta.Url);
    }

    private static async Task<(AppDbContext ctx, Guid tenantId, Guid adminId)> SeedPromoAsync(
        AppDbContext ctx, StatusTenant status, DateTime planoExpiraEm, string? stripeSubscriptionId = null)
    {
        var tenantId = Guid.NewGuid();
        var adminId = Guid.NewGuid();
        ctx.Tenants.Add(new Tenant
        {
            Id = tenantId, Nome = "Promo Tenant", Plano = PlanoTipo.Plus,
            Status = status, PlanoExpiraEm = planoExpiraEm,
            StripeSubscriptionId = stripeSubscriptionId, CriadoEm = DateTime.UtcNow
        });
        ctx.Users.Add(new Usuario
        {
            Id = adminId, TenantId = tenantId, Nome = "Admin",
            Email = "admin@promo.com", UserName = "admin@promo.com",
            Perfil = PerfilUsuario.Admin, Ativo = true, CriadoEm = DateTime.UtcNow
        });
        await ctx.SaveChangesAsync();
        return (ctx, tenantId, adminId);
    }

    [Theory]
    [InlineData(7)]
    [InlineData(3)]
    [InlineData(1)]
    public async Task ExecutarAsync_DeveAlertarPlanoPromocionalExpirando(int dias)
    {
        var hoje = BrasiliaTime.Hoje;
        // 17h UTC = 14h em Brasília, mesmo dia de calendário.
        var expiraEm = DateTime.SpecifyKind(hoje.AddDays(dias).AddHours(17), DateTimeKind.Utc);
        var (ctx, tenantId, adminId) = await SeedPromoAsync(CreateContext(), StatusTenant.Ativo, expiraEm);

        var mockEmail = new Mock<IEmailService>();
        var job = new AlertasJob(ctx, mockEmail.Object, PrefAberto(tenantId, adminId).Object, Mock.Of<ILogger<AlertasJob>>());
        await job.ExecutarAsync(hoje);
        await job.ExecutarAsync(hoje); // não reenvia no mesmo dia

        mockEmail.Verify(e => e.EnviarPlanoPromocionalExpirandoAsync(
            "admin@promo.com", "Promo Tenant", PlanoTipo.Plus, expiraEm, dias, It.IsAny<CancellationToken>()), Times.Once);
        mockEmail.Verify(e => e.EnviarTrialExpirandoAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.Contains(await ctx.Notificacoes.ToListAsync(),
            n => n.Tipo == TipoNotificacao.TrialExpirando && n.Titulo == $"Plano Plus expira em {dias} dia(s)");
    }

    [Fact]
    public async Task ExecutarAsync_NaoAlertaPlanoPromocional_ForaDosLimites()
    {
        var hoje = BrasiliaTime.Hoje;
        var expiraEm = DateTime.SpecifyKind(hoje.AddDays(30).AddHours(17), DateTimeKind.Utc);
        var (ctx, tenantId, adminId) = await SeedPromoAsync(CreateContext(), StatusTenant.Ativo, expiraEm);

        var mockEmail = new Mock<IEmailService>();
        var job = new AlertasJob(ctx, mockEmail.Object, PrefAberto(tenantId, adminId).Object, Mock.Of<ILogger<AlertasJob>>());
        await job.ExecutarAsync(hoje);

        mockEmail.Verify(e => e.EnviarPlanoPromocionalExpirandoAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<PlanoTipo>(), It.IsAny<DateTime>(), It.IsAny<int>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(StatusTenant.Ativo, "sub_123")]   // assinatura paga renova sozinha
    [InlineData(StatusTenant.Cancelado, null)]   // assinatura cancelada também usa PlanoExpiraEm
    public async Task ExecutarAsync_NaoAlertaPlanoPromocional_ParaAssinaturaPaga(StatusTenant status, string? stripeId)
    {
        var hoje = BrasiliaTime.Hoje;
        var expiraEm = DateTime.SpecifyKind(hoje.AddDays(3).AddHours(17), DateTimeKind.Utc);
        var (ctx, tenantId, adminId) = await SeedPromoAsync(CreateContext(), status, expiraEm, stripeId);

        var mockEmail = new Mock<IEmailService>();
        var job = new AlertasJob(ctx, mockEmail.Object, PrefAberto(tenantId, adminId).Object, Mock.Of<ILogger<AlertasJob>>());
        await job.ExecutarAsync(hoje);

        mockEmail.Verify(e => e.EnviarPlanoPromocionalExpirandoAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<PlanoTipo>(), It.IsAny<DateTime>(), It.IsAny<int>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecutarAsync_DeveIncluirPrazoDeTarefaNoResumoUnico()
    {
        var (ctx, tenantId, responsavelId) = await SeedAsync();
        var hoje = BrasiliaTime.Hoje;
        var amanha = hoje.AddDays(1);
        var processoId = Guid.NewGuid();

        ctx.Processos.Add(new Processo
        {
            Id = processoId, TenantId = tenantId, NumeroCNJ = "0000001-00.2024.8.26.0001",
            AreaDireito = AreaDireito.Civil, Fase = FaseProcessual.Conhecimento,
            Status = StatusProcesso.Ativo, CriadoEm = DateTime.UtcNow
        });
        ctx.Tarefas.Add(new Tarefa
        {
            Id = Guid.NewGuid(), TenantId = tenantId, ProcessoId = processoId,
            Titulo = "Prazo para contestação",
            DataInicio = hoje.AddDays(-5),
            QuantidadeDias = 5, TipoCalculo = TipoCalculo.DiasCorridos,
            Prazo = amanha, Status = StatusTarefa.Pendente,
            Tipo = TipoTarefa.Prazo,
            ResponsavelId = responsavelId, CriadoEm = DateTime.UtcNow,
            CriadoPorId = responsavelId, Prioridade = PrioridadeTarefa.Alta
        });
        await ctx.SaveChangesAsync();

        var mockEmail = new Mock<IEmailService>();
        var job = new AlertasJob(ctx, mockEmail.Object, PrefAberto(tenantId, responsavelId).Object, Mock.Of<ILogger<AlertasJob>>());
        await job.ExecutarAsync(hoje);

        mockEmail.Verify(e => e.EnviarResumoDiarioAsync(
            "resp@test.com", "Responsável",
            It.Is<IReadOnlyList<ResumoTarefaItem>>(l =>
                l.Count == 1 && l[0].Titulo == "Prazo para contestação" && l[0].Dias == 1), It.IsAny<IReadOnlyList<ResumoEventoItem>>(), It.IsAny<ResumoDiarioExtras?>()),
            Times.Once);
    }

    [Fact]
    public async Task ExecutarAsync_DeveJuntarOsPrazosNoMesmoEmail()
    {
        // Dois prazos do mesmo responsável chegam no MESMO e-mail, não em dois separados.
        var (ctx, tenantId, responsavelId) = await SeedAsync();
        var hoje = BrasiliaTime.Hoje;
        var amanha = hoje.AddDays(1);

        ctx.Tarefas.Add(new Tarefa
        {
            Id = Guid.NewGuid(), TenantId = tenantId, Titulo = "Prazo para contestação",
            Prazo = amanha, Status = StatusTarefa.Pendente, Tipo = TipoTarefa.Prazo,
            ResponsavelId = responsavelId, CriadoEm = DateTime.UtcNow, CriadoPorId = responsavelId,
            Prioridade = PrioridadeTarefa.Alta
        });
        ctx.Tarefas.Add(new Tarefa
        {
            Id = Guid.NewGuid(), TenantId = tenantId, Titulo = "Inscrição prova OAB",
            Prazo = amanha, Status = StatusTarefa.Pendente, Tipo = TipoTarefa.Prazo,
            ResponsavelId = responsavelId, CriadoEm = DateTime.UtcNow, CriadoPorId = responsavelId,
            Prioridade = PrioridadeTarefa.Media
        });
        await ctx.SaveChangesAsync();

        var mockEmail = new Mock<IEmailService>();
        var job = new AlertasJob(ctx, mockEmail.Object, PrefAberto(tenantId, responsavelId).Object, Mock.Of<ILogger<AlertasJob>>());
        await job.ExecutarAsync(hoje);

        mockEmail.Verify(e => e.EnviarResumoDiarioAsync(
            "resp@test.com", "Responsável",
            It.Is<IReadOnlyList<ResumoTarefaItem>>(l => l.Count == 2
                && l.Any(i => i.Titulo == "Prazo para contestação")
                && l.Any(i => i.Titulo == "Inscrição prova OAB")), It.IsAny<IReadOnlyList<ResumoEventoItem>>(), It.IsAny<ResumoDiarioExtras?>()),
            Times.Once);
    }

    [Fact]
    public async Task ExecutarAsync_DeveIncluirPrazoQueVenceHoje()
    {
        // O prazo também precisa aparecer no dia em que vence, não só nos
        // avisos D-1/D-3/D-5 de antecedência.
        var (ctx, tenantId, responsavelId) = await SeedAsync();
        var hoje = BrasiliaTime.Hoje;

        ctx.Tarefas.Add(new Tarefa
        {
            Id = Guid.NewGuid(), TenantId = tenantId, Titulo = "Inscrição prova OAB",
            Prazo = hoje.AddHours(17), Status = StatusTarefa.Pendente, Tipo = TipoTarefa.Prazo,
            ResponsavelId = responsavelId, CriadoEm = DateTime.UtcNow, CriadoPorId = responsavelId,
            Prioridade = PrioridadeTarefa.Media
        });
        await ctx.SaveChangesAsync();

        var mockEmail = new Mock<IEmailService>();
        var job = new AlertasJob(ctx, mockEmail.Object, PrefAberto(tenantId, responsavelId).Object, Mock.Of<ILogger<AlertasJob>>());
        await job.ExecutarAsync(hoje);

        mockEmail.Verify(e => e.EnviarResumoDiarioAsync(
            "resp@test.com", "Responsável",
            It.Is<IReadOnlyList<ResumoTarefaItem>>(l =>
                l.Count == 1 && l[0].Titulo == "Inscrição prova OAB" && l[0].Dias == 0), It.IsAny<IReadOnlyList<ResumoEventoItem>>(), It.IsAny<ResumoDiarioExtras?>()),
            Times.Once);
    }

    [Fact]
    public async Task ExecutarAsync_NaoDeveIncluirPrazo_QuandoPreferenciaDesabilitada()
    {
        var (ctx, tenantId, responsavelId) = await SeedAsync();
        var hoje = BrasiliaTime.Hoje;

        ctx.Tarefas.Add(new Tarefa
        {
            Id = Guid.NewGuid(), TenantId = tenantId, Titulo = "Inscrição prova OAB",
            Prazo = hoje.AddDays(1), Status = StatusTarefa.Pendente, Tipo = TipoTarefa.Prazo,
            ResponsavelId = responsavelId, CriadoEm = DateTime.UtcNow, CriadoPorId = responsavelId,
            Prioridade = PrioridadeTarefa.Media
        });
        await ctx.SaveChangesAsync();

        var mockEmail = new Mock<IEmailService>();
        var mockPrefs = new Mock<IPreferenciasNotificacaoService>();
        mockPrefs.Setup(p => p.PermiteEmailAsync(tenantId, responsavelId, "Prazos")).ReturnsAsync(false);
        mockPrefs.Setup(p => p.PermiteInAppAsync(tenantId, responsavelId, "Prazos")).ReturnsAsync(false);

        var job = new AlertasJob(ctx, mockEmail.Object, mockPrefs.Object, Mock.Of<ILogger<AlertasJob>>());
        await job.ExecutarAsync(hoje);

        mockEmail.Verify(e => e.EnviarResumoDiarioAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IReadOnlyList<ResumoTarefaItem>>(), It.IsAny<IReadOnlyList<ResumoEventoItem>>(), It.IsAny<ResumoDiarioExtras?>()),
            Times.Never);
    }

    [Fact]
    public async Task ExecutarAsync_NaoDeveFalhar_QuandoNenhumDadoExistente()
    {
        var (ctx, _, _) = await SeedAsync();

        var mockEmail = new Mock<IEmailService>();
        var job = new AlertasJob(ctx, mockEmail.Object, new Mock<IPreferenciasNotificacaoService>().Object, Mock.Of<ILogger<AlertasJob>>());
        await job.ExecutarAsync();

        mockEmail.Verify(e => e.EnviarResumoDiarioAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IReadOnlyList<ResumoTarefaItem>>(), It.IsAny<IReadOnlyList<ResumoEventoItem>>(), It.IsAny<ResumoDiarioExtras?>()),
            Times.Never);
    }

    [Fact]
    public async Task ExecutarAsync_DeveConsolidarTodasTarefasEmUmaUnicaChamadaDeEmail()
    {
        var (ctx, tenantId, responsavelId) = await SeedAsync();
        var hoje = BrasiliaTime.Hoje;
        ctx.Tarefas.AddRange(
            new Tarefa { Id = Guid.NewGuid(), TenantId = tenantId, Titulo = "Vence Hoje", Status = StatusTarefa.Pendente, Prazo = hoje, ResponsavelId = responsavelId, CriadoEm = DateTime.UtcNow, CriadoPorId = responsavelId },
            new Tarefa { Id = Guid.NewGuid(), TenantId = tenantId, Titulo = "Vence Amanha", Status = StatusTarefa.Pendente, Prazo = hoje.AddDays(1), ResponsavelId = responsavelId, CriadoEm = DateTime.UtcNow, CriadoPorId = responsavelId },
            new Tarefa { Id = Guid.NewGuid(), TenantId = tenantId, Titulo = "Vence em 3d", Status = StatusTarefa.Pendente, Prazo = hoje.AddDays(3), ResponsavelId = responsavelId, CriadoEm = DateTime.UtcNow, CriadoPorId = responsavelId }
        );
        await ctx.SaveChangesAsync();

        var mockEmail = new Mock<IEmailService>();
        var job = new AlertasJob(ctx, mockEmail.Object, PrefAberto(tenantId, responsavelId).Object, Mock.Of<ILogger<AlertasJob>>());
        await job.ExecutarAsync(hoje);

        mockEmail.Verify(e => e.EnviarResumoDiarioAsync(
            "resp@test.com", "Responsável",
            It.Is<IReadOnlyList<ResumoTarefaItem>>(l => l.Count == 3), It.IsAny<IReadOnlyList<ResumoEventoItem>>(), It.IsAny<ResumoDiarioExtras?>()),
            Times.Once);
    }

    [Fact]
    public async Task ExecutarAsync_NaoDeveEnviarEmailDuplicado_QuandoJobRodaDuasVezes()
    {
        var (ctx, tenantId, responsavelId) = await SeedAsync();
        var hoje = BrasiliaTime.Hoje;
        ctx.Tarefas.Add(new Tarefa
        {
            Id = Guid.NewGuid(), TenantId = tenantId, Titulo = "Tarefa Deduplicada",
            Status = StatusTarefa.Pendente, Prioridade = PrioridadeTarefa.Alta,
            Prazo = hoje.AddDays(5), ResponsavelId = responsavelId,
            CriadoEm = DateTime.UtcNow, CriadoPorId = responsavelId
        });
        await ctx.SaveChangesAsync();

        var mockEmail = new Mock<IEmailService>();
        var job = new AlertasJob(ctx, mockEmail.Object, PrefAberto(tenantId, responsavelId).Object, Mock.Of<ILogger<AlertasJob>>());

        await job.ExecutarAsync(hoje);
        await job.ExecutarAsync(hoje);

        mockEmail.Verify(e => e.EnviarResumoDiarioAsync(
            "resp@test.com", "Responsável", It.IsAny<IReadOnlyList<ResumoTarefaItem>>(), It.IsAny<IReadOnlyList<ResumoEventoItem>>(), It.IsAny<ResumoDiarioExtras?>()),
            Times.Once);
    }

    [Fact]
    public async Task ExecutarAsync_NaoDeveEnviarEmailDuplicado_Evento_QuandoJobRodaDuasVezes()
    {
        var (ctx, tenantId, responsavelId) = await SeedAsync();
        var hoje = BrasiliaTime.Hoje;
        var amanha = hoje.AddDays(1);
        ctx.Eventos.Add(new Evento
        {
            Id = Guid.NewGuid(), TenantId = tenantId, Titulo = "Audiência",
            Tipo = TipoEvento.Audiencia, DataHora = amanha.AddHours(14),
            ResponsavelId = responsavelId, CriadoEm = DateTime.UtcNow
        });
        await ctx.SaveChangesAsync();

        var mockEmail = new Mock<IEmailService>();
        var mockPrefs = new Mock<IPreferenciasNotificacaoService>();
        mockPrefs.Setup(p => p.PermiteEmailAsync(tenantId, responsavelId, "PrazoEvento")).ReturnsAsync(true);
        mockPrefs.Setup(p => p.PermiteInAppAsync(tenantId, responsavelId, "PrazoEvento")).ReturnsAsync(true);

        var job = new AlertasJob(ctx, mockEmail.Object, mockPrefs.Object, Mock.Of<ILogger<AlertasJob>>());

        await job.ExecutarAsync(hoje);
        await job.ExecutarAsync(hoje);

        mockEmail.Verify(e => e.EnviarResumoDiarioAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IReadOnlyList<ResumoTarefaItem>>(),
            It.Is<IReadOnlyList<ResumoEventoItem>>(itens => itens.Any(i => i.Titulo == "Audiência")), It.IsAny<ResumoDiarioExtras?>()), Times.Once);
    }

    [Fact]
    public async Task ExecutarAsync_NaoDeveEnviarEmailDuplicado_Trial_QuandoJobRodaDuasVezes()
    {
        var ctx = CreateContext();
        var tenantId = Guid.NewGuid();
        var adminId = Guid.NewGuid();
        var hoje = BrasiliaTime.Hoje;
        var expiraEm = hoje.AddDays(3);

        ctx.Tenants.Add(new Tenant
        {
            Id = tenantId, Nome = "Trial Tenant", Plano = PlanoTipo.Free,
            Status = StatusTenant.Trial, TrialExpiraEm = expiraEm,
            CriadoEm = DateTime.UtcNow
        });
        ctx.Users.Add(new Usuario
        {
            Id = adminId, TenantId = tenantId, Nome = "Admin",
            Email = "admin@test.com", UserName = "admin@test.com",
            Perfil = PerfilUsuario.Admin, Ativo = true, CriadoEm = DateTime.UtcNow
        });
        await ctx.SaveChangesAsync();

        var mockEmail = new Mock<IEmailService>();
        var mockPrefs = new Mock<IPreferenciasNotificacaoService>();

        var job = new AlertasJob(ctx, mockEmail.Object, mockPrefs.Object, Mock.Of<ILogger<AlertasJob>>());

        await job.ExecutarAsync(hoje);
        await job.ExecutarAsync(hoje);

        mockEmail.Verify(e => e.EnviarTrialExpirandoAsync(
            "admin@test.com", "Trial Tenant", 3), Times.Once);
    }

    [Fact]
    public async Task ExecutarAsync_NaoDeveEnviarEmailDuplicado_Prazo_QuandoJobRodaDuasVezes()
    {
        var (ctx, tenantId, responsavelId) = await SeedAsync();
        var hoje = BrasiliaTime.Hoje;

        ctx.Tarefas.Add(new Tarefa
        {
            Id = Guid.NewGuid(), TenantId = tenantId, Titulo = "Inscrição prova OAB",
            Prazo = hoje.AddDays(1), Status = StatusTarefa.Pendente, Tipo = TipoTarefa.Prazo,
            ResponsavelId = responsavelId, CriadoEm = DateTime.UtcNow, CriadoPorId = responsavelId,
            Prioridade = PrioridadeTarefa.Media
        });
        await ctx.SaveChangesAsync();

        var mockEmail = new Mock<IEmailService>();
        var job = new AlertasJob(ctx, mockEmail.Object, PrefAberto(tenantId, responsavelId).Object, Mock.Of<ILogger<AlertasJob>>());

        await job.ExecutarAsync(hoje);
        await job.ExecutarAsync(hoje);

        mockEmail.Verify(e => e.EnviarResumoDiarioAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IReadOnlyList<ResumoTarefaItem>>(), It.IsAny<IReadOnlyList<ResumoEventoItem>>(), It.IsAny<ResumoDiarioExtras?>()), Times.Once);
    }

    [Fact]
    public async Task ExecutarAsync_DeveAlertarCriadorQuandoResponsavelIdNulo()
    {
        var (ctx, tenantId, criadorId) = await SeedAsync();
        var hoje = BrasiliaTime.Hoje;
        ctx.Tarefas.Add(new Tarefa
        {
            Id = Guid.NewGuid(), TenantId = tenantId, Titulo = "Tarefa sem responsavel",
            Status = StatusTarefa.Pendente, Prioridade = PrioridadeTarefa.Media,
            Prazo = hoje, ResponsavelId = null,
            CriadoEm = DateTime.UtcNow, CriadoPorId = criadorId
        });
        await ctx.SaveChangesAsync();

        var mockEmail = new Mock<IEmailService>();
        var job = new AlertasJob(ctx, mockEmail.Object, PrefAberto(tenantId, criadorId).Object, Mock.Of<ILogger<AlertasJob>>());
        await job.ExecutarAsync(hoje);

        mockEmail.Verify(e => e.EnviarResumoDiarioAsync(
            "resp@test.com", "Responsável",
            It.Is<IReadOnlyList<ResumoTarefaItem>>(l => l.Count == 1 && l[0].Titulo == "Tarefa sem responsavel"), It.IsAny<IReadOnlyList<ResumoEventoItem>>(), It.IsAny<ResumoDiarioExtras?>()),
            Times.Once);
    }

    [Fact]
    public async Task ExecutarAsync_DeveIncluirTarefaAtrasadaNoResumo()
    {
        var (ctx, tenantId, responsavelId) = await SeedAsync();
        var hoje = BrasiliaTime.Hoje;
        var prazo = hoje.AddDays(-3);
        ctx.Tarefas.Add(new Tarefa
        {
            Id = Guid.NewGuid(), TenantId = tenantId, Titulo = "Tarefa Atrasada",
            Status = StatusTarefa.Pendente, Prioridade = PrioridadeTarefa.Alta,
            Prazo = prazo, ResponsavelId = responsavelId,
            CriadoEm = DateTime.UtcNow, CriadoPorId = responsavelId
        });
        await ctx.SaveChangesAsync();

        var mockEmail = new Mock<IEmailService>();
        var job = new AlertasJob(ctx, mockEmail.Object, PrefAberto(tenantId, responsavelId).Object, Mock.Of<ILogger<AlertasJob>>());
        await job.ExecutarAsync(hoje);

        mockEmail.Verify(e => e.EnviarResumoDiarioAsync(
            "resp@test.com", "Responsável",
            It.Is<IReadOnlyList<ResumoTarefaItem>>(l =>
                l.Count == 1 && l[0].Dias == -3 && l[0].Titulo == "Tarefa Atrasada"), It.IsAny<IReadOnlyList<ResumoEventoItem>>(), It.IsAny<ResumoDiarioExtras?>()),
            Times.Once);

        var notif = await ctx.Notificacoes.FirstOrDefaultAsync(n =>
            n.Tipo == TipoNotificacao.PrazoTarefa && n.UsuarioId == responsavelId);
        Assert.NotNull(notif);
        Assert.Contains("atrasada", notif.Titulo, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExecutarAsync_NaoDeveNotificarTarefaAtrasadaApos5Dias()
    {
        var (ctx, tenantId, responsavelId) = await SeedAsync();
        var hoje = BrasiliaTime.Hoje;
        ctx.Tarefas.AddRange(
            new Tarefa { Id = Guid.NewGuid(), TenantId = tenantId, Titulo = "Limite (5d atrasada)", Status = StatusTarefa.Pendente, Prazo = hoje.AddDays(-5), ResponsavelId = responsavelId, CriadoEm = DateTime.UtcNow, CriadoPorId = responsavelId },
            new Tarefa { Id = Guid.NewGuid(), TenantId = tenantId, Titulo = "Muito atrasada (6d)", Status = StatusTarefa.Pendente, Prazo = hoje.AddDays(-6), ResponsavelId = responsavelId, CriadoEm = DateTime.UtcNow, CriadoPorId = responsavelId },
            new Tarefa { Id = Guid.NewGuid(), TenantId = tenantId, Titulo = "Muito atrasada (20d)", Status = StatusTarefa.Pendente, Prazo = hoje.AddDays(-20), ResponsavelId = responsavelId, CriadoEm = DateTime.UtcNow, CriadoPorId = responsavelId }
        );
        await ctx.SaveChangesAsync();

        var mockEmail = new Mock<IEmailService>();
        var job = new AlertasJob(ctx, mockEmail.Object, PrefAberto(tenantId, responsavelId).Object, Mock.Of<ILogger<AlertasJob>>());
        await job.ExecutarAsync(hoje);

        mockEmail.Verify(e => e.EnviarResumoDiarioAsync(
            "resp@test.com", "Responsável",
            It.Is<IReadOnlyList<ResumoTarefaItem>>(l =>
                l.Count == 1 &&
                l[0].Titulo == "Limite (5d atrasada)" &&
                l[0].Dias == -5), It.IsAny<IReadOnlyList<ResumoEventoItem>>(), It.IsAny<ResumoDiarioExtras?>()),
            Times.Once);

        var notifs = await ctx.Notificacoes
            .Where(n => n.Tipo == TipoNotificacao.PrazoTarefa && n.UsuarioId == responsavelId)
            .ToListAsync();
        Assert.Single(notifs);
        Assert.DoesNotContain("Muito atrasada", notifs[0].Mensagem);
    }

    [Fact]
    public async Task ExecutarAsync_NaoDeveIncluirTarefaAtrasada_QuandoConcluida()
    {
        var (ctx, tenantId, responsavelId) = await SeedAsync();
        var hoje = BrasiliaTime.Hoje;
        ctx.Tarefas.Add(new Tarefa
        {
            Id = Guid.NewGuid(), TenantId = tenantId, Titulo = "Concluida antes",
            Status = StatusTarefa.Concluida, Prioridade = PrioridadeTarefa.Media,
            Prazo = hoje.AddDays(-3), ResponsavelId = responsavelId,
            ConcluidaEm = hoje.AddDays(-1),
            CriadoEm = DateTime.UtcNow, CriadoPorId = responsavelId
        });
        await ctx.SaveChangesAsync();

        var mockEmail = new Mock<IEmailService>();
        var job = new AlertasJob(ctx, mockEmail.Object, PrefAberto(tenantId, responsavelId).Object, Mock.Of<ILogger<AlertasJob>>());
        await job.ExecutarAsync(hoje);

        mockEmail.Verify(e => e.EnviarResumoDiarioAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IReadOnlyList<ResumoTarefaItem>>(), It.IsAny<IReadOnlyList<ResumoEventoItem>>(), It.IsAny<ResumoDiarioExtras?>()),
            Times.Never);
    }

    [Fact]
    public async Task ExecutarAsync_NaoDeveIncluirTarefaAtrasada_QuandoPerdida()
    {
        var (ctx, tenantId, responsavelId) = await SeedAsync();
        var hoje = BrasiliaTime.Hoje;
        ctx.Tarefas.Add(new Tarefa
        {
            Id = Guid.NewGuid(), TenantId = tenantId, Titulo = "Perdida",
            Status = StatusTarefa.Perdida, Prioridade = PrioridadeTarefa.Media,
            Prazo = hoje.AddDays(-3), ResponsavelId = responsavelId,
            CriadoEm = DateTime.UtcNow, CriadoPorId = responsavelId
        });
        await ctx.SaveChangesAsync();

        var mockEmail = new Mock<IEmailService>();
        var job = new AlertasJob(ctx, mockEmail.Object, PrefAberto(tenantId, responsavelId).Object, Mock.Of<ILogger<AlertasJob>>());
        await job.ExecutarAsync(hoje);

        mockEmail.Verify(e => e.EnviarResumoDiarioAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IReadOnlyList<ResumoTarefaItem>>(), It.IsAny<IReadOnlyList<ResumoEventoItem>>(), It.IsAny<ResumoDiarioExtras?>()),
            Times.Never);
    }

    [Fact]
    public async Task ExecutarAsync_TarefaAtrasada_NaoDeveReenviarEmailNoMesmoDia()
    {
        var (ctx, tenantId, responsavelId) = await SeedAsync();
        var hoje = BrasiliaTime.Hoje;
        var prazo = hoje.AddDays(-2);
        ctx.Tarefas.Add(new Tarefa
        {
            Id = Guid.NewGuid(), TenantId = tenantId, Titulo = "Atrasada dedup",
            Status = StatusTarefa.Pendente, Prioridade = PrioridadeTarefa.Media,
            Prazo = prazo, ResponsavelId = responsavelId,
            CriadoEm = DateTime.UtcNow, CriadoPorId = responsavelId
        });
        await ctx.SaveChangesAsync();

        var mockEmail = new Mock<IEmailService>();
        var job = new AlertasJob(ctx, mockEmail.Object, PrefAberto(tenantId, responsavelId).Object, Mock.Of<ILogger<AlertasJob>>());
        await job.ExecutarAsync(hoje);
        await job.ExecutarAsync(hoje);

        mockEmail.Verify(e => e.EnviarResumoDiarioAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IReadOnlyList<ResumoTarefaItem>>(), It.IsAny<IReadOnlyList<ResumoEventoItem>>(), It.IsAny<ResumoDiarioExtras?>()),
            Times.Once);
    }

    [Fact]
    public async Task ExecutarAsync_Resumo_DeveAgruparAtrasadasEPendentesNoMesmoEmail()
    {
        var (ctx, tenantId, responsavelId) = await SeedAsync();
        var hoje = BrasiliaTime.Hoje;
        ctx.Tarefas.AddRange(
            new Tarefa { Id = Guid.NewGuid(), TenantId = tenantId, Titulo = "Atrasada 2d", Status = StatusTarefa.Pendente, Prazo = hoje.AddDays(-2), ResponsavelId = responsavelId, CriadoEm = DateTime.UtcNow, CriadoPorId = responsavelId },
            new Tarefa { Id = Guid.NewGuid(), TenantId = tenantId, Titulo = "Vence Hoje", Status = StatusTarefa.Pendente, Prazo = hoje, ResponsavelId = responsavelId, CriadoEm = DateTime.UtcNow, CriadoPorId = responsavelId },
            new Tarefa { Id = Guid.NewGuid(), TenantId = tenantId, Titulo = "Vence Amanha", Status = StatusTarefa.Pendente, Prazo = hoje.AddDays(1), ResponsavelId = responsavelId, CriadoEm = DateTime.UtcNow, CriadoPorId = responsavelId },
            new Tarefa { Id = Guid.NewGuid(), TenantId = tenantId, Titulo = "Vence em 3d", Status = StatusTarefa.Pendente, Prazo = hoje.AddDays(3), ResponsavelId = responsavelId, CriadoEm = DateTime.UtcNow, CriadoPorId = responsavelId },
            new Tarefa { Id = Guid.NewGuid(), TenantId = tenantId, Titulo = "Vence em 5d", Status = StatusTarefa.Pendente, Prazo = hoje.AddDays(5), ResponsavelId = responsavelId, CriadoEm = DateTime.UtcNow, CriadoPorId = responsavelId },
            new Tarefa { Id = Guid.NewGuid(), TenantId = tenantId, Titulo = "Longe (fora)", Status = StatusTarefa.Pendente, Prazo = hoje.AddDays(20), ResponsavelId = responsavelId, CriadoEm = DateTime.UtcNow, CriadoPorId = responsavelId }
        );
        await ctx.SaveChangesAsync();

        var mockEmail = new Mock<IEmailService>();
        var job = new AlertasJob(ctx, mockEmail.Object, PrefAberto(tenantId, responsavelId).Object, Mock.Of<ILogger<AlertasJob>>());
        await job.ExecutarAsync(hoje);

        mockEmail.Verify(e => e.EnviarResumoDiarioAsync(
            "resp@test.com", "Responsável",
            It.Is<IReadOnlyList<ResumoTarefaItem>>(l =>
                l.Count == 5 &&
                l.Any(i => i.Titulo == "Atrasada 2d" && i.Dias == -2) &&
                l.Any(i => i.Titulo == "Vence Hoje" && i.Dias == 0) &&
                l.Any(i => i.Titulo == "Vence Amanha" && i.Dias == 1) &&
                l.Any(i => i.Titulo == "Vence em 3d" && i.Dias == 3) &&
                l.Any(i => i.Titulo == "Vence em 5d" && i.Dias == 5) &&
                !l.Any(i => i.Titulo == "Longe (fora)")), It.IsAny<IReadOnlyList<ResumoEventoItem>>(), It.IsAny<ResumoDiarioExtras?>()),
            Times.Once);
    }

    [Fact]
    public async Task ExecutarAsync_Resumo_DeveGerarApenasUmaNotificacaoInAppPorUsuarioPorDia()
    {
        var (ctx, tenantId, responsavelId) = await SeedAsync();
        var hoje = BrasiliaTime.Hoje;
        ctx.Tarefas.AddRange(
            new Tarefa { Id = Guid.NewGuid(), TenantId = tenantId, Titulo = "T1", Status = StatusTarefa.Pendente, Prazo = hoje, ResponsavelId = responsavelId, CriadoEm = DateTime.UtcNow, CriadoPorId = responsavelId },
            new Tarefa { Id = Guid.NewGuid(), TenantId = tenantId, Titulo = "T2", Status = StatusTarefa.Pendente, Prazo = hoje.AddDays(1), ResponsavelId = responsavelId, CriadoEm = DateTime.UtcNow, CriadoPorId = responsavelId },
            new Tarefa { Id = Guid.NewGuid(), TenantId = tenantId, Titulo = "T3", Status = StatusTarefa.Pendente, Prazo = hoje.AddDays(-2), ResponsavelId = responsavelId, CriadoEm = DateTime.UtcNow, CriadoPorId = responsavelId }
        );
        await ctx.SaveChangesAsync();

        var mockEmail = new Mock<IEmailService>();
        var job = new AlertasJob(ctx, mockEmail.Object, PrefAberto(tenantId, responsavelId).Object, Mock.Of<ILogger<AlertasJob>>());
        await job.ExecutarAsync(hoje);

        var notifs = await ctx.Notificacoes
            .Where(n => n.Tipo == TipoNotificacao.PrazoTarefa && n.UsuarioId == responsavelId)
            .ToListAsync();
        Assert.Single(notifs);
        Assert.Contains("3 tarefa", notifs[0].Titulo);
    }

    private static Tarefa NovaTarefa(Guid tenantId, Guid responsavelId, string titulo, DateTime prazo,
        TipoTarefa tipo = TipoTarefa.Tarefa) => new()
    {
        Id = Guid.NewGuid(), TenantId = tenantId, Titulo = titulo,
        Prazo = prazo, Status = StatusTarefa.Pendente, Tipo = tipo,
        ResponsavelId = responsavelId, CriadoPorId = responsavelId,
        CriadoEm = DateTime.UtcNow, Prioridade = PrioridadeTarefa.Media
    };

    private static Mock<IPreferenciasNotificacaoService> Prefs(Guid tenantId, Guid userId,
        params (string Categoria, bool Email, bool InApp)[] categorias)
    {
        var mock = new Mock<IPreferenciasNotificacaoService>();
        mock.Setup(p => p.PermiteEmailAsync(tenantId, userId, It.IsAny<string>())).ReturnsAsync(false);
        mock.Setup(p => p.PermiteInAppAsync(tenantId, userId, It.IsAny<string>())).ReturnsAsync(false);
        foreach (var (categoria, email, inApp) in categorias)
        {
            mock.Setup(p => p.PermiteEmailAsync(tenantId, userId, categoria)).ReturnsAsync(email);
            mock.Setup(p => p.PermiteInAppAsync(tenantId, userId, categoria)).ReturnsAsync(inApp);
        }
        return mock;
    }

    [Fact]
    public async Task Resumo_NaoDeveIncluirTarefa_QuandoSoPrazosPermiteEmail()
    {
        var (ctx, tenantId, responsavelId) = await SeedAsync();
        var hoje = BrasiliaTime.Hoje;
        ctx.Tarefas.Add(NovaTarefa(tenantId, responsavelId, "Ligar cliente", hoje.AddDays(1)));
        ctx.Tarefas.Add(new Tarefa
        {
            Id = Guid.NewGuid(), TenantId = tenantId, Titulo = "Inscrição prova OAB",
            Prazo = hoje.AddDays(1), Status = StatusTarefa.Pendente, Tipo = TipoTarefa.Prazo,
            ResponsavelId = responsavelId, CriadoEm = DateTime.UtcNow, CriadoPorId = responsavelId,
            Prioridade = PrioridadeTarefa.Media
        });
        await ctx.SaveChangesAsync();

        var mockEmail = new Mock<IEmailService>();
        var prefs = Prefs(tenantId, responsavelId, ("Prazos", true, true), ("PrazoTarefa", false, false));
        var job = new AlertasJob(ctx, mockEmail.Object, prefs.Object, Mock.Of<ILogger<AlertasJob>>());
        await job.ExecutarAsync(hoje);

        mockEmail.Verify(e => e.EnviarResumoDiarioAsync(
            "resp@test.com", "Responsável",
            It.Is<IReadOnlyList<ResumoTarefaItem>>(l => l.Count == 1 && l[0].Titulo == "Inscrição prova OAB"), It.IsAny<IReadOnlyList<ResumoEventoItem>>(), It.IsAny<ResumoDiarioExtras?>()),
            Times.Once);
    }

    [Fact]
    public async Task Resumo_ItemSoInApp_NaoDeveIrNoEmail_MasDeveIrNaNotificacao()
    {
        var (ctx, tenantId, responsavelId) = await SeedAsync();
        var hoje = BrasiliaTime.Hoje;
        ctx.Tarefas.Add(NovaTarefa(tenantId, responsavelId, "Tarefa atrasada", hoje.AddDays(-2)));
        ctx.Tarefas.Add(NovaTarefa(tenantId, responsavelId, "Tarefa amanhã", hoje.AddDays(1)));
        await ctx.SaveChangesAsync();

        var mockEmail = new Mock<IEmailService>();
        var prefs = Prefs(tenantId, responsavelId, ("PrazoTarefa", true, true), ("TarefaAtrasada", false, true));
        var job = new AlertasJob(ctx, mockEmail.Object, prefs.Object, Mock.Of<ILogger<AlertasJob>>());
        await job.ExecutarAsync(hoje);

        mockEmail.Verify(e => e.EnviarResumoDiarioAsync(
            It.IsAny<string>(), It.IsAny<string>(),
            It.Is<IReadOnlyList<ResumoTarefaItem>>(l => l.Count == 1 && l[0].Titulo == "Tarefa amanhã"), It.IsAny<IReadOnlyList<ResumoEventoItem>>(), It.IsAny<ResumoDiarioExtras?>()),
            Times.Once);
        var notif = await ctx.Notificacoes.SingleAsync();
        Assert.Contains("Tarefa atrasada", notif.Mensagem);
        Assert.Contains("Tarefa amanhã", notif.Mensagem);
        Assert.False(notif.Lida);
    }

    [Fact]
    public async Task Resumo_SoEmailPermitido_NotificacaoDeDedupJaNasceLida()
    {
        var (ctx, tenantId, responsavelId) = await SeedAsync();
        var hoje = BrasiliaTime.Hoje;
        ctx.Tarefas.Add(NovaTarefa(tenantId, responsavelId, "Tarefa amanhã", hoje.AddDays(1)));
        await ctx.SaveChangesAsync();

        var mockEmail = new Mock<IEmailService>();
        var prefs = Prefs(tenantId, responsavelId, ("PrazoTarefa", true, false));
        var job = new AlertasJob(ctx, mockEmail.Object, prefs.Object, Mock.Of<ILogger<AlertasJob>>());
        await job.ExecutarAsync(hoje);
        await job.ExecutarAsync(hoje);

        mockEmail.Verify(e => e.EnviarResumoDiarioAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IReadOnlyList<ResumoTarefaItem>>(), It.IsAny<IReadOnlyList<ResumoEventoItem>>(), It.IsAny<ResumoDiarioExtras?>()), Times.Once);
        Assert.True((await ctx.Notificacoes.SingleAsync()).Lida);
    }

    [Fact]
    public async Task Resumo_TarefaTipoPrazo_DeveSeguirPreferenciaPrazos()
    {
        var (ctx, tenantId, responsavelId) = await SeedAsync();
        var hoje = BrasiliaTime.Hoje;
        ctx.Tarefas.Add(NovaTarefa(tenantId, responsavelId, "Prazo contestação", hoje.AddDays(1), TipoTarefa.Prazo));
        await ctx.SaveChangesAsync();

        var mockEmail = new Mock<IEmailService>();
        var prefs = Prefs(tenantId, responsavelId, ("Prazos", false, false), ("PrazoTarefa", true, true));
        var job = new AlertasJob(ctx, mockEmail.Object, prefs.Object, Mock.Of<ILogger<AlertasJob>>());
        await job.ExecutarAsync(hoje);

        mockEmail.Verify(e => e.EnviarResumoDiarioAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IReadOnlyList<ResumoTarefaItem>>(), It.IsAny<IReadOnlyList<ResumoEventoItem>>(), It.IsAny<ResumoDiarioExtras?>()), Times.Never);
        Assert.Empty(ctx.Notificacoes);
    }

    [Fact]
    public async Task PrazoAmanha_NaoDeveIrNoEmailDeEventos_SoNoResumo()
    {
        var (ctx, tenantId, responsavelId) = await SeedAsync();
        var hoje = BrasiliaTime.Hoje;
        ctx.Tarefas.Add(new Tarefa
        {
            Id = Guid.NewGuid(), TenantId = tenantId, Titulo = "Inscrição prova OAB",
            Prazo = hoje.AddDays(1).AddHours(21), Status = StatusTarefa.Pendente, Tipo = TipoTarefa.Prazo,
            ResponsavelId = responsavelId, CriadoEm = DateTime.UtcNow, CriadoPorId = responsavelId,
            Prioridade = PrioridadeTarefa.Media
        });
        await ctx.SaveChangesAsync();

        var mockEmail = new Mock<IEmailService>();
        var job = new AlertasJob(ctx, mockEmail.Object, PrefAberto(tenantId, responsavelId).Object, Mock.Of<ILogger<AlertasJob>>());
        await job.ExecutarAsync(hoje);

        mockEmail.Verify(e => e.EnviarResumoDiarioAsync(
            It.IsAny<string>(), It.IsAny<string>(),
            It.Is<IReadOnlyList<ResumoTarefaItem>>(l => l.Single().Titulo == "Inscrição prova OAB"),
            It.Is<IReadOnlyList<ResumoEventoItem>>(l => l.Count == 0), It.IsAny<ResumoDiarioExtras?>()), Times.Once);
    }

    [Fact]
    public async Task Resumo_SemHojeFixo_DeveUsarFusoDoEscritorio()
    {
        var (ctx, tenantId, responsavelId) = await SeedAsync();
        // UTC+14: o "hoje" do escritório está sempre 1 ou 2 dias à frente do de Brasília.
        var tenant = await ctx.Tenants.FindAsync(tenantId);
        tenant!.FusoHorario = "Etc/GMT-14";
        // 10h UTC de 22/10: meia-noite de 23/10 no escritório (hora do resumo), 07h de 22/10 em Brasília.
        var agoraUtc = new DateTime(2026, 10, 22, 10, 0, 0, DateTimeKind.Utc);
        ctx.Tarefas.Add(NovaTarefa(tenantId, responsavelId, "Vence hoje lá", new DateTime(2026, 10, 23, 18, 0, 0)));
        await ctx.SaveChangesAsync();

        var mockEmail = new Mock<IEmailService>();
        var job = new AlertasJob(ctx, mockEmail.Object, PrefAberto(tenantId, responsavelId).Object, Mock.Of<ILogger<AlertasJob>>())
        {
            RelogioUtc = () => agoraUtc
        };
        await job.ExecutarAsync();

        mockEmail.Verify(e => e.EnviarResumoDiarioAsync(
            It.IsAny<string>(), It.IsAny<string>(),
            It.Is<IReadOnlyList<ResumoTarefaItem>>(l => l.Single().Dias == 0), It.IsAny<IReadOnlyList<ResumoEventoItem>>(), It.IsAny<ResumoDiarioExtras?>()), Times.Once);
    }

    [Fact]
    public async Task Resumo_ComMuitosItens_DeveCaberNosLimitesDaNotificacao()
    {
        var (ctx, tenantId, responsavelId) = await SeedAsync();
        var hoje = BrasiliaTime.Hoje;
        for (var i = 0; i < 30; i++)
            ctx.Tarefas.Add(NovaTarefa(tenantId, responsavelId, $"Tarefa {i} " + new string('x', 80), hoje.AddDays(1)));
        await ctx.SaveChangesAsync();

        var job = new AlertasJob(ctx, Mock.Of<IEmailService>(), PrefAberto(tenantId, responsavelId).Object, Mock.Of<ILogger<AlertasJob>>());
        await job.ExecutarAsync(hoje);

        var notif = await ctx.Notificacoes.SingleAsync(n => n.Tipo == TipoNotificacao.PrazoTarefa);
        Assert.True(notif.Mensagem.Length <= 1000);
        Assert.True(notif.Titulo.Length <= 300);
    }

    // Regressão: uma falha ao gravar o resumo de tarefas deixava a entidade no ChangeTracker e
    // todo SaveChanges seguinte do job falhava — inclusive o que cria as preferências do admin
    // do trial, antes do envio. Resultado: e-mails de fim de trial (7, 3 e 1 dia) não saíam.
    [Fact]
    public async Task FalhaAoGravarResumoDeTarefas_NaoDeveImpedirEmailDeTrial()
    {
        var (ctx, tenantId, responsavelId) = await SeedAsync();
        var hoje = BrasiliaTime.Hoje;
        ctx.Tarefas.Add(NovaTarefa(tenantId, responsavelId, "Tarefa", hoje.AddDays(1)));

        var trialId = Guid.NewGuid();
        var adminId = Guid.NewGuid();
        ctx.Tenants.Add(new Tenant
        {
            Id = trialId, Nome = "Trial Tenant", Plano = PlanoTipo.Plus,
            Status = StatusTenant.Trial, TrialExpiraEm = hoje.AddDays(7), CriadoEm = DateTime.UtcNow
        });
        ctx.Users.Add(new Usuario
        {
            Id = adminId, TenantId = trialId, Nome = "Admin",
            Email = "admin@trial.com", UserName = "admin@trial.com",
            Perfil = PerfilUsuario.Admin, Ativo = true, CriadoEm = DateTime.UtcNow
        });
        await ctx.SaveChangesAsync();

        // Simula o INSERT inválido do resumo (ex.: Mensagem acima do limite no PostgreSQL).
        var mockEmail = new Mock<IEmailService>();
        mockEmail.Setup(e => e.EnviarResumoDiarioAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IReadOnlyList<ResumoTarefaItem>>(), It.IsAny<IReadOnlyList<ResumoEventoItem>>(), It.IsAny<ResumoDiarioExtras?>()))
            .Callback(() => ctx.Notificacoes.Add(new Notificacao
            {
                Id = Guid.NewGuid(), TenantId = tenantId, UsuarioId = responsavelId,
                Titulo = null!, Mensagem = null!, CriadaEm = DateTime.UtcNow
            }))
            .Returns(Task.CompletedTask);

        // Serviço real: sem preferências gravadas, ele as cria com SaveChanges no mesmo contexto.
        var prefs = new PreferenciasNotificacaoService(ctx);
        var job = new AlertasJob(ctx, mockEmail.Object, prefs, Mock.Of<ILogger<AlertasJob>>());
        await job.ExecutarAsync(hoje);

        mockEmail.Verify(e => e.EnviarTrialExpirandoAsync("admin@trial.com", "Trial Tenant", 7), Times.Once);
        Assert.True(await ctx.Notificacoes.AnyAsync(n => n.ChaveDedup!.StartsWith("email-trial-")));
    }
}
