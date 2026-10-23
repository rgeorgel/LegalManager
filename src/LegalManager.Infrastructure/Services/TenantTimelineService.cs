using System.Text.Json;
using LegalManager.Application.DTOs.SuperAdmin;
using LegalManager.Domain.Entities;
using LegalManager.Domain.Enums;
using LegalManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LegalManager.Infrastructure.Services;

/// <summary>
/// Timeline do tenant no superadmin (GET /api/superadmin/tenants/{id}/timeline). Não tem tabela
/// própria: junta os registros que já existem (CriadoEm das entidades, AuditLog, logs de IA, de
/// consultas externas, das calculadoras…) e devolve do mais recente para o mais antigo.
///
/// Paginação por dia de Brasília: cada página traz dias completos até juntar ~<see cref="EventosPorPagina"/>
/// eventos, e <c>antes</c> pede os dias anteriores ao informado. Fontes de alto volume (processos,
/// tarefas, logins, IA…) viram um evento por dia com os primeiros registros em Itens.
/// </summary>
public sealed class TenantTimelineService(AppDbContext db)
{
    public const int EventosPorPagina = 40;
    private const int LimiteIndividual = 200;
    private const int LimiteAgrupado = 3000;
    private const int MaxSubitens = 30;
    private const int MaxTexto = 400;

    public static readonly IReadOnlyList<string> Categorias =
    [
        "conta", "assinatura", "superadmin", "configuracoes", "oab", "processos", "tarefas", "agenda",
        "contatos", "documentos", "financeiro", "calculadoras", "ia", "integracoes"
    ];

    private static readonly string[] AcoesAssinatura =
        [TimelineAcoes.AssinaturaIniciada, TimelineAcoes.AssinaturaAlterada, TimelineAcoes.AssinaturaCancelada,
         TimelineAcoes.AssinaturaEncerrada, TimelineAcoes.PlanoExpirado];
    private static readonly string[] AcoesSuperAdmin =
        [TimelineAcoes.PlanoAlterado, TimelineAcoes.StatusAlterado, TimelineAcoes.TrialPlusConcedido,
         "EXPORT", "IMPORT"];
    // "Entrar como" do superadmin: só aparece quando pedido (incluirAcessosSuperAdmin), para
    // não misturar o suporte com o uso real do escritório.
    private static readonly string[] AcoesSuperAdminComAcessos = [.. AcoesSuperAdmin, AuditActions.ImpersonationStart];
    private static readonly string[] AcoesConfiguracao =
        [TimelineAcoes.TemaAlterado, TimelineAcoes.LogoAlterado, TimelineAcoes.LogoRemovido, TimelineAcoes.FusoAlterado];

    private sealed class Ev
    {
        public required string Id { get; init; }
        public required DateTime Data { get; init; }
        public required string Categoria { get; init; }
        public required string Tipo { get; init; }
        public required string Titulo { get; init; }
        public string? Descricao { get; init; }
        public Guid? UsuarioId { get; init; }
        public int Quantidade { get; init; } = 1;
        public Dictionary<string, object?>? Detalhes { get; init; }
        public List<Sub>? Itens { get; init; }
    }

    private sealed record Sub(DateTime Data, string Titulo, string? Descricao, Guid? UsuarioId, Dictionary<string, object?>? Detalhes = null);

    private sealed class Coleta(Guid tenantId, DateTime limiteUtc)
    {
        public Guid TenantId { get; } = tenantId;
        /// <summary>Exclusivo: só entra o que aconteceu antes deste instante.</summary>
        public DateTime LimiteUtc { get; } = limiteUtc;
        public List<Ev> Eventos { get; } = [];
        /// <summary>
        /// Dia mais recente em que alguma fonte bateu o limite de linhas: dele para trás os dados
        /// podem estar incompletos, então a página só fecha dias mais recentes que ele.
        /// </summary>
        public DateOnly? Corte { get; private set; }

        public void MarcarCorte(DateOnly dia)
        {
            if (Corte is null || dia > Corte) Corte = dia;
        }
    }

    // ── Datas ───────────────────────────────────────────────────────────────

    public static DateOnly DiaBrasilia(DateTime utc) => DateOnly.FromDateTime(BrasiliaTime.DeUtc(utc));

