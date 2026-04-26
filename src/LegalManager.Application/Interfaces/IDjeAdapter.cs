using LegalManager.Domain.Enums;

namespace LegalManager.Application.Interfaces;

public interface IDjeAdapter
{
    string Nome { get; }
    string Sigla { get; }
    string BaseUrl { get; }
    bool SuportaTipo(TipoDje tipo);

    Task<DjeConsultaResult> ConsultarPublicacoesAsync(
        DateTime data,
        CancellationToken ct = default);

    Task<DjeConsultaResult> ConsultarPorNomeAsync(
        string nome,
        DateTime? dataInicio = null,
        DateTime? dataFim = null,
        CancellationToken ct = default);

    Task<DjeDetalheResult> ObterDetalheAsync(
        string idPublicacao,
        CancellationToken ct = default);
}

public record DjePublicacao(
    string Id,
    string SiglaTribunal,
    DateTime DataPublicacao,
    string? Secao,
    string? Pagina,
    string? Tipo,
    string Titulo,
    string Conteudo,
    List<string> NomesEncontrados,
    string UrlOriginal,
    decimal? PrazoDias,
    bool Urgente = false)
{
    public DjePublicacao WithConteudo(string novoConteudo) =>
        this with { Conteudo = novoConteudo };
}

public record DjeConsultaResult(
    bool Sucesso,
    string? Erro,
    List<DjePublicacao> Publicacoes);

public record DjeDetalheResult(
    bool Sucesso,
    string? Erro,
    string? TextoIntegral,
    string? HashDje);
