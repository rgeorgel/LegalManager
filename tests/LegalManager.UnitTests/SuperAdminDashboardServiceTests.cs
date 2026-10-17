using LegalManager.Application.Interfaces;
using LegalManager.Domain;
using LegalManager.Domain.Entities;
using LegalManager.Domain.Enums;
using LegalManager.Infrastructure.Persistence;
using LegalManager.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Stripe;

namespace LegalManager.UnitTests;

/// <summary>GET /api/superadmin/dashboard — indicadores agregados (SuperAdminDashboardService).</summary>
public class SuperAdminDashboardServiceTests
{
    private readonly Mock<IStripeService> _stripe = new();

    private static AppDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private SuperAdminDashboardService CreateService(AppDbContext ctx) =>
        new(ctx, _stripe.Object, new PrecosPlanosService(ctx, new MemoryCache(new MemoryCacheOptions())),
            new MemoryCache(new MemoryCacheOptions()), NullLogger<SuperAdminDashboardService>.Instance);

    private static Tenant NovoTenant(string nome, PlanoTipo plano = PlanoTipo.Free, StatusTenant status = StatusTenant.Ativo,
        DateTime? criadoEm = null, string? subscription = null) => new()
    {
        Id = Guid.NewGuid(),
        Nome = nome,
        Plano = plano,
        Status = status,
        CriadoEm = criadoEm ?? DateTime.UtcNow.AddDays(-60),
        StripeSubscriptionId = subscription,
        PeriodoBilling = subscription is null ? null : "Mensal"
    };

