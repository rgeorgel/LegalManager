using Hangfire.Dashboard;

namespace LegalManager.Infrastructure.Jobs;

public class HangfireAuthorizationFilter : IDashboardAuthorizationFilter
{
    public bool Authorize(DashboardContext context)
    {
        return true;
    }
}