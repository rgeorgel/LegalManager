using LegalManager.Domain;
using LegalManager.Domain.Enums;
﻿using LegalManager.Application.Interfaces;
using LegalManager.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Resend;

namespace LegalManager.UnitTests;

public class EmailCapture
{
    public EmailMessage? Message { get; set; }
}

public class EmailServiceTests
{
    private readonly IConfiguration _config;

    public EmailServiceTests()
    {
        _config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Resend:FromName"] = "LegalManager",
                ["Resend:FromEmail"] = "noreply@test.com.br",
                ["Resend:ApiToken"] = "test-token",
                ["App:FrontendUrl"] = "http://localhost:5000"
            })
            .Build();
    }

    private static Mock<IResend> CreateMockWithEmailCapture(EmailCapture capture)
    {
        var mock = new Mock<IResend>(MockBehavior.Loose);
        mock.Setup(r => r.EmailSendAsync(It.IsAny<EmailMessage>(), It.IsAny<CancellationToken>()))
            .Callback<EmailMessage, CancellationToken>((m, _) => capture.Message = m)
            .Returns(Task.FromResult<ResendResponse<Guid>>(null!));
        return mock;
    }

    [Fact]
    public async Task EnviarBoasVindasAsync_ChamaEmailSendAsync_ComAssuntoCorreto()
    {
        var capture = new EmailCapture();
        var mock = CreateMockWithEmailCapture(capture);
        var svc = new EmailService(mock.Object, _config, NullLogger<EmailService>.Instance);

        await svc.EnviarBoasVindasAsync("cliente@test.com", "Admin Teste", "Escritório Teste", "Free", null, CancellationToken.None);

        Assert.NotNull(capture.Message);
        Assert.Contains("Bem-vindo ao Causify", capture.Message!.Subject);
        Assert.Contains("cliente@test.com", capture.Message.To);
    }

    [Fact]
    public async Task EnviarBoasVindasAsync_ContemNomeEscritorio_NoHtml()
    {
        var capture = new EmailCapture();
        var mock = CreateMockWithEmailCapture(capture);
        var svc = new EmailService(mock.Object, _config, NullLogger<EmailService>.Instance);

        await svc.EnviarBoasVindasAsync("cliente@test.com", "Admin Teste", "Meu Escritório", "Free", null, CancellationToken.None);

        Assert.NotNull(capture.Message);
        Assert.Contains("Meu Escrit", capture.Message!.HtmlBody);
    }

    [Fact]
    public async Task EnviarConviteUsuarioAsync_ChamaEmailSendAsync_ComLinkNoHtml()
    {
        var capture = new EmailCapture();
        var mock = CreateMockWithEmailCapture(capture);
        var svc = new EmailService(mock.Object, _config, NullLogger<EmailService>.Instance);

        await svc.EnviarConviteUsuarioAsync(
            "usuario@test.com", "Escritorio XYZ", "http://localhost/convite/abc123", CancellationToken.None);

        Assert.NotNull(capture.Message);
        Assert.Contains("http://localhost/convite/abc123", capture.Message!.HtmlBody);
        Assert.Contains("Escritorio XYZ", capture.Message.HtmlBody);
    }

    [Fact]
    public async Task EnviarResetSenhaAsync_ContemLinkDeReset_NoHtml()
    {
        var capture = new EmailCapture();
        var mock = CreateMockWithEmailCapture(capture);
        var svc = new EmailService(mock.Object, _config, NullLogger<EmailService>.Instance);

        await svc.EnviarResetSenhaAsync("user@test.com", "http://localhost/reset/xyz789", CancellationToken.None);

        Assert.NotNull(capture.Message);
        Assert.Contains("http://localhost/reset/xyz789", capture.Message!.HtmlBody);
        Assert.Contains("Redefinição de senha", capture.Message.Subject);
    }

    [Fact]
    public async Task EnviarTrialExpirandoAsync_ContemDiasRestantes_NoHtml()
    {
        var capture = new EmailCapture();
        var mock = CreateMockWithEmailCapture(capture);
        var svc = new EmailService(mock.Object, _config, NullLogger<EmailService>.Instance);

        await svc.EnviarTrialExpirandoAsync("admin@test.com", "Escritório Teste", 5, CancellationToken.None);

        Assert.NotNull(capture.Message);
        Assert.Contains("5 dia(s)", capture.Message!.HtmlBody);
    }

    [Fact]
    public async Task EnviarTrialExpirandoAsync_TabelaUsaLimitesReaisDosPlanos()
    {
        var capture = new EmailCapture();
        var mock = CreateMockWithEmailCapture(capture);
        var svc = new EmailService(mock.Object, _config, NullLogger<EmailService>.Instance);

        await svc.EnviarTrialExpirandoAsync("admin@test.com", "Escritório Teste", 3, CancellationToken.None);

        var html = capture.Message!.HtmlBody;
        var free = PlanoRestricoes.MaxProcessosMonitorados(PlanoTipo.Free);
        var plus = PlanoRestricoes.MaxProcessosMonitorados(PlanoTipo.Plus);
        Assert.Matches($@"Processos monitorados</td>\s*<td[^>]*>{free}</td>\s*<td[^>]*>{plus}</td>", html);
    }

    [Fact]
    public async Task EnviarPlanoPromocionalExpirandoAsync_ComparaFreeComPlano()
    {
        var capture = new EmailCapture();
        var mock = CreateMockWithEmailCapture(capture);
        var svc = new EmailService(mock.Object, _config, NullLogger<EmailService>.Instance);

        var expiraEm = new DateTime(2026, 11, 11, 17, 10, 0, DateTimeKind.Utc);
        await svc.EnviarPlanoPromocionalExpirandoAsync("admin@test.com", "Escritório Teste", PlanoTipo.Pro, expiraEm, 7, CancellationToken.None);

        Assert.NotNull(capture.Message);
        var html = capture.Message!.HtmlBody;
        Assert.Contains("plano Pro termina em 7 dia(s)", capture.Message.Subject);
        Assert.Contains("7 dia(s)", html);
        Assert.Contains("11/11/2026", html);
        Assert.Contains("Manter plano Pro", html);
        Assert.Contains("Processos monitorados", html);
        Assert.Contains("Templates de documentos", html); // só Pro+ tem
        Assert.Contains("5 GB", html);
    }

    [Fact]
    public async Task EnviarPlanoPromocionalExpirandoAsync_OmiteLinhasIguaisAoFree()
    {
        var capture = new EmailCapture();
        var mock = CreateMockWithEmailCapture(capture);
        var svc = new EmailService(mock.Object, _config, NullLogger<EmailService>.Instance);

        await svc.EnviarPlanoPromocionalExpirandoAsync("admin@test.com", "Escritório Teste", PlanoTipo.Plus,
            DateTime.UtcNow.AddDays(3), 3, CancellationToken.None);

        var html = capture.Message!.HtmlBody;
        Assert.DoesNotContain("Usuários", html);               // 1 × 1
        Assert.DoesNotContain("Templates de documentos", html); // ✕ × ✕
        Assert.Contains("Portal do Cliente", html);
    }

    [Fact]
    public async Task EnviarAlertaPrazoTarefaAsync_ExibeUrgenciaZero_Hoje()
    {
        var capture = new EmailCapture();
        var mock = CreateMockWithEmailCapture(capture);
        var svc = new EmailService(mock.Object, _config, NullLogger<EmailService>.Instance);

        var prazo = DateTime.UtcNow.AddHours(5);
        await svc.EnviarAlertaPrazoTarefaAsync(
            "user@test.com", "João Silva", "Petição inicial", prazo, 0, CancellationToken.None);

        Assert.NotNull(capture.Message);
        Assert.Contains("HOJE", capture.Message!.HtmlBody);
        Assert.Contains("Petição inicial", capture.Message.HtmlBody);
    }

    [Fact]
    public async Task EnviarAlertaPrazoTarefaAsync_ExibeDiasRestantes_QuandoMaiorQueZero()
    {
        var capture = new EmailCapture();
        var mock = CreateMockWithEmailCapture(capture);
        var svc = new EmailService(mock.Object, _config, NullLogger<EmailService>.Instance);

        var prazo = DateTime.UtcNow.AddDays(3);
        await svc.EnviarAlertaPrazoTarefaAsync(
            "user@test.com", "João Silva", "Recurso", prazo, 3, CancellationToken.None);

        Assert.NotNull(capture.Message);
        Assert.Contains("em 3 dia(s)", capture.Message!.HtmlBody);
    }

    [Fact]
    public async Task EnviarAlertaEventoAsync_ExibeDataETempo_QuandoLocalNull()
    {
        var capture = new EmailCapture();
        var mock = CreateMockWithEmailCapture(capture);
        var svc = new EmailService(mock.Object, _config, NullLogger<EmailService>.Instance);

        var dataHora = DateTime.UtcNow.AddDays(1).AddHours(14);
        await svc.EnviarAlertaEventoAsync(
            "user@test.com", "Maria Silva", "Audiência de conciliação", dataHora, null, CancellationToken.None);

        Assert.NotNull(capture.Message);
        Assert.Contains("Audiência de conciliação", capture.Message!.HtmlBody);
    }

    [Fact]
    public async Task EnviarAlertaEventoAsync_ExibeLocal_QuandoNaoNulo()
    {
        var capture = new EmailCapture();
        var mock = CreateMockWithEmailCapture(capture);
        var svc = new EmailService(mock.Object, _config, NullLogger<EmailService>.Instance);

        var dataHora = DateTime.UtcNow.AddDays(1).AddHours(9);
        await svc.EnviarAlertaEventoAsync(
            "user@test.com", "João Silva", "Reunião", dataHora, "Sala 302 - Foro Central", CancellationToken.None);

        Assert.NotNull(capture.Message);
        Assert.Contains("Sala 302 - Foro Central", capture.Message!.HtmlBody);
    }

    [Fact]
    public async Task EnviarNovoAndamentoAsync_ContemNumeroCNJ_EDescricao()
    {
        var capture = new EmailCapture();
        var mock = CreateMockWithEmailCapture(capture);
        var svc = new EmailService(mock.Object, _config, NullLogger<EmailService>.Instance);

        await svc.EnviarNovoAndamentoAsync(
            "adv@test.com", "Dr. Joao", "1234567-89.2024.8.26.0001",
            "Audiencia redesignada para 15/05/2026 as 14h", Guid.NewGuid(), 1, CancellationToken.None);

        Assert.NotNull(capture.Message);
        Assert.Contains("1234567-89.2024.8.26.0001", capture.Message!.HtmlBody);
        Assert.Contains("Audiencia redesignada", capture.Message.HtmlBody);
    }

    [Fact]
    public async Task EnviarAlertaPrazoProcessualAsync_ContemDescricao_EDataFinal()
    {
        var capture = new EmailCapture();
        var mock = CreateMockWithEmailCapture(capture);
        var svc = new EmailService(mock.Object, _config, NullLogger<EmailService>.Instance);

        var dataFinal = new DateTime(2026, 5, 15);
        await svc.EnviarAlertaPrazoProcessualAsync(
            "adv@test.com", "Dr. João", "1234567-89.2024.8.26.0001",
            "Prazo para recurso", dataFinal, 3, CancellationToken.None);

        Assert.NotNull(capture.Message);
        Assert.Contains("Prazo para recurso", capture.Message!.HtmlBody);
        Assert.Contains("15/05/2026", capture.Message.HtmlBody);
        Assert.Contains("em 3 dia(s)", capture.Message.HtmlBody);
    }

    [Fact]
    public async Task EnviarNovaPublicacaoAsync_ContemNumeroCNJ_NoAssunto()
    {
        var capture = new EmailCapture();
        var mock = CreateMockWithEmailCapture(capture);
        var svc = new EmailService(mock.Object, _config, NullLogger<EmailService>.Instance);

        await svc.EnviarNovaPublicacaoAsync(
            "adv@test.com", "Dr. João", "9876543-11.2024.5.00.0001", CancellationToken.None);

        Assert.NotNull(capture.Message);
        Assert.Contains("9876543-11.2024.5.00.0001", capture.Message!.Subject);
    }

    [Fact]
    public async Task EnviarAcessoPortalAsync_ContemCredenciais_NoHtml()
    {
        var capture = new EmailCapture();
        var mock = CreateMockWithEmailCapture(capture);
        var svc = new EmailService(mock.Object, _config, NullLogger<EmailService>.Instance);

        await svc.EnviarAcessoPortalAsync(
            "cliente@test.com", "Maria Oliveira", "Escritorio XYZ",
            "Senha@123", "http://localhost/cliente", CancellationToken.None);

        Assert.NotNull(capture.Message);
        Assert.Contains("Maria Oliveira", capture.Message!.HtmlBody);
        Assert.Contains("Senha@123", capture.Message.HtmlBody);
        Assert.Contains("Escritorio XYZ", capture.Message.HtmlBody);
    }

    [Fact]
    public async Task EnviarAndamentoTraduzidoAsync_ContemAndamentoTraduzido_NoHtml()
    {
        var capture = new EmailCapture();
        var mock = CreateMockWithEmailCapture(capture);
        var svc = new EmailService(mock.Object, _config, NullLogger<EmailService>.Instance);

        await svc.EnviarAndamentoTraduzidoAsync(
            "cliente@test.com", "Carlos Souza", "1234567-89.2024.8.26.0001",
            "O tribunal aceitou o recurso e marcado nova audiência para junho.", CancellationToken.None);

        Assert.NotNull(capture.Message);
        Assert.Contains("Carlos Souza", capture.Message!.HtmlBody);
        Assert.Contains("O tribunal aceitou o recurso", capture.Message.HtmlBody);
    }

