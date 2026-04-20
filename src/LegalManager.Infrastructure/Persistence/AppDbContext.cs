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

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

        // Global query filters for multitenancy — applied in queries, not as model-level filters
        // to avoid EF warnings with required navigations (ContatoTag → Contato)
    }
}
