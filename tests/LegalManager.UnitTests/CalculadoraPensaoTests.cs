using System.Net;
using System.Text;
using LegalManager.API.Controllers;
using LegalManager.API.Reports;
using LegalManager.Application.DTOs.Pensao;
using LegalManager.Domain.Entities;
using LegalManager.Domain.Enums;
using LegalManager.Domain.Interfaces;
using LegalManager.Infrastructure.Jobs;
using LegalManager.Infrastructure.Persistence;
using LegalManager.Infrastructure.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace LegalManager.UnitTests;

public class CalculadoraPensaoTests
{
    private static readonly Dictionary<(int, int), decimal> Vazio = [];

    private static SeriesPensao Series(
        Dictionary<(int, int), decimal>? correcao = null,
        Dictionary<(int, int), decimal>? selic = null,
        Dictionary<(int, int), decimal>? ipca = null,
        List<SalarioMinimoVigenciaDto>? salarios = null) =>
        new(correcao ?? Vazio, selic ?? Vazio, ipca ?? Vazio, salarios ?? []);

    private static CalcularPensaoDto Dto(DateOnly calculo, params ParcelaPensaoDto[] parcelas) =>
        new(calculo, FormaPensao.ValorFixo, [.. parcelas], ValorFixo: 1000m,
            Indice: IndicePensao.Nenhum, Juros: JurosPensao.Nenhum);

    private static ParcelaPensaoDto P(int ano, int mes, int dia = 10, decimal? devido = null, decimal pago = 0, string? desc = null) =>
        new(new DateOnly(ano, mes, dia), devido, pago, desc);

    [Fact]
    public void ValorFixo_CorrigePorMesCheioEAplicaJurosSimples()
    {
        var dto = Dto(new DateOnly(2026, 3, 15), P(2026, 1)) with { Indice = IndicePensao.INPC, Juros = JurosPensao.UmPorCentoAoMes };
        var series = Series(correcao: new() { [(2026, 1)] = 0.005m, [(2026, 2)] = 0.004m, [(2026, 3)] = 0.9m });

        var r = CalculadoraPensao.Calcular(dto, series);

        var l = Assert.Single(r.Linhas);
        Assert.Equal(1.009020m, l.FatorCorrecao); // jan e fev; o mês do cálculo fica de fora
        Assert.Equal(1009.02m, l.ValorCorrigido);
        Assert.Equal(2m, l.JurosPercentual);
        Assert.Equal(20.18m, l.Juros);
        Assert.Equal(1029.20m, l.Total);
        Assert.Equal("01/2026", l.Descricao);
        Assert.Equal(9.02m, r.Totais.Correcao);
        Assert.Equal(1029.20m, r.Totais.Total);
        Assert.Equal("INPC (IBGE)", r.Indice);
        Assert.Empty(r.Avisos);
        Assert.Null(r.Prisao);
    }

    [Fact]
    public void ParcelaQueVenceNoMesDoCalculo_NaoTemCorrecaoNemJuros()
    {
        var dto = Dto(new DateOnly(2026, 3, 31), P(2026, 3)) with { Indice = IndicePensao.INPC, Juros = JurosPensao.UmPorCentoAoMes };
        var l = Assert.Single(CalculadoraPensao.Calcular(dto, Series(correcao: new() { [(2026, 3)] = 0.01m })).Linhas);
        Assert.Equal(1m, l.FatorCorrecao);
        Assert.Equal(0m, l.Juros);
        Assert.Equal(1000m, l.Total);
    }

    [Fact]
    public void SalarioMinimo_UsaVigenciaDoMesDoVencimento()
    {
        var salarios = new List<SalarioMinimoVigenciaDto> { new(2025, 1, 1518m), new(2026, 1, 1621m) };
        var dto = Dto(new DateOnly(2026, 2, 1), P(2025, 12), P(2026, 1)) with
        {
            Forma = FormaPensao.SalarioMinimo, ValorFixo = null, PercentualSalarioMinimo = 30m
        };

        var r = CalculadoraPensao.Calcular(dto, Series(salarios: salarios));

        Assert.Equal([455.40m, 486.30m], r.Linhas.Select(l => l.ValorDevido));
        Assert.Equal([1518m, 1621m], r.Linhas.Select(l => l.SalarioMinimo!.Value));
        Assert.Equal(941.70m, r.Totais.Total);
    }

