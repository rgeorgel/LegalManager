using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using LegalManager.Application.Interfaces;
using LegalManager.Domain;
using LegalManager.Domain.Enums;
using LegalManager.Domain.Interfaces;
using LegalManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LegalManager.Infrastructure.Assistente;

/// <summary>
/// Ferramentas (somente leitura) que o assistente de IA pode chamar para consultar os dados do
/// escritório. Regras de segurança:
/// <list type="bullet">
/// <item>O tenant vem SEMPRE do <see cref="ITenantContext"/> da requisição — nenhuma ferramenta
/// recebe tenantId como argumento, e toda consulta filtra por ele.</item>
/// <item>Ferramentas de módulos fora do plano não são oferecidas ao modelo (<see cref="Disponiveis"/>)
/// e são recusadas de novo na execução (<see cref="ExecutarAsync"/>).</item>
/// </list>
/// Prazos/eventos saem na hora "de parede" do escritório (ver CLAUDE.md → Datas).
/// </summary>
public class AssistenteFerramentas
{
    private const int LimitePadrao = 20;
    private const int LimiteMaximo = 50;

    private readonly AppDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly IHonorarioService _honorarios;
    private readonly IReadOnlyList<Definicao> _definicoes;
    private TimeZoneInfo? _tz;

    private sealed record Definicao(
        LlmFerramenta Ferramenta,
        Func<PlanoTipo, bool> Permite,
        Func<JsonElement, CancellationToken, Task<object>> Executar);

    public AssistenteFerramentas(AppDbContext db, ITenantContext tenant, IHonorarioService honorarios)
    {
        _db = db;
        _tenant = tenant;
        _honorarios = honorarios;
        _definicoes = CriarDefinicoes();
    }

    private Guid TenantId => _tenant.TenantId;

    public IReadOnlyList<LlmFerramenta> Disponiveis() =>
        _definicoes.Where(d => d.Permite(_tenant.Plano)).Select(d => d.Ferramenta).ToList();

    /// <summary>Executa a ferramenta e devolve o resultado em JSON (ou um JSON de erro).</summary>
    public async Task<(string Json, bool Erro)> ExecutarAsync(string nome, JsonElement args, CancellationToken ct)
    {
        var def = _definicoes.FirstOrDefault(d => d.Ferramenta.Nome == nome);
        if (def is null || !def.Permite(_tenant.Plano))
            return (Json(new { erro = $"Ferramenta '{nome}' indisponível." }), true);
        try
        {
            return (Json(await def.Executar(args, ct)), false);
        }
        catch (ArgumentException ex)
        {
            return (Json(new { erro = ex.Message }), true);
        }
    }

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private static string Json(object o) => JsonSerializer.Serialize(o, JsonOpts);

    // ── Definições ───────────────────────────────────────────────────────────

