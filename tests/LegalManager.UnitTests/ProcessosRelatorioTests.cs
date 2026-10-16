using System.Text;
using LegalManager.API.Controllers;
using LegalManager.API.Reports;
using LegalManager.Application.DTOs.Processos;
using LegalManager.Application.Interfaces;
using LegalManager.Domain.Entities;
using LegalManager.Domain.Enums;
using LegalManager.Domain.Interfaces;
using LegalManager.Infrastructure.Persistence;
using LegalManager.Infrastructure.Services;
using LegalManager.UnitTests.TestHelpers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace LegalManager.UnitTests;

public class ProcessosRelatorioTests
{
    private static AppDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static ITenantContext TenantContext(Guid tenantId, Guid userId, PlanoTipo plano = PlanoTipo.Plus)
    {
        var mock = new Mock<ITenantContext>();
        mock.Setup(t => t.TenantId).Returns(tenantId);
        mock.Setup(t => t.UserId).Returns(userId);
        mock.Setup(t => t.Plano).Returns(plano);
        return mock.Object;
    }

    private static Processo NovoProcesso(Guid tenantId, string cnj, StatusProcesso status, AreaDireito area = AreaDireito.Civil) => new()
    {
        Id = Guid.NewGuid(), TenantId = tenantId, NumeroCNJ = cnj, AreaDireito = area,
        Fase = FaseProcessual.Conhecimento, Status = status, CriadoEm = DateTime.UtcNow
    };

    private static Contato NovoContato(Guid tenantId, string nome) => new()
    {
        Id = Guid.NewGuid(), TenantId = tenantId, Nome = nome, Tipo = TipoPessoa.PF,
        TipoContato = TipoContato.Cliente, Ativo = true, CriadoEm = DateTime.UtcNow
    };

    [Fact]
    public async Task GetRelatorioAsync_FiltraPorStatus_IsolaTenant_EMontaPartesEUltimoAndamento()
    {
        var ctx = CreateContext();
        var tenant = new Tenant { Id = Guid.NewGuid(), Nome = "Silva Advogados", Plano = PlanoTipo.Plus, Status = StatusTenant.Ativo, CriadoEm = DateTime.UtcNow };
        var outro = new Tenant { Id = Guid.NewGuid(), Nome = "Outro", Plano = PlanoTipo.Plus, Status = StatusTenant.Ativo, CriadoEm = DateTime.UtcNow };
        ctx.Tenants.AddRange(tenant, outro);

        var ativo = NovoProcesso(tenant.Id, "0000001-00.2024.8.26.0001", StatusProcesso.Ativo);
        var arquivado = NovoProcesso(tenant.Id, "0000002-00.2024.8.26.0001", StatusProcesso.Arquivado);
        var deOutroTenant = NovoProcesso(outro.Id, "9999999-99.2024.8.26.0001", StatusProcesso.Ativo);
        ctx.Processos.AddRange(ativo, arquivado, deOutroTenant);

        var autor1 = NovoContato(tenant.Id, "Maria");
        var autor2 = NovoContato(tenant.Id, "João");
        var reu = NovoContato(tenant.Id, "Banco X");
        ctx.Contatos.AddRange(autor1, autor2, reu);
        ctx.ProcessoPartes.AddRange(
            new ProcessoParte { Id = Guid.NewGuid(), ProcessoId = ativo.Id, ContatoId = autor1.Id, TipoParte = TipoParteProcesso.Autor },
            new ProcessoParte { Id = Guid.NewGuid(), ProcessoId = ativo.Id, ContatoId = autor2.Id, TipoParte = TipoParteProcesso.Autor },
            new ProcessoParte { Id = Guid.NewGuid(), ProcessoId = ativo.Id, ContatoId = reu.Id, TipoParte = TipoParteProcesso.Reu });

        ctx.Andamentos.AddRange(
            new Andamento { Id = Guid.NewGuid(), ProcessoId = ativo.Id, TenantId = tenant.Id, Data = new DateTime(2026, 3, 1), Tipo = TipoAndamento.Despacho, Descricao = "Antigo", CriadoEm = DateTime.UtcNow },
            new Andamento { Id = Guid.NewGuid(), ProcessoId = ativo.Id, TenantId = tenant.Id, Data = new DateTime(2026, 9, 10), Tipo = TipoAndamento.Sentenca, Descricao = "Sentença publicada", CriadoEm = DateTime.UtcNow });
        await ctx.SaveChangesAsync();

        var service = new ProcessoService(ctx, TenantContext(tenant.Id, Guid.NewGuid()), new Mock<IEscavadorService>().Object, FakeConsultaExternaLogService.Instance);
        var rel = await service.GetRelatorioAsync(new ProcessoFiltroDto(null, [StatusProcesso.Ativo], null, null, null));

        var item = Assert.Single(rel.Itens);
        Assert.Equal(ativo.NumeroCNJ, item.NumeroCNJ);
        Assert.Contains("Maria", item.Autores);
        Assert.Contains("João", item.Autores);
        Assert.Equal("Banco X", item.Reus);
        Assert.Equal(new DateTime(2026, 9, 10), item.UltimoAndamentoData);
        Assert.Equal("Sentença publicada", item.UltimoAndamentoDescricao);
        Assert.Equal("Silva Advogados", rel.NomeEscritorio);
        Assert.Equal(["Status: Ativo"], rel.Filtros);
    }

