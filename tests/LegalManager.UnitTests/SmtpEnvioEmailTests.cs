using LegalManager.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Resend;

namespace LegalManager.UnitTests;

public class SmtpEnvioEmailTests
{
    private static IConfiguration Config(string? smtpHost) => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Resend:FromName"] = "Causify",
            ["Resend:FromEmail"] = "noreply@test.com.br",
            ["Resend:ApiToken"] = "test-token",
            ["App:FrontendUrl"] = "http://localhost:5000",
            ["Smtp:Host"] = smtpHost,
        })
        .Build();

    [Fact]
    public async Task ComSmtpHost_EnviaPorSmtp_ENaoChamaResend()
    {
        var resend = new Mock<IResend>();
        var smtp = new Mock<ISmtpEnvioEmail>();
        EmailMessage? enviada = null;
        smtp.Setup(s => s.EnviarAsync(It.IsAny<EmailMessage>(), It.IsAny<CancellationToken>()))
            .Callback<EmailMessage, CancellationToken>((m, _) => enviada = m)
            .Returns(Task.CompletedTask);
        var svc = new EmailService(resend.Object, Config("mailpit"), NullLogger<EmailService>.Instance, smtp.Object);

        await svc.EnviarResetSenhaAsync("usuario@test.com", "http://localhost/reset/abc");

        Assert.NotNull(enviada);
        Assert.Contains("usuario@test.com", enviada!.To);
        Assert.Contains("http://localhost/reset/abc", enviada.HtmlBody);
        resend.Verify(r => r.EmailSendAsync(It.IsAny<EmailMessage>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SemSmtpHost_UsaResend()
    {
        var resend = new Mock<IResend>();
        resend.Setup(r => r.EmailSendAsync(It.IsAny<EmailMessage>(), It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult<ResendResponse<Guid>>(null!));
        var smtp = new Mock<ISmtpEnvioEmail>();
        var svc = new EmailService(resend.Object, Config(null), NullLogger<EmailService>.Instance, smtp.Object);

        await svc.EnviarResetSenhaAsync("usuario@test.com", "http://localhost/reset/abc");

        resend.Verify(r => r.EmailSendAsync(It.IsAny<EmailMessage>(), It.IsAny<CancellationToken>()), Times.Once);
        smtp.Verify(s => s.EnviarAsync(It.IsAny<EmailMessage>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task FalhaNoSmtp_NaoPropagaExcecao()
    {
        var smtp = new Mock<ISmtpEnvioEmail>();
        smtp.Setup(s => s.EnviarAsync(It.IsAny<EmailMessage>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("servidor fora"));
        var svc = new EmailService(new Mock<IResend>().Object, Config("mailpit"), NullLogger<EmailService>.Instance, smtp.Object);

        var ex = await Record.ExceptionAsync(() => svc.EnviarResetSenhaAsync("usuario@test.com", "http://x"));

        Assert.Null(ex);
    }

    [Fact]
    public void ParaMailMessage_CopiaRemetenteDestinatariosAssuntoEHtml()
    {
        var msg = new EmailMessage
        {
            From = "Causify <noreply@test.com.br>",
            Subject = "Assunto",
            HtmlBody = "<p>Olá</p>",
        };
        msg.To.Add("a@test.com");
        msg.To.Add("b@test.com");

        using var mail = SmtpEnvioEmail.ParaMailMessage(msg);

        Assert.Equal("noreply@test.com.br", mail.From!.Address);
        Assert.Equal("Causify", mail.From.DisplayName);
        Assert.Equal(new[] { "a@test.com", "b@test.com" }, mail.To.Select(t => t.Address));
        Assert.Equal("Assunto", mail.Subject);
        Assert.Equal("<p>Olá</p>", mail.Body);
        Assert.True(mail.IsBodyHtml);
    }
}
