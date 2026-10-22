using System.Security.Claims;
using LegalManager.API.Controllers;
using LegalManager.Application.DTOs.Auth;
using LegalManager.Application.DTOs.Parceiros;
using LegalManager.Application.Interfaces;
using LegalManager.Domain.Entities;
using LegalManager.Domain.Enums;
using LegalManager.Infrastructure;
using LegalManager.Infrastructure.Persistence;
using LegalManager.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace LegalManager.UnitTests;

public class ParceirosTests
{
    private static AppDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static IConfiguration Config() => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["Jwt:Key"] = "meu-secret-key-minimo-32-caracteres-p",
        ["Jwt:Issuer"] = "LegalManager",
        ["Jwt:Audience"] = "LegalManager",
        ["App:FrontendUrl"] = "https://app.test"
    }).Build();

    private static Parceiro AddParceiro(AppDbContext ctx, string codigo = "crislane", decimal percentual = 20m,
        int? meses = null, bool ativo = true)
    {
        var parceiro = new Parceiro
        {
            Id = Guid.NewGuid(), Nome = "Crislane", Email = $"{codigo}@parceiro.test", Codigo = codigo,
            PercentualComissao = percentual, MesesComissao = meses, Ativo = ativo, CriadoEm = DateTime.UtcNow
        };
        ctx.Parceiros.Add(parceiro);
        ctx.SaveChanges();
        return parceiro;
    }

    private static Tenant AddTenant(AppDbContext ctx, Parceiro? parceiro, string? subscriptionId = "sub_1")
    {
        var tenant = new Tenant
        {
            Id = Guid.NewGuid(), Nome = "Escritório Alfa", Plano = PlanoTipo.Pro, Status = StatusTenant.Ativo,
            CriadoEm = DateTime.UtcNow, StripeSubscriptionId = subscriptionId, PeriodoBilling = "Mensal",
            ValorMensalAssinatura = subscriptionId is null ? null : 100m,
            ParceiroId = parceiro?.Id, ParceiroVinculadoEm = parceiro is null ? null : DateTime.UtcNow
        };
        ctx.Tenants.Add(tenant);
        ctx.SaveChanges();
        return tenant;
    }

    private static Faturamento NovoFaturamento(Tenant tenant, decimal valor, DateTime? pagoEm = null) => new()
    {
        Id = Guid.NewGuid(), TenantId = tenant.Id, BillingId = "sub_1", Periodo = "Mensal", Valor = valor,
        Status = StatusFaturamento.Pago, DataPagamento = pagoEm ?? DateTime.UtcNow, DataCriacao = DateTime.UtcNow,
        Descricao = "Renovação Pro Mensal"
    };

    private static ParceiroService CreateService(AppDbContext ctx, Mock<IEmailService>? email = null) =>
        new(ctx, new PasswordHasher<Parceiro>(), (email ?? new Mock<IEmailService>()).Object, Config(),
            NullLogger<ParceiroService>.Instance);

    // ── Cadastro do escritório ──────────────────────────────────────────────

    private static AuthService CreateAuthService(AppDbContext ctx)
    {
        var userManager = new Mock<UserManager<Usuario>>(new Mock<IUserStore<Usuario>>().Object, null!, null!, null!, null!, null!, null!, null!, null!);
        userManager.Setup(u => u.CreateAsync(It.IsAny<Usuario>(), It.IsAny<string>())).ReturnsAsync(IdentityResult.Success);
        userManager.Setup(u => u.AddToRoleAsync(It.IsAny<Usuario>(), It.IsAny<string>())).ReturnsAsync(IdentityResult.Success);
        return new AuthService(userManager.Object, Config(), new Mock<IEmailService>().Object,
            new Mock<ICreditoService>().Object, ctx, new Mock<IGoogleTokenValidator>().Object);
    }

    private static RegisterTenantDto Cadastro(string? codigoParceiro) =>
        new("Escritório Novo", null, "Admin", "admin@novo.test", "Senha@123", CodigoParceiro: codigoParceiro);

    [Fact]
    public async Task Cadastro_ComCodigoDeParceiro_VinculaOEscritorio()
    {
        var ctx = CreateContext();
        var parceiro = AddParceiro(ctx);

        await CreateAuthService(ctx).RegisterTenantAsync(Cadastro("  CRISLANE "));

        var tenant = await ctx.Tenants.SingleAsync();
        Assert.Equal(parceiro.Id, tenant.ParceiroId);
        Assert.NotNull(tenant.ParceiroVinculadoEm);
    }

    [Theory]
    [InlineData("nao-existe")]
    [InlineData("inativo")]
    public async Task Cadastro_ComCodigoInvalido_NaoBarraENaoVincula(string codigo)
    {
        var ctx = CreateContext();
        AddParceiro(ctx, "inativo", ativo: false);

        await CreateAuthService(ctx).RegisterTenantAsync(Cadastro(codigo));

        Assert.Null((await ctx.Tenants.SingleAsync()).ParceiroId);
    }

    // ── Regras de comissão ──────────────────────────────────────────────────

    [Fact]
    public async Task Registrar_CalculaComissaoComPercentualDoParceiro()
    {
        var ctx = CreateContext();
        var parceiro = AddParceiro(ctx, percentual: 15m);
        var tenant = AddTenant(ctx, parceiro);
        var fat = NovoFaturamento(tenant, 99.90m);

        var comissao = await ComissoesParceiros.RegistrarAsync(ctx, tenant, fat);
        await ctx.SaveChangesAsync();

        Assert.NotNull(comissao);
        Assert.Equal(14.99m, comissao.Valor); // 14,985 arredonda para cima
        Assert.Equal(15m, comissao.Percentual);
        Assert.Equal(99.90m, comissao.ValorPagamento);
        Assert.Equal("Escritório Alfa", comissao.NomeEscritorio);

        // Mesmo Faturamento não comissiona duas vezes.
        Assert.Null(await ComissoesParceiros.RegistrarAsync(ctx, tenant, fat));
    }

    [Fact]
    public async Task Registrar_SemParceiroOuParceiroInativo_NaoGeraComissao()
    {
        var ctx = CreateContext();
        var semParceiro = AddTenant(ctx, null);
        var inativo = AddTenant(ctx, AddParceiro(ctx, "x", ativo: false));

        Assert.Null(await ComissoesParceiros.RegistrarAsync(ctx, semParceiro, NovoFaturamento(semParceiro, 100m)));
        Assert.Null(await ComissoesParceiros.RegistrarAsync(ctx, inativo, NovoFaturamento(inativo, 100m)));
    }

    [Fact]
    public async Task Registrar_RespeitaJanelaDeMesesDesdeOPrimeiroPagamento()
    {
        var ctx = CreateContext();
        var parceiro = AddParceiro(ctx, meses: 3);
        var tenant = AddTenant(ctx, parceiro);
        var inicio = new DateTime(2026, 1, 10, 12, 0, 0, DateTimeKind.Utc);

        for (var mes = 0; mes < 5; mes++)
        {
            await ComissoesParceiros.RegistrarAsync(ctx, tenant, NovoFaturamento(tenant, 100m, inicio.AddMonths(mes)));
            await ctx.SaveChangesAsync();
        }

        // Jan, fev e mar comissionam; abr (início + 3 meses) em diante não.
        Assert.Equal(3, await ctx.ComissoesParceiros.CountAsync());
    }

    [Fact]
    public async Task WebhookRenovacao_GeraComissaoParaOParceiro()
    {
        var ctx = CreateContext();
        var parceiro = AddParceiro(ctx, percentual: 20m);
        var tenant = AddTenant(ctx, parceiro, "sub_parc");
        var body = """
        {
          "id": "evt_1", "object": "event", "type": "invoice.paid",
          "data": { "object": {
            "id": "in_1", "object": "invoice", "billing_reason": "subscription_cycle", "amount_paid": 12990,
            "parent": { "type": "subscription_details", "subscription_details": { "subscription": "sub_parc" } }
          } }
        }
        """;
        var http = new DefaultHttpContext();
        http.Request.Body = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(body));
        var webhook = new WebhookController(ctx, new PrecosPlanosService(ctx, new MemoryCache(new MemoryCacheOptions())),
            new Mock<IConfiguration>().Object, new Mock<ICreditoService>().Object, NullLogger<WebhookController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = http }
        };

        await webhook.Stripe(default);

        var fat = await ctx.Faturamentos.SingleAsync();
        var comissao = await ctx.ComissoesParceiros.SingleAsync();
        Assert.Equal(fat.Id, comissao.FaturamentoId);
        Assert.Equal(parceiro.Id, comissao.ParceiroId);
        Assert.Equal(tenant.Id, comissao.TenantId);
        Assert.Equal(25.98m, comissao.Valor);
    }

    // ── Conta do parceiro ───────────────────────────────────────────────────

    [Fact]
    public async Task Convite_DefinirSenha_ELogin()
    {
        var ctx = CreateContext();
        var parceiro = AddParceiro(ctx);
        var email = new Mock<IEmailService>();
        var service = CreateService(ctx, email);

        var link = await service.EnviarConviteAsync(parceiro);
        var token = Uri.UnescapeDataString(link.Link.Split("token=")[1]);

        email.Verify(e => e.EnviarConviteParceiroAsync(parceiro.Email, parceiro.Nome, "crislane", link.Link,
            It.IsAny<CancellationToken>()), Times.Once);
        Assert.NotEqual(token, (await ctx.Parceiros.SingleAsync()).TokenSenha); // só o hash fica salvo

        var auth = await service.DefinirSenhaAsync(new DefinirSenhaParceiroDto(token, "SenhaNova@1"));
        Assert.False(string.IsNullOrEmpty(auth.AccessToken));

        // O link é de uso único.
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.DefinirSenhaAsync(new DefinirSenhaParceiroDto(token, "OutraSenha@1")));

        var login = await service.LoginAsync(new LoginParceiroDto("CRISLANE@parceiro.test", "SenhaNova@1"));
        Assert.Equal(parceiro.Id, login.Parceiro.Id);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.LoginAsync(new LoginParceiroDto(parceiro.Email, "errada")));
    }

    [Fact]
    public async Task Login_ParceiroInativo_EhRecusado()
    {
        var ctx = CreateContext();
        var parceiro = AddParceiro(ctx);
        var service = CreateService(ctx);
        var token = Uri.UnescapeDataString((await service.EnviarConviteAsync(parceiro)).Link.Split("token=")[1]);
        await service.DefinirSenhaAsync(new DefinirSenhaParceiroDto(token, "SenhaNova@1"));

        parceiro.Ativo = false;
        await ctx.SaveChangesAsync();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.LoginAsync(new LoginParceiroDto(parceiro.Email, "SenhaNova@1")));
    }

    // ── Solicitação de pagamento ────────────────────────────────────────────

    private static async Task<(Parceiro Parceiro, Tenant Tenant)> ComComissoesAsync(AppDbContext ctx, params decimal[] pagamentos)
    {
        var parceiro = AddParceiro(ctx, percentual: 10m);
        var tenant = AddTenant(ctx, parceiro);
        foreach (var valor in pagamentos)
            await ComissoesParceiros.RegistrarAsync(ctx, tenant, NovoFaturamento(tenant, valor));
        await ctx.SaveChangesAsync();
        return (parceiro, tenant);
    }

    private static SuperAdminParceirosController CreateSuperAdmin(AppDbContext ctx) =>
        new(ctx, CreateService(ctx), new Mock<IAuditService>().Object)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        [new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString())], "test"))
                }
            }
        };

    [Fact]
    public async Task Solicitar_ReuneComissoesDisponiveis_EBloqueiaSegundaPendente()
    {
        var ctx = CreateContext();
        var (parceiro, _) = await ComComissoesAsync(ctx, 100m, 200m);
        var service = CreateService(ctx);

        var solicitacao = await service.SolicitarPagamentoAsync(parceiro.Id, new SolicitarPagamentoDto(" pix@crislane.test ", null));

        Assert.Equal(30m, solicitacao.Valor);
        Assert.Equal(2, solicitacao.QuantidadeComissoes);
        Assert.Equal("pix@crislane.test", (await ctx.Parceiros.SingleAsync()).ChavePix);
        var resumo = await service.GetResumoAsync(parceiro.Id);
        Assert.Equal(0m, resumo!.Disponivel);
        Assert.Equal(30m, resumo.Solicitado);
        Assert.True(resumo.SolicitacaoPendente);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.SolicitarPagamentoAsync(parceiro.Id, new SolicitarPagamentoDto("pix", null)));
        Assert.Contains("em análise", ex.Message);
    }

    [Fact]
    public async Task Solicitar_SemComissoes_EhRecusado()
    {
        var ctx = CreateContext();
        var parceiro = AddParceiro(ctx);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CreateService(ctx).SolicitarPagamentoAsync(parceiro.Id, new SolicitarPagamentoDto("pix", null)));
    }

    [Fact]
    public async Task SuperAdmin_MarcarPaga_MoveComissoesParaPago()
    {
        var ctx = CreateContext();
        var (parceiro, tenant) = await ComComissoesAsync(ctx, 100m);
        var service = CreateService(ctx);
        var solicitacao = await service.SolicitarPagamentoAsync(parceiro.Id, new SolicitarPagamentoDto("pix", null));

        // Pagamento novo depois da solicitação fica disponível para a próxima.
        await ComissoesParceiros.RegistrarAsync(ctx, tenant, NovoFaturamento(tenant, 50m));
        await ctx.SaveChangesAsync();

        Assert.IsType<NoContentResult>(await CreateSuperAdmin(ctx).MarcarPaga(solicitacao.Id, new ResolverSolicitacaoDto("E2E123"), default));

        var resumo = await service.GetResumoAsync(parceiro.Id);
        Assert.Equal(10m, resumo!.Pago);
        Assert.Equal(5m, resumo.Disponivel);
        Assert.False(resumo.SolicitacaoPendente);
        Assert.Equal("E2E123", (await ctx.SolicitacoesPagamentoParceiros.SingleAsync()).Resposta);

        Assert.IsType<BadRequestObjectResult>(await CreateSuperAdmin(ctx).MarcarPaga(solicitacao.Id, new ResolverSolicitacaoDto(null), default));
    }

    [Fact]
    public async Task SuperAdmin_Recusar_ExigeMotivoEDevolveComissoes()
    {
        var ctx = CreateContext();
        var (parceiro, _) = await ComComissoesAsync(ctx, 100m);
        var service = CreateService(ctx);
        var solicitacao = await service.SolicitarPagamentoAsync(parceiro.Id, new SolicitarPagamentoDto("pix", null));
        var controller = CreateSuperAdmin(ctx);

        Assert.IsType<BadRequestObjectResult>(await controller.Recusar(solicitacao.Id, new ResolverSolicitacaoDto(" "), default));
        Assert.IsType<NoContentResult>(await controller.Recusar(solicitacao.Id, new ResolverSolicitacaoDto("Chave Pix inválida"), default));

        var resumo = await service.GetResumoAsync(parceiro.Id);
        Assert.Equal(10m, resumo!.Disponivel);
        Assert.False(resumo.SolicitacaoPendente);
        Assert.Equal("Disponivel", (await service.ListarComissoesAsync(parceiro.Id)).Single().Situacao);

        // Pode pedir de novo.
        await service.SolicitarPagamentoAsync(parceiro.Id, new SolicitarPagamentoDto("pix-novo", null));
    }

    // ── Super admin: cadastro e vínculo ─────────────────────────────────────

    [Fact]
    public async Task SuperAdmin_Criar_NormalizaEEnviaConvite_EBarraCodigoRepetido()
    {
        var ctx = CreateContext();
        var controller = CreateSuperAdmin(ctx);
        var dto = new SalvarParceiroDto("Crislane", " Crislane@Parceiro.Test ", null, null, "CRIS-20", 20m, 12, null, null);

        Assert.IsType<OkObjectResult>(await controller.Criar(dto, default));
        var parceiro = await ctx.Parceiros.SingleAsync();
        Assert.Equal("cris-20", parceiro.Codigo);
        Assert.Equal("crislane@parceiro.test", parceiro.Email);
        Assert.NotNull(parceiro.TokenSenha);

        var conflito = await controller.Criar(dto with { Email = "outro@parceiro.test" }, default);
        Assert.Equal(409, Assert.IsAssignableFrom<ObjectResult>(conflito).StatusCode);
    }

    [Fact]
    public async Task SuperAdmin_VincularPorEmail_EExcluirSoSemHistorico()
    {
        var ctx = CreateContext();
        var parceiro = AddParceiro(ctx);
        var tenant = AddTenant(ctx, null);
        ctx.Users.Add(new Usuario
        {
            Id = Guid.NewGuid(), TenantId = tenant.Id, Nome = "Admin", Email = "admin@alfa.test",
            NormalizedEmail = "ADMIN@ALFA.TEST", UserName = "admin@alfa.test"
        });
        await ctx.SaveChangesAsync();
        var controller = CreateSuperAdmin(ctx);

        Assert.IsType<OkObjectResult>(await controller.VincularEscritorio(parceiro.Id, new VincularEscritorioDto("Admin@Alfa.test"), default));
        Assert.Equal(parceiro.Id, (await ctx.Tenants.SingleAsync()).ParceiroId);

        Assert.IsType<BadRequestObjectResult>(await controller.Excluir(parceiro.Id, default));

        Assert.IsType<NoContentResult>(await controller.DesvincularEscritorio(parceiro.Id, tenant.Id, default));
        Assert.IsType<NoContentResult>(await controller.Excluir(parceiro.Id, default));
    }

    [Fact]
    public async Task Escritorios_MostraSituacaoETotais()
    {
        var ctx = CreateContext();
        var (parceiro, tenant) = await ComComissoesAsync(ctx, 100m, 100m);
        var emTeste = AddTenant(ctx, parceiro, subscriptionId: null);
        emTeste.Status = StatusTenant.Trial;
        await ctx.SaveChangesAsync();

        var escritorios = await CreateService(ctx).ListarEscritoriosAsync(parceiro.Id);

        var assinante = escritorios.Single(e => e.TenantId == tenant.Id);
        Assert.Equal("Assinante", assinante.Situacao);
        Assert.Equal(200m, assinante.TotalPago);
        Assert.Equal(20m, assinante.TotalComissao);
        Assert.Equal("Em teste", escritorios.Single(e => e.TenantId == emTeste.Id).Situacao);
    }

    // ── Isolamento do token de parceiro ─────────────────────────────────────

    [Fact]
    public async Task PolicyPadrao_RecusaTokenDeParceiro()
    {
        var policy = ParceiroPortalController.PolicyPadrao();
        // Os requisitos da policy (usuário autenticado + asserção) são os próprios handlers.
        async Task<bool> Autoriza(string role)
        {
            var user = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Role, role)], "jwt"));
            var context = new AuthorizationHandlerContext(policy.Requirements, user, null);
            foreach (var handler in policy.Requirements.OfType<IAuthorizationHandler>())
                await handler.HandleAsync(context);
            return context.HasSucceeded;
        }

        Assert.False(await Autoriza(ParceiroService.Role));
        Assert.True(await Autoriza("Admin"));
    }
}
