using System.Text.RegularExpressions;

namespace LegalManager.Infrastructure.Services;

/// <summary>
/// UF de um processo pelo número CNJ (NNNNNNN-DD.AAAA.J.TR.OOOO), para escolher os feriados
/// estaduais/municipais no cálculo de prazos. Só a Justiça Estadual (J = 8) e os TRTs de um único
/// estado (J = 5) têm UF certa; TRFs, TRTs de mais de um estado e tribunais superiores dão null.
/// </summary>
public static partial class UfProcesso
{
    private static readonly string[] UfsTj =
    [
        "AC", "AL", "AP", "AM", "BA", "CE", "DF", "ES", "GO", "MA", "MT", "MS", "MG", "PA",
        "PB", "PR", "PE", "PI", "RJ", "RN", "RS", "RO", "RR", "SC", "SE", "SP", "TO"
    ];

    // TRT 8 (PA/AP), 10 (DF/TO), 11 (AM/RR) e 14 (RO/AC) abrangem mais de um estado.
    private static readonly Dictionary<int, string> UfsTrt = new()
    {
        [1] = "RJ", [2] = "SP", [3] = "MG", [4] = "RS", [5] = "BA", [6] = "PE", [7] = "CE",
        [9] = "PR", [12] = "SC", [13] = "PB", [15] = "SP", [16] = "MA", [17] = "ES", [18] = "GO",
        [19] = "AL", [20] = "SE", [21] = "RN", [22] = "PI", [23] = "MT", [24] = "MS"
    };

    public static string? DoCnj(string? numeroCnj)
    {
        if (string.IsNullOrWhiteSpace(numeroCnj)) return null;
        var digitos = NaoDigito().Replace(numeroCnj, "");
        if (digitos.Length != 20) return null;
        var tr = int.Parse(digitos[14..16]);
        return digitos[13] switch
        {
            '8' when tr >= 1 && tr <= UfsTj.Length => UfsTj[tr - 1],
            '5' => UfsTrt.GetValueOrDefault(tr),
            _ => null
        };
    }

    [GeneratedRegex(@"\D")]
    private static partial Regex NaoDigito();
}
