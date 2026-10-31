using LegalManager.Domain.Enums;
using LegalManager.Infrastructure.Services;

namespace LegalManager.UnitTests;

public class SituacaoProcessoFonteTests
{
    private static MovimentoSituacao Mov(string data, int? codigo, string? descricao = null) =>
        new(DateTime.Parse(data), codigo, descricao);

    [Fact]
    public void PorMovimentos_ArquivamentoDefinitivo_Arquivado()
    {
        var situacao = SituacaoProcessoFonte.PorMovimentos(
            [Mov("2020-01-10", 26, "Distribuído"), Mov("2021-03-12", 246, "Definitivo")], "G1");

        Assert.Equal(StatusProcesso.Arquivado, situacao!.Status);
        Assert.Equal("Arquivamento definitivo em 12/03/2021", situacao.Motivo);
    }

    [Fact]
    public void PorMovimentos_DesarquivadoDepois_SegueAtivo()
    {
        var situacao = SituacaoProcessoFonte.PorMovimentos(
            [Mov("2022-05-01", 893), Mov("2021-03-12", 246)], "G1");

        Assert.Null(situacao);
    }

    [Fact]
    public void PorMovimentos_ArquivadoDeNovoAposReativacao_UsaOUltimo()
    {
        var situacao = SituacaoProcessoFonte.PorMovimentos(
            [Mov("2021-03-12", 246), Mov("2022-05-01", 849), Mov("2023-07-20", 245)], "G1");

        Assert.Equal(StatusProcesso.Suspenso, situacao!.Status);
        Assert.Equal("Arquivamento provisório em 20/07/2023", situacao.Motivo);
    }

    [Theory]
    [InlineData("G1", true)]
    [InlineData("JE", true)]
    [InlineData("G2", false)]
    [InlineData("SUP", false)]
    [InlineData(null, false)]
    public void PorMovimentos_BaixaDefinitiva_SoContaNaPrimeiraInstancia(string? grau, bool arquiva)
    {
        // No 2º grau a baixa é a devolução dos autos à origem: o processo continua lá.
        var situacao = SituacaoProcessoFonte.PorMovimentos([Mov("2021-03-12", 22, "Baixa Definitiva")], grau);

        Assert.Equal(arquiva, situacao?.Status == StatusProcesso.Arquivado);
    }

    [Theory]
    [InlineData("Arquivado Definitivamente", StatusProcesso.Arquivado)]
    [InlineData("Arquivado Provisoriamente — aguardando", StatusProcesso.Suspenso)]
    [InlineData("ARQUIVAMENTO PROVISÓRIO", StatusProcesso.Suspenso)]
    [InlineData("Baixa Definitiva", StatusProcesso.Arquivado)]
    public void PorMovimentos_SemCodigo_LeOTexto(string descricao, StatusProcesso esperado)
    {
        var situacao = SituacaoProcessoFonte.PorMovimentos([Mov("2021-03-12", null, descricao)], "G1");

        Assert.Equal(esperado, situacao!.Status);
    }

    [Fact]
    public void PorMovimentos_SemCodigo_DesarquivamentoReabre()
    {
        var situacao = SituacaoProcessoFonte.PorMovimentos(
            [Mov("2021-03-12", null, "Arquivado Definitivamente"), Mov("2022-01-03", null, "Desarquivamento")], "G1");

        Assert.Null(situacao);
    }

    [Fact]
    public void PorMovimentos_OutrosMovimentos_SemIndicio()
    {
        Assert.Null(SituacaoProcessoFonte.PorMovimentos(
            [Mov("2021-03-12", 848, "Trânsito em julgado"), Mov("2021-04-01", null, "Juntada de petição")], "G1"));
        Assert.Null(SituacaoProcessoFonte.PorMovimentos([], "G1"));
    }

