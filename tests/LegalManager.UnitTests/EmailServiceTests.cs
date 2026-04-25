using LegalManager.Application.Interfaces;
using LegalManager.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
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
        var svc = new EmailService(mock.Object, _config);

        await svc.EnviarBoasVindasAsync("cliente@test.com", "Escritório Teste", CancellationToken.None);

        Assert.NotNull(capture.Message);
        Assert.Contains("Bem-vindo ao LegalManager", capture.Message!.Subject);
        Assert.Contains("cliente@test.com", capture.Message.To);
    }

    [Fact]
    public async Task EnviarBoasVindasAsync_ContemNomeEscritorio_NoHtml()
    {
        var capture = new EmailCapture();
        var mock = CreateMockWithEmailCapture(capture);
        var svc = new EmailService(mock.Object, _config);

        await svc.EnviarBoasVindasAsync("cliente@test.com", "Meu Escritório", CancellationToken.None);

        Assert.NotNull(capture.Message);
        Assert.Contains("Meu Escritório", capture.Message!.HtmlBody);
    }

    [Fact]
    public async Task EnviarConviteUsuarioAsync_ChamaEmailSendAsync_ComLinkNoHtml()
    {
        var capture = new EmailCapture();
        var mock = CreateMockWithEmailCapture(capture);
        var svc = new EmailService(mock.Object, _config);

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
        var svc = new EmailService(mock.Object, _config);

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
        var svc = new EmailService(mock.Object, _config);

        await svc.EnviarTrialExpirandoAsync("admin@test.com", "Escritório Teste", 5, CancellationToken.None);

        Assert.NotNull(capture.Message);
        Assert.Contains("5 dia(s)", capture.Message!.HtmlBody);
    }

    [Fact]
    public async Task EnviarAlertaPrazoTarefaAsync_ExibeUrgenciaZero_Hoje()
    {
        var capture = new EmailCapture();
        var mock = CreateMockWithEmailCapture(capture);
        var svc = new EmailService(mock.Object, _config);

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
        var svc = new EmailService(mock.Object, _config);

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
        var svc = new EmailService(mock.Object, _config);

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
        var svc = new EmailService(mock.Object, _config);

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
        var svc = new EmailService(mock.Object, _config);

        await svc.EnviarNovoAndamentoAsync(
            "adv@test.com", "Dr. Joao", "1234567-89.2024.8.26.0001",
            "Audiencia redesignada para 15/05/2026 as 14h", CancellationToken.None);

        Assert.NotNull(capture.Message);
        Assert.Contains("1234567-89.2024.8.26.0001", capture.Message!.HtmlBody);
        Assert.Contains("Audiencia redesignada", capture.Message.HtmlBody);
    }

    [Fact]
    public async Task EnviarAlertaPrazoProcessualAsync_ContemDescricao_EDataFinal()
    {
        var capture = new EmailCapture();
        var mock = CreateMockWithEmailCapture(capture);
        var svc = new EmailService(mock.Object, _config);

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
        var svc = new EmailService(mock.Object, _config);

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
        var svc = new EmailService(mock.Object, _config);

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
        var svc = new EmailService(mock.Object, _config);

        await svc.EnviarAndamentoTraduzidoAsync(
            "cliente@test.com", "Carlos Souza", "1234567-89.2024.8.26.0001",
            "O tribunal aceitou o recurso e marcado nova audiência para junho.", CancellationToken.None);

        Assert.NotNull(capture.Message);
        Assert.Contains("Carlos Souza", capture.Message!.HtmlBody);
        Assert.Contains("O tribunal aceitou o recurso", capture.Message.HtmlBody);
    }

[Fact]
    public async Task EnviarCobrancaAsync_SemQRCode_NaoIncluiImagem()
    {
        var capture = new EmailCapture();
        var mock = CreateMockWithEmailCapture(capture);
        var svc = new EmailService(mock.Object, _config);

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
        var svc = new EmailService(mock.Object, _config);

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
        var svc = new EmailService(mock.Object, _config);

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
        var svc = new EmailService(mock.Object, _config);

        await svc.EnviarBoasVindasAsync("x@y.com", "Test", CancellationToken.None);

        Assert.NotNull(capture.Message);
        Assert.Equal("LegalManager <noreply@test.com.br>", capture.Message!.From);
    }

    [Fact]
    public async Task TodosMetodos_EscapaHtml_ContraXSS()
    {
        var capture = new EmailCapture();
        var mock = CreateMockWithEmailCapture(capture);
        var svc = new EmailService(mock.Object, _config);

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
        var svc = new EmailService(mock.Object, _config);

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