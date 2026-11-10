using System.Text;
using LegalManager.API.Controllers;
using LegalManager.API.Reports;
using LegalManager.Application.DTOs.Aluguel;
using LegalManager.Application.DTOs.Bancario;
using LegalManager.Application.DTOs.Pensao;
using LegalManager.Domain.Entities;
using LegalManager.Domain.Enums;
using LegalManager.Domain.Interfaces;
using LegalManager.Infrastructure.Persistence;
using LegalManager.Infrastructure.Services.Bancario;
using LegalManager.Infrastructure.Services.Trabalhista;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace LegalManager.UnitTests;

public class RevisionalTests
{
    private static SeriesMensais Media(decimal taxa, int ano = 2024, int mes = 1, TipoIndice tipo = TipoIndice.TxPessoal) =>
        SeriesTeste.Criar(new() { [tipo] = new() { [(ano, mes)] = taxa } });

    private static CalcularRevisionalDto Dto(decimal taxa = 5) => new(
        ModalidadeCredito.PessoalNaoConsignado, new DateOnly(2024, 1, 15), 10000m, 12, new DateOnly(2024, 4, 20),
        TaxaContratadaMensal: taxa, ParcelasPagas: 3, Correcao: IndiceAluguel.Nenhum, Juros: JurosPensao.Nenhum);

    [Fact]
    public void Abusiva_RecalculaPelaTaxaMedia()
    {
        var r = CalculadorasBancarias.CalcularRevisional(Dto(), Media(0.02m));

        Assert.True(r.Abusiva);
        Assert.Equal(5m, r.TaxaContratada);
        Assert.Equal(2m, r.TaxaMedia);
        Assert.Equal(3m, r.LimiteAbusividade);
        Assert.Equal(2.5m, r.RazaoMedia);
        Assert.Equal(2m, r.TaxaRevisada);
        Assert.Equal("01/2024", r.CompetenciaTaxaMedia);
        Assert.Equal(1128.25m, r.Linhas[0].ParcelaOriginal);
        Assert.Equal(945.60m, r.Linhas[0].ParcelaRevisada);
        Assert.Equal(new DateOnly(2024, 2, 15), r.Linhas[0].Vencimento);
        Assert.Equal(547.95m, r.PagoAMais);
        Assert.Equal(547.95m, r.RestituicaoAtualizada);
        Assert.Equal(1643.85m, r.EconomiaAVencer);
        Assert.True(r.SaldoDevedorRevisado < r.SaldoDevedorOriginal);
        Assert.Equal(Math.Round((decimal)(Math.Pow(1.05, 12) - 1) * 100, 4), r.TaxaContratadaAnual);
    }

    [Fact]
    public void TaxaImplicitaDaParcela()
    {
        var r = CalculadorasBancarias.CalcularRevisional(Dto() with { TaxaContratadaMensal = null, ValorParcela = 1128.25m }, Media(0.02m));
        Assert.True(r.TaxaImplicita);
        Assert.InRange(r.TaxaContratada, 4.99m, 5.01m);
        Assert.Equal(1128.25m, r.Linhas[0].ParcelaOriginal);
        Assert.Equal(0m, CalculadorasBancarias.TaxaImplicita(1000m, 10, 100m));
    }

    [Fact]
    public void RepeticaoEmDobro_SoDepoisDaModulacao()
    {
        var dto = Dto() with { DataContratacao = new DateOnly(2021, 1, 10), RepeticaoEmDobro = true, DataCalculo = new DateOnly(2021, 6, 1) };
        var r = CalculadorasBancarias.CalcularRevisional(dto, Media(0.02m, 2021, 1));
        Assert.Equal([false, false, true], r.Linhas.Take(3).Select(l => l.EmDobro));
        Assert.Equal(730.60m, r.Restituicao);
        Assert.Contains(r.Avisos, a => a.Contains("30/03/2021"));
    }

    [Fact]
    public void NaoAbusiva_MantemTaxa_EExcluiTarifas()
    {
        var r = CalculadorasBancarias.CalcularRevisional(Dto(2.5m) with { TarifasExcluidas = 500m }, Media(0.02m));
        Assert.False(r.Abusiva);
        Assert.Equal(2.5m, r.TaxaRevisada);
        Assert.Equal(974.87m, r.Linhas[0].ParcelaOriginal);
        Assert.Equal(926.13m, r.Linhas[0].ParcelaRevisada);
        Assert.Contains(r.Avisos, a => a.Contains("REsp 1.061.530"));
        Assert.Contains(r.Avisos, a => a.Contains("Súmulas 565 e 566"));
    }

