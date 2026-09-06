using System.Diagnostics;
using System.Security.Claims;
using LegalManager.Infrastructure.Observability;
using Serilog.Context;

namespace LegalManager.API.Middleware;

public class RequestEnrichmentMiddleware
{
    private readonly RequestDelegate _next;

    public RequestEnrichmentMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var requestId = context.TraceIdentifier;
        var correlationId = context.Request.Headers["X-Correlation-Id"].FirstOrDefault()
            ?? context.Request.Headers["X-Request-Id"].FirstOrDefault()
            ?? requestId;

        context.Response.Headers["X-Correlation-Id"] = correlationId;

        var tenantId = context.User?.FindFirstValue("tenantId");
        var userId = context.User?.FindFirstValue(ClaimTypes.NameIdentifier);
        var userRole = context.User?.FindFirstValue(ClaimTypes.Role);
        var plano = context.User?.FindFirstValue("plano");
        var impersonadoPorId = context.User?.FindFirstValue("impersonadoPorId");

        var activity = Activity.Current;
        if (activity is not null)
        {
            if (!string.IsNullOrEmpty(tenantId)) activity.SetTag(Telemetry.Tags.TenantId, tenantId);
            if (!string.IsNullOrEmpty(userId)) activity.SetTag(Telemetry.Tags.UserId, userId);
            if (!string.IsNullOrEmpty(userRole)) activity.SetTag(Telemetry.Tags.UserRole, userRole);
            if (!string.IsNullOrEmpty(plano)) activity.SetTag(Telemetry.Tags.Plano, plano);
            if (!string.IsNullOrEmpty(impersonadoPorId)) activity.SetTag(Telemetry.Tags.ImpersonadoPorId, impersonadoPorId);
            activity.SetTag(Telemetry.Tags.RequestId, requestId);
            activity.SetTag(Telemetry.Tags.CorrelationId, correlationId);
        }

        using (LogContext.PushProperty("RequestId", requestId))
        using (LogContext.PushProperty("CorrelationId", correlationId))
        using (LogContext.PushProperty("TenantId", tenantId ?? string.Empty))
        using (LogContext.PushProperty("UserId", userId ?? string.Empty))
        using (LogContext.PushProperty("UserRole", userRole ?? string.Empty))
        using (LogContext.PushProperty("UserEmail", context.User?.FindFirstValue(ClaimTypes.Email) ?? string.Empty))
        using (LogContext.PushProperty("Plano", plano ?? string.Empty))
        using (LogContext.PushProperty("ClientIp", context.Connection.RemoteIpAddress?.ToString() ?? string.Empty))
        using (LogContext.PushProperty("UserAgent", context.Request.Headers.UserAgent.ToString()))
        {
            await _next(context);
        }
    }
}
