using System.Text.Json;
using LegalManager.Application.Interfaces;
using LegalManager.Domain.Entities;
using LegalManager.Domain.Enums;
using LegalManager.Domain.Interfaces;
using LegalManager.Infrastructure.Assistente;
using LegalManager.Infrastructure.Persistence;
using LegalManager.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace LegalManager.UnitTests;

/// <summary>
/// Ferramentas de consulta do assistente (somente leitura): filtros, formatos de saída e
/// isolamento por tenant, sobre um escritório com dados de todas as áreas.
/// </summary>
public class AssistenteFerramentasConsultasTests
{
    private readonly AppDbContext _db = new(new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _outroTenant = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _colegaId = Guid.NewGuid();
    private readonly Guid _processoId = Guid.NewGuid();
    private readonly Guid _clienteId = Guid.NewGuid();
    private readonly Guid _contratoId = Guid.NewGuid();
    private readonly DateTime _hoje = DateTime.UtcNow.Date;

    public AssistenteFerramentasConsultasTests()
    {
        _db.Tenants.AddRange(
            new Tenant { Id = _tenantId, Nome = "Escritório" },
            new Tenant { Id = _outroTenant, Nome = "Outro" });
        _db.Users.AddRange(
            new Usuario { Id = _userId, TenantId = _tenantId, Nome = "Ana Advogada", Email = "ana@x.com", UserName = "ana@x.com", Perfil = PerfilUsuario.Admin, Ativo = true },
            new Usuario { Id = _colegaId, TenantId = _tenantId, Nome = "Bruno Colaborador", Email = "bruno@x.com", UserName = "bruno@x.com", Perfil = PerfilUsuario.Colaborador, Ativo = true },
            new Usuario { Id = Guid.NewGuid(), TenantId = _tenantId, Nome = "Cliente Portal", Email = "c@x.com", UserName = "c@x.com", Perfil = PerfilUsuario.Cliente, Ativo = true },
            new Usuario { Id = Guid.NewGuid(), TenantId = _outroTenant, Nome = "Intruso", Email = "i@x.com", UserName = "i@x.com", Perfil = PerfilUsuario.Admin, Ativo = true });

        var cliente = new Contato
        {
            Id = _clienteId, TenantId = _tenantId, Nome = "Maria Cliente", Tipo = TipoPessoa.PF, TipoContato = TipoContato.Cliente,
            CpfCnpj = "12345678900", Email = "maria@cliente.com", Telefone = "11999990000", Cidade = "São Paulo", Estado = "SP",
            Endereco = "Rua A, 1", Cep = "01000-000", DataNascimento = new DateTime(1980, 5, 1), Observacoes = "Cliente antiga",
            Ativo = true, CriadoEm = DateTime.UtcNow
        };
        cliente.Tags.Add(new ContatoTag { Id = Guid.NewGuid(), ContatoId = _clienteId, Tag = "BPC LOAS" });
        var empresa = new Contato
        {
            Id = Guid.NewGuid(), TenantId = _tenantId, Nome = "Empresa Ré Ltda", Tipo = TipoPessoa.PJ, TipoContato = TipoContato.ParteContraria,
            ImportadoAutomaticamente = true, Ativo = true, CriadoEm = DateTime.UtcNow
        };
        empresa.Tags.Add(new ContatoTag { Id = Guid.NewGuid(), ContatoId = empresa.Id, Tag = "Trabalhista" });
        var inativo = new Contato
        {
            Id = Guid.NewGuid(), TenantId = _tenantId, Nome = "Zé Inativo", Tipo = TipoPessoa.PF, TipoContato = TipoContato.Testemunha,
            Ativo = false, CriadoEm = DateTime.UtcNow
        };
        _db.Contatos.AddRange(cliente, empresa, inativo,
            new Contato { Id = Guid.NewGuid(), TenantId = _outroTenant, Nome = "Maria de Outro", Tipo = TipoPessoa.PF, Ativo = true });

        var processo = new Processo
        {
            Id = _processoId, TenantId = _tenantId, NumeroCNJ = "0001234-56.2024.8.26.0100", TipoAcao = "Ação de Cobrança",
            Classe = "Procedimento Comum", Tribunal = "TJSP", Comarca = "São Paulo", Vara = "1ª Vara Cível",
            AreaDireito = AreaDireito.Civil, Status = StatusProcesso.Ativo, AdvogadoResponsavelId = _userId,
            ValorCausa = 10000, Monitorado = true, Observacoes = new string('x', 1200),
            DataDistribuicao = _hoje.AddYears(-1), UltimoAndamentoEm = _hoje.AddDays(-2), CriadoEm = DateTime.UtcNow
        };
        processo.Partes.Add(new ProcessoParte { Id = Guid.NewGuid(), ProcessoId = _processoId, ContatoId = _clienteId, TipoParte = TipoParteProcesso.Autor });
        processo.Partes.Add(new ProcessoParte { Id = Guid.NewGuid(), ProcessoId = _processoId, ContatoId = empresa.Id, TipoParte = TipoParteProcesso.Reu });
        processo.Favoritos.Add(new ProcessoFavorito { UsuarioId = _userId, ProcessoId = _processoId, CriadoEm = DateTime.UtcNow });
        var arquivado = new Processo
        {
            Id = Guid.NewGuid(), TenantId = _tenantId, NumeroCNJ = "0009999-00.2020.5.02.0001", TipoAcao = "Reclamação Trabalhista",
            AreaDireito = AreaDireito.Trabalhista, Status = StatusProcesso.Arquivado, CriadoEm = DateTime.UtcNow.AddYears(-2)
        };
        _db.Processos.AddRange(processo, arquivado);

        _db.Andamentos.Add(new Andamento
        {
            Id = Guid.NewGuid(), TenantId = _tenantId, ProcessoId = _processoId, Data = _hoje.AddDays(-2),
            Tipo = TipoAndamento.Despacho, Descricao = "Cite-se o réu.", DescricaoTraduzidaIA = "O juiz mandou citar.", CriadoEm = DateTime.UtcNow
        });

        _db.Tarefas.AddRange(
            new Tarefa { Id = Guid.NewGuid(), TenantId = _tenantId, Titulo = "Contestação atrasada", Tipo = TipoTarefa.Prazo, Status = StatusTarefa.Pendente, Prazo = _hoje.AddDays(-3), ResponsavelId = _userId, ProcessoId = _processoId, ContatoId = _clienteId, Descricao = "Prazo de 15 dias", CriadoEm = DateTime.UtcNow, CriadoPorId = _userId },
            new Tarefa { Id = Guid.NewGuid(), TenantId = _tenantId, Titulo = "Ligar para cliente", Tipo = TipoTarefa.Tarefa, Status = StatusTarefa.EmAndamento, Prazo = _hoje.AddDays(5).AddHours(14), ResponsavelId = _colegaId, ContatoId = _clienteId, CriadoEm = DateTime.UtcNow, CriadoPorId = _userId },
            new Tarefa { Id = Guid.NewGuid(), TenantId = _tenantId, Titulo = "Tarefa concluída", Tipo = TipoTarefa.Tarefa, Status = StatusTarefa.Concluida, ConcluidaEm = DateTime.UtcNow, ResponsavelId = _userId, CriadoEm = DateTime.UtcNow, CriadoPorId = _userId },
            new Tarefa { Id = Guid.NewGuid(), TenantId = _outroTenant, Titulo = "Tarefa de outro", Status = StatusTarefa.Pendente, CriadoEm = DateTime.UtcNow, CriadoPorId = _userId });

        _db.Eventos.AddRange(
            new Evento { Id = Guid.NewGuid(), TenantId = _tenantId, Titulo = "Audiência de conciliação", Tipo = TipoEvento.Audiencia, DataHora = _hoje.AddDays(3).AddHours(10), DataHoraFim = _hoje.AddDays(3).AddHours(11), Local = "Fórum", ResponsavelId = _userId, ProcessoId = _processoId, CriadoEm = DateTime.UtcNow },
            new Evento { Id = Guid.NewGuid(), TenantId = _tenantId, Titulo = "Reunião interna", Tipo = TipoEvento.Reuniao, DataHora = _hoje.AddDays(10).AddHours(9), ResponsavelId = _colegaId, Observacoes = "Pauta mensal", CriadoEm = DateTime.UtcNow },
            new Evento { Id = Guid.NewGuid(), TenantId = _outroTenant, Titulo = "Evento de outro", Tipo = TipoEvento.Reuniao, DataHora = _hoje.AddDays(2), CriadoEm = DateTime.UtcNow });

        _db.RegistrosTempo.AddRange(
            new RegistroTempo { Id = Guid.NewGuid(), TenantId = _tenantId, UsuarioId = _userId, ProcessoId = _processoId, Inicio = DateTime.UtcNow.AddHours(-3), DuracaoMinutos = 90, Descricao = "Redação da inicial" },
            new RegistroTempo { Id = Guid.NewGuid(), TenantId = _tenantId, UsuarioId = _colegaId, Inicio = DateTime.UtcNow.AddDays(-1), DuracaoMinutos = 30, Descricao = "Pesquisa" },
            new RegistroTempo { Id = Guid.NewGuid(), TenantId = _tenantId, UsuarioId = _colegaId, Inicio = DateTime.UtcNow.AddMinutes(-10), EmAndamento = true });

        var inicioMes = new DateTime(_hoje.Year, _hoje.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        _db.LancamentosFinanceiros.AddRange(
            new LancamentoFinanceiro { Id = Guid.NewGuid(), TenantId = _tenantId, Tipo = TipoLancamento.Receita, Categoria = "Honorários", Valor = 1000, DataVencimento = inicioMes, DataPagamento = inicioMes, Status = StatusLancamento.Pago, ContatoId = _clienteId, ProcessoId = _processoId },
            new LancamentoFinanceiro { Id = Guid.NewGuid(), TenantId = _tenantId, Tipo = TipoLancamento.Despesa, Categoria = "Custas", Valor = 200, DataVencimento = inicioMes, Status = StatusLancamento.Pago },
            new LancamentoFinanceiro { Id = Guid.NewGuid(), TenantId = _tenantId, Tipo = TipoLancamento.Receita, Categoria = "Honorários", Valor = 500, DataVencimento = _hoje.AddDays(-40), Status = StatusLancamento.Pendente, ContatoId = _clienteId, Descricao = "Parcela antiga" },
            new LancamentoFinanceiro { Id = Guid.NewGuid(), TenantId = _tenantId, Tipo = TipoLancamento.Despesa, Categoria = "Aluguel", Valor = 300, DataVencimento = _hoje.AddDays(20), Status = StatusLancamento.Pendente },
            new LancamentoFinanceiro { Id = Guid.NewGuid(), TenantId = _tenantId, Tipo = TipoLancamento.Receita, Categoria = "Outros", Valor = 50, DataVencimento = inicioMes, Status = StatusLancamento.Cancelado },
            new LancamentoFinanceiro { Id = Guid.NewGuid(), TenantId = _outroTenant, Tipo = TipoLancamento.Receita, Categoria = "Honorários", Valor = 99999, DataVencimento = inicioMes, Status = StatusLancamento.Pago });

        var contrato = new ContratoHonorario
        {
            Id = _contratoId, TenantId = _tenantId, ContatoId = _clienteId, ProcessoId = _processoId, NumeroContrato = "HON-2026/0001",
            Objeto = "Ação de cobrança", ValorTotal = 3000, FormaPagamento = FormaPagamentoContrato.Parcelado,
            Periodicidade = PeriodicidadeParcela.Mensal, NumeroParcelas = 3, ValorEntrada = 0, Status = StatusContratoHonorario.Ativo,
            DataInicio = _hoje.AddMonths(-3), CriadoPorId = _userId, Observacoes = "Contrato padrão"
        };
        contrato.Parcelas.Add(new ParcelaHonorario { Id = Guid.NewGuid(), TenantId = _tenantId, ContratoId = _contratoId, Numero = 1, Vencimento = _hoje.AddMonths(-2), ValorOriginal = 1000, ValorPago = 1000, DataPagamento = _hoje.AddMonths(-2), Status = StatusParcelaHonorario.Pago });
        contrato.Parcelas.Add(new ParcelaHonorario { Id = Guid.NewGuid(), TenantId = _tenantId, ContratoId = _contratoId, Numero = 2, Vencimento = _hoje.AddMonths(-1), ValorOriginal = 1000, Status = StatusParcelaHonorario.Pendente });
        contrato.Parcelas.Add(new ParcelaHonorario { Id = Guid.NewGuid(), TenantId = _tenantId, ContratoId = _contratoId, Numero = 3, Vencimento = _hoje.AddMonths(1), ValorOriginal = 1000, Status = StatusParcelaHonorario.Pendente });
        var quitado = new ContratoHonorario
        {
            Id = Guid.NewGuid(), TenantId = _tenantId, ContatoId = empresa.Id, NumeroContrato = "HON-2025/0007", ValorTotal = 500,
            FormaPagamento = FormaPagamentoContrato.AVista, Status = StatusContratoHonorario.Quitado, DataInicio = _hoje.AddYears(-1), CriadoPorId = _userId
        };
        quitado.Parcelas.Add(new ParcelaHonorario { Id = Guid.NewGuid(), TenantId = _tenantId, ContratoId = quitado.Id, Numero = 1, Vencimento = _hoje.AddMonths(-10), ValorOriginal = 500, Status = StatusParcelaHonorario.Pago });
        _db.ContratosHonorarios.AddRange(contrato, quitado);

        var pubId = Guid.NewGuid();
        _db.Publicacoes.AddRange(
            new Publicacao { Id = pubId, TenantId = _tenantId, ProcessoId = _processoId, NumeroCNJ = processo.NumeroCNJ, Diario = "DJSP", DataPublicacao = _hoje.AddDays(-1), Conteudo = "Intimação para manifestação em 15 dias.", Tipo = TipoPublicacao.Intimacao, Status = StatusPublicacao.Nova, Urgente = true, ClassificacaoIA = "Prazo de manifestação" },
            new Publicacao { Id = Guid.NewGuid(), TenantId = _tenantId, Diario = "DJEN", DataPublicacao = _hoje.AddDays(-30), Conteudo = "Conteúdo longo", Snippet = "Trecho", Tipo = TipoPublicacao.Despacho, Status = StatusPublicacao.Lida });
        _db.TriagensPublicacao.Add(new TriagemPublicacao
        {
            Id = Guid.NewGuid(), TenantId = _tenantId, PublicacaoId = pubId, Status = StatusTriagem.Pendente, Ato = "Manifestação",
            Vencimento = _hoje.AddDays(15), AudienciaDataHora = _hoje.AddDays(30).AddHours(14), CriadoEm = DateTime.UtcNow
        });

        _db.SaveChanges();
    }

    private AssistenteFerramentas Ferramentas(PlanoTipo plano = PlanoTipo.Pro)
    {
        var t = new Mock<ITenantContext>();
        t.Setup(x => x.TenantId).Returns(_tenantId);
        t.Setup(x => x.UserId).Returns(_userId);
        t.Setup(x => x.UserRole).Returns("Admin");
        t.Setup(x => x.Plano).Returns(plano);
        return new AssistenteFerramentas(_db, t.Object, new HonorarioService(_db, Mock.Of<IAuditService>()));
    }

    private async Task<JsonElement> Executar(string nome, object args, PlanoTipo plano = PlanoTipo.Pro)
    {
        var (json, erro) = await Ferramentas(plano).ExecutarAsync(nome, JsonSerializer.SerializeToElement(args), default);
        Assert.False(erro, json);
        return JsonDocument.Parse(json).RootElement;
    }

    private static string D(DateTime d) => d.ToString("yyyy-MM-dd");

    // ── Processos ────────────────────────────────────────────────────────────

    [Fact]
    public async Task BuscarProcessos_FiltraPorStatusAreaResponsavelEFavoritos()
    {
        var r = await Executar("buscar_processos", new { status = "Ativo", area = "Civil", responsavel = "ana", apenas_favoritos = true, limite = 5 });

        Assert.Equal(1, r.GetProperty("total").GetInt32());
        var p = r.GetProperty("processos")[0];
        Assert.Equal("Ana Advogada", p.GetProperty("responsavel").GetString());
        Assert.Contains(p.GetProperty("partes").EnumerateArray(), x => x.GetString() == "Maria Cliente (Autor)");
    }

    [Fact]
    public async Task BuscarProcessos_PorNomeDaParte()
    {
        var r = await Executar("buscar_processos", new { texto = "empresa ré" });
        Assert.Equal(1, r.GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task DetalharProcesso_PorNumeroSoComDigitos_TrazAndamentosTarefasEEventos()
    {
        var r = await Executar("detalhar_processo", new { numero_cnj = "00012345620248260100" });

        Assert.Equal(_processoId, r.GetProperty("id").GetGuid());
        Assert.Equal("Ana Advogada", r.GetProperty("responsavel").GetString());
        Assert.EndsWith("…", r.GetProperty("observacoes").GetString());
        Assert.Equal("O juiz mandou citar.", r.GetProperty("ultimosAndamentos")[0].GetProperty("explicacao").GetString());
        Assert.Equal(1, r.GetProperty("tarefasAbertas").GetArrayLength());
        Assert.Equal("Audiencia", r.GetProperty("proximosEventos")[0].GetProperty("tipo").GetString());
        Assert.Equal(2, r.GetProperty("partes").GetArrayLength());
    }

    [Fact]
    public async Task DetalharProcesso_PorId_DeOutroTenant_NaoEncontra()
    {
        var alheio = new Processo { Id = Guid.NewGuid(), TenantId = _outroTenant, NumeroCNJ = "1", CriadoEm = DateTime.UtcNow };
        _db.Processos.Add(alheio);
        _db.SaveChanges();

        var r = await Executar("detalhar_processo", new { processo_id = alheio.Id });
        Assert.Equal("Processo não encontrado.", r.GetProperty("erro").GetString());
    }

    [Fact]
    public async Task DetalharProcesso_SemIdentificador_RetornaErro()
    {
        var (json, erro) = await Ferramentas().ExecutarAsync("detalhar_processo", JsonSerializer.SerializeToElement(new { }), default);
        Assert.True(erro);
        Assert.Contains("processo_id ou numero_cnj", json);
    }

    // ── Tarefas / agenda / timesheet ────────────────────────────────────────

    [Fact]
    public async Task ListarTarefas_SemStatus_SoAbertas()
    {
        var r = await Executar("listar_tarefas", new { });
        Assert.Equal(2, r.GetProperty("total").GetInt32());
        Assert.True(r.GetProperty("tarefas")[0].GetProperty("atrasada").GetBoolean());
    }

    [Fact]
    public async Task ListarTarefas_Atrasadas_DoProcessoEContato()
    {
        var r = await Executar("listar_tarefas", new
        {
            atrasadas = true, tipo = "Prazo", apenas_minhas = true, responsavel = "Ana",
            processo_id = _processoId, contato_id = _clienteId, prazo_de = D(_hoje.AddDays(-10)), prazo_ate = D(_hoje)
        });

        Assert.Equal(1, r.GetProperty("total").GetInt32());
        var t = r.GetProperty("tarefas")[0];
        Assert.Equal("Contestação atrasada", t.GetProperty("titulo").GetString());
        Assert.Equal("Maria Cliente", t.GetProperty("contato").GetString());
        Assert.NotNull(t.GetProperty("processoLink").GetString());
    }

    [Fact]
    public async Task ListarTarefas_StatusConcluida_TrazConcluidaEm()
    {
        var r = await Executar("listar_tarefas", new { status = "Concluida" });
        Assert.Equal(1, r.GetProperty("total").GetInt32());
        Assert.NotNull(r.GetProperty("tarefas")[0].GetProperty("concluidaEm").GetString());
    }

    [Fact]
    public async Task ListarEventos_PadraoProximos30Dias_EFiltros()
    {
        var todos = await Executar("listar_eventos", new { });
        Assert.Equal(2, todos.GetProperty("total").GetInt32());

        var meus = await Executar("listar_eventos", new { de = D(_hoje), ate = D(_hoje.AddDays(5)), tipo = "Audiencia", responsavel = "ana", apenas_meus = true, processo_id = _processoId });
        Assert.Equal(1, meus.GetProperty("total").GetInt32());
        var e = meus.GetProperty("eventos")[0];
        Assert.Equal("Fórum", e.GetProperty("local").GetString());
        Assert.Contains("abrirId=", e.GetProperty("link").GetString());
    }

    [Fact]
    public async Task ListarRegistrosTempo_SomaMinutosEFiltra()
    {
        var r = await Executar("listar_registros_tempo", new { });
        Assert.Equal(3, r.GetProperty("total").GetInt32());
        Assert.Equal(120, r.GetProperty("totalMinutos").GetInt32());

        var meus = await Executar("listar_registros_tempo", new { apenas_meus = true, usuario = "ana", processo_id = _processoId, de = D(_hoje.AddDays(-1)), ate = D(_hoje.AddDays(1)) });
        Assert.Equal(1, meus.GetProperty("total").GetInt32());
        Assert.Equal("0001234-56.2024.8.26.0100", meus.GetProperty("registros")[0].GetProperty("processo").GetString());
    }

    [Fact]
    public async Task ListarEquipe_IgnoraClientesEOutrosTenants()
    {
        var r = await Executar("listar_equipe", new { });
        var nomes = r.GetProperty("equipe").EnumerateArray().Select(u => u.GetProperty("nome").GetString()).ToList();
        Assert.Equal(["Ana Advogada", "Bruno Colaborador"], nomes);
        Assert.Equal(_userId, r.GetProperty("usuarioAtualId").GetGuid());
    }

    // ── Contatos ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task BuscarContatos_PorTagSemAcentoETipos()
    {
        var r = await Executar("buscar_contatos", new { tags = "bpc", tipo_contato = new[] { "Cliente" }, pessoa = "PF", importado = "false", texto = "maria" });

        Assert.Equal(1, r.GetProperty("total").GetInt32());
        var c = r.GetProperty("contatos")[0];
        Assert.Equal("Maria Cliente", c.GetProperty("nome").GetString());
        Assert.Equal("BPC LOAS", r.GetProperty("tagsFiltradas")[0].GetString());
    }

    [Fact]
    public async Task BuscarContatos_TagInexistente_ListaAsExistentes()
    {
        var r = await Executar("buscar_contatos", new { tags = new[] { "nada" } });
        Assert.Equal(0, r.GetProperty("total").GetInt32());
        Assert.Equal(2, r.GetProperty("tagsExistentes").GetArrayLength());
    }

    [Fact]
    public async Task BuscarContatos_IncluirInativosEImportados()
    {
        var inativos = await Executar("buscar_contatos", new { incluir_inativos = true });
        Assert.Equal(3, inativos.GetProperty("total").GetInt32());

        var importados = await Executar("buscar_contatos", new { importado = true });
        Assert.Equal("Empresa Ré Ltda", importados.GetProperty("contatos")[0].GetProperty("nome").GetString());
    }

    [Fact]
    public async Task ListarTagsContatos_ContaPorTag()
    {
        var (json, erro) = await Ferramentas().ExecutarAsync("listar_tags_contatos", JsonSerializer.SerializeToElement(new { }), default);
        Assert.False(erro);
        Assert.Contains("BPC LOAS", json);
        Assert.Contains("Trabalhista", json);
    }

    [Fact]
    public async Task DetalharContato_PlanoCompleto_TrazFinanceiroEContratos()
    {
        var r = await Executar("detalhar_contato", new { contato_id = _clienteId }, PlanoTipo.Pro);

        Assert.Equal("Rua A, 1, São Paulo, SP, 01000-000", r.GetProperty("endereco").GetString());
        Assert.Equal(1, r.GetProperty("processos").GetArrayLength());
        Assert.Equal(2, r.GetProperty("tarefasAbertas").GetArrayLength());
        Assert.Equal(1, r.GetProperty("financeiro").GetProperty("lancamentosPendentes").GetArrayLength());
        Assert.Equal(1, r.GetProperty("contratosHonorario")[0].GetProperty("parcelasEmAtraso").GetInt32());
    }

    [Fact]
    public async Task DetalharContato_PlanoFree_SemFinanceiro()
    {
        var r = await Executar("detalhar_contato", new { contato_id = _clienteId }, PlanoTipo.Free);
        Assert.False(r.TryGetProperty("financeiro", out _));
        Assert.False(r.TryGetProperty("contratosHonorario", out _));
    }

    [Fact]
    public async Task DetalharContato_Inexistente_OuSemId()
    {
        var r = await Executar("detalhar_contato", new { contato_id = Guid.NewGuid() });
        Assert.Equal("Contato não encontrado.", r.GetProperty("erro").GetString());

        var (_, erro) = await Ferramentas().ExecutarAsync("detalhar_contato", JsonSerializer.SerializeToElement(new { contato_id = "não-é-guid" }), default);
        Assert.True(erro);
    }

    // ── Financeiro ───────────────────────────────────────────────────────────

    [Fact]
    public async Task ResumoFinanceiro_MesAtual_IgnoraCanceladosEOutroTenant()
    {
        var r = await Executar("resumo_financeiro", new { });

        Assert.Equal(1000m, r.GetProperty("receitasRecebidas").GetDecimal());
        Assert.Equal(200m, r.GetProperty("despesasPagas").GetDecimal());
        Assert.Equal(800m, r.GetProperty("saldoRealizado").GetDecimal());
        var vencidos = r.GetProperty("vencidosEmQualquerData");
        Assert.Equal(500m, vencidos[0].GetProperty("total").GetDecimal());
    }

    [Fact]
    public async Task ResumoFinanceiro_PeriodoExplicito()
    {
        var r = await Executar("resumo_financeiro", new { de = D(_hoje.AddDays(-60)), ate = D(_hoje.AddDays(30)) });
        Assert.Equal(500m, r.GetProperty("aReceber").GetDecimal());
        Assert.Equal(300m, r.GetProperty("aPagar").GetDecimal());
        Assert.Equal(500m, r.GetProperty("vencidosNoPeriodo").GetProperty("receitas").GetDecimal());
    }

    [Fact]
    public async Task ListarLancamentos_Filtros()
    {
        var vencidos = await Executar("listar_lancamentos", new { vencidos = true, tipo = "Receita", contato_id = _clienteId });
        Assert.Equal(1, vencidos.GetProperty("total").GetInt32());
        Assert.True(vencidos.GetProperty("lancamentos")[0].GetProperty("vencido").GetBoolean());

        var pagos = await Executar("listar_lancamentos", new { status = "Pago", processo_id = _processoId, de = D(_hoje.AddMonths(-1)), ate = D(_hoje) });
        Assert.Equal(1000m, pagos.GetProperty("valorTotal").GetDecimal());
        Assert.Equal("Maria Cliente", pagos.GetProperty("lancamentos")[0].GetProperty("contato").GetString());
    }

    [Fact]
    public async Task Financeiro_IndisponivelNoPlanoFree()
    {
        var (json, erro) = await Ferramentas(PlanoTipo.Free).ExecutarAsync("listar_lancamentos", JsonSerializer.SerializeToElement(new { }), default);
        Assert.True(erro);
        Assert.Contains("indisponível", json);
    }

    // ── Honorários ───────────────────────────────────────────────────────────

    [Fact]
    public async Task ResumoHonorarios_ListaClienteEmAtraso()
    {
        var r = await Executar("resumo_honorarios", new { });
        Assert.Equal("Maria Cliente", r.GetProperty("clientesEmAtraso")[0].GetProperty("cliente").GetString());
        Assert.True(r.GetProperty("totalEmAtraso").GetDecimal() >= 1000m);
    }

    [Fact]
    public async Task ListarContratos_EmAtrasoCalculaMultaEJuros()
    {
        var r = await Executar("listar_contratos_honorario", new { em_atraso = true, texto = "maria", contato_id = _clienteId, status = "Ativo" });

        Assert.Equal(1, r.GetProperty("total").GetInt32());
        var k = r.GetProperty("contratos")[0];
        Assert.True(k.GetProperty("emAtraso").GetBoolean());
        Assert.Equal(1000m, k.GetProperty("valorPago").GetDecimal());
        Assert.Equal(1000m, k.GetProperty("valorAVencer").GetDecimal());
        Assert.True(k.GetProperty("valorEmAtrasoComMultaEJuros").GetDecimal() > 1000m);
        Assert.Equal(3, k.GetProperty("parcelasTotal").GetInt32());
    }

    [Fact]
    public async Task ListarContratos_SemFiltro_TrazTodosDoTenant()
    {
        var r = await Executar("listar_contratos_honorario", new { });
        Assert.Equal(2, r.GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task DetalharContrato_ParcelasComSituacao()
    {
        var r = await Executar("detalhar_contrato_honorario", new { contrato_id = _contratoId });

        Assert.Equal("HON-2026/0001", r.GetProperty("numero").GetString());
        Assert.Equal("Mensal", r.GetProperty("periodicidade").GetString());
        var situacoes = r.GetProperty("parcelas").EnumerateArray().Select(p => p.GetProperty("situacao").GetString()).ToList();
        Assert.Equal(["Pago", "EmAtraso", "Pendente"], situacoes);
        Assert.True(r.GetProperty("parcelas")[1].GetProperty("valorAtualizadoComMultaEJuros").GetDecimal() > 1000m);
    }

    [Fact]
    public async Task DetalharContrato_Inexistente()
    {
        var r = await Executar("detalhar_contrato_honorario", new { contrato_id = Guid.NewGuid() });
        Assert.Equal("Contrato não encontrado.", r.GetProperty("erro").GetString());
    }

    // ── Publicações ──────────────────────────────────────────────────────────

    [Fact]
    public async Task ListarPublicacoes_ComSugestaoDaTriagem()
    {
        var r = await Executar("listar_publicacoes", new { urgentes = true, status = "Nova", processo_id = _processoId, de = D(_hoje.AddDays(-7)), ate = D(_hoje) });

        Assert.Equal(1, r.GetProperty("total").GetInt32());
        var p = r.GetProperty("publicacoes")[0];
        Assert.Equal("Manifestação", p.GetProperty("sugestaoIA").GetProperty("providencia").GetString());
        Assert.Equal(D(_hoje.AddDays(15)), p.GetProperty("sugestaoIA").GetProperty("vencimentoCalculado").GetString());
    }

    [Fact]
    public async Task ListarPublicacoes_PrefereSnippetAoConteudo()
    {
        var r = await Executar("listar_publicacoes", new { status = "Lida" });
        Assert.Equal("Trecho", r.GetProperty("publicacoes")[0].GetProperty("trecho").GetString());
    }
}
