namespace LegalManager.API.Middleware;

/// <summary>Redirecionamentos de SEO que dependem só da requisição (o host canônico do site).</summary>
public static class RedirecionamentosCanonicos
{
    /// <summary>
    /// Endereço sem o "www." para um host que começa com ele (ex.: https://www.causify.com.br/blog/x.html
    /// → https://causify.com.br/blog/x.html), mantendo caminho e query. Null para os demais hosts.
    /// Sempre https: o site só é servido assim em produção.
    /// </summary>
    public static string? DestinoSemWww(HttpRequest req)
    {
        var host = req.Host;
        if (!host.HasValue || !host.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase)) return null;
        var semWww = host.Host[4..];
        if (semWww.Length == 0) return null;
        var porta = host.Port is { } p && p != 80 && p != 443 ? $":{p}" : "";
        return $"https://{semWww}{porta}{req.PathBase}{req.Path}{req.QueryString}";
    }
}
