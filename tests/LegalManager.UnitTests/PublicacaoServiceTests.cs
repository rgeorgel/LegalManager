using LegalManager.Application.DTOs.Publicacoes;
using LegalManager.Domain.Entities;
using LegalManager.Domain.Enums;
using LegalManager.Domain.Interfaces;
using LegalManager.Infrastructure.Persistence;
using LegalManager.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace LegalManager.UnitTests;

public class PublicacaoServiceTests
{
    private AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options);
    }

    private ITenantContext CreateTenantContext(Guid tenantId)
    {
        var mock = new Mock<ITenantContext>();
        mock.Setup(t => t.TenantId).Returns(tenantId);
        return mock.Object;
    }

    private async Task<(AppDbContext ctx, Guid tenantId)> SeedAsync()
    {
        var ctx = CreateContext();
        var tenantId = Guid.NewGuid();
        ctx.Tenants.Add(new Tenant
        {
            Id = tenantId, Nome = "Test", Plano = PlanoTipo.Pro,
            Status = StatusTenant.Ativo, CriadoEm = DateTime.UtcNow
        });
        await ctx.SaveChangesAsync();
        return (ctx, tenantId);
    }

    [Fact]
    public async Task GetAllAsync_DeveRetornarPublicacoesDoTenant()
    {
        var (ctx, tenantId) = await SeedAsync();
        var outroTenantId = Guid.NewGuid();
        ctx.Publicacoes.AddRange(
            new Publicacao { Id = Guid.NewGuid(), TenantId = tenantId, NumeroCNJ = "0001", Diario = "DJSP", DataPublicacao = DateTime.UtcNow, Conteudo = "C1", Tipo = TipoPublicacao.Prazo, Status = StatusPublicacao.Nova, Urgente = false, CapturaEm = DateTime.UtcNow },
            new Publicacao { Id = Guid.NewGuid(), TenantId = outroTenantId, NumeroCNJ = "0002", Diario = "DJSP", DataPublicacao = DateTime.UtcNow, Conteudo = "C2", Tipo = TipoPublicacao.Decisao, Status = StatusPublicacao.Nova, Urgente = false, CapturaEm = DateTime.UtcNow }
        );
        await ctx.SaveChangesAsync();

        var service = new PublicacaoService(ctx, CreateTenantContext(tenantId));
        var result = await service.GetAllAsync(new PublicacaoFiltroDto());

        Assert.Single(result);
        Assert.Equal("0001", result.First().NumeroCNJ);
    }

    [Fact]
    public async Task GetAllAsync_DeveFiltrarPorTipo()
    {
        var (ctx, tenantId) = await SeedAsync();
        ctx.Publicacoes.AddRange(
            new Publicacao { Id = Guid.NewGuid(), TenantId = tenantId, NumeroCNJ = "0001", Diario = "DJSP", DataPublicacao = DateTime.UtcNow, Conteudo = "C1", Tipo = TipoPublicacao.Prazo, Status = StatusPublicacao.Nova, Urgente = false, CapturaEm = DateTime.UtcNow },
            new Publicacao { Id = Guid.NewGuid(), TenantId = tenantId, NumeroCNJ = "0002", Diario = "DJSP", DataPublicacao = DateTime.UtcNow, Conteudo = "C2", Tipo = TipoPublicacao.Decisao, Status = StatusPublicacao.Nova, Urgente = false, CapturaEm = DateTime.UtcNow }
        );
        await ctx.SaveChangesAsync();

        var service = new PublicacaoService(ctx, CreateTenantContext(tenantId));
        var result = await service.GetAllAsync(new PublicacaoFiltroDto(Tipo: TipoPublicacao.Prazo));

        Assert.Single(result);
        Assert.Equal(TipoPublicacao.Prazo, result.First().Tipo);
    }

    [Fact]
    public async Task GetAllAsync_DeveFiltrarPorStatus()
    {
        var (ctx, tenantId) = await SeedAsync();
        ctx.Publicacoes.AddRange(
            new Publicacao { Id = Guid.NewGuid(), TenantId = tenantId, NumeroCNJ = "0001", Diario = "DJSP", DataPublicacao = DateTime.UtcNow, Conteudo = "C1", Tipo = TipoPublicacao.Prazo, Status = StatusPublicacao.Nova, Urgente = false, CapturaEm = DateTime.UtcNow },
            new Publicacao { Id = Guid.NewGuid(), TenantId = tenantId, NumeroCNJ = "0002", Diario = "DJSP", DataPublicacao = DateTime.UtcNow, Conteudo = "C2", Tipo = TipoPublicacao.Prazo, Status = StatusPublicacao.Lida, Urgente = false, CapturaEm = DateTime.UtcNow }
        );
        await ctx.SaveChangesAsync();

        var service = new PublicacaoService(ctx, CreateTenantContext(tenantId));
        var result = await service.GetAllAsync(new PublicacaoFiltroDto(Status: StatusPublicacao.Lida));

        Assert.Single(result);
        Assert.Equal(StatusPublicacao.Lida, result.First().Status);
    }

    [Fact]
    public async Task GetByIdAsync_DeveRetornarPublicacao_QuandoExistir()
    {
        var (ctx, tenantId) = await SeedAsync();
        var pubId = Guid.NewGuid();
        ctx.Publicacoes.Add(new Publicacao
        {
            Id = pubId, TenantId = tenantId, NumeroCNJ = "0000001-00.2024.8.26.0001",
            Diario = "DJSP", DataPublicacao = DateTime.UtcNow,
            Conteudo = "Sentença de mérito", Tipo = TipoPublicacao.Decisao,
            Status = StatusPublicacao.Nova, Urgente = true, CapturaEm = DateTime.UtcNow
        });
        await ctx.SaveChangesAsync();

        var service = new PublicacaoService(ctx, CreateTenantContext(tenantId));
        var result = await service.GetByIdAsync(pubId);

        Assert.NotNull(result);
        Assert.Equal(pubId, result.Id);
        Assert.True(result.Urgente);
    }

    [Fact]
    public async Task GetByIdAsync_DeveRetornarNull_QuandoNaoExistir()
    {
        var (ctx, tenantId) = await SeedAsync();
        var service = new PublicacaoService(ctx, CreateTenantContext(tenantId));

        var result = await service.GetByIdAsync(Guid.NewGuid());

        Assert.Null(result);
    }

    [Fact]
    public async Task MarcarLidaAsync_DeveAtualizarStatus()
    {
        var (ctx, tenantId) = await SeedAsync();
        var pubId = Guid.NewGuid();
        ctx.Publicacoes.Add(new Publicacao
        {
            Id = pubId, TenantId = tenantId, NumeroCNJ = "0001", Diario = "DJSP",
            DataPublicacao = DateTime.UtcNow, Conteudo = "C", Tipo = TipoPublicacao.Prazo,
            Status = StatusPublicacao.Nova, Urgente = false, CapturaEm = DateTime.UtcNow
        });
        await ctx.SaveChangesAsync();

        var service = new PublicacaoService(ctx, CreateTenantContext(tenantId));
        await service.MarcarLidaAsync(pubId);

        var updated = await ctx.Publicacoes.FindAsync(pubId);
        Assert.Equal(StatusPublicacao.Lida, updated!.Status);
    }

    [Fact]
    public async Task ArquivarAsync_DeveAtualizarStatus()
    {
        var (ctx, tenantId) = await SeedAsync();
        var pubId = Guid.NewGuid();
        ctx.Publicacoes.Add(new Publicacao
        {
            Id = pubId, TenantId = tenantId, NumeroCNJ = "0001", Diario = "DJSP",
            DataPublicacao = DateTime.UtcNow, Conteudo = "C", Tipo = TipoPublicacao.Prazo,
            Status = StatusPublicacao.Lida, Urgente = false, CapturaEm = DateTime.UtcNow
        });
        await ctx.SaveChangesAsync();

        var service = new PublicacaoService(ctx, CreateTenantContext(tenantId));
        await service.ArquivarAsync(pubId);

        var updated = await ctx.Publicacoes.FindAsync(pubId);
        Assert.Equal(StatusPublicacao.Arquivada, updated!.Status);
    }

    [Fact]
    public async Task GetNaoLidasCountAsync_DeveRetornarContagemCorreta()
    {
        var (ctx, tenantId) = await SeedAsync();
        ctx.Publicacoes.AddRange(
            new Publicacao { Id = Guid.NewGuid(), TenantId = tenantId, NumeroCNJ = "0001", Diario = "DJSP", DataPublicacao = DateTime.UtcNow, Conteudo = "C1", Tipo = TipoPublicacao.Prazo, Status = StatusPublicacao.Nova, Urgente = false, CapturaEm = DateTime.UtcNow },
            new Publicacao { Id = Guid.NewGuid(), TenantId = tenantId, NumeroCNJ = "0002", Diario = "DJSP", DataPublicacao = DateTime.UtcNow, Conteudo = "C2", Tipo = TipoPublicacao.Prazo, Status = StatusPublicacao.Nova, Urgente = false, CapturaEm = DateTime.UtcNow },
            new Publicacao { Id = Guid.NewGuid(), TenantId = tenantId, NumeroCNJ = "0003", Diario = "DJSP", DataPublicacao = DateTime.UtcNow, Conteudo = "C3", Tipo = TipoPublicacao.Prazo, Status = StatusPublicacao.Lida, Urgente = false, CapturaEm = DateTime.UtcNow }
        );
        await ctx.SaveChangesAsync();

        var service = new PublicacaoService(ctx, CreateTenantContext(tenantId));
        var count = await service.GetNaoLidasCountAsync();

        Assert.Equal(2, count);
    }

    [Fact]
    public async Task GetAllAsync_DevePaginarCorretamente()
    {
        var (ctx, tenantId) = await SeedAsync();
        for (int i = 0; i < 25; i++)
        {
            ctx.Publicacoes.Add(new Publicacao
            {
                Id = Guid.NewGuid(), TenantId = tenantId, NumeroCNJ = $"{i:D4}",
                Diario = "DJSP", DataPublicacao = DateTime.UtcNow.AddMinutes(-i),
                Conteudo = $"C{i}", Tipo = TipoPublicacao.Prazo,
                Status = StatusPublicacao.Nova, Urgente = false, CapturaEm = DateTime.UtcNow
            });
        }
        await ctx.SaveChangesAsync();

        var service = new PublicacaoService(ctx, CreateTenantContext(tenantId));
        var page1 = await service.GetAllAsync(new PublicacaoFiltroDto(Page: 1, PageSize: 10));
        var page3 = await service.GetAllAsync(new PublicacaoFiltroDto(Page: 3, PageSize: 10));

        Assert.Equal(10, page1.Count());
        Assert.Equal(5, page3.Count());
    }
}