    [Fact]
    public async Task GetRelatorioAsync_ComVariosStatusEAreas_TrazQualquerUmDeles()
    {
        var ctx = CreateContext();
        var tenantId = Guid.NewGuid();
        ctx.Tenants.Add(new Tenant { Id = tenantId, Nome = "T", Plano = PlanoTipo.Plus, Status = StatusTenant.Ativo, CriadoEm = DateTime.UtcNow });
        ctx.Processos.AddRange(
            NovoProcesso(tenantId, "1", StatusProcesso.Arquivado, AreaDireito.Civil),
            NovoProcesso(tenantId, "2", StatusProcesso.Encerrado, AreaDireito.Trabalhista),
            NovoProcesso(tenantId, "3", StatusProcesso.Ativo, AreaDireito.Civil),
            NovoProcesso(tenantId, "4", StatusProcesso.Encerrado, AreaDireito.Criminal));
        await ctx.SaveChangesAsync();

        var service = new ProcessoService(ctx, TenantContext(tenantId, Guid.NewGuid()), new Mock<IEscavadorService>().Object, FakeConsultaExternaLogService.Instance);
        var rel = await service.GetRelatorioAsync(new ProcessoFiltroDto(null,
            [StatusProcesso.Arquivado, StatusProcesso.Encerrado], [AreaDireito.Civil, AreaDireito.Trabalhista], null, null));

        Assert.Equal(["1", "2"], rel.Itens.Select(i => i.NumeroCNJ).Order());
        Assert.Equal(["Status: Arquivado, Encerrado", "Área: Civil, Trabalhista"], rel.Filtros);
    }

    [Fact]
    public async Task GetRelatorioAsync_IgnoraPaginacao()
    {
        var ctx = CreateContext();
        var tenantId = Guid.NewGuid();
        ctx.Tenants.Add(new Tenant { Id = tenantId, Nome = "T", Plano = PlanoTipo.Plus, Status = StatusTenant.Ativo, CriadoEm = DateTime.UtcNow });
        for (var i = 0; i < 25; i++)
            ctx.Processos.Add(NovoProcesso(tenantId, $"{i:0000000}-00.2024.8.26.0001", StatusProcesso.Ativo));
        await ctx.SaveChangesAsync();

        var service = new ProcessoService(ctx, TenantContext(tenantId, Guid.NewGuid()), new Mock<IEscavadorService>().Object, FakeConsultaExternaLogService.Instance);
        var rel = await service.GetRelatorioAsync(new ProcessoFiltroDto(null, null, null, null, null, Page: 1, PageSize: 10));

        Assert.Equal(25, rel.Itens.Count);
        Assert.Empty(rel.Filtros);
    }

    private static ProcessoRelatorioDto Relatorio(params ProcessoRelatorioItemDto[] itens) =>
        new("Escritório", null, new DateTime(2026, 10, 16, 14, 30, 0), ["Status: Ativo"], itens);

    private static ProcessoRelatorioItemDto Item(string? descricao = "Despacho", decimal? valor = 1234.5m, string? autores = "Maria") =>
        new("0000001-00.2024.8.26.0001", autores, "Banco X", "TJSP", "1ª Vara", "São Paulo",
            AreaDireito.Previdenciario, "Cobrança", FaseProcessual.Execucao, StatusProcesso.Ativo,
            "Dra. Ana", valor, new DateTime(2024, 5, 2), new DateTime(2026, 9, 10), descricao);

