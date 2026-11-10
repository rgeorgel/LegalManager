using System.Text;
using LegalManager.API.Controllers;
using LegalManager.API.Reports;
using LegalManager.Application.DTOs.Pensao;
using LegalManager.Application.DTOs.Trabalhista;
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

internal static class SeriesTeste
{
    public static SeriesMensais Criar(Dictionary<TipoIndice, Dictionary<(int, int), decimal>>? indices = null,
        List<SalarioMinimoVigenciaDto>? salarios = null) =>
        new((indices ?? []).ToDictionary(k => k.Key, k => (IReadOnlyDictionary<(int Ano, int Mes), decimal>)k.Value), salarios ?? []);
}

public class ContratoTrabalhoTests
{
    [Theory]
    [InlineData("2021-03-01", "2024-03-15", 3)]
    [InlineData("2021-03-16", "2024-03-15", 2)]
    [InlineData("2020-02-29", "2021-02-28", 1)]
    public void AnosCompletos(string inicio, string fim, int anos) =>
        Assert.Equal(anos, ContratoTrabalho.AnosCompletos(DateOnly.Parse(inicio), DateOnly.Parse(fim)));

    [Theory]
    [InlineData(0, 30)]
    [InlineData(3, 39)]
    [InlineData(25, 90)]
    public void AvisoProporcional(int anos, int dias) => Assert.Equal(dias, ContratoTrabalho.DiasAvisoProporcional(anos));

    [Fact]
    public void Avos13_ContaMesesCom15DiasOuMais()
    {
        Assert.Equal(4, ContratoTrabalho.Avos13(2024, new DateOnly(2021, 3, 1), new DateOnly(2024, 4, 23)));
        Assert.Equal(3, ContratoTrabalho.Avos13(2024, new DateOnly(2021, 3, 1), new DateOnly(2024, 4, 14)));
        Assert.Equal(1, ContratoTrabalho.Avos13(2024, new DateOnly(2024, 12, 17), new DateOnly(2024, 12, 31)));
    }

    [Fact]
    public void AvosFerias_DoUltimoAniversario()
    {
        Assert.Equal(2, ContratoTrabalho.AvosFerias(new DateOnly(2021, 3, 1), new DateOnly(2024, 4, 23)));
        Assert.Equal(1, ContratoTrabalho.AvosFerias(new DateOnly(2021, 3, 1), new DateOnly(2024, 4, 10)));
        Assert.Equal(0, ContratoTrabalho.AvosFerias(new DateOnly(2021, 3, 1), new DateOnly(2024, 3, 10)));
    }

    [Fact]
    public void DiasDsr_DomingosEFeriadosNacionais_SemPontosFacultativos()
    {
        Assert.Equal((26, 5), ContratoTrabalho.DiasDsr(2024, 1)); // 4 domingos + 1º de janeiro
        Assert.Equal((25, 4), ContratoTrabalho.DiasDsr(2024, 2)); // Carnaval (12 e 13/02) não conta
        Assert.Equal((23, 7), ContratoTrabalho.DiasDsr(2024, 11)); // 4 domingos + 02/11 (sábado), 15/11 e 20/11
    }
}

public class AtualizacaoTrabalhistaTests
{
    private static SeriesMensais Series() => SeriesTeste.Criar(new()
    {
        [TipoIndice.TJSP] = new() { [(2024, 6)] = 0.005m, [(2024, 7)] = 0.003m },
        [TipoIndice.TR] = new() { [(2024, 6)] = 0.001m, [(2024, 7)] = 0.001m },
        [TipoIndice.SELIC] = new() { [(2024, 8)] = 0.0087m, [(2024, 9)] = 0.0084m, [(2024, 10)] = 0.0093m },
        [TipoIndice.IPCA] = new() { [(2024, 9)] = 0.0044m, [(2024, 10)] = 0.0056m },
    });

    [Fact]
    public void Adc58_PreJudicialIpcaETr_JudicialSelic_DepoisIpcaMaisTaxaLegal()
    {
        var a = new AtualizacaoTrabalhista(CriterioAtualizacao.Adc58, new DateOnly(2024, 11, 15), new DateOnly(2024, 8, 10), Series());
        var v = a.Atualizar(1000m, new DateOnly(2024, 6, 1));
        Assert.Equal(1.018120m, v.FatorCorrecao);
        Assert.Equal(1018.12m, v.Corrigido);
        Assert.Equal(1.84m, v.JurosPercentual); // TR 0,1+0,1; Selic 0,87; (0,84−0,44); (0,93−0,56)
        Assert.Equal(18.73m, v.Juros);
        Assert.Equal(1036.85m, v.Total);
        Assert.Empty(a.Avisos());
    }

