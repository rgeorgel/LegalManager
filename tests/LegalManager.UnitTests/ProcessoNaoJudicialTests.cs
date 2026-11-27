using LegalManager.Application.DTOs.Processos;
using LegalManager.Application.Interfaces;
using LegalManager.Domain.Entities;
using LegalManager.Domain.Enums;
using LegalManager.Domain.Interfaces;
using LegalManager.Infrastructure.Persistence;
using LegalManager.Infrastructure.Services;
using LegalManager.UnitTests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace LegalManager.UnitTests;

// Processos sem número CNJ (administrativo: INSS, PROCON...; extrajudicial: cartório) e o
// "Segredo de justiça" marcado à mão no cadastro.
public class ProcessoNaoJudicialTests
{
    private static async Task<(AppDbContext ctx, ProcessoService service, Guid tenantId)> CriarAsync()
    {
        var ctx = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var tenant = new Tenant
        {
            Id = Guid.NewGuid(), Nome = "Escritório", Plano = PlanoTipo.Free,
            Status = StatusTenant.Trial, CriadoEm = DateTime.UtcNow
        };
        ctx.Tenants.Add(tenant);
        await ctx.SaveChangesAsync();

        var tenantContext = new Mock<ITenantContext>();
        tenantContext.Setup(t => t.TenantId).Returns(tenant.Id);
        tenantContext.Setup(t => t.UserId).Returns(Guid.NewGuid());
        var service = new ProcessoService(ctx, tenantContext.Object, new Mock<IEscavadorService>().Object,
            FakeConsultaExternaLogService.Instance);
        return (ctx, service, tenant.Id);
    }

    private static CreateProcessoDto Inss(string numero = " 123456789 ", bool monitorado = false) => new(
        numero, "TJSP", null, null, AreaDireito.Previdenciario, "Aposentadoria por idade",
        FaseProcessual.Outro, StatusProcesso.Ativo, null, null, Monitorado: monitorado,
        DataAjuizamento: new DateTime(2026, 3, 10),
        Tipo: TipoProcesso.Administrativo, Orgao: " INSS ");

    private static UpdateProcessoDto Atualizacao(string numero, TipoProcesso? tipo = null, string? orgao = null,
        DateTime? data = null, bool? segredo = null, bool? monitorado = null) => new(
        numero, null, null, null, AreaDireito.Previdenciario, null, FaseProcessual.Outro, StatusProcesso.Ativo,
        null, null, monitorado, null, null, null, null,
        Tipo: tipo, Orgao: orgao, DataAjuizamento: data, SegredoJustica: segredo);

    [Fact]
    public async Task Create_Administrativo_GravaOrgaoProtocoloEData()
    {
        var (_, service, _) = await CriarAsync();

        var r = await service.CreateAsync(Inss());

        Assert.Equal(TipoProcesso.Administrativo, r.Tipo);
        Assert.Equal("INSS", r.Orgao);
        Assert.Equal("123456789", r.NumeroCNJ);
        Assert.Equal(new DateTime(2026, 3, 10), r.DataAjuizamento);
        Assert.False(r.Monitorado);
    }

    [Fact]
    public async Task Create_Judicial_IgnoraOrgao()
    {
        var (_, service, _) = await CriarAsync();

        var r = await service.CreateAsync(Inss("0000001-00.2024.8.26.0001") with { Tipo = TipoProcesso.Judicial });

        Assert.Equal(TipoProcesso.Judicial, r.Tipo);
        Assert.Null(r.Orgao);
    }

    [Fact]
    public async Task Create_AdministrativoMonitorado_Recusa()
    {
        var (ctx, service, _) = await CriarAsync();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateAsync(Inss(monitorado: true)));

