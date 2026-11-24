using System.Numerics;

namespace LegalManager.Infrastructure.Jurisprudencia;

/// <summary>
/// Número único de processo (Resolução CNJ 65/2008): NNNNNNN-DD.AAAA.J.TR.OOOO. O dígito verificador
/// (DD) é calculado pelo módulo 97 (ISO 7064) sobre os demais dígitos — número com DD errado não existe.
/// </summary>
public static class Cnj
{
    private static readonly string[] UfPorCodigoTj =
    [
        "", "AC", "AL", "AP", "AM", "BA", "CE", "DF", "ES", "GO", "MA", "MT", "MS", "MG", "PA",
        "PB", "PR", "PE", "PI", "RJ", "RN", "RS", "RO", "RR", "SC", "SE", "SP", "TO",
    ];

    /// <summary>Só dígitos, 20 posições, com o dígito verificador correto.</summary>
    public static bool Valido(string? numero)
    {
        var d = ExtratorCitacoes.SoDigitos(numero ?? "");
        if (d.Length != 20) return false;
        var semDv = d[..7] + d[9..] + "00"; // NNNNNNN AAAA J TR OOOO 00
        var dv = 98 - (int)(BigInteger.Parse(semDv) % 97);
        return dv == int.Parse(d[7..9]);
    }

    /// <summary>"10045213320238260100" → "1004521-33.2023.8.26.0100".</summary>
    public static string Formatar(string numero)
    {
        var d = ExtratorCitacoes.SoDigitos(numero);
        return d.Length != 20 ? numero : $"{d[..7]}-{d[7..9]}.{d[9..13]}.{d[13]}.{d[14..16]}.{d[16..]}";
    }

    /// <summary>Sigla do tribunal pelo segmento (J) e código (TR): TJSP, TRF3, TRT2...; null se desconhecido.</summary>
    public static string? Tribunal(string numero)
    {
        var d = ExtratorCitacoes.SoDigitos(numero);
        if (d.Length != 20 || !int.TryParse(d[14..16], out var tr)) return null;
        return d[13] switch
        {
            '8' when tr >= 1 && tr < UfPorCodigoTj.Length => $"TJ{UfPorCodigoTj[tr]}",
            '4' when tr is >= 1 and <= 6 => $"TRF{tr}",
            '5' when tr is >= 1 and <= 24 => $"TRT{tr}",
            '5' when tr == 0 => "TST",
            _ => null,
        };
    }
}
