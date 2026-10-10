using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Text.Json;
using LegalManager.Domain;
using LegalManager.Domain.Entities;
using LegalManager.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace LegalManager.Infrastructure.Assistente;

// Dados validados de cada ação, gravados em AcaoAssistente.DadosJson e usados na confirmação.
// Contato: só os campos presentes em Campos mudam (valor null = limpar o campo).
internal sealed record DadosAtualizarContato(Guid ContatoId, Dictionary<string, string?> Campos,
    List<string> TagsAdicionar, List<string> TagsRemover);

internal sealed record DadosCriarTarefa(string Titulo, string? Descricao, Guid ResponsavelId, DateTime? Prazo,
    PrioridadeTarefa Prioridade, TipoTarefa Tipo, Guid? ProcessoId, Guid? ContatoId, List<string> Tags);

internal sealed record DadosAlterarPrazo(Guid TarefaId, DateTime Prazo);

// Tarefa: campos null não mudam; Descricao muda só com AlteraDescricao (null = limpar).
internal sealed record DadosAtualizarTarefa(Guid TarefaId, string? Titulo, bool AlteraDescricao, string? Descricao,
    PrioridadeTarefa? Prioridade, StatusTarefa? Status, Guid? ResponsavelId);

/// <summary>
/// Ferramentas de alteração. Nenhuma grava nada: validam os argumentos, montam o "antes → depois"
/// e registram uma <see cref="AcaoAssistente"/> pendente em <see cref="AcoesPropostas"/>. A
/// alteração só acontece quando o usuário clica em "Confirmar" no chat (<see cref="AssistenteAcoes"/>)
/// — assim um texto malicioso em andamentos/publicações não consegue alterar dados sozinho.
/// </summary>
public partial class AssistenteFerramentas
{
    public const string AtualizarContato = "atualizar_contato";
    public const string CriarTarefa = "criar_tarefa";
    public const string AlterarPrazoTarefa = "alterar_prazo_tarefa";
    public const string AtualizarTarefa = "atualizar_tarefa";
    public const string CriarEvento = "criar_evento";
    public const string RemarcarEvento = "remarcar_evento";
    public const string RegistrarAtendimento = "registrar_atendimento";
    public const string RegistrarHoras = "registrar_horas";

    // Por pergunta: evita uma enxurrada de cartões de confirmação (ex.: "atualize todos os contatos").
    public const int MaxAcoesPorPergunta = 5;

    internal static readonly JsonSerializerOptions JsonDados = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    /// <summary>Ações propostas nesta pergunta; o <see cref="AssistenteService"/> as grava com a interação.</summary>
    public List<AcaoAssistente> AcoesPropostas { get; } = [];

