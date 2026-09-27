using LegalManager.API.Controllers;
using LegalManager.Application.DTOs.Monitoramento;
using LegalManager.Application.Interfaces;
using LegalManager.Domain.Entities;
using LegalManager.Domain.Enums;
using LegalManager.Domain.Interfaces;
using LegalManager.Infrastructure.Persistence;
using LegalManager.Infrastructure.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace LegalManager.IntegrationTests;

/// <summary>
/// Botão "Consultar" (POST /api/processos/{id}/monitoramento/executar): quando nem e-SAJ nem
/// DataJud encontram o processo, o Escavador é consultado como último recurso (nunca no plano Free).
/// </summary>
public class ConsultarAndamentosEscavadorTests
{
    private const string Cnj = "5005726-50.2022.4.04.7112";

    private static async Task<(AppDbContext Ctx, Guid TenantId, Guid ProcessoId)> SeedAsync()
    {
        var ctx = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var tenantId = Guid.NewGuid();
        var processo = new Processo
        {
            Id = Guid.NewGuid(), TenantId = tenantId, NumeroCNJ = Cnj, Tribunal = "TRF4",
            AreaDireito = AreaDireito.Previdenciario, Fase = FaseProcessual.Conhecimento,
            Status = StatusProcesso.Ativo, CriadoEm = DateTime.UtcNow
        };
        ctx.Processos.Add(processo);
        ctx.Andamentos.Add(new Andamento
        {
            Id = Guid.NewGuid(), ProcessoId = processo.Id, TenantId = tenantId,
            Data = new DateTime(2026, 9, 10, 14, 30, 0), Tipo = TipoAndamento.Outro,
            Descricao = "Juntada de petição (DataJud)", Fonte = FonteAndamento.Automatico, CriadoEm = DateTime.UtcNow
        });
        await ctx.SaveChangesAsync();
        return (ctx, tenantId, processo.Id);
    }

    private static EscavadorMovimentacaoDto Mov(DateTime data, string conteudo, string? tipo = null) =>
        new(1, string.Empty, data, conteudo, conteudo, tipo, "TRF4", null, "TRF4", null,
            null, null, null, null, Cnj, "{}");

    private static ProcessosController CreateController(
        AppDbContext ctx, Guid tenantId, PlanoTipo plano, Mock<IEscavadorService> escavador,
        bool encontradoNoDataJud = false)
    {
        var tenant = new Mock<ITenantContext>();
        tenant.Setup(t => t.TenantId).Returns(tenantId);
        tenant.Setup(t => t.Plano).Returns(plano);

        var monitoramento = new Mock<IMonitoramentoService>();
        monitoramento.Setup(m => m.MonitorarProcessoAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, CancellationToken _) =>
                encontradoNoDataJud
                    ? new MonitoramentoResultDto(id, Cnj, true, 0, null, DateTime.UtcNow)
                    : new MonitoramentoResultDto(id, Cnj, false, 0, "Processo não encontrado no DataJud.", DateTime.UtcNow));

