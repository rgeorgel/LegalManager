using LegalManager.Domain.Entities;
using LegalManager.Domain.Interfaces;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace LegalManager.Infrastructure.Persistence;

public class AppDbContext : IdentityDbContext<Usuario, IdentityRole<Guid>, Guid>
{
    private readonly ITenantContext? _tenantContext;

    public AppDbContext(DbContextOptions<AppDbContext> options, ITenantContext? tenantContext = null)
        : base(options)
    {
        _tenantContext = tenantContext;
    }

    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<ConviteUsuario> ConvitesUsuario => Set<ConviteUsuario>();
    public DbSet<Contato> Contatos => Set<Contato>();
    public DbSet<ContatoTag> ContatoTags => Set<ContatoTag>();
    public DbSet<Atendimento> Atendimentos => Set<Atendimento>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<Processo> Processos => Set<Processo>();
    public DbSet<ProcessoParte> ProcessoPartes => Set<ProcessoParte>();
    public DbSet<Andamento> Andamentos => Set<Andamento>();
    public DbSet<Tarefa> Tarefas => Set<Tarefa>();
    public DbSet<TarefaTag> TarefaTags => Set<TarefaTag>();
    public DbSet<Evento> Eventos => Set<Evento>();
    public DbSet<Notificacao> Notificacoes => Set<Notificacao>();
    public DbSet<Publicacao> Publicacoes => Set<Publicacao>();
    public DbSet<NomeCaptura> NomesCaptura => Set<NomeCaptura>();
    public DbSet<Prazo> Prazos => Set<Prazo>();
    public DbSet<AcessoCliente> AcessosCliente => Set<AcessoCliente>();
    public DbSet<LancamentoFinanceiro> LancamentosFinanceiros => Set<LancamentoFinanceiro>();
    public DbSet<RegistroTempo> RegistrosTempo => Set<RegistroTempo>();
    public DbSet<PreferenciasNotificacao> PreferenciasNotificacoes => Set<PreferenciasNotificacao>();
    public DbSet<AreaAtuacao> AreasAtuacao => Set<AreaAtuacao>();
    public DbSet<CategoriaFinanceira> CategoriasFinanceiras => Set<CategoriaFinanceira>();
    public DbSet<Faturamento> Faturamentos => Set<Faturamento>();
    public DbSet<CreditoAI> CreditosAI => Set<CreditoAI>();
    public DbSet<TraducaoAndamento> TraducoesAndamentos => Set<TraducaoAndamento>();
    public DbSet<PecaGerada> PecasGeradas => Set<PecaGerada>();
    public DbSet<Documento> Documentos => Set<Documento>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

        // Global query filters for multitenancy — applied in queries, not as model-level filters
        // to avoid EF warnings with required navigations (ContatoTag → Contato)
    }
}
