using System.Text.RegularExpressions;
using Serilog.Core;
using Serilog.Events;

namespace LegalManager.API.Configuration;

/// <summary>
/// Rota pública do feed iCal da agenda. O token vai no caminho (os apps de calendário não mandam
/// cabeçalho de autenticação), então os logs e o tracing gravam o caminho mascarado.
/// </summary>
public static partial class FeedAgendaApi
{
    public const string Prefixo = "/api/agenda/feed/";
    public const string PoliticaRateLimit = "feed-agenda";

    public static string Caminho(string token) => $"{Prefixo}{token}.ics";

    [GeneratedRegex(@"(/api/agenda/feed/)[^/]+\.ics", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex RotaComToken();

    /// <summary>"/api/agenda/feed/AbC…xyz.ics" → "/api/agenda/feed/***.ics"; outros caminhos voltam iguais.</summary>
    public static string MascararCaminho(string? caminho) =>
        string.IsNullOrEmpty(caminho) ? caminho ?? string.Empty : RotaComToken().Replace(caminho, "$1***.ics");

    /// <summary>
    /// Mascara o token em qualquer evento de log: "Request starting/finished" do ASP.NET e o
    /// escopo da requisição (RequestPath), que acompanha todo log emitido durante ela.
    /// </summary>
    public sealed class MascaraLog : ILogEventEnricher
    {
        private static readonly string[] Propriedades = ["RequestPath", "Path"];

        public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
        {
            foreach (var nome in Propriedades)
            {
                if (logEvent.Properties.TryGetValue(nome, out var valor)
                    && valor is ScalarValue { Value: string caminho }
                    && caminho.Contains(Prefixo, StringComparison.OrdinalIgnoreCase))
                    logEvent.AddOrUpdateProperty(new LogEventProperty(nome, new ScalarValue(MascararCaminho(caminho))));
            }
        }
    }
}
