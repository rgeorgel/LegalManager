using LegalManager.Domain.Entities;
using LegalManager.Domain.Enums;
using LegalManager.Domain.Interfaces;
using LegalManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace LegalManager.UnitTests;

public class Modulo12Tests
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

    private async Task<(AppDbContext ctx, Tenant tenant)> SeedTenantAsync()
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
        await ctx.SaveChangesAsync();
        return (ctx, tenant);
    }

    // ═══════════════════════════════════════════════════════════════
    // AreaAtuacao
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task AreaAtuacao_Criar_ComDadosValidos()
    {
        var (ctx, tenant) = await SeedTenantAsync();
        var area = new AreaAtuacao
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.Id,
            Nome = "Direito Trabalhista",
            Descricao = "Área especializada em direito do trabalho",
            Ativo = true,
            CriadoEm = DateTime.UtcNow
        };

        ctx.AreasAtuacao.Add(area);
        await ctx.SaveChangesAsync();

        var saved = await ctx.AreasAtuacao.FindAsync(area.Id);
        Assert.NotNull(saved);
        Assert.Equal("Direito Trabalhista", saved!.Nome);
        Assert.Equal(tenant.Id, saved.TenantId);
        Assert.True(saved.Ativo);
    }

    [Fact]
    public async Task AreaAtuacao_Listar_SomenteDoTenant()
    {
        var (ctx, tenant) = await SeedTenantAsync();

        var outroTenant = new Tenant
        {
            Id = Guid.NewGuid(),
            Nome = "Outro Escritório",
            Plano = PlanoTipo.Pro,
            Status = StatusTenant.Ativo,
            CriadoEm = DateTime.UtcNow
        };
        ctx.Tenants.Add(outroTenant);

        ctx.AreasAtuacao.AddRange(
            new AreaAtuacao { Id = Guid.NewGuid(), TenantId = tenant.Id, Nome = "Civil", Ativo = true, CriadoEm = DateTime.UtcNow },
            new AreaAtuacao { Id = Guid.NewGuid(), TenantId = tenant.Id, Nome = "Penal", Ativo = true, CriadoEm = DateTime.UtcNow },
            new AreaAtuacao { Id = Guid.NewGuid(), TenantId = outroTenant.Id, Nome = "Tributário", Ativo = true, CriadoEm = DateTime.UtcNow }
        );
        await ctx.SaveChangesAsync();

        var tenantCtx = CreateTenantContext(tenant.Id);
        var areas = await ctx.AreasAtuacao
            .Where(a => a.TenantId == tenantCtx.TenantId)
            .OrderBy(a => a.Nome)
            .ToListAsync();

        Assert.Equal(2, areas.Count);
        Assert.All(areas, a => Assert.Equal(tenant.Id, a.TenantId));
    }

    [Fact]
    public async Task AreaAtuacao_Atualizar_MudaNomeEDescricao()
    {
        var (ctx, tenant) = await SeedTenantAsync();
        var area = new AreaAtuacao
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.Id,
            Nome = "Direito Civil",
            Descricao = "Descrição original",
            Ativo = true,
            CriadoEm = DateTime.UtcNow
        };
        ctx.AreasAtuacao.Add(area);
        await ctx.SaveChangesAsync();

        area.Nome = "Direito Civil Atualizado";
        area.Descricao = "Nova descrição";
        await ctx.SaveChangesAsync();

        var updated = await ctx.AreasAtuacao.FindAsync(area.Id);
        Assert.Equal("Direito Civil Atualizado", updated!.Nome);
        Assert.Equal("Nova descrição", updated.Descricao);
    }

    [Fact]
    public async Task AreaAtuacao_Deletar_RemoveRegistro()
    {
        var (ctx, tenant) = await SeedTenantAsync();
        var area = new AreaAtuacao
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.Id,
            Nome = "Para Deletar",
            Ativo = true,
            CriadoEm = DateTime.UtcNow
        };
        ctx.AreasAtuacao.Add(area);
        await ctx.SaveChangesAsync();

        ctx.AreasAtuacao.Remove(area);
        await ctx.SaveChangesAsync();

        var exists = await ctx.AreasAtuacao.FindAsync(area.Id);
        Assert.Null(exists);
    }

    // ═══════════════════════════════════════════════════════════════
    // CategoriaFinanceira
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task CategoriaFinanceira_Criar_Receita()
    {
        var (ctx, tenant) = await SeedTenantAsync();
        var cat = new CategoriaFinanceira
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.Id,
            Nome = "Honorário",
            Tipo = TipoCategoriaFinanceira.Receita,
            Ativo = true,
            CriadoEm = DateTime.UtcNow
        };

        ctx.CategoriasFinanceiras.Add(cat);
        await ctx.SaveChangesAsync();

        var saved = await ctx.CategoriasFinanceiras.FindAsync(cat.Id);
        Assert.NotNull(saved);
        Assert.Equal("Honorário", saved!.Nome);
        Assert.Equal(TipoCategoriaFinanceira.Receita, saved.Tipo);
    }

    [Fact]
    public async Task CategoriaFinanceira_Criar_Despesa()
    {
        var (ctx, tenant) = await SeedTenantAsync();
        var cat = new CategoriaFinanceira
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.Id,
            Nome = "Software",
            Tipo = TipoCategoriaFinanceira.Despesa,
            Ativo = true,
            CriadoEm = DateTime.UtcNow
        };

        ctx.CategoriasFinanceiras.Add(cat);
        await ctx.SaveChangesAsync();

        var saved = await ctx.CategoriasFinanceiras.FindAsync(cat.Id);
        Assert.NotNull(saved);
        Assert.Equal(TipoCategoriaFinanceira.Despesa, saved!.Tipo);
    }

    [Fact]
    public async Task CategoriaFinanceira_ListarPorTipo_FiltraCorretamente()
    {
        var (ctx, tenant) = await SeedTenantAsync();

        ctx.CategoriasFinanceiras.AddRange(
            new CategoriaFinanceira { Id = Guid.NewGuid(), TenantId = tenant.Id, Nome = "Honorário", Tipo = TipoCategoriaFinanceira.Receita, Ativo = true, CriadoEm = DateTime.UtcNow },
            new CategoriaFinanceira { Id = Guid.NewGuid(), TenantId = tenant.Id, Nome = "Custas", Tipo = TipoCategoriaFinanceira.Receita, Ativo = true, CriadoEm = DateTime.UtcNow },
            new CategoriaFinanceira { Id = Guid.NewGuid(), TenantId = tenant.Id, Nome = "Salário", Tipo = TipoCategoriaFinanceira.Despesa, Ativo = true, CriadoEm = DateTime.UtcNow },
            new CategoriaFinanceira { Id = Guid.NewGuid(), TenantId = tenant.Id, Nome = "Aluguel", Tipo = TipoCategoriaFinanceira.Despesa, Ativo = true, CriadoEm = DateTime.UtcNow }
        );
        await ctx.SaveChangesAsync();

        var receitas = await ctx.CategoriasFinanceiras
            .Where(c => c.TenantId == tenant.Id && c.Tipo == TipoCategoriaFinanceira.Receita)
            .ToListAsync();
        var despesas = await ctx.CategoriasFinanceiras
            .Where(c => c.TenantId == tenant.Id && c.Tipo == TipoCategoriaFinanceira.Despesa)
            .ToListAsync();

        Assert.Equal(2, receitas.Count);
        Assert.Equal(2, despesas.Count);
        Assert.All(receitas, r => Assert.Equal(TipoCategoriaFinanceira.Receita, r.Tipo));
        Assert.All(despesas, d => Assert.Equal(TipoCategoriaFinanceira.Despesa, d.Tipo));
    }

    [Fact]
    public async Task CategoriaFinanceira_Listar_SomenteDoTenant()
    {
        var (ctx, tenant) = await SeedTenantAsync();

        var outroTenant = new Tenant
        {
            Id = Guid.NewGuid(),
            Nome = "Outro",
            Plano = PlanoTipo.Pro,
            Status = StatusTenant.Ativo,
            CriadoEm = DateTime.UtcNow
        };
        ctx.Tenants.Add(outroTenant);

        ctx.CategoriasFinanceiras.AddRange(
            new CategoriaFinanceira { Id = Guid.NewGuid(), TenantId = tenant.Id, Nome = "Minha Categoria", Tipo = TipoCategoriaFinanceira.Receita, Ativo = true, CriadoEm = DateTime.UtcNow },
            new CategoriaFinanceira { Id = Guid.NewGuid(), TenantId = outroTenant.Id, Nome = "Categoria Alheia", Tipo = TipoCategoriaFinanceira.Receita, Ativo = true, CriadoEm = DateTime.UtcNow }
        );
        await ctx.SaveChangesAsync();

        var tenantCtx = CreateTenantContext(tenant.Id);
        var cats = await ctx.CategoriasFinanceiras
            .Where(c => c.TenantId == tenantCtx.TenantId)
            .ToListAsync();

        Assert.Single(cats);
        Assert.Equal("Minha Categoria", cats[0].Nome);
    }

    [Fact]
    public async Task CategoriaFinanceira_Atualizar_MudaNomeETipo()
    {
        var (ctx, tenant) = await SeedTenantAsync();
        var cat = new CategoriaFinanceira
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.Id,
            Nome = "Antigo Nome",
            Tipo = TipoCategoriaFinanceira.Receita,
            Ativo = true,
            CriadoEm = DateTime.UtcNow
        };
        ctx.CategoriasFinanceiras.Add(cat);
        await ctx.SaveChangesAsync();

        cat.Nome = "Nome Atualizado";
        cat.Tipo = TipoCategoriaFinanceira.Despesa;
        await ctx.SaveChangesAsync();

        var updated = await ctx.CategoriasFinanceiras.FindAsync(cat.Id);
        Assert.Equal("Nome Atualizado", updated!.Nome);
        Assert.Equal(TipoCategoriaFinanceira.Despesa, updated.Tipo);
    }

    // ═══════════════════════════════════════════════════════════════
    // Faturamento
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task Faturamento_Criar_ComDadosValidos()
    {
        var (ctx, tenant) = await SeedTenantAsync();
        var fat = new Faturamento
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.Id,
            BillingId = "bill_abc123",
            Periodo = "Mensal",
            Valor = 249m,
            Moeda = "BRL",
            Status = StatusFaturamento.Pendente,
            DataCriacao = DateTime.UtcNow
        };

        ctx.Faturamentos.Add(fat);
        await ctx.SaveChangesAsync();

        var saved = await ctx.Faturamentos.FindAsync(fat.Id);
        Assert.NotNull(saved);
        Assert.Equal("bill_abc123", saved!.BillingId);
        Assert.Equal(249m, saved.Valor);
        Assert.Equal(StatusFaturamento.Pendente, saved.Status);
    }

    [Fact]
    public async Task Faturamento_Listar_SomenteDoTenant()
    {
        var (ctx, tenant) = await SeedTenantAsync();

        var outroTenant = new Tenant
        {
            Id = Guid.NewGuid(),
            Nome = "Outro",
            Plano = PlanoTipo.Pro,
            Status = StatusTenant.Ativo,
            CriadoEm = DateTime.UtcNow
        };
        ctx.Tenants.Add(outroTenant);

        ctx.Faturamentos.AddRange(
            new Faturamento { Id = Guid.NewGuid(), TenantId = tenant.Id, BillingId = "bill_1", Periodo = "Mensal", Valor = 100m, Status = StatusFaturamento.Pago, DataCriacao = DateTime.UtcNow },
            new Faturamento { Id = Guid.NewGuid(), TenantId = outroTenant.Id, BillingId = "bill_2", Periodo = "Mensal", Valor = 200m, Status = StatusFaturamento.Pago, DataCriacao = DateTime.UtcNow }
        );
        await ctx.SaveChangesAsync();

        var tenantCtx = CreateTenantContext(tenant.Id);
        var faturas = await ctx.Faturamentos
            .Where(f => f.TenantId == tenantCtx.TenantId)
            .ToListAsync();

        Assert.Single(faturas);
        Assert.Equal("bill_1", faturas[0].BillingId);
    }

    [Fact]
    public async Task Faturamento_AtualizarStatus_ParaPago()
    {
        var (ctx, tenant) = await SeedTenantAsync();
        var fat = new Faturamento
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.Id,
            BillingId = "bill_x",
            Periodo = "Anual",
            Valor = 2000m,
            Status = StatusFaturamento.Pendente,
            DataCriacao = DateTime.UtcNow
        };
        ctx.Faturamentos.Add(fat);
        await ctx.SaveChangesAsync();

        fat.Status = StatusFaturamento.Pago;
        fat.DataPagamento = DateTime.UtcNow;
        await ctx.SaveChangesAsync();

        var updated = await ctx.Faturamentos.FindAsync(fat.Id);
        Assert.Equal(StatusFaturamento.Pago, updated!.Status);
        Assert.NotNull(updated.DataPagamento);
    }

    [Fact]
    public async Task Faturamento_FiltrarPorStatus()
    {
        var (ctx, tenant) = await SeedTenantAsync();

        ctx.Faturamentos.AddRange(
            new Faturamento { Id = Guid.NewGuid(), TenantId = tenant.Id, BillingId = "b1", Periodo = "Mensal", Valor = 100m, Status = StatusFaturamento.Pago, DataCriacao = DateTime.UtcNow },
            new Faturamento { Id = Guid.NewGuid(), TenantId = tenant.Id, BillingId = "b2", Periodo = "Mensal", Valor = 100m, Status = StatusFaturamento.Pendente, DataCriacao = DateTime.UtcNow },
            new Faturamento { Id = Guid.NewGuid(), TenantId = tenant.Id, BillingId = "b3", Periodo = "Mensal", Valor = 100m, Status = StatusFaturamento.Cancelado, DataCriacao = DateTime.UtcNow }
        );
        await ctx.SaveChangesAsync();

        var pendentes = await ctx.Faturamentos
            .Where(f => f.TenantId == tenant.Id && f.Status == StatusFaturamento.Pendente)
            .ToListAsync();

        Assert.Single(pendentes);
        Assert.Equal("b2", pendentes[0].BillingId);
    }

    [Fact]
    public async Task Faturamento_FiltrarPorPeriodo()
    {
        var (ctx, tenant) = await SeedTenantAsync();

        ctx.Faturamentos.AddRange(
            new Faturamento { Id = Guid.NewGuid(), TenantId = tenant.Id, BillingId = "m1", Periodo = "Mensal", Valor = 249m, Status = StatusFaturamento.Pago, DataCriacao = DateTime.UtcNow },
            new Faturamento { Id = Guid.NewGuid(), TenantId = tenant.Id, BillingId = "a1", Periodo = "Anual", Valor = 2000m, Status = StatusFaturamento.Pago, DataCriacao = DateTime.UtcNow }
        );
        await ctx.SaveChangesAsync();

        var mensais = await ctx.Faturamentos
            .Where(f => f.TenantId == tenant.Id && f.Periodo == "Mensal")
            .ToListAsync();

        Assert.Single(mensais);
        Assert.Equal("m1", mensais[0].BillingId);
    }

    [Fact]
    public async Task Faturamento_Cancelar_MudaStatus()
    {
        var (ctx, tenant) = await SeedTenantAsync();
        var fat = new Faturamento
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.Id,
            BillingId = "bill_cancel",
            Periodo = "Mensal",
            Valor = 249m,
            Status = StatusFaturamento.Pendente,
            DataCriacao = DateTime.UtcNow
        };
        ctx.Faturamentos.Add(fat);
        await ctx.SaveChangesAsync();

        fat.Status = StatusFaturamento.Cancelado;
        await ctx.SaveChangesAsync();

        var updated = await ctx.Faturamentos.FindAsync(fat.Id);
        Assert.Equal(StatusFaturamento.Cancelado, updated!.Status);
    }

    // ═══════════════════════════════════════════════════════════════
    // Validações de Negócio
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task CategoriaFinanceira_Permite_MesmoNomeTipoDiferente()
    {
        var (ctx, tenant) = await SeedTenantAsync();

        ctx.CategoriasFinanceiras.Add(new CategoriaFinanceira
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.Id,
            Nome = "Custas",
            Tipo = TipoCategoriaFinanceira.Receita,
            Ativo = true,
            CriadoEm = DateTime.UtcNow
        });

        ctx.CategoriasFinanceiras.Add(new CategoriaFinanceira
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.Id,
            Nome = "Custas",
            Tipo = TipoCategoriaFinanceira.Despesa,
            Ativo = true,
            CriadoEm = DateTime.UtcNow
        });

        await ctx.SaveChangesAsync();

        var cats = await ctx.CategoriasFinanceiras
            .Where(c => c.TenantId == tenant.Id && c.Nome == "Custas")
            .ToListAsync();

        Assert.Equal(2, cats.Count);
    }

    [Fact]
    public async Task LancamentoFinanceiro_Criar_ComCategoriaPersonalizada()
    {
        var (ctx, tenant) = await SeedTenantAsync();

        var lancamento = new LancamentoFinanceiro
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.Id,
            Tipo = TipoLancamento.Receita,
            Categoria = "Categoria Customizada",
            Valor = 500m,
            DataVencimento = new DateTime(2026, 4, 15),
            Status = StatusLancamento.Pendente,
            CriadoEm = DateTime.UtcNow
        };

        ctx.LancamentosFinanceiros.Add(lancamento);
        await ctx.SaveChangesAsync();

        var saved = await ctx.LancamentosFinanceiros.FindAsync(lancamento.Id);
        Assert.NotNull(saved);
        Assert.Equal("Categoria Customizada", saved!.Categoria);
        Assert.Equal(TipoLancamento.Receita, saved.Tipo);
    }

    [Fact]
    public async Task LancamentoFinanceiro_Criar_ComCategoriaFixa()
    {
        var (ctx, tenant) = await SeedTenantAsync();

        var lancamento = new LancamentoFinanceiro
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.Id,
            Tipo = TipoLancamento.Receita,
            Categoria = CategoriaLancamento.Honorario,
            Valor = 1000m,
            DataVencimento = new DateTime(2026, 4, 10),
            Status = StatusLancamento.Pendente,
            CriadoEm = DateTime.UtcNow
        };

        ctx.LancamentosFinanceiros.Add(lancamento);
        await ctx.SaveChangesAsync();

        var saved = await ctx.LancamentosFinanceiros.FindAsync(lancamento.Id);
        Assert.NotNull(saved);
        Assert.Equal("Honorario", saved!.Categoria);
    }

    [Fact]
    public async Task AreaAtuacao_IsolamentoEntreTenants()
    {
        var (ctx, tenant) = await SeedTenantAsync();

        var outroTenant = new Tenant
        {
            Id = Guid.NewGuid(),
            Nome = "Outro",
            Plano = PlanoTipo.Pro,
            Status = StatusTenant.Ativo,
            CriadoEm = DateTime.UtcNow
        };
        ctx.Tenants.Add(outroTenant);

        ctx.AreasAtuacao.AddRange(
            new AreaAtuacao { Id = Guid.NewGuid(), TenantId = tenant.Id, Nome = "Civil", Ativo = true, CriadoEm = DateTime.UtcNow },
            new AreaAtuacao { Id = Guid.NewGuid(), TenantId = outroTenant.Id, Nome = "Civil", Ativo = true, CriadoEm = DateTime.UtcNow }
        );
        await ctx.SaveChangesAsync();

        var tenantCtx = CreateTenantContext(tenant.Id);
        var areas = await ctx.AreasAtuacao
            .Where(a => a.TenantId == tenantCtx.TenantId)
            .ToListAsync();

        Assert.Single(areas);
        Assert.Equal(tenant.Id, areas[0].TenantId);
    }
}
