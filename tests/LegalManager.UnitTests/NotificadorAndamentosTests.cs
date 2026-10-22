using LegalManager.Application.Interfaces;
using LegalManager.Domain.Entities;
using LegalManager.Domain.Enums;
using LegalManager.Infrastructure.Persistence;
using LegalManager.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace LegalManager.UnitTests;

public class NotificadorAndamentosTests
{
    private const string Cnj = "1001885-87.2026.8.26.0077";

    private static async Task<(AppDbContext ctx, Processo processo, Usuario adv)> SeedAsync()
    {
        var ctx = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var tenant = new Tenant { Id = Guid.NewGuid(), Nome = "NPJ", Plano = PlanoTipo.Free, Status = StatusTenant.Ativo, CriadoEm = DateTime.UtcNow };
        var adv = new Usuario
        {
            Id = Guid.NewGuid(), TenantId = tenant.Id, Nome = "Adv", Email = "adv@npj.com", UserName = "adv@npj.com",
            Perfil = PerfilUsuario.Advogado, Ativo = true, CriadoEm = DateTime.UtcNow
        };
        var processo = new Processo
        {
            Id = Guid.NewGuid(), TenantId = tenant.Id, NumeroCNJ = Cnj, AreaDireito = AreaDireito.Civil,
            Fase = FaseProcessual.Execucao, Status = StatusProcesso.Ativo, Monitorado = true,
            AdvogadoResponsavelId = adv.Id, CriadoEm = DateTime.UtcNow
        };
        ctx.Tenants.Add(tenant);
        ctx.Users.Add(adv);
        ctx.Processos.Add(processo);
        await ctx.SaveChangesAsync();
        return (ctx, processo, adv);
    }

    private static Andamento Andamento(Processo p, DateTime data, string texto) => new()
    {
        Id = Guid.NewGuid(), ProcessoId = p.Id, TenantId = p.TenantId, Data = data, Descricao = texto,
        Tipo = TipoAndamento.Intimacao, Fonte = FonteAndamento.Automatico, CriadoEm = DateTime.UtcNow
    };

    [Fact]
    public async Task SemPreferenciasSalvas_AvisaNoSinoComLinkParaOProcessoEPorEmail()
    {
        var (ctx, processo, adv) = await SeedAsync();
        var email = new Mock<IEmailService>();
        var notificador = new NotificadorAndamentos(ctx, email.Object, NullLogger<NotificadorAndamentos>.Instance);

        await notificador.NotificarAsync(processo, [Andamento(processo, DateTime.UtcNow.Date, "Intimação")], DateTime.UtcNow);
        await ctx.SaveChangesAsync();

        var notif = await ctx.Notificacoes.SingleAsync();
        Assert.Equal(adv.Id, notif.UsuarioId);
        Assert.Equal($"/pages/processo-detalhe.html?id={processo.Id}", notif.Url);
        Assert.Equal($"Novo andamento — {Cnj}", notif.Titulo);
        email.Verify(e => e.EnviarNovoAndamentoAsync("adv@npj.com", "Adv", Cnj, "Intimação", processo.Id, 1, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task VariosAndamentos_UmAvisoSoComOMaisRecente()
    {
        var (ctx, processo, _) = await SeedAsync();
        var email = new Mock<IEmailService>();
        var notificador = new NotificadorAndamentos(ctx, email.Object, NullLogger<NotificadorAndamentos>.Instance);
        var hoje = DateTime.UtcNow.Date;

        await notificador.NotificarAsync(processo,
            [Andamento(processo, hoje.AddDays(-1), "Antigo"), Andamento(processo, hoje, "Mais recente")], DateTime.UtcNow);
        await ctx.SaveChangesAsync();

        var notif = await ctx.Notificacoes.SingleAsync();
        Assert.Equal($"2 novos andamentos — {Cnj}", notif.Titulo);
        Assert.Equal("Mais recente", notif.Mensagem);
        email.Verify(e => e.EnviarNovoAndamentoAsync(It.IsAny<string>(), It.IsAny<string>(), Cnj, "Mais recente", processo.Id, 2, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task PreferenciasDesligadas_NaoAvisa()
    {
        var (ctx, processo, adv) = await SeedAsync();
        ctx.PreferenciasNotificacoes.Add(new PreferenciasNotificacao
        {
            Id = Guid.NewGuid(), TenantId = processo.TenantId, UsuarioId = adv.Id,
            PublicacoesInApp = false, PublicacoesEmail = false
        });
        await ctx.SaveChangesAsync();
        var email = new Mock<IEmailService>();
        var notificador = new NotificadorAndamentos(ctx, email.Object, NullLogger<NotificadorAndamentos>.Instance);

        await notificador.NotificarAsync(processo, [Andamento(processo, DateTime.UtcNow.Date, "Intimação")], DateTime.UtcNow);
        await ctx.SaveChangesAsync();

        Assert.Empty(await ctx.Notificacoes.ToListAsync());
        email.VerifyNoOtherCalls();
    }
}
