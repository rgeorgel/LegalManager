using System.Security.Claims;
using LegalManager.API.Controllers;
using LegalManager.Application.DTOs.Grupos;
using LegalManager.Application.DTOs.Perguntas;
using LegalManager.Domain.Entities;
using LegalManager.Domain.Enums;
using LegalManager.Domain.Interfaces;
using LegalManager.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace LegalManager.UnitTests;

/// <summary>Pesquisa de interesse: cadastro (super admin), grupos e respostas do usuário final.</summary>
public class PerguntasTests
{
    private readonly AppDbContext _db = new(new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private readonly Guid _superAdminId = Guid.NewGuid();
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();

    public PerguntasTests()
    {
        _db.Tenants.Add(new Tenant { Id = _tenantId, Nome = "Escritório", Plano = PlanoTipo.Pro, Status = StatusTenant.Ativo });
        _db.Users.Add(new Usuario { Id = _userId, TenantId = _tenantId, Nome = "Ana", Email = "ana@x.com", UserName = "ana@x.com" });
        _db.SaveChanges();
    }

    private T ComUsuario<T>(T controller) where T : ControllerBase
    {
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, _superAdminId.ToString())], "test"))
            }
        };
        return controller;
    }

    private SuperAdminPerguntasController Admin() => ComUsuario(new SuperAdminPerguntasController(_db));
    private SuperAdminGruposController Grupos() => ComUsuario(new SuperAdminGruposController(_db));

    private PerguntasController Usuario(string role = "Admin", Guid? userId = null)
    {
        var t = new Mock<ITenantContext>();
        t.Setup(x => x.TenantId).Returns(_tenantId);
        t.Setup(x => x.UserId).Returns(userId ?? _userId);
        t.Setup(x => x.UserRole).Returns(role);
        return new PerguntasController(_db, t.Object);
    }

    private static SalvarPerguntaDto Dto(string tipo = "Texto", List<string>? opcoes = null, string publico = "Advogados",
        string segmento = "Todos", string? plano = null, Guid? grupo = null, bool ativa = true) =>
        new("Como conheceu a Causify?", " Opcional ", tipo, opcoes, publico, segmento, plano, grupo, ativa, 1);

    private async Task<PerguntaAdminDto> Criar(SalvarPerguntaDto dto) =>
        (PerguntaAdminDto)Assert.IsType<OkObjectResult>((await Admin().Criar(dto, default)).Result).Value!;

    // ── Cadastro (super admin) ───────────────────────────────────────────────

    [Theory]
    [InlineData("", "Texto", "Advogados", "Todos", null, "texto da pergunta")]
    [InlineData("P", "Nada", "Advogados", "Todos", null, "Tipo de resposta")]
    [InlineData("P", "Texto", "Ninguem", "Todos", null, "Público")]
    [InlineData("P", "Texto", "Advogados", "Alguns", null, "Segmento")]
    [InlineData("P", "EscolhaUnica", "Advogados", "Todos", null, "duas opções")]
    [InlineData("P", "Texto", "Advogados", "PlanoEspecifico", null, "plano-alvo")]
    [InlineData("P", "Texto", "Advogados", "GrupoEspecifico", null, "grupo")]
    public async Task Criar_Invalida_BadRequest(string texto, string tipo, string publico, string segmento, string? plano, string erro)
    {
        var dto = new SalvarPerguntaDto(texto, null, tipo, ["só uma"], publico, segmento, plano, null, true, 0);
        var r = Assert.IsType<BadRequestObjectResult>((await Admin().Criar(dto, default)).Result);
        Assert.Contains(erro, r.Value!.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Criar_GrupoInexistente_BadRequest()
    {
        var r = await Admin().Criar(Dto(segmento: "GrupoEspecifico", grupo: Guid.NewGuid()), default);
        Assert.IsType<BadRequestObjectResult>(r.Result);
    }

    [Fact]
    public async Task Criar_ParaClientes_IgnoraSegmentacao()
    {
        var p = await Criar(Dto(publico: "Clientes", segmento: "PlanoEspecifico", plano: "Pro"));

        Assert.Equal("Todos", p.Segmento);
        Assert.Null(p.PlanoAlvo);
        Assert.Equal("Opcional", p.Descricao);
    }

    [Fact]
    public async Task Atualizar_Excluir_EListar()
    {
        var grupo = (GrupoDto)Assert.IsType<OkObjectResult>((await Grupos().Criar(new SalvarGrupoDto(" Beta ", " testers "), default)).Result).Value!;
        var p = await Criar(Dto());

        var atualizada = (PerguntaAdminDto)Assert.IsType<OkObjectResult>((await Admin().Atualizar(p.Id,
            Dto("EscolhaMultipla", ["A", "B", " ", "A"], segmento: "GrupoEspecifico", grupo: grupo.Id), default)).Result).Value!;
        Assert.Equal(["A", "B"], atualizada.Opcoes);
        Assert.Equal("Beta", atualizada.GrupoNome);

        Assert.IsType<NotFoundResult>((await Admin().Atualizar(Guid.NewGuid(), Dto(), default)).Result);
        Assert.IsType<BadRequestObjectResult>((await Admin().Atualizar(p.Id, Dto(tipo: "X"), default)).Result);

        await Criar(Dto(segmento: "PlanoEspecifico", plano: "Max"));
        var lista = (List<PerguntaAdminDto>)Assert.IsType<OkObjectResult>((await Admin().Listar(default)).Result).Value!;
        Assert.Equal(2, lista.Count);
        Assert.Contains(lista, x => x.GrupoNome == "Beta");

        Assert.IsType<NoContentResult>(await Admin().Excluir(p.Id, default));
        Assert.IsType<NotFoundResult>(await Admin().Excluir(p.Id, default));
    }

    [Fact]
    public async Task Respostas_ContaOpcoesEIdentificaRespondentes()
    {
        var unica = await Criar(Dto("EscolhaUnica", ["Google", "Indicação"]));
        var multipla = await Criar(Dto("EscolhaMultipla", ["A", "B", "C"]));

        var contatoId = Guid.NewGuid();
        _db.Contatos.Add(new Contato { Id = contatoId, TenantId = _tenantId, Nome = "Cliente Portal" });
        var acesso = new AcessoCliente { Id = Guid.NewGuid(), TenantId = _tenantId, ContatoId = contatoId, Email = "cli@x.com" };
        _db.AcessosCliente.Add(acesso);
        _db.RespostasPerguntas.AddRange(
            new RespostaPergunta { Id = Guid.NewGuid(), PerguntaId = unica.Id, TenantId = _tenantId, RespondenteTipo = TipoRespondente.Usuario, RespondenteId = _userId, OpcaoEscolhida = "Google", RespondidoEm = DateTime.UtcNow },
            new RespostaPergunta { Id = Guid.NewGuid(), PerguntaId = unica.Id, TenantId = _tenantId, RespondenteTipo = TipoRespondente.Cliente, RespondenteId = acesso.Id, OpcaoEscolhida = "Google", RespondidoEm = DateTime.UtcNow },
            new RespostaPergunta { Id = Guid.NewGuid(), PerguntaId = unica.Id, TenantId = Guid.NewGuid(), RespondenteTipo = TipoRespondente.Usuario, RespondenteId = Guid.NewGuid(), OpcaoEscolhida = "Indicação", RespondidoEm = DateTime.UtcNow },
            new RespostaPergunta { Id = Guid.NewGuid(), PerguntaId = multipla.Id, TenantId = _tenantId, RespondenteTipo = TipoRespondente.Usuario, RespondenteId = _userId, OpcoesEscolhidasJson = "[\"A\",\"C\"]", RespondidoEm = DateTime.UtcNow },
            new RespostaPergunta { Id = Guid.NewGuid(), PerguntaId = multipla.Id, TenantId = _tenantId, RespondenteTipo = TipoRespondente.Usuario, RespondenteId = Guid.NewGuid(), OpcoesEscolhidasJson = "não é json", RespondidoEm = DateTime.UtcNow });
        _db.SaveChanges();

        var r = (PerguntaComRespostasDto)Assert.IsType<OkObjectResult>((await Admin().Respostas(unica.Id, default)).Result).Value!;
        Assert.Equal(new OpcaoContagemDto("Google", 2), r.Contagens[0]);
        Assert.Contains(r.Respostas, x => x.RespondenteNome == "Cliente Portal" && x.RespondenteEmail == "cli@x.com");
        Assert.Contains(r.Respostas, x => x.RespondenteNome == "Ana" && x.TenantNome == "Escritório");
        Assert.Contains(r.Respostas, x => x.RespondenteNome == "—" && x.TenantNome == "—");

        var m = (PerguntaComRespostasDto)Assert.IsType<OkObjectResult>((await Admin().Respostas(multipla.Id, default)).Result).Value!;
        Assert.Equal(2, m.Contagens.Count);
        Assert.Equal(2, m.Pergunta.TotalRespostas);

        Assert.IsType<NotFoundResult>((await Admin().Respostas(Guid.NewGuid(), default)).Result);
    }

    // ── Grupos ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Grupos_CrudEMembros()
    {
        var g = (GrupoDto)Assert.IsType<OkObjectResult>((await Grupos().Criar(new SalvarGrupoDto("Beta", null), default)).Result).Value!;
        var removido = Guid.NewGuid();

        Assert.IsType<NoContentResult>(await Grupos().AdicionarMembros(g.Id, new AdicionarMembrosDto([_userId, _userId, removido]), default));
        Assert.IsType<NoContentResult>(await Grupos().AdicionarMembros(g.Id, new AdicionarMembrosDto([_userId]), default));
        Assert.IsType<NotFoundResult>(await Grupos().AdicionarMembros(Guid.NewGuid(), new AdicionarMembrosDto([_userId]), default));

        var membros = (List<GrupoMembroDto>)Assert.IsType<OkObjectResult>((await Grupos().Membros(g.Id, default)).Result).Value!;
        Assert.Equal(2, membros.Count);
        Assert.Contains(membros, m => m.Nome == "Ana" && m.TenantNome == "Escritório");
        Assert.Contains(membros, m => m.Nome == "(usuário removido)");
        Assert.IsType<NotFoundResult>((await Grupos().Membros(Guid.NewGuid(), default)).Result);

        var lista = (List<GrupoDto>)Assert.IsType<OkObjectResult>((await Grupos().Listar(default)).Result).Value!;
        Assert.Equal(2, Assert.Single(lista).TotalMembros);

        var atualizado = (GrupoDto)Assert.IsType<OkObjectResult>((await Grupos().Atualizar(g.Id, new SalvarGrupoDto("Gama", "desc"), default)).Result).Value!;
        Assert.Equal("Gama", atualizado.Nome);
        Assert.Equal(2, atualizado.TotalMembros);
        Assert.IsType<NotFoundResult>((await Grupos().Atualizar(Guid.NewGuid(), new SalvarGrupoDto("X", null), default)).Result);

        Assert.IsType<NoContentResult>(await Grupos().RemoverMembro(g.Id, removido, default));
        Assert.IsType<NotFoundResult>(await Grupos().RemoverMembro(g.Id, removido, default));
    }

    [Fact]
    public async Task Grupos_Excluir_PerguntasVoltamParaTodos()
    {
        var g = (GrupoDto)Assert.IsType<OkObjectResult>((await Grupos().Criar(new SalvarGrupoDto("Beta", null), default)).Result).Value!;
        await Grupos().AdicionarMembros(g.Id, new AdicionarMembrosDto([_userId]), default);
        var p = await Criar(Dto(segmento: "GrupoEspecifico", grupo: g.Id));

        Assert.IsType<NoContentResult>(await Grupos().Excluir(g.Id, default));
        Assert.IsType<NotFoundResult>(await Grupos().Excluir(g.Id, default));

        var pergunta = await _db.Perguntas.FindAsync(p.Id);
        Assert.Equal(SegmentoPergunta.Todos, pergunta!.Segmento);
        Assert.Null(pergunta.GrupoId);
        Assert.Empty(_db.GruposPerguntaMembros);
    }

    // ── Usuário final ────────────────────────────────────────────────────────

    [Fact]
    public async Task Pendentes_SegmentaPorPlanoPagantesTrialEGrupo()
    {
        var g = (GrupoDto)Assert.IsType<OkObjectResult>((await Grupos().Criar(new SalvarGrupoDto("Beta", null), default)).Result).Value!;
        await Grupos().AdicionarMembros(g.Id, new AdicionarMembrosDto([_userId]), default);

        var todos = await Criar(Dto());
        await Criar(Dto(segmento: "PlanoEspecifico", plano: "Pro"));
        await Criar(Dto(segmento: "PlanoEspecifico", plano: "Max"));   // outro plano: não aparece
        await Criar(Dto(segmento: "Pagantes"));
        await Criar(Dto(segmento: "EmTrial"));                         // tenant ativo: não aparece
        await Criar(Dto(segmento: "GrupoEspecifico", grupo: g.Id));
        await Criar(Dto(publico: "Clientes"));                         // outro público
        await Criar(Dto(ativa: false));                                // inativa

        var pendentes = (List<PerguntaPendenteDto>)Assert.IsType<OkObjectResult>((await Usuario().GetPendentes(default)).Result).Value!;
        Assert.Equal(4, pendentes.Count);

        Assert.IsType<NoContentResult>(await Usuario().Responder(todos.Id, new ResponderPerguntaDto("Pelo Google", null, null), default));
        pendentes = (List<PerguntaPendenteDto>)Assert.IsType<OkObjectResult>((await Usuario().GetPendentes(default)).Result).Value!;
        Assert.Equal(3, pendentes.Count);

        // Grupo: outro usuário não é membro
        var outro = (List<PerguntaPendenteDto>)Assert.IsType<OkObjectResult>((await Usuario(userId: Guid.NewGuid()).GetPendentes(default)).Result).Value!;
        Assert.Equal(3, outro.Count);
    }

    [Fact]
    public async Task Pendentes_ClienteVePerguntasDeClientes_TenantInexistenteNaoVeNada()
    {
        await Criar(Dto(publico: "Clientes"));
        await Criar(Dto());

        var cliente = (List<PerguntaPendenteDto>)Assert.IsType<OkObjectResult>((await Usuario("Cliente").GetPendentes(default)).Result).Value!;
        Assert.Single(cliente);

        var t = new Mock<ITenantContext>();
        t.Setup(x => x.TenantId).Returns(Guid.NewGuid());
        t.Setup(x => x.UserRole).Returns("Admin");
        var semTenant = (List<PerguntaPendenteDto>)Assert.IsType<OkObjectResult>((await new PerguntasController(_db, t.Object).GetPendentes(default)).Result).Value!;
        Assert.Empty(semTenant);
    }

    [Fact]
    public async Task Pendentes_SemPerguntas_ListaVazia()
    {
        var r = (List<PerguntaPendenteDto>)Assert.IsType<OkObjectResult>((await Usuario().GetPendentes(default)).Result).Value!;
        Assert.Empty(r);
    }

    [Fact]
    public async Task Responder_ValidaPorTipoESalvaUmaVez()
    {
        var unica = await Criar(Dto("EscolhaUnica", ["Sim", "Não"]));
        var multipla = await Criar(Dto("EscolhaMultipla", ["A", "B"]));
        var texto = await Criar(Dto());

        Assert.IsType<BadRequestObjectResult>(await Usuario().Responder(unica.Id, new ResponderPerguntaDto(null, "Talvez", null), default));
        Assert.IsType<BadRequestObjectResult>(await Usuario().Responder(multipla.Id, new ResponderPerguntaDto(null, null, ["Z"]), default));
        Assert.IsType<BadRequestObjectResult>(await Usuario().Responder(multipla.Id, new ResponderPerguntaDto(null, null, null), default));
        Assert.IsType<BadRequestObjectResult>(await Usuario().Responder(texto.Id, new ResponderPerguntaDto("  ", null, null), default));
        Assert.IsType<NotFoundResult>(await Usuario().Responder(Guid.NewGuid(), new ResponderPerguntaDto("x", null, null), default));

        Assert.IsType<NoContentResult>(await Usuario().Responder(unica.Id, new ResponderPerguntaDto(null, "Sim", null), default));
        Assert.IsType<NoContentResult>(await Usuario().Responder(multipla.Id, new ResponderPerguntaDto(null, null, ["A", "B", "A"]), default));
        Assert.IsType<NoContentResult>(await Usuario("Cliente").Responder(unica.Id, new ResponderPerguntaDto(null, "Não", null), default));
        Assert.IsType<NoContentResult>(await Usuario().Responder(unica.Id, new ResponderPerguntaDto(null, "Não", null), default)); // idempotente

        Assert.Equal(3, await _db.RespostasPerguntas.CountAsync());
        var m = await _db.RespostasPerguntas.SingleAsync(r => r.PerguntaId == multipla.Id);
        Assert.Equal("[\"A\",\"B\"]", m.OpcoesEscolhidasJson);
    }
}