    [Fact]
    public void TaxaRevisadaInformada_Sac_EMediaDoMesAnterior()
    {
        var dto = Dto(1m) with { Sistema = SistemaAmortizacao.SAC, Prazo = 4, ValorFinanciado = 1000m, TaxaRevisadaMensal = 0.5m, DataContratacao = new DateOnly(2024, 2, 10) };
        var r = CalculadorasBancarias.CalcularRevisional(dto, Media(0.02m));
        Assert.Equal([260m, 257.5m, 255m, 252.5m], r.Linhas.Select(l => l.ParcelaOriginal));
        Assert.Equal(0.5m, r.TaxaRevisada);
        Assert.Equal("01/2024", r.CompetenciaTaxaMedia);
        Assert.Contains(r.Avisos, a => a.Contains("ainda não publicada"));
    }

    [Fact]
    public void SemTaxaMedia_Antes2011()
    {
        var r = CalculadorasBancarias.CalcularRevisional(Dto() with { DataContratacao = new DateOnly(2010, 5, 1) }, SeriesMensais.Vazia);
        Assert.Null(r.TaxaMedia);
        Assert.False(r.Abusiva);
        Assert.Contains(r.Avisos, a => a.Contains("março de 2011"));
        var depois = CalculadorasBancarias.CalcularRevisional(Dto(), SeriesMensais.Vazia);
        Assert.Contains(depois.Avisos, a => a.Contains("indisponível"));
    }

    [Fact]
    public void Validacoes()
    {
        Assert.Null(CalculadorasBancarias.ValidarRevisional(Dto()));
        Assert.Contains("taxa contratada ou", CalculadorasBancarias.ValidarRevisional(Dto() with { TaxaContratadaMensal = null }));
        Assert.Contains("SAC", CalculadorasBancarias.ValidarRevisional(Dto() with { TaxaContratadaMensal = null, ValorParcela = 100, Sistema = SistemaAmortizacao.SAC }));
        Assert.Contains("prazo", CalculadorasBancarias.ValidarRevisional(Dto() with { ParcelasPagas = 13 }));
        Assert.Contains("tarifas", CalculadorasBancarias.ValidarRevisional(Dto() with { TarifasExcluidas = 10000 }));
    }

    [Theory]
    [InlineData(ModalidadeCredito.PessoalNaoConsignado, TipoIndice.TxPessoal)]
    [InlineData(ModalidadeCredito.ConsignadoINSS, TipoIndice.TxINSS)]
    [InlineData(ModalidadeCredito.ConsignadoPublico, TipoIndice.TxPublico)]
    [InlineData(ModalidadeCredito.ConsignadoPrivado, TipoIndice.TxPrivado)]
    [InlineData(ModalidadeCredito.Veiculo, TipoIndice.TxVeiculo)]
    [InlineData(ModalidadeCredito.ChequeEspecial, TipoIndice.TxCheque)]
    public void Modalidades(ModalidadeCredito m, TipoIndice t)
    {
        Assert.Equal(t, CalculadorasBancarias.TipoTaxaMedia(m));
        Assert.False(string.IsNullOrWhiteSpace(CalculadorasBancarias.NomeModalidade(m)));
    }
}

public class RmcTests
{
    private static CalcularRmcDto Dto() => new(TipoCartaoConsignado.RMC, new DateOnly(2021, 1, 5), 1000m,
        Enumerable.Range(0, 7).Select(i => new DescontoConsignadoDto(new DateOnly(2021, 2, 5).AddMonths(i), 200m)).ToList(),
        new DateOnly(2021, 9, 1), TaxaMensal: 2, Correcao: IndiceAluguel.Nenhum, Juros: JurosPensao.Nenhum, DanosMorais: 5000m);

