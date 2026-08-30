using System.Security.Cryptography;
using System.Text;
using Hangfire.Dashboard;
using Microsoft.AspNetCore.Http;

namespace LegalManager.Infrastructure.Jobs;

/// <summary>
/// Protege o dashboard do Hangfire com HTTP Basic Auth dedicado,
/// independente da autenticação JWT usada pelo restante da API
/// (o navegador não envia o header Authorization em uma navegação normal,
/// então o JWT não é utilizável aqui).
/// Credenciais configuradas em "Hangfire:DashboardUser" / "Hangfire:DashboardPassword".
/// </summary>
public class HangfireAuthorizationFilter : IDashboardAuthorizationFilter
{
    private readonly string _user;
    private readonly string _password;

    public HangfireAuthorizationFilter(string user, string password)
    {
        _user = user;
        _password = password;
    }

    public bool Authorize(DashboardContext context)
    {
        var httpContext = context.GetHttpContext();
        var header = httpContext.Request.Headers.Authorization.ToString();

        if (string.IsNullOrEmpty(header) || !header.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase))
        {
            Challenge(httpContext);
            return false;
        }

        string decoded;
        try
        {
            var encoded = header["Basic ".Length..].Trim();
            decoded = Encoding.UTF8.GetString(Convert.FromBase64String(encoded));
        }
        catch (FormatException)
        {
            Challenge(httpContext);
            return false;
        }

        var separatorIndex = decoded.IndexOf(':');
        if (separatorIndex < 0)
        {
            Challenge(httpContext);
            return false;
        }

        var user = decoded[..separatorIndex];
        var password = decoded[(separatorIndex + 1)..];

        var userOk = FixedTimeEquals(user, _user);
        var passwordOk = FixedTimeEquals(password, _password);

        if (!userOk || !passwordOk)
        {
            Challenge(httpContext);
            return false;
        }

        return true;
    }

    private static void Challenge(HttpContext httpContext)
    {
        httpContext.Response.Headers.WWWAuthenticate = "Basic realm=\"Hangfire Dashboard\"";
        httpContext.Response.StatusCode = StatusCodes.Status401Unauthorized;
    }

    private static bool FixedTimeEquals(string a, string b)
    {
        var bytesA = Encoding.UTF8.GetBytes(a);
        var bytesB = Encoding.UTF8.GetBytes(b);

        // CryptographicOperations.FixedTimeEquals exige buffers do mesmo tamanho;
        // usamos o maior dos dois para não vazar o tamanho da credencial correta.
        var length = Math.Max(bytesA.Length, bytesB.Length);
        Array.Resize(ref bytesA, length);
        Array.Resize(ref bytesB, length);

        return CryptographicOperations.FixedTimeEquals(bytesA, bytesB);
    }
}