    private List<Definicao> CriarDefinicoes() =>
    [
        new(new LlmFerramenta("buscar_processos",
                "Busca processos do escritório. Use para listar, contar ou localizar processos por número CNJ, " +
                "nome de parte/cliente, tipo de ação, tribunal, status, área ou advogado responsável.",
                Schema(
                    P("texto", "string", "Trecho do número CNJ, nome de uma parte, tipo de ação, tribunal ou comarca."),
                    P("status", "string", "Status do processo.", Nomes<StatusProcesso>()),
                    P("area", "string", "Área do direito.", Nomes<AreaDireito>()),
                    P("responsavel", "string", "Nome (ou parte do nome) do advogado responsável."),
                    P("apenas_favoritos", "boolean", "Só os processos marcados como favoritos pelo usuário atual."),
                    P("limite", "integer", $"Máximo de itens (padrão {LimitePadrao}, máx. {LimiteMaximo})."))),
            _ => true, BuscarProcessosAsync),

        new(new LlmFerramenta("detalhar_processo",
                "Detalhes de um processo: dados, partes, últimos andamentos, tarefas/prazos em aberto e próximos eventos.",
                Schema(
                    P("processo_id", "string", "Id (GUID) do processo, obtido em buscar_processos."),
                    P("numero_cnj", "string", "Número CNJ, se o id não for conhecido."))),
            _ => true, DetalharProcessoAsync),

        new(new LlmFerramenta("listar_tarefas",
                "Lista tarefas e prazos (Tarefa.Prazo). Use para 'prazos da semana', 'tarefas atrasadas', " +
                "'o que fulano tem para fazer' etc. Datas no formato AAAA-MM-DD, no fuso do escritório.",
                Schema(
                    P("status", "string", "Status. Sem status: só as abertas (Pendente, EmAndamento, Suspensa).", Nomes<StatusTarefa>()),
                    P("tipo", "string", "Tarefa comum ou prazo processual.", Nomes<TipoTarefa>()),
                    P("prazo_de", "string", "Prazo a partir desta data (inclusive)."),
                    P("prazo_ate", "string", "Prazo até esta data (inclusive)."),
                    P("atrasadas", "boolean", "Só as abertas com prazo já vencido."),
                    P("responsavel", "string", "Nome (ou parte do nome) do responsável."),
                    P("apenas_minhas", "boolean", "Só as do usuário atual."),
                    P("processo_id", "string", "Só as de um processo (GUID)."),
                    P("contato_id", "string", "Só as de um contato (GUID)."),
                    P("limite", "integer", $"Máximo de itens (padrão {LimitePadrao}, máx. {LimiteMaximo})."))),
            _ => true, ListarTarefasAsync),

        new(new LlmFerramenta("listar_eventos",
                "Lista eventos da agenda (audiências, reuniões, perícias...). Sem datas: próximos 30 dias. " +
                "Datas no formato AAAA-MM-DD, no fuso do escritório.",
                Schema(
                    P("de", "string", "A partir desta data (inclusive)."),
                    P("ate", "string", "Até esta data (inclusive)."),
                    P("tipo", "string", "Tipo de evento.", Nomes<TipoEvento>()),
                    P("responsavel", "string", "Nome (ou parte do nome) do responsável."),
                    P("apenas_meus", "boolean", "Só os do usuário atual."),
                    P("processo_id", "string", "Só os de um processo (GUID)."),
                    P("limite", "integer", $"Máximo de itens (padrão {LimitePadrao}, máx. {LimiteMaximo})."))),
            _ => true, ListarEventosAsync),

        new(new LlmFerramenta("buscar_contatos",
                "Busca contatos (clientes, partes contrárias, testemunhas, peritos...) com os mesmos filtros da tela " +
                "de Contatos: texto (nome, CPF/CNPJ ou e-mail), tags, categoria, pessoa física/jurídica e origem. " +
                "Para saber quais tags existem, use listar_tags_contatos.",
                Schema(
                    P("texto", "string", "Trecho do nome, CPF/CNPJ ou e-mail."),
                    PLista("tags", "Tags do contato (qualquer uma delas). Aceita parte do nome da tag, sem diferenciar maiúsculas e acentos — ex.: \"BPC\" encontra \"BPC LOAS\"."),
                    PLista("tipo_contato", "Categorias do contato (qualquer uma delas).", Nomes<TipoContato>()),
                    P("pessoa", "string", "PF (pessoa física) ou PJ (pessoa jurídica).", Nomes<TipoPessoa>()),
                    P("importado", "boolean", "true: só importados automaticamente (ex.: importação por OAB); false: só cadastrados manualmente."),
                    P("incluir_inativos", "boolean", "Inclui contatos inativos."),
                    P("limite", "integer", $"Máximo de itens (padrão {LimitePadrao}, máx. {LimiteMaximo})."))),
            _ => true, BuscarContatosAsync),

        new(new LlmFerramenta("listar_tags_contatos",
                "Lista as tags usadas nos contatos do escritório, com a quantidade de contatos ativos em cada uma.",
                Schema()),
            _ => true, ListarTagsContatosAsync),

        new(new LlmFerramenta("detalhar_contato",
                "Detalhes de um contato: dados cadastrais, processos em que é parte, tarefas em aberto e, " +
                "se o plano permitir, lançamentos financeiros pendentes e contratos de honorários.",
                Schema(P("contato_id", "string", "Id (GUID) do contato, obtido em buscar_contatos."), "contato_id")),
            _ => true, DetalharContatoAsync),

        new(new LlmFerramenta("listar_equipe",
                "Lista os usuários (membros da equipe) do escritório, com perfil e situação.",
                Schema()),
            _ => true, ListarEquipeAsync),

        new(new LlmFerramenta("listar_registros_tempo",
                "Lista registros de horas trabalhadas (timesheet), com total em minutos. Datas AAAA-MM-DD.",
                Schema(
                    P("de", "string", "A partir desta data (inclusive)."),
                    P("ate", "string", "Até esta data (inclusive)."),
                    P("usuario", "string", "Nome (ou parte do nome) de quem registrou."),
                    P("apenas_meus", "boolean", "Só os do usuário atual."),
                    P("processo_id", "string", "Só os de um processo (GUID)."),
                    P("limite", "integer", $"Máximo de itens (padrão {LimitePadrao}, máx. {LimiteMaximo})."))),
            _ => true, ListarRegistrosTempoAsync),

        new(new LlmFerramenta("resumo_financeiro",
                "Totais financeiros do escritório num período (padrão: mês atual): receitas e despesas pagas, " +
                "a receber, a pagar e valores vencidos.",
                Schema(
                    P("de", "string", "Vencimento a partir desta data (AAAA-MM-DD)."),
                    P("ate", "string", "Vencimento até esta data (AAAA-MM-DD)."))),
            PlanoRestricoes.PermiteFinanceiro, ResumoFinanceiroAsync),

        new(new LlmFerramenta("listar_lancamentos",
                "Lista lançamentos financeiros (receitas/despesas). Datas de vencimento AAAA-MM-DD.",
                Schema(
                    P("tipo", "string", "Receita ou despesa.", Nomes<TipoLancamento>()),
                    P("status", "string", "Status do lançamento.", Nomes<StatusLancamento>()),
                    P("vencidos", "boolean", "Só os pendentes com vencimento já passado."),
                    P("de", "string", "Vencimento a partir desta data."),
                    P("ate", "string", "Vencimento até esta data."),
                    P("contato_id", "string", "Só os de um contato (GUID)."),
                    P("processo_id", "string", "Só os de um processo (GUID)."),
                    P("limite", "integer", $"Máximo de itens (padrão {LimitePadrao}, máx. {LimiteMaximo})."))),
            PlanoRestricoes.PermiteFinanceiro, ListarLancamentosAsync),

        new(new LlmFerramenta("resumo_honorarios",
                "Painel dos contratos de honorários (os mesmos números da tela): total a receber, total em atraso " +
                "(com multa e juros), recebido no mês e a lista de CLIENTES EM ATRASO / inadimplentes. " +
                "Use para 'quais clientes estão em atraso', 'quem está devendo', 'quanto tenho a receber'.",
                Schema()),
            PlanoRestricoes.PermiteHonorariosContratos, ResumoHonorariosAsync),

        new(new LlmFerramenta("listar_contratos_honorario",
                "Lista contratos de honorários, com valor total, pago, a vencer e em atraso. Uma parcela está em atraso " +
                "quando não foi paga e o vencimento já passou (o status gravado do contrato pode não refletir isso: " +
                "use o campo emAtraso ou o filtro em_atraso).",
                Schema(
                    P("texto", "string", "Trecho do número do contrato, do objeto ou do nome do cliente."),
                    P("em_atraso", "boolean", "Só contratos com parcelas em atraso."),
                    P("status", "string", "Status gravado do contrato.", Nomes<StatusContratoHonorario>()),
                    P("contato_id", "string", "Só os de um contato (GUID)."),
                    P("limite", "integer", $"Máximo de itens (padrão {LimitePadrao}, máx. {LimiteMaximo})."))),
            PlanoRestricoes.PermiteHonorariosContratos, ListarContratosAsync),

        new(new LlmFerramenta("detalhar_contrato_honorario",
                "Detalhes de um contrato de honorários, com todas as parcelas.",
                Schema(P("contrato_id", "string", "Id (GUID) do contrato."), "contrato_id")),
            PlanoRestricoes.PermiteHonorariosContratos, DetalharContratoAsync),

        new(new LlmFerramenta("listar_publicacoes",
                "Lista publicações capturadas nos diários oficiais. Datas AAAA-MM-DD.",
                Schema(
                    P("status", "string", "Status da publicação.", Nomes<StatusPublicacao>()),
                    P("urgentes", "boolean", "Só as marcadas como urgentes."),
                    P("de", "string", "Publicadas a partir desta data."),
                    P("ate", "string", "Publicadas até esta data."),
                    P("processo_id", "string", "Só as de um processo (GUID)."),
                    P("limite", "integer", $"Máximo de itens (padrão {LimitePadrao}, máx. {LimiteMaximo})."))),
            PlanoRestricoes.PermiteCapturacaoPublicacoes, ListarPublicacoesAsync),
    ];

    // ── Processos ────────────────────────────────────────────────────────────

    private async Task<object> BuscarProcessosAsync(JsonElement a, CancellationToken ct)
    {
        var q = _db.Processos.AsNoTracking().Where(p => p.TenantId == TenantId);

        if (Texto(a, "texto") is { } texto)
        {
            var t = texto.ToLower();
            var digitos = new string(texto.Where(char.IsDigit).ToArray());
            q = q.Where(p =>
                p.NumeroCNJ.Contains(texto) ||
                (digitos.Length >= 4 && p.NumeroCNJ.Replace(".", "").Replace("-", "").Contains(digitos)) ||
                (p.TipoAcao != null && p.TipoAcao.ToLower().Contains(t)) ||
                (p.Classe != null && p.Classe.ToLower().Contains(t)) ||
                (p.Tribunal != null && p.Tribunal.ToLower().Contains(t)) ||
                (p.Comarca != null && p.Comarca.ToLower().Contains(t)) ||
                p.Partes.Any(pp => pp.Contato.Nome.ToLower().Contains(t)));
        }
        if (Enum<StatusProcesso>(a, "status") is { } status) q = q.Where(p => p.Status == status);
        if (Enum<AreaDireito>(a, "area") is { } area) q = q.Where(p => p.AreaDireito == area);
        if (Texto(a, "responsavel") is { } resp)
        {
            var r = resp.ToLower();
            q = q.Where(p => p.AdvogadoResponsavel != null && p.AdvogadoResponsavel.Nome.ToLower().Contains(r));
        }
        if (Bool(a, "apenas_favoritos"))
        {
            var userId = _tenant.UserId;
            q = q.Where(p => p.Favoritos.Any(f => f.UsuarioId == userId));
        }

        var total = await q.CountAsync(ct);
        var itens = await q
            .OrderByDescending(p => p.UltimoAndamentoEm ?? p.CriadoEm)
            .Take(Limite(a))
            .Select(p => new
            {
                p.Id,
                p.NumeroCNJ,
                p.TipoAcao,
                p.Classe,
                Area = p.AreaDireito.ToString(),
                Fase = p.Fase.ToString(),
                Status = p.Status.ToString(),
                p.Tribunal,
                p.Vara,
                p.Comarca,
                Responsavel = p.AdvogadoResponsavel != null ? p.AdvogadoResponsavel.Nome : null,
                p.UltimoAndamentoEm,
                Partes = p.Partes.Select(pp => new { pp.Contato.Nome, Tipo = pp.TipoParte.ToString() }).ToList()
            })
            .ToListAsync(ct);

        return new
        {
            total,
            exibidos = itens.Count,
            processos = itens.Select(p => new
            {
                id = p.Id,
                numero = p.NumeroCNJ,
                tipoAcao = p.TipoAcao ?? p.Classe,
                area = p.Area,
                fase = p.Fase,
                status = p.Status,
                tribunal = p.Tribunal,
                vara = p.Vara,
                comarca = p.Comarca,
                responsavel = p.Responsavel,
                ultimoAndamento = Data(p.UltimoAndamentoEm),
                partes = p.Partes.Select(x => $"{x.Nome} ({x.Tipo})"),
                link = LinkProcesso(p.Id)
            })
        };
    }

