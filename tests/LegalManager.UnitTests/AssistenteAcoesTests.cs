using System.Text.Json;
using LegalManager.API.Controllers;
using LegalManager.Application.DTOs.Assistente;
using LegalManager.Application.Interfaces;
using LegalManager.Domain.Entities;
using LegalManager.Domain.Enums;
using LegalManager.Domain.Interfaces;
using LegalManager.Infrastructure.Assistente;
using LegalManager.Infrastructure.Persistence;
using LegalManager.Infrastructure.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace LegalManager.UnitTests;

/// <summary>
/// Alterações pelo assistente: as ferramentas só propõem; a gravação acontece na confirmação,
/// pelo próprio usuário que fez a pergunta.
/// </summary>
public class AssistenteAcoesTests
{
    private readonly AppDbContext _db = new(new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Mock<IAuditService> _audit = new();

    public AssistenteAcoesTests()
    {
        _db.Tenants.Add(new Tenant { Id = _tenantId, Nome = "Escritório A" });
        _db.Users.Add(new Usuario
        {
            Id = _userId, TenantId = _tenantId, Nome = "Ana Advogada", Email = "ana@a.com", UserName = "ana@a.com",
            Perfil = PerfilUsuario.Advogado, Ativo = true, CriadoEm = DateTime.UtcNow
        });
        _db.SaveChanges();
    }

    private ITenantContext Tenant(Guid? userId = null, Guid? tenantId = null)
    {
        var t = new Mock<ITenantContext>();
        t.Setup(x => x.TenantId).Returns(tenantId ?? _tenantId);
        t.Setup(x => x.UserId).Returns(userId ?? _userId);
        t.Setup(x => x.UserRole).Returns("Advogado");
        t.Setup(x => x.Plano).Returns(PlanoTipo.Plus);
        return t.Object;
    }

    private AssistenteFerramentas Ferramentas(ITenantContext? tenant = null) =>
        new(_db, tenant ?? Tenant(), new HonorarioService(_db, Mock.Of<IAuditService>()));

    private AssistenteAcoes Acoes(ITenantContext? tenant = null)
    {
        tenant ??= Tenant();
        return new AssistenteAcoes(_db, tenant,
            new ContatoService(_db, tenant, new HonorarioService(_db, Mock.Of<IAuditService>())),
            new TarefaService(_db, tenant), new EventoService(_db, tenant), new TimesheetService(_db),
            _audit.Object, NullLogger<AssistenteAcoes>.Instance);
    }

    private static JsonElement Args(object o) => JsonSerializer.SerializeToElement(o);

    private Contato SeedContato(Guid? tenantId = null)
    {
        var c = new Contato
        {
            Id = Guid.NewGuid(), TenantId = tenantId ?? _tenantId, Nome = "Maria Silva", TipoContato = TipoContato.Cliente,
            Email = "antigo@x.com", Telefone = "(11) 1111-1111", Observacoes = "Cliente antigo", CriadoEm = DateTime.UtcNow
        };
        c.Tags = [new ContatoTag { Id = Guid.NewGuid(), ContatoId = c.Id, Tag = "VIP" }, new ContatoTag { Id = Guid.NewGuid(), ContatoId = c.Id, Tag = "BPC LOAS" }];
        _db.Contatos.Add(c);
        _db.SaveChanges();
        return c;
    }

    /// <summary>Propõe pela ferramenta e grava a proposta, como o AssistenteService faz.</summary>
    private async Task<(string Json, bool Erro, AcaoAssistente? Acao)> ProporAsync(string ferramenta, object args)
    {
        var f = Ferramentas();
        var (json, erro) = await f.ExecutarAsync(ferramenta, Args(args), default);
        var acao = f.AcoesPropostas.SingleOrDefault();
        if (acao is not null)
        {
            acao.InteracaoId = Guid.NewGuid();
            _db.AcoesAssistente.Add(acao);
            await _db.SaveChangesAsync();
            _db.ChangeTracker.Clear();
        }
        return (json, erro, acao);
    }

    // ── Atualizar contato ────────────────────────────────────────────────────

    [Fact]
    public async Task AtualizarContato_SoPropoe_NaoGravaAteConfirmar()
    {
        var c = SeedContato();

        var (json, erro, acao) = await ProporAsync("atualizar_contato",
            new { contato_id = c.Id.ToString(), email = "nova@x.com", telefone = "", tags_adicionar = new[] { "Urgente" } });

        Assert.False(erro);
        Assert.Contains("aguardando_confirmacao", json);
        Assert.NotNull(acao);
        Assert.Equal(StatusAcaoAssistente.Pendente, acao.Status);
        var dto = AssistenteAcoes.ParaDto(acao);
        Assert.Contains(dto.Alteracoes, x => x.Campo == "E-mail" && x.Antes == "antigo@x.com" && x.Depois == "nova@x.com");
        Assert.Contains(dto.Alteracoes, x => x.Campo == "Telefone" && x.Antes == "(11) 1111-1111" && x.Depois == null);
        Assert.Equal("antigo@x.com", (await _db.Contatos.AsNoTracking().SingleAsync()).Email);
    }

    [Fact]
    public async Task AtualizarContato_Confirmar_GravaSoOsCamposAlterados()
    {
        var c = SeedContato();
        var (_, _, acao) = await ProporAsync("atualizar_contato",
            new { contato_id = c.Id.ToString(), email = "nova@x.com", telefone = "", tags_adicionar = new[] { "Urgente" }, tags_remover = new[] { "vip" } });

        var r = await Acoes().ConfirmarAsync(acao!.Id, "1.2.3.4");

        Assert.True(r!.Status == "Confirmada", r.Erro);
        Assert.Equal($"/pages/contato-detalhe.html?id={c.Id}", r.Link);
        var salvo = await _db.Contatos.AsNoTracking().Include(x => x.Tags).SingleAsync();
        Assert.Equal("nova@x.com", salvo.Email);
        Assert.Null(salvo.Telefone);
        Assert.Equal("Maria Silva", salvo.Nome);
        Assert.Equal("Cliente antigo", salvo.Observacoes);
        Assert.Equal(["BPC LOAS", "Urgente"], salvo.Tags.Select(t => t.Tag).Order());
        _audit.Verify(a => a.LogAsync(It.Is<AuditLogEntry>(e => e.EntidadeId == c.Id.ToString() && e.IpAddress == "1.2.3.4"), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AtualizarContato_DeOutroTenant_NaoEncontra()
    {
        var c = SeedContato(Guid.NewGuid());

        var (json, _, acao) = await ProporAsync("atualizar_contato", new { contato_id = c.Id.ToString(), email = "x@x.com" });

        Assert.Contains("não encontrado", json);
        Assert.Null(acao);
    }

    [Theory]
    [InlineData("email", "nao-e-email")]
    [InlineData("cpf_cnpj", "123")]
    [InlineData("estado", "Sao Paulo")]
    [InlineData("nome", "")]
    public async Task AtualizarContato_ValorInvalido_Recusa(string campo, string valor)
    {
        var c = SeedContato();
        var args = new Dictionary<string, object> { ["contato_id"] = c.Id.ToString(), [campo] = valor };

        var (_, erro, acao) = await ProporAsync("atualizar_contato", args);

        Assert.True(erro);
        Assert.Null(acao);
    }

    [Fact]
    public async Task AtualizarContato_SemMudanca_NaoPropoe()
    {
        var c = SeedContato();
        var (json, _, acao) = await ProporAsync("atualizar_contato", new { contato_id = c.Id.ToString(), email = "antigo@x.com" });
        Assert.Contains("Nada a alterar", json);
        Assert.Null(acao);
    }

    // ── Criar tarefa ─────────────────────────────────────────────────────────

    [Fact]
    public async Task CriarTarefa_ConfirmarDuasVezes_CriaUmaSo()
    {
        var c = SeedContato();
        var (_, erro, acao) = await ProporAsync("criar_tarefa",
            new { titulo = "Ligar para a cliente", prazo = "2026-10-20 14:30", contato_id = c.Id.ToString(), prioridade = "Alta" });
        Assert.False(erro);
        Assert.Equal(0, await _db.Tarefas.CountAsync());

        var r1 = await Acoes().ConfirmarAsync(acao!.Id, null);
        var r2 = await Acoes().ConfirmarAsync(acao.Id, null);

        Assert.Equal("Confirmada", r1!.Status);
        Assert.Equal("Confirmada", r2!.Status);
        var t = await _db.Tarefas.AsNoTracking().SingleAsync();
        Assert.Equal("Ligar para a cliente", t.Titulo);
        Assert.Equal(new DateTime(2026, 10, 20, 14, 30, 0), t.Prazo);
        Assert.Equal(_userId, t.ResponsavelId);
        Assert.Equal(c.Id, t.ContatoId);
        Assert.Equal(PrioridadeTarefa.Alta, t.Prioridade);
        Assert.Equal($"/pages/tarefas.html?abrirId={t.Id}", r1.Link);
    }

    [Fact]
    public async Task CriarTarefa_ResponsavelDeOutroEscritorio_Recusa()
    {
        var outro = Guid.NewGuid();
        _db.Users.Add(new Usuario { Id = outro, TenantId = Guid.NewGuid(), Nome = "Intruso", Ativo = true, Email = "i@i.com", UserName = "i@i.com" });
        _db.SaveChanges();

        var (json, _, acao) = await ProporAsync("criar_tarefa", new { titulo = "X", responsavel_id = outro.ToString() });

        Assert.Contains("Responsável não encontrado", json);
        Assert.Null(acao);
    }

    [Fact]
    public async Task CriarTarefa_PrazoProcessualSemData_Recusa()
    {
        var (_, erro, acao) = await ProporAsync("criar_tarefa", new { titulo = "Contestação", tipo = "Prazo" });
        Assert.True(erro);
        Assert.Null(acao);
    }

    [Fact]
    public async Task LimiteDeAcoesPorPergunta()
    {
        var f = Ferramentas();
        for (var i = 0; i < AssistenteFerramentas.MaxAcoesPorPergunta; i++)
            Assert.False((await f.ExecutarAsync("criar_tarefa", Args(new { titulo = $"T{i}" }), default)).Erro);

        var (json, erro) = await f.ExecutarAsync("criar_tarefa", Args(new { titulo = "demais" }), default);

        Assert.True(erro);
        Assert.Contains("Limite", json);
        Assert.Equal(AssistenteFerramentas.MaxAcoesPorPergunta, f.AcoesPropostas.Count);
    }

    // ── Alterar prazo ────────────────────────────────────────────────────────

    private Tarefa SeedTarefa(StatusTarefa status = StatusTarefa.Pendente)
    {
        var t = new Tarefa
        {
            Id = Guid.NewGuid(), TenantId = _tenantId, Titulo = "Réplica", Status = status, Prioridade = PrioridadeTarefa.Media,
            Prazo = new DateTime(2026, 10, 15, 9, 0, 0), DataInicio = new DateTime(2026, 10, 1), QuantidadeDias = 10,
            TipoCalculo = TipoCalculo.DiasUteis, ResponsavelId = _userId, CriadoPorId = _userId, CriadoEm = DateTime.UtcNow
        };
        t.Tags = [new TarefaTag { Id = Guid.NewGuid(), TarefaId = t.Id, Tag = "cível" }];
        _db.Tarefas.Add(t);
        _db.SaveChanges();
        return t;
    }

    [Fact]
    public async Task AlterarPrazo_SoData_MantemHorario_EViraDataFixa()
    {
        var t = SeedTarefa();
        var (_, erro, acao) = await ProporAsync("alterar_prazo_tarefa", new { tarefa_id = t.Id.ToString(), prazo = "2026-10-22" });
        Assert.False(erro);
        var dto = AssistenteAcoes.ParaDto(acao!);
        Assert.Contains(dto.Alteracoes, x => x.Campo == "Prazo" && x.Antes == "15/10/2026 09:00" && x.Depois == "22/10/2026 09:00");
        Assert.Contains(dto.Alteracoes, x => x.Campo == "Cálculo do prazo");

        var r = await Acoes().ConfirmarAsync(acao!.Id, null);

        Assert.Equal("Confirmada", r!.Status);
        var salva = await _db.Tarefas.AsNoTracking().Include(x => x.Tags).SingleAsync();
        Assert.Equal(new DateTime(2026, 10, 22, 9, 0, 0), salva.Prazo);
        Assert.Null(salva.QuantidadeDias);
        Assert.Equal("Réplica", salva.Titulo);
        Assert.Equal(["cível"], salva.Tags.Select(x => x.Tag));
    }

    [Fact]
    public async Task AlterarPrazo_TarefaConcluida_Recusa()
    {
        var t = SeedTarefa(StatusTarefa.Concluida);
        var (json, _, acao) = await ProporAsync("alterar_prazo_tarefa", new { tarefa_id = t.Id.ToString(), prazo = "2026-10-22" });
        Assert.Contains("concluída", json);
        Assert.Null(acao);
    }

    // ── Confirmação: dono, cancelamento e validade ───────────────────────────

    [Fact]
    public async Task Confirmar_PorOutroUsuario_NaoEncontra()
    {
        var c = SeedContato();
        var (_, _, acao) = await ProporAsync("atualizar_contato", new { contato_id = c.Id.ToString(), email = "nova@x.com" });

        Assert.Null(await Acoes(Tenant(userId: Guid.NewGuid())).ConfirmarAsync(acao!.Id, null));
        Assert.Equal("antigo@x.com", (await _db.Contatos.AsNoTracking().SingleAsync()).Email);
    }

    [Fact]
    public async Task Cancelar_NaoGrava_ENaoPermiteConfirmarDepois()
    {
        var (_, _, acao) = await ProporAsync("criar_tarefa", new { titulo = "Talvez" });

        Assert.Equal("Cancelada", (await Acoes().CancelarAsync(acao!.Id))!.Status);
        Assert.Equal("Cancelada", (await Acoes().ConfirmarAsync(acao.Id, null))!.Status);
        Assert.Equal(0, await _db.Tarefas.CountAsync());
    }

    [Fact]
    public async Task Confirmar_Expirada_NaoGrava()
    {
        var (_, _, acao) = await ProporAsync("criar_tarefa", new { titulo = "Antiga" });
        var salva = await _db.AcoesAssistente.SingleAsync();
        salva.ExpiraEm = DateTime.UtcNow.AddMinutes(-1);
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        Assert.Equal("Expirada", (await Acoes().ConfirmarAsync(acao!.Id, null))!.Status);
        Assert.Equal(0, await _db.Tarefas.CountAsync());
    }

    [Fact]
    public async Task Confirmar_ContatoExcluidoDepois_RegistraFalha()
    {
        var c = SeedContato();
        var (_, _, acao) = await ProporAsync("atualizar_contato", new { contato_id = c.Id.ToString(), email = "nova@x.com" });
        _db.Contatos.Remove(await _db.Contatos.SingleAsync());
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        var r = await Acoes().ConfirmarAsync(acao!.Id, null);

        Assert.Equal("Falhou", r!.Status);
        Assert.Contains("não existe mais", r.Erro);
        Assert.Equal(StatusAcaoAssistente.Falhou, (await _db.AcoesAssistente.AsNoTracking().SingleAsync()).Status);
    }

    // ── Integração com o AssistenteService ───────────────────────────────────

    private sealed class LlmRoteiro(params LlmResposta[] respostas) : IAssistenteLlmClient
    {
        private int _i;
        public string Provedor => "Teste";
        public string Modelo => "modelo-teste";
        public Task<LlmResposta> EnviarAsync(string sistema, IReadOnlyList<LlmMensagem> mensagens,
            IReadOnlyList<LlmFerramenta> ferramentas, CancellationToken ct = default) =>
            Task.FromResult(respostas[Math.Min(_i++, respostas.Length - 1)]);
    }

    [Fact]
    public async Task Perguntar_DevolveEGravaAcaoPendente_EHistoricoMostraSituacao()
    {
        var c = SeedContato();
        var tenant = Tenant();
        var llm = new LlmRoteiro(
            new LlmResposta([new LlmChamadaFerramenta("c1", "atualizar_contato", Args(new { contato_id = c.Id.ToString(), email = "nova@x.com" }))], 10, 5),
            new LlmResposta([new LlmTexto("Preparei a alteração do e-mail. Confira e confirme no cartão abaixo.")], 20, 8));
        var svc = new AssistenteService(llm, Ferramentas(tenant), _db, tenant, NullLogger<AssistenteService>.Instance);

        var r = await svc.PerguntarAsync(new AssistentePerguntaDto([new("user", "Mude o e-mail da Maria para nova@x.com")]));

        var acao = Assert.Single(r.Acoes!);
        Assert.Equal("Pendente", acao.Status);
        Assert.Equal("atualizar_contato", acao.Tipo);
        var gravada = await _db.AcoesAssistente.AsNoTracking().SingleAsync();
        Assert.Equal((await _db.InteracoesAssistente.SingleAsync()).Id, gravada.InteracaoId);
        Assert.Equal(r.ConversaId, gravada.ConversaId);
        Assert.Equal("antigo@x.com", (await _db.Contatos.AsNoTracking().SingleAsync()).Email);

        _db.ChangeTracker.Clear();
        await Acoes(tenant).ConfirmarAsync(acao.Id, null);
        var conversa = await svc.ObterConversaAsync(r.ConversaId);
        Assert.Equal("Confirmada", Assert.Single(Assert.Single(conversa!).Acoes!).Status);
    }

    // ── Atualizar tarefa ─────────────────────────────────────────────────────

    [Fact]
    public async Task AtualizarTarefa_Concluir_MantemPrazoCalculadoEDados()
    {
        var t = SeedTarefa();
        var (_, erro, acao) = await ProporAsync("atualizar_tarefa", new { tarefa_id = t.Id.ToString(), status = "Concluida" });
        Assert.False(erro);
        Assert.StartsWith("Concluir tarefa", acao!.Titulo);

        Assert.Equal("Confirmada", (await Acoes().ConfirmarAsync(acao.Id, null))!.Status);

        var salva = await _db.Tarefas.AsNoTracking().Include(x => x.Tags).SingleAsync();
        Assert.Equal(StatusTarefa.Concluida, salva.Status);
        Assert.NotNull(salva.ConcluidaEm);
        Assert.Equal(new DateTime(2026, 10, 15, 9, 0, 0), salva.Prazo);
        Assert.Equal(10, salva.QuantidadeDias);
        Assert.Equal(["cível"], salva.Tags.Select(x => x.Tag));
    }

    [Fact]
    public async Task AtualizarTarefa_Reatribuir_ValidaEquipe()
    {
        var t = SeedTarefa();
        var bruno = Guid.NewGuid();
        _db.Users.Add(new Usuario { Id = bruno, TenantId = _tenantId, Nome = "Bruno", Ativo = true, Email = "b@a.com", UserName = "b@a.com" });
        _db.SaveChanges();

        var (_, _, acao) = await ProporAsync("atualizar_tarefa", new { tarefa_id = t.Id.ToString(), responsavel_id = bruno.ToString(), prioridade = "Urgente" });
        var dto = AssistenteAcoes.ParaDto(acao!);
        Assert.Contains(dto.Alteracoes, x => x.Campo == "Responsável" && x.Antes == "Ana Advogada" && x.Depois == "Bruno");
        Assert.Contains(dto.Alteracoes, x => x.Campo == "Prioridade" && x.Antes == "Média" && x.Depois == "Urgente");

        await Acoes().ConfirmarAsync(acao!.Id, null);
        var salva = await _db.Tarefas.AsNoTracking().SingleAsync();
        Assert.Equal(bruno, salva.ResponsavelId);
        Assert.Equal(PrioridadeTarefa.Urgente, salva.Prioridade);
    }

    // ── Agenda ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task CriarEvento_SemHorario_PedeHorario()
    {
        var (json, _, acao) = await ProporAsync("criar_evento", new { titulo = "Reunião", tipo = "Reuniao", inicio = "2026-10-20" });
        Assert.Contains("horário", json);
        Assert.Null(acao);
    }

    [Fact]
    public async Task CriarEvento_ConfirmarEDesfazer()
    {
        var (_, erro, acao) = await ProporAsync("criar_evento",
            new { titulo = "Reunião com Maria", tipo = "Reuniao", inicio = "2026-10-20 15:00", duracao_minutos = 60, local = "Escritório" });
        Assert.False(erro);

        var r = await Acoes().ConfirmarAsync(acao!.Id, null);
        var e = await _db.Eventos.AsNoTracking().SingleAsync();
        Assert.Equal(new DateTime(2026, 10, 20, 15, 0, 0), e.DataHora);
        Assert.Equal(new DateTime(2026, 10, 20, 16, 0, 0), e.DataHoraFim);
        Assert.Equal(_userId, e.ResponsavelId);
        Assert.Equal($"/pages/agenda.html?date=2026-10-20&abrirId={e.Id}", r!.Link);
        Assert.NotNull(r.DesfazerAte);

        _db.ChangeTracker.Clear();
        Assert.Equal("Desfeita", (await Acoes().DesfazerAsync(acao.Id, null))!.Status);
        Assert.Equal(0, await _db.Eventos.CountAsync());
    }

    [Fact]
    public async Task RemarcarEvento_SoData_MantemHorarioEDuracao_EDesfazVolta()
    {
        var ev = new Evento
        {
            Id = Guid.NewGuid(), TenantId = _tenantId, Titulo = "Audiência", Tipo = TipoEvento.Audiencia,
            DataHora = new DateTime(2026, 10, 20, 14, 0, 0), DataHoraFim = new DateTime(2026, 10, 20, 15, 30, 0),
            ResponsavelId = _userId, CriadoEm = DateTime.UtcNow
        };
        _db.Eventos.Add(ev);
        _db.SaveChanges();
        _db.ChangeTracker.Clear();

        var (_, _, acao) = await ProporAsync("remarcar_evento", new { evento_id = ev.Id.ToString(), inicio = "2026-10-27" });
        await Acoes().ConfirmarAsync(acao!.Id, null);
        var salvo = await _db.Eventos.AsNoTracking().SingleAsync();
        Assert.Equal(new DateTime(2026, 10, 27, 14, 0, 0), salvo.DataHora);
        Assert.Equal(new DateTime(2026, 10, 27, 15, 30, 0), salvo.DataHoraFim);

        _db.ChangeTracker.Clear();
        Assert.Equal("Desfeita", (await Acoes().DesfazerAsync(acao.Id, null))!.Status);
        var voltou = await _db.Eventos.AsNoTracking().SingleAsync();
        Assert.Equal(ev.DataHora, voltou.DataHora);
        Assert.Equal(ev.DataHoraFim, voltou.DataHoraFim);
    }

    // ── Atendimento e horas ──────────────────────────────────────────────────

    [Fact]
    public async Task RegistrarAtendimento_ConfirmarEDesfazer()
    {
        var c = SeedContato();
        var (_, erro, acao) = await ProporAsync("registrar_atendimento",
            new { contato_id = c.Id.ToString(), descricao = "Ligou; vai mandar os documentos", data = "2026-10-01" });
        Assert.False(erro);

        var r = await Acoes().ConfirmarAsync(acao!.Id, null);
        var at = await _db.Atendimentos.AsNoTracking().SingleAsync();
        Assert.Equal(c.Id, at.ContatoId);
        Assert.Equal(new DateTime(2026, 10, 1), at.Data);
        Assert.Equal($"/pages/contato-detalhe.html?id={c.Id}", r!.Link);

        _db.ChangeTracker.Clear();
        await Acoes().DesfazerAsync(acao.Id, null);
        Assert.Equal(0, await _db.Atendimentos.CountAsync());
    }

    [Fact]
    public async Task RegistrarHoras_ConverteHorarioDoEscritorioParaUtc()
    {
        var (_, erro, acao) = await ProporAsync("registrar_horas",
            new { minutos = 90, data = "2026-10-01", inicio = "14:00", descricao = "Contestação" });
        Assert.False(erro);
        Assert.Contains("1h30", acao!.Titulo);

        await Acoes().ConfirmarAsync(acao.Id, null);

        var reg = await _db.RegistrosTempo.AsNoTracking().SingleAsync();
        Assert.Equal(_userId, reg.UsuarioId);
        Assert.Equal(90, reg.DuracaoMinutos);
        // Brasília (padrão, UTC-3): 14:00 local = 17:00 UTC.
        Assert.Equal(new DateTime(2026, 10, 1, 17, 0, 0), reg.Inicio);
    }

    [Fact]
    public async Task RegistrarHoras_NoFuturo_Recusa()
    {
        var (json, _, acao) = await ProporAsync("registrar_horas", new { minutos = 60, data = "2099-01-01" });
        Assert.Contains("futuro", json);
        Assert.Null(acao);
    }

    // ── Desfazer ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Desfazer_AtualizacaoDeContato_VoltaValoresETags()
    {
        var c = SeedContato();
        var (_, _, acao) = await ProporAsync("atualizar_contato",
            new { contato_id = c.Id.ToString(), email = "nova@x.com", tags_remover = new[] { "VIP" } });
        await Acoes().ConfirmarAsync(acao!.Id, null);
        _db.ChangeTracker.Clear();

        var r = await Acoes().DesfazerAsync(acao.Id, null);

        Assert.Equal("Desfeita", r!.Status);
        Assert.Null(r.DesfazerAte);
        var salvo = await _db.Contatos.AsNoTracking().Include(x => x.Tags).SingleAsync();
        Assert.Equal("antigo@x.com", salvo.Email);
        Assert.Equal(["BPC LOAS", "VIP"], salvo.Tags.Select(t => t.Tag).Order());
    }

    [Fact]
    public async Task Desfazer_AlteracaoDePrazo_VoltaPrazoCalculado()
    {
        var t = SeedTarefa();
        var (_, _, acao) = await ProporAsync("alterar_prazo_tarefa", new { tarefa_id = t.Id.ToString(), prazo = "2026-10-22" });
        await Acoes().ConfirmarAsync(acao!.Id, null);
        _db.ChangeTracker.Clear();

        await Acoes().DesfazerAsync(acao.Id, null);

        var salva = await _db.Tarefas.AsNoTracking().SingleAsync();
        Assert.Equal(new DateTime(2026, 10, 15, 9, 0, 0), salva.Prazo);
        Assert.Equal(10, salva.QuantidadeDias);
        Assert.Equal(TipoCalculo.DiasUteis, salva.TipoCalculo);
    }

    [Fact]
    public async Task Desfazer_RegistroAlteradoDepois_Recusa()
    {
        var (_, _, acao) = await ProporAsync("criar_tarefa", new { titulo = "Ligar" });
        await Acoes().ConfirmarAsync(acao!.Id, null);
        var tarefa = await _db.Tarefas.SingleAsync();
        tarefa.AtualizadoEm = DateTime.UtcNow.AddSeconds(5); // editada pela tela depois
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        var r = await Acoes().DesfazerAsync(acao.Id, null);

        Assert.Equal("Confirmada", r!.Status);
        Assert.Contains("alterado depois", r.Erro);
        Assert.Equal(1, await _db.Tarefas.CountAsync());
    }

    [Fact]
    public async Task Desfazer_ForaDaJanela_Recusa()
    {
        var (_, _, acao) = await ProporAsync("criar_tarefa", new { titulo = "Ligar" });
        await Acoes().ConfirmarAsync(acao!.Id, null);
        var gravada = await _db.AcoesAssistente.SingleAsync();
        gravada.ResolvidaEm = DateTime.UtcNow - AssistenteAcoes.JanelaDesfazer - TimeSpan.FromMinutes(1);
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        var r = await Acoes().DesfazerAsync(acao.Id, null);

        Assert.Equal("Confirmada", r!.Status);
        Assert.Null(r.DesfazerAte);
        Assert.Equal(1, await _db.Tarefas.CountAsync());
    }

    [Fact]
    public async Task Desfazer_PorOutroUsuario_NaoEncontra()
    {
        var (_, _, acao) = await ProporAsync("criar_tarefa", new { titulo = "Ligar" });
        await Acoes().ConfirmarAsync(acao!.Id, null);
        _db.ChangeTracker.Clear();

        Assert.Null(await Acoes(Tenant(userId: Guid.NewGuid())).DesfazerAsync(acao.Id, null));
        Assert.Equal(1, await _db.Tarefas.CountAsync());
    }

    // ── Super Admin ──────────────────────────────────────────────────────────

    [Fact]
    public async Task SuperAdmin_MostraAlteracoesPropostasEConfirmadas()
    {
        var tenant = Tenant();
        var llm = new LlmRoteiro(
            new LlmResposta([
                new LlmChamadaFerramenta("c1", "criar_tarefa", Args(new { titulo = "A" })),
                new LlmChamadaFerramenta("c2", "criar_tarefa", Args(new { titulo = "B" }))], 10, 5),
            new LlmResposta([new LlmTexto("Confira as duas tarefas abaixo.")], 20, 8));
        var svc = new AssistenteService(llm, Ferramentas(tenant), _db, tenant, NullLogger<AssistenteService>.Instance);
        var r = await svc.PerguntarAsync(new AssistentePerguntaDto([new("user", "Crie as tarefas A e B")]));
        _db.ChangeTracker.Clear();
        await Acoes(tenant).ConfirmarAsync(r.Acoes![0].Id, null);
        await Acoes(tenant).CancelarAsync(r.Acoes![1].Id);
        // Uma pergunta sem alterações, que o filtro "só com alterações" esconde.
        await new AssistenteService(new LlmRoteiro(new LlmResposta([new LlmTexto("ok")], 1, 1)), Ferramentas(tenant), _db, tenant,
            NullLogger<AssistenteService>.Instance).PerguntarAsync(new AssistentePerguntaDto([new("user", "oi")]));
        var controller = new SuperAdminAssistenteController(_db);

        var lista = Assert.IsType<InteracaoAssistentePagedResultDto>(Assert.IsType<OkObjectResult>(
            (await controller.Listar(_tenantId, null, null, null, null, null, null, comAcoes: true)).Result).Value);
        var item = Assert.Single(lista.Items);
        Assert.Equal(2, item.Acoes);
        Assert.Equal(1, item.AcoesConfirmadas);

        var detalhe = Assert.IsType<InteracaoAssistenteDetalheDto>(
            Assert.IsType<OkObjectResult>((await controller.Detalhe(item.Id, default)).Result).Value);
        Assert.Equal(["Confirmada", "Cancelada"], detalhe.Acoes!.Select(a => a.Status));

        var resumo = Assert.IsType<List<AssistenteResumoTenantDto>>(
            Assert.IsType<OkObjectResult>((await controller.Resumo(null, null, null, default)).Result).Value);
        Assert.Equal((2, 1), (resumo.Single().Acoes, resumo.Single().AcoesConfirmadas));
    }
}
