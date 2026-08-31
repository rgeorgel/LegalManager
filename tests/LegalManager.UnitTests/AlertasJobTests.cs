using LegalManager.Application.Interfaces;
using LegalManager.Domain.Entities;
using LegalManager.Domain.Enums;
using LegalManager.Infrastructure;
using LegalManager.Infrastructure.Jobs;
using LegalManager.Infrastructure.Persistence;
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

        mockEmail.Verify(e => e.EnviarResumoTarefasAsync(
            "resp@test.com", "Responsável",
            It.Is<IReadOnlyList<ResumoTarefaItem>>(l => l.Count == 1 && l[0].Dias == 0 && l[0].Titulo == "Tarefa Urgente")),
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

        mockEmail.Verify(e => e.EnviarResumoTarefasAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IReadOnlyList<ResumoTarefaItem>>()),
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

        mockEmail.Verify(e => e.EnviarAlertaEventoAsync(
            "resp@test.com", "Responsável", "Audiência",
            It.IsAny<DateTime>(), It.IsAny<string?>()), Times.Once);
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

        mockEmail.Verify(e => e.EnviarResumoTarefasAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IReadOnlyList<ResumoTarefaItem>>()),
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
    }

    [Fact]
    public async Task ExecutarAsync_DeveAlertarPrazoProcessual()
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
        var mockPrefs = new Mock<IPreferenciasNotificacaoService>();
        mockPrefs.Setup(p => p.PermiteEmailAsync(tenantId, responsavelId, "Prazos")).ReturnsAsync(true);
        mockPrefs.Setup(p => p.PermiteInAppAsync(tenantId, responsavelId, "Prazos")).ReturnsAsync(true);

        var job = new AlertasJob(ctx, mockEmail.Object, mockPrefs.Object, Mock.Of<ILogger<AlertasJob>>());
        await job.ExecutarAsync(hoje);

        mockEmail.Verify(e => e.EnviarAlertaPrazoProcessualAsync(
            "resp@test.com", "Responsável", "0000001-00.2024.8.26.0001",
            "Prazo para contestação", amanha, 1), Times.Once);
    }

    [Fact]
    public async Task ExecutarAsync_NaoDeveFalhar_QuandoNenhumDadoExistente()
    {
        var (ctx, _, _) = await SeedAsync();

        var mockEmail = new Mock<IEmailService>();
        var job = new AlertasJob(ctx, mockEmail.Object, new Mock<IPreferenciasNotificacaoService>().Object, Mock.Of<ILogger<AlertasJob>>());
        await job.ExecutarAsync();

        mockEmail.Verify(e => e.EnviarResumoTarefasAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IReadOnlyList<ResumoTarefaItem>>()),
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

        mockEmail.Verify(e => e.EnviarResumoTarefasAsync(
            "resp@test.com", "Responsável",
            It.Is<IReadOnlyList<ResumoTarefaItem>>(l => l.Count == 3)),
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

        mockEmail.Verify(e => e.EnviarResumoTarefasAsync(
            "resp@test.com", "Responsável", It.IsAny<IReadOnlyList<ResumoTarefaItem>>()),
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

        mockEmail.Verify(e => e.EnviarAlertaEventoAsync(
            It.IsAny<string>(), It.IsAny<string>(), "Audiência",
            It.IsAny<DateTime>(), It.IsAny<string?>()), Times.Once);
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
    public async Task ExecutarAsync_NaoDeveEnviarEmailDuplicado_PrazoProcessual_QuandoJobRodaDuasVezes()
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
        var mockPrefs = new Mock<IPreferenciasNotificacaoService>();
        mockPrefs.Setup(p => p.PermiteEmailAsync(tenantId, responsavelId, "Prazos")).ReturnsAsync(true);
        mockPrefs.Setup(p => p.PermiteInAppAsync(tenantId, responsavelId, "Prazos")).ReturnsAsync(true);

        var job = new AlertasJob(ctx, mockEmail.Object, mockPrefs.Object, Mock.Of<ILogger<AlertasJob>>());

        await job.ExecutarAsync(hoje);
        await job.ExecutarAsync(hoje);

        mockEmail.Verify(e => e.EnviarAlertaPrazoProcessualAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<int>()), Times.Once);
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

        mockEmail.Verify(e => e.EnviarResumoTarefasAsync(
            "resp@test.com", "Responsável",
            It.Is<IReadOnlyList<ResumoTarefaItem>>(l => l.Count == 1 && l[0].Titulo == "Tarefa sem responsavel")),
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

        mockEmail.Verify(e => e.EnviarResumoTarefasAsync(
            "resp@test.com", "Responsável",
            It.Is<IReadOnlyList<ResumoTarefaItem>>(l =>
                l.Count == 1 && l[0].Dias == -3 && l[0].Titulo == "Tarefa Atrasada")),
            Times.Once);

        var notif = await ctx.Notificacoes.FirstOrDefaultAsync(n =>
            n.Tipo == TipoNotificacao.PrazoTarefa && n.UsuarioId == responsavelId);
        Assert.NotNull(notif);
        Assert.Contains("atrasada", notif.Titulo, StringComparison.OrdinalIgnoreCase);
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

        mockEmail.Verify(e => e.EnviarResumoTarefasAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IReadOnlyList<ResumoTarefaItem>>()),
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

        mockEmail.Verify(e => e.EnviarResumoTarefasAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IReadOnlyList<ResumoTarefaItem>>()),
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

        mockEmail.Verify(e => e.EnviarResumoTarefasAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IReadOnlyList<ResumoTarefaItem>>()),
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

        mockEmail.Verify(e => e.EnviarResumoTarefasAsync(
            "resp@test.com", "Responsável",
            It.Is<IReadOnlyList<ResumoTarefaItem>>(l =>
                l.Count == 5 &&
                l.Any(i => i.Titulo == "Atrasada 2d" && i.Dias == -2) &&
                l.Any(i => i.Titulo == "Vence Hoje" && i.Dias == 0) &&
                l.Any(i => i.Titulo == "Vence Amanha" && i.Dias == 1) &&
                l.Any(i => i.Titulo == "Vence em 3d" && i.Dias == 3) &&
                l.Any(i => i.Titulo == "Vence em 5d" && i.Dias == 5) &&
                !l.Any(i => i.Titulo == "Longe (fora)"))),
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
}