    // Valores reais de fontes[].capa.situacao no Escavador (amostra de produção, out/2026).
    [Theory]
    [InlineData("Arquivado definitivamente", StatusProcesso.Arquivado)]
    [InlineData("Arquivado", StatusProcesso.Arquivado)]
    [InlineData("Processo arquivado", StatusProcesso.Arquivado)]
    [InlineData("Baixado definitivamente", StatusProcesso.Arquivado)]
    [InlineData("Baixado", StatusProcesso.Arquivado)]
    [InlineData("Extinto", StatusProcesso.Arquivado)]
    [InlineData("Encerrado", StatusProcesso.Arquivado)]
    [InlineData("Cancelado", StatusProcesso.Arquivado)]
    [InlineData("Distribuição cancelada", StatusProcesso.Arquivado)]
    [InlineData("Arquivado provisoriamente", StatusProcesso.Suspenso)]
    [InlineData("Arquivado administrativamente", StatusProcesso.Suspenso)]
    [InlineData("Suspenso", StatusProcesso.Suspenso)]
    [InlineData("Suspensão", StatusProcesso.Suspenso)]
    [InlineData("Suspenso/sobrestado por decisão judicial", StatusProcesso.Suspenso)]
    [InlineData("Suspenso / sobrestado", StatusProcesso.Suspenso)]
    [InlineData("Susp/sobr-aguarda dec.inst.sup", StatusProcesso.Suspenso)]
    [InlineData("Susp/sobr-aguarda pagamento", StatusProcesso.Suspenso)]
    [InlineData("Supenso/sobrestado por sirdr", StatusProcesso.Suspenso)]
    public void PorSituacaoTexto_MapeiaEtiqueta(string situacao, StatusProcesso esperado)
    {
        var resultado = SituacaoProcessoFonte.PorSituacaoTexto(situacao, primeiraInstancia: true);

        Assert.Equal(esperado, resultado!.Status);
        Assert.Equal($"Situação no tribunal: {situacao}", resultado.Motivo);
    }

    [Theory]
    [InlineData("Tramitando")]
    [InlineData("Movimento")]
    [InlineData("Movimento-aguarda sentença")]
    [InlineData("Em andamento")]
    [InlineData("Ativo")]
    [InlineData("Em grau de recurso")]
    [InlineData("Remetido")]
    [InlineData("Remetido a outro foro")]
    [InlineData("Remetido para outra instância")]
    [InlineData("Encaminhado a outro tribunal")]
    [InlineData("Pendente de julgamento")]
    [InlineData("Não informado")]
    [InlineData("Baixado - requisição pagamento")] // aguarda RPV/precatório
    [InlineData("Julgado")] // ainda cabe recurso ou cumprimento de sentença
    [InlineData("Julgado transitado")]
    [InlineData("")]
    [InlineData(null)]
    public void PorSituacaoTexto_SemIndicio(string? situacao) =>
        Assert.Null(SituacaoProcessoFonte.PorSituacaoTexto(situacao, primeiraInstancia: true));

    [Theory]
    [InlineData("Baixado")]
    [InlineData("Baixado definitivamente")]
    public void PorSituacaoTexto_BaixadoForaDaPrimeiraInstancia_VoltouAOrigem(string situacao) =>
        Assert.Null(SituacaoProcessoFonte.PorSituacaoTexto(situacao, primeiraInstancia: false));

    [Fact]
    public void PorEscavador_TodasAsFontesArquivadas_ArquivadoComData()
    {
        const string json = """
            {"fontes_tribunais_estao_arquivadas": true,
             "fontes": [{"tipo": "TRIBUNAL", "arquivado": true, "capa": {"data_arquivamento": "2022-08-15"}}]}
            """;

        var situacao = SituacaoProcessoFonte.PorEscavador(json);

        Assert.Equal(StatusProcesso.Arquivado, situacao!.Status);
        Assert.Equal("Arquivado no tribunal em 15/08/2022", situacao.Motivo);
    }

    [Fact]
    public void PorEscavador_ArquivadoEmCadaFonteTribunal_IgnoraDiario()
    {
        const string json = """
            {"fontes": [{"tipo": "TRIBUNAL", "arquivado": true}, {"tipo": "DIARIO_OFICIAL", "arquivado": false}]}
            """;

        Assert.Equal(StatusProcesso.Arquivado, SituacaoProcessoFonte.PorEscavador(json)!.Status);
    }

    [Fact]
    public void PorEscavador_FlagDaRaizComUmaFonteArquivada_Arquivado()
    {
        // 1º grau arquivado; a fonte do 2º grau não traz a flag, mas o Escavador diz que todas estão.
        const string json = """
            {"fontes_tribunais_estao_arquivadas": true,
             "fontes": [{"tipo": "TRIBUNAL", "grau": 1, "arquivado": true}, {"tipo": "TRIBUNAL", "grau": 2, "arquivado": null}]}
            """;

        Assert.Equal(StatusProcesso.Arquivado, SituacaoProcessoFonte.PorEscavador(json)!.Status);
    }