        Assert.Contains("judiciais", ex.Message);
        Assert.Empty(ctx.Processos);
    }

    [Fact]
    public async Task Create_NumeroEmBranco_PedeProtocolo()
    {
        var (_, service, _) = await CriarAsync();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateAsync(Inss("   ")));

        Assert.Contains("protocolo", ex.Message);
    }

    [Fact]
    public async Task Create_ProtocoloDuplicado_Recusa()
    {
        var (_, service, _) = await CriarAsync();
        await service.CreateAsync(Inss());

        await Assert.ThrowsAsync<ProcessoDuplicadoException>(() => service.CreateAsync(Inss("123456789")));
    }

    [Fact]
    public async Task Create_SegredoDeJustica_GravaNivelSigilo()
    {
        var (_, service, _) = await CriarAsync();
        var dto = Inss("0000001-00.2024.8.26.0001") with { Tipo = TipoProcesso.Judicial, SegredoJustica = true };

        var r = await service.CreateAsync(dto);

        Assert.Equal(1, r.NivelSigilo);
    }

    [Fact]
    public async Task Update_SemTipo_MantemTipoOrgaoEData()
    {
        var (_, service, _) = await CriarAsync();
        var criado = await service.CreateAsync(Inss());

        var r = await service.UpdateAsync(criado.Id, Atualizacao("987654321"));

        Assert.Equal(TipoProcesso.Administrativo, r.Tipo);
        Assert.Equal("INSS", r.Orgao);
        Assert.Equal(new DateTime(2026, 3, 10), r.DataAjuizamento);
        Assert.Equal("987654321", r.NumeroCNJ);
    }

    [Fact]
    public async Task Update_JudicialMonitoradoViraAdministrativo_DesligaMonitoramento()
    {
        var (ctx, service, tenantId) = await CriarAsync();
        var processo = new Processo
        {
            Id = Guid.NewGuid(), TenantId = tenantId, NumeroCNJ = "0000001-00.2024.8.26.0001",
            AreaDireito = AreaDireito.Previdenciario, Fase = FaseProcessual.Conhecimento,
            Status = StatusProcesso.Ativo, Monitorado = true, CriadoEm = DateTime.UtcNow
        };
        ctx.Processos.Add(processo);
        await ctx.SaveChangesAsync();

        var r = await service.UpdateAsync(processo.Id,
            Atualizacao("123", TipoProcesso.Administrativo, "PROCON", new DateTime(2026, 1, 5)));

        Assert.False(r.Monitorado);
        Assert.Equal(TipoProcesso.Administrativo, r.Tipo);
        Assert.Equal("PROCON", r.Orgao);
        Assert.Equal(new DateTime(2026, 1, 5), r.DataAjuizamento);
    }

    [Fact]
    public async Task Update_LigarMonitoramentoDeAdministrativo_Recusa()
    {
        var (_, service, _) = await CriarAsync();
        var criado = await service.CreateAsync(Inss());

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.UpdateAsync(criado.Id, Atualizacao("123456789", monitorado: true)));
    }

    [Fact]
    public async Task Update_VoltaParaJudicial_LimpaOrgao()
    {
        var (_, service, _) = await CriarAsync();
        var criado = await service.CreateAsync(Inss());

        var r = await service.UpdateAsync(criado.Id,
            Atualizacao("0000001-00.2024.8.26.0001", TipoProcesso.Judicial, "INSS"));

        Assert.Equal(TipoProcesso.Judicial, r.Tipo);
        Assert.Null(r.Orgao);
    }

    [Fact]
    public async Task Update_SegredoDeJustica_LigaDesligaEMantem()
    {
        var (_, service, _) = await CriarAsync();
        var criado = await service.CreateAsync(Inss("0000001-00.2024.8.26.0001") with { Tipo = TipoProcesso.Judicial });

        Assert.Equal(1, (await service.UpdateAsync(criado.Id, Atualizacao(criado.NumeroCNJ, segredo: true))).NivelSigilo);
        Assert.Equal(1, (await service.UpdateAsync(criado.Id, Atualizacao(criado.NumeroCNJ))).NivelSigilo);
        Assert.Equal(0, (await service.UpdateAsync(criado.Id, Atualizacao(criado.NumeroCNJ, segredo: false))).NivelSigilo);
    }

    [Fact]
    public async Task GetAll_BuscaPeloOrgao_ETrazTipo()
    {
        var (_, service, _) = await CriarAsync();
        await service.CreateAsync(Inss());
        await service.CreateAsync(Inss("0000001-00.2024.8.26.0001") with { Tipo = TipoProcesso.Judicial });

        var r = await service.GetAllAsync(new ProcessoFiltroDto("inss", null, null, null, null));

        var item = Assert.Single(r.Items);
        Assert.Equal(TipoProcesso.Administrativo, item.Tipo);
        Assert.Equal("INSS", item.Orgao);
    }

    [Fact]
    public async Task SituacaoNaFonte_IgnoraAdministrativo()
    {
        var (ctx, service, tenantId) = await CriarAsync();
        var adm = await service.CreateAsync(Inss());
        var jud = await service.CreateAsync(Inss("0000001-00.2024.8.26.0001") with { Tipo = TipoProcesso.Judicial });
        // Mesmo andamento de arquivamento (código CNJ 246) nos dois: só o judicial vira sugestão.
        foreach (var id in new[] { adm.Id, jud.Id })
            ctx.Andamentos.Add(new Andamento
            {
                Id = Guid.NewGuid(), ProcessoId = id, TenantId = tenantId, CodigoCNJ = 246,
                Data = DateTime.UtcNow, Descricao = "Arquivado Definitivamente", Tipo = TipoAndamento.Outro,
                Fonte = FonteAndamento.DataJud, CriadoEm = DateTime.UtcNow
            });
        await ctx.SaveChangesAsync();

        var sugestoes = await service.SugerirSituacaoNaFonteAsync();

        Assert.Equal(jud.Id, Assert.Single(sugestoes).ProcessoId);
    }
}
