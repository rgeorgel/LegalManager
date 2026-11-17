using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Hangfire;
using Hangfire.Common;
using Hangfire.States;
using LegalManager.API.Controllers;
using LegalManager.Application.DTOs.AssinaturaDigital;
using LegalManager.Application.Interfaces;
using LegalManager.Domain.Entities;
using LegalManager.Domain.Enums;
using LegalManager.Domain.Interfaces;
using LegalManager.Infrastructure.AssinaturaDigital;
using LegalManager.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace LegalManager.UnitTests;

public class AssinaturaDigitalServiceTests
{
    private const string Chave = "ctr_ChaveDeTesteMuitoLonga123";
    private const string Segredo = "whsec_SegredoDeTeste123";

    private readonly AppDbContext _db = new(new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private readonly Mock<IContratosApi> _api = new();
    private readonly Mock<IStorageService> _storage = new();
    private readonly Mock<IBackgroundJobClient> _jobs = new();
    private readonly Mock<IAuditService> _audit = new();
    private readonly CifraSegredos _cifra = new(RandomNumberGenerator.GetBytes(32));
    private readonly Tenant _tenant;
    private readonly Usuario _admin;
    private readonly Usuario _advogado;
    private readonly Contato _cliente;
    private readonly ContratoHonorario _contrato;

    public AssinaturaDigitalServiceTests()
    {
        _tenant = new Tenant { Id = Guid.NewGuid(), Nome = "Silva Advogados", Cnpj = "11.222.333/0001-81", Plano = PlanoTipo.Pro, Status = StatusTenant.Ativo, CriadoEm = DateTime.UtcNow };
        _admin = Usuario(_tenant, "Ana Admin", PerfilUsuario.Admin);
        _advogado = Usuario(_tenant, "Bruno Advogado", PerfilUsuario.Advogado);
        _cliente = new Contato
        {
            Id = Guid.NewGuid(), TenantId = _tenant.Id, Nome = "Maria Cliente", Tipo = TipoPessoa.PF, CpfCnpj = "529.982.247-25",
            Email = "maria@cliente.com", Telefone = "(11) 98765-4321", Endereco = "Rua A, 1", Cidade = "Santos", Estado = "SP", CriadoEm = DateTime.UtcNow,
        };
        _contrato = new ContratoHonorario
        {
            Id = Guid.NewGuid(), TenantId = _tenant.Id, ContatoId = _cliente.Id, NumeroContrato = "HON-1", Objeto = "Ação revisional",
            ValorTotal = 1500.5m, DataInicio = new DateTime(2026, 11, 1), CriadoPorId = _admin.Id,
        };
        _db.Tenants.Add(_tenant);
        _db.Contatos.Add(_cliente);
        _db.ContratosHonorarios.Add(_contrato);
        _db.SaveChanges();
    }

    private Usuario Usuario(Tenant t, string nome, PerfilUsuario perfil)
    {
        var email = $"{Guid.NewGuid():N}@silva.adv.br";
        var u = new Usuario { Id = Guid.NewGuid(), TenantId = t.Id, Nome = nome, Perfil = perfil, Email = email, UserName = email, CriadoEm = DateTime.UtcNow };
        _db.Users.Add(u);
        return u;
    }

    private AssinaturaDigitalService Service(string? baseUrl = "https://contratos.test") =>
        new(_db, _api.Object, _cifra, _storage.Object, _jobs.Object,
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                [ContratosHttpClient.ConfigBaseUrl] = baseUrl,
                ["App:FrontendUrl"] = "https://app.causify.test/",
            }).Build(),
            NullLogger<AssinaturaDigitalService>.Instance);

    private AssinaturaDigitalController Controller(Usuario? usuario = null, string? papel = null, AssinaturaDigitalService? service = null)
    {
        usuario ??= _admin;
        var tc = new Mock<ITenantContext>();
        tc.Setup(t => t.TenantId).Returns(usuario.TenantId);
        tc.Setup(t => t.UserId).Returns(usuario.Id);
        tc.Setup(t => t.UserRole).Returns(papel ?? usuario.Perfil.ToString());
        return new AssinaturaDigitalController(service ?? Service(), tc.Object, _audit.Object)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };
    }

    private IntegracaoAssinatura Conectar(bool comSegredo = true)
    {
        var i = new IntegracaoAssinatura
        {
            Id = Guid.NewGuid(), TenantId = _tenant.Id, ApiKeyCifrada = _cifra.Cifrar(Chave), ApiKeyFinal = Chave[^4..],
            WebhookSecretCifrado = comSegredo ? _cifra.Cifrar(Segredo) : null, OrganizacaoNome = "Ricardos Corp", PlanoNome = "Empresa",
            ConectadoEm = DateTime.UtcNow,
        };
        _db.IntegracoesAssinatura.Add(i);
        _db.SaveChanges();
        return i;
    }

    private static ContratosConta Conta(string nome = "Ricardos Corp") =>
        new(new ContratosOrganizacao(nome, "r", "empresa", "Empresa"), new ContratosUso(3, 250, 0, 247, false));

    private static readonly DateTimeOffset T0 = new(2026, 11, 16, 12, 0, 0, TimeSpan.Zero);

    private static ContratosSignatario Sig(string id, string nome, string status, string? motivo = null) =>
        new(id, nome, $"{id}@x.com", null, "email", "Parte", "sign", 1, status, null,
            status == "signed" ? T0 : null, status == "declined" ? T0 : null, motivo);

    private static ContratosContrato Ext(string status, DateTimeOffset atualizado, params ContratosSignatario[] sigs) =>
        new("ext-1", 7, "NDA: Maria Cliente", status, "ref", new ContratosTemplateRef("tpl-1", "NDA", 1), sigs.ToList(),
            status == "completed", null, null, T0.AddHours(-2), atualizado, status == "draft" ? null : T0.AddHours(-1),
            status == "completed" ? atualizado : null, status == "cancelled" ? atualizado : null);

    private SolicitacaoAssinatura Solicitacao(StatusSolicitacaoAssinatura status = StatusSolicitacaoAssinatura.Enviada,
        DateTimeOffset? atualizado = null, params ContratosSignatario[] sigs)
    {
        var s = new SolicitacaoAssinatura
        {
            Id = Guid.NewGuid(), TenantId = _tenant.Id, ContratoHonorarioId = _contrato.Id, ContatoId = _cliente.Id,
            ContratoExternoId = "ext-1", NumeroExterno = 7, TemplateId = "tpl-1", TemplateNome = "NDA", Titulo = "NDA: Maria Cliente",
            Status = status, CriadoPorId = _advogado.Id, CriadoEm = DateTime.UtcNow, AtualizadoExternoEm = (atualizado ?? T0).UtcDateTime,
            SignatariosJson = JsonSerializer.Serialize(sigs.Select(g => new SignatarioAssinaturaDto(g.Id, g.Name, g.Email, null, "email", g.Role, g.Status, null, null, null, null)).ToList(),
                new JsonSerializerOptions(JsonSerializerDefaults.Web)),
        };
        _db.SolicitacoesAssinatura.Add(s);
        _db.SaveChanges();
        return s;
    }

    private static int Status(IActionResult r) => r switch
    {
        ObjectResult o => o.StatusCode ?? 200,
        StatusCodeResult s => s.StatusCode,
        _ => throw new InvalidOperationException(r.GetType().Name),
    };

    private static int Status<T>(ActionResult<T> r) => r.Result == null ? 200 : Status(r.Result);
    private static T Valor<T>(ActionResult<T> r) => r.Value ?? (T)Assert.IsAssignableFrom<ObjectResult>(r.Result).Value!;
    private static string Mensagem(IActionResult? r) =>
        JsonSerializer.Serialize(Assert.IsAssignableFrom<ObjectResult>(r).Value,
            new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });

    // ── Conexão ───────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Integracao_ClienteNaoAcessaENaoAdminSoVeSeEstaConectado()
    {
        Assert.Equal(403, Status(await Controller(papel: "Cliente").Integracao(CancellationToken.None)));

        var semConexao = Valor(await Controller(_advogado).Integracao(CancellationToken.None));
        Assert.False(semConexao.Conectado);
        Assert.True(semConexao.Disponivel);
        Assert.False(semConexao.PodeConfigurar);

        Conectar();
        var dto = Valor(await Controller(_advogado).Integracao(CancellationToken.None));
        Assert.True(dto.Conectado);
        Assert.Null(dto.Organizacao);
        Assert.Null(dto.WebhookUrl);
        _api.Verify(a => a.ObterContaAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Integracao_AdminVeUsoEWebhookEAtualizaNomeDaConta()
    {
        var i = Conectar();
        _api.Setup(a => a.ObterContaAsync(Chave, It.IsAny<CancellationToken>())).ReturnsAsync(Conta("Novo Nome"));
        var dto = Valor(await Controller().Integracao(CancellationToken.None));
        Assert.Equal("Novo Nome", dto.Organizacao);
        Assert.Equal(247, dto.Uso!.EnviosDisponiveis);
        Assert.Equal($"https://app.causify.test/api/assinatura-digital/webhook/{i.Id}", dto.WebhookUrl);
        Assert.True(dto.TemSegredoWebhook);
        Assert.Equal("https://contratos.test", dto.ContratosUrl);
        Assert.Equal("Novo Nome", (await _db.IntegracoesAssinatura.SingleAsync()).OrganizacaoNome);
    }

    [Fact]
    public async Task Integracao_ErroNaContaViraAviso()
    {
        Conectar();
        _api.Setup(a => a.ObterContaAsync(Chave, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ContratosApiException(403, "api_not_in_plan", "O plano atual da conta não inclui a API."));
        var dto = Valor(await Controller().Integracao(CancellationToken.None));
        Assert.True(dto.Conectado);
        Assert.Null(dto.Uso);
        Assert.Equal("O plano atual da conta não inclui a API.", dto.AvisoUso);
    }

    [Fact]
    public async Task Conectar_SoAdmin()
    {
        var r = await Controller(_advogado).Conectar(new ConectarAssinaturaDto(Chave, null), CancellationToken.None);
        Assert.Equal(403, Status(r));
        Assert.Equal(403, Status(await Controller(_advogado).Desconectar(CancellationToken.None)));
    }

    [Fact]
    public async Task Conectar_GuardaAChaveCifradaEAudita()
    {
        _api.Setup(a => a.ObterContaAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(Conta());
        var r = await Controller().Conectar(new ConectarAssinaturaDto("  " + Chave + " ", null), CancellationToken.None);
        var dto = Valor(r);
        Assert.True(dto.Conectado);
        Assert.Equal("Ricardos Corp", dto.Organizacao);
        Assert.Equal(Chave[^4..], dto.ChaveFinal);

        var i = await _db.IntegracoesAssinatura.SingleAsync();
        Assert.DoesNotContain(Chave, i.ApiKeyCifrada);
        Assert.Equal(Chave, _cifra.Decifrar(i.ApiKeyCifrada));
        Assert.Equal(Chave[^4..], i.ApiKeyFinal);
        Assert.Equal(_admin.Id, i.ConectadoPorId);
        Assert.Null(i.WebhookSecretCifrado);
        _audit.Verify(a => a.LogAsync(It.IsAny<AuditLogEntry>(), It.IsAny<CancellationToken>()), Times.Once);

        // Trocar a chave reaproveita a linha; colar só o segredo não pede a chave de novo.
        await Controller().Conectar(new ConectarAssinaturaDto(null, Segredo), CancellationToken.None);
        i = await _db.IntegracoesAssinatura.SingleAsync();
        Assert.Equal(Segredo, _cifra.Decifrar(i.WebhookSecretCifrado!));
        await Controller().Conectar(new ConectarAssinaturaDto("ctr_OutraChaveDeTeste999", null), CancellationToken.None);
        Assert.Equal("ctr_OutraChaveDeTeste999", _cifra.Decifrar((await _db.IntegracoesAssinatura.SingleAsync()).ApiKeyCifrada));
    }

    [Theory]
    [InlineData("chave-errada", null, "começa com ctr_")]
    [InlineData("ctr_curta", null, "começa com ctr_")]
    [InlineData("ctr_tem espaco no meio", null, "começa com ctr_")]
    [InlineData(null, null, "Cole a chave")]
    [InlineData(null, "whsec_x", "Cole a chave")]
    public async Task Conectar_ValidaOFormato(string? chave, string? segredo, string trecho)
    {
        var r = await Controller().Conectar(new ConectarAssinaturaDto(chave, segredo), CancellationToken.None);
        Assert.Equal(400, Status(r));
        Assert.Contains(trecho, Mensagem(r.Result));
    }

    [Fact]
    public async Task Conectar_SegredoPrecisaComecarComWhsec()
    {
        Conectar(comSegredo: false);
        var r = await Controller().Conectar(new ConectarAssinaturaDto(null, "segredo-qualquer"), CancellationToken.None);
        Assert.Equal(400, Status(r));
        Assert.Contains("whsec_", Mensagem(r.Result));
    }

    [Theory]
    [InlineData(401, 400, "não reconheceu a chave")]
    [InlineData(403, 400, "não inclui a API")]
    [InlineData(503, 502, "não inclui a API")]
    public async Task Conectar_ChaveRecusadaPeloContratos(int statusApi, int esperado, string trecho)
    {
        _api.Setup(a => a.ObterContaAsync(Chave, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ContratosApiException(statusApi, null, "O plano atual da conta não inclui a API."));
        var r = await Controller().Conectar(new ConectarAssinaturaDto(Chave, null), CancellationToken.None);
        Assert.Equal(esperado, Status(r));
        Assert.Contains(trecho, Mensagem(r.Result));
        Assert.False(await _db.IntegracoesAssinatura.AnyAsync());
    }

    [Fact]
    public async Task Conectar_IndisponivelSemEnderecoDoContratos()
    {
        var r = await Controller(service: Service(baseUrl: null)).Conectar(new ConectarAssinaturaDto(Chave, null), CancellationToken.None);
        Assert.Equal(503, Status(r));
        Assert.False(Valor(await Controller(service: Service(baseUrl: null)).Integracao(CancellationToken.None)).Disponivel);
    }

    [Fact]
    public async Task Desconectar_ApagaAIntegracaoEMantemAsSolicitacoes()
    {
        Assert.IsType<NoContentResult>(await Controller().Desconectar(CancellationToken.None));
        Conectar();
        Solicitacao();
        Assert.IsType<NoContentResult>(await Controller().Desconectar(CancellationToken.None));
        Assert.False(await _db.IntegracoesAssinatura.AnyAsync());
        Assert.True(await _db.SolicitacoesAssinatura.AnyAsync());
        _audit.Verify(a => a.LogAsync(It.IsAny<AuditLogEntry>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── Modelos e preparação ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Modelos_SemConexaoPedeParaConectar()
    {
        var r = await Controller(_advogado).Modelos(CancellationToken.None);
        Assert.Equal(409, Status(r));
        Assert.Contains("Integrações", Mensagem(r.Result));
        Assert.Equal(403, Status(await Controller(papel: "Cliente").Modelos(CancellationToken.None)));
    }

    [Fact]
    public async Task Modelos_ListaOsTemplates()
    {
        Conectar();
        _api.Setup(a => a.ListarTemplatesAsync(Chave, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new ContratosTemplateResumo("tpl-1", "NDA", null, 2, 12)]);
        var lista = Valor(await Controller(_advogado).Modelos(CancellationToken.None));
        Assert.Equal(new ModeloAssinaturaDto("tpl-1", "NDA", null, 2, 12), lista.Single());
    }

    [Fact]
    public async Task Modelos_ErroDaApiViraMensagem()
    {
        Conectar();
        _api.Setup(a => a.ListarTemplatesAsync(Chave, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ContratosApiException(401, "unauthorized", "Chave inválida"));
        var r = await Controller(_advogado).Modelos(CancellationToken.None);
        Assert.Equal(409, Status(r));
        Assert.Contains("conectar de novo", Mensagem(r.Result));
    }

    private void TemplateNda() =>
        _api.Setup(a => a.ObterTemplateAsync(Chave, "tpl-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ContratosTemplate("tpl-1", "NDA", null, 1, FontesAssinaturaTests.Nda,
                [new ContratosSignatarioSugerido("Segunda parte", "sign", 2), new ContratosSignatarioSugerido("Primeira parte", "sign", 1)]));

    [Fact]
    public async Task Preparar_PreencheComOsDadosDoContratoESugereSignatarios()
    {
        Conectar();
        TemplateNda();
        var p = Valor(await Controller(_advogado).Preparar(_contrato.Id, "tpl-1", CancellationToken.None));

        Assert.Equal("NDA: Maria Cliente", p.Titulo);
        var v = p.Variaveis.ToDictionary(x => x.Chave);
        Assert.Equal("Maria Cliente", v["parte1.nome"].Valor);
        Assert.Equal("529.982.247-25", v["parte1.documento"].Valor);
        Assert.Equal("Rua A, 1, Santos/SP", v["parte1.endereco"].Valor);
        Assert.Equal("Silva Advogados", v["parte2.nome"].Valor);
        Assert.Equal("1500.50", v["multa.valor"].Valor);
        Assert.Equal("Ação revisional", v["finalidade"].Valor);
        Assert.Null(v["vigencia.anos"].Fonte);
        Assert.Matches(@"^\d{4}-\d{2}-\d{2}$", v["assinatura.data"].Valor);
        Assert.Equal("1500.50", p.ValoresFontes["contrato.valorTotal"]);
        Assert.NotEmpty(p.Fontes);

        Assert.Equal(2, p.Signatarios.Count);
        var cliente = p.Signatarios[0];
        Assert.Equal(("Maria Cliente", "email", "maria@cliente.com", "+55 11 98765-4321", "52998224725", "Primeira parte"),
            (cliente.Nome, cliente.Canal, cliente.Email, cliente.Telefone, cliente.Cpf, cliente.Papel));
        Assert.Equal(("Bruno Advogado", "Segunda parte"), (p.Signatarios[1].Nome, p.Signatarios[1].Papel));
    }

    [Fact]
    public async Task Preparar_UsaAsFontesLembradasDoTemplate()
    {
        Conectar();
        TemplateNda();
        _db.MapeamentosModeloAssinatura.Add(new MapeamentoModeloAssinatura
        {
            Id = Guid.NewGuid(), TenantId = _tenant.Id, TemplateId = "tpl-1",
            FontesJson = """{"parte2.nome":"advogado.nome","foro.cidade":"inexistente"}""",
        });
        await _db.SaveChangesAsync();
        var v = Valor(await Controller(_advogado).Preparar(_contrato.Id, "tpl-1", CancellationToken.None)).Variaveis.ToDictionary(x => x.Chave);
        Assert.Equal(("advogado.nome", "Bruno Advogado"), (v["parte2.nome"].Fonte, v["parte2.nome"].Valor));
        Assert.Equal("cliente.cidade", v["foro.cidade"].Fonte);
    }

    [Fact]
    public async Task Preparar_ContratoDeOutroEscritorioEh404()
    {
        Conectar();
        TemplateNda();
        var r = await Controller(_advogado).Preparar(Guid.NewGuid(), "tpl-1", CancellationToken.None);
        Assert.Equal(404, Status(r));
    }

    [Fact]
    public void SignatariosSugeridos_SemEmailUsaWhatsApp()
    {
        var ctx = new ContextoAssinatura(new Contato { Nome = "João", Telefone = "11 91234-5678", Tipo = TipoPessoa.PJ },
            _contrato, null, _tenant, _admin, DateTime.Today);
        var s = AssinaturaDigitalService.SignatariosSugeridos(ctx, null);
        Assert.Equal(("whatsapp", "+55 11 91234-5678", null, "Contratante"), (s[0].Canal, s[0].Telefone, s[0].Cpf, s[0].Papel));
        Assert.Equal("Contratado", s[1].Papel);
        var semNada = AssinaturaDigitalService.SignatariosSugeridos(ctx with { Cliente = new Contato { Nome = "X" } }, []);
        Assert.Equal("email", semNada[0].Canal);
    }

    // ── Envio ─────────────────────────────────────────────────────────────────────────

    private CriarSolicitacaoAssinaturaDto Pedido(bool enviar = true, List<SignatarioEnvioDto>? sigs = null, DateOnly? prazo = null) =>
        new(_contrato.Id, "tpl-1", "  NDA: Maria  ",
            new() { ["parte1.nome"] = " Maria Cliente ", ["vazia"] = "  ", ["nula"] = null },
            new() { ["parte1.nome"] = "cliente.nome", ["parte2.nome"] = "inventada" },
            sigs ?? [new("Maria Cliente", "email", "maria@cliente.com", null, "529.982.247-25", "Contratante"),
                     new("Bruno", "whatsapp", null, "(11) 91234-5678", null, "Testemunha", "sign_as_witness")],
            EmOrdem: true, Prazo: prazo, Mensagem: "  Olá  ", Enviar: enviar);

    [Fact]
    public async Task Criar_MandaParaOContratosEGuardaASolicitacao()
    {
        Conectar();
        ContratosNovoContrato? enviado = null;
        string? idem = null;
        _api.Setup(a => a.CriarContratoAsync(Chave, It.IsAny<ContratosNovoContrato>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, ContratosNovoContrato, string, CancellationToken>((_, c, k, _) => { enviado = c; idem = k; })
            .ReturnsAsync(Ext("sent", T0, Sig("s1", "Maria Cliente", "pending"), Sig("s2", "Bruno", "pending")));

        var prazo = DateOnly.FromDateTime(DateTime.Today.AddDays(10));
        var dto = Valor(await Controller(_advogado).Criar(Pedido(prazo: prazo), CancellationToken.None));

        Assert.NotNull(enviado);
        Assert.Equal("NDA: Maria", enviado!.Title);
        Assert.Equal(new Dictionary<string, string> { ["parte1.nome"] = "Maria Cliente" }, enviado.Variables);
        Assert.Equal("sequential", enviado.SigningMode);
        Assert.Equal(prazo.ToString("yyyy-MM-dd"), enviado.ExpiresAt);
        Assert.Equal("Olá", enviado.Message);
        Assert.True(enviado.Send);
        Assert.Equal(("email", "52998224725", 1), (enviado.Signers[0].DeliveryMethod, enviado.Signers[0].Cpf, enviado.Signers[0].SignOrder));
        Assert.Equal(("whatsapp", "+55 11 91234-5678", null, "sign_as_witness"),
            (enviado.Signers[1].DeliveryMethod, enviado.Signers[1].Phone, enviado.Signers[1].Email, enviado.Signers[1].Action));

        var sol = await _db.SolicitacoesAssinatura.SingleAsync();
        Assert.Equal(sol.Id.ToString(), enviado.ExternalReference);
        Assert.Equal($"causify-{sol.Id}", idem);
        Assert.Equal((StatusSolicitacaoAssinatura.Enviada, "ext-1", 7, "NDA", _cliente.Id, _advogado.Id),
            (sol.Status, sol.ContratoExternoId, sol.NumeroExterno, sol.TemplateNome, sol.ContatoId, sol.CriadoPorId));
        Assert.Equal(StatusSolicitacaoAssinatura.Enviada, dto.Status);
        Assert.Equal("Bruno Advogado", dto.CriadoPor);
        Assert.Equal(2, dto.Signatarios.Count);

        var hist = await _db.HistoricosContratoHonorario.SingleAsync();
        Assert.Equal(EventoContratoHonorario.Assinatura, hist.TipoEvento);
        Assert.Contains("Enviado para assinatura", hist.Descricao);
        Assert.Contains("Maria Cliente, Bruno", hist.Descricao);

        var fontes = JsonSerializer.Deserialize<Dictionary<string, string>>((await _db.MapeamentosModeloAssinatura.SingleAsync()).FontesJson)!;
        Assert.Equal(new Dictionary<string, string> { ["parte1.nome"] = "cliente.nome" }, fontes);
        _audit.Verify(a => a.LogAsync(It.IsAny<AuditLogEntry>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Criar_RascunhoEMapeamentoExistenteAtualizado()
    {
        Conectar();
        _db.MapeamentosModeloAssinatura.Add(new MapeamentoModeloAssinatura { Id = Guid.NewGuid(), TenantId = _tenant.Id, TemplateId = "tpl-1", FontesJson = "{}" });
        await _db.SaveChangesAsync();
        _api.Setup(a => a.CriarContratoAsync(Chave, It.Is<ContratosNovoContrato>(c => !c.Send), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Ext("draft", T0, Sig("s1", "Maria Cliente", "pending")));
        var dto = Valor(await Controller(_advogado).Criar(Pedido(enviar: false), CancellationToken.None));
        Assert.Equal(StatusSolicitacaoAssinatura.Rascunho, dto.Status);
        Assert.Contains("Rascunho de assinatura criado", (await _db.HistoricosContratoHonorario.SingleAsync()).Descricao);
        Assert.Contains("cliente.nome", (await _db.MapeamentosModeloAssinatura.SingleAsync()).FontesJson);
    }

    [Fact]
    public async Task Criar_SemCotaGuardaORascunhoComOAviso()
    {
        Conectar();
        _api.Setup(a => a.CriarContratoAsync(Chave, It.IsAny<ContratosNovoContrato>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ContratosApiException(409, "quota_exceeded", "Acabaram os contratos do ciclo.", contratoId: "ext-1"));
        _api.Setup(a => a.ObterContratoAsync(Chave, "ext-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Ext("draft", T0, Sig("s1", "Maria Cliente", "pending")));
        var dto = Valor(await Controller(_advogado).Criar(Pedido(), CancellationToken.None));
        Assert.Equal(StatusSolicitacaoAssinatura.Rascunho, dto.Status);
        Assert.Equal("Acabaram os contratos do ciclo.", dto.Erro);
    }

    [Fact]
    public async Task Criar_ErroDeValidacaoDoContratosMostraOsCampos()
    {
        Conectar();
        _api.Setup(a => a.CriarContratoAsync(Chave, It.IsAny<ContratosNovoContrato>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ContratosApiException(400, "validation", "Confira os dados informados.",
                new Dictionary<string, string[]> { ["variables.valor"] = ["Valor: Informe um valor."] }));
        var r = await Controller(_advogado).Criar(Pedido(), CancellationToken.None);
        Assert.Equal(400, Status(r));
        Assert.Contains("Informe um valor", Mensagem(r.Result));
        Assert.False(await _db.SolicitacoesAssinatura.AnyAsync());
    }

    public static TheoryData<List<SignatarioEnvioDto>, string> SignatariosInvalidos => new()
    {
        { [], "pelo menos um" },
        { [new("", "email", "a@x.com", null, null, null)], "nome do 1º" },
        { [new("Ana", "fax", "a@x.com", null, null, null)], "Canal inválido" },
        { [new("Ana", "email", " ", null, null, null)], "e-mail de Ana" },
        { [new("Ana", "whatsapp", null, "123", null, null)], "celular de Ana" },
        { [new("Ana", "sms", null, null, null, null)], "celular de Ana" },
        { [new("Ana", "email", "a@x.com", null, "123", null)], "CPF de Ana" },
        { [new("Ana", "email", "a@x.com", null, null, null, "dançar")], "Ação inválida" },
        { Enumerable.Range(1, 31).Select(i => new SignatarioEnvioDto($"P{i}", "email", $"p{i}@x.com", null, null, null)).ToList(), "até 30" },
    };

    [Theory]
    [MemberData(nameof(SignatariosInvalidos))]
    public async Task Criar_ValidaOsSignatarios(List<SignatarioEnvioDto> sigs, string trecho)
    {
        Conectar();
        var r = await Controller(_advogado).Criar(Pedido(sigs: sigs), CancellationToken.None);
        Assert.Equal(400, Status(r));
        Assert.Contains(trecho, Mensagem(r.Result));
        _api.Verify(a => a.CriarContratoAsync(It.IsAny<string>(), It.IsAny<ContratosNovoContrato>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Criar_PrazoNoPassadoETemplateVazioSaoRecusados()
    {
        Conectar();
        var r = await Controller(_advogado).Criar(Pedido(prazo: DateOnly.FromDateTime(DateTime.Today.AddDays(-2))), CancellationToken.None);
        Assert.Contains("passado", Mensagem(r.Result));
        r = await Controller(_advogado).Criar(Pedido() with { TemplateId = " " }, CancellationToken.None);
        Assert.Contains("template", Mensagem(r.Result));
        Assert.Equal(403, Status(await Controller(papel: "Cliente").Criar(Pedido(), CancellationToken.None)));
    }

    // ── Ações sobre a solicitação ─────────────────────────────────────────────────────

    [Fact]
    public async Task Listar_TrazAsDoContratoComONomeDeQuemEnviou()
    {
        Solicitacao(sigs: [Sig("s1", "Maria", "signed"), Sig("s2", "Bruno", "pending")]);
        var lista = Valor(await Controller(_advogado).Solicitacoes(_contrato.Id, CancellationToken.None));
        var s = lista.Single();
        Assert.Equal(("Bruno Advogado", 1, 2), (s.CriadoPor, s.Assinados, s.Signatarios.Count));
        Assert.Empty(Valor(await Controller(_advogado).Solicitacoes(Guid.NewGuid(), CancellationToken.None)));
        Assert.Equal(403, Status(await Controller(papel: "Cliente").Solicitacoes(_contrato.Id, CancellationToken.None)));
    }

    [Fact]
    public async Task Atualizar_BuscaNoContratosEAvisaQuemAssinou()
    {
        Conectar();
        var sol = Solicitacao(sigs: [Sig("s1", "Maria", "pending"), Sig("s2", "Bruno", "pending")]);
        _api.Setup(a => a.ObterContratoAsync(Chave, "ext-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Ext("partially_signed", T0.AddMinutes(5), Sig("s1", "Maria", "signed"), Sig("s2", "Bruno", "pending")));
        var dto = Valor(await Controller(_advogado).Atualizar(sol.Id, CancellationToken.None));
        Assert.Equal((StatusSolicitacaoAssinatura.ParcialmenteAssinada, 1), (dto.Status, dto.Assinados));
        var n = await _db.Notificacoes.SingleAsync();
        Assert.Equal((_advogado.Id, TipoNotificacao.AssinaturaContrato, "Maria assinou o contrato"), (n.UsuarioId, n.Tipo, n.Titulo));
        Assert.Contains($"honorarios-contrato-detalhe.html?id={_contrato.Id}", n.Url);
        Assert.Contains("Maria assinou o contrato nº 7 (1/2)", (await _db.HistoricosContratoHonorario.SingleAsync()).Descricao);
        Assert.NotNull((await _db.SolicitacoesAssinatura.SingleAsync()).ConferidoEm);

        // A mesma resposta de novo não gera aviso em dobro.
        await Controller(_advogado).Atualizar(sol.Id, CancellationToken.None);
        Assert.Equal(1, await _db.Notificacoes.CountAsync());
    }

    [Fact]
    public async Task Atualizar_SolicitacaoDeOutroEscritorioEh404()
    {
        Conectar();
        var r = await Controller(_advogado).Atualizar(Guid.NewGuid(), CancellationToken.None);
        Assert.Equal(404, Status(r));
    }

    [Fact]
    public async Task EnviarRascunho_MandaComIdempotencyKey()
    {
        Conectar();
        var sol = Solicitacao(StatusSolicitacaoAssinatura.Rascunho, sigs: [Sig("s1", "Maria", "pending")]);
        _api.Setup(a => a.EnviarAsync(Chave, "ext-1", $"causify-envio-{sol.Id}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Ext("sent", T0.AddMinutes(1), Sig("s1", "Maria", "pending")));
        var dto = Valor(await Controller(_advogado).Enviar(sol.Id, CancellationToken.None));
        Assert.Equal(StatusSolicitacaoAssinatura.Enviada, dto.Status);
        Assert.Contains("enviado para assinatura", (await _db.HistoricosContratoHonorario.SingleAsync()).Descricao);

        var r = await Controller(_advogado).Enviar(sol.Id, CancellationToken.None);
        Assert.Equal(409, Status(r));
    }

    [Fact]
    public async Task EnviarRascunho_ErroDoContratos()
    {
        Conectar();
        var sol = Solicitacao(StatusSolicitacaoAssinatura.Rascunho);
        _api.Setup(a => a.EnviarAsync(Chave, "ext-1", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ContratosApiException(402, "payment_overdue", "Pagamento pendente."));
        var r = await Controller(_advogado).Enviar(sol.Id, CancellationToken.None);
        Assert.Equal(409, Status(r));
        Assert.Contains("Pagamento pendente", Mensagem(r.Result));
    }

    [Fact]
    public async Task Cancelar_RegistraNoHistoricoEAudita()
    {
        Conectar();
        var sol = Solicitacao(sigs: [Sig("s1", "Maria", "pending")]);
        _api.Setup(a => a.CancelarAsync(Chave, "ext-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Ext("cancelled", T0.AddMinutes(1), Sig("s1", "Maria", "pending")));
        var dto = Valor(await Controller(_advogado).Cancelar(sol.Id, CancellationToken.None));
        Assert.Equal(StatusSolicitacaoAssinatura.Cancelada, dto.Status);
        var h = await _db.HistoricosContratoHonorario.SingleAsync();
        Assert.Equal(_advogado.Id, h.UsuarioId);
        Assert.Contains("cancelada", h.Descricao);
        Assert.False(await _db.Notificacoes.AnyAsync());
        _audit.Verify(a => a.LogAsync(It.IsAny<AuditLogEntry>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Cancelar_JaFinalizadoAtualizaEAvisa()
    {
        Conectar();
        var sol = Solicitacao(sigs: [Sig("s1", "Maria", "pending")]);
        _api.Setup(a => a.CancelarAsync(Chave, "ext-1", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ContratosApiException(409, "already_final", "Este contrato já foi finalizado."));
        _api.Setup(a => a.ObterContratoAsync(Chave, "ext-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Ext("completed", T0.AddMinutes(1), Sig("s1", "Maria", "signed")));
        var r = await Controller(_advogado).Cancelar(sol.Id, CancellationToken.None);
        Assert.Equal(409, Status(r));
        Assert.Equal(StatusSolicitacaoAssinatura.Concluida, (await _db.SolicitacoesAssinatura.SingleAsync()).Status);

        _api.Setup(a => a.CancelarAsync(Chave, "ext-1", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ContratosApiException(500, null, "Erro interno"));
        Assert.Equal(502, Status(await Controller(_advogado).Cancelar(sol.Id, CancellationToken.None)));
    }

    [Fact]
    public async Task Reenviar_ChamaOContratos()
    {
        Conectar();
        var sol = Solicitacao();
        Assert.Equal(200, Status(await Controller(_advogado).Reenviar(sol.Id, "s1", CancellationToken.None)));
        _api.Verify(a => a.ReenviarLinkAsync(Chave, "ext-1", "s1", It.IsAny<CancellationToken>()), Times.Once);

        _api.Setup(a => a.ReenviarLinkAsync(Chave, "ext-1", "s2", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ContratosApiException(409, "cannot_resend", "O signatário já assinou."));
        var r = await Controller(_advogado).Reenviar(sol.Id, "s2", CancellationToken.None);
        Assert.Equal(409, Status(r));
        Assert.Equal(403, Status(await Controller(papel: "Cliente").Reenviar(sol.Id, "s1", CancellationToken.None)));
    }

    // ── Webhook ───────────────────────────────────────────────────────────────────────

    private static byte[] Evento(string id, string tipo, ContratosContrato? c, string? reason = null) =>
        JsonSerializer.SerializeToUtf8Bytes(new { id, type = tipo, createdAt = T0, data = new { contract = c, signer = (object?)null, reason } },
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

    private async Task<IActionResult> Webhook(Guid integracaoId, byte[] corpo, string? segredo = Segredo, DateTimeOffset? quando = null)
    {
        var c = Controller();
        c.ControllerContext.HttpContext = new DefaultHttpContext();
        c.Request.Body = new MemoryStream(corpo);
        if (segredo != null) c.Request.Headers[WebhookContratos.CabecalhoAssinatura] = WebhookContratos.Assinar(corpo, segredo, quando ?? DateTimeOffset.UtcNow);
        return await c.Webhook(integracaoId, CancellationToken.None);
    }

    [Fact]
    public async Task Webhook_RecusaAssinaturaErradaEIntegracaoDesconhecida()
    {
        var i = Conectar();
        var corpo = Evento("e1", "signer.signed", Ext("partially_signed", T0));
        Assert.IsType<NotFoundResult>(await Webhook(Guid.NewGuid(), corpo));
        Assert.Equal(400, Status(await Webhook(i.Id, corpo, "whsec_outro")));
        Assert.Equal(400, Status(await Webhook(i.Id, corpo, segredo: null)));
        Assert.Equal(400, Status(await Webhook(i.Id, corpo, quando: DateTimeOffset.UtcNow.AddMinutes(-10))));
        Assert.Equal(400, Status(await Webhook(i.Id, "não é json"u8.ToArray())));
        Assert.Equal(400, Status(await Webhook(i.Id, """{"id":"","type":"x"}"""u8.ToArray())));
        Assert.False(await _db.EventosAssinaturaRecebidos.AnyAsync());
    }

    [Fact]
    public async Task Webhook_SemSegredoCadastradoRecusa()
    {
        var i = Conectar(comSegredo: false);
        Assert.Equal(400, Status(await Webhook(i.Id, Evento("e1", "webhook.test", null))));
    }

    [Fact]
    public async Task Webhook_AssinaturaAtualizaAvisaEIgnoraRepeticao()
    {
        var i = Conectar();
        Solicitacao(sigs: [Sig("s1", "Maria", "pending"), Sig("s2", "Bruno", "pending")]);
        var corpo = Evento("e1", "signer.signed", Ext("partially_signed", T0.AddMinutes(5), Sig("s1", "Maria", "signed"), Sig("s2", "Bruno", "viewed")));

        var r = await Webhook(i.Id, corpo);
        Assert.Contains("Processado", Mensagem(r));
        var sol = await _db.SolicitacoesAssinatura.SingleAsync();
        Assert.Equal(StatusSolicitacaoAssinatura.ParcialmenteAssinada, sol.Status);
        Assert.Equal(1, await _db.Notificacoes.CountAsync());
        Assert.NotNull((await _db.IntegracoesAssinatura.SingleAsync()).UltimoWebhookEm);

        Assert.Contains("Repetido", Mensagem(await Webhook(i.Id, corpo)));
        Assert.Equal(1, await _db.Notificacoes.CountAsync());
        Assert.Equal(1, await _db.EventosAssinaturaRecebidos.CountAsync());
    }

    [Fact]
    public async Task Webhook_EventoAntigoQueChegaDepoisNaoVoltaOEstado()
    {
        var i = Conectar();
        Solicitacao(StatusSolicitacaoAssinatura.Concluida, T0.AddMinutes(10), Sig("s1", "Maria", "signed"));
        await Webhook(i.Id, Evento("e-velho", "signer.viewed", Ext("sent", T0.AddMinutes(2), Sig("s1", "Maria", "viewed"))));
        var sol = await _db.SolicitacoesAssinatura.SingleAsync();
        Assert.Equal(StatusSolicitacaoAssinatura.Concluida, sol.Status);
        Assert.False(await _db.Notificacoes.AnyAsync());
    }

    [Fact]
    public async Task Webhook_ConclusaoAvisaEAgendaODownloadDoPdf()
    {
        var i = Conectar();
        Solicitacao(sigs: [Sig("s1", "Maria", "signed"), Sig("s2", "Bruno", "pending")]);
        var concluido = Ext("completed", T0.AddMinutes(5), Sig("s1", "Maria", "signed"), Sig("s2", "Bruno", "signed"));
        await Webhook(i.Id, Evento("e1", "contract.completed", concluido with { HasSignedDocument = false }));

        var titulos = await _db.Notificacoes.Select(n => n.Titulo).ToListAsync();
        Assert.Equal(["Contrato assinado por todos"], titulos);
        _jobs.Verify(j => j.Create(It.IsAny<Job>(), It.IsAny<IState>()), Times.Never);

        await Webhook(i.Id, Evento("e2", "contract.signed_document_ready", concluido with { HasSignedDocument = false }));
        _jobs.Verify(j => j.Create(
            It.Is<Job>(job => job.Type == typeof(AssinaturaDigitalService) && job.Method.Name == nameof(AssinaturaDigitalService.BaixarAssinadoAsync)),
            It.IsAny<EnqueuedState>()), Times.Once);
    }

    [Fact]
    public async Task Webhook_RecusaEFalhaDeEntregaEFalhaDeEnvio()
    {
        var i = Conectar();
        Solicitacao(sigs: [Sig("s1", "Maria", "pending"), Sig("s2", "Bruno", "pending")]);

        var falhaEntrega = Ext("sent", T0.AddMinutes(1), Sig("s1", "Maria", "pending"), Sig("s2", "Bruno", "delivery_failed"));
        await Webhook(i.Id, Evento("e1", "signer.delivery_failed", falhaEntrega));
        var recusa = Ext("declined", T0.AddMinutes(2), Sig("s1", "Maria", "declined", "Valor errado"), Sig("s2", "Bruno", "delivery_failed"));
        await Webhook(i.Id, Evento("e2", "contract.declined", recusa, reason: "Valor errado na cláusula 2"));
        var falhaEnvio = recusa with { Status = "sent", UpdatedAt = T0.AddMinutes(3), Signers = [Sig("s1", "Maria", "pending")] };
        await Webhook(i.Id, Evento("e3", "contract.send_failed", falhaEnvio, reason: "E-mail recusado pelo provedor"));

        var titulos = await _db.Notificacoes.OrderBy(n => n.CriadaEm).Select(n => n.Titulo).ToListAsync();
        Assert.Contains("O link não chegou a Bruno", titulos);
        Assert.Contains("Maria recusou o contrato", titulos);
        Assert.Contains("Falha ao enviar o contrato para assinatura", titulos);
        var sol = await _db.SolicitacoesAssinatura.SingleAsync();
        Assert.Equal("Valor errado na cláusula 2", sol.MotivoRecusa);
        Assert.Equal("E-mail recusado pelo provedor", sol.Erro);
        Assert.Contains(await _db.HistoricosContratoHonorario.Select(h => h.Descricao).ToListAsync(), d => d.Contains("Motivo: Valor errado"));
    }

    [Fact]
    public async Task Webhook_ContratoQueNaoVeioDoCausifyEhIgnorado()
    {
        var i = Conectar();
        var r = await Webhook(i.Id, Evento("e1", "contract.created", Ext("draft", T0) with { Id = "outro" }));
        Assert.Contains("Ignorado", Mensagem(r));
        Assert.Contains("Ignorado", Mensagem(await Webhook(i.Id, Evento("e2", "webhook.test", null))));
        Assert.Equal(2, await _db.EventosAssinaturaRecebidos.CountAsync());
    }

    [Fact]
    public async Task Webhook_ExpiradoAvisa()
    {
        var i = Conectar();
        Solicitacao(sigs: [Sig("s1", "Maria", "pending")]);
        await Webhook(i.Id, Evento("e1", "contract.expired", Ext("expired", T0.AddMinutes(1), Sig("s1", "Maria", "pending"))));
        Assert.Equal("Prazo de assinatura encerrado", (await _db.Notificacoes.SingleAsync()).Titulo);
    }

    // ── PDF assinado e conferência ────────────────────────────────────────────────────

    [Fact]
    public async Task BaixarAssinado_GuardaComoDocumentoDoContrato()
    {
        Conectar();
        var sol = Solicitacao(StatusSolicitacaoAssinatura.Concluida);
        _api.Setup(a => a.BaixarAssinadoAsync(Chave, "ext-1", It.IsAny<CancellationToken>())).ReturnsAsync("%PDF-1.4 conteúdo"u8.ToArray());
        string? chave = null;
        _storage.Setup(s => s.UploadAsync(It.IsAny<Stream>(), It.IsAny<string>(), "application/pdf", It.IsAny<CancellationToken>()))
            .Callback<Stream, string, string, CancellationToken>((_, k, _, _) => chave = k).ReturnsAsync("ok");

        await Service().BaixarAssinadoAsync(sol.Id);

        var doc = await _db.Documentos.SingleAsync();
        Assert.Equal((TipoDocumento.Contrato, _cliente.Id, _contrato.Id, "NDA: Maria Cliente (assinado).pdf", "application/pdf"),
            (doc.Tipo, doc.ClienteId, doc.ContratoId, doc.Nome, doc.ContentType));
        Assert.Equal(chave, doc.ObjectKey);
        Assert.StartsWith($"{_tenant.Id}/contratos/{_contrato.Id}/", chave);
        Assert.Equal(doc.Id, (await _db.SolicitacoesAssinatura.SingleAsync()).DocumentoAssinadoId);
        Assert.Contains("PDF assinado", (await _db.HistoricosContratoHonorario.SingleAsync()).Descricao);

        await Service().BaixarAssinadoAsync(sol.Id);
        _api.Verify(a => a.BaixarAssinadoAsync(Chave, "ext-1", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task BaixarAssinado_AindaNaoDisponivelOuNaoEhPdf()
    {
        Conectar();
        var sol = Solicitacao(StatusSolicitacaoAssinatura.Concluida);
        _api.Setup(a => a.BaixarAssinadoAsync(Chave, "ext-1", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ContratosApiException(404, "not_found", "O PDF assinado ainda não está disponível."));
        await Service().BaixarAssinadoAsync(sol.Id);
        Assert.False(await _db.Documentos.AnyAsync());

        _api.Setup(a => a.BaixarAssinadoAsync(Chave, "ext-1", It.IsAny<CancellationToken>())).ReturnsAsync("<html>"u8.ToArray());
        await Assert.ThrowsAsync<InvalidOperationException>(() => Service().BaixarAssinadoAsync(sol.Id));
        _storage.Verify(s => s.UploadAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);

        await Service().BaixarAssinadoAsync(Guid.NewGuid());
        _db.IntegracoesAssinatura.RemoveRange(_db.IntegracoesAssinatura);
        await _db.SaveChangesAsync();
        await Service().BaixarAssinadoAsync(sol.Id);
    }

    [Fact]
    public async Task ConferirPendentes_AtualizaOsAbertosBaixaPdfEApagaEventosAntigos()
    {
        Conectar();
        var aberta = Solicitacao(sigs: [Sig("s1", "Maria", "pending")]);
        var recente = Solicitacao(sigs: [Sig("s1", "Maria", "pending")]);
        recente.ContratoExternoId = "ext-2";
        recente.ConferidoEm = DateTime.UtcNow.AddMinutes(-5);
        var cancelada = Solicitacao(StatusSolicitacaoAssinatura.Cancelada);
        cancelada.ContratoExternoId = "ext-3";
        _db.EventosAssinaturaRecebidos.AddRange(
            new EventoAssinaturaRecebido { Id = "velho", TenantId = _tenant.Id, Tipo = "x", RecebidoEm = DateTime.UtcNow.AddDays(-40) },
            new EventoAssinaturaRecebido { Id = "novo", TenantId = _tenant.Id, Tipo = "x", RecebidoEm = DateTime.UtcNow });
        await _db.SaveChangesAsync();

        _api.Setup(a => a.ObterContratoAsync(Chave, "ext-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Ext("completed", T0.AddMinutes(5), Sig("s1", "Maria", "signed")));
        _api.Setup(a => a.BaixarAssinadoAsync(Chave, "ext-1", It.IsAny<CancellationToken>())).ReturnsAsync("%PDF-1.7"u8.ToArray());
        _storage.Setup(s => s.UploadAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync("ok");

        Assert.Equal(1, await Service().ConferirPendentesAsync());

        var sol = await _db.SolicitacoesAssinatura.SingleAsync(s => s.Id == aberta.Id);
        Assert.Equal(StatusSolicitacaoAssinatura.Concluida, sol.Status);
        Assert.NotNull(sol.DocumentoAssinadoId);
        Assert.Equal("Contrato assinado por todos", (await _db.Notificacoes.SingleAsync()).Titulo);
        _api.Verify(a => a.ObterContratoAsync(Chave, "ext-2", It.IsAny<CancellationToken>()), Times.Never);
        _api.Verify(a => a.ObterContratoAsync(Chave, "ext-3", It.IsAny<CancellationToken>()), Times.Never);
        Assert.Equal(["novo"], await _db.EventosAssinaturaRecebidos.Select(e => e.Id).ToListAsync());
    }

    [Fact]
    public async Task ConferirPendentes_ChaveRevogadaParaOEscritorioEErroAvulsoSegue()
    {
        Conectar();
        var a = Solicitacao();
        var b = Solicitacao();
        b.ContratoExternoId = "ext-2";
        await _db.SaveChangesAsync();
        _api.Setup(x => x.ObterContratoAsync(Chave, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ContratosApiException(401, "unauthorized", "Chave revogada"));
        Assert.Equal(0, await Service().ConferirPendentesAsync());
        _api.Verify(x => x.ObterContratoAsync(Chave, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);

        _api.Setup(x => x.ObterContratoAsync(Chave, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ContratosApiException(404, "not_found", "Contrato não encontrado."));
        Assert.Equal(0, await Service().ConferirPendentesAsync());
        Assert.All(await _db.SolicitacoesAssinatura.ToListAsync(), s => Assert.NotNull(s.ConferidoEm));
    }

    [Fact]
    public async Task ConferirPendentes_SemIntegracaoNaoChamaAApi()
    {
        Solicitacao();
        Assert.Equal(0, await Service().ConferirPendentesAsync());
        _api.VerifyNoOtherCalls();
    }

    // ── Estado ────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("draft", StatusSolicitacaoAssinatura.Rascunho)]
    [InlineData("sent", StatusSolicitacaoAssinatura.Enviada)]
    [InlineData("partially_signed", StatusSolicitacaoAssinatura.ParcialmenteAssinada)]
    [InlineData("completed", StatusSolicitacaoAssinatura.Concluida)]
    [InlineData("declined", StatusSolicitacaoAssinatura.Recusada)]
    [InlineData("expired", StatusSolicitacaoAssinatura.Expirada)]
    [InlineData("cancelled", StatusSolicitacaoAssinatura.Cancelada)]
    [InlineData("novo_status", StatusSolicitacaoAssinatura.Enviada)]
    public void MapearStatus(string status, StatusSolicitacaoAssinatura esperado) =>
        Assert.Equal(esperado, AssinaturaDigitalService.MapearStatus(status));

    [Fact]
    public void Aplicar_CopiaOEstadoENormalizaOCanal()
    {
        var sol = new SolicitacaoAssinatura { Titulo = "x", Erro = "antigo" };
        var c = Ext("sent", T0, Sig("s1", "Maria", "pending") with { DeliveryMethod = "whats_app", Phone = "+55 11 90000-0000" })
            with { ExpiresAt = T0.AddDays(5) };
        Assert.True(AssinaturaDigitalService.Aplicar(sol, c));
        Assert.Null(sol.Erro);
        Assert.Equal(T0.AddDays(5).UtcDateTime, sol.ExpiraEm);
        var sig = AssinaturaDigitalService.LerSignatarios(sol.SignatariosJson).Single();
        Assert.Equal(("whatsapp", "+55 11 90000-0000"), (sig.Canal, sig.Telefone));

        Assert.False(AssinaturaDigitalService.Aplicar(sol, c with { Status = "draft", UpdatedAt = T0.AddMinutes(-1) }));
        Assert.Equal(StatusSolicitacaoAssinatura.Enviada, sol.Status);

        AssinaturaDigitalService.Aplicar(sol, c with { UpdatedAt = T0.AddMinutes(1), SendError = "Telefone inválido" });
        Assert.Equal("Telefone inválido", sol.Erro);
        Assert.Empty(AssinaturaDigitalService.LerSignatarios("não é json"));
        Assert.Empty(AssinaturaDigitalService.LerSignatarios(null));
    }

    [Theory]
    [InlineData(400, 400)]
    [InlineData(404, 404)]
    [InlineData(409, 409)]
    [InlineData(429, 429)]
    [InlineData(401, 409)]
    [InlineData(402, 409)]
    [InlineData(403, 409)]
    [InlineData(500, 502)]
    [InlineData(503, 502)]
    public void Traduzir_StatusParaOUsuario(int api, int esperado) =>
        Assert.Equal(esperado, AssinaturaDigitalService.Traduzir(new ContratosApiException(api, null, "m")).Status);
}
