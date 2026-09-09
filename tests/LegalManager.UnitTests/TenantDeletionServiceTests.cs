using LegalManager.Application.Interfaces;
using LegalManager.Domain;
using LegalManager.Domain.Entities;
using LegalManager.Domain.Enums;
using LegalManager.Infrastructure.Persistence;
using LegalManager.Infrastructure.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace LegalManager.UnitTests;

public class TenantDeletionServiceTests
{
    private AppDbContext CreateContext(string dbName)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(dbName)
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new AppDbContext(options);
    }

    private static Tenant SeedTenant(AppDbContext ctx, string nome = "Tenant Alvo")
    {
        var t = new Tenant
        {
            Id = Guid.NewGuid(),
            Nome = nome,
            Plano = PlanoTipo.Pro,
            Status = StatusTenant.Ativo,
            CriadoEm = DateTime.UtcNow
        };
        ctx.Tenants.Add(t);
        return t;
    }

    private static ITenantImportService CreateImportService(AppDbContext ctx) =>
        new TenantImportService(ctx, new TenantAnonymizer(), new PasswordHasher<Usuario>(), NullLogger<TenantImportService>.Instance);

    [Fact]
    public async Task DeleteTenantAsync_TenantSistema_RetornaErro()
    {
        using var ctx = CreateContext(nameof(DeleteTenantAsync_TenantSistema_RetornaErro));
        var service = new TenantDeletionService(ctx, CreateImportService(ctx));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.DeleteTenantAsync(TenantConstants.SystemTenantId));
    }

    [Fact]
    public async Task DeleteTenantAsync_TenantInexistente_RetornaErro()
    {
        using var ctx = CreateContext(nameof(DeleteTenantAsync_TenantInexistente_RetornaErro));
        var service = new TenantDeletionService(ctx, CreateImportService(ctx));

        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => service.DeleteTenantAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task DeleteTenantAsync_RemoveTenantEDados()
    {
        using var ctx = CreateContext(nameof(DeleteTenantAsync_RemoveTenantEDados));
        var tenant = SeedTenant(ctx);
        var user = new Usuario
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.Id,
            Nome = "Adv",
            Email = "adv@alvo.com",
            UserName = "adv@alvo.com",
            Perfil = PerfilUsuario.Admin,
            Ativo = true,
            CriadoEm = DateTime.UtcNow
        };
        var contato = new Contato
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.Id,
            Nome = "Cliente",
            Tipo = TipoPessoa.PF,
            TipoContato = TipoContato.Cliente,
            CriadoEm = DateTime.UtcNow,
            Ativo = true
        };
        ctx.Users.Add(user);
        ctx.Contatos.Add(contato);
        await ctx.SaveChangesAsync();

        var service = new TenantDeletionService(ctx, CreateImportService(ctx));
        var result = await service.DeleteTenantAsync(tenant.Id);

        Assert.Equal(tenant.Id, result.TenantId);
        Assert.Equal("Tenant Alvo", result.TenantNome);
        Assert.False(await ctx.Tenants.AnyAsync(t => t.Id == tenant.Id));
        Assert.False(await ctx.Users.AnyAsync(u => u.TenantId == tenant.Id));
        Assert.False(await ctx.Contatos.AnyAsync(c => c.TenantId == tenant.Id));
    }

    [Fact]
    public async Task DeleteTenantAsync_RemoveCreditosAI()
    {
        // Regressão: CreditosAI não faz parte de TenantTableSpecs (não é replicável entre
        // ambientes via export/import), mas tem FK Restrict para Tenants — é a única tabela
        // do schema nessa situação. WipeTenantDataAsync (reaproveitado do import) não o
        // apaga, então excluir o Tenant sem tratamento explícito violava a FK no Postgres.
        using var ctx = CreateContext(nameof(DeleteTenantAsync_RemoveCreditosAI));
        var tenant = SeedTenant(ctx);
        var credito = new CreditoAI
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.Id,
            Tipo = TipoCreditoAI.GeracaoPeca,
            QuantidadeTotal = 10,
            QuantidadeUsada = 0,
            Origem = OrigemCreditoAI.Cortesai,
            CriadoEm = DateTime.UtcNow
        };
        ctx.CreditosAI.Add(credito);
        await ctx.SaveChangesAsync();

        var service = new TenantDeletionService(ctx, CreateImportService(ctx));
        await service.DeleteTenantAsync(tenant.Id);

        Assert.False(await ctx.Tenants.AnyAsync(t => t.Id == tenant.Id));
        Assert.False(await ctx.CreditosAI.AnyAsync(c => c.Id == credito.Id));
    }

    [Fact]
    public async Task DeleteTenantAsync_ComCicloParcelaLancamento_NaoFalha()
    {
        // Mesma regressão coberta em TenantImportServiceTests para o wipe do modo Replace:
        // ParcelaHonorario.LancamentoFinanceiroId <-> LancamentoFinanceiro.ParcelaHonorarioId
        // formam um ciclo de FK genuíno que bloqueava o DELETE no Postgres real.
        // TenantDeletionService reaproveita o mesmo WipeTenantDataAsync, então precisa da
        // mesma cobertura.
        using var ctx = CreateContext(nameof(DeleteTenantAsync_ComCicloParcelaLancamento_NaoFalha));
        var tenant = SeedTenant(ctx);
        var user = new Usuario
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.Id,
            Nome = "Adv",
            Email = "adv@alvo.com",
            UserName = "adv@alvo.com",
            Perfil = PerfilUsuario.Admin,
            Ativo = true,
            CriadoEm = DateTime.UtcNow
        };
        var contato = new Contato
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.Id,
            Nome = "Cliente",
            Tipo = TipoPessoa.PF,
            TipoContato = TipoContato.Cliente,
            CriadoEm = DateTime.UtcNow,
            Ativo = true
        };
        var contrato = new ContratoHonorario
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.Id,
            ContatoId = contato.Id,
            NumeroContrato = "C-001",
            ValorTotal = 1000m,
            FormaPagamento = FormaPagamentoContrato.Parcelado,
            CriadoPorId = user.Id,
            DataInicio = DateTime.UtcNow,
            CriadoEm = DateTime.UtcNow
        };
        var lancamento = new LancamentoFinanceiro
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.Id,
            ContratoHonorarioId = contrato.Id
        };
        var parcela = new ParcelaHonorario
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.Id,
            ContratoId = contrato.Id,
            Numero = 1,
            Vencimento = DateTime.UtcNow,
            ValorOriginal = 1000m,
            LancamentoFinanceiroId = lancamento.Id
        };
        lancamento.ParcelaHonorarioId = parcela.Id;

        ctx.Users.Add(user);
        ctx.Contatos.Add(contato);
        ctx.ContratosHonorarios.Add(contrato);
        ctx.LancamentosFinanceiros.Add(lancamento);
        ctx.ParcelasHonorarios.Add(parcela);
        await ctx.SaveChangesAsync();

        var service = new TenantDeletionService(ctx, CreateImportService(ctx));
        await service.DeleteTenantAsync(tenant.Id);

        Assert.False(await ctx.Tenants.AnyAsync(t => t.Id == tenant.Id));
        Assert.False(await ctx.ParcelasHonorarios.AnyAsync(p => p.Id == parcela.Id));
        Assert.False(await ctx.LancamentosFinanceiros.AnyAsync(l => l.Id == lancamento.Id));
        Assert.False(await ctx.ContratosHonorarios.AnyAsync(c => c.Id == contrato.Id));
    }
}