    [Fact]
    public void ValorInformadoNaParcela_PrevaleceSobreAForma()
    {
        var dto = Dto(new DateOnly(2026, 2, 1), P(2026, 1, devido: 750m));
        var l = Assert.Single(CalculadoraPensao.Calcular(dto, Series()).Linhas);
        Assert.Equal(750m, l.ValorDevido);
        Assert.Null(l.SalarioMinimo);
    }

    [Fact]
    public void Deflacao_NaoReduzOValorNominal()
    {
        var dto = Dto(new DateOnly(2026, 2, 20), P(2026, 1)) with { Indice = IndicePensao.IGPM };
        var r = CalculadoraPensao.Calcular(dto, Series(correcao: new() { [(2026, 1)] = -0.01m }));
        Assert.Equal(1m, r.Linhas[0].FatorCorrecao);
        Assert.Equal(1000m, r.Totais.Total);
        Assert.Contains(r.Avisos, a => a.Contains("negativo"));
    }

    [Fact]
    public void IndiceNaoPublicado_ContaZeroEAvisa()
    {
        var dto = Dto(new DateOnly(2026, 4, 15), P(2026, 1)) with { Indice = IndicePensao.IPCAE };
        var r = CalculadoraPensao.Calcular(dto, Series(correcao: new() { [(2026, 1)] = 0.01m, [(2026, 2)] = 0.01m }));
        Assert.Equal(1.0201m, r.Linhas[0].FatorCorrecao);
        var aviso = Assert.Single(r.Avisos);
        Assert.Contains("IPCA-E", aviso);
        Assert.Contains("03/2026", aviso);
    }

    [Fact]
    public void TaxaLegal_UmPorCentoAteAgosto2024_DepoisSelicMenosIpcaNuncaNegativa()
    {
        var dto = Dto(new DateOnly(2024, 11, 20), P(2024, 7)) with { Juros = JurosPensao.TaxaLegal };
        var series = Series(
            selic: new() { [(2024, 9)] = 0.0084m, [(2024, 10)] = 0.0093m },
            ipca: new() { [(2024, 9)] = 0.0044m, [(2024, 10)] = 0.0100m });

        var r = CalculadoraPensao.Calcular(dto, series);

        // jul e ago: 1% cada; set: 0,84% − 0,44%; out: negativo → 0
        Assert.Equal(2.4m, r.Linhas[0].JurosPercentual);
        Assert.Equal(24m, r.Linhas[0].Juros);
        Assert.Contains("Lei 14.905/2024", r.Juros);
        Assert.Empty(r.Avisos);
    }

    [Fact]
    public void TaxaLegal_SemSelicPublicada_Avisa()
    {
        var dto = Dto(new DateOnly(2024, 11, 20), P(2024, 10)) with { Juros = JurosPensao.TaxaLegal };
        var r = CalculadoraPensao.Calcular(dto, Series());
        Assert.Equal(0m, r.Linhas[0].Juros);
        Assert.Contains(r.Avisos, a => a.Contains("10/2024"));
    }

    [Fact]
    public void JurosPersonalizados_APartirDaCitacao()
    {
        var dto = Dto(new DateOnly(2026, 5, 10), P(2026, 1)) with
        {
            Juros = JurosPensao.Personalizado, JurosPercentualMensal = 0.5m, DataCitacao = new DateOnly(2026, 3, 5)
        };
        var r = CalculadoraPensao.Calcular(dto, Series());
        Assert.Equal(1m, r.Linhas[0].JurosPercentual); // mar e abr
        Assert.Equal(10m, r.Linhas[0].Juros);
        Assert.Equal("0,5% ao mês (juros simples)", r.Juros);
    }

