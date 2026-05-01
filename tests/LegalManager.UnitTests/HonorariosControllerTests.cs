using LegalManager.API.Controllers;
using LegalManager.Domain.Entities;
using LegalManager.Domain.Enums;
using LegalManager.Domain.Interfaces;
using LegalManager.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Moq;
using System.Text.Json;

namespace LegalManager.UnitTests;

public class HonorariosControllerTests
{
    private static AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options);
    }

    private static ITenantContext CreateTenantContext(Guid tenantId, Guid userId)
    {
        var mock = new Mock<ITenantContext>();
        mock.Setup(t => t.TenantId).Returns(tenantId);
        mock.Setup(t => t.UserId).Returns(userId);
        return mock.Object;
    }

    private static async Task<(AppDbContext ctx, Tenant tenant, Usuario usuario)> SeedTenantAsync()
    {
        var ctx = CreateContext();
        var tenant = new Tenant
        {
            Id = Guid.NewGuid(),
            Nome = "Escritório Teste",
            Plano = PlanoTipo.Free,
            Status = StatusTenant.Trial,
            CriadoEm = DateTime.UtcNow
        };
        ctx.Tenants.Add(tenant);

        var usuario = new Usuario
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.Id,
            Nome = "Advogado Teste",
            Email = "adv@teste.com",
            UserName = "adv@teste.com",
            Perfil = PerfilUsuario.Admin,
            Ativo = true,
            CriadoEm = DateTime.UtcNow
        };
        ctx.Users.Add(usuario);
        await ctx.SaveChangesAsync();

        return (ctx, tenant, usuario);
    }

    private static HonorarioCalculo MakeCalculo(Guid tenantId, Guid usuarioId, DateTime? criadoEm = null) =>
        new()
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            UsuarioId = usuarioId,
            CriadoEm = criadoEm ?? DateTime.UtcNow,
            Cliente = "Cliente Teste",
            Mode = "j",
            Area = "Cível",
            Tipo = "Procedimento Ordinário",
            ItemOAB = "4.1",
            ValorCausa = 50000m,
            HonorarioBase = 10000m,
            HonorarioAjustado = 13000m,
            TotalCorrigido = 13500m,
            Multiplicador = 1.3m,
            TaxaCorrecao = 0.42m,
            FormaPagamento = "o",
            Complexidade = "Média",
            Risco = "Baixo",
            Urgencia = "Normal",
            Capacidade = "Média",
            Exito = "Provável",
            StateJson = "{}"
        };

    private static readonly JsonSerializerOptions _jsonOpts =
        new() { PropertyNameCaseInsensitive = true };

    // Helper para extrair lista de propriedade de um OkObjectResult com tipo anônimo
    private static List<T> ExtractProperty<T>(OkObjectResult ok, string propertyName)
    {
        var json = JsonSerializer.Serialize(ok.Value);
        var docs = JsonDocument.Parse(json).RootElement;
        return docs.EnumerateArray()
            .Select(e => e.GetProperty(propertyName).Deserialize<T>(_jsonOpts)!)
            .ToList();
    }

    // ── GET ───────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetHistorico_DeveRetornarListaVazia_QuandoNaoHaDados()
    {
        var (ctx, tenant, usuario) = await SeedTenantAsync();
        var controller = new HonorariosController(ctx, CreateTenantContext(tenant.Id, usuario.Id));

        var result = await controller.GetHistorico(default);

        var ok = Assert.IsType<OkObjectResult>(result);
        var json = JsonSerializer.Serialize(ok.Value);
        var arr = JsonDocument.Parse(json).RootElement;
        Assert.Equal(0, arr.GetArrayLength());
    }

    [Fact]
    public async Task GetHistorico_DeveRetornarApenasDadosDoUsuarioDoTenant()
    {
        var (ctx, tenant, usuario) = await SeedTenantAsync();
        ctx.HonorariosCalculos.Add(MakeCalculo(tenant.Id, usuario.Id));
        ctx.HonorariosCalculos.Add(MakeCalculo(tenant.Id, usuario.Id));
        await ctx.SaveChangesAsync();

        var controller = new HonorariosController(ctx, CreateTenantContext(tenant.Id, usuario.Id));

        var result = await controller.GetHistorico(default);

        var ok = Assert.IsType<OkObjectResult>(result);
        var json = JsonSerializer.Serialize(ok.Value);
        Assert.Equal(2, JsonDocument.Parse(json).RootElement.GetArrayLength());
    }

    [Fact]
    public async Task GetHistorico_NaoDeveRetornarDadosDeOutroTenant()
    {
        var (ctx, tenant, usuario) = await SeedTenantAsync();

        // Outro tenant/usuário
        var outroTenantId = Guid.NewGuid();
        var outroUsuarioId = Guid.NewGuid();
        ctx.HonorariosCalculos.Add(MakeCalculo(outroTenantId, outroUsuarioId));
        await ctx.SaveChangesAsync();

        var controller = new HonorariosController(ctx, CreateTenantContext(tenant.Id, usuario.Id));

        var result = await controller.GetHistorico(default);

        var ok = Assert.IsType<OkObjectResult>(result);
        var json = JsonSerializer.Serialize(ok.Value);
        Assert.Equal(0, JsonDocument.Parse(json).RootElement.GetArrayLength());
    }

    [Fact]
    public async Task GetHistorico_DeveOrdenarPorDataDecrescente()
    {
        var (ctx, tenant, usuario) = await SeedTenantAsync();

        var base_dt = DateTime.UtcNow;
        var calculo1 = MakeCalculo(tenant.Id, usuario.Id, base_dt.AddDays(-2));
        calculo1.Cliente = "Primeiro salvo";
        var calculo2 = MakeCalculo(tenant.Id, usuario.Id, base_dt.AddDays(-1));
        calculo2.Cliente = "Segundo salvo";
        var calculo3 = MakeCalculo(tenant.Id, usuario.Id, base_dt);
        calculo3.Cliente = "Terceiro salvo";

        ctx.HonorariosCalculos.AddRange(calculo1, calculo2, calculo3);
        await ctx.SaveChangesAsync();

        var controller = new HonorariosController(ctx, CreateTenantContext(tenant.Id, usuario.Id));

        var result = await controller.GetHistorico(default);

        var ok = Assert.IsType<OkObjectResult>(result);
        var clientes = ExtractProperty<string>(ok, "Cliente");

        Assert.Equal("Terceiro salvo", clientes[0]);
        Assert.Equal("Segundo salvo", clientes[1]);
        Assert.Equal("Primeiro salvo", clientes[2]);
    }

    [Fact]
    public async Task GetHistorico_DeveRetornarMaximo15Registros()
    {
        var (ctx, tenant, usuario) = await SeedTenantAsync();

        for (int i = 0; i < 20; i++)
            ctx.HonorariosCalculos.Add(MakeCalculo(tenant.Id, usuario.Id));

        await ctx.SaveChangesAsync();

        var controller = new HonorariosController(ctx, CreateTenantContext(tenant.Id, usuario.Id));

        var result = await controller.GetHistorico(default);

        var ok = Assert.IsType<OkObjectResult>(result);
        var json = JsonSerializer.Serialize(ok.Value);
        Assert.Equal(15, JsonDocument.Parse(json).RootElement.GetArrayLength());
    }

    // ── POST ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task SalvarHistorico_DeveCriarRegistro_QuandoDadosValidos()
    {
        var (ctx, tenant, usuario) = await SeedTenantAsync();
        var controller = new HonorariosController(ctx, CreateTenantContext(tenant.Id, usuario.Id));

        var dto = new SalvarHistoricoDto(
            Cliente: "João Silva",
            Mode: "j",
            Area: "Cível",
            Tipo: "Procedimento Ordinário (proposição/defesa)",
            ItemOAB: "4.1",
            ValorCausa: 50000m,
            HonorarioBase: 10000m,
            HonorarioAjustado: 13000m,
            TotalCorrigido: 13500m,
            Multiplicador: 1.3m,
            TaxaCorrecao: 0.42m,
            FormaPagamento: "o",
            Complexidade: "Média",
            Risco: "Baixo",
            Urgencia: "Normal",
            Capacidade: "Média",
            Exito: "Provável",
            StateJson: "{\"mode\":\"j\"}"
        );

        var result = await controller.SalvarHistorico(dto, default);

        Assert.IsType<OkObjectResult>(result);
        Assert.Equal(1, await ctx.HonorariosCalculos.CountAsync());

        var salvo = await ctx.HonorariosCalculos.FirstAsync();
        Assert.Equal(tenant.Id, salvo.TenantId);
        Assert.Equal(usuario.Id, salvo.UsuarioId);
        Assert.Equal("João Silva", salvo.Cliente);
        Assert.Equal("j", salvo.Mode);
        Assert.Equal("4.1", salvo.ItemOAB);
        Assert.Equal(13000m, salvo.HonorarioAjustado);
    }

    [Fact]
    public async Task SalvarHistorico_DeveRemoverMaisAntigo_QuandoAtinge15Registros()
    {
        var (ctx, tenant, usuario) = await SeedTenantAsync();

        var base_dt = DateTime.UtcNow;
        for (int i = 0; i < 15; i++)
        {
            var c = MakeCalculo(tenant.Id, usuario.Id, base_dt.AddMinutes(i));
            c.Cliente = $"Cliente {i}";
            ctx.HonorariosCalculos.Add(c);
        }
        await ctx.SaveChangesAsync();

        var controller = new HonorariosController(ctx, CreateTenantContext(tenant.Id, usuario.Id));

        var dto = new SalvarHistoricoDto(
            Cliente: "Cliente Novo",
            Mode: "e", Area: "Administrativo", Tipo: "Ação Administrativa",
            ItemOAB: "2.3e", ValorCausa: null,
            HonorarioBase: 10427m, HonorarioAjustado: 10427m, TotalCorrigido: 10427m,
            Multiplicador: 1m, TaxaCorrecao: 0m,
            FormaPagamento: "v",
            Complexidade: "Baixa", Risco: "Baixo", Urgencia: "Normal",
            Capacidade: "Média", Exito: "Provável",
            StateJson: "{}"
        );

        await controller.SalvarHistorico(dto, default);

        // Ainda deve ter exatamente 15
        Assert.Equal(15, await ctx.HonorariosCalculos.CountAsync());

        // O mais antigo foi removido, o novo está presente
        Assert.False(await ctx.HonorariosCalculos.AnyAsync(h => h.Cliente == "Cliente 0"));
        Assert.True(await ctx.HonorariosCalculos.AnyAsync(h => h.Cliente == "Cliente Novo"));
    }

    [Fact]
    public async Task SalvarHistorico_DeveIsollarPorTenantEUsuario()
    {
        var (ctx, tenant, usuario) = await SeedTenantAsync();

        // Outro usuário no mesmo tenant já tem 15 registros
        var outroUserId = Guid.NewGuid();
        for (int i = 0; i < 15; i++)
            ctx.HonorariosCalculos.Add(MakeCalculo(tenant.Id, outroUserId));
        await ctx.SaveChangesAsync();

        var controller = new HonorariosController(ctx, CreateTenantContext(tenant.Id, usuario.Id));

        var dto = new SalvarHistoricoDto(
            Cliente: "Meu cliente", Mode: "j", Area: "Cível",
            Tipo: "Ação", ItemOAB: "4.1", ValorCausa: 10000m,
            HonorarioBase: 6256m, HonorarioAjustado: 6256m, TotalCorrigido: 6256m,
            Multiplicador: 1m, TaxaCorrecao: 0m, FormaPagamento: "v",
            Complexidade: "Baixa", Risco: "Baixo", Urgencia: "Normal",
            Capacidade: "Média", Exito: "Provável", StateJson: "{}"
        );

        await controller.SalvarHistorico(dto, default);

        // O usuário atual tem 1; o outro ainda tem 15 — total = 16
        Assert.Equal(16, await ctx.HonorariosCalculos.CountAsync());
        Assert.Equal(1, await ctx.HonorariosCalculos.CountAsync(h => h.UsuarioId == usuario.Id));
        Assert.Equal(15, await ctx.HonorariosCalculos.CountAsync(h => h.UsuarioId == outroUserId));
    }

    // ── DELETE ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task DeletarHistorico_DeveRemoverRegistro_QuandoPertenceAoTenant()
    {
        var (ctx, tenant, usuario) = await SeedTenantAsync();
        var calculo = MakeCalculo(tenant.Id, usuario.Id);
        ctx.HonorariosCalculos.Add(calculo);
        await ctx.SaveChangesAsync();

        var controller = new HonorariosController(ctx, CreateTenantContext(tenant.Id, usuario.Id));

        var result = await controller.DeletarHistorico(calculo.Id, default);

        Assert.IsType<NoContentResult>(result);
        Assert.Equal(0, await ctx.HonorariosCalculos.CountAsync());
    }

    [Fact]
    public async Task DeletarHistorico_DeveRetornarNotFound_QuandoNaoExiste()
    {
        var (ctx, tenant, usuario) = await SeedTenantAsync();
        var controller = new HonorariosController(ctx, CreateTenantContext(tenant.Id, usuario.Id));

        var result = await controller.DeletarHistorico(Guid.NewGuid(), default);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task DeletarHistorico_DeveRetornarNotFound_QuandoPertenceAOutroTenant()
    {
        var (ctx, tenant, usuario) = await SeedTenantAsync();

        var outroTenantId = Guid.NewGuid();
        var outroUsuarioId = Guid.NewGuid();
        var calculoOutro = MakeCalculo(outroTenantId, outroUsuarioId);
        ctx.HonorariosCalculos.Add(calculoOutro);
        await ctx.SaveChangesAsync();

        // Tenta deletar um registro de outro tenant
        var controller = new HonorariosController(ctx, CreateTenantContext(tenant.Id, usuario.Id));

        var result = await controller.DeletarHistorico(calculoOutro.Id, default);

        Assert.IsType<NotFoundResult>(result);
        // Registro do outro tenant permanece intacto
        Assert.Equal(1, await ctx.HonorariosCalculos.CountAsync());
    }
}
