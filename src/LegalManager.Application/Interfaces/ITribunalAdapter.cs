namespace LegalManager.Application.Interfaces;

public record TribunalMovimento(
    string Descricao,
    DateTime Data,
    string TipoNome,
    int? CodigoCNJ
);

public record TribunalConsultaResult(
    bool Encontrado,
    string? NomeTribunal,
    string? Vara,
    string? Comarca,
    IReadOnlyList<TribunalMovimento> Movimentos
);

public interface ITribunalAdapter
{
    string Nome { get; }
    bool SuportaTribunal(string tribunal);
    Task<TribunalConsultaResult> ConsultarAsync(string numeroCNJ, CancellationToken ct = default);
}