    [Fact]
    public void CitacaoAnteriorAoVencimento_JurosDesdeOVencimento()
    {
        var dto = Dto(new DateOnly(2026, 5, 10), P(2026, 3)) with
        {
            Juros = JurosPensao.UmPorCentoAoMes, DataCitacao = new DateOnly(2026, 1, 5)
        };
        Assert.Equal(2m, CalculadoraPensao.Calcular(dto, Series()).Linhas[0].JurosPercentual);
    }

    [Fact]
    public void PagamentoParcialAbateAParcela_EPagamentoAMaiorAvisa()
    {
        var dto = Dto(new DateOnly(2026, 3, 1), P(2026, 1, pago: 400m), P(2026, 2, devido: 500m, pago: 600m));
        var r = CalculadoraPensao.Calcular(dto, Series());
        Assert.Equal(600m, r.Linhas[0].Saldo);
        Assert.Equal(0m, r.Linhas[1].Saldo);
        Assert.Equal(1, r.Totais.Parcelas);
        Assert.Equal(1000m, r.Totais.Pago);
        Assert.Equal(600m, r.Totais.Total);
        Assert.Contains(r.Avisos, a => a.Contains("02/2026"));
    }

    [Fact]
    public void MultaEHonorarios_SemSepararRitos_IncidemSobreOTotal()
    {
        var dto = Dto(new DateOnly(2026, 3, 1), P(2026, 1), P(2026, 2)) with { MultaPercentual = 10, HonorariosPercentual = 10 };
        var t = CalculadoraPensao.Calcular(dto, Series()).Totais;
        Assert.Equal(2000m, t.Subtotal);
        Assert.Equal(200m, t.Multa);
        Assert.Equal(200m, t.Honorarios);
        Assert.Equal(2400m, t.Total);
    }

    [Fact]
    public void SepararRitos_TresAnterioresAoAjuizamentoMaisVincendasNoRitoDaPrisao()
    {
        var dto = Dto(new DateOnly(2026, 7, 1),
                P(2026, 1, devido: 500m), P(2026, 2, devido: 500m), P(2026, 3, devido: 500m),
                P(2026, 4, devido: 500m), P(2026, 5, devido: 500m), P(2026, 6, devido: 500m)) with
        {
            SepararRitos = true, DataAjuizamento = new DateOnly(2026, 5, 1), MultaPercentual = 10, HonorariosPercentual = 10
        };

        var r = CalculadoraPensao.Calcular(dto, Series());

        Assert.Equal(["penhora", "prisao", "prisao", "prisao", "prisao", "prisao"], r.Linhas.Select(l => l.Rito));
        Assert.NotNull(r.Prisao);
        Assert.NotNull(r.Penhora);
        Assert.Equal(5, r.Prisao!.Parcelas);
        Assert.Equal(0m, r.Prisao.Multa); // multa do art. 523 só na penhora
        Assert.Equal(250m, r.Prisao.Honorarios);
        Assert.Equal(2750m, r.Prisao.Total);
        Assert.Equal(600m, r.Penhora!.Total);
        Assert.Equal(3350m, r.Totais.Total);
        Assert.Equal(6, r.Totais.Parcelas);
    }

    [Fact]
    public void SepararRitos_ParcelaQuitadaNaoContaEntreAsTres_ESemAjuizamentoUsaDataDoCalculo()
    {
        var dto = Dto(new DateOnly(2026, 5, 1),
                P(2026, 1), P(2026, 2), P(2026, 3, pago: 1000m), P(2026, 4)) with { SepararRitos = true };

        var r = CalculadoraPensao.Calcular(dto, Series());

        Assert.Equal(["prisao", "prisao", "", "prisao"], r.Linhas.Select(l => l.Rito));
        Assert.Equal(0, r.Penhora!.Parcelas);
        Assert.Equal(3000m, r.Prisao!.Total);
    }

    [Fact]
    public void SepararRitos_ParcelasIguaisNoMesmoDiaContamSeparadas()
    {
        var dto = Dto(new DateOnly(2027, 2, 1),
                P(2026, 10), P(2026, 11), P(2026, 12, desc: "12/2026"), P(2026, 12, desc: "12/2026")) with { SepararRitos = true };
        var r = CalculadoraPensao.Calcular(dto, Series());
        Assert.Equal(3, r.Prisao!.Parcelas);
        Assert.Equal(1, r.Penhora!.Parcelas);
        Assert.Equal("penhora", r.Linhas[0].Rito);
    }