    private static Usuario NovoUsuario(Guid tenantId, DateTime? ultimoAcesso = null, PerfilUsuario perfil = PerfilUsuario.Admin) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = tenantId,
        Nome = "Usuário",
        Email = $"{Guid.NewGuid():N}@x.com",
        UserName = $"{Guid.NewGuid():N}@x.com",
        Perfil = perfil,
        Ativo = true,
        UltimoAcessoEm = ultimoAcesso
    };

    [Fact]
    public async Task Receita_UsaPreviaDoStripe_SoParaPagantesComAssinatura()
    {
        using var ctx = CreateContext();
        var mensal = NovoTenant("Mensal", PlanoTipo.Pro, subscription: "sub_mensal");
        var anual = NovoTenant("Anual", PlanoTipo.Plus, subscription: "sub_anual");
        anual.PeriodoBilling = "Anual";
        var cancelando = NovoTenant("Cancelando", PlanoTipo.Plus, subscription: "sub_cancelando");
        ctx.Tenants.AddRange(mensal, anual, cancelando,
            NovoTenant("Trial", PlanoTipo.Plus, StatusTenant.Trial),
            NovoTenant("Sem Stripe", PlanoTipo.Pro));
        await ctx.SaveChangesAsync();

        var em10Dias = DateTime.UtcNow.AddDays(10);
        _stripe.Setup(s => s.ObterProximaCobrancaAsync("sub_mensal", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StripeProximaCobranca(25m, em10Dias)); // com desconto: 50 → 25
        _stripe.Setup(s => s.ObterProximaCobrancaAsync("sub_anual", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StripeProximaCobranca(240m, DateTime.UtcNow.AddDays(200)));
        _stripe.Setup(s => s.ObterProximaCobrancaAsync("sub_cancelando", It.IsAny<CancellationToken>()))
            .ReturnsAsync((StripeProximaCobranca?)null);

        var r = (await CreateService(ctx).ObterAsync()).Receita;

        Assert.Equal(3, r.Pagantes);
        Assert.Equal(1, r.CancelamentoAgendado);
        Assert.Equal(0, r.Estimados);
        Assert.Equal(25m + 20m, r.MrrReal); // anual normalizado: 240 / 12
        Assert.Equal(25m, r.ReceberProximos30Dias);
        Assert.Equal(2, r.ProximasCobrancas.Count);
        Assert.Equal("Mensal", r.ProximasCobrancas[0].Nome);
    }

    [Fact]
    public async Task Receita_FalhaNoStripe_UsaPrecoDeTabelaComDescontoVigente()
    {
        using var ctx = CreateContext();
        var t = NovoTenant("Com cupom", PlanoTipo.Pro, subscription: "sub_x");
        t.CodigoDesconto = "OAB50";
        t.DescontoPromocionalUsadoEm = DateTime.UtcNow.AddDays(-10);
        ctx.Tenants.Add(t);
        ctx.CodigosPromocionais.Add(new CodigoPromocional
        {
            Id = Guid.NewGuid(), Codigo = "oab50", DescontoPercentual = 50, DescontoMeses = 3, CriadoEm = DateTime.UtcNow
        });
        await ctx.SaveChangesAsync();
        _stripe.Setup(s => s.ObterProximaCobrancaAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new StripeException("fora do ar"));

        var r = (await CreateService(ctx).ObterAsync()).Receita;

        Assert.Equal(1, r.Estimados);
        Assert.Equal(PlanoPrecos.Padrao[PlanoTipo.Pro] / 2, r.MrrReal);
        Assert.Equal(PlanoPrecos.Padrao[PlanoTipo.Pro], r.MrrTabela);
    }

    [Fact]
    public async Task Receita_FalhaNoStripe_UsaOValorDaAssinaturaENaoATabelaAtual()
    {
        using var ctx = CreateContext();
        var t = NovoTenant("Assinou antes do aumento", PlanoTipo.Pro, subscription: "sub_x");
        t.ValorMensalAssinatura = 50m;
        ctx.Tenants.Add(t);
        ctx.PrecosPlanos.Add(new PrecoPlano { Plano = PlanoTipo.Pro, ValorMensal = 70m });
        await ctx.SaveChangesAsync();
        _stripe.Setup(s => s.ObterProximaCobrancaAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new StripeException("fora do ar"));

        var r = (await CreateService(ctx).ObterAsync()).Receita;

        Assert.Equal(50m, r.MrrReal);
        Assert.Equal(70m, r.MrrTabela);
    }

    [Fact]
    public async Task Cadastros_ComparaSemanaAtualComAnterior()
    {
        using var ctx = CreateContext();
        ctx.Tenants.AddRange(
            NovoTenant("A", criadoEm: DateTime.UtcNow.AddDays(-1)),
            NovoTenant("B", criadoEm: DateTime.UtcNow.AddDays(-3)),
            NovoTenant("C", criadoEm: DateTime.UtcNow.AddDays(-9)),
            NovoTenant("D", criadoEm: DateTime.UtcNow.AddDays(-40)));
        await ctx.SaveChangesAsync();

        var c = (await CreateService(ctx).ObterAsync()).Cadastros;

        Assert.Equal(2, c.Ultimos7Dias);
        Assert.Equal(1, c.Anteriores7Dias);
        Assert.Equal(12, c.PorSemana.Count);
        Assert.Equal(2, c.PorSemana[^1].Total);
        Assert.Equal("A", c.Recentes[0].Nome);
    }

    [Fact]
    public async Task Acessos_ContaUsuariosPorUltimoAcessoELoginsDoAudit()
    {
        using var ctx = CreateContext();
        var t = NovoTenant("T");
        ctx.Tenants.Add(t);
        ctx.Users.AddRange(
            NovoUsuario(t.Id, DateTime.UtcNow.AddHours(-2)),
            NovoUsuario(t.Id, DateTime.UtcNow.AddDays(-5)),
            NovoUsuario(t.Id, null),
            NovoUsuario(TenantConstants.SystemTenantId, DateTime.UtcNow, PerfilUsuario.SuperAdmin));
        foreach (var dias in new[] { 1, 2, 10 })
            ctx.AuditLogs.Add(new AuditLog { Id = Guid.NewGuid(), TenantId = t.Id, Acao = AuditActions.Login, Entidade = "Auth", CriadoEm = DateTime.UtcNow.AddDays(-dias) });
        await ctx.SaveChangesAsync();

        var a = (await CreateService(ctx).ObterAsync()).Acessos;

        Assert.Equal(3, a.TotalUsuarios);
        Assert.Equal(1, a.Acessaram24h);
        Assert.Equal(2, a.Acessaram7Dias);
        Assert.Equal(1, a.NuncaAcessaram);
        Assert.Equal(2, a.Logins7Dias);
        Assert.Equal(1, a.LoginsAnteriores7Dias);
        Assert.Equal(3, a.LoginsPorDia.Sum(d => d.Total));
    }

    [Fact]
    public async Task TrialECupons_ContamTenantsEUsuarios()
    {
        using var ctx = CreateContext();
        var trial = NovoTenant("Trial", PlanoTipo.Plus, StatusTenant.Trial);
        trial.TrialExpiraEm = DateTime.UtcNow.AddDays(3);
        trial.VoucherUtilizado = "LANCAMENTO";
        var pago = NovoTenant("Pago", PlanoTipo.Pro, subscription: "sub_1");
        pago.CodigoDesconto = "lancamento";
        pago.DescontoPromocionalUsadoEm = DateTime.UtcNow.AddDays(-2);
        ctx.Tenants.AddRange(trial, pago, NovoTenant("Free"));
        ctx.Users.AddRange(NovoUsuario(trial.Id), NovoUsuario(trial.Id), NovoUsuario(pago.Id));
        await ctx.SaveChangesAsync();
        _stripe.Setup(s => s.ObterProximaCobrancaAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StripeProximaCobranca(50m, DateTime.UtcNow.AddDays(5)));

        var d = await CreateService(ctx).ObterAsync();

        Assert.Equal(1, d.Trial.Tenants);
        Assert.Equal(2, d.Trial.Usuarios);
        Assert.Equal("Trial", Assert.Single(d.Trial.Expirando).Nome);
        Assert.Equal(2, d.Cupons.TenantsComCodigo);
        Assert.Equal(3, d.Cupons.UsuariosComCodigo);
        Assert.Equal(1, d.Cupons.DescontoUsadoNoCheckout);
        var codigo = Assert.Single(d.Cupons.Codigos);
        Assert.Equal(("lancamento", 2, 1), (codigo.Codigo, codigo.Tenants, codigo.Pagantes));
    }

    [Fact]
    public async Task Perguntas_ContaPublicoEPendentesPelaSegmentacao()
    {
        using var ctx = CreateContext();
        var trial = NovoTenant("Trial", PlanoTipo.Plus, StatusTenant.Trial);
        var free = NovoTenant("Free");
        ctx.Tenants.AddRange(trial, free);
        var u1 = NovoUsuario(trial.Id);
        var u2 = NovoUsuario(trial.Id);
        ctx.Users.AddRange(u1, u2, NovoUsuario(free.Id));
        var pergunta = new Pergunta
        {
            Id = Guid.NewGuid(), Texto = "Gosta do trial?", Publico = PublicoPergunta.Advogados,
            Segmento = SegmentoPergunta.EmTrial, Ativa = true, CriadoEm = DateTime.UtcNow
        };
        ctx.Perguntas.AddRange(pergunta, new Pergunta
        {
            Id = Guid.NewGuid(), Texto = "Inativa", Publico = PublicoPergunta.Advogados,
            Segmento = SegmentoPergunta.Todos, Ativa = false, CriadoEm = DateTime.UtcNow
        });
        ctx.RespostasPerguntas.Add(new RespostaPergunta
        {
            Id = Guid.NewGuid(), PerguntaId = pergunta.Id, TenantId = trial.Id, RespondenteTipo = TipoRespondente.Usuario,
            RespondenteId = u1.Id, RespostaTexto = "sim", RespondidoEm = DateTime.UtcNow
        });
        await ctx.SaveChangesAsync();

        var p = (await CreateService(ctx).ObterAsync()).Perguntas;

        Assert.Equal(1, p.Ativas);
        Assert.Equal(2, p.UsuariosNoPublico);
        Assert.Equal(1, p.UsuariosComPendentes);
        Assert.Equal(1, p.Respostas7Dias);
        var resumo = Assert.Single(p.PorPergunta);
        Assert.Equal((2, 1), (resumo.Alcance, resumo.Respostas));
    }
}
