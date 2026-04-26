using LegalManager.Application.Interfaces;
using LegalManager.Domain.Entities;
using LegalManager.Domain.Enums;
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

    [Fact]
    public async Task ExecutarAsync_DeveAlertarTarefaVencendoHoje()
    {
        var (ctx, tenantId, responsavelId) = await SeedAsync();
        var hoje = DateTime.UtcNow.Date;
        ctx.Tarefas.Add(new Tarefa
        {
            Id = Guid.NewGuid(), TenantId = tenantId, Titulo = "Tarefa Urgente",
            Status = StatusTarefa.Pendente, Prioridade = PrioridadeTarefa.Alta,
            Prazo = hoje, ResponsavelId = responsavelId,
            CriadoEm = DateTime.UtcNow, CriadoPorId = responsavelId
        });
        await ctx.SaveChangesAsync();

        var mockEmail = new Mock<IEmailService>();
        var mockPrefs = new Mock<IPreferenciasNotificacaoService>();
        mockPrefs.Setup(p => p.PermiteEmailAsync(tenantId, responsavelId, "PrazoTarefa")).ReturnsAsync(true);
        mockPrefs.Setup(p => p.PermiteInAppAsync(tenantId, responsavelId, "PrazoTarefa")).ReturnsAsync(true);

        var job = new AlertasJob(ctx, mockEmail.Object, mockPrefs.Object, Mock.Of<ILogger<AlertasJob>>());
        await job.ExecutarAsync();

        mockEmail.Verify(e => e.EnviarAlertaPrazoTarefaAsync(
            "resp@test.com", "Responsável", "Tarefa Urgente", hoje, 0), Times.Once);

        var notif = await ctx.Notificacoes.FirstOrDefaultAsync(n => n.Titulo.Contains("vencendo hoje"));
        Assert.NotNull(notif);
    }

    [Fact]
    public async Task ExecutarAsync_NaoDeveEnviarEmail_QuandoPrefereenciasDesabilitadas()
    {
        var (ctx, tenantId, responsavelId) = await SeedAsync();
        var hoje = DateTime.UtcNow.Date;
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
        mockPrefs.Setup(p => p.PermiteEmailAsync(tenantId, responsavelId, "PrazoTarefa")).ReturnsAsync(false);
        mockPrefs.Setup(p => p.PermiteInAppAsync(tenantId, responsavelId, "PrazoTarefa")).ReturnsAsync(false);

        var job = new AlertasJob(ctx, mockEmail.Object, mockPrefs.Object, Mock.Of<ILogger<AlertasJob>>());
        await job.ExecutarAsync();

        mockEmail.Verify(e => e.EnviarAlertaPrazoTarefaAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<DateTime>(), It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task ExecutarAsync_DeveAlertarEventoAmanha()
    {
        var (ctx, tenantId, responsavelId) = await SeedAsync();
        var amanha = DateTime.UtcNow.Date.AddDays(1);
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
        await job.ExecutarAsync();

        mockEmail.Verify(e => e.EnviarAlertaEventoAsync(
            "resp@test.com", "Responsável", "Audiência",
            It.IsAny<DateTime>(), It.IsAny<string?>()), Times.Once);
    }

    [Fact]
    public async Task ExecutarAsync_NaoDeveAlertarTarefaConcluida()
    {
        var (ctx, tenantId, responsavelId) = await SeedAsync();
        var hoje = DateTime.UtcNow.Date;
        ctx.Tarefas.Add(new Tarefa
        {
            Id = Guid.NewGuid(), TenantId = tenantId, Titulo = "Tarefa Concluida",
            Status = StatusTarefa.Concluida, Prioridade = PrioridadeTarefa.Alta,
            Prazo = hoje, ResponsavelId = responsavelId,
            CriadoEm = DateTime.UtcNow, CriadoPorId = responsavelId
        });
        await ctx.SaveChangesAsync();

        var mockEmail = new Mock<IEmailService>();
        var mockPrefs = new Mock<IPreferenciasNotificacaoService>();

        var job = new AlertasJob(ctx, mockEmail.Object, mockPrefs.Object, Mock.Of<ILogger<AlertasJob>>());
        await job.ExecutarAsync();

        mockEmail.Verify(e => e.EnviarAlertaPrazoTarefaAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<DateTime>(), It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task ExecutarAsync_DeveAlertarTrialExpirando()
    {
        var ctx = CreateContext();
        var tenantId = Guid.NewGuid();
        var adminId = Guid.NewGuid();
        var expiraEm = DateTime.UtcNow.Date.AddDays(3);

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
        await job.ExecutarAsync();

        mockEmail.Verify(e => e.EnviarTrialExpirandoAsync("admin@test.com", "Trial Tenant", 3), Times.Once);
        var notif = await ctx.Notificacoes.FirstOrDefaultAsync();
        Assert.NotNull(notif);
        Assert.Equal(TipoNotificacao.TrialExpirando, notif.Tipo);
    }

    [Fact]
    public async Task ExecutarAsync_DeveAlertarPrazoProcessual()
    {
        var (ctx, tenantId, responsavelId) = await SeedAsync();
        var amanha = DateTime.UtcNow.Date.AddDays(1);
        var processoId = Guid.NewGuid();

        ctx.Processos.Add(new Processo
        {
            Id = processoId, TenantId = tenantId, NumeroCNJ = "0000001-00.2024.8.26.0001",
            AreaDireito = AreaDireito.Civil, Fase = FaseProcessual.Conhecimento,
            Status = StatusProcesso.Ativo, CriadoEm = DateTime.UtcNow
        });
        ctx.Prazos.Add(new Prazo
        {
            Id = Guid.NewGuid(), TenantId = tenantId, ProcessoId = processoId,
            Descricao = "Prazo para contestação", DataInicio = DateTime.UtcNow.Date.AddDays(-5),
            QuantidadeDias = 5, TipoCalculo = TipoCalculo.DiasCorridos,
            DataFinal = amanha, Status = StatusPrazo.Pendente,
            ResponsavelId = responsavelId, CriadoEm = DateTime.UtcNow
        });
        await ctx.SaveChangesAsync();

        var mockEmail = new Mock<IEmailService>();
        var mockPrefs = new Mock<IPreferenciasNotificacaoService>();
        mockPrefs.Setup(p => p.PermiteEmailAsync(tenantId, responsavelId, "Prazos")).ReturnsAsync(true);
        mockPrefs.Setup(p => p.PermiteInAppAsync(tenantId, responsavelId, "Prazos")).ReturnsAsync(true);

        var job = new AlertasJob(ctx, mockEmail.Object, mockPrefs.Object, Mock.Of<ILogger<AlertasJob>>());
        await job.ExecutarAsync();

        mockEmail.Verify(e => e.EnviarAlertaPrazoProcessualAsync(
            "resp@test.com", "Responsável", "0000001-00.2024.8.26.0001",
            "Prazo para contestação", amanha, 1), Times.Once);
    }

    [Fact]
    public async Task CriarNotificacaoAsync_DeveEvitarDuplicatasPorChaveDedup()
    {
        var (ctx, tenantId, responsavelId) = await SeedAsync();
        var mockEmail = new Mock<IEmailService>();
        var mockPrefs = new Mock<IPreferenciasNotificacaoService>();

        var job = new AlertasJob(ctx, mockEmail.Object, mockPrefs.Object, Mock.Of<ILogger<AlertasJob>>());

        var chave = "tarefa-test-0d-20260101";
        await job.ExecutarAsync();
        await job.ExecutarAsync();

        var existing = await ctx.Notificacoes.AnyAsync(n => n.ChaveDedup == chave);
        Assert.False(existing);
    }

    [Fact]
    public async Task ExecutarAsync_NaoDeveFalhar_QuandoNenhumDadoExistente()
    {
        var (ctx, _, _) = await SeedAsync();

        var mockEmail = new Mock<IEmailService>();
        var mockPrefs = new Mock<IPreferenciasNotificacaoService>();

        var job = new AlertasJob(ctx, mockEmail.Object, mockPrefs.Object, Mock.Of<ILogger<AlertasJob>>());
        await job.ExecutarAsync();

        mockEmail.Verify(e => e.EnviarAlertaPrazoTarefaAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<DateTime>(), It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task ExecutarAsync_DeveSuportarMultiplosDiasDeLimite()
    {
        var (ctx, tenantId, responsavelId) = await SeedAsync();
        var hoje = DateTime.UtcNow.Date;
        ctx.Tarefas.AddRange(
            new Tarefa { Id = Guid.NewGuid(), TenantId = tenantId, Titulo = "T1", Status = StatusTarefa.Pendente, Prazo = hoje, ResponsavelId = responsavelId, CriadoEm = DateTime.UtcNow, CriadoPorId = responsavelId },
            new Tarefa { Id = Guid.NewGuid(), TenantId = tenantId, Titulo = "T2", Status = StatusTarefa.Pendente, Prazo = hoje.AddDays(1), ResponsavelId = responsavelId, CriadoEm = DateTime.UtcNow, CriadoPorId = responsavelId },
            new Tarefa { Id = Guid.NewGuid(), TenantId = tenantId, Titulo = "T3", Status = StatusTarefa.Pendente, Prazo = hoje.AddDays(3), ResponsavelId = responsavelId, CriadoEm = DateTime.UtcNow, CriadoPorId = responsavelId }
        );
        await ctx.SaveChangesAsync();

        var mockEmail = new Mock<IEmailService>();
        var mockPrefs = new Mock<IPreferenciasNotificacaoService>();
        mockPrefs.Setup(p => p.PermiteEmailAsync(tenantId, responsavelId, "PrazoTarefa")).ReturnsAsync(true);
        mockPrefs.Setup(p => p.PermiteInAppAsync(tenantId, responsavelId, "PrazoTarefa")).ReturnsAsync(true);

        var job = new AlertasJob(ctx, mockEmail.Object, mockPrefs.Object, Mock.Of<ILogger<AlertasJob>>());
        await job.ExecutarAsync();

        mockEmail.Verify(e => e.EnviarAlertaPrazoTarefaAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<DateTime>(), It.IsAny<int>()), Times.Exactly(3));
    }
}
