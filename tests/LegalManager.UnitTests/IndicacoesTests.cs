using LegalManager.API.Controllers;
using LegalManager.Application.DTOs.Auth;
using LegalManager.Application.DTOs.Parceiros;
using LegalManager.Application.Interfaces;
using LegalManager.Domain;
using LegalManager.Domain.Entities;
using LegalManager.Domain.Enums;
using LegalManager.Domain.Interfaces;
using LegalManager.Infrastructure;
using LegalManager.Infrastructure.Persistence;
using LegalManager.Infrastructure.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace LegalManager.UnitTests;

// "Indique e ganhe": o usuário do escritório vira um Parceiro (ProgramaIndicacao) e usa as mesmas
// regras de comissão e de pagamento do portal do parceiro.
public class IndicacoesTests
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

    private static ParceiroService CreateService(AppDbContext ctx) =>
        new(ctx, new PasswordHasher<Parceiro>(), new Mock<IEmailService>().Object, Config(), NullLogger<ParceiroService>.Instance);

    private static (Tenant tenant, Usuario usuario) AddEscritorioComUsuario(AppDbContext ctx,
        string nome = "José da Silva", string email = "Jose@Escritorio.test")
    {
        var tenant = new Tenant
        {
            Id = Guid.NewGuid(), Nome = "Silva Advogados", Plano = PlanoTipo.Pro, Status = StatusTenant.Ativo,
            CriadoEm = DateTime.UtcNow, StripeSubscriptionId = "sub_origem", ValorMensalAssinatura = 100m
        };
        var usuario = new Usuario
        {
            Id = Guid.NewGuid(), TenantId = tenant.Id, Nome = nome, Email = email, UserName = email,
            Ativo = true, CriadoEm = DateTime.UtcNow
        };
        ctx.Tenants.Add(tenant);
        ctx.Users.Add(usuario);
        ctx.SaveChanges();
        return (tenant, usuario);
    }

    private static IndicacoesController CreateController(AppDbContext ctx, Guid userId, Guid tenantId, Guid? impersonadoPor = null)
    {
        var tenant = new Mock<ITenantContext>();
        tenant.Setup(t => t.UserId).Returns(userId);
        tenant.Setup(t => t.TenantId).Returns(tenantId);
        tenant.Setup(t => t.ImpersonadoPorId).Returns(impersonadoPor);
        return new IndicacoesController(CreateService(ctx), tenant.Object)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
    }

    private static T Valor<T>(ActionResult<T> resultado) =>
        Assert.IsType<T>(Assert.IsType<OkObjectResult>(resultado.Result).Value);

    private static Faturamento Pagamento(Tenant tenant, decimal valor) => new()
    {
        Id = Guid.NewGuid(), TenantId = tenant.Id, BillingId = "sub_x", Periodo = "Mensal", Valor = valor,
        Status = StatusFaturamento.Pago, DataPagamento = DateTime.UtcNow, DataCriacao = DateTime.UtcNow,
        Descricao = "Assinatura Pro Mensal"
    };

    private static AuthService CreateAuthService(AppDbContext ctx)
    {
        var userManager = new Mock<UserManager<Usuario>>(new Mock<IUserStore<Usuario>>().Object, null!, null!, null!, null!, null!, null!, null!, null!);
        userManager.Setup(u => u.CreateAsync(It.IsAny<Usuario>(), It.IsAny<string>())).ReturnsAsync(IdentityResult.Success);
        userManager.Setup(u => u.AddToRoleAsync(It.IsAny<Usuario>(), It.IsAny<string>())).ReturnsAsync(IdentityResult.Success);
        return new AuthService(userManager.Object, Config(), new Mock<IEmailService>().Object,
            new Mock<ICreditoService>().Object, ctx, new Mock<IGoogleTokenValidator>().Object);
    }

    // ── Participar ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Status_AntesDeParticipar_MostraAsRegrasDoPrograma()
    {
        var ctx = CreateContext();
        var (tenant, usuario) = AddEscritorioComUsuario(ctx);

        var status = Valor(await CreateController(ctx, usuario.Id, tenant.Id).Status(default));

        Assert.False(status.Participando);
        Assert.Equal(25m, status.PercentualComissao);
        Assert.Equal(ProgramaIndicacao.MesesComissao, status.MesesComissao);
        Assert.Null(status.Resumo);
        Assert.Empty(await ctx.Parceiros.ToListAsync());
    }

    [Fact]
    public async Task Participar_CriaParceiroDoUsuarioCom25PorCento_EIdempotente()
    {
        var ctx = CreateContext();
        var (tenant, usuario) = AddEscritorioComUsuario(ctx);
        var controller = CreateController(ctx, usuario.Id, tenant.Id);

        var status = Valor(await controller.Participar(default));

        var parceiro = await ctx.Parceiros.SingleAsync();
        Assert.True(status.Participando);
        Assert.True(status.Ativo);
        Assert.Equal(usuario.Id, parceiro.UsuarioId);
        Assert.Equal(tenant.Id, parceiro.TenantOrigemId);
        Assert.Equal(25m, parceiro.PercentualComissao);
        Assert.Null(parceiro.MesesComissao);
        Assert.Equal("jose@escritorio.test", parceiro.Email);
        Assert.Equal("José da Silva", parceiro.Nome);
        Assert.Null(parceiro.SenhaHash);
        Assert.Matches("^jose-[a-z0-9]{4}$", parceiro.Codigo);
        Assert.Equal(parceiro.Codigo, status.Resumo!.Codigo);

        // Segunda vez devolve o mesmo cadastro.
        var denovo = Valor(await controller.Participar(default));
        Assert.Equal(parceiro.Codigo, denovo.Resumo!.Codigo);
        Assert.Equal(1, await ctx.Parceiros.CountAsync());
    }

    [Fact]
    public async Task Participar_ComEmailDeParceiroComercial_Recusa()
    {
        var ctx = CreateContext();
        var (tenant, usuario) = AddEscritorioComUsuario(ctx);
        ctx.Parceiros.Add(new Parceiro
        {
            Id = Guid.NewGuid(), Nome = "José", Email = "jose@escritorio.test", Codigo = "jose", PercentualComissao = 20m,
            CriadoEm = DateTime.UtcNow
        });
        await ctx.SaveChangesAsync();

        var resultado = await CreateController(ctx, usuario.Id, tenant.Id).Participar(default);

        Assert.IsType<BadRequestObjectResult>(resultado.Result);
        Assert.Equal(1, await ctx.Parceiros.CountAsync());
    }

    [Fact]
    public async Task Participar_UsuarioInexistente_Recusa()
    {
        var ctx = CreateContext();

        var resultado = await CreateController(ctx, Guid.NewGuid(), Guid.NewGuid()).Participar(default);

        Assert.IsType<BadRequestObjectResult>(resultado.Result);
    }

    [Fact]
    public async Task AcessoComoSuporte_NaoParticipaNemSolicita()
    {
        var ctx = CreateContext();
        var (tenant, usuario) = AddEscritorioComUsuario(ctx);
        var controller = CreateController(ctx, usuario.Id, tenant.Id, impersonadoPor: Guid.NewGuid());

        var participar = await controller.Participar(default);
        var solicitar = await controller.Solicitar(new SolicitarPagamentoDto("pix@x.test", null), default);

        Assert.Equal(403, Assert.IsType<ObjectResult>(participar.Result).StatusCode);
        Assert.Equal(403, Assert.IsType<ObjectResult>(solicitar.Result).StatusCode);
        Assert.Empty(await ctx.Parceiros.ToListAsync());
    }

    [Fact]
    public async Task SemParticipar_ListasVaziasESolicitarRecusa()
    {
        var ctx = CreateContext();
        var (tenant, usuario) = AddEscritorioComUsuario(ctx);
        var controller = CreateController(ctx, usuario.Id, tenant.Id);

        Assert.Empty(Valor(await controller.Escritorios(default)));
        Assert.Empty(Valor(await controller.Comissoes(default)));
        Assert.Empty(Valor(await controller.Solicitacoes(default)));
        var solicitar = await controller.Solicitar(new SolicitarPagamentoDto("pix@x.test", null), default);
        Assert.IsType<BadRequestObjectResult>(solicitar.Result);
    }

    // ── Fluxo completo ──────────────────────────────────────────────────────

    [Fact]
    public async Task EscritorioIndicadoAssina_UsuarioRecebe25PorCento_ESolicitaPagamento()
    {
        var ctx = CreateContext();
        var (tenant, usuario) = AddEscritorioComUsuario(ctx);
        var controller = CreateController(ctx, usuario.Id, tenant.Id);
        var codigo = Valor(await controller.Participar(default)).Resumo!.Codigo;

        // Outro escritório se cadastra pelo link do usuário...
        await CreateAuthService(ctx).RegisterTenantAsync(
            new RegisterTenantDto("Escritório Indicado", null, "Maria", "maria@indicado.test", "Senha@123",
                CodigoParceiro: codigo.ToUpperInvariant()));
        var indicado = await ctx.Tenants.SingleAsync(t => t.Nome == "Escritório Indicado");
        Assert.NotNull(indicado.ParceiroId);

        // ...e paga a assinatura.
        var comissao = await ComissoesParceiros.RegistrarAsync(ctx, indicado, Pagamento(indicado, 199.90m));
        await ctx.SaveChangesAsync();
        Assert.NotNull(comissao);
        Assert.Equal(49.98m, comissao.Valor); // 25% de 199,90 = 49,975 → 49,98
        Assert.Equal(25m, comissao.Percentual);

        var status = Valor(await controller.Status(default));
        Assert.Equal(1, status.Resumo!.Escritorios);
        Assert.Equal(49.98m, status.Resumo.Disponivel);
        Assert.Single(Valor(await controller.Escritorios(default)), e => e.Nome == "Escritório Indicado");
        Assert.Single(Valor(await controller.Comissoes(default)));

        var solicitacao = Valor(await controller.Solicitar(new SolicitarPagamentoDto(" pix@jose.test ", "NF em nome do escritório"), default));
        Assert.Equal(49.98m, solicitacao.Valor);
        Assert.Equal("Pendente", solicitacao.Status);
        Assert.Equal("pix@jose.test", (await ctx.Parceiros.SingleAsync()).ChavePix);
        Assert.Single(Valor(await controller.Solicitacoes(default)));

        // Segunda solicitação com uma pendente é recusada (regra do portal do parceiro).
        Assert.IsType<BadRequestObjectResult>(
            (await controller.Solicitar(new SolicitarPagamentoDto("pix@jose.test", null), default)).Result);
    }

    [Fact]
    public async Task ProprioEscritorio_NaoComissiona()
    {
        var ctx = CreateContext();
        var (tenant, usuario) = AddEscritorioComUsuario(ctx);
        await CreateService(ctx).ParticiparIndicacoesAsync(usuario.Id);
        // Ex.: super admin vinculou por engano o escritório do próprio usuário.
        tenant.ParceiroId = (await ctx.Parceiros.SingleAsync()).Id;
        await ctx.SaveChangesAsync();

        Assert.Null(await ComissoesParceiros.RegistrarAsync(ctx, tenant, Pagamento(tenant, 100m)));
    }

    [Fact]
    public async Task SuperAdmin_VeQueOParceiroEUsuarioDeEscritorio()
    {
        var ctx = CreateContext();
        var (tenant, usuario) = AddEscritorioComUsuario(ctx);
        await CreateService(ctx).ParticiparIndicacoesAsync(usuario.Id);
        var superAdmin = new SuperAdminParceirosController(ctx, CreateService(ctx), new Mock<IAuditService>().Object)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };

        var lista = Valor(await superAdmin.Listar(default));

        var dto = Assert.Single(lista);
        Assert.Equal(usuario.Id, dto.UsuarioId);
        Assert.Equal("Silva Advogados", dto.EscritorioOrigem);
        Assert.Equal(25m, dto.PercentualComissao);
    }

    [Theory]
    [InlineData("José da Silva", "jose")]
    [InlineData("  ANA  Paula ", "ana")]
    [InlineData("Conceição", "conceicao")]
    [InlineData("O'Neil", "oneil")]
    [InlineData("", "causify")]
    [InlineData(null, "causify")]
    [InlineData("Ü", "causify")]
    [InlineData("Maximilianoooooooooooooo", "maximilianoooooooooo")]
    public void Slug_UsaOPrimeiroNomeSemAcento(string? nome, string esperado) =>
        Assert.Equal(esperado, ParceiroService.Slug(nome));
}