    [Fact]
    public void SemAjuizamento_TudoPreJudicial_EAvisaIndiceFaltante()
    {
        var a = new AtualizacaoTrabalhista(CriterioAtualizacao.Adc58, new DateOnly(2024, 9, 1), null, Series());
        var v = a.Atualizar(1000m, new DateOnly(2024, 6, 1));
        Assert.Equal(1.008015m, v.FatorCorrecao);
        Assert.Equal(0.2m, v.JurosPercentual);
        Assert.Contains(a.Avisos(), x => x.StartsWith("IPCA-E") && x.Contains("08/2024"));
    }

    [Fact]
    public void Nenhum_MantemONominal()
    {
        var a = new AtualizacaoTrabalhista(CriterioAtualizacao.Nenhum, new DateOnly(2024, 11, 15), null, Series());
        Assert.Equal(1000m, a.Atualizar(1000m, new DateOnly(2024, 6, 1)).Total);
        Assert.StartsWith("Sem atualização", AtualizacaoTrabalhista.Descricao(CriterioAtualizacao.Nenhum, null));
        Assert.Contains("Lei 14.905", AtualizacaoTrabalhista.Descricao(CriterioAtualizacao.Adc58, new DateOnly(2024, 1, 1)));
    }
}

public class CalculadoraTrabalhistaTests
{
    private static CalcularTrabalhistaDto Base(TipoRescisao rescisao = TipoRescisao.SemJustaCausa) => new(
        new DateOnly(2021, 3, 1), new DateOnly(2024, 3, 15), rescisao, 3000m, new DateOnly(2024, 4, 1),
        SaldoFgts: 10000m, Atualizacao: CriterioAtualizacao.Nenhum);

    private static decimal Valor(ResultadoTrabalhistaDto r, string descricao) =>
        r.Verbas.Single(v => v.Descricao == descricao).Valor;

    [Fact]
    public void SemJustaCausa_AvisoProporcionalProjetaAvos()
    {
        var r = CalculadoraTrabalhista.Calcular(Base(), SeriesMensais.Vazia);

        Assert.Equal(3, r.AnosServico);
        Assert.Equal(39, r.DiasAvisoIndenizados);
        Assert.Equal(new DateOnly(2024, 4, 23), r.DataProjetada);
        Assert.Equal(1500m, Valor(r, "Saldo de salário"));
        Assert.Equal(3900m, Valor(r, "Aviso prévio indenizado"));
        Assert.Equal(1000m, Valor(r, "13º salário proporcional 2024"));
        Assert.Equal(500m, Valor(r, "Férias proporcionais"));
        Assert.Equal(166.67m, Valor(r, "1/3 sobre férias proporcionais"));
        Assert.Equal(512m, Valor(r, "FGTS sobre saldo de salário, aviso e 13º (8%)"));
        Assert.Equal(4204.80m, Valor(r, "Multa de 40% do FGTS"));
        Assert.Equal(11783.47m, r.TotalNominal);
        Assert.Equal(r.TotalNominal, r.TotalGeral);
        Assert.Single(r.Grupos);
        Assert.Empty(r.Meses);
    }

    [Fact]
    public void JustaCausa_SoSaldoEFeriasVencidas()
    {
        var r = CalculadoraTrabalhista.Calcular(Base(TipoRescisao.JustaCausa) with { FeriasVencidas = 1 }, SeriesMensais.Vazia);
        Assert.Equal(0, r.DiasAviso);
        Assert.Equal(
            ["Saldo de salário", "Férias vencidas", "1/3 constitucional sobre férias vencidas", "FGTS sobre saldo de salário, aviso e 13º (8%)"],
            r.Verbas.Select(v => v.Descricao));
        Assert.Equal(120m, Valor(r, "FGTS sobre saldo de salário, aviso e 13º (8%)"));
        Assert.Equal(1500m + 3000m + 1000m + 120m, r.TotalNominal);
    }