    private async Task<object> DetalharProcessoAsync(JsonElement a, CancellationToken ct)
    {
        var q = _db.Processos.AsNoTracking().Where(p => p.TenantId == TenantId);
        if (Id(a, "processo_id") is { } id) q = q.Where(p => p.Id == id);
        else if (Texto(a, "numero_cnj") is { } numero)
        {
            var digitos = new string(numero.Where(char.IsDigit).ToArray());
            q = q.Where(p => p.NumeroCNJ == numero || p.NumeroCNJ.Replace(".", "").Replace("-", "") == digitos);
        }
        else throw new ArgumentException("Informe processo_id ou numero_cnj.");

        var p = await q.Select(p => new
        {
            p.Id, p.NumeroCNJ, p.TipoAcao, p.Classe, p.Assuntos, p.AreaDireito, p.Fase, p.Status,
            p.Tribunal, p.Vara, p.Comarca, p.Instancia, p.ValorCausa, p.Monitorado,
            p.DataDistribuicao, p.DataAjuizamento, p.EncerradoEm, p.Observacoes, p.Decisao, p.Resultado,
            Responsavel = p.AdvogadoResponsavel != null ? p.AdvogadoResponsavel.Nome : null,
            Partes = p.Partes.Select(pp => new { pp.ContatoId, pp.Contato.Nome, pp.TipoParte }).ToList()
        }).FirstOrDefaultAsync(ct);
        if (p is null) return new { erro = "Processo não encontrado." };

        var andamentos = await _db.Andamentos.AsNoTracking()
            .Where(x => x.TenantId == TenantId && x.ProcessoId == p.Id)
            .OrderByDescending(x => x.Data)
            .Take(15)
            .Select(x => new { x.Data, x.Tipo, x.Descricao, x.DescricaoTraduzidaIA })
            .ToListAsync(ct);

        var tarefas = await _db.Tarefas.AsNoTracking()
            .Where(t => t.TenantId == TenantId && t.ProcessoId == p.Id
                && (t.Status == StatusTarefa.Pendente || t.Status == StatusTarefa.EmAndamento || t.Status == StatusTarefa.Suspensa))
            .OrderBy(t => t.Prazo == null).ThenBy(t => t.Prazo)
            .Take(20)
            .Select(t => new { t.Id, t.Titulo, t.Tipo, t.Status, t.Prioridade, t.Prazo, Responsavel = t.Responsavel != null ? t.Responsavel.Nome : null })
            .ToListAsync(ct);

        var agora = await AgoraAsync(ct);
        var eventos = await _db.Eventos.AsNoTracking()
            .Where(e => e.TenantId == TenantId && e.ProcessoId == p.Id && e.DataHora >= agora.Date)
            .OrderBy(e => e.DataHora)
            .Take(10)
            .Select(e => new { e.Titulo, e.Tipo, e.DataHora, e.Local })
            .ToListAsync(ct);

        return new
        {
            id = p.Id,
            numero = p.NumeroCNJ,
            tipoAcao = p.TipoAcao,
            classe = p.Classe,
            assuntos = p.Assuntos,
            area = p.AreaDireito.ToString(),
            fase = p.Fase.ToString(),
            status = p.Status.ToString(),
            tribunal = p.Tribunal,
            vara = p.Vara,
            comarca = p.Comarca,
            instancia = p.Instancia,
            valorCausa = p.ValorCausa,
            monitorado = p.Monitorado,
            distribuicao = Data(p.DataDistribuicao ?? p.DataAjuizamento),
            encerradoEm = Data(p.EncerradoEm),
            responsavel = p.Responsavel,
            observacoes = Cortar(p.Observacoes, 1000),
            decisao = Cortar(p.Decisao, 1000),
            resultado = Cortar(p.Resultado, 500),
            link = LinkProcesso(p.Id),
            partes = p.Partes.Select(x => new { contatoId = x.ContatoId, nome = x.Nome, tipo = x.TipoParte.ToString(), link = LinkContato(x.ContatoId) }),
            ultimosAndamentos = andamentos.Select(x => new
            {
                data = Data(x.Data),
                tipo = x.Tipo.ToString(),
                descricao = Cortar(x.Descricao, 600),
                explicacao = Cortar(x.DescricaoTraduzidaIA, 600)
            }),
            tarefasAbertas = tarefas.Select(t => new
            {
                id = t.Id, titulo = t.Titulo, tipo = t.Tipo.ToString(), status = t.Status.ToString(),
                prioridade = t.Prioridade.ToString(), prazo = DataHora(t.Prazo), responsavel = t.Responsavel
            }),
            proximosEventos = eventos.Select(e => new { titulo = e.Titulo, tipo = e.Tipo.ToString(), dataHora = DataHora(e.DataHora), local = e.Local })
        };
    }

    // ── Tarefas / prazos ─────────────────────────────────────────────────────

