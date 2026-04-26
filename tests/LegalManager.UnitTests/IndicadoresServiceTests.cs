using LegalManager.Application.DTOs.Indicadores;
using LegalManager.Domain.Entities;
using LegalManager.Domain.Enums;
using LegalManager.Domain.Interfaces;
using LegalManager.Infrastructure.Persistence;
using LegalManager.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace LegalManager.UnitTests;

public class IndicadoresServiceTests
{
    private AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options);
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
    public async Task GetIndicadoresAsync_DeveRetornarIndicadoresComDados()
    {
        var (ctx, tenantId) = await SeedAsync();
        ctx.Processos.Add(new Processo
        {
            Id = Guid.NewGuid(), TenantId = tenantId, NumeroCNJ = "0000001-00.2024.8.26.0001",
            AreaDireito = AreaDireito.Civil, Fase = FaseProcessual.Conhecimento,
            Status = StatusProcesso.Ativo, CriadoEm = DateTime.UtcNow
        });
        ctx.Tarefas.Add(new Tarefa
        {
            Id = Guid.NewGuid(), TenantId = tenantId, Titulo = "Tarefa 1",
            Status = StatusTarefa.Pendente, Prioridade = PrioridadeTarefa.Alta,
            CriadoEm = DateTime.UtcNow, CriadoPorId = Guid.NewGuid()
        });
        await ctx.SaveChangesAsync();

        var service = new IndicadoresService(ctx);
        var result = await service.GetIndicadoresAsync(tenantId);

        Assert.NotNull(result);
        Assert.Equal(1, result.Processos.Total);
        Assert.Equal(1, result.Processos.Ativos);
        Assert.Equal(1, result.Tarefas.Total);
        Assert.Equal(1, result.Tarefas.Pendentes);
    }

    [Fact]
    public async Task GetIndicadoresAsync_DeveCalcularProcessosPorArea()
    {
        var (ctx, tenantId) = await SeedAsync();
        ctx.Processos.AddRange(
            new Processo { Id = Guid.NewGuid(), TenantId = tenantId, NumeroCNJ = "0001", AreaDireito = AreaDireito.Civil, Fase = FaseProcessual.Conhecimento, Status = StatusProcesso.Ativo, CriadoEm = DateTime.UtcNow },
            new Processo { Id = Guid.NewGuid(), TenantId = tenantId, NumeroCNJ = "0002", AreaDireito = AreaDireito.Civil, Fase = FaseProcessual.Conhecimento, Status = StatusProcesso.Ativo, CriadoEm = DateTime.UtcNow },
            new Processo { Id = Guid.NewGuid(), TenantId = tenantId, NumeroCNJ = "0003", AreaDireito = AreaDireito.Trabalhista, Fase = FaseProcessual.Conhecimento, Status = StatusProcesso.Ativo, CriadoEm = DateTime.UtcNow }
        );
        await ctx.SaveChangesAsync();

        var service = new IndicadoresService(ctx);
        var result = await service.GetIndicadoresAsync(tenantId);

        var porArea = result.Processos.PorArea.ToList();
        Assert.Equal(2, porArea.Count);
        var civilArea = porArea.FirstOrDefault(a => a.Label == "Civil");
        Assert.NotNull(civilArea);
        Assert.Equal(2, civilArea.Count);
    }

    [Fact]
    public async Task GetIndicadoresAsync_DeveCalcularTarefasAtrasadas()
    {
        var (ctx, tenantId) = await SeedAsync();
        var userId = Guid.NewGuid();
        ctx.Tarefas.AddRange(
            new Tarefa { Id = Guid.NewGuid(), TenantId = tenantId, Titulo = "No Prazo", Status = StatusTarefa.Pendente, Prioridade = PrioridadeTarefa.Baixa, Prazo = DateTime.UtcNow.AddDays(5), CriadoEm = DateTime.UtcNow, CriadoPorId = userId },
            new Tarefa { Id = Guid.NewGuid(), TenantId = tenantId, Titulo = "Atrasada", Status = StatusTarefa.Pendente, Prioridade = PrioridadeTarefa.Alta, Prazo = DateTime.UtcNow.AddDays(-2), CriadoEm = DateTime.UtcNow, CriadoPorId = userId }
        );
        await ctx.SaveChangesAsync();

        var service = new IndicadoresService(ctx);
        var result = await service.GetIndicadoresAsync(tenantId);

        Assert.Equal(1, result.Tarefas.Atrasadas);
    }

    [Fact]
    public async Task GetIndicadoresAsync_DeveCalcularFinanceiro()
    {
        var (ctx, tenantId) = await SeedAsync();
        var now = DateTime.UtcNow;
        ctx.LancamentosFinanceiros.AddRange(
            new LancamentoFinanceiro { Id = Guid.NewGuid(), TenantId = tenantId, Tipo = TipoLancamento.Receita, Categoria = "H", Valor = 5000m, DataVencimento = new DateTime(now.Year, now.Month, 5), Status = StatusLancamento.Pago },
            new LancamentoFinanceiro { Id = Guid.NewGuid(), TenantId = tenantId, Tipo = TipoLancamento.Despesa, Categoria = "SW", Valor = 1000m, DataVencimento = new DateTime(now.Year, now.Month, 10), Status = StatusLancamento.Pago }
        );
        await ctx.SaveChangesAsync();

        var service = new IndicadoresService(ctx);
        var result = await service.GetIndicadoresAsync(tenantId);

        Assert.Equal(5000m, result.Financeiro.TotalReceitasMes);
        Assert.Equal(1000m, result.Financeiro.TotalDespesasMes);
        Assert.Equal(4000m, result.Financeiro.SaldoMes);
    }

    [Fact]
    public async Task GetIndicadoresAsync_DeveCalcularTimesheet()
    {
        var (ctx, tenantId) = await SeedAsync();
        var now = DateTime.UtcNow;
        ctx.RegistrosTempo.Add(new RegistroTempo
        {
            Id = Guid.NewGuid(), TenantId = tenantId, UsuarioId = Guid.NewGuid(),
            Inicio = now.AddHours(-2), Fim = now, DuracaoMinutos = 120,
            Descricao = "Trabalho", EmAndamento = false, CriadoEm = DateTime.UtcNow
        });
        await ctx.SaveChangesAsync();

        var service = new IndicadoresService(ctx);
        var result = await service.GetIndicadoresAsync(tenantId);

        Assert.Equal(120, result.Timesheet.MinutosEsteMes);
        Assert.Equal(1, result.Timesheet.TotalRegistrosEsteMes);
    }

    [Fact]
    public async Task GetIndicadoresAsync_DeveRetornarZeros_QuandoNenhumDado()
    {
        var (ctx, tenantId) = await SeedAsync();
        var service = new IndicadoresService(ctx);

        var result = await service.GetIndicadoresAsync(tenantId);

        Assert.Equal(0, result.Processos.Total);
        Assert.Equal(0, result.Tarefas.Total);
        Assert.Equal(0, result.Financeiro.TotalReceitasMes);
        Assert.Equal(0, result.Timesheet.MinutosEsteMes);
    }

    [Fact]
    public async Task GetIndicadoresAsync_DeveCalcularNovosProcessosEsteMes()
    {
        var (ctx, tenantId) = await SeedAsync();
        var now = DateTime.UtcNow;
        ctx.Processos.AddRange(
            new Processo { Id = Guid.NewGuid(), TenantId = tenantId, NumeroCNJ = "0001", AreaDireito = AreaDireito.Civil, Fase = FaseProcessual.Conhecimento, Status = StatusProcesso.Ativo, CriadoEm = now.AddMonths(-2) },
            new Processo { Id = Guid.NewGuid(), TenantId = tenantId, NumeroCNJ = "0002", AreaDireito = AreaDireito.Civil, Fase = FaseProcessual.Conhecimento, Status = StatusProcesso.Ativo, CriadoEm = new DateTime(now.Year, now.Month, 5) }
        );
        await ctx.SaveChangesAsync();

        var service = new IndicadoresService(ctx);
        var result = await service.GetIndicadoresAsync(tenantId);

        Assert.Equal(1, result.Processos.NovosEsteMes);
    }
}
