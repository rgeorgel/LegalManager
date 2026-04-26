using LegalManager.Application.Interfaces;
using LegalManager.Domain.Entities;
using LegalManager.Domain.Enums;
using LegalManager.Infrastructure.Jobs;
using LegalManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;

namespace LegalManager.UnitTests;

public class MonitoramentoJobTests
{
    private AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options);
    }

    private async Task<(AppDbContext ctx, Guid tenantId, Guid advogadoId, Guid processoId)> SeedAsync()
    {
        var ctx = CreateContext();
        var tenantId = Guid.NewGuid();
        var advogadoId = Guid.NewGuid();
        var processoId = Guid.NewGuid();

        ctx.Tenants.Add(new Tenant
        {
            Id = tenantId, Nome = "Test", Plano = PlanoTipo.Pro,
            Status = StatusTenant.Ativo, CriadoEm = DateTime.UtcNow
        });
        ctx.Users.Add(new Usuario
        {
            Id = advogadoId, TenantId = tenantId, Nome = "Advogado",
            Email = "adv@test.com", UserName = "adv@test.com",
            Perfil = PerfilUsuario.Advogado, Ativo = true, CriadoEm = DateTime.UtcNow
        });
        ctx.Processos.Add(new Processo
        {
            Id = processoId, TenantId = tenantId, NumeroCNJ = "0000001-00.2024.8.26.0001",
            AreaDireito = AreaDireito.Civil, Fase = FaseProcessual.Conhecimento,
            Status = StatusProcesso.Ativo, Monitorado = true,
            AdvogadoResponsavelId = advogadoId, CriadoEm = DateTime.UtcNow
        });
        await ctx.SaveChangesAsync();
        return (ctx, tenantId, advogadoId, processoId);
    }

    private static ITribunalAdapter CreateFakeAdapter(
        TribunalConsultaResult? result = null,
        bool supportsTribunal = true)
    {
        var mock = new Mock<ITribunalAdapter>();
        mock.Setup(d => d.Nome).Returns("DataJud (CNJ)");
        mock.Setup(d => d.SuportaTribunal(It.IsAny<string>())).Returns(supportsTribunal);
        mock.Setup(d => d.ConsultarAsync(It.IsAny<string>(), default))
            .ReturnsAsync(result ?? new TribunalConsultaResult(false, null, null, null, []));
        mock.Setup(d => d.ConsultarPorTribunalAsync(It.IsAny<string>(), It.IsAny<string>(), default))
            .ReturnsAsync(result ?? new TribunalConsultaResult(false, null, null, null, []));
        return mock.Object;
    }

    [Fact]
    public async Task ExecutarAsync_DeveNaoProcessar_QuandoNenhumProcessoMonitorado()
    {
        var ctx = CreateContext();
        var mockEmail = new Mock<IEmailService>();
        var job = new MonitoramentoJob(ctx, CreateFakeAdapter(), mockEmail.Object,
            Mock.Of<ILogger<MonitoramentoJob>>());

        await job.ExecutarAsync();

        mockEmail.Verify(e => e.EnviarNovoAndamentoAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task ExecutarAsync_DeveChamarDataJud_QuandoProcessoMonitorado()
    {
        var (ctx, tenantId, _, processoId) = await SeedAsync();
        var dataJudMock = new Mock<ITribunalAdapter>();
        dataJudMock.Setup(d => d.ConsultarAsync(It.IsAny<string>(), default))
            .ReturnsAsync(new TribunalConsultaResult(false, null, null, null, []));

        var job = new MonitoramentoJob(ctx, dataJudMock.Object, Mock.Of<IEmailService>(),
            Mock.Of<ILogger<MonitoramentoJob>>());
        await job.ExecutarAsync();

        dataJudMock.Verify(d => d.ConsultarAsync("0000001-00.2024.8.26.0001", default), Times.Once);
    }

    [Fact]
    public async Task ExecutarAsync_DeveSalvarAndamentosNovos()
    {
        var (ctx, tenantId, _, processoId) = await SeedAsync();

        var movimentos = new List<TribunalMovimento>
        {
            new("Distribuição", new DateTime(2024, 1, 15), "Petição", null)
        };
        var consultaResult = new TribunalConsultaResult(true, "TJSP", "1ª Vara Cível", "São Paulo", movimentos);
        var dataJud = CreateFakeAdapter(consultaResult);

        var job = new MonitoramentoJob(ctx, dataJud, Mock.Of<IEmailService>(),
            Mock.Of<ILogger<MonitoramentoJob>>());
        await job.ExecutarAsync();

        var andamentos = await ctx.Andamentos.Where(a => a.ProcessoId == processoId).ToListAsync();
        Assert.Single(andamentos);
        Assert.Equal("Distribuição", andamentos[0].Descricao);
        Assert.Equal(FonteAndamento.Automatico, andamentos[0].Fonte);
    }

    [Fact]
    public async Task ExecutarAsync_NaoDeveDuplicarAndamentos_QuandoJaExistente()
    {
        var (ctx, tenantId, _, processoId) = await SeedAsync();

        ctx.Andamentos.Add(new Andamento
        {
            Id = Guid.NewGuid(), ProcessoId = processoId, TenantId = tenantId,
            Data = new DateTime(2024, 1, 15), Tipo = TipoAndamento.Peticao,
            Descricao = "Distribuição", Fonte = FonteAndamento.Automatico,
            CriadoEm = DateTime.UtcNow
        });
        await ctx.SaveChangesAsync();

        var movimentos = new List<TribunalMovimento>
        {
            new("Distribuição", new DateTime(2024, 1, 15), "Petição", null)
        };
        var consultaResult = new TribunalConsultaResult(true, "TJSP", "1ª Vara Cível", "São Paulo", movimentos);
        var dataJud = CreateFakeAdapter(consultaResult);

        var job = new MonitoramentoJob(ctx, dataJud, Mock.Of<IEmailService>(),
            Mock.Of<ILogger<MonitoramentoJob>>());
        await job.ExecutarAsync();

        var andamentos = await ctx.Andamentos.Where(a => a.ProcessoId == processoId).ToListAsync();
        Assert.Single(andamentos);
    }

    [Fact]
    public async Task ExecutarAsync_DeveNotificarAdvogado_QuandoNovoAndamento()
    {
        var (ctx, tenantId, advogadoId, processoId) = await SeedAsync();

        var movimentos = new List<TribunalMovimento>
        {
            new("Sentença", new DateTime(2024, 2, 1), "Sentença", null)
        };
        var consultaResult = new TribunalConsultaResult(true, "TJSP", "1ª Vara Cível", "São Paulo", movimentos);
        var dataJud = CreateFakeAdapter(consultaResult);
        var mockEmail = new Mock<IEmailService>();

        var job = new MonitoramentoJob(ctx, dataJud, mockEmail.Object,
            Mock.Of<ILogger<MonitoramentoJob>>());
        await job.ExecutarAsync();

        mockEmail.Verify(e => e.EnviarNovoAndamentoAsync(
            "adv@test.com", "Advogado", "0000001-00.2024.8.26.0001", "Sentença"), Times.Once);

        var notif = await ctx.Notificacoes.FirstOrDefaultAsync(n => n.Titulo.Contains("Novo andamento"));
        Assert.NotNull(notif);
    }

    [Fact]
    public async Task ExecutarAsync_DeveAtualizarTribunal_QuandoNaoEstiverDefinido()
    {
        var (ctx, tenantId, _, processoId) = await SeedAsync();

        var movimentos = new List<TribunalMovimento>
        {
            new("Despacho", new DateTime(2024, 1, 20), "Despacho", null)
        };
        var consultaResult = new TribunalConsultaResult(true, "TJSP", "3ª Vara Cível", "Campinas", movimentos);
        var dataJud = CreateFakeAdapter(consultaResult);

        var job = new MonitoramentoJob(ctx, dataJud, Mock.Of<IEmailService>(),
            Mock.Of<ILogger<MonitoramentoJob>>());
        await job.ExecutarAsync();

        var processo = await ctx.Processos.FindAsync(processoId);
        Assert.Equal("TJSP", processo!.Tribunal);
    }

    [Theory]
    [InlineData("despacho", TipoAndamento.Despacho)]
    [InlineData("decisão", TipoAndamento.Decisao)]
    [InlineData("sentença", TipoAndamento.Sentenca)]
    [InlineData("acórdão", TipoAndamento.Acordao)]
    [InlineData("audiência", TipoAndamento.Audiencia)]
    [InlineData("intimação", TipoAndamento.Intimacao)]
    [InlineData("publicação", TipoAndamento.Publicacao)]
    [InlineData("peticao", TipoAndamento.Peticao)]
    [InlineData("desconhecidoxyz", TipoAndamento.Outro)]
    public void MapearTipo_DeveMapearCorretamente(string input, TipoAndamento expected)
    {
        var tipo = input.ToLowerInvariant() switch
        {
            var s when s.Contains("despacho")   => TipoAndamento.Despacho,
            var s when s.Contains("decis")       => TipoAndamento.Decisao,
            var s when s.Contains("senten")      => TipoAndamento.Sentenca,
            var s when s.Contains("acórd") || s.Contains("acord") => TipoAndamento.Acordao,
            var s when s.Contains("audiên") || s.Contains("audien") => TipoAndamento.Audiencia,
            var s when s.Contains("intim")       => TipoAndamento.Intimacao,
            var s when s.Contains("public")        => TipoAndamento.Publicacao,
            var s when s.Contains("petic")        => TipoAndamento.Peticao,
            _ => TipoAndamento.Outro
        };

        Assert.Equal(expected, tipo);
    }
}