    private async Task<object> ListarTarefasAsync(JsonElement a, CancellationToken ct)
    {
        var agora = await AgoraAsync(ct);
        var q = _db.Tarefas.AsNoTracking().Where(t => t.TenantId == TenantId);

        if (Enum<StatusTarefa>(a, "status") is { } status) q = q.Where(t => t.Status == status);
        else q = q.Where(t => t.Status == StatusTarefa.Pendente || t.Status == StatusTarefa.EmAndamento || t.Status == StatusTarefa.Suspensa);
        if (Enum<TipoTarefa>(a, "tipo") is { } tipo) q = q.Where(t => t.Tipo == tipo);
        if (DataArg(a, "prazo_de") is { } de) q = q.Where(t => t.Prazo >= de);
        if (DataArg(a, "prazo_ate") is { } ate) { var lim = ate.AddDays(1); q = q.Where(t => t.Prazo < lim); }
        if (Bool(a, "atrasadas"))
            q = q.Where(t => t.Prazo < agora
                && (t.Status == StatusTarefa.Pendente || t.Status == StatusTarefa.EmAndamento || t.Status == StatusTarefa.Suspensa));
        if (Texto(a, "responsavel") is { } resp)
        {
            var r = resp.ToLower();
            q = q.Where(t => t.Responsavel != null && t.Responsavel.Nome.ToLower().Contains(r));
        }
        if (Bool(a, "apenas_minhas")) { var u = _tenant.UserId; q = q.Where(t => t.ResponsavelId == u); }
        if (Id(a, "processo_id") is { } pid) q = q.Where(t => t.ProcessoId == pid);
        if (Id(a, "contato_id") is { } cid) q = q.Where(t => t.ContatoId == cid);

        var total = await q.CountAsync(ct);
        var itens = await q
            .OrderBy(t => t.Prazo == null).ThenBy(t => t.Prazo).ThenByDescending(t => t.Prioridade)
            .Take(Limite(a))
            .Select(t => new
            {
                t.Id, t.Titulo, t.Descricao, t.Tipo, t.Status, t.Prioridade, t.Prazo, t.ConcluidaEm, t.ProcessoId,
                Responsavel = t.Responsavel != null ? t.Responsavel.Nome : null,
                Processo = t.Processo != null ? t.Processo.NumeroCNJ : null,
                Contato = t.Contato != null ? t.Contato.Nome : null
            })
            .ToListAsync(ct);

        return new
        {
            agora = DataHora(agora),
            total,
            exibidos = itens.Count,
            tarefas = itens.Select(t => new
            {
                id = t.Id,
                titulo = t.Titulo,
                descricao = Cortar(t.Descricao, 300),
                tipo = t.Tipo.ToString(),
                status = t.Status.ToString(),
                prioridade = t.Prioridade.ToString(),
                prazo = DataHora(t.Prazo),
                atrasada = t.Prazo < agora && t.Status is StatusTarefa.Pendente or StatusTarefa.EmAndamento or StatusTarefa.Suspensa,
                concluidaEm = DataHoraUtc(t.ConcluidaEm),
                responsavel = t.Responsavel,
                processo = t.Processo,
                processoLink = t.ProcessoId is { } p ? LinkProcesso(p) : null,
                contato = t.Contato,
                link = $"/pages/tarefas.html?abrirId={t.Id}"
            })
        };
    }

    // ── Agenda ───────────────────────────────────────────────────────────────

    private async Task<object> ListarEventosAsync(JsonElement a, CancellationToken ct)
    {
        var agora = await AgoraAsync(ct);
        var de = DataArg(a, "de") ?? agora.Date;
        var ate = (DataArg(a, "ate") ?? de.AddDays(30)).AddDays(1);

        var q = _db.Eventos.AsNoTracking()
            .Where(e => e.TenantId == TenantId && e.DataHora >= de && e.DataHora < ate);
        if (Enum<TipoEvento>(a, "tipo") is { } tipo) q = q.Where(e => e.Tipo == tipo);
        if (Texto(a, "responsavel") is { } resp)
        {
            var r = resp.ToLower();
            q = q.Where(e => e.Responsavel != null && e.Responsavel.Nome.ToLower().Contains(r));
        }
        if (Bool(a, "apenas_meus")) { var u = _tenant.UserId; q = q.Where(e => e.ResponsavelId == u); }
        if (Id(a, "processo_id") is { } pid) q = q.Where(e => e.ProcessoId == pid);

        var total = await q.CountAsync(ct);
        var itens = await q.OrderBy(e => e.DataHora).Take(Limite(a))
            .Select(e => new
            {
                e.Titulo, e.Tipo, e.DataHora, e.DataHoraFim, e.Local, e.Observacoes, e.ProcessoId,
                Responsavel = e.Responsavel != null ? e.Responsavel.Nome : null,
                Processo = e.Processo != null ? e.Processo.NumeroCNJ : null
            })
            .ToListAsync(ct);

        return new
        {
            periodo = new { de = Data(de), ate = Data(ate.AddDays(-1)) },
            total,
            exibidos = itens.Count,
            eventos = itens.Select(e => new
            {
                titulo = e.Titulo,
                tipo = e.Tipo.ToString(),
                inicio = DataHora(e.DataHora),
                fim = DataHora(e.DataHoraFim),
                local = e.Local,
                observacoes = Cortar(e.Observacoes, 300),
                responsavel = e.Responsavel,
                processo = e.Processo,
                processoLink = e.ProcessoId is { } p ? LinkProcesso(p) : null
            }),
            link = "/pages/agenda.html"
        };
    }

    // ── Contatos ─────────────────────────────────────────────────────────────

    private async Task<object> BuscarContatosAsync(JsonElement a, CancellationToken ct)
    {
        var q = _db.Contatos.AsNoTracking().Where(c => c.TenantId == TenantId);
        if (!Bool(a, "incluir_inativos")) q = q.Where(c => c.Ativo);
        if (Texto(a, "texto") is { } texto)
        {
            var t = texto.ToLower();
            q = q.Where(c => c.Nome.ToLower().Contains(t)
                || (c.CpfCnpj != null && c.CpfCnpj.Contains(texto))
                || (c.Email != null && c.Email.ToLower().Contains(t)));
        }
        var tipos = Lista(a, "tipo_contato").Select(v => Enum<TipoContato>(v, "tipo_contato")).ToList();
        if (tipos.Count > 0) q = q.Where(c => tipos.Contains(c.TipoContato));
        if (Enum<TipoPessoa>(a, "pessoa") is { } pessoa) q = q.Where(c => c.Tipo == pessoa);
        if (BoolOpcional(a, "importado") is { } importado) q = q.Where(c => c.ImportadoAutomaticamente == importado);

        // Tags: resolve o que o modelo pediu (parte do nome, sem acento/maiúsculas) para as tags
        // reais do escritório e filtra por igualdade — como o filtro da tela.
        var tagsPedidas = Lista(a, "tags");
        List<string>? tagsResolvidas = null;
        if (tagsPedidas.Count > 0)
        {
            var existentes = await TagsDoTenantAsync(ct);
            tagsResolvidas = existentes
                .Where(e => tagsPedidas.Any(p => Normalizar(e).Contains(Normalizar(p))))
                .ToList();
            if (tagsResolvidas.Count == 0)
                return new { total = 0, erro = $"Nenhuma tag corresponde a: {string.Join(", ", tagsPedidas)}.", tagsExistentes = existentes };
            q = q.Where(c => c.Tags.Any(t => tagsResolvidas.Contains(t.Tag)));
        }

        var total = await q.CountAsync(ct);
        var itens = await q.OrderBy(c => c.Nome).Take(Limite(a))
            .Select(c => new
            {
                c.Id, c.Nome, c.Tipo, c.TipoContato, c.CpfCnpj, c.Email, c.Telefone, c.Cidade, c.Estado, c.Ativo,
                c.ImportadoAutomaticamente,
                Tags = c.Tags.Select(t => t.Tag).ToList(),
                Processos = _db.ProcessoPartes.Count(pp => pp.ContatoId == c.Id && pp.Processo.TenantId == TenantId)
            })
            .ToListAsync(ct);

        return new
        {
            total,
            exibidos = itens.Count,
            tagsFiltradas = tagsResolvidas,
            contatos = itens.Select(c => new
            {
                id = c.Id,
                nome = c.Nome,
                pessoa = c.Tipo.ToString(),
                tipo = c.TipoContato.ToString(),
                tags = c.Tags,
                cpfCnpj = c.CpfCnpj,
                email = c.Email,
                telefone = c.Telefone,
                cidade = c.Cidade is null ? null : $"{c.Cidade}/{c.Estado}",
                ativo = c.Ativo,
                importado = c.ImportadoAutomaticamente,
                processos = c.Processos,
                link = LinkContato(c.Id)
            })
        };
    }

