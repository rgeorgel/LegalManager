using System.Globalization;
using System.Text.Json;
using LegalManager.Domain;
using LegalManager.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace LegalManager.Infrastructure.Assistente;

internal sealed record DadosCriarEvento(string Titulo, TipoEvento Tipo, DateTime Inicio, DateTime? Fim, string? Local,
    Guid ResponsavelId, Guid? ProcessoId, string? Observacoes);

internal sealed record DadosRemarcarEvento(Guid EventoId, DateTime Inicio, DateTime? Fim);

internal sealed record DadosRegistrarAtendimento(Guid ContatoId, DateTime Data, string Descricao);

// Timesheet guarda carimbos UTC reais (diferente de prazos/eventos): a conversão do horário
// do escritório para UTC é feita aqui, na proposta.
internal sealed record DadosRegistrarHoras(DateTime InicioUtc, DateTime FimUtc, string? Descricao, Guid? ProcessoId, Guid? TarefaId);

/// <summary>Ferramentas de alteração da agenda, atendimentos e horas (ver <c>AssistenteFerramentas.Acoes.cs</c>).</summary>
public partial class AssistenteFerramentas
{
    private const int MaxMinutosPorRegistro = 24 * 60;
    // Prazo é Tarefa (Tipo=Prazo) — a agenda mostra as tarefas com prazo.
    private static readonly string[] TiposDeEvento = Nomes<TipoEvento>().Where(n => n != nameof(TipoEvento.Prazo)).ToArray();

    private List<Definicao> DefinicoesDeAgenda() =>
    [
        new(new LlmFerramenta(CriarEvento,
                "PROPÕE um compromisso na agenda (audiência, reunião, perícia...). Não grava: o usuário confirma num " +
                "botão do chat. Precisa de data E horário de início; sem horário, pergunte ao usuário. " +
                "Prazo não é evento: use criar_tarefa com tipo Prazo.",
                Schema([
                    P("titulo", "string", "Título do compromisso."),
                    P("tipo", "string", "Tipo de evento.", TiposDeEvento),
                    P("inicio", "string", "Início AAAA-MM-DD HH:mm, na hora do escritório."),
                    P("fim", "string", "Fim AAAA-MM-DD HH:mm (opcional)."),
                    P("duracao_minutos", "integer", "Duração em minutos, se o fim não for informado (opcional)."),
                    P("local", "string", "Local (endereço, sala, link da videochamada)."),
                    P("responsavel_id", "string", "Id (GUID) do responsável, obtido em listar_equipe (padrão: usuário atual)."),
                    P("processo_id", "string", "Id (GUID) do processo vinculado, obtido em buscar_processos."),
                    P("observacoes", "string", "Observações."),
                ], ["titulo", "tipo", "inicio"])),
            _ => true, CriarEventoAsync),

        new(new LlmFerramenta(RemarcarEvento,
                "PROPÕE remarcar um compromisso da agenda. Não grava: o usuário confirma num botão do chat. " +
                "Obtenha o evento_id com listar_eventos. Sem fim informado, mantém a duração original.",
                Schema([
                    P("evento_id", "string", "Id (GUID) do evento."),
                    P("inicio", "string", "Novo início AAAA-MM-DD HH:mm (só a data: mantém o horário atual)."),
                    P("fim", "string", "Novo fim AAAA-MM-DD HH:mm (opcional)."),
                ], ["evento_id", "inicio"])),
            _ => true, RemarcarEventoAsync),

        new(new LlmFerramenta(RegistrarAtendimento,
                "PROPÕE registrar um atendimento no histórico de um contato (ligação, reunião, mensagem...). " +
                "Não grava: o usuário confirma num botão do chat. Obtenha o contato_id com buscar_contatos.",
                Schema([
                    P("contato_id", "string", "Id (GUID) do contato."),
                    P("descricao", "string", "O que foi tratado no atendimento, como o usuário contou."),
                    P("data", "string", "Data AAAA-MM-DD (padrão: hoje)."),
                ], ["contato_id", "descricao"])),
            _ => true, RegistrarAtendimentoAsync),

        new(new LlmFerramenta(RegistrarHoras,
                "PROPÕE lançar horas trabalhadas no timesheet do usuário atual. Não grava: o usuário confirma num " +
                "botão do chat. Só para o próprio usuário.",
                Schema([
                    P("minutos", "integer", "Duração em minutos (ex.: 1h30 = 90)."),
                    P("data", "string", "Dia AAAA-MM-DD (padrão: hoje)."),
                    P("inicio", "string", "Horário de início HH:mm (opcional; hoje sem início: termina agora)."),
                    P("descricao", "string", "Atividade realizada."),
                    P("processo_id", "string", "Id (GUID) do processo, obtido em buscar_processos."),
                    P("tarefa_id", "string", "Id (GUID) da tarefa, obtido em listar_tarefas."),
                ], ["minutos"])),
            _ => true, RegistrarHorasAsync),
    ];