    [Fact]
    public void Csv_TemBom_SeparadorPontoEVirgula_EFormatoBrasileiro()
    {
        var bytes = ProcessosRelatorioCsv.Gerar(Relatorio(Item()));

        Assert.Equal(Encoding.UTF8.GetPreamble(), bytes[..3]);
        var linhas = Encoding.UTF8.GetString(bytes[3..]).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(2, linhas.Length);
        Assert.StartsWith("Número do processo;Autor(es);Réu(s)", linhas[0]);
        Assert.Equal(
            "0000001-00.2024.8.26.0001;Maria;Banco X;TJSP;1ª Vara;São Paulo;Previdenciário;Cobrança;Execução;Ativo;Dra. Ana;1.234,50;02/05/2024;10/09/2026;Despacho",
            linhas[1]);
    }

    [Fact]
    public void Csv_EscapaAspasPontoEVirgulaEQuebraDeLinha_ENeutralizaFormulas()
    {
        var bytes = ProcessosRelatorioCsv.Gerar(Relatorio(Item(descricao: "Juntada; \"petição\"\nnova", autores: "=HYPERLINK(\"x\")")));
        var texto = Encoding.UTF8.GetString(bytes[3..]);

        Assert.Contains("\"Juntada; \"\"petição\"\"\nnova\"", texto);
        Assert.Contains("\"'=HYPERLINK(\"\"x\"\")\"", texto);
    }

    [Fact]
    public void Pdf_GeraDocumentoComESemProcessos()
    {
        Assert.StartsWith("%PDF", Encoding.ASCII.GetString(ProcessosRelatorioPdfRenderer.Renderizar(Relatorio(Item(), Item(descricao: null, valor: null)))[..4]));
        Assert.StartsWith("%PDF", Encoding.ASCII.GetString(ProcessosRelatorioPdfRenderer.Renderizar(Relatorio())[..4]));
    }

    private static ProcessosController Controller(Mock<IProcessoService> service, PlanoTipo plano) =>
        new(service.Object, Mock.Of<IMonitoramentoService>(), Mock.Of<IAuditService>(),
            TenantContext(Guid.NewGuid(), Guid.NewGuid(), plano), null!, null!, Mock.Of<IContatoResolverService>());

    [Fact]
    public async Task Relatorio_PlanoFree_Retorna402()
    {
        var service = new Mock<IProcessoService>();
        var result = await Controller(service, PlanoTipo.Free)
            .Relatorio("csv", null, ["Ativo"], null, null, null, Mock.Of<IWebHostEnvironment>());

        Assert.Equal(402, Assert.IsType<ObjectResult>(result).StatusCode);
        service.Verify(s => s.GetRelatorioAsync(It.IsAny<ProcessoFiltroDto>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Relatorio_FormatoInvalido_Retorna400()
    {
        var result = await Controller(new Mock<IProcessoService>(), PlanoTipo.Plus)
            .Relatorio("xlsx", null, null, null, null, null, Mock.Of<IWebHostEnvironment>());

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Theory]
    [InlineData("csv", "text/csv; charset=utf-8", ".csv")]
    [InlineData("PDF", "application/pdf", ".pdf")]
    public async Task Relatorio_Plus_RetornaArquivoComFiltrosDaTela(string formato, string contentType, string extensao)
    {
        var service = new Mock<IProcessoService>();
        ProcessoFiltroDto? recebido = null;
        service.Setup(s => s.GetRelatorioAsync(It.IsAny<ProcessoFiltroDto>(), It.IsAny<CancellationToken>()))
            .Callback<ProcessoFiltroDto, CancellationToken>((f, _) => recebido = f)
            .ReturnsAsync(Relatorio(Item()));

        var result = await Controller(service, PlanoTipo.Plus)
            .Relatorio(formato, "cobrança", ["ativo"], ["Trabalhista"], null, null, Mock.Of<IWebHostEnvironment>(), favoritos: true);

        var file = Assert.IsType<FileContentResult>(result);
        Assert.Equal(contentType, file.ContentType);
        Assert.Equal("processos-20261016-1430" + extensao, file.FileDownloadName);
        Assert.NotNull(recebido);
        Assert.Equal("cobrança", recebido.Busca);
        Assert.Equal([StatusProcesso.Ativo], recebido.Status);
        Assert.Equal([AreaDireito.Trabalhista], recebido.AreaDireito);
        Assert.True(recebido.SomenteFavoritos);
    }
}
