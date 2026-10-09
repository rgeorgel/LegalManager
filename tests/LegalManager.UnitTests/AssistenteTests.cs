using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using LegalManager.API.Controllers;
using LegalManager.Application.DTOs.Assistente;
using LegalManager.Application.Interfaces;
using LegalManager.Domain.Entities;
using LegalManager.Domain.Enums;
using LegalManager.Domain.Interfaces;
using LegalManager.Infrastructure.Assistente;
using LegalManager.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace LegalManager.UnitTests;

public class AssistenteTests
{
    private static AppDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static ITenantContext Tenant(Guid tenantId, PlanoTipo plano, Guid? userId = null, Guid? impersonadoPor = null)
    {
        var t = new Mock<ITenantContext>();
        t.Setup(x => x.ImpersonadoPorId).Returns(impersonadoPor);
        t.Setup(x => x.TenantId).Returns(tenantId);
        t.Setup(x => x.UserId).Returns(userId ?? Guid.NewGuid());
        t.Setup(x => x.UserRole).Returns("Admin");
        t.Setup(x => x.Plano).Returns(plano);
        return t.Object;
    }

    private static AssistenteFerramentas Ferramentas(AppDbContext db, ITenantContext tenant) =>
        new(db, tenant, new LegalManager.Infrastructure.Services.HonorarioService(db, Mock.Of<IAuditService>()));

    private static JsonElement Args(object o) => JsonSerializer.SerializeToElement(o);

    private static (AppDbContext db, Guid tenantA, Guid tenantB) SeedDoisTenants()
    {
        var db = CreateContext();
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        db.Tenants.AddRange(new Tenant { Id = a, Nome = "Escritório A" }, new Tenant { Id = b, Nome = "Escritório B" });
        db.Processos.AddRange(
            new Processo { Id = Guid.NewGuid(), TenantId = a, NumeroCNJ = "0000001-11.2024.8.26.0100", TipoAcao = "Cobrança", CriadoEm = DateTime.UtcNow },
            new Processo { Id = Guid.NewGuid(), TenantId = b, NumeroCNJ = "0000002-22.2024.8.26.0100", TipoAcao = "Cobrança", CriadoEm = DateTime.UtcNow });
        db.LancamentosFinanceiros.AddRange(
            new LancamentoFinanceiro { Id = Guid.NewGuid(), TenantId = a, Tipo = TipoLancamento.Receita, Categoria = "Honorários", Valor = 100, DataVencimento = DateTime.UtcNow.Date },
            new LancamentoFinanceiro { Id = Guid.NewGuid(), TenantId = b, Tipo = TipoLancamento.Receita, Categoria = "Honorários", Valor = 999, DataVencimento = DateTime.UtcNow.Date });
        db.SaveChanges();
        return (db, a, b);
    }

    // ── Ferramentas: isolamento e plano ──────────────────────────────────────