    [Fact]
    public void Validar_DadosValidos_RetornaNull()
    {
        Assert.Null(CalculadoraPensao.Validar(Dto(new DateOnly(2026, 3, 1), P(2026, 1)), Series()));
    }

    [Fact]
    public void Validar_RejeitaEntradasInconsistentes()
    {
        var calc = new DateOnly(2026, 3, 1);
        var salarios = new List<SalarioMinimoVigenciaDto> { new(2025, 1, 1518m) };
        var sm = Dto(calc, P(2026, 1)) with { Forma = FormaPensao.SalarioMinimo, ValorFixo = null, PercentualSalarioMinimo = 30 };

        Assert.Contains("ao menos uma", CalculadoraPensao.Validar(Dto(calc), Series()));
        Assert.Contains("depois da data do cálculo", CalculadoraPensao.Validar(Dto(calc, P(2026, 4)), Series()));
        Assert.Contains("valor mensal", CalculadoraPensao.Validar(Dto(calc, P(2026, 1)) with { ValorFixo = null }, Series()));
        Assert.Null(CalculadoraPensao.Validar(Dto(calc, P(2026, 1, devido: 10)) with { ValorFixo = null }, Series()));
        Assert.Contains("percentual", CalculadoraPensao.Validar(sm with { PercentualSalarioMinimo = null }, Series(salarios: salarios)));
        Assert.Contains("ainda não foi carregada", CalculadoraPensao.Validar(sm, Series()));
        Assert.Contains("12/2024", CalculadoraPensao.Validar(sm with { Parcelas = [P(2024, 12)] }, Series(salarios: salarios)));
        Assert.Null(CalculadoraPensao.Validar(sm, Series(salarios: salarios)));
        Assert.Contains("taxa mensal", CalculadoraPensao.Validar(Dto(calc, P(2026, 1)) with { Juros = JurosPensao.Personalizado }, Series()));
    }

    [Fact]
    public void Meses_AtravessaAViradaDoAno()
    {
        Assert.Equal([(2025, 11), (2025, 12), (2026, 1)], CalculadoraPensao.Meses((2025, 11), (2026, 2)).ToList());
        Assert.Empty(CalculadoraPensao.Meses((2026, 2), (2026, 2)));
    }

    [Theory]
    [InlineData(IndicePensao.INPC, TipoIndice.INPC)]
    [InlineData(IndicePensao.IPCA, TipoIndice.IPCA)]
    [InlineData(IndicePensao.IPCAE, TipoIndice.TJSP)]
    [InlineData(IndicePensao.IGPM, TipoIndice.IGPM)]
    public void TipoDoIndice_MapeiaParaASerieGuardada(IndicePensao indice, TipoIndice tipo)
    {
        Assert.Equal(tipo, CalculadoraPensao.TipoDoIndice(indice));
        Assert.NotEqual("Sem correção monetária", CalculadoraPensao.NomeIndice(indice));
    }

    [Fact]
    public void SemIndiceNemJuros_Rotulos()
    {
        Assert.Null(CalculadoraPensao.TipoDoIndice(IndicePensao.Nenhum));
        Assert.Equal("Sem correção monetária", CalculadoraPensao.NomeIndice(IndicePensao.Nenhum));
        Assert.Equal("Sem juros de mora", CalculadoraPensao.NomeJuros(Dto(new DateOnly(2026, 1, 1))));
        Assert.Equal("1% ao mês (juros simples)", CalculadoraPensao.NomeJuros(Dto(new DateOnly(2026, 1, 1)) with { Juros = JurosPensao.UmPorCentoAoMes }));
    }

