using LegalManager.Application.DTOs.Financeiro;
using LegalManager.Domain.Entities;
using LegalManager.Domain.Enums;
using LegalManager.Domain.Interfaces;
using LegalManager.Infrastructure.Persistence;
using LegalManager.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace LegalManager.UnitTests;

public class FinanceiroServiceTests
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

    private async Task<(AppDbContext ctx, Guid tenantId)> SeedTenantAsync()
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
    public async Task CriarAsync_DeveCriarLancamento_ComDadosValidos()
    {
        var (ctx, tenantId) = await SeedTenantAsync();
        var service = new FinanceiroService(ctx);

        var dto = new CriarLancamentoDto(TipoLancamento.Receita, "Honorário", 5000m,
            DateTime.UtcNow.AddDays(30), "Serviço prestado");

        var result = await service.CriarAsync(tenantId, dto);

        Assert.NotNull(result);
        Assert.Equal("Honorário", result.Categoria);
        Assert.Equal(5000m, result.Valor);
        Assert.Equal(StatusLancamento.Pendente, result.Status);
    }

    [Fact]
    public async Task GetAllAsync_DeveRetornarApenasLancamentosDoTenant()
    {
        var (ctx, tenantId) = await SeedTenantAsync();
        var outroTenantId = Guid.NewGuid();
        ctx.Tenants.Add(new Tenant { Id = outroTenantId, Nome = "Outro", Plano = PlanoTipo.Free, Status = StatusTenant.Ativo, CriadoEm = DateTime.UtcNow });
        ctx.LancamentosFinanceiros.AddRange(
            new LancamentoFinanceiro { Id = Guid.NewGuid(), TenantId = tenantId, Tipo = TipoLancamento.Receita, Categoria = "H1", Valor = 1000m, DataVencimento = DateTime.UtcNow, Status = StatusLancamento.Pendente },
            new LancamentoFinanceiro { Id = Guid.NewGuid(), TenantId = outroTenantId, Tipo = TipoLancamento.Receita, Categoria = "H2", Valor = 2000m, DataVencimento = DateTime.UtcNow, Status = StatusLancamento.Pendente }
        );
        await ctx.SaveChangesAsync();

        var service = new FinanceiroService(ctx);
        var result = await service.GetAllAsync(tenantId, null, null, null, null, 1, 10);

        Assert.Single(result.Items);
    }

    [Fact]
    public async Task GetAllAsync_DeveFiltrarPorTipo()
    {
        var (ctx, tenantId) = await SeedTenantAsync();
        ctx.LancamentosFinanceiros.AddRange(
            new LancamentoFinanceiro { Id = Guid.NewGuid(), TenantId = tenantId, Tipo = TipoLancamento.Receita, Categoria = "H", Valor = 1000m, DataVencimento = DateTime.UtcNow, Status = StatusLancamento.Pendente },
            new LancamentoFinanceiro { Id = Guid.NewGuid(), TenantId = tenantId, Tipo = TipoLancamento.Despesa, Categoria = "C", Valor = 500m, DataVencimento = DateTime.UtcNow, Status = StatusLancamento.Pendente }
        );
        await ctx.SaveChangesAsync();

        var service = new FinanceiroService(ctx);
        var result = await service.GetAllAsync(tenantId, TipoLancamento.Receita, null, null, null, 1, 10);

        Assert.Single(result.Items);
        Assert.Equal(TipoLancamento.Receita, result.Items.First().Tipo);
    }

    [Fact]
    public async Task GetAllAsync_DeveFiltrarPorStatus()
    {
        var (ctx, tenantId) = await SeedTenantAsync();
        ctx.LancamentosFinanceiros.AddRange(
            new LancamentoFinanceiro { Id = Guid.NewGuid(), TenantId = tenantId, Tipo = TipoLancamento.Receita, Categoria = "H", Valor = 1000m, DataVencimento = DateTime.UtcNow, Status = StatusLancamento.Pendente },
            new LancamentoFinanceiro { Id = Guid.NewGuid(), TenantId = tenantId, Tipo = TipoLancamento.Receita, Categoria = "H2", Valor = 2000m, DataVencimento = DateTime.UtcNow, Status = StatusLancamento.Pago }
        );
        await ctx.SaveChangesAsync();

        var service = new FinanceiroService(ctx);
        var result = await service.GetAllAsync(tenantId, null, StatusLancamento.Pago, null, null, 1, 10);

        Assert.Single(result.Items);
    }

    [Fact]
    public async Task GetByIdAsync_DeveRetornarLancamento_QuandoExistir()
    {
        var (ctx, tenantId) = await SeedTenantAsync();
        var lancId = Guid.NewGuid();
        ctx.LancamentosFinanceiros.Add(new LancamentoFinanceiro
        {
            Id = lancId, TenantId = tenantId, Tipo = TipoLancamento.Receita,
            Categoria = "Honorário", Valor = 3000m, DataVencimento = DateTime.UtcNow,
            Status = StatusLancamento.Pendente
        });
        await ctx.SaveChangesAsync();

        var service = new FinanceiroService(ctx);
        var result = await service.GetByIdAsync(lancId, tenantId);

        Assert.NotNull(result);
        Assert.Equal(3000m, result.Valor);
    }

    [Fact]
    public async Task GetByIdAsync_DeveRetornarNull_QuandoNaoExistir()
    {
        var (ctx, tenantId) = await SeedTenantAsync();
        var service = new FinanceiroService(ctx);

        var result = await service.GetByIdAsync(Guid.NewGuid(), tenantId);

        Assert.Null(result);
    }

    [Fact]
    public async Task PagarAsync_DeveAtualizarStatusEPopularDataPagamento()
    {
        var (ctx, tenantId) = await SeedTenantAsync();
        var lancId = Guid.NewGuid();
        ctx.LancamentosFinanceiros.Add(new LancamentoFinanceiro
        {
            Id = lancId, TenantId = tenantId, Tipo = TipoLancamento.Receita,
            Categoria = "H", Valor = 1000m, DataVencimento = DateTime.UtcNow.AddDays(-5),
            Status = StatusLancamento.Pendente
        });
        await ctx.SaveChangesAsync();

        var service = new FinanceiroService(ctx);
        var dataPag = DateTime.UtcNow.AddDays(-1);
        await service.PagarAsync(lancId, tenantId, dataPag);

        var updated = await ctx.LancamentosFinanceiros.FindAsync(lancId);
        Assert.Equal(StatusLancamento.Pago, updated!.Status);
        Assert.NotNull(updated.DataPagamento);
    }

    [Fact]
    public async Task CancelarAsync_DeveAtualizarStatus()
    {
        var (ctx, tenantId) = await SeedTenantAsync();
        var lancId = Guid.NewGuid();
        ctx.LancamentosFinanceiros.Add(new LancamentoFinanceiro
        {
            Id = lancId, TenantId = tenantId, Tipo = TipoLancamento.Receita,
            Categoria = "H", Valor = 1000m, DataVencimento = DateTime.UtcNow,
            Status = StatusLancamento.Pendente
        });
        await ctx.SaveChangesAsync();

        var service = new FinanceiroService(ctx);
        await service.CancelarAsync(lancId, tenantId);

        var updated = await ctx.LancamentosFinanceiros.FindAsync(lancId);
        Assert.Equal(StatusLancamento.Cancelado, updated!.Status);
    }

    [Fact]
    public async Task AtualizarAsync_DeveAtualizarCampos()
    {
        var (ctx, tenantId) = await SeedTenantAsync();
        var lancId = Guid.NewGuid();
        ctx.LancamentosFinanceiros.Add(new LancamentoFinanceiro
        {
            Id = lancId, TenantId = tenantId, Tipo = TipoLancamento.Receita,
            Categoria = "H", Valor = 1000m, DataVencimento = DateTime.UtcNow,
            Status = StatusLancamento.Pendente
        });
        await ctx.SaveChangesAsync();

        var service = new FinanceiroService(ctx);
        var dto = new AtualizarLancamentoDto("Novo Honorário", 5000m, DateTime.UtcNow.AddDays(15), "Nova descrição");
        await service.AtualizarAsync(lancId, tenantId, dto);

        var updated = await service.GetByIdAsync(lancId, tenantId);
        Assert.Equal("Novo Honorário", updated!.Categoria);
        Assert.Equal(5000m, updated.Valor);
    }

    [Fact]
    public async Task GetResumoCompletoAsync_DeveCalcularResumoMesEAno()
    {
        var (ctx, tenantId) = await SeedTenantAsync();
        var now = DateTime.UtcNow;
        ctx.LancamentosFinanceiros.AddRange(
            new LancamentoFinanceiro { Id = Guid.NewGuid(), TenantId = tenantId, Tipo = TipoLancamento.Receita, Categoria = "H", Valor = 5000m, DataVencimento = new DateTime(now.Year, now.Month, 10), DataPagamento = new DateTime(now.Year, now.Month, 10), Status = StatusLancamento.Pago },
            new LancamentoFinanceiro { Id = Guid.NewGuid(), TenantId = tenantId, Tipo = TipoLancamento.Despesa, Categoria = "SW", Valor = 1000m, DataVencimento = new DateTime(now.Year, now.Month, 15), DataPagamento = new DateTime(now.Year, now.Month, 15), Status = StatusLancamento.Pago }
        );
        await ctx.SaveChangesAsync();

        var service = new FinanceiroService(ctx);
        var result = await service.GetResumoCompletoAsync(tenantId, now.Year, now.Month);

        Assert.Equal(5000m, result.Mes.TotalReceitas);
        Assert.Equal(1000m, result.Mes.TotalDespesas);
        Assert.Equal(4000m, result.Mes.Saldo);
    }

    [Fact]
    public async Task GetAllAsync_DevePaginarCorretamente()
    {
        var (ctx, tenantId) = await SeedTenantAsync();
        for (int i = 0; i < 25; i++)
        {
            ctx.LancamentosFinanceiros.Add(new LancamentoFinanceiro
            {
                Id = Guid.NewGuid(), TenantId = tenantId, Tipo = TipoLancamento.Receita,
                Categoria = $"H{i}", Valor = 1000m + i,
                DataVencimento = DateTime.UtcNow.AddDays(-i), Status = StatusLancamento.Pendente
            });
        }
        await ctx.SaveChangesAsync();

        var service = new FinanceiroService(ctx);
        var page1 = await service.GetAllAsync(tenantId, null, null, null, null, 1, 10);
        var page3 = await service.GetAllAsync(tenantId, null, null, null, null, 3, 10);

        Assert.Equal(10, page1.Items.Count());
        Assert.Equal(5, page3.Items.Count());
        Assert.Equal(25, page1.Total);
    }

    [Fact]
    public async Task PagarAsync_QuandoVinculadoAParcela_DeveMarcarParcelaComoPaga()
    {
        var (ctx, tenantId) = await SeedTenantAsync();
        var contatoId = Guid.NewGuid();
        ctx.Contatos.Add(new Contato
        {
            Id = contatoId, TenantId = tenantId, Nome = "Cliente", Tipo = TipoPessoa.PF,
            TipoContato = TipoContato.Cliente, CriadoEm = DateTime.UtcNow, Ativo = true
        });

        var contratoId = Guid.NewGuid();
        var contrato = new ContratoHonorario
        {
            Id = contratoId, TenantId = tenantId, ContatoId = contatoId,
            NumeroContrato = "HON-2026/0001", Objeto = "X", ValorTotal = 2000m,
            FormaPagamento = FormaPagamentoContrato.Parcelado,
            Periodicidade = PeriodicidadeParcela.Mensal, NumeroParcelas = 2,
            DataPrimeiraParcela = DateTime.UtcNow.Date.AddDays(15),
            PercentualMulta = 0.02m, PercentualJurosMensal = 0.015m,
            TipoCobranca = "Boleto/PIX", Status = StatusContratoHonorario.Ativo,
            CriadoPorId = Guid.NewGuid(), DataInicio = DateTime.UtcNow.Date.AddDays(15)
        };
        ctx.ContratosHonorarios.Add(contrato);

        var parcelaId = Guid.NewGuid();
        var parcela = new ParcelaHonorario
        {
            Id = parcelaId, TenantId = tenantId, ContratoId = contratoId,
            Numero = 1, IsEntrada = false, Vencimento = DateTime.UtcNow.Date.AddDays(15),
            ValorOriginal = 1000m, Status = StatusParcelaHonorario.Pendente
        };
        ctx.ParcelasHonorarios.Add(parcela);
        parcela.Contrato = contrato;

        var lancId = Guid.NewGuid();
        ctx.LancamentosFinanceiros.Add(new LancamentoFinanceiro
        {
            Id = lancId, TenantId = tenantId, Tipo = TipoLancamento.Receita,
            Categoria = "Honorario", Valor = 1000m,
            DataVencimento = DateTime.UtcNow.Date.AddDays(15),
            ContratoHonorarioId = contratoId, ParcelaHonorarioId = parcelaId,
            Status = StatusLancamento.Pendente
        });
        await ctx.SaveChangesAsync();

        var service = new FinanceiroService(ctx);
        var dataPg = new DateTime(2026, 9, 1);
        await service.PagarAsync(lancId, tenantId, dataPg);

        var lanc = await ctx.LancamentosFinanceiros.FindAsync(lancId);
        Assert.Equal(StatusLancamento.Pago, lanc!.Status);
        Assert.Equal(dataPg, lanc.DataPagamento);

        var parcAtualizada = await ctx.ParcelasHonorarios.FindAsync(parcelaId);
        Assert.Equal(StatusParcelaHonorario.Pago, parcAtualizada!.Status);
        Assert.Equal(dataPg, parcAtualizada.DataPagamento);
        Assert.Equal(1000m, parcAtualizada.ValorPago);
        Assert.Equal(lancId, parcAtualizada.LancamentoFinanceiroId);
    }

    [Fact]
    public async Task PagarAsync_QuandoUltimaParcela_DeveMarcarContratoComoQuitado()
    {
        var (ctx, tenantId) = await SeedTenantAsync();
        var contatoId = Guid.NewGuid();
        ctx.Contatos.Add(new Contato
        {
            Id = contatoId, TenantId = tenantId, Nome = "Cliente", Tipo = TipoPessoa.PF,
            TipoContato = TipoContato.Cliente, CriadoEm = DateTime.UtcNow, Ativo = true
        });

        var contratoId = Guid.NewGuid();
        var parcelaId = Guid.NewGuid();
        var contrato = new ContratoHonorario
        {
            Id = contratoId, TenantId = tenantId, ContatoId = contatoId,
            NumeroContrato = "HON-2026/0002", Objeto = "X", ValorTotal = 1000m,
            FormaPagamento = FormaPagamentoContrato.AVista,
            Periodicidade = null, NumeroParcelas = null,
            DataPrimeiraParcela = DateTime.UtcNow.Date.AddDays(30),
            PercentualMulta = 0.02m, PercentualJurosMensal = 0.015m,
            TipoCobranca = "Boleto/PIX", Status = StatusContratoHonorario.Ativo,
            CriadoPorId = Guid.NewGuid(), DataInicio = DateTime.UtcNow.Date.AddDays(30)
        };
        var parcela = new ParcelaHonorario
        {
            Id = parcelaId, TenantId = tenantId, ContratoId = contratoId,
            Numero = 1, IsEntrada = true, Vencimento = DateTime.UtcNow.Date.AddDays(30),
            ValorOriginal = 1000m, Status = StatusParcelaHonorario.Pendente
        };
        ctx.ContratosHonorarios.Add(contrato);
        ctx.ParcelasHonorarios.Add(parcela);
        parcela.Contrato = contrato;

        var lancId = Guid.NewGuid();
        ctx.LancamentosFinanceiros.Add(new LancamentoFinanceiro
        {
            Id = lancId, TenantId = tenantId, Tipo = TipoLancamento.Receita,
            Categoria = "Honorario", Valor = 1000m,
            DataVencimento = DateTime.UtcNow.Date.AddDays(30),
            ContratoHonorarioId = contratoId, ParcelaHonorarioId = parcelaId,
            Status = StatusLancamento.Pendente
        });
        await ctx.SaveChangesAsync();

        var service = new FinanceiroService(ctx);
        await service.PagarAsync(lancId, tenantId, DateTime.UtcNow.Date);

        var contratoAtualizado = await ctx.ContratosHonorarios.FindAsync(contratoId);
        Assert.Equal(StatusContratoHonorario.Quitado, contratoAtualizado!.Status);
    }

    [Fact]
    public async Task CancelarAsync_QuandoLancamentoVinculadoAParcelaPaga_DeveVoltarParcelaParaPendente()
    {
        var (ctx, tenantId) = await SeedTenantAsync();
        var contatoId = Guid.NewGuid();
        ctx.Contatos.Add(new Contato
        {
            Id = contatoId, TenantId = tenantId, Nome = "Cliente", Tipo = TipoPessoa.PF,
            TipoContato = TipoContato.Cliente, CriadoEm = DateTime.UtcNow, Ativo = true
        });

        var contratoId = Guid.NewGuid();
        var contrato = new ContratoHonorario
        {
            Id = contratoId, TenantId = tenantId, ContatoId = contatoId,
            NumeroContrato = "HON-2026/0003", Objeto = "X", ValorTotal = 1000m,
            FormaPagamento = FormaPagamentoContrato.AVista,
            Periodicidade = null, NumeroParcelas = null,
            DataPrimeiraParcela = DateTime.UtcNow.Date.AddDays(30),
            PercentualMulta = 0.02m, PercentualJurosMensal = 0.015m,
            TipoCobranca = "Boleto/PIX", Status = StatusContratoHonorario.Quitado,
            CriadoPorId = Guid.NewGuid(), DataInicio = DateTime.UtcNow.Date.AddDays(30)
        };
        var parcelaId = Guid.NewGuid();
        var parcela = new ParcelaHonorario
        {
            Id = parcelaId, TenantId = tenantId, ContratoId = contratoId,
            Numero = 1, IsEntrada = true, Vencimento = DateTime.UtcNow.Date.AddDays(30),
            ValorOriginal = 1000m, Status = StatusParcelaHonorario.Pago,
            DataPagamento = DateTime.UtcNow.Date, ValorPago = 1000m
        };
        ctx.ContratosHonorarios.Add(contrato);
        ctx.ParcelasHonorarios.Add(parcela);
        parcela.Contrato = contrato;

        var lancId = Guid.NewGuid();
        ctx.LancamentosFinanceiros.Add(new LancamentoFinanceiro
        {
            Id = lancId, TenantId = tenantId, Tipo = TipoLancamento.Receita,
            Categoria = "Honorario", Valor = 1000m,
            DataVencimento = DateTime.UtcNow.Date.AddDays(30),
            DataPagamento = DateTime.UtcNow.Date,
            ContratoHonorarioId = contratoId, ParcelaHonorarioId = parcelaId,
            Status = StatusLancamento.Pago
        });
        parcela.LancamentoFinanceiroId = lancId;
        await ctx.SaveChangesAsync();

        var service = new FinanceiroService(ctx);
        await service.CancelarAsync(lancId, tenantId);

        var parc = await ctx.ParcelasHonorarios.FindAsync(parcelaId);
        Assert.Equal(StatusParcelaHonorario.Pendente, parc!.Status);
        Assert.Null(parc.DataPagamento);
        Assert.Null(parc.ValorPago);
        Assert.Null(parc.LancamentoFinanceiroId);

        var contratoAtualizado = await ctx.ContratosHonorarios.FindAsync(contratoId);
        Assert.Equal(StatusContratoHonorario.Ativo, contratoAtualizado!.Status);
    }

    [Fact]
    public async Task PagarAsync_QuandoLancamentoSemParcela_NaoDeveLancarExcecao()
    {
        var (ctx, tenantId) = await SeedTenantAsync();
        var lancId = Guid.NewGuid();
        ctx.LancamentosFinanceiros.Add(new LancamentoFinanceiro
        {
            Id = lancId, TenantId = tenantId, Tipo = TipoLancamento.Receita,
            Categoria = "Custas", Valor = 500m, DataVencimento = DateTime.UtcNow.AddDays(5),
            Status = StatusLancamento.Pendente
        });
        await ctx.SaveChangesAsync();

        var service = new FinanceiroService(ctx);
        await service.PagarAsync(lancId, tenantId, DateTime.UtcNow);

        var lanc = await ctx.LancamentosFinanceiros.FindAsync(lancId);
        Assert.Equal(StatusLancamento.Pago, lanc!.Status);
    }

    [Fact]
    public async Task GetAllAsync_LancamentoPago_DeveAparecerNoMesDaDataPagamento_NaoNoVencimento()
    {
        var (ctx, tenantId) = await SeedTenantAsync();
        var vencSet = new DateTime(2026, 9, 10);
        var pagAgo = new DateTime(2026, 8, 8);
        ctx.LancamentosFinanceiros.Add(
            new LancamentoFinanceiro { Id = Guid.NewGuid(), TenantId = tenantId, Tipo = TipoLancamento.Receita, Categoria = "H", Valor = 810.50m, DataVencimento = vencSet, DataPagamento = pagAgo, Status = StatusLancamento.Pago }
        );
        await ctx.SaveChangesAsync();

        var service = new FinanceiroService(ctx);

        var emSet = await service.GetAllAsync(tenantId, null, null, null, null, 1, 50, 9, 2026);
        var emAgo = await service.GetAllAsync(tenantId, null, null, null, null, 1, 50, 8, 2026);

        Assert.Empty(emSet.Items);
        Assert.Single(emAgo.Items);
        Assert.Equal(810.50m, emAgo.Items.First().Valor);
    }

    [Fact]
    public async Task GetAllAsync_LancamentoPendente_DeveAparecerNoMesDoVencimento()
    {
        var (ctx, tenantId) = await SeedTenantAsync();
        var vencSet = new DateTime(2026, 9, 10);
        ctx.LancamentosFinanceiros.Add(
            new LancamentoFinanceiro { Id = Guid.NewGuid(), TenantId = tenantId, Tipo = TipoLancamento.Receita, Categoria = "H", Valor = 810.50m, DataVencimento = vencSet, Status = StatusLancamento.Pendente }
        );
        await ctx.SaveChangesAsync();

        var service = new FinanceiroService(ctx);
        var emSet = await service.GetAllAsync(tenantId, null, null, null, null, 1, 50, 9, 2026);
        var emAgo = await service.GetAllAsync(tenantId, null, null, null, null, 1, 50, 8, 2026);

        Assert.Single(emSet.Items);
        Assert.Empty(emAgo.Items);
    }

    [Fact]
    public async Task GetResumoCompletoAsync_DeveContarReceitasPagasPelaDataPagamento()
    {
        var (ctx, tenantId) = await SeedTenantAsync();
        ctx.LancamentosFinanceiros.AddRange(
            new LancamentoFinanceiro { Id = Guid.NewGuid(), TenantId = tenantId, Tipo = TipoLancamento.Receita, Categoria = "H", Valor = 810.50m, DataVencimento = new DateTime(2026, 9, 10), DataPagamento = new DateTime(2026, 8, 8), Status = StatusLancamento.Pago },
            new LancamentoFinanceiro { Id = Guid.NewGuid(), TenantId = tenantId, Tipo = TipoLancamento.Receita, Categoria = "H", Valor = 1000m, DataVencimento = new DateTime(2026, 9, 5), DataPagamento = new DateTime(2026, 9, 5), Status = StatusLancamento.Pago }
        );
        await ctx.SaveChangesAsync();

        var service = new FinanceiroService(ctx);
        var resumoSet = await service.GetResumoCompletoAsync(tenantId, 2026, 9);
        var resumoAgo = await service.GetResumoCompletoAsync(tenantId, 2026, 8);

        Assert.Equal(1000m, resumoSet.Mes.TotalReceitas);
        Assert.Equal(810.50m, resumoAgo.Mes.TotalReceitas);
    }

    [Fact]
    public async Task GetResumoCompletoAsync_DeveIncluirParcelasHonorariosPendentesEmAReceberEVencidas()
    {
        var (ctx, tenantId) = await SeedTenantAsync();
        var hoje = DateTime.UtcNow.Date;

        var contratoId = Guid.NewGuid();
        ctx.ContratosHonorarios.Add(new ContratoHonorario
        {
            Id = contratoId, TenantId = tenantId,
            NumeroContrato = "HON-TEST", Objeto = "X",
            ValorTotal = 4000m, FormaPagamento = FormaPagamentoContrato.Parcelado,
            Periodicidade = PeriodicidadeParcela.Mensal, NumeroParcelas = 4,
            DataPrimeiraParcela = hoje.AddMonths(-3),
            Status = StatusContratoHonorario.Ativo,
            DataInicio = hoje.AddMonths(-3),
            PercentualMulta = 0.02m, PercentualJurosMensal = 0.015m,
            TipoCobranca = "Boleto", CriadoEm = DateTime.UtcNow
        });

        // 3 parcelas vencidas (meses -3, -2, -1)
        for (int i = 0; i < 3; i++)
        {
            ctx.ParcelasHonorarios.Add(new ParcelaHonorario
            {
                Id = Guid.NewGuid(), TenantId = tenantId, ContratoId = contratoId, Numero = i + 1,
                Vencimento = hoje.AddMonths(-(3 - i)), ValorOriginal = 1000m,
                Status = StatusParcelaHonorario.Pendente, CriadoEm = DateTime.UtcNow
            });
        }
        // 1 parcela a vencer (mês +1)
        ctx.ParcelasHonorarios.Add(new ParcelaHonorario
        {
            Id = Guid.NewGuid(), TenantId = tenantId, ContratoId = contratoId, Numero = 4,
            Vencimento = hoje.AddMonths(1), ValorOriginal = 1000m,
            Status = StatusParcelaHonorario.Pendente, CriadoEm = DateTime.UtcNow
        });

        // contrato Encerrado — deve ser IGNORADO mesmo com parcela pendente
        var contratoEncId = Guid.NewGuid();
        ctx.ContratosHonorarios.Add(new ContratoHonorario
        {
            Id = contratoEncId, TenantId = tenantId, NumeroContrato = "HON-ENC",
            Objeto = "Y", ValorTotal = 5000m, FormaPagamento = FormaPagamentoContrato.AVista,
            Status = StatusContratoHonorario.Encerrado,
            DataInicio = hoje.AddMonths(-3), PercentualMulta = 0.02m, PercentualJurosMensal = 0.015m,
            TipoCobranca = "Boleto", CriadoEm = DateTime.UtcNow
        });
        ctx.ParcelasHonorarios.Add(new ParcelaHonorario
        {
            Id = Guid.NewGuid(), TenantId = tenantId, ContratoId = contratoEncId, Numero = 1,
            Vencimento = hoje.AddMonths(-2), ValorOriginal = 5000m,
            Status = StatusParcelaHonorario.Pendente, CriadoEm = DateTime.UtcNow
        });

        await ctx.SaveChangesAsync();

        var service = new FinanceiroService(ctx);
        var resumo = await service.GetResumoCompletoAsync(tenantId, hoje.Year, hoje.Month);

        // Ano (acumulado): 4 parcelas ativas (4 × 1000 = 4000), 3 vencidas (3000).
        // Os R$ 5000 do contrato Encerrado são IGNORADOS.
        Assert.Equal(4000m, resumo.Ano.ReceitasPendentes);
        Assert.Equal(3000m, resumo.Ano.ReceitasVencidas);

        // Mês atual: nenhuma parcela vence no mês corrente
        Assert.Equal(0m, resumo.Mes.ReceitasPendentes);
        Assert.Equal(0m, resumo.Mes.ReceitasVencidas);
    }
}
