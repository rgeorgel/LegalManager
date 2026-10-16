using LegalManager.Application.DTOs.SuperAdmin;
using LegalManager.Application.Interfaces;
using LegalManager.Domain;
using LegalManager.Domain.Entities;
using LegalManager.Domain.Enums;
using LegalManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace LegalManager.Infrastructure.Services;

/// <summary>
/// Indicadores do dashboard do superadmin. Janelas são móveis a partir de agora (UTC real —
/// CriadoEm/UltimoAcessoEm são carimbos do servidor); séries por dia/semana usam o dia de Brasília.
/// A receita consulta o Stripe uma vez por tenant pagante e fica em cache por alguns minutos.
/// </summary>
public class SuperAdminDashboardService(
    AppDbContext db,
    IStripeService stripe,
    IMemoryCache cache,
    ILogger<SuperAdminDashboardService> logger)
{
    private static readonly Guid SystemTenantId = TenantConstants.SystemTenantId;
    private const string ReceitaCacheKey = "superadmin-dashboard:receita";
    private static readonly TimeSpan ReceitaCacheDuracao = TimeSpan.FromMinutes(10);
    private const int SemanasSerie = 12;
    private const int DiasSerieLogins = 14;

    private record TenantLinha(
        Guid Id, string Nome, PlanoTipo Plano, StatusTenant Status, DateTime CriadoEm,
        DateTime? TrialExpiraEm, string? StripeSubscriptionId, string? PeriodoBilling,
        string? VoucherUtilizado, string? CodigoDesconto, DateTime? DescontoPromocionalUsadoEm,
        DateTime? TrialConcedidoEm)
    {
        public bool Pagante => Status == StatusTenant.Ativo && Plano != PlanoTipo.Free && StripeSubscriptionId != null;
        public string? Codigo => CodigoDesconto ?? VoucherUtilizado;
    }

    private record UsuarioLinha(Guid Id, Guid TenantId, PerfilUsuario Perfil, bool Ativo, DateTime? UltimoAcessoEm);

    public async Task<SuperAdminDashboardDto> ObterAsync(bool atualizarReceita = false, CancellationToken ct = default)
    {
        var agora = DateTime.UtcNow;

        var tenants = await db.Tenants.AsNoTracking()
            .Where(t => t.Id != SystemTenantId)
            .Select(t => new TenantLinha(
                t.Id, t.Nome, t.Plano, t.Status, t.CriadoEm, t.TrialExpiraEm, t.StripeSubscriptionId,
                t.PeriodoBilling, t.VoucherUtilizado, t.CodigoDesconto, t.DescontoPromocionalUsadoEm, t.TrialConcedidoEm))
            .ToListAsync(ct);

        var usuarios = await db.Users.AsNoTracking()
            .Where(u => u.TenantId != SystemTenantId && u.Perfil != PerfilUsuario.SuperAdmin)
            .Select(u => new UsuarioLinha(u.Id, u.TenantId, u.Perfil, u.Ativo, u.UltimoAcessoEm))
            .ToListAsync(ct);

        if (atualizarReceita) cache.Remove(ReceitaCacheKey);
        var receita = await cache.GetOrCreateAsync(ReceitaCacheKey, e =>
        {
            e.AbsoluteExpirationRelativeToNow = ReceitaCacheDuracao;
            return ReceitaAsync(tenants, agora, ct);
        });

        return new SuperAdminDashboardDto(
            agora,
            receita!,
            Cadastros(tenants, agora),
            await AcessosAsync(usuarios, agora, ct),
            await TenantsAsync(tenants, agora, ct),
            Trial(tenants, usuarios, agora),
            Cupons(tenants, usuarios),
            await PerguntasAsync(tenants, usuarios, agora, ct),
            await UsoAsync(agora, ct));
    }

    // ── Receita ──────────────────────────────────────────────────────────

    private async Task<ReceitaDashboardDto> ReceitaAsync(List<TenantLinha> tenants, DateTime agora, CancellationToken ct)
    {
        var pagantes = tenants.Where(t => t.Pagante).ToList();
        var codigos = await db.CodigosPromocionais.AsNoTracking()
            .Where(c => c.DescontoPercentual > 0)
            .ToDictionaryAsync(c => c.Codigo, ct);

        // Poucas chamadas em paralelo para não estourar o rate limit do Stripe.
        using var limite = new SemaphoreSlim(4);
        var cobrancas = await Task.WhenAll(pagantes.Select(async t =>
        {
            await limite.WaitAsync(ct);
            try
            {
                var proxima = await stripe.ObterProximaCobrancaAsync(t.StripeSubscriptionId!, ct);
                return (Tenant: t, Valor: proxima?.Valor ?? 0m, Data: proxima?.Data, Cancelando: proxima is null, Estimado: false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Prévia de fatura Stripe falhou para o tenant {TenantId}; usando preço de tabela", t.Id);
                return (Tenant: t, Valor: ValorEstimado(t, codigos, agora), Data: (DateTime?)null, Cancelando: false, Estimado: true);
            }
            finally
            {
                limite.Release();
            }
        }));

        decimal Mensal(TenantLinha t, decimal valor) => t.PeriodoBilling == "Anual" ? Math.Round(valor / 12m, 2) : valor;

        var limite30 = agora.AddDays(30);
        var porPlano = cobrancas
            .GroupBy(c => c.Tenant.Plano)
            .OrderBy(g => g.Key)
            .Select(g => new ReceitaPlanoDto(g.Key.ToString(), g.Count(), g.Sum(c => Mensal(c.Tenant, c.Valor))))
            .ToList();

        var desde30 = agora.AddDays(-30);
        var desde60 = agora.AddDays(-60);
        var recebidos = await db.Faturamentos.AsNoTracking()
            .Where(f => f.Status == StatusFaturamento.Pago && f.DataPagamento >= desde60 && f.TenantId != SystemTenantId)
            .Select(f => new { f.Valor, f.DataPagamento })
            .ToListAsync(ct);

        return new ReceitaDashboardDto(
            MrrReal: cobrancas.Sum(c => Mensal(c.Tenant, c.Valor)),
            ReceberProximos30Dias: cobrancas.Where(c => c.Data is { } d && d <= limite30).Sum(c => c.Valor),
            Pagantes: pagantes.Count,
            CancelamentoAgendado: cobrancas.Count(c => c.Cancelando),
            Estimados: cobrancas.Count(c => c.Estimado),
            RecebidoUltimos30Dias: recebidos.Where(f => f.DataPagamento >= desde30).Sum(f => f.Valor),
            RecebidoAnteriores30Dias: recebidos.Where(f => f.DataPagamento < desde30).Sum(f => f.Valor),
            MrrTabela: pagantes.Sum(t => PlanoPrecos.Mensal.GetValueOrDefault(t.Plano)),
            PorPlano: porPlano,
            ProximasCobrancas: cobrancas
                .Where(c => !c.Cancelando)
                .OrderBy(c => c.Data ?? DateTime.MaxValue)
                .Take(10)
                .Select(c => new ProximaCobrancaDto(c.Tenant.Id, c.Tenant.Nome, c.Tenant.Plano.ToString(),
                    c.Tenant.PeriodoBilling, c.Valor, c.Data, c.Estimado))
                .ToList(),
            CalculadoEm: agora);
    }

    /// <summary>Preço de tabela do ciclo, com o desconto do código promocional enquanto ele vale.</summary>
    private static decimal ValorEstimado(TenantLinha t, Dictionary<string, CodigoPromocional> codigos, DateTime agora)
    {
        var mensal = PlanoPrecos.Mensal.GetValueOrDefault(t.Plano);
        var valor = t.PeriodoBilling == "Anual" ? mensal * 12 : mensal;
        if (t.DescontoPromocionalUsadoEm is { } usadoEm
            && t.CodigoDesconto is { } codigo
            && codigos.TryGetValue(CodigoPromocional.Normalizar(codigo), out var c)
            && (c.DescontoMeses is null || usadoEm.AddMonths(c.DescontoMeses.Value) > agora))
            valor = Math.Round(valor * (100 - c.DescontoPercentual!.Value) / 100m, 2);
        return valor;
    }

    // ── Cadastros ────────────────────────────────────────────────────────

    private static CadastrosDashboardDto Cadastros(List<TenantLinha> tenants, DateTime agora)
    {
        var desde7 = agora.AddDays(-7);
        var desde14 = agora.AddDays(-14);

        // Semanas móveis terminando hoje (Brasília): a última é [hoje-6, hoje].
        var hoje = DateOnly.FromDateTime(BrasiliaTime.DeUtc(agora));
        var porDia = tenants
            .GroupBy(t => DateOnly.FromDateTime(BrasiliaTime.DeUtc(t.CriadoEm)))
            .ToDictionary(g => g.Key, g => g.Count());
        var semanas = Enumerable.Range(0, SemanasSerie).Reverse().Select(i =>
        {
            var inicio = hoje.AddDays(-7 * i - 6);
            var total = Enumerable.Range(0, 7).Sum(d => porDia.GetValueOrDefault(inicio.AddDays(d)));
            return new SerieSemanaDto(inicio, total);
        }).ToList();

        return new CadastrosDashboardDto(
            tenants.Count(t => t.CriadoEm >= desde7),
            tenants.Count(t => t.CriadoEm >= desde14 && t.CriadoEm < desde7),
            tenants.Count(t => t.CriadoEm >= agora.AddDays(-30)),
            semanas,
            tenants.OrderByDescending(t => t.CriadoEm).Take(8)
                .Select(t => new RecentTenantDto(t.Id, t.Nome, t.Plano.ToString(), t.Status.ToString(), t.CriadoEm))
                .ToList());
    }

    // ── Acessos ──────────────────────────────────────────────────────────

    private async Task<AcessosDashboardDto> AcessosAsync(List<UsuarioLinha> usuarios, DateTime agora, CancellationToken ct)
    {
        var ativos = usuarios.Where(u => u.Ativo).ToList();
        var hoje = DateOnly.FromDateTime(BrasiliaTime.DeUtc(agora));
        var primeiroDia = hoje.AddDays(-(DiasSerieLogins - 1));
        // Um dia de margem para cobrir o fuso; o corte por dia de Brasília é feito em memória.
        var desde = agora.AddDays(-(DiasSerieLogins + 1));

        var logins = await db.AuditLogs.AsNoTracking()
            .Where(a => a.Acao == AuditActions.Login && a.CriadoEm >= desde && a.TenantId != SystemTenantId)
            .Select(a => a.CriadoEm)
            .ToListAsync(ct);

        var porDia = logins
            .GroupBy(d => DateOnly.FromDateTime(BrasiliaTime.DeUtc(d)))
            .ToDictionary(g => g.Key, g => g.Count());

        return new AcessosDashboardDto(
            TotalUsuarios: ativos.Count,
            Acessaram24h: ativos.Count(u => u.UltimoAcessoEm >= agora.AddHours(-24)),
            Acessaram7Dias: ativos.Count(u => u.UltimoAcessoEm >= agora.AddDays(-7)),
            Acessaram30Dias: ativos.Count(u => u.UltimoAcessoEm >= agora.AddDays(-30)),
            NuncaAcessaram: ativos.Count(u => u.UltimoAcessoEm == null),
            Logins7Dias: logins.Count(d => d >= agora.AddDays(-7)),
            LoginsAnteriores7Dias: logins.Count(d => d >= agora.AddDays(-14) && d < agora.AddDays(-7)),
            LoginsPorDia: Enumerable.Range(0, DiasSerieLogins)
                .Select(i => primeiroDia.AddDays(i))
                .Select(d => new SerieDiaDto(d, porDia.GetValueOrDefault(d)))
                .ToList());
    }

    // ── Tenants ──────────────────────────────────────────────────────────

    private async Task<TenantsDashboardDto> TenantsAsync(List<TenantLinha> tenants, DateTime agora, CancellationToken ct)
    {
        var desde30 = agora.AddDays(-30);
        var cancelamentos = await db.AuditLogs.AsNoTracking()
            .CountAsync(a => a.Acao == TimelineAcoes.AssinaturaEncerrada && a.CriadoEm >= desde30, ct);
        var waitlist = await db.WaitlistEntries.CountAsync(w => w.Status == null || w.Status == "Pendente", ct);

        return new TenantsDashboardDto(
            tenants.Count,
            tenants.Count(t => t.Status == StatusTenant.Ativo),
            tenants.Count(t => t.Status == StatusTenant.Trial),
            tenants.Count(t => t.Status == StatusTenant.Suspenso),
            tenants.Count(t => t.Status == StatusTenant.Cancelado),
            tenants.GroupBy(t => t.Plano.ToString()).ToDictionary(g => g.Key, g => g.Count()),
            cancelamentos,
            waitlist);
    }

    // ── Trial ────────────────────────────────────────────────────────────

    private static TrialDashboardDto Trial(List<TenantLinha> tenants, List<UsuarioLinha> usuarios, DateTime agora)
    {
        var emTrial = tenants.Where(t => t.Status == StatusTenant.Trial).ToList();
        var ids = emTrial.Select(t => t.Id).ToHashSet();
        var usuariosPorTenant = usuarios.Where(u => u.Ativo).GroupBy(u => u.TenantId).ToDictionary(g => g.Key, g => g.Count());
        var limite = agora.AddDays(7);

        return new TrialDashboardDto(
            emTrial.Count,
            usuarios.Count(u => u.Ativo && ids.Contains(u.TenantId)),
            emTrial.Count(t => t.TrialConcedidoEm != null),
            emTrial
                .Where(t => t.TrialExpiraEm is { } e && e >= agora && e <= limite)
                .OrderBy(t => t.TrialExpiraEm)
                .Select(t => new TrialExpirandoDto(t.Id, t.Nome, t.Plano.ToString(), t.TrialExpiraEm!.Value,
                    usuariosPorTenant.GetValueOrDefault(t.Id)))
                .ToList());
    }

    // ── Cupons ───────────────────────────────────────────────────────────

    private static CuponsDashboardDto Cupons(List<TenantLinha> tenants, List<UsuarioLinha> usuarios)
    {
        var comCodigo = tenants.Where(t => !string.IsNullOrWhiteSpace(t.Codigo)).ToList();
        var ids = comCodigo.Select(t => t.Id).ToHashSet();

        return new CuponsDashboardDto(
            comCodigo.Count,
            usuarios.Count(u => u.Ativo && ids.Contains(u.TenantId)),
            tenants.Count(t => t.DescontoPromocionalUsadoEm != null),
            comCodigo.Count(t => t.Pagante),
            comCodigo
                .GroupBy(t => CodigoPromocional.Normalizar(t.Codigo!))
                .Select(g => new CodigoUsoDto(g.Key, g.Count(), g.Count(t => t.Pagante)))
                .OrderByDescending(c => c.Tenants).ThenBy(c => c.Codigo)
                .Take(8)
                .ToList());
    }

    // ── Perguntas ────────────────────────────────────────────────────────

    private async Task<PerguntasDashboardDto> PerguntasAsync(
        List<TenantLinha> tenants, List<UsuarioLinha> usuarios, DateTime agora, CancellationToken ct)
    {
        var desde7 = agora.AddDays(-7);
        var respostas7 = await db.RespostasPerguntas.CountAsync(r => r.RespondidoEm >= desde7, ct);
        var perguntas = await db.Perguntas.AsNoTracking().Where(p => p.Ativa).ToListAsync(ct);
        if (perguntas.Count == 0)
            return new PerguntasDashboardDto(0, 0, 0, respostas7, []);

        var ids = perguntas.Select(p => p.Id).ToList();
        var respostas = await db.RespostasPerguntas.AsNoTracking()
            .Where(r => ids.Contains(r.PerguntaId))
            .Select(r => new { r.PerguntaId, r.RespondenteTipo, r.RespondenteId })
            .ToListAsync(ct);
        var respondidas = respostas
            .Where(r => r.RespondenteTipo == TipoRespondente.Usuario)
            .Select(r => (r.PerguntaId, r.RespondenteId))
            .ToHashSet();

        var grupoIds = perguntas.Where(p => p.GrupoId != null).Select(p => p.GrupoId!.Value).Distinct().ToList();
        var membros = (await db.GruposPerguntaMembros.AsNoTracking()
                .Where(m => grupoIds.Contains(m.GrupoId))
                .Select(m => new { m.GrupoId, m.UsuarioId })
                .ToListAsync(ct))
            .Select(m => (m.GrupoId, m.UsuarioId))
            .ToHashSet();

        var clientesAtivos = perguntas.Any(p => p.Publico == PublicoPergunta.Clientes)
            ? await db.AcessosCliente.CountAsync(a => a.Ativo && a.TenantId != SystemTenantId, ct)
            : 0;

        // Mesma segmentação de PerguntasController.GetPendentes, avaliada para cada advogado/equipe.
        var tenantPorId = tenants.ToDictionary(t => t.Id);
        var equipe = usuarios.Where(u => u.Ativo && u.Perfil != PerfilUsuario.Cliente && tenantPorId.ContainsKey(u.TenantId)).ToList();
        bool Alcanca(Pergunta p, UsuarioLinha u)
        {
            var t = tenantPorId[u.TenantId];
            return p.Segmento switch
            {
                SegmentoPergunta.Todos => true,
                SegmentoPergunta.PlanoEspecifico => p.PlanoAlvo == t.Plano,
                SegmentoPergunta.Pagantes => t.Status == StatusTenant.Ativo && t.Plano != PlanoTipo.Free,
                SegmentoPergunta.EmTrial => t.Status == StatusTenant.Trial,
                SegmentoPergunta.GrupoEspecifico => p.GrupoId is { } g && membros.Contains((g, u.Id)),
                _ => false
            };
        }

        var paraEquipe = perguntas.Where(p => p.Publico == PublicoPergunta.Advogados).ToList();
        var noPublico = 0;
        var comPendentes = 0;
        foreach (var u in equipe)
        {
            var alcancadas = paraEquipe.Where(p => Alcanca(p, u)).ToList();
            if (alcancadas.Count == 0) continue;
            noPublico++;
            if (alcancadas.Any(p => !respondidas.Contains((p.Id, u.Id)))) comPendentes++;
        }

        var porPergunta = perguntas
            .OrderBy(p => p.Ordem).ThenBy(p => p.CriadoEm)
            .Select(p => new PerguntaAtivaResumoDto(
                p.Id, p.Texto, p.Publico.ToString(),
                p.Publico == PublicoPergunta.Clientes ? clientesAtivos : equipe.Count(u => Alcanca(p, u)),
                respostas.Count(r => r.PerguntaId == p.Id)))
            .ToList();

        return new PerguntasDashboardDto(perguntas.Count, noPublico, comPendentes, respostas7, porPergunta);
    }

    // ── Uso do produto ───────────────────────────────────────────────────

    private async Task<UsoDashboardDto> UsoAsync(DateTime agora, CancellationToken ct)
    {
        var desde7 = agora.AddDays(-7);
        var processos = await db.Processos.AsNoTracking()
            .Where(p => p.TenantId != SystemTenantId)
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Total = g.Count(),
                Novos = g.Count(p => p.CriadoEm >= desde7),
                Monitorados = g.Count(p => p.Monitorado)
            })
            .FirstOrDefaultAsync(ct);

        var ia = await db.InteracoesAssistente.AsNoTracking()
            .Where(i => i.CriadoEm >= desde7 && i.ImpersonadoPorId == null)
            .GroupBy(_ => 1)
            .Select(g => new { Total = g.Count(), Usuarios = g.Select(i => i.UsuarioId).Distinct().Count() })
            .FirstOrDefaultAsync(ct);

        var consultas = await db.ConsultasExternas.AsNoTracking()
            .Where(c => c.CriadoEm >= desde7)
            .GroupBy(_ => 1)
            .Select(g => new { Total = g.Count(), Falhas = g.Count(c => !c.Sucesso) })
            .FirstOrDefaultAsync(ct);

        return new UsoDashboardDto(
            processos?.Total ?? 0,
            processos?.Novos ?? 0,
            processos?.Monitorados ?? 0,
            ia?.Total ?? 0,
            ia?.Usuarios ?? 0,
            consultas?.Total ?? 0,
            consultas?.Falhas ?? 0);
    }
}
