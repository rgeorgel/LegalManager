using System.Text;
using LegalManager.API.Controllers;
using LegalManager.API.Reports;
using LegalManager.Application.DTOs.Aluguel;
using LegalManager.Application.DTOs.Pensao;
using LegalManager.Domain.Entities;
using LegalManager.Domain.Enums;
using LegalManager.Domain.Interfaces;
using LegalManager.Infrastructure.Persistence;
using LegalManager.Infrastructure.Services.Trabalhista;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace LegalManager.UnitTests;

public class CalculadoraAluguelTests
{
    private static readonly ContratoAluguelDto Contrato = new(new DateOnly(2024, 3, 1), 1000m);

    private static SeriesMensais Igpm(decimal primeiroAno, decimal? segundoAno = null)
    {
        var d = new Dictionary<(int, int), decimal>();
        for (var i = 0; i < 12; i++) d[(i < 10 ? 2024 : 2025, (i + 2) % 12 + 1)] = primeiroAno;   // 03/2024 a 02/2025
        if (segundoAno is { } s) for (var i = 0; i < 12; i++) d[(i < 10 ? 2025 : 2026, (i + 2) % 12 + 1)] = s; // 03/2025 a 02/2026
        return SeriesTeste.Criar(new() { [TipoIndice.IGPM] = d });
    }

    [Fact]
    public void Reajuste_AnualPeloAcumuladoDoPeriodoAnterior_ENaoReduzComDeflacao()
    {
        var r = CalculadoraAluguel.Reajustes(Contrato, new DateOnly(2026, 3, 15), Igpm(0.01m, -0.005m));

        Assert.Equal(2, r.Reajustes.Count);
        var primeiro = r.Reajustes[0];
        Assert.Equal(new DateOnly(2025, 3, 1), primeiro.Data);
        Assert.Equal("03/2024 a 02/2025", primeiro.Periodo);
        Assert.Equal(12.6825m, primeiro.IndiceAcumulado);
        Assert.Equal(1126.83m, primeiro.ValorNovo);
        Assert.Equal(1126.83m, r.Reajustes[1].ValorNovo); // deflação: mantém
        Assert.True(r.Reajustes[1].IndiceAcumulado < 0);
        Assert.Equal(1126.83m, r.ValorAtual);
        Assert.Equal(new DateOnly(2027, 3, 1), r.ProximoReajuste);
        Assert.Contains(r.Avisos, a => a.Contains("negativo"));
        Assert.Equal("IGP-M (FGV)", r.Indice);

        var reduz = CalculadoraAluguel.Reajustes(Contrato with { NaoReduzir = false }, new DateOnly(2026, 3, 15), Igpm(0.01m, -0.005m));
        Assert.True(reduz.ValorAtual < 1126.83m);
    }

    [Fact]
    public void Reajuste_SemestralSemIndicePublicado_AvisaNulidadeEEstimativa()
    {
        var r = CalculadoraAluguel.Reajustes(Contrato with { PeriodicidadeMeses = 6 }, new DateOnly(2024, 9, 1), SeriesMensais.Vazia);
        var l = Assert.Single(r.Reajustes);
        Assert.True(l.Estimado);
        Assert.Equal(1000m, l.ValorNovo);
        Assert.Contains(r.Avisos, a => a.Contains("10.192"));
        Assert.Contains(r.Avisos, a => a.Contains("03/2024"));
    }

