using Hangfire.Dashboard;

namespace LegalManager.Infrastructure.Jobs;

public class HangfireAuthorizationFilter : IDashboardAuthorizationFilter
{
    public bool Authorize(DashboardContext context)
    {
        var user = context.GetHttpContext().User;
        return user.Identity?.IsAuthenticated == true && user.IsInRole("SuperAdmin");
    }
}