    private async Task<object> ListarTagsContatosAsync(JsonElement a, CancellationToken ct)
    {
        var tags = await _db.ContatoTags.AsNoTracking()
            .Where(t => t.Contato.TenantId == TenantId && t.Contato.Ativo)
            .GroupBy(t => t.Tag)
            .Select(g => new { tag = g.Key, contatos = g.Select(t => t.ContatoId).Distinct().Count() })
            .ToListAsync(ct);
        return new { tags = tags.OrderByDescending(t => t.contatos).ThenBy(t => t.tag), link = "/pages/contatos.html" };
    }

    private async Task<List<string>> TagsDoTenantAsync(CancellationToken ct) =>
        await _db.ContatoTags.AsNoTracking()
            .Where(t => t.Contato.TenantId == TenantId)
            .Select(t => t.Tag)
            .Distinct()
            .ToListAsync(ct);

    private static string Normalizar(string s)
    {
        var d = s.Trim().ToLowerInvariant().Normalize(System.Text.NormalizationForm.FormD);
        return new string(d.Where(ch => CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark).ToArray());
    }

    private async Task<object> DetalharContatoAsync(JsonElement a, CancellationToken ct)
    {
        var id = Id(a, "contato_id") ?? throw new ArgumentException("Informe contato_id.");
        var c = await _db.Contatos.AsNoTracking().Include(c => c.Tags)
            .FirstOrDefaultAsync(c => c.Id == id && c.TenantId == TenantId, ct);
        if (c is null) return new { erro = "Contato não encontrado." };

        var processos = await _db.ProcessoPartes.AsNoTracking()
            .Where(pp => pp.ContatoId == id && pp.Processo.TenantId == TenantId)
            .Select(pp => new { pp.ProcessoId, pp.Processo.NumeroCNJ, pp.Processo.TipoAcao, pp.Processo.Status, pp.TipoParte })
            .Take(30)
            .ToListAsync(ct);

        var tarefas = await _db.Tarefas.AsNoTracking()
            .Where(t => t.TenantId == TenantId && t.ContatoId == id
                && (t.Status == StatusTarefa.Pendente || t.Status == StatusTarefa.EmAndamento || t.Status == StatusTarefa.Suspensa))
            .OrderBy(t => t.Prazo == null).ThenBy(t => t.Prazo)
            .Take(15)
            .Select(t => new { t.Titulo, t.Status, t.Prazo })
            .ToListAsync(ct);

        object? financeiro = null;
        if (PlanoRestricoes.PermiteFinanceiro(_tenant.Plano))
        {
            var pendentes = await _db.LancamentosFinanceiros.AsNoTracking()
                .Where(l => l.TenantId == TenantId && l.ContatoId == id
                    && (l.Status == StatusLancamento.Pendente || l.Status == StatusLancamento.Vencido))
                .OrderBy(l => l.DataVencimento)
                .Take(20)
                .Select(l => new { l.Tipo, l.Categoria, l.Descricao, l.Valor, l.DataVencimento, l.Status })
                .ToListAsync(ct);
            financeiro = new
            {
                lancamentosPendentes = pendentes.Select(l => new
                {
                    tipo = l.Tipo.ToString(), categoria = l.Categoria, descricao = l.Descricao,
                    valor = l.Valor, vencimento = Data(l.DataVencimento), status = l.Status.ToString()
                })
            };
        }

        object? contratos = null;
        if (PlanoRestricoes.PermiteHonorariosContratos(_tenant.Plano))
        {
            var hoje = (await AgoraAsync(ct)).Date;
            contratos = await _db.ContratosHonorarios.AsNoTracking()
                .Where(k => k.TenantId == TenantId && k.ContatoId == id)
                .Select(k => new
                {
                    id = k.Id,
                    numero = k.NumeroContrato,
                    objeto = k.Objeto,
                    status = k.Status.ToString(),
                    valorTotal = k.ValorTotal,
                    parcelasEmAtraso = k.Parcelas.Count(p => p.Status != StatusParcelaHonorario.Pago
                        && p.Status != StatusParcelaHonorario.Cancelado && p.Vencimento < hoje),
                    link = "/pages/honorarios-contrato-detalhe.html?id=" + k.Id
                })
                .ToListAsync(ct);
        }

        return new
        {
            id = c.Id,
            nome = c.Nome,
            pessoa = c.Tipo.ToString(),
            tipo = c.TipoContato.ToString(),
            tags = c.Tags.Select(t => t.Tag),
            cpfCnpj = c.CpfCnpj,
            oab = c.Oab,
            email = c.Email,
            telefone = c.Telefone,
            endereco = string.Join(", ", new[] { c.Endereco, c.Cidade, c.Estado, c.Cep }.Where(s => !string.IsNullOrWhiteSpace(s))),
            nascimento = Data(c.DataNascimento),
            observacoes = Cortar(c.Observacoes, 800),
            ativo = c.Ativo,
            link = LinkContato(c.Id),
            processos = processos.Select(p => new
            {
                id = p.ProcessoId, numero = p.NumeroCNJ, tipoAcao = p.TipoAcao,
                status = p.Status.ToString(), papel = p.TipoParte.ToString(), link = LinkProcesso(p.ProcessoId)
            }),
            tarefasAbertas = tarefas.Select(t => new { titulo = t.Titulo, status = t.Status.ToString(), prazo = DataHora(t.Prazo) }),
            financeiro,
            contratosHonorario = contratos
        };
    }

    private async Task<object> ListarEquipeAsync(JsonElement a, CancellationToken ct)
    {
        var usuarios = await _db.Users.AsNoTracking()
            .Where(u => u.TenantId == TenantId && u.Perfil != PerfilUsuario.SuperAdmin && u.Perfil != PerfilUsuario.Cliente)
            .OrderBy(u => u.Nome)
            .Select(u => new { u.Id, u.Nome, u.Perfil, u.Ativo })
            .ToListAsync(ct);
        return new
        {
            usuarioAtualId = _tenant.UserId,
            equipe = usuarios.Select(u => new { id = u.Id, nome = u.Nome, perfil = u.Perfil.ToString(), ativo = u.Ativo })
        };
    }

