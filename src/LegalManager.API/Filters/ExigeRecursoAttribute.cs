using LegalManager.Domain;
using LegalManager.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;

namespace LegalManager.API.Filters;

/// <summary>
/// Esconde as ações de um recurso em teste (<see cref="RecursosEmTeste"/>) dos escritórios que o
/// super admin não liberou: responde 404, como se a rota não existisse. Ações anônimas (webhooks)
/// passam direto — não têm escritório no login e já se protegem de outro jeito.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public sealed class ExigeRecursoAttribute(string recurso) : Attribute, IAsyncActionFilter
{
    public string Recurso { get; } = recurso;

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        if (context.ActionDescriptor.EndpointMetadata.OfType<IAllowAnonymous>().Any())
        {
            await next();
            return;
        }

        var db = context.HttpContext.RequestServices.GetRequiredService<AppDbContext>();
        var liberado = Guid.TryParse(context.HttpContext.User.FindFirst("tenantId")?.Value, out var tenantId)
            && await LiberadoAsync(db, tenantId, Recurso, context.HttpContext.RequestAborted);
        if (!liberado)
        {
            context.Result = new NotFoundObjectResult(new { message = "Recurso indisponível para este escritório." });
            return;
        }
        await next();
    }

    public static async Task<bool> LiberadoAsync(AppDbContext db, Guid tenantId, string recurso, CancellationToken ct) =>
        await db.Tenants.AsNoTracking().AnyAsync(t => t.Id == tenantId && t.RecursosLiberados.Contains(recurso), ct);
}