    private List<Definicao> DefinicoesDeAcao() =>
    [
        new(new LlmFerramenta(AtualizarContato,
                "PROPÕE a atualização de dados cadastrais de um contato. Não grava: o usuário confirma num botão do chat. " +
                "Envie só os campos que devem mudar; string vazia (\"\") limpa o campo. Obtenha o contato_id com buscar_contatos.",
                Schema([
                    P("contato_id", "string", "Id (GUID) do contato, obtido em buscar_contatos."),
                    P("nome", "string", "Nome completo / razão social."),
                    P("pessoa", "string", "PF (pessoa física) ou PJ (pessoa jurídica).", Nomes<TipoPessoa>()),
                    P("tipo_contato", "string", "Categoria do contato.", Nomes<TipoContato>()),
                    P("cpf_cnpj", "string", "CPF (11 dígitos) ou CNPJ (14 dígitos), com ou sem pontuação."),
                    P("oab", "string", "Número da OAB."),
                    P("email", "string", "E-mail."),
                    P("telefone", "string", "Telefone."),
                    P("endereco", "string", "Endereço (logradouro, número, complemento, bairro)."),
                    P("cidade", "string", "Cidade."),
                    P("estado", "string", "UF com 2 letras."),
                    P("cep", "string", "CEP."),
                    P("data_nascimento", "string", "Data de nascimento AAAA-MM-DD."),
                    P("observacoes", "string", "Observações (substitui o texto atual: para acrescentar, envie o texto atual + o novo)."),
                    PLista("tags_adicionar", "Tags a acrescentar."),
                    PLista("tags_remover", "Tags a remover."),
                ], ["contato_id"])),
            _ => true, AtualizarContatoAsync),

        new(new LlmFerramenta(CriarTarefa,
                "PROPÕE o cadastro de uma tarefa ou prazo processual. Não grava: o usuário confirma num botão do chat. " +
                "Para o responsável, use o id de listar_equipe (sem responsável: o usuário atual).",
                Schema([
                    P("titulo", "string", "Título da tarefa."),
                    P("descricao", "string", "Descrição."),
                    P("prazo", "string", "Prazo AAAA-MM-DD ou AAAA-MM-DD HH:mm, na hora do escritório. Obrigatório para tipo Prazo."),
                    P("prioridade", "string", "Prioridade (padrão Media).", Nomes<PrioridadeTarefa>()),
                    P("tipo", "string", "Tarefa comum ou prazo processual (padrão Tarefa).", Nomes<TipoTarefa>()),
                    P("responsavel_id", "string", "Id (GUID) do responsável, obtido em listar_equipe."),
                    P("processo_id", "string", "Id (GUID) do processo vinculado, obtido em buscar_processos."),
                    P("contato_id", "string", "Id (GUID) do contato vinculado, obtido em buscar_contatos."),
                    PLista("tags", "Tags da tarefa."),
                ], ["titulo"])),
            _ => true, CriarTarefaAsync),

        new(new LlmFerramenta(AlterarPrazoTarefa,
                "PROPÕE a alteração da data (prazo) de uma tarefa existente. Não grava: o usuário confirma num botão do chat. " +
                "Obtenha o tarefa_id com listar_tarefas ou detalhar_processo.",
                Schema([
                    P("tarefa_id", "string", "Id (GUID) da tarefa."),
                    P("prazo", "string", "Novo prazo AAAA-MM-DD ou AAAA-MM-DD HH:mm, na hora do escritório. Só a data: mantém o horário atual."),
                ], ["tarefa_id", "prazo"])),
            _ => true, AlterarPrazoTarefaAsync),

        new(new LlmFerramenta(AtualizarTarefa,
                "PROPÕE alterações numa tarefa existente: concluir (status Concluida), mudar status, prioridade, " +
                "responsável, título ou descrição. Para o prazo use alterar_prazo_tarefa. Não grava: o usuário confirma " +
                "num botão do chat. Obtenha o tarefa_id com listar_tarefas e o responsavel_id com listar_equipe.",
                Schema([
                    P("tarefa_id", "string", "Id (GUID) da tarefa."),
                    P("status", "string", "Novo status. Concluida = marcar como feita.", Nomes<StatusTarefa>()),
                    P("prioridade", "string", "Nova prioridade.", Nomes<PrioridadeTarefa>()),
                    P("responsavel_id", "string", "Id (GUID) do novo responsável, obtido em listar_equipe."),
                    P("titulo", "string", "Novo título."),
                    P("descricao", "string", "Nova descrição (substitui a atual; \"\" limpa)."),
                ], ["tarefa_id"])),
            _ => true, AtualizarTarefaAsync),

        .. DefinicoesDeAgenda(),
    ];

    // ── Contato ──────────────────────────────────────────────────────────────