    // ── Agenda ───────────────────────────────────────────────────────────────

    private async Task<object> CriarEventoAsync(JsonElement a, CancellationToken ct)
    {
        LimiteDeAcoes();
        var titulo = Texto(a, "titulo") ?? throw new ArgumentException("Informe o titulo.");
        if (titulo.Length > 300) throw new ArgumentException("titulo: máximo de 300 caracteres.");
        var tipo = Enum<TipoEvento>(a, "tipo") ?? throw new ArgumentException($"Informe o tipo: {string.Join(", ", TiposDeEvento)}.");
        if (tipo == TipoEvento.Prazo)
            return new { erro = "Prazo não é evento da agenda: use criar_tarefa com tipo Prazo." };
        var inicio = DataHoraArg(a, "inicio") ?? throw new ArgumentException("Informe o inicio.");
        if (Texto(a, "inicio")!.Length == "AAAA-MM-DD".Length)
            return new { erro = "Falta o horário do compromisso: pergunte ao usuário." };
        var fim = FimEvento(a, inicio);
        var local = Maximo(Texto(a, "local"), 300, "local");
        var observacoes = Texto(a, "observacoes");

        var responsavelId = Id(a, "responsavel_id") ?? _tenant.UserId;
        if (await ResponsavelAsync(responsavelId, ct) is not { } responsavel) return ErroResponsavel;
        string? processo = null;
        if (Id(a, "processo_id") is { } pid && (processo = await NumeroProcessoAsync(pid, ct)) is null)
            return new { erro = "Processo não encontrado." };

        var alteracoes = new List<object>();
        void Mostrar(string rotulo, string? valor) { if (valor is not null) alteracoes.Add(new { campo = rotulo, antes = (string?)null, depois = valor }); }
        Mostrar("Título", titulo);
        Mostrar("Tipo", Rotulo(tipo));
        Mostrar("Quando", Periodo(inicio, fim));
        Mostrar("Local", local);
        Mostrar("Responsável", responsavel);
        Mostrar("Processo", processo);
        Mostrar("Observações", Cortar(observacoes, 300));

        var aviso = inicio < await AgoraAsync(ct) ? "O horário informado já passou." : null;
        return Propor(CriarEvento, $"Agendar {Rotulo(tipo).ToLowerInvariant()} \"{Cortar(titulo, 120)}\"", null, alteracoes,
            new DadosCriarEvento(titulo, tipo, inicio, fim, local, responsavelId, Id(a, "processo_id"), observacoes), aviso);
    }