    [Fact]
    public void Reajuste_SemIndice_MantemValorInicial()
    {
        var r = CalculadoraAluguel.Reajustes(Contrato with { Indice = IndiceAluguel.Nenhum }, new DateOnly(2026, 1, 1), SeriesMensais.Vazia);
        Assert.Empty(r.Reajustes);
        Assert.Equal(1000m, r.ValorAtual);
        Assert.Equal(new DateOnly(2026, 3, 1), r.ProximoReajuste);
        Assert.Contains(r.Avisos, a => a.Contains("sem índice", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void GerarParcelas_AluguelVigenteEmCadaVencimento()
    {
        var dto = new GerarParcelasAluguelDto(Contrato, new DateOnly(2025, 1, 1), new DateOnly(2025, 4, 1), DiaVencimento: 31, Encargos: 300m);
        var p = CalculadoraAluguel.GerarParcelas(dto, Igpm(0.01m));

        Assert.Equal([new DateOnly(2025, 1, 31), new DateOnly(2025, 2, 28), new DateOnly(2025, 3, 31), new DateOnly(2025, 4, 30)], p.Select(x => x.Vencimento));
        Assert.Equal([1000m, 1000m, 1126.83m, 1126.83m], p.Select(x => x.Aluguel));
        Assert.All(p, x => Assert.Equal(300m, x.Encargos));
        Assert.Equal("01/2025", p[0].Descricao);
    }

    [Fact]
    public void Validacoes()
    {
        Assert.Null(CalculadoraAluguel.ValidarContrato(Contrato));
        Assert.Contains("periodicidade", CalculadoraAluguel.ValidarContrato(Contrato with { PeriodicidadeMeses = 5 }));
        var parcelas = new GerarParcelasAluguelDto(Contrato, new DateOnly(2025, 1, 1), new DateOnly(2025, 4, 1));
        Assert.Null(CalculadoraAluguel.ValidarParcelas(parcelas));
        Assert.Contains("anterior", CalculadoraAluguel.ValidarParcelas(parcelas with { UltimoMes = new DateOnly(2024, 1, 1) }));
        Assert.Contains("600", CalculadoraAluguel.ValidarParcelas(parcelas with { UltimoMes = new DateOnly(2080, 1, 1) }));
        Assert.Contains("periodicidade", CalculadoraAluguel.ValidarParcelas(parcelas with { Contrato = Contrato with { PeriodicidadeMeses = 7 } }));

        var debito = Debito();
        Assert.Null(CalculadoraAluguel.ValidarDebito(debito));
        Assert.Contains("ao menos", CalculadoraAluguel.ValidarDebito(debito with { Parcelas = [] }));
        Assert.Contains("depois da data", CalculadoraAluguel.ValidarDebito(debito with { DataCalculo = new DateOnly(2025, 1, 1) }));
        Assert.Contains("taxa mensal", CalculadoraAluguel.ValidarDebito(debito with { Juros = JurosPensao.Personalizado }));
    }

    private static CalcularDebitoAluguelDto Debito() => new(new DateOnly(2025, 4, 15),
        [new(new DateOnly(2025, 1, 10), 1000m, 200m), new(new DateOnly(2025, 2, 10), 1000m, 200m, 1500m)],
        HonorariosPercentual: 10, Locador: "Locador", Imovel: "Rua A, 1");

    [Fact]
    public void Debito_CorrecaoJurosMultaEHonorarios()
    {
        var series = SeriesTeste.Criar(new() { [TipoIndice.IGPM] = new() { [(2025, 1)] = 0.01m, [(2025, 2)] = 0.01m, [(2025, 3)] = 0.01m } });
        var r = CalculadoraAluguel.CalcularDebito(Debito(), series);

        var l = r.Linhas[0];
        Assert.Equal(1200m, l.Saldo);
        Assert.Equal(1.030301m, l.FatorCorrecao);
        Assert.Equal(1236.36m, l.Corrigido);
        Assert.Equal(3m, l.JurosPercentual);
        Assert.Equal(37.09m, l.Juros);
        Assert.Equal(123.64m, l.Multa);
        Assert.Equal(1397.09m, l.Total);
        Assert.Equal(0m, r.Linhas[1].Total);
        Assert.Equal(1, r.Parcelas);
        Assert.Equal(36.36m, r.Correcao);
        Assert.Equal(139.71m, r.Honorarios);
        Assert.Equal(1536.80m, r.Total);
        Assert.Contains(r.Avisos, a => a.Contains("02/2025"));
    }

    [Fact]
    public void Debito_TaxaLegal_JurosPersonalizados_SemIndice_EAvisos()
    {
        var series = SeriesTeste.Criar(new()
        {
            [TipoIndice.SELIC] = new() { [(2024, 9)] = 0.0084m },
            [TipoIndice.IPCA] = new() { [(2024, 9)] = 0.0044m },
        });
        var dto = new CalcularDebitoAluguelDto(new DateOnly(2024, 11, 1), [new(new DateOnly(2024, 8, 10), 1000m)],
            IndiceAluguel.Nenhum, JurosPensao.TaxaLegal, MultaMoratoria: 20);
        var r = CalculadoraAluguel.CalcularDebito(dto, series);
        Assert.Equal(1.4m, r.Linhas[0].JurosPercentual); // ago 1% + set (0,84 − 0,44) + out sem dado
        Assert.Contains(r.Avisos, a => a.Contains("10/2024"));
        Assert.Contains(r.Avisos, a => a.Contains("art. 413"));
        Assert.Equal("Sem índice", r.Indice);

        var pers = CalculadoraAluguel.CalcularDebito(dto with { Juros = JurosPensao.Personalizado, JurosPercentualMensal = 0.5m, IndiceCorrecao = IndiceAluguel.IPCA }, series);
        Assert.Equal(1.5m, pers.Linhas[0].JurosPercentual);
        Assert.Contains(pers.Avisos, a => a.StartsWith("IPCA (IBGE)"));
        Assert.Equal("Sem juros de mora", CalculadoraAluguel.DescricaoJuros(dto with { Juros = JurosPensao.Nenhum }));
        Assert.Contains("Lei 14.905", CalculadoraAluguel.DescricaoJuros(dto));
    }

    [Theory]
    [InlineData(IndiceAluguel.IGPM, TipoIndice.IGPM)]
    [InlineData(IndiceAluguel.IPCA, TipoIndice.IPCA)]
    [InlineData(IndiceAluguel.INPC, TipoIndice.INPC)]
    [InlineData(IndiceAluguel.IGPDI, TipoIndice.IGPDI)]
    [InlineData(IndiceAluguel.IPCAE, TipoIndice.TJSP)]
    public void Indices(IndiceAluguel indice, TipoIndice tipo)
    {
        Assert.Equal(tipo, CalculadoraAluguel.TipoDoIndice(indice));
        Assert.NotEqual("Sem índice", CalculadoraAluguel.NomeIndice(indice));
    }

    [Fact]
    public void Pdfs_Renderizam()
    {
        var reajuste = new CalcularReajusteAluguelDto(Contrato with { PeriodicidadeMeses = 3 }, new DateOnly(2025, 3, 1));
        var r1 = CalculadoraAluguel.Reajustes(reajuste.Contrato, reajuste.Ate, Igpm(0.01m));
        var r2 = CalculadoraAluguel.CalcularDebito(Debito(), SeriesMensais.Vazia);
        foreach (var d in new[] { DemonstrativosAluguel.Reajuste(reajuste, r1), DemonstrativosAluguel.Debito(Debito(), r2) })
            Assert.StartsWith("%PDF", Encoding.ASCII.GetString(DemonstrativoPdfRenderer.Renderizar(d, "Escritório", null, new DateTime(2025, 4, 1))[..4]));
    }
}

public class AluguelControllerTests
{
    private static AluguelController Controller(PlanoTipo plano, out AppDbContext db)
    {
        db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        db.IndicesCorrecaoMonetaria.Add(new IndiceCorrecaoMonetaria { Id = Guid.NewGuid(), Tipo = TipoIndice.IGPM, Ano = 2024, Mes = 3, Valor = 0.01m, Fonte = "BCB" });
        db.SaveChanges();
        var t = new Mock<ITenantContext>();
        t.Setup(x => x.TenantId).Returns(Guid.NewGuid());
        t.Setup(x => x.Plano).Returns(plano);
        return new AluguelController(db, t.Object);
    }

    private static IWebHostEnvironment Env() => new Mock<IWebHostEnvironment>().Object;
    private static readonly ContratoAluguelDto Contrato = new(new DateOnly(2024, 3, 1), 1000m);
    private static readonly CalcularReajusteAluguelDto Reajuste = new(Contrato, new DateOnly(2025, 3, 1));
    private static readonly GerarParcelasAluguelDto Parcelas = new(Contrato, new DateOnly(2025, 1, 1), new DateOnly(2025, 3, 1));
    private static readonly CalcularDebitoAluguelDto Debito = new(new DateOnly(2025, 4, 1), [new(new DateOnly(2025, 1, 10), 1000m)], Juros: JurosPensao.TaxaLegal);

    [Fact]
    public async Task PlanoFree_402()
    {
        var c = Controller(PlanoTipo.Free, out _);
        Assert.Equal(402, Assert.IsType<ObjectResult>(await c.GetIndices(default)).StatusCode);
        Assert.Equal(402, Assert.IsType<ObjectResult>(await c.Reajuste(Reajuste, default)).StatusCode);
        Assert.Equal(402, Assert.IsType<ObjectResult>(await c.ReajustePdf(Reajuste, Env(), default)).StatusCode);
        Assert.Equal(402, Assert.IsType<ObjectResult>(await c.Parcelas(Parcelas, default)).StatusCode);
        Assert.Equal(402, Assert.IsType<ObjectResult>(await c.Debito(Debito, default)).StatusCode);
        Assert.Equal(402, Assert.IsType<ObjectResult>(await c.DebitoPdf(Debito, Env(), default)).StatusCode);
    }

    [Fact]
    public async Task PlanoPago_TodasAsRotas()
    {
        var c = Controller(PlanoTipo.Plus, out _);

        var indices = Assert.IsType<List<IndiceDisponivelDto>>(Assert.IsType<OkObjectResult>(await c.GetIndices(default)).Value);
        Assert.Equal((2024, 3), (indices[0].Ano, indices[0].Mes));
        Assert.Equal("IGP-M", indices[0].Nome);
        Assert.Null(indices.Single(i => i.Indice == "IGPDI").Ano);

        var r = Assert.IsType<ResultadoReajusteAluguelDto>(Assert.IsType<OkObjectResult>(await c.Reajuste(Reajuste, default)).Value);
        Assert.True(r.Reajustes[0].Estimado);
        var semIndice = Reajuste with { Contrato = Contrato with { Indice = IndiceAluguel.Nenhum } };
        Assert.IsType<OkObjectResult>(await c.Reajuste(semIndice, default));
        var invalido = Reajuste with { Contrato = Contrato with { PeriodicidadeMeses = 5 } };
        Assert.IsType<BadRequestObjectResult>(await c.Reajuste(invalido, default));
        Assert.IsType<BadRequestObjectResult>(await c.ReajustePdf(invalido, Env(), default));
        Assert.Equal("reajuste-aluguel-20250301.pdf", Assert.IsType<FileContentResult>(await c.ReajustePdf(Reajuste, Env(), default)).FileDownloadName);

        Assert.Equal(3, Assert.IsType<List<ParcelaAluguelDto>>(Assert.IsType<OkObjectResult>(await c.Parcelas(Parcelas, default)).Value).Count);
        Assert.IsType<BadRequestObjectResult>(await c.Parcelas(Parcelas with { UltimoMes = new DateOnly(2020, 1, 1) }, default));

        Assert.IsType<ResultadoDebitoAluguelDto>(Assert.IsType<OkObjectResult>(await c.Debito(Debito, default)).Value);
        Assert.IsType<BadRequestObjectResult>(await c.Debito(Debito with { Parcelas = [] }, default));
        Assert.IsType<BadRequestObjectResult>(await c.DebitoPdf(Debito with { Parcelas = [] }, Env(), default));
        Assert.Equal("debito-aluguel-20250401.pdf", Assert.IsType<FileContentResult>(await c.DebitoPdf(Debito, Env(), default)).FileDownloadName);
    }
}