    private async Task<object> AtualizarContatoAsync(JsonElement a, CancellationToken ct)
    {
        LimiteDeAcoes();
        var id = Id(a, "contato_id") ?? throw new ArgumentException("Informe contato_id.");
        var c = await _db.Contatos.AsNoTracking().Include(c => c.Tags)
            .FirstOrDefaultAsync(c => c.Id == id && c.TenantId == TenantId, ct);
        if (c is null) return new { erro = "Contato não encontrado." };

        var campos = new Dictionary<string, string?>();
        var alteracoes = new List<object>();
        void Mudar(string chave, string rotulo, string? atual, string? novo, Func<string?, string?>? exibir = null)
        {
            if (string.Equals(atual ?? "", novo ?? "", StringComparison.Ordinal)) return;
            campos[chave] = novo;
            exibir ??= v => v;
            alteracoes.Add(new { campo = rotulo, antes = exibir(atual), depois = exibir(novo) });
        }

        if (Campo(a, "nome", out var nome))
        {
            if (nome is null) throw new ArgumentException("O nome não pode ficar vazio.");
            if (nome.Length > 300) throw new ArgumentException("nome: máximo de 300 caracteres.");
            Mudar(nameof(Contato.Nome), "Nome", c.Nome, nome);
        }
        if (Campo(a, "pessoa", out var pessoa))
            Mudar(nameof(Contato.Tipo), "Pessoa", c.Tipo.ToString(),
                Enum<TipoPessoa>(pessoa ?? throw new ArgumentException("pessoa não pode ficar vazio."), "pessoa").ToString());
        if (Campo(a, "tipo_contato", out var tipo))
            Mudar(nameof(Contato.TipoContato), "Categoria", c.TipoContato.ToString(),
                Enum<TipoContato>(tipo ?? throw new ArgumentException("tipo_contato não pode ficar vazio."), "tipo_contato").ToString());
        if (Campo(a, "cpf_cnpj", out var doc))
        {
            if (doc is not null && (doc.Count(char.IsDigit) is not (11 or 14) || doc.Length > 18))
                throw new ArgumentException("cpf_cnpj inválido: o CPF tem 11 dígitos e o CNPJ, 14.");
            Mudar(nameof(Contato.CpfCnpj), "CPF/CNPJ", c.CpfCnpj, doc);
        }
        if (Campo(a, "oab", out var oab)) Mudar(nameof(Contato.Oab), "OAB", c.Oab, Maximo(oab, 20, "oab"));
        if (Campo(a, "email", out var email))
        {
            if (email is not null && (email.Length > 200 || !new EmailAddressAttribute().IsValid(email)))
                throw new ArgumentException("email inválido.");
            Mudar(nameof(Contato.Email), "E-mail", c.Email, email);
        }
        if (Campo(a, "telefone", out var tel)) Mudar(nameof(Contato.Telefone), "Telefone", c.Telefone, Maximo(tel, 20, "telefone"));
        if (Campo(a, "endereco", out var end)) Mudar(nameof(Contato.Endereco), "Endereço", c.Endereco, end);
        if (Campo(a, "cidade", out var cidade)) Mudar(nameof(Contato.Cidade), "Cidade", c.Cidade, cidade);
        if (Campo(a, "estado", out var uf))
        {
            if (uf is not null && (uf.Length != 2 || !uf.All(char.IsLetter)))
                throw new ArgumentException("estado inválido: use a UF com 2 letras (ex.: SP).");
            Mudar(nameof(Contato.Estado), "UF", c.Estado, uf?.ToUpperInvariant());
        }
        if (Campo(a, "cep", out var cep)) Mudar(nameof(Contato.Cep), "CEP", c.Cep, Maximo(cep, 10, "cep"));
        if (Campo(a, "data_nascimento", out var nasc))
        {
            if (nasc is not null)
            {
                var d = DataArg(a, "data_nascimento")!.Value;
                if (d > await AgoraAsync(ct)) throw new ArgumentException("data_nascimento não pode ser futura.");
                nasc = Data(d);
            }
            Mudar(nameof(Contato.DataNascimento), "Nascimento", Data(c.DataNascimento), nasc, DataBr);
        }
        if (Campo(a, "observacoes", out var obs)) Mudar(nameof(Contato.Observacoes), "Observações", c.Observacoes, obs);

        var atuais = c.Tags.Select(t => t.Tag).ToList();
        var adicionar = Lista(a, "tags_adicionar")
            .Where(t => !atuais.Contains(t, StringComparer.OrdinalIgnoreCase)).ToList();
        if (adicionar.Any(t => t.Length > 100)) throw new ArgumentException("Tag com mais de 100 caracteres.");
        var remover = new List<string>();
        foreach (var t in Lista(a, "tags_remover"))
        {
            var existente = atuais.FirstOrDefault(x => Normalizar(x) == Normalizar(t))
                ?? throw new ArgumentException($"O contato não tem a tag \"{t}\". Tags atuais: {string.Join(", ", atuais)}.");
            remover.Add(existente);
        }
        if (adicionar.Count > 0 || remover.Count > 0)
        {
            var novas = atuais.Except(remover).Concat(adicionar).ToList();
            alteracoes.Add(new { campo = "Tags", antes = Juntar(atuais), depois = Juntar(novas) });
        }

        if (alteracoes.Count == 0)
            return new { erro = "Nada a alterar: os valores informados já são os atuais do contato." };

        return Propor(AtualizarContato, $"Atualizar contato {c.Nome}", c.Id, alteracoes,
            new DadosAtualizarContato(c.Id, campos, adicionar, remover));
    }