    private async Task<object> RemarcarEventoAsync(JsonElement a, CancellationToken ct)
    {
        LimiteDeAcoes();
        var id = Id(a, "evento_id") ?? throw new ArgumentException("Informe evento_id.");
        var inicio = DataHoraArg(a, "inicio") ?? throw new ArgumentException("Informe o novo inicio.");
        var e = await _db.Eventos.AsNoTracking()
            .Where(e => e.Id == id && e.TenantId == TenantId)
            .Select(e => new { e.Id, e.Titulo, e.DataHora, e.DataHoraFim })
            .FirstOrDefaultAsync(ct);
        if (e is null) return new { erro = "Evento não encontrado." };

        if (Texto(a, "inicio")!.Length == "AAAA-MM-DD".Length) inicio = inicio.Date.Add(e.DataHora.TimeOfDay);
        // Sem fim: mantém a duração que o compromisso tinha.
        var fim = Texto(a, "fim") is null
            ? e.DataHoraFim is { } f ? inicio + (f - e.DataHora) : null
            : FimEvento(a, inicio);
        if (inicio == e.DataHora && fim == e.DataHoraFim) return new { erro = "Nada a alterar: o compromisso já está nesse horário." };

        var alteracoes = new List<object> { new { campo = "Quando", antes = Periodo(e.DataHora, e.DataHoraFim), depois = Periodo(inicio, fim) } };
        var aviso = inicio < await AgoraAsync(ct) ? "O novo horário já passou." : null;
        return Propor(RemarcarEvento, $"Remarcar \"{Cortar(e.Titulo, 120)}\"", e.Id, alteracoes,
            new DadosRemarcarEvento(e.Id, inicio, fim), aviso);
    }

    private static DateTime? FimEvento(JsonElement a, DateTime inicio)
    {
        DateTime? fim = DataHoraArg(a, "fim");
        if (fim is null && a.ValueKind == JsonValueKind.Object && a.TryGetProperty("duracao_minutos", out var d) && d.TryGetInt32(out var min))
        {
            if (min is <= 0 or > MaxMinutosPorRegistro) throw new ArgumentException("duracao_minutos inválida.");
            fim = inicio.AddMinutes(min);
        }
        if (fim <= inicio) throw new ArgumentException("O fim precisa ser depois do início.");
        return fim;
    }

    // ── Atendimento ──────────────────────────────────────────────────────────

    private async Task<object> RegistrarAtendimentoAsync(JsonElement a, CancellationToken ct)
    {
        LimiteDeAcoes();
        var contatoId = Id(a, "contato_id") ?? throw new ArgumentException("Informe contato_id.");
        var descricao = Texto(a, "descricao") ?? throw new ArgumentException("Informe a descricao do atendimento.");
        if (descricao.Length > 4000) throw new ArgumentException("descricao: máximo de 4000 caracteres.");
        var hoje = (await AgoraAsync(ct)).Date;
        var data = DataArg(a, "data") is { } d ? DateTime.SpecifyKind(d, DateTimeKind.Unspecified) : hoje;
        if (data > hoje) throw new ArgumentException("A data do atendimento não pode ser futura.");

        var nome = await _db.Contatos.AsNoTracking().Where(c => c.Id == contatoId && c.TenantId == TenantId)
            .Select(c => c.Nome).FirstOrDefaultAsync(ct);
        if (nome is null) return new { erro = "Contato não encontrado." };

        List<object> alteracoes =
        [
            new { campo = "Contato", antes = (string?)null, depois = nome },
            new { campo = "Data", antes = (string?)null, depois = data.ToString("dd/MM/yyyy", BrasiliaTime.PtBr) },
            new { campo = "Descrição", antes = (string?)null, depois = Cortar(descricao, 600) },
        ];
        return Propor(RegistrarAtendimento, $"Registrar atendimento de {nome}", contatoId, alteracoes,
            new DadosRegistrarAtendimento(contatoId, data, descricao));
    }

    // ── Timesheet ────────────────────────────────────────────────────────────