    [Fact]
    public void PedidoDemissao_AvisoNaoCumpridoDescontaUmSalario()
    {
        var r = CalculadoraTrabalhista.Calcular(Base(TipoRescisao.PedidoDemissao) with { Aviso = AvisoPrevioTipo.NaoCumprido }, SeriesMensais.Vazia);
        Assert.Equal(-3000m, Valor(r, "Desconto do aviso prévio não cumprido"));
        Assert.Equal(750m, Valor(r, "13º salário proporcional 2024")); // jan, fev e 15 dias de março
        Assert.DoesNotContain(r.Verbas, v => v.Descricao.StartsWith("Multa de"));
    }

    [Fact]
    public void Acordo_MetadeDoAvisoEMultaDe20()
    {
        var r = CalculadoraTrabalhista.Calcular(Base(TipoRescisao.Acordo), SeriesMensais.Vazia);
        Assert.Equal(19, r.DiasAvisoIndenizados);
        Assert.Equal(1900m, Valor(r, "Aviso prévio indenizado"));
        Assert.Contains(r.Verbas, v => v.Descricao == "Multa de 20% do FGTS");
    }

    [Fact]
    public void AvisoTrabalhado_SemIndenizacaoNemProjecao()
    {
        var r = CalculadoraTrabalhista.Calcular(Base() with { Aviso = AvisoPrevioTipo.Trabalhado }, SeriesMensais.Vazia);
        Assert.Equal(0, r.DiasAvisoIndenizados);
        Assert.Equal(new DateOnly(2024, 3, 15), r.DataProjetada);
        Assert.DoesNotContain(r.Verbas, v => v.Descricao == "Aviso prévio indenizado");
    }

    [Fact]
    public void FeriasEmDobro_ProjecaoQueCompletaPeriodo_EValoresPagos()
    {
        var dto = Base() with
        {
            Saida = new DateOnly(2024, 2, 20), FeriasVencidas = 2, FeriasVencidasEmDobro = 1, ValoresPagos = 500m, SaldoFgts = null
        };
        var r = CalculadoraTrabalhista.Calcular(dto, SeriesMensais.Vazia);
        Assert.Equal(6000m, Valor(r, "Férias vencidas"));
        Assert.Equal(3000m, Valor(r, "Dobra das férias vencidas (art. 137 da CLT)"));
        Assert.Equal(3000m, Valor(r, "Férias integrais (período completado na projeção do aviso)"));
        Assert.Equal(-500m, Valor(r, "Valores já pagos na rescisão"));
        Assert.Contains(r.Avisos, a => a.StartsWith("Saldo do FGTS estimado"));
    }

    [Fact]
    public void HorasExtras_DsrEReflexos()
    {
        var dto = new CalcularTrabalhistaDto(new DateOnly(2024, 1, 1), new DateOnly(2024, 1, 31), TipoRescisao.SemJustaCausa,
            2200m, new DateOnly(2024, 2, 1), SaldoFgts: 0, HorasExtrasMes: 10, Atualizacao: CriterioAtualizacao.Nenhum);

        var r = CalculadoraTrabalhista.Calcular(dto, SeriesMensais.Vazia);

        decimal V(string d) => r.Verbas.Single(v => v.Grupo == "Horas extras" && v.Descricao == d).Valor;
        Assert.Equal(150m, V("Horas extras (50%)"));
        Assert.Equal(28.85m, V("Reflexo em DSR (domingos e feriados)"));
        Assert.Equal(14.90m, V("Reflexo em 13º salário"));
        Assert.Equal(19.87m, V("Reflexo em férias + 1/3"));
        Assert.Equal(178.85m, V("Reflexo em aviso prévio"));
        Assert.Equal(29.81m, V("FGTS (8%) sobre as parcelas e reflexos"));
        Assert.Equal(11.92m, V("Multa de 40% sobre o FGTS"));
        var mes = Assert.Single(r.Meses);
        Assert.Equal("01/2024", mes.Competencia);
        Assert.Equal(new DateOnly(2024, 1, 1), r.InicioPedidos);
    }