    [Fact]
    public void PorEscavador_SoAFlagDaRaiz_SemIndicio()
    {
        // Em produção, a flag sozinha acompanha o status_predito (previsão) e pega processos vivos.
        const string json = """
            {"fontes_tribunais_estao_arquivadas": true,
             "fontes": [{"tipo": "TRIBUNAL", "grau": 1, "arquivado": null, "status_predito": "INATIVO", "capa": {"situacao": null}}]}
            """;

        Assert.Null(SituacaoProcessoFonte.PorEscavador(json));
    }

    [Fact]
    public void PorEscavador_SituacaoDaCapa()
    {
        const string json = """{"fontes": [{"tipo": "TRIBUNAL", "grau": 1, "arquivado": null, "capa": {"situacao": "Suspenso"}}]}""";

        Assert.Equal(StatusProcesso.Suspenso, SituacaoProcessoFonte.PorEscavador(json)!.Status);
    }

    [Fact]
    public void PorEscavador_ArquivadoProvisoriamente_SuspensoMesmoComFlagDeArquivado()
    {
        const string json = """
            {"fontes_tribunais_estao_arquivadas": true,
             "fontes": [{"tipo": "TRIBUNAL", "grau": 1, "arquivado": true, "capa": {"situacao": "Arquivado provisoriamente"}}]}
            """;

        var situacao = SituacaoProcessoFonte.PorEscavador(json);

        Assert.Equal(StatusProcesso.Suspenso, situacao!.Status);
        Assert.Equal("Situação no tribunal: Arquivado provisoriamente", situacao.Motivo);
    }

    [Fact]
    public void PorEscavador_BaixadoNoSegundoGrau_SemIndicio()
    {
        const string json = """
            {"fontes_tribunais_estao_arquivadas": false,
             "fontes": [{"tipo": "TRIBUNAL", "grau": 2, "arquivado": null, "capa": {"situacao": "Baixado"}},
                        {"tipo": "TRIBUNAL", "grau": 1, "arquivado": null, "capa": {"situacao": "Tramitando"}}]}
            """;

        Assert.Null(SituacaoProcessoFonte.PorEscavador(json));
    }

    [Fact]
    public void PorEscavador_FormatoReal_ArquivadoDefinitivamente()
    {
        // Recorte de um item real (TJPA, sem envolvidos), com a fonte DIARIO_OFICIAL ao lado.
        const string json = """
            {"numero_cnj": "0804516-56.2025.8.14.0012", "fontes_tribunais_estao_arquivadas": true,
             "fontes": [
               {"tipo": "TRIBUNAL", "grau": 1, "arquivado": true, "status_predito": "INATIVO",
                "capa": {"situacao": "Arquivado definitivamente", "data_arquivamento": "2026-05-20"}},
               {"tipo": "DIARIO_OFICIAL", "grau": 1, "arquivado": null, "capa": null}]}
            """;

        var situacao = SituacaoProcessoFonte.PorEscavador(json);

        Assert.Equal(StatusProcesso.Arquivado, situacao!.Status);
        Assert.Equal("Arquivado no tribunal em 20/05/2026", situacao.Motivo);
    }

    [Theory]
    [InlineData("""{"fontes": [{"tipo": "TRIBUNAL", "arquivado": null, "status_predito": "INATIVO"}]}""")]
    [InlineData("""{"fontes": [{"tipo": "TRIBUNAL", "arquivado": true}, {"tipo": "TRIBUNAL", "arquivado": null}]}""")]
    [InlineData("""{"fontes_tribunais_estao_arquivadas": false, "fontes": [{"tipo": "DIARIO_OFICIAL", "arquivado": null, "capa": null}]}""")]
    [InlineData("""{"unidade_origem": {"tribunal_sigla": "TRT3"}}""")]
    [InlineData("não é json")]
    [InlineData("[]")]
    [InlineData(null)]
    public void PorEscavador_SemIndicio(string? json) =>
        Assert.Null(SituacaoProcessoFonte.PorEscavador(json));

    // ── Conferência ─────────────────────────────────────────────────────────

    private static readonly DateTime Hoje = new(2026, 10, 31);

