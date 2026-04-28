namespace LegalManager.Application.Interfaces;

public record TribunalMovimentoComplemento(
    int Codigo,
    int? Valor,
    string Nome,
    string Descricao
);

public record TribunalMovimento(
    string Descricao,
    DateTime Data,
    string TipoNome,
    int? CodigoCNJ,
    string? OrgaoJulgador = null,
    IReadOnlyList<TribunalMovimentoComplemento>? Complementos = null,
    IReadOnlyList<string>? ComplementosNaoEstruturados = null,
    IReadOnlyDictionary<string, string>? CamposNaoEstruturados = null
);

public record TribunalParte(
    string Nome,
    string? Cpf,
    string? Cnpj,
    string? OAB,
    string? Polo
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
    string? Grau = null,
    IReadOnlyList<TribunalParte>? Partes = null,
    decimal? ValorCaixa = null,
    DateTime? DataDistribuicao = null,
    string? Numero = null,
    string? SiglaTribunal = null,
    string? Segmento = null,
    string? Ementa = null,
    string? Decisao = null,
    string? Observacao = null,
    string? Relator = null,
    string? TipoDecisao = null,
    string? ResultadoJulgamento = null,
    int? CodigoClasse = null,
    int? NivelSigilo = null,
    string? Instancia = null,
    DateTime? DataJulgamento = null,
    DateTime? DataPublicacao = null,
    DateTime? DataHoraUltimaAtualizacao = null,
    IReadOnlyList<int>? AssuntosCodigos = null
);

public interface ITribunalAdapter
{
    string Nome { get; }
    bool SuportaTribunal(string tribunal);
    Task<TribunalConsultaResult> ConsultarAsync(string numeroCNJ, CancellationToken ct = default);
    Task<TribunalConsultaResult> ConsultarPorTribunalAsync(string numeroCNJ, string tribunal, CancellationToken ct = default);
}