    [Fact]
    public void AdicionaisMensais_Insalubridade_Periculosidade_Noturno()
    {
        var dto = new CalcularTrabalhistaDto(new DateOnly(2024, 1, 1), new DateOnly(2024, 1, 31), TipoRescisao.TerminoContrato,
            2200m, new DateOnly(2024, 2, 1), HorasNoturnasMes: 7, InsalubridadeGrau: 20, Periculosidade: true,
            Atualizacao: CriterioAtualizacao.Nenhum);
        var series = SeriesTeste.Criar(salarios: [new(2024, 1, 1412m)]);

        var r = CalculadoraTrabalhista.Calcular(dto, series);

        var mes = Assert.Single(r.Meses);
        Assert.Equal(16m, mes.Noturno);       // 10 × 20% × 7 × 8/7
        Assert.Equal(282.40m, mes.Insalubridade);
        Assert.Equal(660m, mes.Periculosidade);
        Assert.DoesNotContain(r.Verbas, v => v.Descricao.StartsWith("Multa de")); // término de contrato: sem multa do FGTS
        Assert.Contains(r.Avisos, a => a.Contains("art. 193, §2º"));
    }

    [Fact]
    public void PrescricaoQuinquenal_EBienal()
    {
        var dto = new CalcularTrabalhistaDto(new DateOnly(2010, 1, 1), new DateOnly(2020, 6, 30), TipoRescisao.SemJustaCausa,
            2200m, new DateOnly(2023, 1, 10), SaldoFgts: 0, HorasExtrasMes: 10, DataAjuizamento: new DateOnly(2022, 8, 1),
            Atualizacao: CriterioAtualizacao.Nenhum);
        var r = CalculadoraTrabalhista.Calcular(dto, SeriesMensais.Vazia);
        Assert.Equal(new DateOnly(2017, 8, 1), r.InicioPedidos);
        Assert.Equal("08/2017", r.Meses[0].Competencia);
        Assert.Contains(r.Avisos, a => a.Contains("prescrição bienal"));

        var tudoPrescrito = CalculadoraTrabalhista.Calcular(dto with { DataAjuizamento = new DateOnly(2026, 1, 1), DataCalculo = new DateOnly(2026, 2, 1) }, SeriesMensais.Vazia);
        Assert.Empty(tudoPrescrito.Meses);
        Assert.Contains(tudoPrescrito.Avisos, a => a.Contains("prescrito"));
    }

    [Fact]
    public void Multas_Indenizacoes_Honorarios_EAtualizacao()
    {
        var dto = Base() with
        {
            Multa477 = true, Multa467 = true, DanoMoral = 5000m, OutrosPedidos = 1000m, OutrosDescricao = "Vale-transporte",
            HonorariosPercentual = 10, Atualizacao = CriterioAtualizacao.Adc58, DataCalculo = new DateOnly(2024, 6, 1)
        };
        var series = SeriesTeste.Criar(new() { [TipoIndice.TJSP] = new() { [(2024, 3)] = 0.01m, [(2024, 4)] = 0.01m, [(2024, 5)] = 0.01m } });

        var r = CalculadoraTrabalhista.Calcular(dto, series);

        Assert.Equal(3000m, Valor(r, "Multa do art. 477, §8º, da CLT"));
        Assert.Equal(5635.74m, Valor(r, "Multa do art. 467 da CLT")); // 50% × (1500+3900+1000+666,67+4204,80)
        var dano = r.Verbas.Single(v => v.Descricao == "Indenização por danos morais");
        Assert.Equal(5000m, dano.Total); // sem atualização antes do arbitramento
        Assert.Contains(r.Verbas, v => v.Descricao == "Vale-transporte");
        Assert.True(r.TotalAtualizado > r.TotalNominal);
        Assert.Equal(Math.Round(r.TotalAtualizado * 0.1m, 2, MidpointRounding.AwayFromZero), r.Honorarios);
        Assert.Equal(2, r.Grupos.Count);
        Assert.Contains(r.Avisos, a => a.Contains("TR"));
    }

