using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LegalManager.Domain.Entities;
using LegalManager.Domain.Enums;
using LegalManager.Infrastructure.AssinaturaDigital;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Moq;

namespace LegalManager.UnitTests;

public class CifraSegredosTests
{
    private static CifraSegredos Nova() => new(RandomNumberGenerator.GetBytes(32));

    [Fact]
    public void Cifrar_E_Decifrar_DevolveOTextoOriginal()
    {
        var c = Nova();
        var cifrado = c.Cifrar("ctr_segredo-123");
        Assert.StartsWith("v1:", cifrado);
        Assert.DoesNotContain("segredo", cifrado);
        Assert.Equal("ctr_segredo-123", c.Decifrar(cifrado));
    }

    [Fact]
    public void Cifrar_UsaNonceNovoACadaVez()
    {
        var c = Nova();
        Assert.NotEqual(c.Cifrar("a"), c.Cifrar("a"));
    }

    [Fact]
    public void Decifrar_RecusaAdulteracaoEChaveErrada()
    {
        var c = Nova();
        var cifrado = c.Cifrar("valor");
        var bytes = Convert.FromBase64String(cifrado[3..]);
        bytes[^1] ^= 1;
        Assert.ThrowsAny<CryptographicException>(() => c.Decifrar("v1:" + Convert.ToBase64String(bytes)));
        Assert.ThrowsAny<CryptographicException>(() => Nova().Decifrar(cifrado));
        Assert.ThrowsAny<CryptographicException>(() => c.Decifrar("v2:abc"));
        Assert.ThrowsAny<CryptographicException>(() => c.Decifrar("v1:" + Convert.ToBase64String(new byte[10])));
    }

    [Fact]
    public void SemChave_FicaIndisponivel()
    {
        var c = new CifraSegredos((byte[]?)null);
        Assert.False(c.Disponivel);
        Assert.Throws<InvalidOperationException>(() => c.Cifrar("x"));
        Assert.Throws<InvalidOperationException>(() => c.Decifrar("v1:x"));
    }

    private static CifraSegredos PorConfig(string? chave, string ambiente)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [CifraSegredos.ChaveConfig] = chave }).Build();
        var env = new Mock<IHostEnvironment>();
        env.Setup(e => e.EnvironmentName).Returns(ambiente);
        return new CifraSegredos(config, env.Object);
    }

    [Fact]
    public void Config_ChaveDeDesenvolvimentoSoEmDevelopment()
    {
        Assert.True(PorConfig(null, Environments.Development).Disponivel);
        Assert.False(PorConfig(null, Environments.Production).Disponivel);
        var chave = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var a = PorConfig(chave, Environments.Production);
        Assert.Equal("ok", PorConfig(chave, Environments.Production).Decifrar(a.Cifrar("ok")));
    }

    [Theory]
    [InlineData("nao-e-base64!!")]
    [InlineData("AAAA")]
    public void Config_ChaveInvalidaFalhaNaPartida(string chave) =>
        Assert.Throws<InvalidOperationException>(() => PorConfig(chave, Environments.Production));
}

public class WebhookContratosTests
{
    private const string Segredo = "whsec_teste123";
    private static readonly byte[] Corpo = Encoding.UTF8.GetBytes("{\"id\":\"e1\",\"type\":\"webhook.test\"}");

    [Fact]
    public void AssinaturaValida_AceitaAMontadaComOMesmoSegredo()
    {
        var agora = DateTimeOffset.UtcNow;
        var cab = WebhookContratos.Assinar(Corpo, Segredo, agora);
        Assert.Matches("^t=\\d+,v1=[0-9a-f]{64}$", cab);
        Assert.True(WebhookContratos.AssinaturaValida(cab, Corpo, Segredo, agora));
        Assert.True(WebhookContratos.AssinaturaValida(" v1=" + cab.Split(",v1=")[1] + ", " + cab.Split(',')[0], Corpo, Segredo, agora));
    }