    [Fact]
    public void ConverteEmConsignado_ApuraIndebitoEmDobro()
    {
        var r = CalculadorasBancarias.CalcularRmc(Dto(), SeriesMensais.Vazia);

        Assert.Equal(2m, r.TaxaAplicada);
        Assert.Equal("informada", r.TaxaFonte);
        Assert.Equal([20m, 16.40m, 12.73m, 8.98m, 5.16m], r.Linhas.Take(5).Select(l => l.JurosMes));
        Assert.Equal(63.27m, r.Linhas[4].Saldo);
        Assert.Equal(6, r.ParcelasParaQuitar);
        Assert.Equal(new DateOnly(2021, 7, 5), r.DataQuitacao);
        Assert.Equal(135.46m, r.Linhas[5].Indebito);
        Assert.Equal(200m, r.Linhas[6].Indebito);
        Assert.Equal(1400m, r.TotalDescontado);
        Assert.Equal(335.46m, r.PagoAMais);
        Assert.Equal(670.92m, r.Restituicao);
        Assert.Equal(5670.92m, r.Total);
        Assert.Equal(0m, r.SaldoDevedorRemanescente);
    }

    [Fact]
    public void TaxaMediaDoBcb_EAntesDaModulacaoSimples()
    {
        var series = SeriesTeste.Criar(new() { [TipoIndice.TxINSS] = new() { [(2020, 12)] = 0.015m } });
        var dto = Dto() with
        {
            TaxaMensal = null, DataContratacao = new DateOnly(2021, 1, 5), DanosMorais = 0,
            Descontos = [new(new DateOnly(2021, 1, 20), 2000m)], DataCalculo = new DateOnly(2021, 2, 1)
        };
        var r = CalculadorasBancarias.CalcularRmc(dto, series);
        Assert.Contains("12/2020", r.TaxaFonte);
        Assert.Equal(1000m, r.PagoAMais); // mesmo mês: sem juros
        Assert.False(r.Linhas[0].EmDobro);
        Assert.Contains(r.Avisos, a => a.Contains("30/03/2021"));
    }

    [Fact]
    public void NaoQuitado_ESemTaxa()
    {
        var dto = Dto() with { TaxaMensal = null, Descontos = [new(new DateOnly(2021, 2, 5), 100m)] };
        var r = CalculadorasBancarias.CalcularRmc(dto, SeriesMensais.Vazia);
        Assert.Null(r.ParcelasParaQuitar);
        Assert.Equal(900m, r.SaldoDevedorRemanescente);
        Assert.Contains(r.Avisos, a => a.Contains("ainda não estaria quitado"));
        Assert.Contains(r.Avisos, a => a.Contains("indisponível"));
    }

    [Fact]
    public void Validacoes()
    {
        Assert.Null(CalculadorasBancarias.ValidarRmc(Dto()));
        Assert.Contains("descontos", CalculadorasBancarias.ValidarRmc(Dto() with { Descontos = [] }));
        Assert.Contains("anterior", CalculadorasBancarias.ValidarRmc(Dto() with { Descontos = [new(new DateOnly(2020, 1, 1), 1)] }));
        Assert.Contains("posterior", CalculadorasBancarias.ValidarRmc(Dto() with { Descontos = [new(new DateOnly(2030, 1, 1), 1)] }));
    }
}

public class SuperendividamentoTests
{
    private static CalcularSuperendividamentoDto Dto() => new(5000m,
        [new("Banco A", 30000m, 1500m, "Crédito pessoal"), new("Loja B", 10000m, 1000m), new("Banco C", 200000m, 2000m, "Imobiliário", Excluida: true)],
        DespesasBasicas: 3000m);

    [Fact]
    public void PlanoCabeNoPrazo_RateioPorCredor()
    {
        var r = CalculadorasBancarias.CalcularSuperendividamento(Dto());
        Assert.True(r.Superendividado);
        Assert.Equal(90m, r.ComprometimentoAtual);
        Assert.Equal(3000m, r.ValorPreservado);
        Assert.Equal(2000m, r.CapacidadePagamento);
        Assert.Equal(40000m, r.TotalRepactuavel);
        Assert.Equal(240000m, r.TotalDividas);
        Assert.Equal(2000m, r.ParcelaPlano);
        Assert.Equal(20, r.MesesPlano);
        Assert.True(r.CabeNoPrazo);
        Assert.Equal(40m, r.ComprometimentoPlano);
        Assert.Equal((75m, 1500m, 30000m), (r.Credores[0].Participacao, r.Credores[0].ParcelaPlano, r.Credores[0].TotalPlano));
        Assert.Equal(500m, r.Credores[1].ParcelaPlano);
        Assert.True(r.Credores[2].Excluida);
        Assert.Equal(200000m, r.Credores[2].Remanescente);
        Assert.Contains(r.Avisos, a => a.Contains("104-A, §1º"));
    }