    [Fact]
    public void Pdf_RenderizaComRitosEAvisos()
    {
        var dto = Dto(new DateOnly(2026, 7, 1), P(2026, 1), P(2026, 2, pago: 100m), P(2026, 5)) with
        {
            Forma = FormaPensao.SalarioMinimo, ValorFixo = null, PercentualSalarioMinimo = 30, Indice = IndicePensao.INPC,
            Juros = JurosPensao.UmPorCentoAoMes, DataCitacao = new DateOnly(2026, 2, 1), MultaPercentual = 10, HonorariosPercentual = 10,
            SepararRitos = true, DataAjuizamento = new DateOnly(2026, 4, 1),
            Processo = "0001234-56.2026.8.26.0100", Alimentando = "Filha", Alimentante = "Pai"
        };
        var series = Series(salarios: [new(2026, 1, 1621m)]);
        var r = CalculadoraPensao.Calcular(dto, series);

        var pdf = PensaoPdfRenderer.Renderizar(dto, r, "Escritório Modelo", null, new DateTime(2026, 7, 1, 10, 0, 0));

        Assert.StartsWith("%PDF", Encoding.ASCII.GetString(pdf[..4]));
        Assert.NotEmpty(r.Avisos); // INPC não publicado
    }
}

public class PensaoControllerTests
{
    private static AppDbContext Db()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        return new AppDbContext(options);
    }

    private static PensaoController Controller(AppDbContext db, PlanoTipo plano, Guid? tenantId = null)
    {
        var tenant = new Mock<ITenantContext>();
        tenant.Setup(t => t.TenantId).Returns(tenantId ?? Guid.NewGuid());
        tenant.Setup(t => t.UserId).Returns(Guid.NewGuid());
        tenant.Setup(t => t.Plano).Returns(plano);
        return new PensaoController(db, tenant.Object);
    }

    private static void Semear(AppDbContext db)
    {
        void Indice(TipoIndice tipo, int ano, int mes, decimal valor) => db.IndicesCorrecaoMonetaria.Add(new IndiceCorrecaoMonetaria
        {
            Id = Guid.NewGuid(), Tipo = tipo, Ano = ano, Mes = mes, Valor = valor, Fonte = "BCB", AtualizadoEm = DateTime.UtcNow
        });
        Indice(TipoIndice.INPC, 2026, 1, 0.005m);
        Indice(TipoIndice.INPC, 2026, 2, 0.004m);
        Indice(TipoIndice.SELIC, 2026, 1, 0.01m);
        Indice(TipoIndice.IPCA, 2026, 1, 0.004m);
        foreach (var (ano, mes, valor) in new[] { (2025, 12, 1518m), (2026, 1, 1621m), (2026, 2, 1621m) })
            db.SalariosMinimos.Add(new SalarioMinimo { Id = Guid.NewGuid(), Ano = ano, Mes = mes, Valor = valor, Fonte = "BCB", AtualizadoEm = DateTime.UtcNow });
        db.SaveChanges();
    }

    private static CalcularPensaoDto Dto() => new(
        new DateOnly(2026, 3, 15), FormaPensao.SalarioMinimo,
        [new ParcelaPensaoDto(new DateOnly(2026, 1, 10))],
        PercentualSalarioMinimo: 30, Indice: IndicePensao.INPC, Juros: JurosPensao.TaxaLegal);

    private static IWebHostEnvironment Env()
    {
        var env = new Mock<IWebHostEnvironment>();
        env.Setup(e => e.WebRootPath).Returns((string)null!);
        return env.Object;
    }

    [Fact]
    public async Task PlanoFree_Recebe402EmTodasAsRotas()
    {
        var c = Controller(Db(), PlanoTipo.Free);
        Assert.Equal(402, Assert.IsType<ObjectResult>(await c.GetIndices(default)).StatusCode);
        Assert.Equal(402, Assert.IsType<ObjectResult>(await c.Calcular(Dto(), default)).StatusCode);
        Assert.Equal(402, Assert.IsType<ObjectResult>(await c.Pdf(Dto(), Env(), default)).StatusCode);
    }

    [Theory]
    [InlineData(PlanoTipo.Plus)]
    [InlineData(PlanoTipo.Pro)]
    [InlineData(PlanoTipo.Max)]
    [InlineData(PlanoTipo.Enterprise)]
    public async Task PlanosPagos_Calculam(PlanoTipo plano)
    {
        var db = Db();
        Semear(db);
        var ok = Assert.IsType<OkObjectResult>(await Controller(db, plano).Calcular(Dto(), default));
        var r = Assert.IsType<ResultadoPensaoDto>(ok.Value);
        var l = Assert.Single(r.Linhas);
        Assert.Equal(486.30m, l.ValorDevido);
        Assert.Equal(1.009020m, l.FatorCorrecao);
        // jan: Selic 1% − IPCA 0,4%; fev: sem Selic publicada (aviso)
        Assert.Equal(0.6m, l.JurosPercentual);
        Assert.Contains(r.Avisos, a => a.Contains("02/2026"));
    }

    [Fact]
    public async Task Calcular_DadosInvalidos_Retorna400ComMensagem()
    {
        var db = Db();
        Semear(db);
        var dto = Dto() with { PercentualSalarioMinimo = null };
        var bad = Assert.IsType<BadRequestObjectResult>(await Controller(db, PlanoTipo.Plus).Calcular(dto, default));
        Assert.Contains("percentual", bad.Value!.ToString());
        Assert.IsType<BadRequestObjectResult>(await Controller(db, PlanoTipo.Plus).Pdf(dto, Env(), default));
    }

    [Fact]
    public async Task GetIndices_UltimoMesDeCadaSerieEVigenciasDoSalarioMinimo()
    {
        var db = Db();
        Semear(db);
        var ok = Assert.IsType<OkObjectResult>(await Controller(db, PlanoTipo.Plus).GetIndices(default));
        var dto = Assert.IsType<IndicesPensaoDto>(ok.Value);

        var inpc = dto.Indices.Single(i => i.Indice == "INPC");
        Assert.Equal((2026, 2), (inpc.Ano, inpc.Mes));
        Assert.Null(dto.Indices.Single(i => i.Indice == "IGPM").Ano);
        Assert.Contains(dto.Indices, i => i.Indice == "SELIC" && i.Mes == 1);
        Assert.Equal([new(2025, 12, 1518m), new(2026, 1, 1621m)], dto.SalariosMinimos);
    }

    [Fact]
    public async Task Pdf_RetornaArquivo()
    {
        var db = Db();
        Semear(db);
        var tenantId = Guid.NewGuid();
        db.Tenants.Add(new Tenant { Id = tenantId, Nome = "Escritório Teste" });
        db.SaveChanges();

        var file = Assert.IsType<FileContentResult>(await Controller(db, PlanoTipo.Plus, tenantId).Pdf(Dto(), Env(), default));
        Assert.Equal("application/pdf", file.ContentType);
        Assert.Equal("calculo-pensao-20260315.pdf", file.FileDownloadName);
        Assert.StartsWith("%PDF", Encoding.ASCII.GetString(file.FileContents[..4]));
    }

    [Fact]
    public void Vigencias_SoMesesEmQueOValorMuda()
    {
        var v = PensaoController.Vigencias([new(2025, 1, 1m), new(2025, 2, 1m), new(2025, 3, 2m), new(2025, 4, 2m), new(2025, 5, 1m)]);
        Assert.Equal([(2025, 1), (2025, 3), (2025, 5)], v.Select(x => (x.Ano, x.Mes)));
    }
}