    [Fact]
    public void Validar()
    {
        Assert.Null(CalculadoraTrabalhista.Validar(Base()));
        Assert.Contains("anterior à admissão", CalculadoraTrabalhista.Validar(Base() with { Saida = new DateOnly(2020, 1, 1) }));
        Assert.Contains("data do cálculo", CalculadoraTrabalhista.Validar(Base() with { DataCalculo = new DateOnly(2024, 1, 1) }));
        Assert.Contains("ajuizamento", CalculadoraTrabalhista.Validar(Base() with { DataAjuizamento = new DateOnly(2030, 1, 1) }));
        Assert.Contains("em dobro", CalculadoraTrabalhista.Validar(Base() with { FeriasVencidasEmDobro = 1 }));
        Assert.Contains("insalubridade", CalculadoraTrabalhista.Validar(Base() with { InsalubridadeGrau = 15 }));
    }

    [Theory]
    [InlineData(TipoRescisao.SemJustaCausa, 40)]
    [InlineData(TipoRescisao.RescisaoIndireta, 40)]
    [InlineData(TipoRescisao.Acordo, 20)]
    [InlineData(TipoRescisao.PedidoDemissao, 0)]
    [InlineData(TipoRescisao.JustaCausa, 0)]
    [InlineData(TipoRescisao.TerminoContrato, 0)]
    public void MultaFgtsENome(TipoRescisao r, int pct)
    {
        Assert.Equal(pct, CalculadoraTrabalhista.PercentualMultaFgts(r));
        Assert.False(string.IsNullOrWhiteSpace(CalculadoraTrabalhista.NomeRescisao(r)));
    }

    [Fact]
    public void Pdf_Renderiza()
    {
        var dto = Base() with { HorasExtrasMes = 10, Multa477 = true, Reclamante = "Fulano", Processo = "0000001-00.2024.5.02.0001" };
        var r = CalculadoraTrabalhista.Calcular(dto, SeriesMensais.Vazia);
        var pdf = DemonstrativoPdfRenderer.Renderizar(DemonstrativosTrabalhistas.Trabalhista(dto, r), "Escritório", null, new DateTime(2024, 4, 1));
        Assert.StartsWith("%PDF", Encoding.ASCII.GetString(pdf[..4]));
    }
}

public class CalculadoraFgtsTests
{
    [Fact]
    public void GerarCompetencias_ProporcionalNasPontas_13PorAvos_EAviso()
    {
        var c = CalculadoraFgts.GerarCompetencias(new GerarCompetenciasFgtsDto(
            new DateOnly(2024, 3, 10), new DateOnly(2024, 12, 20), 3000m, true, 33));

        Assert.Equal(12, c.Count);
        Assert.Equal(2200m, c[0].Remuneracao);
        Assert.Equal(3000m, c[1].Remuneracao);
        Assert.Equal(2000m, c.Single(x => x is { Mes: 12, Tipo: "mensal" }).Remuneracao);
        Assert.Equal(2500m, c.Single(x => x.Tipo == "13").Remuneracao); // 10/12
        Assert.Equal(3300m, c.Single(x => x.Tipo == "aviso").Remuneracao);
        Assert.Empty(CalculadoraFgts.GerarCompetencias(new GerarCompetenciasFgtsDto(new DateOnly(2024, 3, 10), new DateOnly(2024, 1, 1), 3000m)));
    }

    private static CalcularFgtsDevidoDto Devido() => new(
        new DateOnly(2024, 4, 1),
        [new(2024, 1, 2000m), new(2024, 2, 2000m, 160m), new(2024, 2, 333.33m, 0, "13")],
        Atualizacao: CriterioAtualizacao.Nenhum);

    [Fact]
    public void Devido_DiferencasEMultaSobreOsDepositosDevidos()
    {
        var r = CalculadoraFgts.CalcularDevido(Devido(), SeriesMensais.Vazia);
        Assert.Equal([160m, 0m, 26.67m], r.Linhas.Select(l => l.Diferenca));
        Assert.Equal("13º salário 2024", r.Linhas[2].Descricao);
        Assert.Equal(186.67m, r.Diferencas);
        Assert.Equal(346.67m, r.BaseMulta);
        Assert.Equal(138.67m, r.Multa);
        Assert.Equal(325.34m, r.Total);
        Assert.Contains(r.Avisos, a => a.Contains("saldo para fins rescisórios"));
    }

