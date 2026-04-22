using System.Security.Claims;
using LegalManager.Domain.Enums;
using LegalManager.Domain.Interfaces;
using Microsoft.AspNetCore.Http;

namespace LegalManager.Infrastructure.Identity;

public class TenantContext : ITenantContext
{
    public Guid TenantId { get; }
    public Guid UserId { get; }
    public string UserRole { get; }
    public PlanoTipo Plano { get; }

    public TenantContext(IHttpContextAccessor httpContextAccessor)
    {
        var user = httpContextAccessor.HttpContext?.User;
        TenantId = Guid.Parse(user?.FindFirstValue("tenantId") ?? Guid.Empty.ToString());
        UserId = Guid.Parse(user?.FindFirstValue(ClaimTypes.NameIdentifier) ?? Guid.Empty.ToString());
        UserRole = user?.FindFirstValue(ClaimTypes.Role) ?? string.Empty;
        Plano = Enum.TryParse<PlanoTipo>(user?.FindFirstValue("plano"), out var plano) ? plano : PlanoTipo.Free;
    }
}
