namespace LegalManager.Application.Interfaces;

public record TribunalMovimento(
    string Descricao,
    DateTime Data,
    string TipoNome,
    int? CodigoCNJ,
    string? OrgaoJulgador = null
);

public record TribunalConsultaResult(
    bool Encontrado,
    string? NomeTribunal,
    string? Vara,
    string? Comarca,
    IReadOnlyList<TribunalMovimento> Movimentos,
    string? Classe = null,
    IReadOnlyList<string>? Assuntos = null,
    DateTime? DataAjuizamento = null,
    string? Grau = null
);

public interface ITribunalAdapter
{
    string Nome { get; }
    bool SuportaTribunal(string tribunal);
    Task<TribunalConsultaResult> ConsultarAsync(string numeroCNJ, CancellationToken ct = default);
    Task<TribunalConsultaResult> ConsultarPorTribunalAsync(string numeroCNJ, string tribunal, CancellationToken ct = default);
}