[Fact]
    public async Task EnviarResumoDiarioAsync_SoProximosDias_EhODiaSemNovidades()
    {
        var capture = new EmailCapture();
        var mock = CreateMockWithEmailCapture(capture);
        var svc = new EmailService(mock.Object, _config, NullLogger<EmailService>.Instance);

        await svc.EnviarResumoDiarioAsync(
            "user@test.com", "Vanessa",
            [new ResumoTarefaItem("Recurso Inominado", new DateTime(2026, 10, 16, 9, 0, 0), 1)],
            [new ResumoEventoItem("Audiencia Raz x Luciano", new DateTime(2026, 10, 16, 10, 35, 0), "Fórum", 1)],
            new ResumoDiarioExtras(new DateTime(2026, 10, 15), TemPublicacoes: true, Convite: ConviteResumo.Plus),
            CancellationToken.None);

        Assert.NotNull(capture.Message);
        Assert.Equal("Seu dia: nada novo desde ontem", capture.Message!.Subject);
        var html = capture.Message.HtmlBody;
        Assert.Contains("Nada novo desde o último resumo", html);
        Assert.Contains("Nenhuma publicação nova", html);
        Assert.Contains("Próximos 7 dias", html);
        Assert.Contains("Sex, 16/10", html);
        Assert.Contains("Recurso Inominado", html);
        Assert.Contains("10:35", html);
        Assert.Contains("/pages/tarefas.html", html);
        Assert.Contains("/pages/agenda.html", html);
        Assert.DoesNotContain("Conhecer o", html); // dia vazio não leva convite
    }

    [Fact]
    public async Task EnviarResumoDiarioAsync_SoEventos_OmiteTarefas()
    {
        var capture = new EmailCapture();
        var mock = CreateMockWithEmailCapture(capture);
        var svc = new EmailService(mock.Object, _config, NullLogger<EmailService>.Instance);

        await svc.EnviarResumoDiarioAsync(
            "user@test.com", "Vanessa", [],
            [new ResumoEventoItem("Reunião", new DateTime(2026, 10, 15, 15, 0, 0), null, 0),
             new ResumoEventoItem("Audiência", new DateTime(2026, 10, 16, 10, 0, 0), null, 1)],
            ct: CancellationToken.None);

        Assert.Equal("Seu dia: 1 compromisso hoje", capture.Message!.Subject);
        Assert.Contains("Atenção hoje", capture.Message.HtmlBody);
        Assert.DoesNotContain("/pages/tarefas.html", capture.Message.HtmlBody);
    }

    [Fact]
    public async Task EnviarResumoDiarioAsync_PlanoComPublicacoes_MostraSugestoesETextosDaIA()
    {
        var capture = new EmailCapture();
        var mock = CreateMockWithEmailCapture(capture);
        var svc = new EmailService(mock.Object, _config, NullLogger<EmailService>.Instance);

        await svc.EnviarResumoDiarioAsync(
            "user@test.com", "Ana",
            [new ResumoTarefaItem("Juntar procuração", new DateTime(2026, 10, 21, 12, 0, 0), -2)],
            [],
            new ResumoDiarioExtras(
                new DateTime(2026, 10, 23),
                VisaoGeral: "Dia com um ponto de atenção.",
                Sugestoes: [new ResumoSugestaoItem("Manifestar sobre o laudo", "1001885-87.2026.8.26.0077", "DJSP",
                    new DateTime(2026, 10, 21), new DateTime(2026, 11, 16), 15, true, null)],
                PublicacoesSemProvidencia: 3,
                Processos: [new ResumoProcessoItem("Mercado x Distribuidora", "5004321-11.2024.8.21.0001",
                    new DateTime(2026, 10, 22), "O juiz aceitou a penhora on-line.", "Acompanhar o bloqueio.", true)],
                TemPublicacoes: true),
            CancellationToken.None);

        Assert.Equal("Seu dia: 1 tarefa atrasada, 1 sugestão para confirmar e 1 processo com novidades", capture.Message!.Subject);
        // HtmlEncode troca acentos por entidades (&#231;): compara o texto como o leitor vê.
        var html = System.Net.WebUtility.HtmlDecode(capture.Message.HtmlBody);
        Assert.Contains("Dia com um ponto de atenção.", html);
        Assert.Contains("Publicações a tratar", html);
        Assert.Contains("15 dias úteis · vence <strong>16/11/2026</strong> (segunda-feira)", html);
        Assert.Contains("E mais 3 publicações novas sem providência a tomar", html);
        Assert.Contains("Próximo passo: Acompanhar o bloqueio.", html);
        Assert.Contains("podem conter erros", html);
        Assert.Contains("/pages/publicacoes.html", html);
        Assert.DoesNotContain("Nada novo", html);
    }

    [Fact]
    public async Task EnviarResumoDiarioAsync_PlanoFree_TextoDoTribunalEConvite()
    {
        var capture = new EmailCapture();
        var mock = CreateMockWithEmailCapture(capture);
        var svc = new EmailService(mock.Object, _config, NullLogger<EmailService>.Instance);

        await svc.EnviarResumoDiarioAsync(
            "user@test.com", "Ana", [], [],
            new ResumoDiarioExtras(
                new DateTime(2026, 10, 26),
                Processos: [new ResumoProcessoItem("Cobrança", "5004321-11.2024.8.21.0001",
                    new DateTime(2026, 10, 23), "Defiro o pedido de penhora <SISBAJUD>.", null, false)],
                Convite: ConviteResumo.Plus),
            CancellationToken.None);

        Assert.Equal("Seu dia: 1 processo com novidades", capture.Message!.Subject);
        var html = capture.Message.HtmlBody;
        Assert.Contains("Defiro o pedido de penhora &lt;SISBAJUD&gt;.", html);
        Assert.Contains("Conhecer o Plus", html);
        Assert.Contains("/pages/processos.html", html);
        Assert.DoesNotContain("podem conter erros", html);
        Assert.DoesNotContain("Próximo passo", html);
    }