public class IndicesCorrecaoJobTests
{
    private sealed class BcbFake(Func<string, string> responder) : HttpMessageHandler
    {
        public List<string> Urls { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var url = request.RequestUri!.ToString();
            Urls.Add(url);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responder(url), Encoding.UTF8, "application/json")
            });
        }
    }

    private static AppDbContext Db() => new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static IndicesCorrecaoJob Job(AppDbContext db, HttpMessageHandler handler)
    {
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient("BCB")).Returns(() => new HttpClient(handler, disposeHandler: false));
        return new IndicesCorrecaoJob(db, factory.Object, NullLogger<IndicesCorrecaoJob>.Instance);
    }

    private static string Responder(string url, string valorFev = "0.50")
    {
        var sm = url.Contains("sgs.1619/");
        if (url.Contains("ultimos/12"))
            return sm ? """[{"data":"01/02/2000","valor":"151.00"}]"""
                      : $$"""[{"data":"01/03/2000","valor":"0.30"},{"data":"01/02/2000","valor":"{{valorFev}}"}]""";
        if (!url.Contains("dataInicial=01/01/2000")) return "[]";
        // Fora de ordem, como a série da Selic vem do BCB.
        return sm ? """[{"data":"01/02/2000","valor":"151.00"},{"data":"01/01/2000","valor":"136.00"}]"""
                  : """[{"data":"01/02/2000","valor":"0.20"},{"data":"01/01/2000","valor":"0.61"}]""";
    }

    [Fact]
    public async Task PrimeiraExecucao_BaixaHistoricoEmJanelasDeDezAnos_DepoisSoOsUltimos12Meses()
    {
        var db = Db();
        Assert.True(await IndicesCorrecaoJob.PrecisaCargaHistoricaAsync(db));

        var handler = new BcbFake(u => Responder(u));
        await Job(db, handler).ExecutarAsync();

        var series = IndicesCorrecaoJob.Series.Count + 1;
        var janelas = (DateTime.UtcNow.Year - IndicesCorrecaoJob.AnoInicioHistorico) / 10 + 1;
        Assert.Equal(series * janelas, handler.Urls.Count);
        Assert.All(handler.Urls, u => Assert.Contains("dataInicial=", u));
        foreach (var (_, tipo) in IndicesCorrecaoJob.Series)
        {
            var jan = await db.IndicesCorrecaoMonetaria.SingleAsync(i => i.Tipo == tipo && i.Ano == 2000 && i.Mes == 1);
            Assert.Equal(0.0061m, jan.Valor);
        }
        Assert.Equal(151m, (await db.SalariosMinimos.SingleAsync(s => s.Mes == 2)).Valor);
        Assert.False(await IndicesCorrecaoJob.PrecisaCargaHistoricaAsync(db));

        var incremental = new BcbFake(u => Responder(u, valorFev: "0.25"));
        await Job(db, incremental).ExecutarAsync();
        Assert.All(incremental.Urls, u => Assert.Contains("ultimos/12", u));
        var fev = await db.IndicesCorrecaoMonetaria.SingleAsync(i => i.Tipo == TipoIndice.INPC && i.Ano == 2000 && i.Mes == 2);
        Assert.Equal(0.0025m, fev.Valor); // revisão regravada
        Assert.True(await db.IndicesCorrecaoMonetaria.AnyAsync(i => i.Tipo == TipoIndice.SELIC && i.Mes == 3));
        Assert.Equal(2, await db.SalariosMinimos.CountAsync());
    }

    [Fact]
    public async Task FalhaNoBcb_NaoDerrubaOJob()
    {
        var db = Db();
        var handler = new BcbFake(u => u.Contains("sgs.433/") ? throw new HttpRequestException("fora do ar") : Responder(u));
        await Job(db, handler).ExecutarAsync();

        Assert.False(await db.IndicesCorrecaoMonetaria.AnyAsync(i => i.Tipo == TipoIndice.IPCA));
        Assert.True(await db.IndicesCorrecaoMonetaria.AnyAsync(i => i.Tipo == TipoIndice.INPC));
        Assert.True(await IndicesCorrecaoJob.PrecisaCargaHistoricaAsync(db));
    }

    [Fact]
    public async Task SemSalarioMinimo_PrecisaCarga()
    {
        var db = Db();
        foreach (var (_, tipo) in IndicesCorrecaoJob.Series)
            db.IndicesCorrecaoMonetaria.Add(new IndiceCorrecaoMonetaria { Id = Guid.NewGuid(), Tipo = tipo, Ano = 2000, Mes = 1, Fonte = "BCB" });
        await db.SaveChangesAsync();
        Assert.True(await IndicesCorrecaoJob.PrecisaCargaHistoricaAsync(db));
    }

    [Fact]
    public void Parse_IgnoraItensMalformados()
    {
        var itens = IndicesCorrecaoJob.Parse("""[{"data":"01/04/2026","valor":"0.43"},{"data":"x","valor":"1"},{"data":"01/05/2026","valor":"abc"}]""");
        Assert.Equal([(2026, 4, 0.43m)], itens);
    }
}
