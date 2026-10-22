using LegalManager.Application.Interfaces;
using LegalManager.Domain.Entities;
using LegalManager.Domain.Enums;
using LegalManager.Infrastructure.Jobs;
using LegalManager.Infrastructure.Persistence;
using LegalManager.Infrastructure.Services;
using LegalManager.UnitTests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace LegalManager.UnitTests;

public class EscavadorMovimentacoesPollingJobTests
{
    private const string Cnj = "1001885-87.2026.8.26.0077";

    private static AppDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static EscavadorMovimentacaoDto Mov(long id, DateTime data, string texto) => new(
        Id: id, Uuid: $"v2-mov-{id}", Data: data, ConteudoHtml: texto, Snippet: texto, Tipo: "PUBLICACAO",
        Diario: "Diário de Justiça do Estado de São Paulo", DiarioId: null, DiarioSigla: "DJSP", OrigemEstado: null,
        Link: null, LinkApi: null, LinkPdf: null, LinkPdfApi: null, NumeroCnj: Cnj, JsonBruto: "{}");

    private static async Task<(AppDbContext ctx, Processo processo)> SeedAsync(PlanoTipo plano, DateTime ultimoAndamento, string textoUltimo)
    {
        var ctx = CreateContext();
        var tenant = new Tenant { Id = Guid.NewGuid(), Nome = "NPJ", Plano = plano, Status = StatusTenant.Ativo, CriadoEm = DateTime.UtcNow };
        var processo = new Processo
        {
            Id = Guid.NewGuid(), TenantId = tenant.Id, NumeroCNJ = Cnj, AreaDireito = AreaDireito.Civil,
            Fase = FaseProcessual.Execucao, Status = StatusProcesso.Ativo, Monitorado = true,
            AdvogadoResponsavelId = Guid.NewGuid(), CriadoEm = DateTime.UtcNow
        };
        ctx.Tenants.Add(tenant);
        ctx.Processos.Add(processo);
        ctx.Andamentos.Add(new Andamento
        {
            Id = Guid.NewGuid(), ProcessoId = processo.Id, TenantId = tenant.Id, Data = ultimoAndamento,
            Descricao = textoUltimo, Tipo = TipoAndamento.Despacho, Fonte = FonteAndamento.Automatico, CriadoEm = DateTime.UtcNow
        });
        await ctx.SaveChangesAsync();
        return (ctx, processo);
    }

    private static EscavadorMovimentacoesPollingJob Job(AppDbContext ctx, params EscavadorMovimentacaoDto[] movs)
    {
        var escavador = new Mock<IEscavadorService>();
        escavador
            .Setup(e => e.ListarMovimentacoesPorProcessoAsync(Cnj, It.IsAny<DateTime?>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EscavadorPagedResult<EscavadorMovimentacaoDto>(movs.ToList(), movs.Length, 1, 1, false));
        return new EscavadorMovimentacoesPollingJob(ctx, escavador.Object, new PublicacaoMapper(),
            FakeConsultaExternaLogService.Instance, NullLogger<EscavadorMovimentacoesPollingJob>.Instance);
    }

    [Fact]
    public async Task PlanoFree_ProcessoMonitorado_ImportaSoAsMovimentacoesNovas()
    {
        var hoje = DateTime.UtcNow.Date;
        var (ctx, processo) = await SeedAsync(PlanoTipo.Free, hoje.AddDays(-60), "Despacho de 60 dias atrás");
        var job = Job(ctx,
            Mov(1, hoje.AddDays(-90), "Anterior ao último andamento — não entra"),
            Mov(2, hoje.AddDays(-60), "Despacho de 60 dias atrás"),
            Mov(3, hoje.AddDays(-30), "Intimação antiga"),
            Mov(4, hoje.AddDays(-2), "Intimação recente"));

        await job.ExecutarAsync();

        var textos = await ctx.Andamentos.Where(a => a.ProcessoId == processo.Id).Select(a => a.Descricao).ToListAsync();
        Assert.Equal(3, textos.Count);
        Assert.Contains("Intimação antiga", textos);
        Assert.Contains("Intimação recente", textos);
        Assert.Equal(2, await ctx.Publicacoes.CountAsync());
        Assert.Equal(hoje.AddDays(-2), (await ctx.Processos.SingleAsync()).UltimoAndamentoEm);
        // Recuperação de histórico não notifica: só o que é dos últimos 7 dias.
        Assert.Equal("Intimação recente", (await ctx.Notificacoes.SingleAsync()).Mensagem);
    }

    [Fact]
    public async Task SegundaExecucao_NaoDuplica()
    {
        var hoje = DateTime.UtcNow.Date;
        var (ctx, processo) = await SeedAsync(PlanoTipo.Plus, hoje.AddDays(-10), "Último");
        var movs = new[] { Mov(1, hoje.AddDays(-1), "Novo"), Mov(2, hoje, "Mais novo") };

        await Job(ctx, movs).ExecutarAsync();
        await Job(ctx, movs).ExecutarAsync();

        Assert.Equal(3, await ctx.Andamentos.CountAsync(a => a.ProcessoId == processo.Id));
        Assert.Equal(2, await ctx.Publicacoes.CountAsync());
    }

    [Fact]
    public async Task MovimentacaoJaRecebidaPeloWebhook_ComOutroUuid_NaoDuplica()
    {
        var hoje = DateTime.UtcNow.Date;
        var (ctx, processo) = await SeedAsync(PlanoTipo.Pro, hoje.AddDays(-1), "Veio pelo webhook");

        await Job(ctx, Mov(99, hoje.AddDays(-1), "Veio pelo webhook")).ExecutarAsync();

        Assert.Equal(1, await ctx.Andamentos.CountAsync(a => a.ProcessoId == processo.Id));
    }
}
