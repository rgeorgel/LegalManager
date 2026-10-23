using System.Globalization;

namespace LegalManager.Infrastructure.Services;

/// <summary>
/// Soma o custo informado pelas respostas HTTP de uma consulta externa. O
/// <see cref="ConsultaExternaLogService"/> abre a medição em volta da chamada e o
/// <see cref="CreditosUtilizadosHandler"/> soma o header de cada resposta — uma consulta pode
/// fazer várias requisições (paginação), por isso é soma e não último valor.
/// </summary>
public static class CreditosConsultaExterna
{
    private static readonly AsyncLocal<Medicao?> Atual = new();

    public sealed class Medicao : IDisposable
    {
        private readonly Medicao? _anterior;
        private int _total;

        internal Medicao(Medicao? anterior) => _anterior = anterior;

        /// <summary>Centavos somados; null se nenhuma resposta trouxe o header.</summary>
        public int? Total { get; private set; }

        internal void Somar(int centavos)
        {
            _total += centavos;
            Total = _total;
        }

        public void Dispose() => Atual.Value = _anterior;
    }

    public static Medicao Medir()
    {
        var medicao = new Medicao(Atual.Value);
        Atual.Value = medicao;
        return medicao;
    }

    internal static void Registrar(int centavos) => Atual.Value?.Somar(centavos);
}

/// <summary>Lê o header <c>Creditos-Utilizados</c> (centavos) das respostas do Escavador.</summary>
public sealed class CreditosUtilizadosHandler : DelegatingHandler
{
    public const string Header = "Creditos-Utilizados";

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var resp = await base.SendAsync(request, ct);
        if (resp.Headers.TryGetValues(Header, out var valores)
            && int.TryParse(valores.FirstOrDefault(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var centavos))
        {
            CreditosConsultaExterna.Registrar(centavos);
        }
        return resp;
    }
}
