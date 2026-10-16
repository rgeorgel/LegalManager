namespace LegalManager.API.Controllers;

/// <summary>Filtros de listagem que aceitam um valor ou vários (?status=A&amp;status=B).</summary>
internal static class FiltroEnums
{
    /// <summary>Valores inválidos e repetidos são ignorados; nenhum válido = null (sem filtro).</summary>
    public static List<T>? Parse<T>(string[]? valores) where T : struct, Enum
    {
        var lista = (valores ?? [])
            .Select(v => Enum.TryParse<T>(v, true, out var e) && Enum.IsDefined(e) ? (T?)e : null)
            .OfType<T>().Distinct().ToList();
        return lista.Count > 0 ? lista : null;
    }
}