    public static DateTime InicioDoDiaUtc(DateOnly dia) =>
        TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(dia.ToDateTime(TimeOnly.MinValue), DateTimeKind.Unspecified), BrasiliaTime.Tz);

    private static DateTime Utc(DateTime d) => DateTime.SpecifyKind(d, DateTimeKind.Utc);

    // ── API pública ─────────────────────────────────────────────────────────

    public async Task<TimelinePaginaDto?> ObterAsync(
        Guid tenantId, DateOnly? antes, IReadOnlyCollection<string>? categorias, CancellationToken ct,
        bool incluirAcessosSuperAdmin = false)
    {
        var tenant = await db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Id == tenantId, ct);
        if (tenant is null) return null;

        var c = new Coleta(tenantId, antes is null ? DateTime.MaxValue : InicioDoDiaUtc(antes.Value));
        bool Quer(string cat) => categorias is null || categorias.Count == 0 || categorias.Contains(cat);

        if (Quer("conta")) await ContaAsync(c, tenant, ct);
        if (Quer("assinatura")) await AssinaturaAsync(c, ct);
        if (Quer("superadmin"))
            await AuditoriaAsync(c, "superadmin", incluirAcessosSuperAdmin ? AcoesSuperAdminComAcessos : AcoesSuperAdmin, ct);
        if (Quer("configuracoes")) await ConfiguracoesAsync(c, ct);
        if (Quer("oab")) await OabAsync(c, ct);
        if (Quer("processos")) await ProcessosAsync(c, ct);
        if (Quer("tarefas")) await TarefasAsync(c, ct);
        if (Quer("agenda")) await AgendaAsync(c, ct);
        if (Quer("contatos")) await ContatosAsync(c, ct);
        if (Quer("documentos")) await DocumentosAsync(c, ct);
        if (Quer("financeiro")) await FinanceiroAsync(c, ct);
        if (Quer("calculadoras")) await CalculadorasAsync(c, ct);
        if (Quer("ia")) await IaAsync(c, ct);
        if (Quer("integracoes")) await IntegracoesAsync(c, ct);

        // Fecha a página em dias completos: do mais recente para trás até juntar EventosPorPagina,
        // sem entrar em dias possivelmente incompletos (Corte). Se até o primeiro dia já está no
        // corte (um dia com volume acima dos limites), ele entra mesmo assim, parcial.
        var porDia = c.Eventos.GroupBy(e => DiaBrasilia(e.Data)).OrderByDescending(g => g.Key).ToList();
        var pagina = new List<Ev>();
        DateOnly? ultimoDia = null;
        foreach (var dia in porDia)
        {
            var seguro = c.Corte is null || dia.Key > c.Corte;
            if (pagina.Count > 0 && (!seguro || pagina.Count >= EventosPorPagina)) break;
            pagina.AddRange(dia);
            ultimoDia = dia.Key;
        }
        var temMais = c.Corte is not null || porDia.Any(d => ultimoDia is null || d.Key < ultimoDia);

        var eventos = await MontarDtosAsync(pagina.OrderByDescending(e => e.Data).ToList(), ct);
        return new TimelinePaginaDto(eventos, temMais ? ultimoDia : null, temMais && ultimoDia is not null);
    }

    public async Task<RecalculoPrazoDto?> RecalcularPrazoAsync(Guid tenantId, Guid calculoId, CancellationToken ct)
    {
        var calc = await db.CalculosPrazo.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == calculoId && x.TenantId == tenantId, ct);
        if (calc?.DataInicio is null || calc.QuantidadeDias is null || calc.TipoCalculo is null) return null;

        var extras = LerFeriados(calc.FeriadosAdicionaisJson);
        var (dataFinal, feriados) = FeriadosService.CalcularPrazo(
            calc.DataInicio.Value, calc.QuantidadeDias.Value, calc.TipoCalculo.Value, extras, calc.PrazoProcessual == true);

        return new RecalculoPrazoDto(
            calc.Id, Utc(calc.CriadoEm), calc.DataInicio.Value, calc.QuantidadeDias.Value, calc.TipoCalculo.Value.ToString(),
            extras.Select(f => f.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)).ToList(),
            calc.DataFinal, dataFinal,
            calc.DataFinal is not null && calc.DataFinal.Value.Date == dataFinal.Date,
            feriados);
    }

    private static List<DateOnly> LerFeriados(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try
        {
            return (JsonSerializer.Deserialize<List<string>>(json) ?? [])
                .Select(s => DateOnly.TryParseExact(s, "yyyy-MM-dd", out var d) ? d : (DateOnly?)null)
                .Where(d => d is not null).Select(d => d!.Value).ToList();
        }
        catch (JsonException) { return []; }
    }

    // ── Helpers de coleta ───────────────────────────────────────────────────

    private static async Task<List<T>> BuscarAsync<T>(Coleta c, IQueryable<T> ordenadaDesc, int limite, Func<T, DateTime> data, CancellationToken ct)
    {
        var rows = await ordenadaDesc.Take(limite).ToListAsync(ct);
        if (rows.Count == limite) c.MarcarCorte(DiaBrasilia(data(rows[^1])));
        return rows;
    }

    /// <summary>Um evento por dia (Brasília) para fontes de alto volume.</summary>
    private static void AgruparPorDia<T>(
        Coleta c, IEnumerable<T> rows, Func<T, DateTime> data, string categoria, string tipo,
        Func<List<T>, (string Titulo, string? Descricao, Dictionary<string, object?>? Detalhes)> resumo,
        Func<T, Sub> subitem, string? idSufixo = null)
    {
        foreach (var dia in rows.GroupBy(r => DiaBrasilia(data(r))))
        {
            var lista = dia.OrderByDescending(data).ToList();
            var (titulo, descricao, detalhes) = resumo(lista);
            var usuarios = lista.Select(r => subitem(r).UsuarioId).Where(u => u is not null).Distinct().ToList();
            c.Eventos.Add(new Ev
            {
                Id = $"{tipo}:{dia.Key:yyyy-MM-dd}{(idSufixo is null ? "" : ":" + idSufixo)}",
                Data = Utc(data(lista[0])),
                Categoria = categoria,
                Tipo = tipo,
                Titulo = titulo,
                Descricao = descricao,
                UsuarioId = usuarios.Count == 1 ? usuarios[0] : null,
                Quantidade = lista.Count,
                Detalhes = detalhes,
                Itens = lista.Take(MaxSubitens).Select(subitem).ToList()
            });
        }
    }

    private static string Plural(int n, string singular, string plural) => n == 1 ? $"1 {singular}" : $"{n} {plural}";

    private static string? Cortar(string? texto, int max = MaxTexto) =>
        texto is null ? null : texto.Length <= max ? texto : texto[..max] + "…";

    private static object? Json(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { using var doc = JsonDocument.Parse(json); return doc.RootElement.Clone(); }
        catch (JsonException) { return json; }
    }

    private static string? LerCampo(string? json, string campo)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty(campo, out var v)
                ? v.ValueKind == JsonValueKind.String ? v.GetString() : v.GetRawText()
                : null;
        }
        catch (JsonException) { return null; }
    }

    // ── Fontes ──────────────────────────────────────────────────────────────

    private async Task ContaAsync(Coleta c, Tenant tenant, CancellationToken ct)
    {
        // O primeiro usuário é quem cadastrou o escritório: os dados dele (origem, UTM) vão no
        // evento de cadastro em vez de um "usuário criado" separado.
        var fundador = await db.Users.AsNoTracking()
            .Where(u => u.TenantId == c.TenantId)
            .OrderBy(u => u.CriadoEm)
            .Select(u => new { u.Id, u.Nome, u.Email, u.OrigemCadastro, u.UtmSource, u.UtmMedium, u.UtmCampaign, u.Referrer, u.LandingPage })
            .FirstOrDefaultAsync(ct);

        if (tenant.CriadoEm < c.LimiteUtc)
        {
            c.Eventos.Add(new Ev
            {
                Id = $"cadastro:{tenant.Id}",
                Data = Utc(tenant.CriadoEm),
                Categoria = "conta",
                Tipo = "cadastro",
                Titulo = "Escritório cadastrado",
                Descricao = fundador is null ? null : $"{fundador.Nome} ({fundador.Email})",
                UsuarioId = fundador?.Id,
                Detalhes = new()
                {
                    ["Origem do cadastro"] = fundador?.OrigemCadastro,
                    ["Código promocional"] = tenant.VoucherUtilizado,
                    ["UTM source"] = fundador?.UtmSource,
                    ["UTM medium"] = fundador?.UtmMedium,
                    ["UTM campaign"] = fundador?.UtmCampaign,
                    ["Referrer"] = fundador?.Referrer,
                    ["Landing page"] = fundador?.LandingPage,
                }
            });
        }

        var usuarios = await BuscarAsync(c, db.Users.AsNoTracking()
            .Where(u => u.TenantId == c.TenantId && u.CriadoEm < c.LimiteUtc)
            .OrderByDescending(u => u.CriadoEm)
            .Select(u => new { u.Id, u.Nome, u.Email, u.Perfil, u.CriadoEm, u.OrigemCadastro }),
            LimiteIndividual, u => u.CriadoEm, ct);
        foreach (var u in usuarios.Where(u => u.Id != fundador?.Id))
        {
            c.Eventos.Add(new Ev
            {
                Id = $"usuario:{u.Id}",
                Data = Utc(u.CriadoEm),
                Categoria = "conta",
                Tipo = "usuario-criado",
                Titulo = u.Perfil == PerfilUsuario.Cliente ? $"Acesso de cliente criado: {u.Nome}" : $"Usuário criado: {u.Nome}",
                Descricao = $"{u.Email} · {u.Perfil}",
                UsuarioId = u.Id,
                Detalhes = u.OrigemCadastro is null ? null : new() { ["Origem"] = u.OrigemCadastro }
            });
        }

        var convites = await BuscarAsync(c, db.ConvitesUsuario.AsNoTracking()
            .Where(x => x.TenantId == c.TenantId && x.CriadoEm < c.LimiteUtc)
            .OrderByDescending(x => x.CriadoEm)
            .Select(x => new { x.Id, x.Email, x.Perfil, x.Usado, x.CriadoEm }),
            LimiteIndividual, x => x.CriadoEm, ct);
        foreach (var x in convites)
        {
            c.Eventos.Add(new Ev
            {
                Id = $"convite:{x.Id}",
                Data = Utc(x.CriadoEm),
                Categoria = "conta",
                Tipo = "convite",
                Titulo = $"Convite enviado para {x.Email}",
                Descricao = $"{x.Perfil} · {(x.Usado ? "aceito" : "não aceito")}",
            });
        }

        // Login não grava UsuarioId (só o e-mail digitado em DadosNovos).
        var logins = await BuscarAsync(c, db.AuditLogs.AsNoTracking()
            .Where(a => a.TenantId == c.TenantId && a.Acao == AuditActions.Login && a.CriadoEm < c.LimiteUtc)
            .OrderByDescending(a => a.CriadoEm)
            .Select(a => new { a.CriadoEm, a.DadosNovos, a.IpAddress }),
            LimiteAgrupado, a => a.CriadoEm, ct);
        AgruparPorDia(c, logins, a => a.CriadoEm, "conta", "login",
            l =>
            {
                var emails = l.Select(a => LerCampo(a.DadosNovos, "Email")).Where(e => e is not null).Distinct().ToList();
                return (Plural(l.Count, "login", "logins"),
                    emails.Count == 0 ? null : string.Join(", ", emails.Take(5)) + (emails.Count > 5 ? $" e mais {emails.Count - 5}" : ""),
                    new() { ["Usuários distintos"] = emails.Count });
            },
            a => new Sub(Utc(a.CriadoEm), LerCampo(a.DadosNovos, "Email") ?? "login",
                LerCampo(a.DadosNovos, "Provider") is { } p ? $"via {p}" : null, null,
                a.IpAddress is null ? null : new() { ["IP"] = a.IpAddress }));
    }

    private async Task AuditoriaAsync(Coleta c, string categoria, string[] acoes, CancellationToken ct)
    {
        var logs = await BuscarAsync(c, db.AuditLogs.AsNoTracking()
            .Where(a => a.TenantId == c.TenantId && acoes.Contains(a.Acao) && a.CriadoEm < c.LimiteUtc)
            .OrderByDescending(a => a.CriadoEm)
            .Select(a => new { a.Id, a.Acao, a.Entidade, a.UsuarioId, a.CriadoEm, a.DadosAnteriores, a.DadosNovos, a.ImpersonadoPorId }),
            LimiteIndividual, a => a.CriadoEm, ct);

        foreach (var a in logs)
        {
            var antes = Json(a.DadosAnteriores);
            var depois = Json(a.DadosNovos);
            string Novo(string campo) => LerCampo(a.DadosNovos, campo) ?? "?";
            string Velho(string campo) => LerCampo(a.DadosAnteriores, campo) ?? "?";

            var (titulo, descricao) = a.Acao switch
            {
                TimelineAcoes.AssinaturaIniciada => ($"Assinatura iniciada: {Novo("Plano")}", $"{Novo("Periodo")} · R$ {Novo("Valor")}"),
                TimelineAcoes.AssinaturaAlterada => ($"Assinatura alterada: {Velho("Plano")} → {Novo("Plano")}", $"Cobrado agora: R$ {Novo("ValorCobrado")}"),
                TimelineAcoes.AssinaturaCancelada => ($"Assinatura cancelada pelo cliente ({Velho("Plano")})", $"Acesso até {Novo("PlanoExpiraEm")}"),
                TimelineAcoes.AssinaturaEncerrada => ("Assinatura encerrada no Stripe", LerCampo(a.DadosNovos, "Motivo")),
                TimelineAcoes.PlanoExpirado => (Velho("Motivo") == "trial" ? $"Trial expirou: {Velho("Plano")} → Free" : $"Plano expirou: {Velho("Plano")} → Free", null),
                TimelineAcoes.PlanoAlterado => ($"Plano alterado pelo superadmin: {Velho("Plano")} → {Novo("Plano")}", null),
                TimelineAcoes.StatusAlterado => ($"Status alterado pelo superadmin: {Velho("Status")} → {Novo("Status")}", null),
                TimelineAcoes.TrialPlusConcedido => ($"Trial Plus de {Novo("Dias")} dias concedido", LerCampo(a.DadosNovos, "Motivo")),
                "EXPORT" => ("Dados exportados pelo superadmin", null),
                "IMPORT" => ("Dados importados pelo superadmin", null),
                AuditActions.ImpersonationStart => ($"Superadmin entrou como {Novo("UsuarioAlvo")}", null),
                TimelineAcoes.TemaAlterado => ("Tema personalizado salvo", null),
                TimelineAcoes.LogoAlterado => ("Logo enviado", null),
                TimelineAcoes.LogoRemovido => ("Logo removido", null),
                TimelineAcoes.FusoAlterado => ($"Fuso horário: {Velho("FusoHorario")} → {Novo("FusoHorario")}", null),
                _ => ($"{a.Acao} {a.Entidade}", null)
            };

            var detalhes = new Dictionary<string, object?>();
            if (antes is not null) detalhes["Antes"] = antes;
            if (depois is not null) detalhes["Depois"] = depois;
            if (a.ImpersonadoPorId is not null) detalhes["Feito em modo \"entrar como\""] = true;

            c.Eventos.Add(new Ev
            {
                Id = $"audit:{a.Id}",
                Data = Utc(a.CriadoEm),
                Categoria = categoria,
                Tipo = a.Acao,
                Titulo = titulo,
                Descricao = descricao,
                UsuarioId = a.UsuarioId,
                Detalhes = detalhes.Count == 0 ? null : detalhes
            });
        }
    }

    private async Task AssinaturaAsync(Coleta c, CancellationToken ct)
    {
        await AuditoriaAsync(c, "assinatura", AcoesAssinatura, ct);

        var faturas = await BuscarAsync(c, db.Faturamentos.AsNoTracking()
            .Where(f => f.TenantId == c.TenantId && f.DataCriacao < c.LimiteUtc)
            .OrderByDescending(f => f.DataCriacao)
            .Select(f => new { f.Id, f.DataCriacao, f.Valor, f.Periodo, f.Status, f.Descricao, f.DataPagamento }),
            LimiteIndividual, f => f.DataCriacao, ct);
        foreach (var f in faturas)
        {
            c.Eventos.Add(new Ev
            {
                Id = $"fatura:{f.Id}",
                Data = Utc(f.DataCriacao),
                Categoria = "assinatura",
                Tipo = "pagamento",
                Titulo = $"Pagamento de R$ {f.Valor:N2} ({f.Status})",
                Descricao = f.Descricao ?? f.Periodo,
                Detalhes = new() { ["Período"] = f.Periodo, ["Pago em"] = f.DataPagamento }
            });
        }
    }

    private async Task ConfiguracoesAsync(Coleta c, CancellationToken ct)
    {
        await AuditoriaAsync(c, "configuracoes", AcoesConfiguracao, ct);

        var honorarios = await BuscarAsync(c, db.AuditLogs.AsNoTracking()
            .Where(a => a.TenantId == c.TenantId && a.Entidade == AuditEntities.ConfiguracaoHonorario && a.CriadoEm < c.LimiteUtc)
            .OrderByDescending(a => a.CriadoEm)
            .Select(a => new { a.Id, a.UsuarioId, a.CriadoEm, a.DadosNovos }),
            LimiteIndividual, a => a.CriadoEm, ct);
        foreach (var a in honorarios)
        {
            c.Eventos.Add(new Ev
            {
                Id = $"audit:{a.Id}",
                Data = Utc(a.CriadoEm),
                Categoria = "configuracoes",
                Tipo = "config-honorarios",
                Titulo = "Configuração de honorários alterada",
                UsuarioId = a.UsuarioId,
                Detalhes = Json(a.DadosNovos) is { } d ? new() { ["Depois"] = d } : null
            });
        }
    }

    private async Task OabAsync(Coleta c, CancellationToken ct)
    {
        var oabs = await BuscarAsync(c, db.TenantOabs.AsNoTracking()
            .Where(o => o.TenantId == c.TenantId && o.CriadoEm < c.LimiteUtc)
            .OrderByDescending(o => o.CriadoEm)
            .Select(o => new { o.Id, o.Uf, o.Numero, o.Nome, o.Ativo, o.UserId, o.CriadoEm, o.SyncError, o.UltimoSyncEm }),
            LimiteIndividual, o => o.CriadoEm, ct);
        foreach (var o in oabs)
        {
            c.Eventos.Add(new Ev
            {
                Id = $"oab:{o.Id}",
                Data = Utc(o.CriadoEm),
                Categoria = "oab",
                Tipo = "oab-cadastrada",
                Titulo = $"OAB cadastrada: {o.Uf}/{o.Numero}",
                Descricao = o.Nome,
                UsuarioId = o.UserId,
                Detalhes = new()
                {
                    ["Ativa"] = o.Ativo,
                    ["Último sync"] = o.UltimoSyncEm,
                    ["Erro de sync"] = o.SyncError
                }
            });
        }

        var importacoes = await BuscarAsync(c, db.ImportacoesProcessos.AsNoTracking()
            .Where(i => i.TenantId == c.TenantId && i.CriadoEm < c.LimiteUtc)
            .OrderByDescending(i => i.CriadoEm)
            .Select(i => new
            {
                i.Id, i.UsuarioId, i.CriadoEm, i.ConcluidoEm, i.Modo, i.NumeroOab, i.Uf, i.Status,
                i.Total, i.Processados, i.Importados, i.JaCadastrados, i.Erros, i.MensagemErro
            }),
            LimiteIndividual, i => i.CriadoEm, ct);
        foreach (var i in importacoes)
        {
            c.Eventos.Add(new Ev
            {
                Id = $"importacao:{i.Id}",
                Data = Utc(i.CriadoEm),
                Categoria = "oab",
                Tipo = "importacao-oab",
                Titulo = $"Importação de processos pela OAB {i.Uf}/{i.NumeroOab}",
                Descricao = $"{i.Status} · {i.Importados} importados de {i.Total} encontrados",
                UsuarioId = i.UsuarioId,
                Detalhes = new()
                {
                    ["Modo"] = i.Modo == ModoImportacao.Todos ? "Todos os processos" : "Processos selecionados",
                    ["Encontrados"] = i.Total,
                    ["Processados"] = i.Processados,
                    ["Importados"] = i.Importados,
                    ["Já cadastrados"] = i.JaCadastrados,
                    ["Erros"] = i.Erros,
                    ["Duração"] = i.ConcluidoEm is null ? null : FormatarDuracao(i.ConcluidoEm.Value - i.CriadoEm),
                    ["Mensagem de erro"] = i.MensagemErro
                }
            });
        }
    }

    private static string FormatarDuracao(TimeSpan t) =>
        t.TotalHours >= 1 ? $"{(int)t.TotalHours}h{t.Minutes:00}min"
        : t.TotalMinutes >= 1 ? $"{(int)t.TotalMinutes}min{t.Seconds:00}s"
        : $"{Math.Max(0, (int)t.TotalSeconds)}s";

    private async Task ProcessosAsync(Coleta c, CancellationToken ct)
    {
        var processos = await BuscarAsync(c, db.Processos.AsNoTracking()
            .Where(p => p.TenantId == c.TenantId && p.CriadoEm < c.LimiteUtc)
            .OrderByDescending(p => p.CriadoEm)
            .Select(p => new { p.CriadoEm, p.NumeroCNJ, p.AreaDireito, p.Tribunal, p.Monitorado, p.AdvogadoResponsavelId }),
            LimiteAgrupado, p => p.CriadoEm, ct);
        AgruparPorDia(c, processos, p => p.CriadoEm, "processos", "processos-cadastrados",
            l => (Plural(l.Count, "processo cadastrado", "processos cadastrados"), null, new()
            {
                ["Monitorados hoje"] = l.Count(p => p.Monitorado),
                ["Por área"] = string.Join(", ", l.GroupBy(p => p.AreaDireito).OrderByDescending(g => g.Count()).Select(g => $"{g.Key} ({g.Count()})"))
            }),
            p => new Sub(Utc(p.CriadoEm), p.NumeroCNJ, $"{p.AreaDireito}{(p.Tribunal is null ? "" : " · " + p.Tribunal)}", p.AdvogadoResponsavelId));

        var monitoramento = await BuscarAsync(c, db.AuditLogs.AsNoTracking()
            .Where(a => a.TenantId == c.TenantId && a.Acao == TimelineAcoes.MonitoramentoAlternado && a.CriadoEm < c.LimiteUtc)
            .OrderByDescending(a => a.CriadoEm)
            .Select(a => new { a.CriadoEm, a.UsuarioId, a.EntidadeId, a.DadosNovos }),
            LimiteAgrupado, a => a.CriadoEm, ct);
        AgruparPorDia(c, monitoramento, a => a.CriadoEm, "processos", "monitoramento",
            l =>
            {
                var ligados = l.Count(a => LerCampo(a.DadosNovos, "Monitorado") == "true");
                return ("Monitoramento de processos alterado", $"{ligados} ligado(s) · {l.Count - ligados} desligado(s)", null);
            },
            a => new Sub(Utc(a.CriadoEm), LerCampo(a.DadosNovos, "Monitorado") == "true" ? "Monitoramento ligado" : "Monitoramento desligado",
                $"Processo {a.EntidadeId}", a.UsuarioId));
    }

    private async Task TarefasAsync(Coleta c, CancellationToken ct)
    {
        var criadas = await BuscarAsync(c, db.Tarefas.AsNoTracking()
            .Where(t => t.TenantId == c.TenantId && t.CriadoEm < c.LimiteUtc)
            .OrderByDescending(t => t.CriadoEm)
            .Select(t => new { t.CriadoEm, t.Titulo, t.Tipo, t.Prioridade, t.Status, t.CriadoPorId, t.Prazo }),
            LimiteAgrupado, t => t.CriadoEm, ct);
        AgruparPorDia(c, criadas, t => t.CriadoEm, "tarefas", "tarefas-criadas",
            l => (Plural(l.Count, "tarefa criada", "tarefas criadas"), null, new()
            {
                ["Por tipo"] = string.Join(", ", l.GroupBy(t => t.Tipo).Select(g => $"{g.Key} ({g.Count()})")),
                ["Por prioridade"] = string.Join(", ", l.GroupBy(t => t.Prioridade).Select(g => $"{g.Key} ({g.Count()})"))
            }),
            t => new Sub(Utc(t.CriadoEm), t.Titulo, $"{t.Tipo} · {t.Prioridade} · {t.Status}", t.CriadoPorId));

        var concluidas = await BuscarAsync(c, db.Tarefas.AsNoTracking()
            .Where(t => t.TenantId == c.TenantId && t.ConcluidaEm != null && t.ConcluidaEm < c.LimiteUtc)
            .OrderByDescending(t => t.ConcluidaEm)
            .Select(t => new { ConcluidaEm = t.ConcluidaEm!.Value, t.Titulo, t.ResponsavelId }),
            LimiteAgrupado, t => t.ConcluidaEm, ct);
        AgruparPorDia(c, concluidas, t => t.ConcluidaEm, "tarefas", "tarefas-concluidas",
            l => (Plural(l.Count, "tarefa concluída", "tarefas concluídas"), null, null),
            t => new Sub(Utc(t.ConcluidaEm), t.Titulo, null, t.ResponsavelId));

        var horas = await BuscarAsync(c, db.RegistrosTempo.AsNoTracking()
            .Where(r => r.TenantId == c.TenantId && r.CriadoEm < c.LimiteUtc)
            .OrderByDescending(r => r.CriadoEm)
            .Select(r => new { r.CriadoEm, r.UsuarioId, r.DuracaoMinutos, r.Descricao }),
            LimiteAgrupado, r => r.CriadoEm, ct);
        AgruparPorDia(c, horas, r => r.CriadoEm, "tarefas", "timesheet",
            l =>
            {
                var minutos = l.Sum(r => r.DuracaoMinutos ?? 0);
                return (Plural(l.Count, "registro de horas", "registros de horas"), $"{minutos / 60}h{minutos % 60:00}min no total", null);
            },
            r => new Sub(Utc(r.CriadoEm), Cortar(r.Descricao, 120) ?? "Registro de horas",
                r.DuracaoMinutos is null ? "em andamento" : $"{r.DuracaoMinutos} min", r.UsuarioId));
    }

    private async Task AgendaAsync(Coleta c, CancellationToken ct)
    {
        var eventos = await BuscarAsync(c, db.Eventos.AsNoTracking()
            .Where(e => e.TenantId == c.TenantId && e.CriadoEm < c.LimiteUtc)
            .OrderByDescending(e => e.CriadoEm)
            .Select(e => new { e.CriadoEm, e.Titulo, e.Tipo, e.DataHora, e.ResponsavelId }),
            LimiteAgrupado, e => e.CriadoEm, ct);
        AgruparPorDia(c, eventos, e => e.CriadoEm, "agenda", "eventos-criados",
            l => (Plural(l.Count, "evento criado na agenda", "eventos criados na agenda"), null, null),
            // DataHora é hora "de parede" (sem fuso): vai como texto para o front não converter.
            e => new Sub(Utc(e.CriadoEm), e.Titulo, $"{e.Tipo} · {e.DataHora.ToString("dd/MM/yyyy HH:mm", BrasiliaTime.PtBr)}", e.ResponsavelId));

        var atendimentos = await BuscarAsync(c, db.Atendimentos.AsNoTracking()
            .Where(a => a.TenantId == c.TenantId && a.CriadoEm < c.LimiteUtc)
            .OrderByDescending(a => a.CriadoEm)
            .Select(a => new { a.CriadoEm, a.Descricao, a.UsuarioId }),
            LimiteAgrupado, a => a.CriadoEm, ct);
        AgruparPorDia(c, atendimentos, a => a.CriadoEm, "agenda", "atendimentos",
            l => (Plural(l.Count, "atendimento registrado", "atendimentos registrados"), null, null),
            a => new Sub(Utc(a.CriadoEm), Cortar(a.Descricao, 160) ?? "Atendimento", null, a.UsuarioId));
    }

    private async Task ContatosAsync(Coleta c, CancellationToken ct)
    {
        var contatos = await BuscarAsync(c, db.Contatos.AsNoTracking()
            .Where(x => x.TenantId == c.TenantId && x.CriadoEm < c.LimiteUtc)
            .OrderByDescending(x => x.CriadoEm)
            .Select(x => new { x.CriadoEm, x.Nome, x.TipoContato, x.ImportadoAutomaticamente }),
            LimiteAgrupado, x => x.CriadoEm, ct);
        AgruparPorDia(c, contatos, x => x.CriadoEm, "contatos", "contatos-criados",
            l =>
            {
                var auto = l.Count(x => x.ImportadoAutomaticamente);
                return (Plural(l.Count, "contato cadastrado", "contatos cadastrados"),
                    auto > 0 ? $"{auto} importado(s) automaticamente" : null, null);
            },
            x => new Sub(Utc(x.CriadoEm), x.Nome, $"{x.TipoContato}{(x.ImportadoAutomaticamente ? " · importado" : "")}", null));
    }

    private async Task DocumentosAsync(Coleta c, CancellationToken ct)
    {
        var docs = await BuscarAsync(c, db.Documentos.AsNoTracking()
            .Where(d => d.TenantId == c.TenantId && d.CriadoEm < c.LimiteUtc)
            .OrderByDescending(d => d.CriadoEm)
            .Select(d => new { d.CriadoEm, d.Nome, d.Tipo, d.TamanhoBytes, d.UploadedPorId }),
            LimiteAgrupado, d => d.CriadoEm, ct);
        AgruparPorDia(c, docs, d => d.CriadoEm, "documentos", "documentos-enviados",
            l => (Plural(l.Count, "documento enviado", "documentos enviados"),
                $"{l.Sum(d => d.TamanhoBytes) / 1048576.0:N1} MB", null),
            d => new Sub(Utc(d.CriadoEm), d.Nome, $"{d.Tipo} · {d.TamanhoBytes / 1024.0:N0} KB", d.UploadedPorId));
    }

    private async Task FinanceiroAsync(Coleta c, CancellationToken ct)
    {
        var lancamentos = await BuscarAsync(c, db.LancamentosFinanceiros.AsNoTracking()
            .Where(l => l.TenantId == c.TenantId && l.CriadoEm < c.LimiteUtc)
            .OrderByDescending(l => l.CriadoEm)
            .Select(l => new { l.CriadoEm, l.Tipo, l.Valor, l.Categoria, l.Descricao, l.Status }),
            LimiteAgrupado, l => l.CriadoEm, ct);
        AgruparPorDia(c, lancamentos, l => l.CriadoEm, "financeiro", "lancamentos",
            l => (Plural(l.Count, "lançamento financeiro", "lançamentos financeiros"),
                $"Receitas R$ {l.Where(x => x.Tipo == TipoLancamento.Receita).Sum(x => x.Valor):N2} · Despesas R$ {l.Where(x => x.Tipo == TipoLancamento.Despesa).Sum(x => x.Valor):N2}",
                null),
            l => new Sub(Utc(l.CriadoEm), l.Descricao ?? l.Categoria, $"{l.Tipo} · R$ {l.Valor:N2} · {l.Status}", null));

        var contratos = await BuscarAsync(c, db.ContratosHonorarios.AsNoTracking()
            .Where(x => x.TenantId == c.TenantId && x.CriadoEm < c.LimiteUtc)
            .OrderByDescending(x => x.CriadoEm)
            .Select(x => new { x.Id, x.CriadoEm, x.NumeroContrato, x.ValorTotal, x.FormaPagamento, x.NumeroParcelas, x.Status, x.TipoCobranca, x.CriadoPorId }),
            LimiteIndividual, x => x.CriadoEm, ct);
        foreach (var x in contratos)
        {
            c.Eventos.Add(new Ev
            {
                Id = $"contrato:{x.Id}",
                Data = Utc(x.CriadoEm),
                Categoria = "financeiro",
                Tipo = "contrato-honorario",
                Titulo = $"Contrato de honorários {x.NumeroContrato}",
                Descricao = $"R$ {x.ValorTotal:N2} · {x.FormaPagamento}{(x.NumeroParcelas is null ? "" : $" em {x.NumeroParcelas}x")}",
                UsuarioId = x.CriadoPorId,
                Detalhes = new() { ["Status atual"] = x.Status.ToString(), ["Cobrança"] = x.TipoCobranca }
            });
        }
    }

    private async Task CalculadorasAsync(Coleta c, CancellationToken ct)
    {
        var honorarios = await BuscarAsync(c, db.HonorariosCalculos.AsNoTracking()
            .Where(h => h.TenantId == c.TenantId && h.CriadoEm < c.LimiteUtc)
            .OrderByDescending(h => h.CriadoEm),
            LimiteIndividual, h => h.CriadoEm, ct);
        foreach (var h in honorarios)
        {
            c.Eventos.Add(new Ev
            {
                Id = $"calc-honorarios:{h.Id}",
                Data = Utc(h.CriadoEm),
                Categoria = "calculadoras",
                Tipo = "calculadora-honorarios",
                Titulo = $"Calculadora de honorários: R$ {h.TotalCorrigido:N2}",
                Descricao = $"{h.Area} · {h.ItemOAB}",
                UsuarioId = h.UsuarioId,
                Detalhes = new()
                {
                    ["Modo"] = h.Mode,
                    ["Cliente"] = h.Cliente,
                    ["Área"] = h.Area,
                    ["Tipo"] = h.Tipo,
                    ["Item OAB"] = h.ItemOAB,
                    ["Valor da causa"] = h.ValorCausa,
                    ["Complexidade"] = h.Complexidade,
                    ["Risco"] = h.Risco,
                    ["Urgência"] = h.Urgencia,
                    ["Capacidade"] = h.Capacidade,
                    ["Êxito"] = h.Exito,
                    ["Forma de pagamento"] = h.FormaPagamento,
                    ["Multiplicador"] = h.Multiplicador,
                    ["Taxa de correção"] = h.TaxaCorrecao,
                    ["Honorário base"] = h.HonorarioBase,
                    ["Honorário ajustado"] = h.HonorarioAjustado,
                    ["Total corrigido"] = h.TotalCorrigido,
                    ["Estado completo da tela"] = Json(h.StateJson)
                }
            });
        }

        var prazos = await BuscarAsync(c, db.CalculosPrazo.AsNoTracking()
            .Where(p => p.TenantId == c.TenantId && p.CriadoEm < c.LimiteUtc)
            .OrderByDescending(p => p.CriadoEm),
            LimiteIndividual, p => p.CriadoEm, ct);
        foreach (var p in prazos)
        {
            var temParametros = p.DataInicio is not null && p.QuantidadeDias is not null && p.TipoCalculo is not null;
            var tipo = p.TipoCalculo == TipoCalculo.DiasCorridos ? "dias corridos" : "dias úteis";
            c.Eventos.Add(new Ev
            {
                Id = $"calc-prazo:{p.Id}",
                Data = Utc(p.CriadoEm),
                Categoria = "calculadoras",
                Tipo = "calculadora-prazos",
                Titulo = temParametros
                    ? $"Calculadora de prazos: {p.QuantidadeDias} {tipo} a partir de {p.DataInicio!.Value.ToString("dd/MM/yyyy", BrasiliaTime.PtBr)}"
                    : "Calculadora de prazos usada",
                Descricao = p.DataFinal is null ? (temParametros ? null : "Uso anterior ao registro dos parâmetros")
                    : $"Data final: {p.DataFinal.Value.ToString("dd/MM/yyyy", BrasiliaTime.PtBr)}",
                UsuarioId = p.UsuarioId,
                Detalhes = temParametros ? new()
                {
                    ["calculoPrazoId"] = p.Id,
                    ["Data inicial"] = p.DataInicio!.Value.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
                    ["Quantidade de dias"] = p.QuantidadeDias,
                    ["Tipo"] = tipo,
                    ["Feriados adicionais"] = LerFeriados(p.FeriadosAdicionaisJson) is { Count: > 0 } f
                        ? string.Join(", ", f.Select(d => d.ToString("dd/MM/yyyy", BrasiliaTime.PtBr))) : null,
                    ["Data final calculada"] = p.DataFinal?.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)
                } : null
            });
        }
    }

    private async Task IaAsync(Coleta c, CancellationToken ct)
    {
        var interacoes = await BuscarAsync(c, db.InteracoesAssistente.AsNoTracking()
            .Where(i => i.TenantId == c.TenantId && i.CriadoEm < c.LimiteUtc)
            .OrderByDescending(i => i.CriadoEm)
            .Select(i => new
            {
                i.CriadoEm, i.UsuarioId, i.ConversaId, i.Pergunta, i.Resposta, i.Sucesso, i.MensagemErro,
                i.Provedor, i.Modelo, i.TokensEntrada, i.TokensSaida, i.DuracaoMs, i.QuantidadeFerramentas, i.FerramentasJson
            }),
            LimiteAgrupado, i => i.CriadoEm, ct);
        AgruparPorDia(c, interacoes, i => i.CriadoEm, "ia", "assistente",
            l =>
            {
                var erros = l.Count(i => !i.Sucesso);
                return (Plural(l.Count, "pergunta ao assistente de IA", "perguntas ao assistente de IA"),
                    $"{l.Select(i => i.ConversaId).Distinct().Count()} conversa(s){(erros > 0 ? $" · {erros} com erro" : "")}",
                    new()
                    {
                        ["Tokens de entrada"] = l.Sum(i => i.TokensEntrada),
                        ["Tokens de saída"] = l.Sum(i => i.TokensSaida),
                        ["Modelos"] = string.Join(", ", l.Select(i => $"{i.Provedor}/{i.Modelo}").Distinct()),
                        ["Ferramentas usadas"] = l.Sum(i => i.QuantidadeFerramentas)
                    });
            },
            i => new Sub(Utc(i.CriadoEm), Cortar(i.Pergunta) ?? "", i.Sucesso ? Cortar(i.Resposta, 300) : $"Erro: {i.MensagemErro}", i.UsuarioId,
                new()
                {
                    ["Modelo"] = $"{i.Provedor}/{i.Modelo}",
                    ["Tokens"] = $"{i.TokensEntrada} entrada · {i.TokensSaida} saída",
                    ["Duração"] = $"{i.DuracaoMs} ms",
                    ["Ferramentas"] = i.QuantidadeFerramentas > 0 ? Json(i.FerramentasJson) : null
                }));

        var acoes = await BuscarAsync(c, db.AcoesAssistente.AsNoTracking()
            .Where(a => a.TenantId == c.TenantId && a.CriadoEm < c.LimiteUtc)
            .OrderByDescending(a => a.CriadoEm)
            .Select(a => new { a.CriadoEm, a.UsuarioId, a.Tipo, a.Titulo, a.Status }),
            LimiteAgrupado, a => a.CriadoEm, ct);
        AgruparPorDia(c, acoes, a => a.CriadoEm, "ia", "assistente-acoes",
            l => (Plural(l.Count, "ação proposta pelo assistente", "ações propostas pelo assistente"),
                string.Join(" · ", l.GroupBy(a => a.Status).Select(g => $"{g.Count()} {g.Key.ToString().ToLowerInvariant()}")), null),
            a => new Sub(Utc(a.CriadoEm), a.Titulo, $"{a.Tipo} · {a.Status}", a.UsuarioId));

        var pecas = await BuscarAsync(c, db.PecasGeradas.AsNoTracking()
            .Where(p => p.TenantId == c.TenantId && p.CriadoEm < c.LimiteUtc)
            .OrderByDescending(p => p.CriadoEm)
            .Select(p => new { p.Id, p.CriadoEm, p.Tipo, p.DescricaoSolicitacao, p.GeradoPorId, p.ProcessoId }),
            LimiteIndividual, p => p.CriadoEm, ct);
        foreach (var p in pecas)
        {
            c.Eventos.Add(new Ev
            {
                Id = $"peca:{p.Id}",
                Data = Utc(p.CriadoEm),
                Categoria = "ia",
                Tipo = "peca-gerada",
                Titulo = $"Peça gerada por IA: {p.Tipo}",
                Descricao = Cortar(p.DescricaoSolicitacao, 300),
                UsuarioId = p.GeradoPorId,
                Detalhes = p.ProcessoId is null ? null : new() { ["Processo"] = p.ProcessoId }
            });
        }

        var resumos = await BuscarAsync(c, db.ResumosProcesso.AsNoTracking()
            .Where(r => r.TenantId == c.TenantId && r.GeradoEm < c.LimiteUtc)
            .OrderByDescending(r => r.GeradoEm)
            .Select(r => new { r.GeradoEm, r.GeradoPorId, NumeroCNJ = r.Processo.NumeroCNJ }),
            LimiteAgrupado, r => r.GeradoEm, ct);
        AgruparPorDia(c, resumos, r => r.GeradoEm, "ia", "resumos",
            l => (Plural(l.Count, "resumo de processo gerado por IA", "resumos de processo gerados por IA"), null, null),
            r => new Sub(Utc(r.GeradoEm), r.NumeroCNJ, null, r.GeradoPorId));

        var traducoes = await BuscarAsync(c, db.TraducoesAndamentos.AsNoTracking()
            .Where(t => t.TenantId == c.TenantId && t.CriadoEm < c.LimiteUtc)
            .OrderByDescending(t => t.CriadoEm)
            .Select(t => new { t.CriadoEm, t.SolicitadoPorId, t.EnviadoAoCliente, t.TextoOriginal }),
            LimiteAgrupado, t => t.CriadoEm, ct);
        AgruparPorDia(c, traducoes, t => t.CriadoEm, "ia", "traducoes",
            l => (Plural(l.Count, "andamento traduzido por IA", "andamentos traduzidos por IA"),
                $"{l.Count(t => t.EnviadoAoCliente)} enviado(s) ao cliente", null),
            t => new Sub(Utc(t.CriadoEm), Cortar(t.TextoOriginal, 160) ?? "", t.EnviadoAoCliente ? "enviado ao cliente" : null, t.SolicitadoPorId));
    }

    private async Task IntegracoesAsync(Coleta c, CancellationToken ct)
    {
        var consultas = await BuscarAsync(c, db.ConsultasExternas.AsNoTracking()
            .Where(x => x.TenantId == c.TenantId && x.CriadoEm < c.LimiteUtc)
            .OrderByDescending(x => x.CriadoEm)
            .Select(x => new { x.CriadoEm, x.UsuarioId, x.Api, x.TipoConsulta, x.Origem, x.ParametrosJson, x.Sucesso, x.MensagemErro, x.QuantidadeResultados, x.DuracaoMs }),
            LimiteAgrupado, x => x.CriadoEm, ct);
        // Um evento por dia e por API (Escavador, DataJud…).
        foreach (var api in consultas.GroupBy(x => x.Api))
        {
            AgruparPorDia(c, api, x => x.CriadoEm, "integracoes", "consultas-externas",
                l =>
                {
                    var erros = l.Count(x => !x.Sucesso);
                    return ($"{Plural(l.Count, "consulta", "consultas")} ao {api.Key}",
                        string.Join(", ", l.GroupBy(x => x.TipoConsulta).Select(g => $"{g.Key} ({g.Count()})")) + (erros > 0 ? $" · {erros} com erro" : ""),
                        null);
                },
                x => new Sub(Utc(x.CriadoEm), x.TipoConsulta, x.Sucesso ? $"{x.Origem} · {x.QuantidadeResultados ?? 0} resultado(s)" : $"{x.Origem} · Erro: {x.MensagemErro}",
                    x.UsuarioId, new() { ["Parâmetros"] = Json(x.ParametrosJson), ["Duração"] = x.DuracaoMs is null ? null : $"{x.DuracaoMs} ms" }),
                idSufixo: api.Key);
        }
    }

    // ── Saída ───────────────────────────────────────────────────────────────

    private async Task<List<TimelineEventoDto>> MontarDtosAsync(List<Ev> eventos, CancellationToken ct)
    {
        var ids = eventos.Select(e => e.UsuarioId)
            .Concat(eventos.SelectMany(e => e.Itens ?? []).Select(s => s.UsuarioId))
            .Where(id => id is not null).Select(id => id!.Value).Distinct().ToList();
        var nomes = ids.Count == 0 ? new Dictionary<Guid, string>()
            : await db.Users.AsNoTracking().Where(u => ids.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.Nome, ct);
        string? Nome(Guid? id) => id is not null && nomes.TryGetValue(id.Value, out var n) ? n : null;

        static Dictionary<string, object?>? SemVazios(Dictionary<string, object?>? d)
        {
            if (d is null) return null;
            var filtrado = d.Where(kv => kv.Value is not null && !(kv.Value is string s && string.IsNullOrWhiteSpace(s)))
                .ToDictionary(kv => kv.Key, kv => kv.Value);
            return filtrado.Count == 0 ? null : filtrado;
        }

        return eventos.Select(e => new TimelineEventoDto(
            e.Id, e.Data, e.Categoria, e.Tipo, e.Titulo, e.Descricao, e.UsuarioId, Nome(e.UsuarioId), e.Quantidade,
            SemVazios(e.Detalhes),
            e.Itens?.Select(s => new TimelineSubitemDto(s.Data, s.Titulo, s.Descricao, s.UsuarioId, Nome(s.UsuarioId), SemVazios(s.Detalhes))).ToList()
        )).ToList();
    }
}