    // ── Tarefas ──────────────────────────────────────────────────────────────

    private async Task<object> CriarTarefaAsync(JsonElement a, CancellationToken ct)
    {
        LimiteDeAcoes();
        var titulo = Texto(a, "titulo") ?? throw new ArgumentException("Informe o titulo.");
        if (titulo.Length > 300) throw new ArgumentException("titulo: máximo de 300 caracteres.");
        var descricao = Texto(a, "descricao");
        var prazo = PrazoArg(a, "prazo");
        var prioridade = Enum<PrioridadeTarefa>(a, "prioridade") ?? PrioridadeTarefa.Media;
        var tipo = Enum<TipoTarefa>(a, "tipo") ?? TipoTarefa.Tarefa;
        if (tipo == TipoTarefa.Prazo && prazo is null) throw new ArgumentException("Prazo processual precisa da data (prazo).");
        var tags = Lista(a, "tags");
        if (tags.Any(t => t.Length > 100)) throw new ArgumentException("Tag com mais de 100 caracteres.");

        var responsavelId = Id(a, "responsavel_id") ?? _tenant.UserId;
        if (await ResponsavelAsync(responsavelId, ct) is not { } responsavel) return ErroResponsavel;

        string? processo = null;
        if (Id(a, "processo_id") is { } pid && (processo = await NumeroProcessoAsync(pid, ct)) is null)
            return new { erro = "Processo não encontrado." };
        string? contato = null;
        if (Id(a, "contato_id") is { } cid)
        {
            contato = await _db.Contatos.AsNoTracking().Where(c => c.Id == cid && c.TenantId == TenantId)
                .Select(c => c.Nome).FirstOrDefaultAsync(ct);
            if (contato is null) return new { erro = "Contato não encontrado." };
        }

        var alteracoes = new List<object> { new { campo = "Título", antes = (string?)null, depois = titulo } };
        void Mostrar(string rotulo, string? valor) { if (valor is not null) alteracoes.Add(new { campo = rotulo, antes = (string?)null, depois = valor }); }
        Mostrar("Tipo", tipo == TipoTarefa.Prazo ? "Prazo processual" : "Tarefa");
        Mostrar("Prazo", PrazoBr(prazo));
        Mostrar("Prioridade", Rotulo(prioridade));
        Mostrar("Responsável", responsavel);
        Mostrar("Processo", processo);
        Mostrar("Contato", contato);
        Mostrar("Descrição", Cortar(descricao, 300));
        Mostrar("Tags", tags.Count > 0 ? Juntar(tags) : null);

        var aviso = prazo is { } p && p < await AgoraAsync(ct) ? "O prazo informado já passou." : null;
        return Propor(CriarTarefa, $"Criar tarefa \"{Cortar(titulo, 120)}\"", null, alteracoes,
            new DadosCriarTarefa(titulo, descricao, responsavelId, prazo, prioridade, tipo,
                Id(a, "processo_id"), Id(a, "contato_id"), tags), aviso);
    }

    private async Task<object> AlterarPrazoTarefaAsync(JsonElement a, CancellationToken ct)
    {
        LimiteDeAcoes();
        var id = Id(a, "tarefa_id") ?? throw new ArgumentException("Informe tarefa_id.");
        var novo = PrazoArg(a, "prazo") ?? throw new ArgumentException("Informe o novo prazo.");
        var t = await _db.Tarefas.AsNoTracking()
            .Where(t => t.Id == id && t.TenantId == TenantId)
            .Select(t => new { t.Id, t.Titulo, t.Prazo, t.Status, t.QuantidadeDias, t.TipoCalculo, t.DataInicio })
            .FirstOrDefaultAsync(ct);
        if (t is null) return new { erro = "Tarefa não encontrada." };
        if (t.Status is StatusTarefa.Concluida or StatusTarefa.Cancelada)
            return new { erro = $"A tarefa está {(t.Status == StatusTarefa.Concluida ? "concluída" : "cancelada")}: reabra-a na tela de Tarefas antes de mudar o prazo." };

        // Só a data: mantém o horário que a tarefa já tinha.
        var soData = Texto(a, "prazo")!.Length == "AAAA-MM-DD".Length;
        if (soData && t.Prazo is { } atual) novo = novo.Date.Add(atual.TimeOfDay);
        if (novo == t.Prazo) return new { erro = "Nada a alterar: a tarefa já tem esse prazo." };

        var alteracoes = new List<object> { new { campo = "Prazo", antes = PrazoBr(t.Prazo), depois = PrazoBr(novo) } };
        if (t.QuantidadeDias is { } dias)
            alteracoes.Add(new
            {
                campo = "Cálculo do prazo",
                antes = $"{dias} {(t.TipoCalculo == TipoCalculo.DiasCorridos ? "dias corridos" : "dias úteis")}" +
                        (t.DataInicio is { } ini ? $" a partir de {ini.ToString("dd/MM/yyyy", BrasiliaTime.PtBr)}" : ""),
                depois = "data fixa"
            });

        var aviso = novo < await AgoraAsync(ct) ? "O novo prazo já passou." : null;
        return Propor(AlterarPrazoTarefa, $"Alterar prazo da tarefa \"{Cortar(t.Titulo, 120)}\"", t.Id, alteracoes,
            new DadosAlterarPrazo(t.Id, novo), aviso);
    }

