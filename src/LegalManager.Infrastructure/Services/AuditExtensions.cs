using LegalManager.Application.Interfaces;
using LegalManager.Domain.Interfaces;

namespace LegalManager.Infrastructure.Services;

public static class AuditExtensions
{
    public static AuditLogEntry CreateEntry(
        this ITenantContext tenantContext,
        string action,
        string entity,
        Guid? entityId = null,
        object? dadosAnteriores = null,
        object? dadosNovos = null,
        string? ipAddress = null)
    {
        return new AuditLogEntry(
            tenantContext.TenantId,
            tenantContext.UserId,
            action,
            entity,
            entityId?.ToString(),
            dadosAnteriores,
            dadosNovos,
            ipAddress
        );
    }

    public static string GetClientIpAddress(this Microsoft.AspNetCore.Http.HttpContext? context)
    {
        if (context == null) return null;

        var forwardedFor = context.Request.Headers["X-Forwarded-For"].FirstOrDefault();
        if (!string.IsNullOrEmpty(forwardedFor))
            return forwardedFor.Split(',')[0].Trim();

        return context.Connection.RemoteIpAddress?.ToString();
    }
}

public static class AuditActions
{
    public const string Create = "CREATE";
    public const string Update = "UPDATE";
    public const string Delete = "DELETE";
    public const string Login = "LOGIN";
    public const string Logout = "LOGOUT";
    public const string Access = "ACCESS";
}

public static class AuditEntities
{
    public const string Contato = "Contato";
    public const string Processo = "Processo";
    public const string Tarefa = "Tarefa";
    public const string Evento = "Evento";
    public const string Financeiro = "LancamentoFinanceiro";
    public const string Usuario = "Usuario";
    public const string Documento = "Documento";
    public const string Configuracao = "Configuracao";
}