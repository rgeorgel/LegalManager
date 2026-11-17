using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace LegalManager.Infrastructure.AssinaturaDigital;

public sealed record ContratosEvento(string Id, string Type, DateTimeOffset? CreatedAt, ContratosEventoDados? Data);
public sealed record ContratosEventoDados(ContratosContrato? Contract, ContratosSignatario? Signer, string? Reason, string? Message);

/// <summary>Conferência e leitura dos webhooks do Contratos (cabeçalho X-Contratos-Signature: t=…,v1=…).</summary>
public static class WebhookContratos
{
    public const string CabecalhoAssinatura = "X-Contratos-Signature";
    public static readonly TimeSpan Tolerancia = TimeSpan.FromMinutes(5);

    /// <summary>HMAC-SHA256(segredo, t + "." + corpo cru) em hex minúsculo, comparado em tempo constante;
    /// t a mais de 5 minutos do relógio é recusado (replay).</summary>
    public static bool AssinaturaValida(string? cabecalho, ReadOnlySpan<byte> corpo, string segredo, DateTimeOffset agora)
    {
        if (string.IsNullOrWhiteSpace(cabecalho) || string.IsNullOrEmpty(segredo)) return false;
        string? t = null, v1 = null;
        foreach (var parte in cabecalho.Split(','))
        {
            var kv = parte.Split('=', 2);
            if (kv.Length != 2) continue;
            var chave = kv[0].Trim();
            if (chave == "t") t = kv[1].Trim();
            else if (chave == "v1") v1 = kv[1].Trim();
        }
        if (t == null || v1 == null || !long.TryParse(t, out var segundos)) return false;
        if ((agora - DateTimeOffset.FromUnixTimeSeconds(segundos)).Duration() > Tolerancia) return false;

        var esperado = Convert.ToHexStringLower(Calcular(segredo, t, corpo));
        return CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(esperado), Encoding.ASCII.GetBytes(v1));
    }

    /// <summary>Monta o cabeçalho como o Contratos manda — usado nos testes.</summary>
    public static string Assinar(ReadOnlySpan<byte> corpo, string segredo, DateTimeOffset quando)
    {
        var t = quando.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture);
        return $"t={t},v1={Convert.ToHexStringLower(Calcular(segredo, t, corpo))}";
    }

    private static byte[] Calcular(string segredo, string t, ReadOnlySpan<byte> corpo)
    {
        var prefixo = Encoding.UTF8.GetBytes(t + ".");
        var dados = new byte[prefixo.Length + corpo.Length];
        prefixo.CopyTo(dados, 0);
        corpo.CopyTo(dados.AsSpan(prefixo.Length));
        return HMACSHA256.HashData(Encoding.UTF8.GetBytes(segredo), dados);
    }

    public static ContratosEvento? Ler(ReadOnlySpan<byte> corpo)
    {
        try { return JsonSerializer.Deserialize<ContratosEvento>(corpo, ContratosHttpClient.Json); }
        catch (JsonException) { return null; }
    }
}