    private async Task<object> AtualizarTarefaAsync(JsonElement a, CancellationToken ct)
    {
        LimiteDeAcoes();
        var id = Id(a, "tarefa_id") ?? throw new ArgumentException("Informe tarefa_id.");
        var t = await _db.Tarefas.AsNoTracking()
            .Where(t => t.Id == id && t.TenantId == TenantId)
            .Select(t => new
            {
                t.Id, t.Titulo, t.Descricao, t.Status, t.Prioridade, t.ResponsavelId,
                Responsavel = t.Responsavel != null ? t.Responsavel.Nome : null
            })
            .FirstOrDefaultAsync(ct);
        if (t is null) return new { erro = "Tarefa não encontrada." };

        var alteracoes = new List<object>();
        string? titulo = null;
        if (Texto(a, "titulo") is { } novoTitulo && novoTitulo != t.Titulo)
        {
            if (novoTitulo.Length > 300) throw new ArgumentException("titulo: máximo de 300 caracteres.");
            titulo = novoTitulo;
            alteracoes.Add(new { campo = "Título", antes = t.Titulo, depois = novoTitulo });
        }
        var alteraDescricao = Campo(a, "descricao", out var descricao) && (descricao ?? "") != (t.Descricao ?? "");
        if (alteraDescricao)
            alteracoes.Add(new { campo = "Descrição", antes = Cortar(t.Descricao, 300), depois = Cortar(descricao, 300) });

        var status = Enum<StatusTarefa>(a, "status") is { } st && st != t.Status ? st : (StatusTarefa?)null;
        if (status is { } s) alteracoes.Add(new { campo = "Status", antes = Rotulo(t.Status), depois = Rotulo(s) });
        var prioridade = Enum<PrioridadeTarefa>(a, "prioridade") is { } pr && pr != t.Prioridade ? pr : (PrioridadeTarefa?)null;
        if (prioridade is { } p) alteracoes.Add(new { campo = "Prioridade", antes = Rotulo(t.Prioridade), depois = Rotulo(p) });

        Guid? responsavelId = null;
        if (Id(a, "responsavel_id") is { } rid && rid != t.ResponsavelId)
        {
            if (await ResponsavelAsync(rid, ct) is not { } nome) return ErroResponsavel;
            responsavelId = rid;
            alteracoes.Add(new { campo = "Responsável", antes = t.Responsavel, depois = nome });
        }

        if (alteracoes.Count == 0) return new { erro = "Nada a alterar: os valores informados já são os atuais da tarefa." };

        var acao = alteracoes.Count == 1 && status == StatusTarefa.Concluida ? "Concluir" : "Atualizar";
        return Propor(AtualizarTarefa, $"{acao} tarefa \"{Cortar(t.Titulo, 120)}\"", t.Id, alteracoes,
            new DadosAtualizarTarefa(t.Id, titulo, alteraDescricao, descricao, prioridade, status, responsavelId));
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static readonly object ErroResponsavel = new { erro = "Responsável não encontrado na equipe (use o id de listar_equipe)." };

    /// <summary>Nome do usuário ativo do escritório; null se não for da equipe.</summary>
    private Task<string?> ResponsavelAsync(Guid id, CancellationToken ct) =>
        _db.Users.AsNoTracking()
            .Where(u => u.Id == id && u.TenantId == TenantId && u.Ativo)
            .Select(u => u.Nome).FirstOrDefaultAsync(ct);

    private Task<string?> NumeroProcessoAsync(Guid id, CancellationToken ct) =>
        _db.Processos.AsNoTracking().Where(p => p.Id == id && p.TenantId == TenantId)
            .Select(p => p.NumeroCNJ).FirstOrDefaultAsync(ct);

    private static string Rotulo(StatusTarefa s) => s switch
    {
        StatusTarefa.EmAndamento => "Em andamento",
        StatusTarefa.Concluida => "Concluída",
        _ => s.ToString()
    };

    private static string Rotulo(PrioridadeTarefa p) => p == PrioridadeTarefa.Media ? "Média" : p.ToString();

    private void LimiteDeAcoes()
    {
        if (AcoesPropostas.Count >= MaxAcoesPorPergunta)
            throw new ArgumentException($"Limite de {MaxAcoesPorPergunta} alterações por pergunta. Peça ao usuário para confirmar estas e continuar em seguida.");
    }

    private object Propor(string tipo, string titulo, Guid? entidadeId, List<object> alteracoes, object dados, string? aviso = null)
    {
        var agora = DateTime.UtcNow;
        AcoesPropostas.Add(new AcaoAssistente
        {
            Id = Guid.NewGuid(),
            TenantId = TenantId,
            UsuarioId = _tenant.UserId,
            ImpersonadoPorId = _tenant.ImpersonadoPorId,
            CriadoEm = agora,
            ExpiraEm = agora + AssistenteAcoes.Validade,
            Tipo = tipo,
            EntidadeId = entidadeId,
            Titulo = titulo,
            AlteracoesJson = JsonSerializer.Serialize(alteracoes, JsonDados),
            DadosJson = JsonSerializer.Serialize(dados, JsonDados),
            Status = StatusAcaoAssistente.Pendente
        });
        return new
        {
            status = "aguardando_confirmacao",
            acao = titulo,
            alteracoes,
            aviso,
            instrucao = "NADA foi gravado ainda. O usuário verá um cartão com essas alterações e os botões Confirmar/Cancelar " +
                        "logo abaixo da sua resposta. Resuma o que será feito e peça para ele conferir e confirmar no cartão."
        };
    }

    /// <summary>
    /// Campo de texto opcional: false se não veio; valor null quando veio vazio/null (limpar o campo).
    /// </summary>
    private static bool Campo(JsonElement a, string nome, out string? valor)
    {
        valor = null;
        if (a.ValueKind != JsonValueKind.Object || !a.TryGetProperty(nome, out var v)) return false;
        if (v.ValueKind == JsonValueKind.Null) return true;
        if (v.ValueKind != JsonValueKind.String) throw new ArgumentException($"{nome} deve ser texto.");
        var s = v.GetString()!.Trim();
        valor = s.Length == 0 ? null : s;
        return true;
    }

    private static string? Maximo(string? s, int max, string nome) =>
        s is not null && s.Length > max ? throw new ArgumentException($"{nome}: máximo de {max} caracteres.") : s;

    private static string? Juntar(IEnumerable<string> itens) =>
        string.Join(", ", itens) is { Length: > 0 } s ? s : null;

    private static string? DataBr(string? iso) =>
        iso is null ? null
            : DateTime.ParseExact(iso, "yyyy-MM-dd", CultureInfo.InvariantCulture).ToString("dd/MM/yyyy", BrasiliaTime.PtBr);

    // Prazos na hora "de parede" do escritório (ver CLAUDE.md → Datas).
    private static string? PrazoBr(DateTime? d) =>
        d?.ToString(d.Value.TimeOfDay == TimeSpan.Zero ? "dd/MM/yyyy" : "dd/MM/yyyy HH:mm", BrasiliaTime.PtBr);

    private static DateTime? PrazoArg(JsonElement a, string nome)
    {
        if (Texto(a, nome) is not { } s) return null;
        string[] formatos = ["yyyy-MM-dd", "yyyy-MM-dd HH:mm", "yyyy-MM-dd'T'HH:mm", "yyyy-MM-dd'T'HH:mm:ss"];
        return DateTime.TryParseExact(s, formatos, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d
            : throw new ArgumentException($"{nome} inválido: use AAAA-MM-DD ou AAAA-MM-DD HH:mm.");
    }
}