    [Fact]
    public void PorMovimentos_MovimentouDepoisDoArquivamento_PedeConferencia()
    {
        var situacao = SituacaoProcessoFonte.PorMovimentos(
            [Mov("2021-03-12", 246, "Definitivo"), Mov("2022-01-20", 60, "Juntada de petição")], "G1", hoje: Hoje);

        Assert.Equal(StatusProcesso.Arquivado, situacao!.Status);
        Assert.Equal("movimentou em 20/01/2022, depois do arquivamento", situacao.Conferir);
        Assert.Equal(StatusProcesso.Ativo, situacao.StatusAoImportar);
        Assert.Equal("Arquivamento definitivo em 12/03/2021. Mantido Ativo para conferir: movimentou em 20/01/2022, depois do arquivamento",
            situacao.MensagemImportacao);
    }

    [Fact]
    public void PorMovimentos_PublicacaoLogoAposOArquivamento_NaoPedeConferencia()
    {
        var situacao = SituacaoProcessoFonte.PorMovimentos(
            [Mov("2021-03-12", 246, "Definitivo"), Mov("2021-04-05", 92, "Publicação")], "G1", hoje: Hoje);

        Assert.Null(situacao!.Conferir);
        Assert.Equal(StatusProcesso.Arquivado, situacao.StatusAoImportar);
        Assert.Equal("Arquivamento definitivo em 12/03/2021", situacao.MensagemImportacao);
    }

    [Fact]
    public void PorMovimentos_UltimaMovimentacaoInformada_UsaElaEmVezDaLista()
    {
        // Na revisão só chegam os andamentos relevantes; a última movimentação vem à parte.
        var situacao = SituacaoProcessoFonte.PorMovimentos(
            [Mov("2021-03-12", 246, "Definitivo")], "G1", ultimaMovimentacao: new DateTime(2021, 6, 1), hoje: Hoje);

        Assert.Equal("movimentou em 01/06/2021, depois do arquivamento", situacao!.Conferir);
    }

    [Theory]
    [InlineData("2026-06-01", "movimentou em 01/06/2026, nos últimos 6 meses")] // arquivado há pouco
    [InlineData("2026-05-03", null)] // 181 dias antes de hoje
    public void ComConferencia_MovimentacaoRecente(string ultima, string? esperado)
    {
        var situacao = SituacaoProcessoFonte.ComConferencia(
            new SituacaoNaFonte(StatusProcesso.Arquivado, "Situação no tribunal: Arquivado"),
            arquivadoEm: null, ultimaMovimentacao: DateTime.Parse(ultima), hoje: Hoje);

        Assert.Equal(esperado, situacao.Conferir);
    }

    [Fact]
    public void ComConferencia_SemDataDeMovimentacao_AplicaDireto() =>
        Assert.Null(SituacaoProcessoFonte.ComConferencia(
            new SituacaoNaFonte(StatusProcesso.Arquivado, "x"), new DateTime(2021, 3, 12), null, Hoje).Conferir);

    [Fact]
    public void PorEscavador_MovimentouDepoisDoArquivamento_PedeConferencia()
    {
        const string json = """
            {"fontes_tribunais_estao_arquivadas": true,
             "fontes": [{"tipo": "TRIBUNAL", "grau": 1, "arquivado": true, "data_ultima_movimentacao": "2024-02-10",
                         "capa": {"situacao": "Arquivado definitivamente", "data_arquivamento": "2023-05-20"}},
                        {"tipo": "DIARIO_OFICIAL", "arquivado": null, "data_ultima_movimentacao": "2026-10-01", "capa": null}]}
            """;

        var situacao = SituacaoProcessoFonte.PorEscavador(json, Hoje);

        Assert.Equal(StatusProcesso.Arquivado, situacao!.Status);
        // A movimentação do Diário Oficial não conta: só as fontes de tribunal.
        Assert.Equal("movimentou em 10/02/2024, depois do arquivamento", situacao.Conferir);
    }

    [Fact]
    public void PorEscavador_ParadoDesdeOArquivamento_AplicaDireto()
    {
        const string json = """
            {"fontes": [{"tipo": "TRIBUNAL", "grau": 1, "arquivado": true, "data_ultima_movimentacao": "2023-05-25",
                         "capa": {"data_arquivamento": "2023-05-20"}}]}
            """;

        var situacao = SituacaoProcessoFonte.PorEscavador(json, Hoje);

        Assert.Null(situacao!.Conferir);
        Assert.Equal("Arquivado no tribunal em 20/05/2023", situacao.Motivo);
    }
}