    [Fact]
    public void Devido_ComSaldoDoExtrato_MultaJaPaga_EAtualizacao()
    {
        var series = SeriesTeste.Criar(new() { [TipoIndice.TJSP] = new() { [(2024, 2)] = 0.01m, [(2024, 3)] = 0.01m } });
        var dto = Devido() with
        {
            SaldoParaFinsRescisorios = 1000m, MultaJaPaga = 100m, Atualizacao = CriterioAtualizacao.Adc58,
            DataRescisao = new DateOnly(2024, 2, 29), Reclamante = "Fulano"
        };
        var r = CalculadoraFgts.CalcularDevido(dto, series);
        Assert.Equal(1186.67m, r.BaseMulta);
        Assert.Equal(374.67m, r.Multa);
        Assert.Equal(1.0201m, r.Linhas[0].FatorCorrecao); // jan exigível em fev: fev e mar
        Assert.Equal(163.22m, r.Linhas[0].Total);
        Assert.True(r.MultaAtualizada > r.Multa);
        Assert.Contains(r.Avisos, a => a.StartsWith("TR"));

        var pdf = DemonstrativoPdfRenderer.Renderizar(DemonstrativosTrabalhistas.FgtsDevido(dto, r), "Escritório", null, new DateTime(2024, 4, 1));
        Assert.StartsWith("%PDF", Encoding.ASCII.GetString(pdf[..4]));
    }

    [Fact]
    public void Devido_PrescricaoQuinquenal()
    {
        var dto = new CalcularFgtsDevidoDto(new DateOnly(2024, 7, 1), [new(2015, 1, 1000m), new(2023, 1, 1000m)],
            DataAjuizamento: new DateOnly(2024, 6, 1), LimitarPrescricao: true, Atualizacao: CriterioAtualizacao.Nenhum, MultaPercentual: 0);
        var r = CalculadoraFgts.CalcularDevido(dto, SeriesMensais.Vazia);
        Assert.True(r.Linhas[0].Prescrita);
        Assert.Equal(0m, r.Linhas[0].Total);
        Assert.Equal(80m, r.Diferencas);
        Assert.Contains(r.Avisos, a => a.Contains("prescrição quinquenal"));
    }

    [Fact]
    public void ValidarDevido()
    {
        Assert.Null(CalculadoraFgts.ValidarDevido(Devido()));
        Assert.Contains("ao menos", CalculadoraFgts.ValidarDevido(Devido() with { Competencias = [] }));
        Assert.Contains("posterior", CalculadoraFgts.ValidarDevido(Devido() with { Competencias = [new(2024, 5, 1m)] }));
        Assert.Contains("ajuizamento", CalculadoraFgts.ValidarDevido(Devido() with { DataAjuizamento = new DateOnly(2025, 1, 1) }));
    }

    [Fact]
    public void Revisao_RefazSaldoComTrEComIndice()
    {
        var series = SeriesTeste.Criar(new()
        {
            [TipoIndice.TR] = new() { [(1999, 1)] = 0.01m, [(1999, 2)] = 0.01m },
            [TipoIndice.INPC] = new() { [(1999, 1)] = 0.02m, [(1999, 2)] = 0.02m, [(1999, 3)] = 0.01m },
        });
        var dto = new CalcularRevisaoFgtsDto(new DateOnly(1999, 4, 15), [new(1999, 1, 100m), new(1999, 2)],
            SaldoInicial: 1000m, JurosAnuais: 0, Titular: "Fulano");

        var r = CalculadoraFgts.CalcularRevisao(dto, series);

        Assert.Equal(1121.10m, r.SaldoTr);
        Assert.Equal(1142.40m, r.SaldoRevisado);
        Assert.Equal(21.30m, r.DiferencaNoPeriodo);
        Assert.Equal(1.01m, r.FatorAteCalculo);
        Assert.Equal(21.51m, r.DiferencaAtualizada);
        Assert.Equal(2, r.Linhas.Count);
        Assert.Contains("ADI 5090", r.Avisos[0]);
        Assert.Equal("INPC (IBGE)", r.Indice);

        var pdf = DemonstrativoPdfRenderer.Renderizar(DemonstrativosTrabalhistas.RevisaoFgts(dto, r), "Escritório", null, new DateTime(1999, 4, 15));
        Assert.StartsWith("%PDF", Encoding.ASCII.GetString(pdf[..4]));
    }

