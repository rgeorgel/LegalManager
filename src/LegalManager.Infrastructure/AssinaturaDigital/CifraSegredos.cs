using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace LegalManager.Infrastructure.AssinaturaDigital;

/// <summary>
/// Cifra segredos de integrações (chave de API, segredo de webhook) com AES-256-GCM. A chave vem de
/// <c>AssinaturaDigital:ChaveCifragem</c> (32 bytes em base64). Em Development, sem chave configurada,
/// usa uma chave fixa de desenvolvimento; em outros ambientes a integração fica indisponível até configurar.
/// Formato: "v1:" + base64(nonce[12] | tag[16] | cifrado).
/// </summary>
public sealed class CifraSegredos
{
    public const string ChaveConfig = "AssinaturaDigital:ChaveCifragem";
    private const string Prefixo = "v1:";
    private const int TamNonce = 12, TamTag = 16;
    private readonly byte[]? _chave;

    public CifraSegredos(IConfiguration config, IHostEnvironment env)
        : this(Resolver(config[ChaveConfig], env.IsDevelopment())) { }

    public CifraSegredos(byte[]? chave) => _chave = chave;

    public bool Disponivel => _chave != null;

    private static byte[]? Resolver(string? base64, bool desenvolvimento)
    {
        if (!string.IsNullOrWhiteSpace(base64))
        {
            byte[] chave;
            try { chave = Convert.FromBase64String(base64.Trim()); }
            catch (FormatException) { throw new InvalidOperationException($"{ChaveConfig} não é base64 válido."); }
            if (chave.Length != 32) throw new InvalidOperationException($"{ChaveConfig} precisa ter 32 bytes (base64 de 44 caracteres).");
            return chave;
        }
        return desenvolvimento ? SHA256.HashData("causify-dev-somente-local"u8) : null;
    }

    public string Cifrar(string texto)
    {
        var chave = _chave ?? throw new InvalidOperationException($"Configure {ChaveConfig} para usar integrações.");
        var claro = Encoding.UTF8.GetBytes(texto);
        var saida = new byte[TamNonce + TamTag + claro.Length];
        var nonce = saida.AsSpan(0, TamNonce);
        RandomNumberGenerator.Fill(nonce);
        using var aes = new AesGcm(chave, TamTag);
        aes.Encrypt(nonce, claro, saida.AsSpan(TamNonce + TamTag), saida.AsSpan(TamNonce, TamTag));
        return Prefixo + Convert.ToBase64String(saida);
    }

    public string Decifrar(string cifrado)
    {
        var chave = _chave ?? throw new InvalidOperationException($"Configure {ChaveConfig} para usar integrações.");
        if (!cifrado.StartsWith(Prefixo, StringComparison.Ordinal))
            throw new CryptographicException("Formato de segredo desconhecido.");
        var dados = Convert.FromBase64String(cifrado[Prefixo.Length..]);
        if (dados.Length < TamNonce + TamTag) throw new CryptographicException("Segredo truncado.");
        var claro = new byte[dados.Length - TamNonce - TamTag];
        using var aes = new AesGcm(chave, TamTag);
        aes.Decrypt(dados.AsSpan(0, TamNonce), dados.AsSpan(TamNonce + TamTag), dados.AsSpan(TamNonce, TamTag), claro);
        return Encoding.UTF8.GetString(claro);
    }
}