    private async Task<object> ListarRegistrosTempoAsync(JsonElement a, CancellationToken ct)
    {
        await AgoraAsync(ct); // carrega o fuso para DataHoraUtc
        var q = _db.RegistrosTempo.AsNoTracking().Where(r => r.TenantId == TenantId);
        if (DataArg(a, "de") is { } de) q = q.Where(r => r.Inicio >= de);
        if (DataArg(a, "ate") is { } ate) { var lim = ate.AddDays(1); q = q.Where(r => r.Inicio < lim); }
        if (Texto(a, "usuario") is { } nome) { var n = nome.ToLower(); q = q.Where(r => r.Usuario.Nome.ToLower().Contains(n)); }
        if (Bool(a, "apenas_meus")) { var u = _tenant.UserId; q = q.Where(r => r.UsuarioId == u); }
        if (Id(a, "processo_id") is { } pid) q = q.Where(r => r.ProcessoId == pid);

        var total = await q.CountAsync(ct);
        var minutos = await q.SumAsync(r => r.DuracaoMinutos ?? 0, ct);
        var itens = await q.OrderByDescending(r => r.Inicio).Take(Limite(a))
            .Select(r => new
            {
                r.Inicio, r.DuracaoMinutos, r.Descricao, r.EmAndamento,
                Usuario = r.Usuario.Nome,
                Processo = r.Processo != null ? r.Processo.NumeroCNJ : null,
                Tarefa = r.Tarefa != null ? r.Tarefa.Titulo : null
            })
            .ToListAsync(ct);

        return new
        {
            total,
            totalMinutos = minutos,
            exibidos = itens.Count,
            registros = itens.Select(r => new
            {
                inicio = DataHoraUtc(r.Inicio),
                minutos = r.DuracaoMinutos,
                emAndamento = r.EmAndamento,
                descricao = Cortar(r.Descricao, 300),
                usuario = r.Usuario,
                processo = r.Processo,
                tarefa = r.Tarefa
            }),
            link = "/pages/timesheet.html"
        };
    }

    // ── Financeiro ───────────────────────────────────────────────────────────