    [Fact]
    public void Revisao_JurosDaContaEIndicesFaltantes()
    {
        var dto = new CalcularRevisaoFgtsDto(new DateOnly(1999, 2, 1), [new(1999, 1)], IndiceRevisaoFgts.IPCAE, SaldoInicial: 1000m);
        var r = CalculadoraFgts.CalcularRevisao(dto, SeriesMensais.Vazia);
        Assert.Equal(1002.47m, r.SaldoTr); // só os juros de 3% a.a. (0,2466% a.m.)
        Assert.Equal(0m, r.DiferencaNoPeriodo);
        Assert.Contains(r.Avisos, a => a.StartsWith("TR sem dado"));
        Assert.Contains(r.Avisos, a => a.StartsWith("IPCA-E"));
    }

    [Theory]
    [InlineData(IndiceRevisaoFgts.INPC, TipoIndice.INPC)]
    [InlineData(IndiceRevisaoFgts.IPCAE, TipoIndice.TJSP)]
    [InlineData(IndiceRevisaoFgts.IPCA, TipoIndice.IPCA)]
    public void Revisao_Indices(IndiceRevisaoFgts indice, TipoIndice tipo) => Assert.Equal(tipo, CalculadoraFgts.TipoDoIndice(indice));

    [Fact]
    public void ValidarRevisao()
    {
        var ok = new CalcularRevisaoFgtsDto(new DateOnly(2000, 1, 1), [new(1999, 12)]);
        Assert.Null(CalculadoraFgts.ValidarRevisao(ok));
        Assert.Contains("ao menos", CalculadoraFgts.ValidarRevisao(ok with { Movimentos = [] }));
        Assert.Contains("anteriores", CalculadoraFgts.ValidarRevisao(ok with { Movimentos = [new(2000, 1)] }));
    }
}

public class CalculadorasTrabalhistasControllerTests
{
    private static AppDbContext Db()
    {
        var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        foreach (var (tipo, ano, mes, valor) in new[] { (TipoIndice.TJSP, 2024, 2, 0.01m), (TipoIndice.TR, 2024, 2, 0.001m), (TipoIndice.INPC, 1999, 1, 0.02m) })
            db.IndicesCorrecaoMonetaria.Add(new IndiceCorrecaoMonetaria { Id = Guid.NewGuid(), Tipo = tipo, Ano = ano, Mes = mes, Valor = valor, Fonte = "BCB" });
        db.SalariosMinimos.Add(new SalarioMinimo { Id = Guid.NewGuid(), Ano = 2024, Mes = 1, Valor = 1412m, Fonte = "BCB" });
        db.SaveChanges();
        return db;
    }

    private static Mock<ITenantContext> Tenant(PlanoTipo plano, Guid? id = null)
    {
        var t = new Mock<ITenantContext>();
        t.Setup(x => x.TenantId).Returns(id ?? Guid.NewGuid());
        t.Setup(x => x.Plano).Returns(plano);
        return t;
    }

    private static IWebHostEnvironment Env() => new Mock<IWebHostEnvironment>().Object;

    private static readonly CalcularTrabalhistaDto Trab = new(new DateOnly(2024, 1, 1), new DateOnly(2024, 1, 31),
        TipoRescisao.SemJustaCausa, 2200m, new DateOnly(2024, 3, 1), InsalubridadeGrau: 20);
    private static readonly CalcularFgtsDevidoDto Fgts = new(new DateOnly(2024, 4, 1), [new(2024, 1, 2000m)]);
    private static readonly CalcularRevisaoFgtsDto Rev = new(new DateOnly(1999, 3, 1), [new(1999, 1, 100m)]);
    private static readonly GerarCompetenciasFgtsDto Gerar = new(new DateOnly(2024, 1, 1), new DateOnly(2024, 3, 31), 1000m);

    [Fact]
    public async Task PlanoFree_402()
    {
        var db = Db();
        var f = new FgtsController(db, Tenant(PlanoTipo.Free).Object);
        var t = new TrabalhistaController(db, Tenant(PlanoTipo.Free).Object);
        Assert.Equal(402, Assert.IsType<ObjectResult>(f.Competencias(Gerar)).StatusCode);
        Assert.Equal(402, Assert.IsType<ObjectResult>(await f.Devido(Fgts, default)).StatusCode);
        Assert.Equal(402, Assert.IsType<ObjectResult>(await f.DevidoPdf(Fgts, Env(), default)).StatusCode);
        Assert.Equal(402, Assert.IsType<ObjectResult>(await f.Revisao(Rev, default)).StatusCode);
        Assert.Equal(402, Assert.IsType<ObjectResult>(await f.RevisaoPdf(Rev, Env(), default)).StatusCode);
        Assert.Equal(402, Assert.IsType<ObjectResult>(await t.GetIndices(default)).StatusCode);
        Assert.Equal(402, Assert.IsType<ObjectResult>(await t.Calcular(Trab, default)).StatusCode);
        Assert.Equal(402, Assert.IsType<ObjectResult>(await t.Pdf(Trab, Env(), default)).StatusCode);
    }