    private async Task<object> RegistrarHorasAsync(JsonElement a, CancellationToken ct)
    {
        LimiteDeAcoes();
        if (a.ValueKind != JsonValueKind.Object || !a.TryGetProperty("minutos", out var m) || !m.TryGetInt32(out var minutos)
            || minutos is <= 0 or > MaxMinutosPorRegistro)
            throw new ArgumentException($"Informe minutos entre 1 e {MaxMinutosPorRegistro}.");
        var agora = await AgoraAsync(ct);
        var dia = DataArg(a, "data") is { } d ? DateTime.SpecifyKind(d, DateTimeKind.Unspecified) : agora.Date;

        DateTime inicio;
        if (Texto(a, "inicio") is { } hora)
        {
            if (!TimeSpan.TryParseExact(hora, @"hh\:mm", CultureInfo.InvariantCulture, out var h))
                throw new ArgumentException("inicio inválido: use HH:mm.");
            inicio = dia.Add(h);
        }
        else inicio = dia == agora.Date ? agora.AddMinutes(-minutos) : dia.AddHours(9);
        inicio = new DateTime(inicio.Year, inicio.Month, inicio.Day, inicio.Hour, inicio.Minute, 0);
        var fim = inicio.AddMinutes(minutos);
        if (fim > agora.AddMinutes(1)) return new { erro = "Não é possível lançar horas que terminam no futuro." };

        var descricao = Texto(a, "descricao");
        string? processo = null;
        if (Id(a, "processo_id") is { } pid && (processo = await NumeroProcessoAsync(pid, ct)) is null)
            return new { erro = "Processo não encontrado." };
        string? tarefa = null;
        if (Id(a, "tarefa_id") is { } tid)
        {
            tarefa = await _db.Tarefas.AsNoTracking().Where(t => t.Id == tid && t.TenantId == TenantId)
                .Select(t => t.Titulo).FirstOrDefaultAsync(ct);
            if (tarefa is null) return new { erro = "Tarefa não encontrada." };
        }

        var alteracoes = new List<object>
        {
            new { campo = "Quando", antes = (string?)null, depois = $"{Periodo(inicio, fim)} ({Duracao(minutos)})" }
        };
        if (descricao is not null) alteracoes.Add(new { campo = "Atividade", antes = (string?)null, depois = Cortar(descricao, 300) });
        if (processo is not null) alteracoes.Add(new { campo = "Processo", antes = (string?)null, depois = processo });
        if (tarefa is not null) alteracoes.Add(new { campo = "Tarefa", antes = (string?)null, depois = tarefa });

        var tz = _tz ?? BrasiliaTime.Tz;
        return Propor(RegistrarHoras, $"Lançar {Duracao(minutos)} no timesheet", null, alteracoes,
            new DadosRegistrarHoras(ParaUtc(inicio, tz), ParaUtc(fim, tz), descricao, Id(a, "processo_id"), Id(a, "tarefa_id")));
    }

    private static DateTime ParaUtc(DateTime parede, TimeZoneInfo tz) =>
        TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(parede, DateTimeKind.Unspecified), tz);

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static DateTime? DataHoraArg(JsonElement a, string nome) => PrazoArg(a, nome);

    private static string Periodo(DateTime inicio, DateTime? fim)
    {
        var texto = inicio.ToString("dd/MM/yyyy HH:mm", BrasiliaTime.PtBr);
        if (fim is not { } f) return texto;
        return texto + (f.Date == inicio.Date ? $"–{f:HH:mm}" : $" até {f.ToString("dd/MM/yyyy HH:mm", BrasiliaTime.PtBr)}");
    }

    private static string Duracao(int minutos) =>
        minutos < 60 ? $"{minutos} min" : minutos % 60 == 0 ? $"{minutos / 60}h" : $"{minutos / 60}h{minutos % 60:00}";

    private static string Rotulo(TipoEvento t) => t switch
    {
        TipoEvento.Audiencia => "Audiência",
        TipoEvento.Reuniao => "Reunião",
        TipoEvento.Pericia => "Perícia",
        _ => t.ToString()
    };
}