[Fact]
    public async Task EnviarCobrancaAsync_SemQRCode_NaoIncluiImagem()
    {
        var capture = new EmailCapture();
        var mock = CreateMockWithEmailCapture(capture);
        var svc = new EmailService(mock.Object, _config, NullLogger<EmailService>.Instance);

        await svc.EnviarCobrancaAsync(
            "cliente@test.com", "Joao Silva", "Escritorio Legal",
            1500m, new DateTime(2026, 5, 15), null, null, CancellationToken.None);

        Assert.NotNull(capture.Message);
        var html = capture.Message.HtmlBody;
        Assert.Contains("15/05/2026", html);
        Assert.DoesNotContain("data:image/png;base64", html);
        Assert.True(html.Contains("1") && html.Contains("500"));
    }

    [Fact]
    public async Task EnviarCobrancaAsync_ComQRCodeBase64_IncluiImagem()
    {
        var capture = new EmailCapture();
        var mock = CreateMockWithEmailCapture(capture);
        var svc = new EmailService(mock.Object, _config, NullLogger<EmailService>.Instance);

        var qrBase64 = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==";
        await svc.EnviarCobrancaAsync(
            "cliente@test.com", "Joao Silva", "Escritorio Legal",
            2500m, new DateTime(2026, 6, 1), qrBase64, "00020101021253650014br.gov.bcb.pix0114teste520400005303986540425000.005802BR5925TESTE6009SAOPAULO6304E3F6",
            CancellationToken.None);

        Assert.NotNull(capture.Message);
        var html = capture.Message.HtmlBody;
        Assert.Contains("data:image/png;base64", html);
        Assert.Contains("01/06/2026", html);
        Assert.True(html.Contains("2500") || html.Contains("2"));
    }

    [Fact]
    public async Task EnviarCobrancaAsync_ContemAssuntoComVencimento()
    {
        var capture = new EmailCapture();
        var mock = CreateMockWithEmailCapture(capture);
        var svc = new EmailService(mock.Object, _config, NullLogger<EmailService>.Instance);

        await svc.EnviarCobrancaAsync(
            "cliente@test.com", "Cliente", "Escritorio",
            500m, new DateTime(2026, 12, 25), null, null, CancellationToken.None);

        Assert.NotNull(capture.Message);
        Assert.Contains("25/12/2026", capture.Message!.Subject);
        Assert.Contains("Cobrança de honorários", capture.Message.Subject);
    }

    [Fact]
    public async Task TodosMetodos_UsamFromCorretamente()
    {
        var capture = new EmailCapture();
        var mock = CreateMockWithEmailCapture(capture);
        var svc = new EmailService(mock.Object, _config, NullLogger<EmailService>.Instance);

        await svc.EnviarBoasVindasAsync("x@y.com", "Test", "Test", "Free", null, CancellationToken.None);

        Assert.NotNull(capture.Message);
        Assert.Equal("LegalManager <noreply@test.com.br>", capture.Message!.From);
    }

    [Fact]
    public async Task TodosMetodos_EscapaHtml_ContraXSS()
    {
        var capture = new EmailCapture();
        var mock = CreateMockWithEmailCapture(capture);
        var svc = new EmailService(mock.Object, _config, NullLogger<EmailService>.Instance);

        await svc.EnviarAndamentoTraduzidoAsync(
            "cliente@test.com", "<script>alert('xss')</script>",
            "1234567-89.2024.8.26.0001",
            "Descrição com <b>negrito</b> e & acentuação",
            CancellationToken.None);

        Assert.NotNull(capture.Message);
        Assert.DoesNotContain("<script>", capture.Message!.HtmlBody);
        Assert.Contains("&lt;script&gt;", capture.Message.HtmlBody);
        Assert.Contains("&lt;b&gt;", capture.Message.HtmlBody);
        Assert.Contains("&amp;", capture.Message.HtmlBody);
    }

    [Fact]
    public async Task EnviarCobrancaAsync_ComQRCodeBase64UsaImgTagCorreta()
    {
        var capture = new EmailCapture();
        var mock = CreateMockWithEmailCapture(capture);
        var svc = new EmailService(mock.Object, _config, NullLogger<EmailService>.Instance);

        var qrBase64 = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==";
        await svc.EnviarCobrancaAsync(
            "cliente@test.com", "João Silva", "Escritório Legal",
            1500m, new DateTime(2026, 5, 15), qrBase64, null, CancellationToken.None);

        Assert.NotNull(capture.Message);
        Assert.Contains("data:image/png;base64", capture.Message!.HtmlBody);
        Assert.Contains("width:180px", capture.Message.HtmlBody);
        Assert.Contains("height:180px", capture.Message.HtmlBody);
    }
}

internal class FakeResendResponse
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public bool Success { get; init; } = true;
}