    private async Task<object> ResumoFinanceiroAsync(JsonElement a, CancellationToken ct)
    {
        var hoje = (await AgoraAsync(ct)).Date;
        var de = DataArg(a, "de") ?? new DateTime(hoje.Year, hoje.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var ate = (DataArg(a, "ate") ?? de.AddMonths(1).AddDays(-1)).AddDays(1);

        var lancs = await _db.LancamentosFinanceiros.AsNoTracking()
            .Where(l => l.TenantId == TenantId && l.DataVencimento >= de && l.DataVencimento < ate
                && l.Status != StatusLancamento.Cancelado)
            .Select(l => new { l.Tipo, l.Status, l.Valor, l.DataVencimento })
            .ToListAsync(ct);

        bool Aberto(StatusLancamento s) => s is StatusLancamento.Pendente or StatusLancamento.Vencido;
        decimal Soma(TipoLancamento tipo, Func<StatusLancamento, DateTime, bool> filtro) =>
            lancs.Where(l => l.Tipo == tipo && filtro(l.Status, l.DataVencimento)).Sum(l => l.Valor);

        var vencidosGeral = await _db.LancamentosFinanceiros.AsNoTracking()
            .Where(l => l.TenantId == TenantId && l.DataVencimento < hoje
                && (l.Status == StatusLancamento.Pendente || l.Status == StatusLancamento.Vencido))
            .GroupBy(l => l.Tipo)
            .Select(g => new { Tipo = g.Key, Total = g.Sum(l => l.Valor), Qtd = g.Count() })
            .ToListAsync(ct);

        var receitasPagas = Soma(TipoLancamento.Receita, (s, _) => s == StatusLancamento.Pago);
        var despesasPagas = Soma(TipoLancamento.Despesa, (s, _) => s == StatusLancamento.Pago);
        return new
        {
            periodo = new { de = Data(de), ate = Data(ate.AddDays(-1)) },
            receitasRecebidas = receitasPagas,
            despesasPagas,
            saldoRealizado = receitasPagas - despesasPagas,
            aReceber = Soma(TipoLancamento.Receita, (s, _) => Aberto(s)),
            aPagar = Soma(TipoLancamento.Despesa, (s, _) => Aberto(s)),
            vencidosNoPeriodo = new
            {
                receitas = Soma(TipoLancamento.Receita, (s, d) => Aberto(s) && d < hoje),
                despesas = Soma(TipoLancamento.Despesa, (s, d) => Aberto(s) && d < hoje)
            },
            vencidosEmQualquerData = vencidosGeral.Select(v => new { tipo = v.Tipo.ToString(), total = v.Total, quantidade = v.Qtd }),
            link = "/pages/financeiro.html"
        };
    }

    private async Task<object> ListarLancamentosAsync(JsonElement a, CancellationToken ct)
    {
        var hoje = (await AgoraAsync(ct)).Date;
        var q = _db.LancamentosFinanceiros.AsNoTracking().Where(l => l.TenantId == TenantId);
        if (Enum<TipoLancamento>(a, "tipo") is { } tipo) q = q.Where(l => l.Tipo == tipo);
        if (Enum<StatusLancamento>(a, "status") is { } status) q = q.Where(l => l.Status == status);
        if (Bool(a, "vencidos"))
            q = q.Where(l => l.DataVencimento < hoje && (l.Status == StatusLancamento.Pendente || l.Status == StatusLancamento.Vencido));
        if (DataArg(a, "de") is { } de) q = q.Where(l => l.DataVencimento >= de);
        if (DataArg(a, "ate") is { } ate) { var lim = ate.AddDays(1); q = q.Where(l => l.DataVencimento < lim); }
        if (Id(a, "contato_id") is { } cid) q = q.Where(l => l.ContatoId == cid);
        if (Id(a, "processo_id") is { } pid) q = q.Where(l => l.ProcessoId == pid);

        var total = await q.CountAsync(ct);
        var soma = await q.SumAsync(l => l.Valor, ct);
        var itens = await q.OrderBy(l => l.DataVencimento).Take(Limite(a))
            .Select(l => new
            {
                l.Tipo, l.Categoria, l.Descricao, l.Valor, l.DataVencimento, l.DataPagamento, l.Status,
                Contato = l.Contato != null ? l.Contato.Nome : null,
                Processo = l.Processo != null ? l.Processo.NumeroCNJ : null
            })
            .ToListAsync(ct);

        return new
        {
            total,
            valorTotal = soma,
            exibidos = itens.Count,
            lancamentos = itens.Select(l => new
            {
                tipo = l.Tipo.ToString(),
                categoria = l.Categoria,
                descricao = Cortar(l.Descricao, 200),
                valor = l.Valor,
                vencimento = Data(l.DataVencimento),
                pagamento = Data(l.DataPagamento),
                status = l.Status.ToString(),
                vencido = l.DataVencimento < hoje && l.Status is StatusLancamento.Pendente or StatusLancamento.Vencido,
                contato = l.Contato,
                processo = l.Processo
            }),
            link = "/pages/financeiro.html"
        };
    }

    // ── Honorários ───────────────────────────────────────────────────────────

    // Parcela em atraso = não paga/cancelada com vencimento já passado — o status gravado continua
    // "Pendente"; é a mesma regra (e o mesmo cálculo de multa/juros) do HonorarioService.
    private static bool EmAtraso(StatusParcelaHonorario status, DateTime vencimento, DateTime hoje) =>
        status is not (StatusParcelaHonorario.Pago or StatusParcelaHonorario.Cancelado) && vencimento.Date < hoje;

    private static bool Encerrado(StatusContratoHonorario s) =>
        s is StatusContratoHonorario.Encerrado or StatusContratoHonorario.Distratado;

    private async Task<object> ResumoHonorariosAsync(JsonElement a, CancellationToken ct)
    {
        var d = await _honorarios.GetDashboardAsync(TenantId, ct);
        return new
        {
            totalAReceber = d.TotalAReceber,
            totalEmAtraso = d.TotalEmAtraso,
            recebidoNoMes = d.RecebidoNoMes,
            metaMensal = d.MetaMensal,
            contratosEmAtraso = d.ContratosAtrasados,
            contratosAtivos = d.ContratosAtivos,
            contratosQuitados = d.ContratosQuitados,
            clientesEmAtraso = d.Inadimplentes.Select(i => new
            {
                cliente = i.NomeContato,
                valorEmAtrasoComMultaEJuros = i.ValorEmAtraso,
                parcelasEmAtraso = i.ParcelasVencidas,
                telefone = i.Telefone,
                email = i.Email,
                link = "/pages/honorarios-contrato-detalhe.html?id=" + i.ContratoId
            }),
            observacao = d.ContratosAtrasados > 10 ? "clientesEmAtraso mostra só os 10 maiores valores." : null,
            link = "/pages/honorarios-contratos.html"
        };
    }

    private async Task<object> ListarContratosAsync(JsonElement a, CancellationToken ct)
    {
        var hoje = (await AgoraAsync(ct)).Date;
        var q = _db.ContratosHonorarios.AsNoTracking().Where(k => k.TenantId == TenantId);
        if (Texto(a, "texto") is { } texto)
        {
            var t = texto.ToLower();
            q = q.Where(k => k.NumeroContrato.ToLower().Contains(t)
                || (k.Objeto != null && k.Objeto.ToLower().Contains(t))
                || k.Contato.Nome.ToLower().Contains(t));
        }
        if (Enum<StatusContratoHonorario>(a, "status") is { } status) q = q.Where(k => k.Status == status);
        if (Id(a, "contato_id") is { } cid) q = q.Where(k => k.ContatoId == cid);

        // Valores calculados em memória (multa/juros), como na tela de contratos.
        var contratos = await q.OrderByDescending(k => k.CriadoEm)
            .Select(k => new
            {
                k.Id, k.NumeroContrato, k.Objeto, k.Status, k.ValorTotal, k.FormaPagamento, k.DataInicio,
                Cliente = k.Contato.Nome,
                Processo = k.Processo != null ? k.Processo.NumeroCNJ : null,
                Parcelas = k.Parcelas.Select(p => new { p.Status, p.Vencimento, p.ValorOriginal, p.ValorPago }).ToList()
            })
            .ToListAsync(ct);

        var calculados = contratos.Select(k =>
        {
            var validas = k.Parcelas.Where(p => p.Status != StatusParcelaHonorario.Cancelado).ToList();
            var atrasadas = validas.Where(p => EmAtraso(p.Status, p.Vencimento, hoje)).ToList();
            return new
            {
                id = k.Id,
                numero = k.NumeroContrato,
                cliente = k.Cliente,
                objeto = Cortar(k.Objeto, 200),
                status = k.Status.ToString(),
                emAtraso = atrasadas.Count > 0 && !Encerrado(k.Status),
                formaPagamento = k.FormaPagamento.ToString(),
                inicio = Data(k.DataInicio),
                processo = k.Processo,
                valorTotal = k.ValorTotal,
                valorPago = validas.Where(p => p.Status == StatusParcelaHonorario.Pago).Sum(p => p.ValorPago ?? p.ValorOriginal),
                valorAVencer = validas.Where(p => p.Status != StatusParcelaHonorario.Pago && !EmAtraso(p.Status, p.Vencimento, hoje))
                    .Sum(p => p.ValorOriginal),
                valorEmAtrasoComMultaEJuros = atrasadas.Sum(p => Services.HonorarioService.CalcularJuros(p.ValorOriginal, p.Vencimento, hoje).total),
                parcelasEmAtraso = atrasadas.Count,
                parcelasPagas = validas.Count(p => p.Status == StatusParcelaHonorario.Pago),
                parcelasTotal = validas.Count,
                link = "/pages/honorarios-contrato-detalhe.html?id=" + k.Id
            };
        });
        if (Bool(a, "em_atraso")) calculados = calculados.Where(k => k.emAtraso);

        var lista = calculados.ToList();
        var itens = lista.Take(Limite(a)).ToList();
        return new { total = lista.Count, exibidos = itens.Count, contratos = itens };
    }

    private async Task<object> DetalharContratoAsync(JsonElement a, CancellationToken ct)
    {
        var id = Id(a, "contrato_id") ?? throw new ArgumentException("Informe contrato_id.");
        var hoje = (await AgoraAsync(ct)).Date;
        var k = await _db.ContratosHonorarios.AsNoTracking()
            .Where(k => k.Id == id && k.TenantId == TenantId)
            .Select(k => new
            {
                k.Id, k.NumeroContrato, k.Objeto, k.Status, k.ValorTotal, k.FormaPagamento, k.Periodicidade,
                k.NumeroParcelas, k.ValorEntrada, k.PercentualMulta, k.PercentualJurosMensal, k.TipoCobranca,
                k.Observacoes, k.DataInicio, k.DataFim, k.DistratoEm, k.DistratoMotivo, k.ContatoId, k.ProcessoId,
                Cliente = k.Contato.Nome,
                Processo = k.Processo != null ? k.Processo.NumeroCNJ : null,
                Parcelas = k.Parcelas.OrderBy(p => p.Vencimento)
                    .Select(p => new { p.Numero, p.IsEntrada, p.Vencimento, p.ValorOriginal, p.ValorPago, p.DataPagamento, p.Status })
                    .ToList()
            })
            .FirstOrDefaultAsync(ct);
        if (k is null) return new { erro = "Contrato não encontrado." };

        return new
        {
            id = k.Id,
            numero = k.NumeroContrato,
            cliente = k.Cliente,
            clienteLink = LinkContato(k.ContatoId),
            processo = k.Processo,
            processoLink = k.ProcessoId is { } p ? LinkProcesso(p) : null,
            objeto = Cortar(k.Objeto, 800),
            status = k.Status.ToString(),
            valorTotal = k.ValorTotal,
            formaPagamento = k.FormaPagamento.ToString(),
            periodicidade = k.Periodicidade?.ToString(),
            numeroParcelas = k.NumeroParcelas,
            valorEntrada = k.ValorEntrada,
            multaPercentual = k.PercentualMulta * 100,
            jurosMensalPercentual = k.PercentualJurosMensal * 100,
            cobranca = k.TipoCobranca,
            inicio = Data(k.DataInicio),
            fim = Data(k.DataFim),
            distratoEm = Data(k.DistratoEm),
            distratoMotivo = k.DistratoMotivo,
            observacoes = Cortar(k.Observacoes, 800),
            parcelas = k.Parcelas.Select(x =>
            {
                var atraso = EmAtraso(x.Status, x.Vencimento, hoje);
                return new
                {
                    numero = x.IsEntrada ? "Entrada" : x.Numero.ToString(CultureInfo.InvariantCulture),
                    vencimento = Data(x.Vencimento),
                    valor = x.ValorOriginal,
                    valorAtualizadoComMultaEJuros = atraso
                        ? Services.HonorarioService.CalcularJuros(x.ValorOriginal, x.Vencimento, hoje).total
                        : (decimal?)null,
                    valorPago = x.ValorPago,
                    pagamento = Data(x.DataPagamento),
                    situacao = atraso ? "EmAtraso" : x.Status.ToString()
                };
            }),
            link = "/pages/honorarios-contrato-detalhe.html?id=" + k.Id
        };
    }

    // ── Publicações ──────────────────────────────────────────────────────────

    private async Task<object> ListarPublicacoesAsync(JsonElement a, CancellationToken ct)
    {
        var q = _db.Publicacoes.AsNoTracking().Where(p => p.TenantId == TenantId);
        if (Enum<StatusPublicacao>(a, "status") is { } status) q = q.Where(p => p.Status == status);
        if (Bool(a, "urgentes")) q = q.Where(p => p.Urgente);
        if (DataArg(a, "de") is { } de) q = q.Where(p => p.DataPublicacao >= de);
        if (DataArg(a, "ate") is { } ate) { var lim = ate.AddDays(1); q = q.Where(p => p.DataPublicacao < lim); }
        if (Id(a, "processo_id") is { } pid) q = q.Where(p => p.ProcessoId == pid);

        var total = await q.CountAsync(ct);
        var itens = await q.OrderByDescending(p => p.DataPublicacao).Take(Limite(a))
            .Select(p => new
            {
                p.DataPublicacao, p.Diario, p.NumeroCNJ, p.Tipo, p.Status, p.Urgente, p.ClassificacaoIA,
                p.ProcessoId, p.Snippet, p.Conteudo
            })
            .ToListAsync(ct);

        return new
        {
            total,
            exibidos = itens.Count,
            publicacoes = itens.Select(p => new
            {
                data = Data(p.DataPublicacao),
                diario = p.Diario,
                processo = p.NumeroCNJ,
                processoLink = p.ProcessoId is { } id ? LinkProcesso(id) : null,
                tipo = p.Tipo.ToString(),
                status = p.Status.ToString(),
                urgente = p.Urgente,
                classificacao = Cortar(p.ClassificacaoIA, 300),
                trecho = Cortar(string.IsNullOrWhiteSpace(p.Snippet) ? p.Conteudo : p.Snippet, 500)
            }),
            link = "/pages/publicacoes.html"
        };
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private async Task<DateTime> AgoraAsync(CancellationToken ct)
    {
        _tz ??= await _db.DoTenantAsync(TenantId, ct);
        return FusoHorario.AgoraParede(_tz);
    }

    private static string LinkProcesso(Guid id) => $"/pages/processo-detalhe.html?id={id}";
    private static string LinkContato(Guid id) => $"/pages/contato-detalhe.html?id={id}";

    // Prazos/eventos: já estão na hora "de parede" — só formata.
    private static string? DataHora(DateTime? d) =>
        d?.ToString(d.Value.TimeOfDay == TimeSpan.Zero ? "yyyy-MM-dd" : "yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

    private static string? Data(DateTime? d) => d?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    // Carimbos do servidor (UTC reais) → fuso do escritório.
    private string? DataHoraUtc(DateTime? d) =>
        d is null ? null
            : TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(d.Value, DateTimeKind.Utc), _tz ?? BrasiliaTime.Tz)
                .ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

    private static string? Cortar(string? s, int max) =>
        string.IsNullOrWhiteSpace(s) ? null : s.Length <= max ? s : s[..max] + "…";

    private static string? Texto(JsonElement a, string nome) =>
        a.ValueKind == JsonValueKind.Object && a.TryGetProperty(nome, out var v) && v.ValueKind == JsonValueKind.String
        && !string.IsNullOrWhiteSpace(v.GetString()) ? v.GetString()!.Trim() : null;

    private static bool Bool(JsonElement a, string nome) =>
        a.ValueKind == JsonValueKind.Object && a.TryGetProperty(nome, out var v)
        && (v.ValueKind == JsonValueKind.True || (v.ValueKind == JsonValueKind.String && v.GetString() == "true"));

    private static bool? BoolOpcional(JsonElement a, string nome) =>
        a.ValueKind == JsonValueKind.Object && a.TryGetProperty(nome, out var v)
            ? v.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.String when bool.TryParse(v.GetString(), out var b) => b,
                _ => null
            }
            : null;

    /// <summary>Argumento de lista: aceita array de strings ou uma string só.</summary>
    private static List<string> Lista(JsonElement a, string nome)
    {
        if (a.ValueKind != JsonValueKind.Object || !a.TryGetProperty(nome, out var v)) return [];
        IEnumerable<string?> valores = v.ValueKind switch
        {
            JsonValueKind.Array => v.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()),
            JsonValueKind.String => [v.GetString()],
            _ => []
        };
        return valores.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x!.Trim()).Distinct().ToList();
    }

    private static int Limite(JsonElement a)
    {
        var n = a.ValueKind == JsonValueKind.Object && a.TryGetProperty("limite", out var v) && v.TryGetInt32(out var i) ? i : LimitePadrao;
        return Math.Clamp(n, 1, LimiteMaximo);
    }

    private static Guid? Id(JsonElement a, string nome)
    {
        if (Texto(a, nome) is not { } s) return null;
        return Guid.TryParse(s, out var g) ? g : throw new ArgumentException($"{nome} inválido: use o id (GUID) retornado por outra ferramenta.");
    }

    private static DateTime? DataArg(JsonElement a, string nome)
    {
        if (Texto(a, nome) is not { } s) return null;
        return DateTime.TryParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? DateTime.SpecifyKind(d, DateTimeKind.Utc)
            : throw new ArgumentException($"{nome} inválido: use o formato AAAA-MM-DD.");
    }

    private static T? Enum<T>(JsonElement a, string nome) where T : struct, System.Enum =>
        Texto(a, nome) is { } s ? Enum<T>(s, nome) : null;

    private static T Enum<T>(string valor, string nome) where T : struct, System.Enum =>
        System.Enum.TryParse<T>(valor, true, out var v) && System.Enum.IsDefined(v)
            ? v
            : throw new ArgumentException($"{nome} inválido. Valores aceitos: {string.Join(", ", System.Enum.GetNames<T>())}.");

    private static string[] Nomes<T>() where T : struct, System.Enum => System.Enum.GetNames<T>();

    private static (string Nome, JsonObject Def) P(string nome, string tipo, string descricao, string[]? valores = null)
    {
        var def = new JsonObject { ["type"] = tipo, ["description"] = descricao };
        if (valores is not null) def["enum"] = new JsonArray(valores.Select(v => (JsonNode)v).ToArray());
        return (nome, def);
    }

    private static (string Nome, JsonObject Def) PLista(string nome, string descricao, string[]? valores = null)
    {
        var itens = new JsonObject { ["type"] = "string" };
        if (valores is not null) itens["enum"] = new JsonArray(valores.Select(v => (JsonNode)v).ToArray());
        return (nome, new JsonObject { ["type"] = "array", ["items"] = itens, ["description"] = descricao });
    }

    private static JsonObject Schema(params (string Nome, JsonObject Def)[] props) => Schema(props, []);

    private static JsonObject Schema((string Nome, JsonObject Def) prop, string obrigatorio) => Schema([prop], [obrigatorio]);

    private static JsonObject Schema((string Nome, JsonObject Def)[] props, string[] obrigatorios)
    {
        var properties = new JsonObject();
        foreach (var (nome, def) in props) properties[nome] = def;
        var schema = new JsonObject { ["type"] = "object", ["properties"] = properties };
        if (obrigatorios.Length > 0) schema["required"] = new JsonArray(obrigatorios.Select(o => (JsonNode)o).ToArray());
        return schema;
    }
}
