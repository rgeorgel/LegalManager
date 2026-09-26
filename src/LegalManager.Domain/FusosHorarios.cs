namespace LegalManager.Domain;

/// <summary>
/// Fusos horários que o escritório pode escolher (Configurações → Perfil do Escritório).
/// Prazos e eventos são gravados na hora "de parede" digitada pelo usuário; o fuso do
/// escritório define o "agora" contra o qual eles são comparados (atrasada, hoje, etc.).
/// </summary>
public static class FusosHorarios
{
    /// <summary>Horário de Brasília — usado quando o escritório não escolheu outro.</summary>
    public const string Padrao = "America/Sao_Paulo";

    public record Opcao(string Id, string Nome);

    public static readonly IReadOnlyList<Opcao> Opcoes =
    [
        new("America/Noronha", "Fernando de Noronha (UTC−2)"),
        new("America/Sao_Paulo", "Brasília — DF, SP, RJ, MG, Sul, Nordeste, GO, TO, PA, AP (UTC−3)"),
        new("America/Manaus", "Amazonas (UTC−4)"),
        new("America/Cuiaba", "Mato Grosso (UTC−4)"),
        new("America/Campo_Grande", "Mato Grosso do Sul (UTC−4)"),
        new("America/Porto_Velho", "Rondônia (UTC−4)"),
        new("America/Boa_Vista", "Roraima (UTC−4)"),
        new("America/Rio_Branco", "Acre e sudoeste do Amazonas (UTC−5)"),
    ];

    public static bool EhValido(string? id) => id is not null && Opcoes.Any(o => o.Id == id);
}