    [Fact]
    public async Task Fgts_PlanoPago()
    {
        var db = Db();
        var tenantId = Guid.NewGuid();
        db.Tenants.Add(new Tenant { Id = tenantId, Nome = "Escritório" });
        db.SaveChanges();
        var f = new FgtsController(db, Tenant(PlanoTipo.Plus, tenantId).Object);

        var lista = Assert.IsType<List<CompetenciaFgtsDto>>(Assert.IsType<OkObjectResult>(f.Competencias(Gerar)).Value);
        Assert.Equal(4, lista.Count); // jan–mar + 13º
        Assert.IsType<BadRequestObjectResult>(f.Competencias(Gerar with { Saida = new DateOnly(2023, 1, 1) }));
        Assert.IsType<BadRequestObjectResult>(f.Competencias(Gerar with { Saida = new DateOnly(2090, 1, 1), Admissao = new DateOnly(2000, 1, 1) }));

        var devido = Assert.IsType<ResultadoFgtsDevidoDto>(Assert.IsType<OkObjectResult>(await f.Devido(Fgts, default)).Value);
        Assert.Equal(1.01m, devido.Linhas[0].FatorCorrecao);
        Assert.IsType<BadRequestObjectResult>(await f.Devido(Fgts with { Competencias = [] }, default));
        Assert.IsType<BadRequestObjectResult>(await f.DevidoPdf(Fgts with { Competencias = [] }, Env(), default));
        Assert.Equal("fgts-devido-20240401.pdf", Assert.IsType<FileContentResult>(await f.DevidoPdf(Fgts, Env(), default)).FileDownloadName);

        var rev = Assert.IsType<ResultadoRevisaoFgtsDto>(Assert.IsType<OkObjectResult>(await f.Revisao(Rev, default)).Value);
        Assert.Equal(2m, rev.Linhas[0].Indice); // em %
        Assert.IsType<BadRequestObjectResult>(await f.Revisao(Rev with { Movimentos = [] }, default));
        Assert.IsType<BadRequestObjectResult>(await f.RevisaoPdf(Rev with { Movimentos = [] }, Env(), default));
        Assert.IsType<FileContentResult>(await f.RevisaoPdf(Rev, Env(), default));
    }

    [Fact]
    public async Task Trabalhista_PlanoPago()
    {
        var db = Db();
        var t = new TrabalhistaController(db, Tenant(PlanoTipo.Pro).Object);

        var indices = Assert.IsType<IndicesTrabalhistasDto>(Assert.IsType<OkObjectResult>(await t.GetIndices(default)).Value);
        Assert.Equal((2024, 2), (indices.Indices.Single(i => i.Indice == "IPCAE").Ano, indices.Indices.Single(i => i.Indice == "IPCAE").Mes));
        Assert.Null(indices.Indices.Single(i => i.Indice == "SELIC").Ano);
        Assert.Equal(1412m, Assert.Single(indices.SalariosMinimos).Valor);

        var r = Assert.IsType<ResultadoTrabalhistaDto>(Assert.IsType<OkObjectResult>(await t.Calcular(Trab, default)).Value);
        Assert.Equal(282.40m, r.Meses[0].Insalubridade);
        Assert.IsType<BadRequestObjectResult>(await t.Calcular(Trab with { InsalubridadeGrau = 5 }, default));
        Assert.IsType<BadRequestObjectResult>(await t.Pdf(Trab with { InsalubridadeGrau = 5 }, Env(), default));
        Assert.Equal("calculo-trabalhista-20240301.pdf", Assert.IsType<FileContentResult>(await t.Pdf(Trab, Env(), default)).FileDownloadName);
    }
}