        return new ProcessosController(
            new ProcessoService(ctx, tenant.Object, escavador.Object, FakeConsultaExternaLogService.Instance),
            monitoramento.Object,
            Mock.Of<IAuditService>(),
            tenant.Object,
            null!,
            ctx,
            Mock.Of<IContatoResolverService>(),
            escavador.Object,
            FakeConsultaExternaLogService.Instance);
    }

    private static Mock<IEscavadorService> EscavadorRetornando(params EscavadorMovimentacaoDto[] movs)
    {
        var mock = new Mock<IEscavadorService>();
        mock.Setup(e => e.ListarMovimentacoesPorProcessoAsync(Cnj, null, 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EscavadorPagedResult<EscavadorMovimentacaoDto>(movs, movs.Length, 1, 1, false));
        return mock;
    }

    [Fact]
    public async Task NaoEncontradoNoDataJud_ImportaDoEscavadorSoMovimentacoesPosterioresAoUltimoAndamento()
    {
        var (ctx, tenantId, processoId) = await SeedAsync();
        var escavador = EscavadorRetornando(
            Mov(new DateTime(2026, 9, 1), "Juntada de petição"),            // já coberta pelo DataJud
            Mov(new DateTime(2026, 9, 20), "<p>Sentença   de procedência</p>"),
            Mov(new DateTime(2026, 9, 22), "Intimação eletrônica"));

        var result = await CreateController(ctx, tenantId, PlanoTipo.Pro, escavador)
            .ExecutarMonitoramento(processoId, CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);
        var andamentos = await ctx.Andamentos.Where(a => a.ProcessoId == processoId).OrderBy(a => a.Data).ToListAsync();
        Assert.Equal(3, andamentos.Count);
        Assert.Equal("Sentença de procedência", andamentos[1].Descricao);
        Assert.Equal(TipoAndamento.Sentenca, andamentos[1].Tipo);
        Assert.Equal(TipoAndamento.Intimacao, andamentos[2].Tipo);
        Assert.NotNull((await ctx.Processos.FindAsync(processoId))!.UltimoMonitoramento);
    }

    [Fact]
    public async Task ConsultarDuasVezes_NaoDuplicaAndamentosDoEscavador()
    {
        var (ctx, tenantId, processoId) = await SeedAsync();
        var escavador = EscavadorRetornando(Mov(new DateTime(2026, 9, 20), "Sentença"));

        await CreateController(ctx, tenantId, PlanoTipo.Pro, escavador).ExecutarMonitoramento(processoId, CancellationToken.None);
        await CreateController(ctx, tenantId, PlanoTipo.Pro, escavador).ExecutarMonitoramento(processoId, CancellationToken.None);

        Assert.Equal(2, await ctx.Andamentos.CountAsync(a => a.ProcessoId == processoId));
    }

    [Fact]
    public async Task EncontradoNoDataJudSemNovidade_NaoConsultaEscavador()
    {
        var (ctx, tenantId, processoId) = await SeedAsync();
        var escavador = EscavadorRetornando(Mov(new DateTime(2026, 9, 20), "Sentença"));

        await CreateController(ctx, tenantId, PlanoTipo.Pro, escavador, encontradoNoDataJud: true)
            .ExecutarMonitoramento(processoId, CancellationToken.None);

        escavador.Verify(e => e.ListarMovimentacoesPorProcessoAsync(
            It.IsAny<string>(), It.IsAny<DateTime?>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.Equal(1, await ctx.Andamentos.CountAsync(a => a.ProcessoId == processoId));
    }

    [Theory]
    [InlineData(PlanoTipo.Plus)]
    [InlineData(PlanoTipo.Pro)]
    public async Task PlanoPago_ConsultaEscavador(PlanoTipo plano)
    {
        var (ctx, tenantId, processoId) = await SeedAsync();
        var escavador = EscavadorRetornando(Mov(new DateTime(2026, 9, 20), "Sentença"));

        await CreateController(ctx, tenantId, plano, escavador).ExecutarMonitoramento(processoId, CancellationToken.None);

        Assert.Equal(2, await ctx.Andamentos.CountAsync(a => a.ProcessoId == processoId));
    }

    [Fact]
    public async Task PlanoFree_NaoConsultaEscavador()
    {
        var (ctx, tenantId, processoId) = await SeedAsync();
        var escavador = EscavadorRetornando(Mov(new DateTime(2026, 9, 20), "Sentença"));

        await CreateController(ctx, tenantId, PlanoTipo.Free, escavador).ExecutarMonitoramento(processoId, CancellationToken.None);

        escavador.Verify(e => e.ListarMovimentacoesPorProcessoAsync(
            It.IsAny<string>(), It.IsAny<DateTime?>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.Equal(1, await ctx.Andamentos.CountAsync(a => a.ProcessoId == processoId));
    }
}
