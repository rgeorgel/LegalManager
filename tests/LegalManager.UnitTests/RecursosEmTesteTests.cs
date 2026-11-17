using System.Security.Claims;
using LegalManager.API.Controllers;
using LegalManager.API.Filters;
using LegalManager.Application.DTOs.SuperAdmin;
using LegalManager.Application.Interfaces;
using LegalManager.Domain;
using LegalManager.Domain.Entities;
using LegalManager.Domain.Enums;
using LegalManager.Infrastructure.Persistence;
using LegalManager.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace LegalManager.UnitTests;

// Recursos em teste: o super admin libera por escritório; quem não está liberado não vê telas nem API.
public class RecursosEmTesteTests
{
    private const string Assinatura = RecursosEmTeste.AssinaturaDigital;

    private static AppDbContext CriarDb() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static Tenant NovoTenant(AppDbContext db, params string[] recursos)
    {
        var t = new Tenant
        {
            Id = Guid.NewGuid(), Nome = "Escritório", Plano = PlanoTipo.Pro, Status = StatusTenant.Ativo,
            CriadoEm = DateTime.UtcNow, RecursosLiberados = [.. recursos],
        };
        db.Tenants.Add(t);
        db.SaveChanges();
        return t;
    }

    private static ClaimsPrincipal Login(Guid? tenantId) => new(new ClaimsIdentity(
        tenantId is { } id ? [new Claim("tenantId", id.ToString())] : [], "test"));

    // ── Regra central ───────────────────────────────────────────────────────────────

    [Fact]
    public void Liberado_SoComAChaveNoTenant()
    {
        Assert.True(RecursosEmTeste.Liberado(new Tenant { RecursosLiberados = [Assinatura] }, Assinatura));
        Assert.False(RecursosEmTeste.Liberado(new Tenant(), Assinatura));
        Assert.False(RecursosEmTeste.Liberado(null, Assinatura));
    }

    [Fact]
    public void DoTenant_IgnoraChavesForaDoCatalogo()
    {
        var t = new Tenant { RecursosLiberados = ["recurso-antigo", Assinatura] };
        Assert.Equal([Assinatura], RecursosEmTeste.DoTenant(t));
        Assert.Empty(RecursosEmTeste.DoTenant(null));
        Assert.True(RecursosEmTeste.Existe(Assinatura));
        Assert.False(RecursosEmTeste.Existe("recurso-antigo"));
        Assert.False(RecursosEmTeste.Existe(null));
    }

    // ── Filtro da API ───────────────────────────────────────────────────────────────

    private static async Task<(IActionResult? Resultado, bool Executou)> RodarFiltro(
        AppDbContext db, Guid? tenantId, bool anonimo = false)
    {
        var http = new DefaultHttpContext
        {
            User = Login(tenantId),
            RequestServices = new ServiceCollection().AddSingleton(db).BuildServiceProvider(),
        };
        var descritor = new ActionDescriptor { EndpointMetadata = anonimo ? [new AllowAnonymousAttribute()] : [] };
        var contexto = new ActionExecutingContext(
            new ActionContext(http, new RouteData(), descritor), [], new Dictionary<string, object?>(), new object());
        var executou = false;
        await new ExigeRecursoAttribute(Assinatura).OnActionExecutionAsync(contexto, () =>
        {
            executou = true;
            return Task.FromResult(new ActionExecutedContext(contexto, [], new object()));
        });
        return (contexto.Result, executou);
    }

    [Fact]
    public async Task Filtro_TenantLiberado_Segue()
    {
        using var db = CriarDb();
        var t = NovoTenant(db, Assinatura);

        var (resultado, executou) = await RodarFiltro(db, t.Id);

        Assert.True(executou);
        Assert.Null(resultado);
    }

    [Fact]
    public async Task Filtro_TenantNaoLiberado_Responde404()
    {
        using var db = CriarDb();
        var t = NovoTenant(db);

        var (resultado, executou) = await RodarFiltro(db, t.Id);

        Assert.False(executou);
        Assert.IsType<NotFoundObjectResult>(resultado);
    }

    [Fact]
    public async Task Filtro_SemTenantNoLogin_Responde404()
    {
        using var db = CriarDb();
        NovoTenant(db, Assinatura);

        var (resultado, executou) = await RodarFiltro(db, null);

        Assert.False(executou);
        Assert.IsType<NotFoundObjectResult>(resultado);
    }

    [Fact]
    public async Task Filtro_AcaoAnonima_PassaDireto()
    {
        using var db = CriarDb();

        var (resultado, executou) = await RodarFiltro(db, null, anonimo: true);

        Assert.True(executou);
        Assert.Null(resultado);
    }

    [Fact]
    public void ControllersDaAssinatura_ExigemORecurso_EOWebhookEhAnonimo()
    {
        foreach (var tipo in new[] { typeof(AssinaturaDigitalController), typeof(PortalClienteAssinaturasController) })
        {
            var attr = Assert.Single(tipo.GetCustomAttributes(typeof(ExigeRecursoAttribute), true).Cast<ExigeRecursoAttribute>());
            Assert.Equal(Assinatura, attr.Recurso);
        }
        var webhook = typeof(AssinaturaDigitalController).GetMethod(nameof(AssinaturaDigitalController.Webhook))!;
        Assert.NotEmpty(webhook.GetCustomAttributes(typeof(AllowAnonymousAttribute), true));
    }

