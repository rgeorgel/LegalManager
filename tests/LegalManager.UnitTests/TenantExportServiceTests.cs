using LegalManager.Domain.Entities;
using LegalManager.Domain.Enums;
using LegalManager.Infrastructure.Persistence;
using LegalManager.Infrastructure.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace LegalManager.UnitTests;

public class TenantExportServiceTests
{
    private AppDbContext CreateContext(string dbName)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(dbName)
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new AppDbContext(options);
    }

    private static Tenant SeedTenant(AppDbContext ctx)
    {
        var tenant = new Tenant
        {
            Id = Guid.NewGuid(),
            Nome = "Escritório Demo",
            Cnpj = "12.345.678/0001-90",
            Plano = PlanoTipo.Pro,
            Status = StatusTenant.Ativo,
            CriadoEm = DateTime.UtcNow
        };
        ctx.Tenants.Add(tenant);
        return tenant;
    }

    private static Usuario SeedUser(AppDbContext ctx, Guid tenantId, string email)
    {
        var user = new Usuario
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Nome = "Admin Test",
            Email = email,
            UserName = email,
            Perfil = PerfilUsuario.Admin,
            Ativo = true,
            CriadoEm = DateTime.UtcNow,
            PasswordHash = "AQAAAAIAAYagAAAAIFakeHashForTesting1234567890="
        };
        ctx.Users.Add(user);
        return user;
    }

    private static Contato SeedContato(AppDbContext ctx, Guid tenantId, string email, string telefone)
    {
        var c = new Contato
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Nome = "João Silva",
            Tipo = TipoPessoa.PF,
            TipoContato = TipoContato.Cliente,
            Email = email,
            Telefone = telefone,
            CriadoEm = DateTime.UtcNow,
            Ativo = true
        };
        ctx.Contatos.Add(c);
        return c;
    }

    [Fact]
    public async Task ExportAsync_TenantVazio_RetornaJsonComTabelasVazias()
    {
        using var ctx = CreateContext(nameof(ExportAsync_TenantVazio_RetornaJsonComTabelasVazias));
        var tenant = SeedTenant(ctx);
        await ctx.SaveChangesAsync();

        var service = new TenantExportService(ctx, new TenantAnonymizer(), NullLogger<TenantExportService>.Instance);

        var result = await service.ExportAsync(tenant.Id);

        Assert.NotNull(result);
        Assert.NotEmpty(result.Payload);
        Assert.Contains("\"version\":1", System.Text.Encoding.UTF8.GetString(result.Payload));
    }

    [Fact]
    public async Task ExportAsync_ContemAnonymizacaoEmails_TrocaDominioParaReplica()
    {
        using var ctx = CreateContext(nameof(ExportAsync_ContemAnonymizacaoEmails_TrocaDominioParaReplica));
        var tenant = SeedTenant(ctx);
        SeedContato(ctx, tenant.Id, "cliente.real@empresa.com.br", "(11) 91234-5678");
        await ctx.SaveChangesAsync();

        var service = new TenantExportService(ctx, new TenantAnonymizer(), NullLogger<TenantExportService>.Instance);
        var result = await service.ExportAsync(tenant.Id);
        var json = System.Text.Encoding.UTF8.GetString(result.Payload);

        Assert.Contains("cliente.real@causify-replica.com", json);
        Assert.DoesNotContain("cliente.real@empresa.com.br", json);
    }

    [Fact]
    public async Task ExportAsync_ContemAnonymizacaoTelefones_SubstituiPorPlaceholder()
    {
        using var ctx = CreateContext(nameof(ExportAsync_ContemAnonymizacaoTelefones_SubstituiPorPlaceholder));
        var tenant = SeedTenant(ctx);
        SeedContato(ctx, tenant.Id, "user@test.com", "(11) 91234-5678");
        await ctx.SaveChangesAsync();

        var service = new TenantExportService(ctx, new TenantAnonymizer(), NullLogger<TenantExportService>.Instance);
        var result = await service.ExportAsync(tenant.Id);
        var json = System.Text.Encoding.UTF8.GetString(result.Payload);

        // JSON encodes '+' as \u002B
        Assert.Contains("5511900000000", json);
        Assert.Contains(@"\u002B", json);
        Assert.DoesNotContain("(11) 91234-5678", json);
    }

    [Fact]
    public async Task ExportAsync_UsuariosIncluidos_ComEmailAnomizado()
    {
        using var ctx = CreateContext(nameof(ExportAsync_UsuariosIncluidos_ComEmailAnomizado));
        var tenant = SeedTenant(ctx);
        SeedUser(ctx, tenant.Id, "admin@empresa.com.br");
        await ctx.SaveChangesAsync();

        var service = new TenantExportService(ctx, new TenantAnonymizer(), NullLogger<TenantExportService>.Instance);
        var result = await service.ExportAsync(tenant.Id);
        var json = System.Text.Encoding.UTF8.GetString(result.Payload);

        Assert.Contains("admin@causify-replica.com", json);
        Assert.Contains("\"Usuarios\"", json);
    }

    [Fact]
    public async Task ExportAsync_NaoIncluiSenhas_NoJson()
    {
        using var ctx = CreateContext(nameof(ExportAsync_NaoIncluiSenhas_NoJson));
        var tenant = SeedTenant(ctx);
        var user = SeedUser(ctx, tenant.Id, "admin@empresa.com.br");
        await ctx.SaveChangesAsync();

        var service = new TenantExportService(ctx, new TenantAnonymizer(), NullLogger<TenantExportService>.Instance);
        var result = await service.ExportAsync(tenant.Id);
        var json = System.Text.Encoding.UTF8.GetString(result.Payload);

        Assert.DoesNotContain(user.PasswordHash, json);
        Assert.DoesNotContain("PasswordHash", json);
        Assert.DoesNotContain("SecurityStamp", json);
    }

    [Fact]
    public async Task ExportAsync_TenantSistema_RetornaErro()
    {
        using var ctx = CreateContext(nameof(ExportAsync_TenantSistema_RetornaErro));
        var service = new TenantExportService(ctx, new TenantAnonymizer(), NullLogger<TenantExportService>.Instance);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.ExportAsync(Domain.TenantConstants.SystemTenantId));
    }

    [Fact]
    public async Task ExportAsync_TenantInexistente_RetornaErro()
    {
        using var ctx = CreateContext(nameof(ExportAsync_TenantInexistente_RetornaErro));
        var service = new TenantExportService(ctx, new TenantAnonymizer(), NullLogger<TenantExportService>.Instance);

        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => service.ExportAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task ExportAsync_ApenasTenantSolicitado_NaoIncluiDadosDeOutrosTenants()
    {
        using var ctx = CreateContext(nameof(ExportAsync_ApenasTenantSolicitado_NaoIncluiDadosDeOutrosTenants));
        var t1 = SeedTenant(ctx);
        t1.Nome = "Tenant Origem";
        var t2 = new Tenant
        {
            Id = Guid.NewGuid(),
            Nome = "Outro Tenant",
            Plano = PlanoTipo.Free,
            Status = StatusTenant.Ativo,
            CriadoEm = DateTime.UtcNow
        };
        ctx.Tenants.Add(t2);
        SeedContato(ctx, t1.Id, "origem@test.com", "1111111111");
        SeedContato(ctx, t2.Id, "outro@test.com", "2222222222");
        await ctx.SaveChangesAsync();

        var service = new TenantExportService(ctx, new TenantAnonymizer(), NullLogger<TenantExportService>.Instance);
        var result = await service.ExportAsync(t1.Id);
        var json = System.Text.Encoding.UTF8.GetString(result.Payload);

        Assert.Contains("origem@causify-replica.com", json);
        Assert.DoesNotContain("outro@causify-replica.com", json);
    }
}
