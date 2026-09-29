using System.Security.Claims;
using LegalManager.Domain.Enums;
using LegalManager.Domain.Interfaces;
using Microsoft.AspNetCore.Http;

namespace LegalManager.Infrastructure.Identity;

public class TenantContext : ITenantContext
{
    public Guid TenantId { get; private set; }
    public Guid UserId { get; private set; }
    public string UserRole { get; private set; }
    public PlanoTipo Plano { get; private set; }
    public Guid? ImpersonadoPorId { get; private set; }

    public TenantContext(IHttpContextAccessor httpContextAccessor)
    {
        var user = httpContextAccessor.HttpContext?.User;
        TenantId = Guid.Parse(user?.FindFirstValue("tenantId") ?? Guid.Empty.ToString());
        UserId = Guid.Parse(user?.FindFirstValue(ClaimTypes.NameIdentifier) ?? Guid.Empty.ToString());
        UserRole = user?.FindFirstValue(ClaimTypes.Role) ?? string.Empty;
        Plano = Enum.TryParse<PlanoTipo>(user?.FindFirstValue("plano"), out var plano) ? plano : PlanoTipo.Free;
        ImpersonadoPorId = Guid.TryParse(user?.FindFirstValue("impersonadoPorId"), out var impersonadoPorId) ? impersonadoPorId : null;
    }

    /// <summary>
    /// Assume o tenant/usuário fora de uma requisição HTTP (jobs do Hangfire), para que os
    /// serviços tenant-scoped resolvidos no mesmo escopo gravem no tenant certo.
    /// </summary>
    public void Assumir(Guid tenantId, Guid userId, PlanoTipo plano)
    {
        TenantId = tenantId;
        UserId = userId;
        Plano = plano;
        UserRole = string.Empty;
        ImpersonadoPorId = null;
    }
}