    [Fact]
    public void TetoDaRenda_NaoCabeNoPrazo_ESemCapacidade()
    {
        var teto = CalculadorasBancarias.CalcularSuperendividamento(Dto() with { ComprometimentoMaximoPercentual = 30 });
        Assert.Equal(1500m, teto.CapacidadePagamento);
        Assert.Equal(27, teto.MesesPlano);
        Assert.Equal(1481.48m, teto.ParcelaPlano);

        var longo = CalculadorasBancarias.CalcularSuperendividamento(new CalcularSuperendividamentoDto(3000m, [new("Banco", 200000m)]));
        Assert.Equal(60, longo.MesesPlano);
        Assert.Equal(2400m, longo.ParcelaPlano);
        Assert.Equal(56000m, longo.Remanescente);
        Assert.False(longo.CabeNoPrazo);
        Assert.False(longo.Superendividado);

        var zero = CalculadorasBancarias.CalcularSuperendividamento(new CalcularSuperendividamentoDto(500m, [new("Banco", 1000m, 100m)]));
        Assert.Equal(0, zero.MesesPlano);
        Assert.Equal(1000m, zero.Remanescente);
        Assert.Contains(zero.Avisos, a => a.Contains("não cobre"));
    }

    [Fact]
    public void Validacoes()
    {
        Assert.Null(CalculadorasBancarias.ValidarSuperendividamento(Dto()));
        Assert.Contains("ao menos", CalculadorasBancarias.ValidarSuperendividamento(Dto() with { Dividas = [] }));
        Assert.Contains("Nenhuma", CalculadorasBancarias.ValidarSuperendividamento(Dto() with { Dividas = [new("X", 10, Excluida: true)] }));
        Assert.Contains("credor", CalculadorasBancarias.ValidarSuperendividamento(Dto() with { Dividas = [new(" ", 10)] }));
    }

    [Fact]
    public void Pdfs_Renderizam()
    {
        var rev = new CalcularRevisionalDto(ModalidadeCredito.Veiculo, new DateOnly(2024, 1, 15), 10000m, 12, new DateOnly(2024, 4, 20), TaxaContratadaMensal: 5, ParcelasPagas: 3, Contratante: "Fulano");
        var rmc = new CalcularRmcDto(TipoCartaoConsignado.RCC, new DateOnly(2021, 1, 5), 1000m, [new(new DateOnly(2021, 5, 5), 2000m)], new DateOnly(2021, 9, 1), TaxaMensal: 2);
        var docs = new[]
        {
            DemonstrativosBancarios.Revisional(rev, CalculadorasBancarias.CalcularRevisional(rev, SeriesMensais.Vazia)),
            DemonstrativosBancarios.Rmc(rmc, CalculadorasBancarias.CalcularRmc(rmc, SeriesMensais.Vazia)),
            DemonstrativosBancarios.Superendividamento(Dto(), CalculadorasBancarias.CalcularSuperendividamento(Dto())),
        };
        foreach (var d in docs)
            Assert.StartsWith("%PDF", Encoding.ASCII.GetString(DemonstrativoPdfRenderer.Renderizar(d, "Escritório", null, new DateTime(2024, 4, 20))[..4]));
    }
}

public class BancarioControllerTests
{
    private static BancarioController Controller(PlanoTipo plano)
    {
        var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        db.IndicesCorrecaoMonetaria.Add(new IndiceCorrecaoMonetaria { Id = Guid.NewGuid(), Tipo = TipoIndice.TxPessoal, Ano = 2024, Mes = 1, Valor = 0.02m, Fonte = "BCB" });
        db.IndicesCorrecaoMonetaria.Add(new IndiceCorrecaoMonetaria { Id = Guid.NewGuid(), Tipo = TipoIndice.TxINSS, Ano = 2021, Mes = 1, Valor = 0.015m, Fonte = "BCB" });
        db.SaveChanges();
        var t = new Mock<ITenantContext>();
        t.Setup(x => x.TenantId).Returns(Guid.NewGuid());
        t.Setup(x => x.Plano).Returns(plano);
        return new BancarioController(db, t.Object);
    }