    // ── GET /api/recursos ───────────────────────────────────────────────────────────

    private static RecursosController RecursosCom(AppDbContext db, Guid? tenantId) => new(db)
    {
        ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = Login(tenantId) } },
    };

    [Fact]
    public async Task Recursos_ListaOsLiberadosDoEscritorio()
    {
        using var db = CriarDb();
        var liberado = NovoTenant(db, Assinatura);
        var outro = NovoTenant(db);

        var r1 = Assert.IsType<OkObjectResult>((await RecursosCom(db, liberado.Id).Listar(default)).Result);
        var r2 = Assert.IsType<OkObjectResult>((await RecursosCom(db, outro.Id).Listar(default)).Result);
        var r3 = Assert.IsType<OkObjectResult>((await RecursosCom(db, null).Listar(default)).Result);

        Assert.Equal([Assinatura], Assert.IsAssignableFrom<IEnumerable<string>>(r1.Value));
        Assert.Empty(Assert.IsAssignableFrom<IEnumerable<string>>(r2.Value));
        Assert.Empty(Assert.IsAssignableFrom<IEnumerable<string>>(r3.Value));
    }

    // ── Super Admin ─────────────────────────────────────────────────────────────────

    private static SuperAdminController SuperAdmin(AppDbContext db, out Mock<IAuditService> audit)
    {
        audit = new Mock<IAuditService>();
        var config = new Mock<IConfiguration>();
        config.Setup(c => c.GetSection("Jwt")).Returns(new Mock<IConfigurationSection>().Object);
        var auth = new AuthService(
            new Mock<UserManager<Usuario>>(new Mock<IUserStore<Usuario>>().Object, null!, null!, null!, null!, null!, null!, null!, null!).Object,
            config.Object, new Mock<IEmailService>().Object, new Mock<ICreditoService>().Object, db, new Mock<IGoogleTokenValidator>().Object);
        return new SuperAdminController(db, audit.Object, auth, new Mock<ITenantExportService>().Object,
            new Mock<ITenantImportService>().Object, new Mock<ITenantDeletionService>().Object)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString())], "test")),
                },
            },
        };
    }

    [Fact]
    public async Task SuperAdmin_LiberaERetira_ComAuditoria()
    {
        using var db = CriarDb();
        var t = NovoTenant(db);
        var controller = SuperAdmin(db, out var audit);

        Assert.IsType<NoContentResult>(await controller.AlterarRecursoEmTeste(t.Id, Assinatura, new AlterarRecursoEmTesteDto(true), default));
        Assert.Equal([Assinatura], (await db.Tenants.AsNoTracking().SingleAsync(x => x.Id == t.Id)).RecursosLiberados);

        Assert.IsType<NoContentResult>(await controller.AlterarRecursoEmTeste(t.Id, Assinatura, new AlterarRecursoEmTesteDto(false), default));
        Assert.Empty((await db.Tenants.AsNoTracking().SingleAsync(x => x.Id == t.Id)).RecursosLiberados);

        audit.Verify(a => a.LogAsync(It.Is<AuditLogEntry>(e => e.Acao == TimelineAcoes.RecursoEmTesteAlterado && e.TenantId == t.Id),
            It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task SuperAdmin_SemMudanca_NaoAudita()
    {
        using var db = CriarDb();
        var t = NovoTenant(db, Assinatura);
        var controller = SuperAdmin(db, out var audit);

        Assert.IsType<NoContentResult>(await controller.AlterarRecursoEmTeste(t.Id, Assinatura, new AlterarRecursoEmTesteDto(true), default));

        audit.Verify(a => a.LogAsync(It.IsAny<AuditLogEntry>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SuperAdmin_RecursoDesconhecidoOuTenantInexistente()
    {
        using var db = CriarDb();
        var t = NovoTenant(db);
        var controller = SuperAdmin(db, out _);

        Assert.IsType<BadRequestObjectResult>(await controller.AlterarRecursoEmTeste(t.Id, "nao-existe", new AlterarRecursoEmTesteDto(true), default));
        Assert.IsType<NotFoundResult>(await controller.AlterarRecursoEmTeste(Guid.NewGuid(), Assinatura, new AlterarRecursoEmTesteDto(true), default));
    }

    [Fact]
    public async Task SuperAdmin_DetalheMostraOsRecursosEmTeste()
    {
        using var db = CriarDb();
        var t = NovoTenant(db, Assinatura);

        var dto = Assert.IsType<TenantDetailDto>(Assert.IsType<OkObjectResult>(await SuperAdmin(db, out _).GetTenant(t.Id, default)).Value);

        var recurso = Assert.Single(dto.RecursosEmTeste!);
        Assert.Equal(Assinatura, recurso.Chave);
        Assert.True(recurso.Liberado);
    }
}