    [Fact]
    public async Task BuscarProcessos_RetornaSomenteDoTenantDaSessao()
    {
        var (db, a, _) = SeedDoisTenants();
        var f = Ferramentas(db, Tenant(a, PlanoTipo.Pro));

        var (json, erro) = await f.ExecutarAsync("buscar_processos", Args(new { texto = "cobrança" }), default);

        Assert.False(erro);
        Assert.Contains("0000001-11.2024.8.26.0100", json);
        Assert.DoesNotContain("0000002-22.2024.8.26.0100", json);
        Assert.Equal(1, JsonDocument.Parse(json).RootElement.GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task BuscarProcessos_AceitaNumeroSoComDigitos()
    {
        var (db, a, _) = SeedDoisTenants();
        var f = Ferramentas(db, Tenant(a, PlanoTipo.Pro));

        var (json, _) = await f.ExecutarAsync("buscar_processos", Args(new { texto = "00000011120248260100" }), default);

        Assert.Equal(1, JsonDocument.Parse(json).RootElement.GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task DetalharProcesso_DeOutroTenant_NaoEncontra()
    {
        var (db, a, b) = SeedDoisTenants();
        var idB = db.Processos.Single(p => p.TenantId == b).Id;
        var f = Ferramentas(db, Tenant(a, PlanoTipo.Pro));

        var (json, _) = await f.ExecutarAsync("detalhar_processo", Args(new { processo_id = idB.ToString() }), default);

        Assert.Contains("não encontrado", json);
    }

    [Fact]
    public async Task ResumoFinanceiro_SomaSomenteDoTenant()
    {
        var (db, a, _) = SeedDoisTenants();
        var f = Ferramentas(db, Tenant(a, PlanoTipo.Pro));

        var (json, erro) = await f.ExecutarAsync("resumo_financeiro", Args(new { }), default);

        Assert.False(erro);
        Assert.Equal(100m, JsonDocument.Parse(json).RootElement.GetProperty("aReceber").GetDecimal());
    }

    private static void SeedContatosComTags(AppDbContext db, Guid tenantId)
    {
        Contato Novo(string nome, params string[] tags)
        {
            var c = new Contato { Id = Guid.NewGuid(), TenantId = tenantId, Nome = nome, TipoContato = TipoContato.Cliente };
            c.Tags = tags.Select(t => new ContatoTag { Id = Guid.NewGuid(), ContatoId = c.Id, Tag = t }).ToList();
            return c;
        }
        db.Contatos.AddRange(
            Novo("Marta Ferreira Dias", "BPC LOAS"),
            Novo("Wesley Gularte Ferreira", "BPC LOAS"),
            Novo("Maria Fernanda Vargas", "PENSÃO POR MORTE", "BPC LOAS"),
            Novo("Aleir Prestes", "benefício por incapacidade"));
        db.SaveChanges();
    }

    [Fact]
    public async Task BuscarContatos_PorParteDaTag_IgnoraMaiusculasEAcentos()
    {
        var (db, a, b) = SeedDoisTenants();
        SeedContatosComTags(db, a);
        SeedContatosComTags(db, b); // mesmas tags em outro tenant não podem aparecer
        var f = Ferramentas(db, Tenant(a, PlanoTipo.Pro));

        var (json, erro) = await f.ExecutarAsync("buscar_contatos", Args(new { tags = new[] { "bpc" } }), default);
        var (json2, _) = await f.ExecutarAsync("buscar_contatos", Args(new { tags = "pensao" }), default);

        Assert.False(erro);
        Assert.Equal(3, JsonDocument.Parse(json).RootElement.GetProperty("total").GetInt32());
        Assert.Equal(1, JsonDocument.Parse(json2).RootElement.GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task BuscarContatos_TagInexistente_InformaTagsExistentes()
    {
        var (db, a, _) = SeedDoisTenants();
        SeedContatosComTags(db, a);
        var f = Ferramentas(db, Tenant(a, PlanoTipo.Pro));

        var (json, _) = await f.ExecutarAsync("buscar_contatos", Args(new { tags = new[] { "trabalhista" } }), default);

        var root = JsonDocument.Parse(json).RootElement;
        Assert.Equal(0, root.GetProperty("total").GetInt32());
        Assert.Contains("BPC LOAS", root.GetProperty("tagsExistentes").EnumerateArray().Select(t => t.GetString()));
    }

    [Fact]
    public async Task ListarTagsContatos_ContaPorTag()
    {
        var (db, a, b) = SeedDoisTenants();
        SeedContatosComTags(db, a);
        SeedContatosComTags(db, b);
        var f = Ferramentas(db, Tenant(a, PlanoTipo.Pro));

        var (json, _) = await f.ExecutarAsync("listar_tags_contatos", Args(new { }), default);

        var bpc = JsonDocument.Parse(json).RootElement.GetProperty("tags").EnumerateArray()
            .Single(t => t.GetProperty("tag").GetString() == "BPC LOAS");
        Assert.Equal(3, bpc.GetProperty("contatos").GetInt32());
    }

    [Fact]
    public void Disponiveis_RespeitaPlano()
    {
        var db = CreateContext();
        var free = Ferramentas(db, Tenant(Guid.NewGuid(), PlanoTipo.Free)).Disponiveis().Select(x => x.Nome).ToList();
        var plus = Ferramentas(db, Tenant(Guid.NewGuid(), PlanoTipo.Plus)).Disponiveis().Select(x => x.Nome).ToList();
        var pro = Ferramentas(db, Tenant(Guid.NewGuid(), PlanoTipo.Pro)).Disponiveis().Select(x => x.Nome).ToList();

        Assert.DoesNotContain("resumo_financeiro", free);
        Assert.DoesNotContain("listar_contratos_honorario", free);
        Assert.Contains("resumo_financeiro", plus);
        Assert.Contains("listar_contratos_honorario", plus);
        Assert.DoesNotContain("listar_publicacoes", plus);
        Assert.Contains("listar_publicacoes", pro);
        Assert.Contains("buscar_processos", free);
    }

    [Fact]
    public async Task Executar_FerramentaForaDoPlano_Recusa()
    {
        var (db, a, _) = SeedDoisTenants();
        var f = Ferramentas(db, Tenant(a, PlanoTipo.Plus));

        var (json, erro) = await f.ExecutarAsync("listar_publicacoes", Args(new { }), default);

        Assert.True(erro);
        Assert.Contains("indisponível", json);
    }

    [Fact]
    public async Task Executar_ArgumentoInvalido_RetornaErroParaOModelo()
    {
        var (db, a, _) = SeedDoisTenants();
        var f = Ferramentas(db, Tenant(a, PlanoTipo.Pro));

        var (json, erro) = await f.ExecutarAsync("listar_tarefas", Args(new { prazo_de = "31/12/2026" }), default);

        Assert.True(erro);
        Assert.Contains("AAAA-MM-DD", json);
    }

    private static Guid SeedContratoComParcelaVencida(AppDbContext db, Guid tenantId)
    {
        var contato = new Contato { Id = Guid.NewGuid(), TenantId = tenantId, Nome = "Emerson Henriques" };
        var contrato = new ContratoHonorario
        {
            Id = Guid.NewGuid(), TenantId = tenantId, ContatoId = contato.Id, NumeroContrato = "HON-2026/0002",
            ValorTotal = 2000, Status = StatusContratoHonorario.Ativo, DataInicio = DateTime.UtcNow.AddMonths(-3)
        };
        db.Contatos.Add(contato);
        db.ContratosHonorarios.Add(contrato);
        // Status gravado "Pendente", mas com vencimento passado — é assim que a tela mostra atraso.
        db.ParcelasHonorarios.AddRange(
            new ParcelaHonorario { Id = Guid.NewGuid(), TenantId = tenantId, ContratoId = contrato.Id, Numero = 1, ValorOriginal = 1000,
                Vencimento = DateTime.UtcNow.Date.AddDays(-40), Status = StatusParcelaHonorario.Pendente },
            new ParcelaHonorario { Id = Guid.NewGuid(), TenantId = tenantId, ContratoId = contrato.Id, Numero = 2, ValorOriginal = 1000,
                Vencimento = DateTime.UtcNow.Date.AddDays(20), Status = StatusParcelaHonorario.Pendente });
        db.SaveChanges();
        return contrato.Id;
    }

    [Fact]
    public async Task ListarContratos_ParcelaPendenteVencida_ContaComoEmAtraso()
    {
        var (db, a, _) = SeedDoisTenants();
        SeedContratoComParcelaVencida(db, a);
        var f = Ferramentas(db, Tenant(a, PlanoTipo.Pro));

        var (json, erro) = await f.ExecutarAsync("listar_contratos_honorario", Args(new { em_atraso = true }), default);

        Assert.False(erro);
        var root = JsonDocument.Parse(json).RootElement;
        Assert.Equal(1, root.GetProperty("total").GetInt32());
        var c = root.GetProperty("contratos")[0];
        Assert.Equal(1, c.GetProperty("parcelasEmAtraso").GetInt32());
        Assert.True(c.GetProperty("valorEmAtrasoComMultaEJuros").GetDecimal() > 1000m);
        Assert.Equal(1000m, c.GetProperty("valorAVencer").GetDecimal());
    }

    [Fact]
    public async Task ResumoHonorarios_ListaClientesEmAtraso()
    {
        var (db, a, _) = SeedDoisTenants();
        SeedContratoComParcelaVencida(db, a);
        var f = Ferramentas(db, Tenant(a, PlanoTipo.Pro));

        var (json, erro) = await f.ExecutarAsync("resumo_honorarios", Args(new { }), default);

        Assert.False(erro);
        var root = JsonDocument.Parse(json).RootElement;
        Assert.Equal(1, root.GetProperty("contratosEmAtraso").GetInt32());
        Assert.Equal("Emerson Henriques", root.GetProperty("clientesEmAtraso")[0].GetProperty("cliente").GetString());
    }

    // ── Serviço: laço de ferramentas ─────────────────────────────────────────

    private sealed class LlmFalso(params LlmResposta[] respostas) : IAssistenteLlmClient
    {
        private int _i;
        public List<IReadOnlyList<LlmMensagem>> Chamadas { get; } = [];
        public string Provedor => "Teste";
        public string Modelo => "modelo-teste";

        public Task<LlmResposta> EnviarAsync(string sistema, IReadOnlyList<LlmMensagem> mensagens,
            IReadOnlyList<LlmFerramenta> ferramentas, CancellationToken ct = default)
        {
            Chamadas.Add(mensagens.ToList());
            return Task.FromResult(respostas[Math.Min(_i++, respostas.Length - 1)]);
        }
    }

    [Fact]
    public async Task Perguntar_ExecutaFerramentaEDevolveTextoFinal()
    {
        var (db, a, _) = SeedDoisTenants();
        var tenant = Tenant(a, PlanoTipo.Pro);
        var llm = new LlmFalso(
            new LlmResposta([new LlmChamadaFerramenta("c1", "buscar_processos", Args(new { texto = "cobrança" }))], 10, 5),
            new LlmResposta([new LlmTexto("Você tem 1 processo de cobrança.")], 20, 8));
        var svc = new AssistenteService(llm, Ferramentas(db, tenant), db, tenant,
            NullLogger<AssistenteService>.Instance);

        var r = await svc.PerguntarAsync(new AssistentePerguntaDto([new("user", "Quantos processos de cobrança?")]));

        Assert.Equal("Você tem 1 processo de cobrança.", r.Resposta);
        Assert.Equal(["buscar_processos"], r.FerramentasUsadas);
        var resultado = llm.Chamadas[1][^1].Blocos.OfType<LlmResultadoFerramenta>().Single();
        Assert.Equal("c1", resultado.ChamadaId);
        Assert.Contains("0000001-11.2024.8.26.0100", resultado.Conteudo);
    }

    [Fact]
    public async Task Perguntar_RegistraInteracaoComFerramentasParaAuditoria()
    {
        var (db, a, _) = SeedDoisTenants();
        var tenant = Tenant(a, PlanoTipo.Pro);
        var llm = new LlmFalso(
            new LlmResposta([new LlmChamadaFerramenta("c1", "buscar_processos", Args(new { texto = "cobrança" }))], 10, 5),
            new LlmResposta([new LlmTexto("Você tem 1 processo.")], 20, 8));
        var svc = new AssistenteService(llm, Ferramentas(db, tenant), db, tenant, NullLogger<AssistenteService>.Instance);
        var conversa = Guid.NewGuid();

        var r = await svc.PerguntarAsync(new AssistentePerguntaDto([new("user", "Quantos processos?")], conversa));

        Assert.Equal(conversa, r.ConversaId);
        var i = await db.InteracoesAssistente.SingleAsync();
        Assert.Equal(a, i.TenantId);
        Assert.Equal(conversa, i.ConversaId);
        Assert.Equal("Quantos processos?", i.Pergunta);
        Assert.Equal("Você tem 1 processo.", i.Resposta);
        Assert.True(i.Sucesso);
        Assert.Equal(1, i.QuantidadeFerramentas);
        Assert.Equal(30, i.TokensEntrada);
        Assert.Equal("modelo-teste", i.Modelo);
        var ferramenta = JsonDocument.Parse(i.FerramentasJson!).RootElement[0];
        Assert.Equal("buscar_processos", ferramenta.GetProperty("nome").GetString());
        Assert.Equal("cobrança", ferramenta.GetProperty("argumentos").GetProperty("texto").GetString());
        Assert.Contains("0000001-11.2024.8.26.0100", ferramenta.GetProperty("resultado").GetString());
    }

    private sealed class LlmComErro : IAssistenteLlmClient
    {
        public string Provedor => "Teste";
        public string Modelo => "modelo-teste";
        public Task<LlmResposta> EnviarAsync(string sistema, IReadOnlyList<LlmMensagem> mensagens,
            IReadOnlyList<LlmFerramenta> ferramentas, CancellationToken ct = default) =>
            throw new AssistenteIndisponivelException("O provedor de IA respondeu 500.");
    }

    [Fact]
    public async Task Perguntar_ErroDoProvedor_RegistraFalhaERelanca()
    {
        var (db, a, _) = SeedDoisTenants();
        var tenant = Tenant(a, PlanoTipo.Pro);
        var svc = new AssistenteService(new LlmComErro(), Ferramentas(db, tenant), db, tenant, NullLogger<AssistenteService>.Instance);

        await Assert.ThrowsAsync<AssistenteIndisponivelException>(() =>
            svc.PerguntarAsync(new AssistentePerguntaDto([new("user", "oi")])));

        var i = await db.InteracoesAssistente.SingleAsync();
        Assert.False(i.Sucesso);
        Assert.Contains("500", i.MensagemErro);
        Assert.Equal("oi", i.Pergunta);
    }

    [Fact]
    public async Task SuperAdmin_ListaFiltraPorTenantEMostraConversa()
    {
        var db = CreateContext();
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var conversa = Guid.NewGuid();
        db.Tenants.AddRange(new Tenant { Id = a, Nome = "Escritório A" }, new Tenant { Id = b, Nome = "Escritório B" });
        InteracaoAssistente Nova(Guid tenantId, Guid conversaId, string pergunta, int minutos, bool ok = true) => new()
        {
            Id = Guid.NewGuid(), TenantId = tenantId, UsuarioId = Guid.NewGuid(), ConversaId = conversaId,
            CriadoEm = DateTime.UtcNow.AddMinutes(-minutos), Pergunta = pergunta, Resposta = ok ? "resp" : null,
            Sucesso = ok, MensagemErro = ok ? null : "falha", Provedor = "Teste", Modelo = "m", TokensEntrada = 10, TokensSaida = 2
        };
        db.InteracoesAssistente.AddRange(
            Nova(a, conversa, "primeira", 10), Nova(a, conversa, "segunda", 5), Nova(b, Guid.NewGuid(), "outra", 1, ok: false));
        db.SaveChanges();
        var controller = new SuperAdminAssistenteController(db);

        var lista = Assert.IsType<InteracaoAssistentePagedResultDto>(
            Assert.IsType<OkObjectResult>((await controller.Listar(a, null, null, null, null, null, null)).Result).Value);
        Assert.Equal(2, lista.Total);
        Assert.All(lista.Items, i => Assert.Equal("Escritório A", i.TenantNome));

        var conv = Assert.IsType<List<InteracaoAssistenteDetalheDto>>(
            Assert.IsType<OkObjectResult>((await controller.Conversa(conversa, default)).Result).Value);
        Assert.Equal(["primeira", "segunda"], conv.Select(c => c.Pergunta));

        var resumo = Assert.IsType<List<AssistenteResumoTenantDto>>(
            Assert.IsType<OkObjectResult>((await controller.Resumo(null, null, null, default)).Result).Value);
        var ra = resumo.Single(r => r.TenantId == a);
        Assert.Equal(2, ra.Perguntas);
        Assert.Equal(1, ra.Conversas);
        Assert.Equal(1, resumo.Single(r => r.TenantId == b).Erros);
    }

    // ── Histórico do usuário ─────────────────────────────────────────────────

    private static InteracaoAssistente Interacao(Guid tenantId, Guid usuarioId, Guid conversaId, string pergunta,
        int minutosAtras, Guid? impersonadoPor = null) => new()
    {
        Id = Guid.NewGuid(), TenantId = tenantId, UsuarioId = usuarioId, ImpersonadoPorId = impersonadoPor,
        ConversaId = conversaId, CriadoEm = DateTime.UtcNow.AddMinutes(-minutosAtras), Pergunta = pergunta,
        Resposta = "resp " + pergunta, Sucesso = true, Provedor = "Teste", Modelo = "m",
        FerramentasJson = """[{"nome":"buscar_processos"},{"nome":"buscar_processos"}]"""
    };

    private static AssistenteService Servico(AppDbContext db, ITenantContext tenant, IAssistenteLlmClient? llm = null) =>
        new(llm ?? new LlmFalso(new LlmResposta([new LlmTexto("ok")], 1, 1)), Ferramentas(db, tenant), db, tenant,
            NullLogger<AssistenteService>.Instance);

    [Fact]
    public async Task ListarConversas_SoDoUsuarioSemImpersonacao_ComTituloDaPrimeiraPergunta()
    {
        var db = CreateContext();
        var tenantId = Guid.NewGuid();
        var eu = Guid.NewGuid();
        var c1 = Guid.NewGuid();
        var c2 = Guid.NewGuid();
        db.InteracoesAssistente.AddRange(
            Interacao(tenantId, eu, c1, "primeira de c1", 60),
            Interacao(tenantId, eu, c1, "segunda de c1", 50),
            Interacao(tenantId, eu, c2, "única de c2", 5),
            Interacao(tenantId, Guid.NewGuid(), Guid.NewGuid(), "de outro usuário", 1),
            Interacao(Guid.NewGuid(), eu, Guid.NewGuid(), "de outro tenant", 1),
            Interacao(tenantId, eu, Guid.NewGuid(), "do super admin impersonando", 1, impersonadoPor: Guid.NewGuid()));
        db.SaveChanges();

        var lista = await Servico(db, Tenant(tenantId, PlanoTipo.Pro, eu)).ListarConversasAsync();

        Assert.Equal([c2, c1], lista.Select(c => c.ConversaId));
        Assert.Equal("primeira de c1", lista[1].Titulo);
        Assert.Equal(2, lista[1].Perguntas);
    }

    [Fact]
    public async Task ObterConversa_DeOutroUsuario_RetornaNull()
    {
        var db = CreateContext();
        var tenantId = Guid.NewGuid();
        var conversa = Guid.NewGuid();
        db.InteracoesAssistente.Add(Interacao(tenantId, Guid.NewGuid(), conversa, "não é minha", 1));
        db.SaveChanges();

        Assert.Null(await Servico(db, Tenant(tenantId, PlanoTipo.Pro, Guid.NewGuid())).ObterConversaAsync(conversa));
    }

    [Fact]
    public async Task ObterConversa_RetornaMensagensEmOrdemComFerramentas()
    {
        var db = CreateContext();
        var tenantId = Guid.NewGuid();
        var eu = Guid.NewGuid();
        var conversa = Guid.NewGuid();
        db.InteracoesAssistente.AddRange(Interacao(tenantId, eu, conversa, "b", 1), Interacao(tenantId, eu, conversa, "a", 10));
        db.SaveChanges();

        var msgs = await Servico(db, Tenant(tenantId, PlanoTipo.Pro, eu)).ObterConversaAsync(conversa);

        Assert.Equal(["a", "b"], msgs!.Select(m => m.Pergunta));
        Assert.Equal(["buscar_processos"], msgs[0].FerramentasUsadas);
    }

    [Fact]
    public async Task Perguntar_ContinuaConversaPropria_EIgnoraIdDeConversaAlheia()
    {
        var (db, a, _) = SeedDoisTenants();
        var eu = Guid.NewGuid();
        var minha = Guid.NewGuid();
        var alheia = Guid.NewGuid();
        db.InteracoesAssistente.AddRange(
            Interacao(a, eu, minha, "antes", 10),
            Interacao(a, Guid.NewGuid(), alheia, "de outra pessoa", 10));
        db.SaveChanges();
        var tenant = Tenant(a, PlanoTipo.Pro, eu);

        var r1 = await Servico(db, tenant).PerguntarAsync(new AssistentePerguntaDto([new("user", "continua")], minha));
        var r2 = await Servico(db, tenant).PerguntarAsync(new AssistentePerguntaDto([new("user", "tentativa")], alheia));

        Assert.Equal(minha, r1.ConversaId);
        Assert.NotEqual(alheia, r2.ConversaId);
        Assert.Equal(1, db.InteracoesAssistente.Count(i => i.ConversaId == alheia));
    }

    [Fact]
    public async Task Perguntar_UltimaMensagemNaoEhDoUsuario_Lanca()
    {
        var db = CreateContext();
        var tenant = Tenant(Guid.NewGuid(), PlanoTipo.Pro);
        var svc = new AssistenteService(new LlmFalso(), Ferramentas(db, tenant), db, tenant,
            NullLogger<AssistenteService>.Instance);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            svc.PerguntarAsync(new AssistentePerguntaDto([new("user", "oi"), new("assistant", "olá")])));
    }

    [Fact]
    public async Task Controller_PlanoFree_Retorna402()
    {
        var controller = new AssistenteController(Mock.Of<IAssistenteService>(), Tenant(Guid.NewGuid(), PlanoTipo.Free));

        var r = await controller.Perguntar(new AssistentePerguntaDto([new("user", "oi")]), default);

        Assert.Equal(402, Assert.IsType<ObjectResult>(r.Result).StatusCode);
    }

    // ── Cliente LLM: formatos dos provedores ─────────────────────────────────

    private sealed class HandlerCaptura(string resposta) : HttpMessageHandler
    {
        public string? Corpo { get; private set; }
        public Uri? Uri { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Uri = request.RequestUri;
            Corpo = await request.Content!.ReadAsStringAsync(ct);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(resposta, Encoding.UTF8, "application/json") };
        }
    }

    private static AssistenteLlmClient Cliente(HandlerCaptura h, string provider) =>
        new(new HttpClient(h), new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["IA:Provider"] = provider,
            ["IA:ApiKey"] = "k",
            ["IA:Model"] = "m"
        }).Build(), NullLogger<AssistenteLlmClient>.Instance);

    private static readonly LlmFerramenta FerramentaTeste =
        new("buscar_processos", "Busca", new JsonObject { ["type"] = "object", ["properties"] = new JsonObject() });

    [Fact]
    public async Task Anthropic_EnviaFerramentasELeToolUse()
    {
        var h = new HandlerCaptura("""
            {"content":[{"type":"text","text":"Vou buscar."},{"type":"tool_use","id":"tu1","name":"buscar_processos","input":{"texto":"x"}}],
             "stop_reason":"tool_use","usage":{"input_tokens":12,"output_tokens":3}}
            """);

        var r = await Cliente(h, "Anthropic").EnviarAsync("sis", [new(LlmPapel.Usuario, [new LlmTexto("oi")])], [FerramentaTeste]);

        Assert.EndsWith("/v1/messages", h.Uri!.ToString());
        var body = JsonNode.Parse(h.Corpo!)!;
        Assert.Equal("sis", body["system"]!.GetValue<string>());
        Assert.Equal("buscar_processos", body["tools"]![0]!["name"]!.GetValue<string>());
        var chamada = Assert.Single(r.Chamadas);
        Assert.Equal("tu1", chamada.Id);
        Assert.Equal("x", chamada.Argumentos.GetProperty("texto").GetString());
        Assert.Equal(12, r.TokensEntrada);
    }

    [Fact]
    public async Task OpenAI_ConverteResultadosEmMensagensToolELeToolCalls()
    {
        var h = new HandlerCaptura("""
            {"choices":[{"message":{"content":null,"tool_calls":[{"id":"call1","type":"function","function":{"name":"buscar_processos","arguments":"{\"texto\":\"y\"}"}}]}}],
             "usage":{"prompt_tokens":7,"completion_tokens":2}}
            """);
        var args = Args(new { texto = "z" });
        LlmMensagem[] historico =
        [
            new(LlmPapel.Usuario, [new LlmTexto("oi")]),
            new(LlmPapel.Assistente, [new LlmChamadaFerramenta("call0", "buscar_processos", args)]),
            new(LlmPapel.Usuario, [new LlmResultadoFerramenta("call0", "{\"total\":0}", false)])
        ];

        var r = await Cliente(h, "OpenAI").EnviarAsync("sis", historico, [FerramentaTeste]);

        Assert.EndsWith("/v1/chat/completions", h.Uri!.ToString());
        var msgs = JsonNode.Parse(h.Corpo!)!["messages"]!.AsArray();
        Assert.Equal("system", msgs[0]!["role"]!.GetValue<string>());
        Assert.Equal("call0", msgs[2]!["tool_calls"]![0]!["id"]!.GetValue<string>());
        Assert.Equal("tool", msgs[3]!["role"]!.GetValue<string>());
        Assert.Equal("call0", msgs[3]!["tool_call_id"]!.GetValue<string>());
        var chamada = Assert.Single(r.Chamadas);
        Assert.Equal("y", chamada.Argumentos.GetProperty("texto").GetString());
    }

    [Fact]
    public async Task SemApiKey_LancaIndisponivel()
    {
        var client = new AssistenteLlmClient(new HttpClient(new HandlerCaptura("{}")),
            new ConfigurationBuilder().Build(), NullLogger<AssistenteLlmClient>.Instance);

        await Assert.ThrowsAsync<AssistenteIndisponivelException>(() =>
            client.EnviarAsync("s", [new(LlmPapel.Usuario, [new LlmTexto("oi")])], []));
    }

    // ── Idioma da resposta ───────────────────────────────────────────────────

    [Theory]
    [InlineData("Prezada Janete,\n\nHope you're doing well. We'd like to inform you that your recent payment is overdue.")]
    [InlineData("Solicitamos que realize o pagamento o mais breve possível para evitar further complications. Thank you.")]
    [InlineData("O processo está ativo 这是 uma decisão.")]
    public void Idioma_DetectaTrechosEmOutroIdioma(string texto) =>
        Assert.True(IdiomaResposta.PareceTerOutroIdioma(texto));

    [Theory]
    [InlineData("Você tem **2 prazos** esta semana: [0001234-56.2024.8.26.0100](/pages/processo-detalhe.html?id=abc). Se for preciso, ajuste no sistema.")]
    [InlineData("Prezada Janete, informamos que a parcela do contrato HON-2026/0001 está em atraso. Valor: R$ 404,01. E-mail: janete.the.best@email.com")]
    [InlineData("A audiência do processo será no dia 12/10. Some o valor da multa e veja se ele more no endereço do cadastro.")]
    [InlineData("")]
    public void Idioma_NaoMarcaTextoEmPortugues(string texto) =>
        Assert.False(IdiomaResposta.PareceTerOutroIdioma(texto));

    [Fact]
    public async Task Perguntar_RespostaComIngles_ReescreveEmPortugues()
    {
        var (db, a, _) = SeedDoisTenants();
        var tenant = Tenant(a, PlanoTipo.Pro);
        var llm = new LlmFalso(
            new LlmResposta([new LlmTexto("Prezada Janete, we'd like to inform you that your payment is overdue.")], 100, 20),
            new LlmResposta([new LlmTexto("Prezada Janete, informamos que o seu pagamento está em atraso.")], 30, 15));
        var svc = new AssistenteService(llm, Ferramentas(db, tenant), db, tenant, NullLogger<AssistenteService>.Instance);

        var r = await svc.PerguntarAsync(new AssistentePerguntaDto([new("user", "prepare um email para a Janete")]));

        Assert.Equal("Prezada Janete, informamos que o seu pagamento está em atraso.", r.Resposta);
        Assert.Equal(2, llm.Chamadas.Count);
        var i = await db.InteracoesAssistente.SingleAsync();
        Assert.True(i.IdiomaCorrigido);
        Assert.Equal(130, i.TokensEntrada);
        Assert.Equal(r.Resposta, i.Resposta);
    }

    [Fact]
    public async Task Perguntar_RespostaEmPortugues_NaoChamaCorrecao()
    {
        var (db, a, _) = SeedDoisTenants();
        var tenant = Tenant(a, PlanoTipo.Pro);
        var llm = new LlmFalso(new LlmResposta([new LlmTexto("Você não tem prazos esta semana.")], 10, 5));
        var svc = new AssistenteService(llm, Ferramentas(db, tenant), db, tenant, NullLogger<AssistenteService>.Instance);

        await svc.PerguntarAsync(new AssistentePerguntaDto([new("user", "prazos?")]));

        Assert.Single(llm.Chamadas);
        Assert.False((await db.InteracoesAssistente.SingleAsync()).IdiomaCorrigido);
    }

    [Fact]
    public void LimparRaciocinio_RemoveThink()
    {
        Assert.Equal("Resposta", AssistenteLlmClient.LimparRaciocinio("<think>pensando…</think>\nResposta"));
    }
}