    private static IWebHostEnvironment Env() => new Mock<IWebHostEnvironment>().Object;
    private static readonly CalcularRevisionalDto Rev = new(ModalidadeCredito.PessoalNaoConsignado, new DateOnly(2024, 1, 15), 10000m, 12, new DateOnly(2024, 4, 20), TaxaContratadaMensal: 5, ParcelasPagas: 3);
    private static readonly CalcularRmcDto Rmc = new(TipoCartaoConsignado.RMC, new DateOnly(2021, 1, 5), 1000m, [new(new DateOnly(2021, 5, 5), 2000m)], new DateOnly(2021, 9, 1));
    private static readonly CalcularSuperendividamentoDto Sup = new(5000m, [new("Banco", 1000m, 100m)]);

    [Fact]
    public async Task PlanoFree_402()
    {
        var c = Controller(PlanoTipo.Free);
        Assert.Equal(402, Assert.IsType<ObjectResult>(await c.GetIndices(default)).StatusCode);
        Assert.Equal(402, Assert.IsType<ObjectResult>(await c.Revisional(Rev, default)).StatusCode);
        Assert.Equal(402, Assert.IsType<ObjectResult>(await c.RevisionalPdf(Rev, Env(), default)).StatusCode);
        Assert.Equal(402, Assert.IsType<ObjectResult>(await c.Rmc(Rmc, default)).StatusCode);
        Assert.Equal(402, Assert.IsType<ObjectResult>(await c.RmcPdf(Rmc, Env(), default)).StatusCode);
        Assert.Equal(402, Assert.IsType<ObjectResult>(c.Superendividamento(Sup)).StatusCode);
        Assert.Equal(402, Assert.IsType<ObjectResult>(await c.SuperendividamentoPdf(Sup, Env(), default)).StatusCode);
    }

    [Fact]
    public async Task PlanoPago_TodasAsRotas()
    {
        var c = Controller(PlanoTipo.Max);

        var indices = Assert.IsType<List<IndiceDisponivelDto>>(Assert.IsType<OkObjectResult>(await c.GetIndices(default)).Value);
        Assert.Equal(6, indices.Count);
        Assert.Equal((2024, 1), (indices[0].Ano, indices[0].Mes));

        var rev = Assert.IsType<ResultadoRevisionalDto>(Assert.IsType<OkObjectResult>(await c.Revisional(Rev, default)).Value);
        Assert.True(rev.Abusiva);
        Assert.IsType<BadRequestObjectResult>(await c.Revisional(Rev with { TaxaContratadaMensal = null }, default));
        Assert.IsType<BadRequestObjectResult>(await c.RevisionalPdf(Rev with { TaxaContratadaMensal = null }, Env(), default));
        Assert.Equal("revisional-20240420.pdf", Assert.IsType<FileContentResult>(await c.RevisionalPdf(Rev, Env(), default)).FileDownloadName);

        var rmc = Assert.IsType<ResultadoRmcDto>(Assert.IsType<OkObjectResult>(await c.Rmc(Rmc, default)).Value);
        Assert.Contains("01/2021", rmc.TaxaFonte);
        Assert.IsType<BadRequestObjectResult>(await c.Rmc(Rmc with { Descontos = [] }, default));
        Assert.IsType<BadRequestObjectResult>(await c.RmcPdf(Rmc with { Descontos = [] }, Env(), default));
        Assert.Equal("rmc-20210901.pdf", Assert.IsType<FileContentResult>(await c.RmcPdf(Rmc, Env(), default)).FileDownloadName);

        Assert.IsType<ResultadoSuperendividamentoDto>(Assert.IsType<OkObjectResult>(c.Superendividamento(Sup)).Value);
        Assert.IsType<BadRequestObjectResult>(c.Superendividamento(Sup with { Dividas = [] }));
        Assert.IsType<BadRequestObjectResult>(await c.SuperendividamentoPdf(Sup with { Dividas = [] }, Env(), default));
        Assert.IsType<FileContentResult>(await c.SuperendividamentoPdf(Sup, Env(), default));
    }
}