    [Fact]
    public void AssinaturaValida_RecusaSegredoCorpoOuHorarioErrados()
    {
        var agora = DateTimeOffset.UtcNow;
        var cab = WebhookContratos.Assinar(Corpo, Segredo, agora);
        Assert.False(WebhookContratos.AssinaturaValida(cab, Corpo, "whsec_outro", agora));
        Assert.False(WebhookContratos.AssinaturaValida(cab, Encoding.UTF8.GetBytes("{}"), Segredo, agora));
        Assert.False(WebhookContratos.AssinaturaValida(cab, Corpo, Segredo, agora.AddMinutes(6)));
        Assert.False(WebhookContratos.AssinaturaValida(cab, Corpo, Segredo, agora.AddMinutes(-6)));
        Assert.True(WebhookContratos.AssinaturaValida(cab, Corpo, Segredo, agora.AddMinutes(4)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("t=abc,v1=00")]
    [InlineData("v1=00")]
    [InlineData("t=1")]
    [InlineData("lixo")]
    public void AssinaturaValida_RecusaCabecalhoMalformado(string? cab) =>
        Assert.False(WebhookContratos.AssinaturaValida(cab, Corpo, Segredo, DateTimeOffset.UtcNow));

    [Fact]
    public void AssinaturaValida_SemSegredoRecusa() =>
        Assert.False(WebhookContratos.AssinaturaValida(WebhookContratos.Assinar(Corpo, Segredo, DateTimeOffset.UtcNow), Corpo, "", DateTimeOffset.UtcNow));

    [Fact]
    public void Ler_DevolveEventoOuNull()
    {
        var e = WebhookContratos.Ler(Corpo);
        Assert.Equal("e1", e!.Id);
        Assert.Equal("webhook.test", e.Type);
        Assert.Null(e.Data);
        Assert.Null(WebhookContratos.Ler("não é json"u8));
    }
}

public class FontesAssinaturaTests
{
    internal static readonly List<ContratosVariavel> Nda =
    [
        new("parte1.nome", "Nome da primeira parte", "text", true),
        new("parte1.documento", "CPF/CNPJ da primeira parte", "cpf_cnpj", true),
        new("parte1.endereco", "Endereço da primeira parte", "text", true),
        new("parte2.nome", "Nome da segunda parte", "text", true),
        new("parte2.documento", "CPF/CNPJ da segunda parte", "cpf_cnpj", true),
        new("parte2.endereco", "Endereço da segunda parte", "text", true),
        new("finalidade", "Finalidade da troca de informações", "text", true),
        new("vigencia.anos", "Anos de sigilo após o encerramento", "number", true),
        new("multa.valor", "Valor da multa", "money", true),
        new("foro.cidade", "Cidade do foro", "text", true),
        new("assinatura.cidade", "Cidade da assinatura", "text", true),
        new("assinatura.data", "Data da assinatura", "date", true),
    ];

    [Fact]
    public void Sugerir_PrimeiraParteEhOClienteESegundaOEscritorio()
    {
        var s = FontesAssinatura.Sugerir(Nda);
        Assert.Equal("cliente.nome", s["parte1.nome"]);
        Assert.Equal("cliente.cpfCnpj", s["parte1.documento"]);
        Assert.Equal("cliente.endereco", s["parte1.endereco"]);
        Assert.Equal("escritorio.nome", s["parte2.nome"]);
        Assert.Equal("escritorio.cnpj", s["parte2.documento"]);
        Assert.Equal("escritorio.endereco", s["parte2.endereco"]);
        Assert.Equal("contrato.objeto", s["finalidade"]);
        Assert.Null(s["vigencia.anos"]);
        Assert.Equal("contrato.valorTotal", s["multa.valor"]);
        Assert.Equal("cliente.cidade", s["foro.cidade"]);
        Assert.Equal("hoje", s["assinatura.data"]);
    }

    [Fact]
    public void Sugerir_PalavrasDeContratoDecidemDeQuemEhODado()
    {
        var s = FontesAssinatura.Sugerir(
        [
            new("contratada.nome", "Nome da contratada", "text", true),
            new("contratada.cnpj", "CNPJ do escritório", "cpf_cnpj", true),
            new("contratante.nome", "Nome do contratante", "text", true),
            new("contratante.email", "E-mail do cliente", "text", false),
            new("contratante.telefone", "Celular do contratante", "text", false),
            new("advogado", "Nome do advogado responsável", "text", false),
            new("valor.entrada", "Valor da entrada", "money", false),
            new("inicio", "Data de início", "date", false),
            new("vencimento", "Primeiro vencimento", "date", false),
            new("parcelas", "Número de parcelas", "number", false),
            new("forma", "Forma de pagamento", "text", false),
            new("objeto", "Objeto do contrato", "text", false),
            new("numero.contrato", "Número do contrato", "text", false),
            new("processo", "Número do processo", "text", false),
            new("vara", "Vara", "text", false),
            new("comarca", "Comarca", "text", false),
            new("cidade.escritorio", "Cidade do escritório", "text", false),
            new("outra", "Observação", "text", false),
        ]);
        Assert.Equal("escritorio.nome", s["contratada.nome"]);
        Assert.Equal("escritorio.cnpj", s["contratada.cnpj"]);
        Assert.Equal("cliente.nome", s["contratante.nome"]);
        Assert.Equal("cliente.email", s["contratante.email"]);
        Assert.Equal("cliente.telefone", s["contratante.telefone"]);
        Assert.Equal("advogado.nome", s["advogado"]);
        Assert.Equal("contrato.valorEntrada", s["valor.entrada"]);
        Assert.Equal("contrato.dataInicio", s["inicio"]);
        Assert.Equal("contrato.dataPrimeiraParcela", s["vencimento"]);
        Assert.Equal("contrato.numeroParcelas", s["parcelas"]);
        Assert.Equal("contrato.formaPagamento", s["forma"]);
        Assert.Equal("contrato.objeto", s["objeto"]);
        Assert.Equal("contrato.numero", s["numero.contrato"]);
        Assert.Equal("processo.numero", s["processo"]);
        Assert.Equal("processo.vara", s["vara"]);
        Assert.Equal("processo.comarca", s["comarca"]);
        Assert.Null(s["cidade.escritorio"]);
        Assert.Null(s["outra"]);
    }

    [Fact]
    public void Sugerir_NomeDoAdvogadoSemParte() =>
        Assert.Equal("advogado.nome", FontesAssinatura.Sugerir([new("resp", "Nome do advogado", "text", false)])["resp"]);

    [Theory]
    [InlineData("(11) 98765-4321", "+55 11 98765-4321")]
    [InlineData("11987654321", "+55 11 98765-4321")]
    [InlineData("1133334444", "+55 11 3333-4444")]
    [InlineData("011 98765-4321", "+55 11 98765-4321")]
    [InlineData("+55 (21) 99999-0000", "+55 21 99999-0000")]
    [InlineData("5511987654321", "+55 11 98765-4321")]
    [InlineData("+1 415 555 0100", "+14155550100")]
    [InlineData("12345", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void TelefoneInternacional(string? entrada, string? esperado) =>
        Assert.Equal(esperado, FontesAssinatura.TelefoneInternacional(entrada));

    [Fact]
    public void EnderecoCompleto_JuntaOQueExiste()
    {
        Assert.Equal("Rua A, 10, São Paulo/SP, CEP 01000-000",
            FontesAssinatura.EnderecoCompleto(new Contato { Endereco = "Rua A, 10", Cidade = "São Paulo", Estado = "SP", Cep = "01000-000" }));
        Assert.Equal("Campinas", FontesAssinatura.EnderecoCompleto(new Contato { Cidade = "Campinas" }));
        Assert.Null(FontesAssinatura.EnderecoCompleto(new Contato()));
    }

    [Fact]
    public void CpfDoSignatario_SoPessoaFisicaCom11Digitos()
    {
        Assert.Equal("52998224725", FontesAssinatura.CpfDoSignatario(new Contato { Tipo = TipoPessoa.PF, CpfCnpj = "529.982.247-25" }));
        Assert.Null(FontesAssinatura.CpfDoSignatario(new Contato { Tipo = TipoPessoa.PJ, CpfCnpj = "11.222.333/0001-81" }));
        Assert.Null(FontesAssinatura.CpfDoSignatario(new Contato { Tipo = TipoPessoa.PF, CpfCnpj = "123" }));
        Assert.Null(FontesAssinatura.CpfDoSignatario(new Contato { Tipo = TipoPessoa.PF }));
    }

    [Fact]
    public void Valor_CobreTodoOCatalogoNoFormatoDaApi()
    {
        var ctx = new ContextoAssinatura(
            new Contato { Nome = "Maria", CpfCnpj = "529.982.247-25", Cidade = "Santos", Email = "m@x.com", Telefone = "11999990000", Endereco = "Rua B" },
            new ContratoHonorario
            {
                NumeroContrato = "HON-7", Objeto = "Ação trabalhista", ValorTotal = 12345.6m, ValorEntrada = 1000m, NumeroParcelas = 10,
                FormaPagamento = FormaPagamentoContrato.EntradaParcelado, DataInicio = new DateTime(2026, 11, 1), DataPrimeiraParcela = new DateTime(2026, 12, 5),
            },
            new Processo { NumeroCNJ = "0000001-02.2026.8.26.0100", Vara = "1ª Vara", Comarca = "São Paulo" },
            new Tenant { Nome = "Silva Advogados", Cnpj = "11.222.333/0001-81", Endereco = "Av. Paulista, 1", Email = "c@silva.adv.br", Telefone = "1133334444" },
            new Usuario { Nome = "Ana", Email = "ana@silva.adv.br" },
            new DateTime(2026, 11, 16));

        foreach (var f in FontesAssinatura.Catalogo)
            Assert.False(string.IsNullOrEmpty(FontesAssinatura.Valor(f.Chave, ctx)), f.Chave);

        Assert.Equal("12345.60", FontesAssinatura.Valor("contrato.valorTotal", ctx));
        Assert.Equal("2026-12-05", FontesAssinatura.Valor("contrato.dataPrimeiraParcela", ctx));
        Assert.Equal("2026-11-16", FontesAssinatura.Valor("hoje", ctx));
        Assert.Equal("Entrada + parcelas", FontesAssinatura.Valor("contrato.formaPagamento", ctx));
        Assert.Null(FontesAssinatura.Valor("desconhecida", ctx));
        Assert.True(FontesAssinatura.Existe("cliente.nome"));
        Assert.False(FontesAssinatura.Existe("cliente.senha"));
        Assert.False(FontesAssinatura.Existe(null));

        var semOpcionais = ctx with
        {
            Contrato = new ContratoHonorario { FormaPagamento = FormaPagamentoContrato.AVista },
            Processo = null,
        };
        Assert.Null(FontesAssinatura.Valor("contrato.valorEntrada", semOpcionais));
        Assert.Null(FontesAssinatura.Valor("contrato.dataPrimeiraParcela", semOpcionais));
        Assert.Null(FontesAssinatura.Valor("processo.numero", semOpcionais));
        Assert.Equal("À vista", FontesAssinatura.Valor("contrato.formaPagamento", semOpcionais));
        Assert.Equal("Parcelado", FontesAssinatura.Valor("contrato.formaPagamento",
            semOpcionais with { Contrato = new ContratoHonorario { FormaPagamento = FormaPagamentoContrato.Parcelado } }));
    }
}

public class ContratosHttpClientTests
{
    private sealed class Handler : HttpMessageHandler
    {
        public readonly List<(HttpRequestMessage Req, string? Corpo)> Recebidas = [];
        public readonly Queue<Func<HttpResponseMessage>> Respostas = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Recebidas.Add((request, request.Content == null ? null : await request.Content.ReadAsStringAsync(ct)));
            return Respostas.Dequeue()();
        }
    }

    private readonly Handler _h = new();
    private ContratosHttpClient Cliente() =>
        new(new HttpClient(_h) { BaseAddress = new Uri("https://contratos.test" + ContratosHttpClient.Caminho) });

    private void Responder(HttpStatusCode status, string json, string tipo = "application/json") =>
        _h.Respostas.Enqueue(() => new HttpResponseMessage(status) { Content = new StringContent(json, Encoding.UTF8, tipo) });

    private const string ContratoJson = """
        {"id":"c1","number":5,"title":"NDA","status":"sent","externalReference":"ref","template":{"id":"t1","name":"NDA","version":2},
         "signers":[{"id":"s1","name":"Ana","email":"a@x.com","phone":null,"deliveryMethod":"email","role":"Contratante","action":"sign","signOrder":1,"status":"pending"},
                    {"id":"s2","name":"Bia","phone":"+55 11 90000-0000","deliveryMethod":"whats_app","status":"viewed","viewedAt":"2026-11-16T10:00:00+00:00"}],
         "hasSignedDocument":false,"sendError":null,"createdAt":"2026-11-16T09:00:00+00:00","updatedAt":"2026-11-16T10:00:00+00:00","novoCampo":1}
        """;

    [Fact]
    public async Task ObterConta_MandaBearerEAcceptELeOJson()
    {
        Responder(HttpStatusCode.OK, """{"organization":{"name":"Ricardos Corp","slug":"r","planId":"empresa","planName":"Empresa"},"usage":{"contractsUsed":1,"contractsLimit":250,"creditsAvailable":0,"availableSends":249,"paymentOverdue":false}}""");
        var conta = await Cliente().ObterContaAsync("ctr_abc");
        Assert.Equal("Ricardos Corp", conta.Organization.Name);
        Assert.Equal(249, conta.Usage.AvailableSends);
        var (req, _) = _h.Recebidas.Single();
        Assert.Equal("https://contratos.test/api/public/v1/account", req.RequestUri!.ToString());
        Assert.Equal("Bearer ctr_abc", req.Headers.Authorization!.ToString());
        Assert.Contains("application/json", req.Headers.Accept.ToString());
    }

    [Fact]
    public async Task Templates_ListaEDetalhe()
    {
        Responder(HttpStatusCode.OK, """[{"id":"t1","name":"NDA","description":null,"version":1,"variableCount":12}]""");
        Responder(HttpStatusCode.OK, """{"id":"t 1","name":"NDA","version":1,"variables":[{"key":"a.b","label":"A","type":"text","required":true}],"suggestedSigners":[{"role":"Parte","action":"sign","order":1}]}""");
        var c = Cliente();
        Assert.Equal("NDA", (await c.ListarTemplatesAsync("k")).Single().Name);
        var t = await c.ObterTemplateAsync("k", "t 1");
        Assert.Equal("a.b", t.Variables.Single().Key);
        Assert.Equal("/api/public/v1/templates/t%201", _h.Recebidas[1].Req.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task CriarContrato_MandaIdempotencyKeyECamelCaseSemNulos_ENormalizaWhatsApp()
    {
        Responder(HttpStatusCode.Created, ContratoJson);
        var novo = new ContratosNovoContrato("t1", new() { ["parte1.nome"] = "Ana" },
            [new ContratosNovoSignatario("Ana", "email", "a@x.com", null, null, "Contratante", "sign", 1)],
            "NDA", "ref", "parallel", null, null, true);
        var c = await Cliente().CriarContratoAsync("k", novo, "causify-1");

        var (req, corpo) = _h.Recebidas.Single();
        Assert.Equal(HttpMethod.Post, req.Method);
        Assert.Equal("causify-1", req.Headers.GetValues("Idempotency-Key").Single());
        using var doc = JsonDocument.Parse(corpo!);
        Assert.Equal("t1", doc.RootElement.GetProperty("templateId").GetString());
        Assert.True(doc.RootElement.GetProperty("send").GetBoolean());
        Assert.False(doc.RootElement.TryGetProperty("expiresAt", out _));
        Assert.Equal("Ana", doc.RootElement.GetProperty("variables").GetProperty("parte1.nome").GetString());
        Assert.False(doc.RootElement.GetProperty("signers")[0].TryGetProperty("phone", out _));

        Assert.Equal(5, c.Number);
        Assert.Equal("whatsapp", c.Signers[1].DeliveryMethod);
        Assert.Equal("viewed", c.Signers[1].Status);
    }

    [Fact]
    public async Task AcoesDoContrato_UsamOsCaminhosCertos()
    {
        Responder(HttpStatusCode.OK, ContratoJson);
        Responder(HttpStatusCode.Accepted, ContratoJson);
        Responder(HttpStatusCode.OK, ContratoJson);
        Responder(HttpStatusCode.NoContent, "");
        _h.Respostas.Enqueue(() => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent("%PDF-1.4"u8.ToArray()) });
        var c = Cliente();
        await c.ObterContratoAsync("k", "c1");
        await c.EnviarAsync("k", "c1", "envio-1");
        await c.CancelarAsync("k", "c1");
        await c.ReenviarLinkAsync("k", "c1", "s2");
        var pdf = await c.BaixarAssinadoAsync("k", "c1");

        Assert.Equal("%PDF-1.4", Encoding.ASCII.GetString(pdf));
        Assert.Equal(
            ["GET /api/public/v1/contracts/c1", "POST /api/public/v1/contracts/c1/send", "POST /api/public/v1/contracts/c1/cancel",
             "POST /api/public/v1/contracts/c1/signers/s2/resend", "GET /api/public/v1/contracts/c1/signed-document"],
            _h.Recebidas.Select(r => $"{r.Req.Method} {r.Req.RequestUri!.AbsolutePath}"));
        Assert.Equal("envio-1", _h.Recebidas[1].Req.Headers.GetValues("Idempotency-Key").Single());
        Assert.Equal("{}", _h.Recebidas[2].Corpo);
    }

    [Fact]
    public async Task ProblemDetails_ViraExcecaoComCodigoErrosEContrato()
    {
        Responder(HttpStatusCode.Conflict,
            """{"title":"Acabaram os contratos do ciclo.","status":409,"code":"quota_exceeded","contractId":"c9","errors":{"signers[0].email":["E-mail inválido."]}}""",
            "application/problem+json");
        var e = await Assert.ThrowsAsync<ContratosApiException>(() => Cliente().ObterContaAsync("k"));
        Assert.Equal(409, e.Status);
        Assert.Equal("quota_exceeded", e.Codigo);
        Assert.Equal("Acabaram os contratos do ciclo.", e.Mensagem);
        Assert.Equal("c9", e.ContratoId);
        Assert.Equal("E-mail inválido.", e.Erros!["signers[0].email"].Single());
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "Chave de API inválida")]
    [InlineData(HttpStatusCode.TooManyRequests, "Muitas chamadas")]
    [InlineData(HttpStatusCode.BadGateway, "indisponível")]
    [InlineData(HttpStatusCode.Gone, "HTTP 410")]
    public async Task ErroSemCorpo_TemMensagemPadrao(HttpStatusCode status, string trecho)
    {
        Responder(status, "<html>erro</html>", "text/html");
        var e = await Assert.ThrowsAsync<ContratosApiException>(() => Cliente().ObterContaAsync("k"));
        Assert.Equal((int)status, e.Status);
        Assert.Contains(trecho, e.Mensagem);
    }

    [Fact]
    public async Task FalhaDeRede_Vira503()
    {
        _h.Respostas.Enqueue(() => throw new HttpRequestException("sem rede"));
        var e = await Assert.ThrowsAsync<ContratosApiException>(() => Cliente().ObterContaAsync("k"));
        Assert.Equal(503, e.Status);
        Assert.Equal("unavailable", e.Codigo);
    }

    [Fact]
    public async Task RespostaVazia_ViraExcecao()
    {
        Responder(HttpStatusCode.OK, "null");
        await Assert.ThrowsAsync<ContratosApiException>(() => Cliente().ObterContaAsync("k"));
    }

    [Theory]
    [InlineData(null, "email")]
    [InlineData("", "email")]
    [InlineData("whats_app", "whatsapp")]
    [InlineData("sms", "sms")]
    public void NormalizarCanal(string? entrada, string esperado) =>
        Assert.Equal(esperado, ContratosHttpClient.NormalizarCanal(entrada));
}
