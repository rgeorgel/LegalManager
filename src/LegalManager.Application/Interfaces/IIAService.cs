using LegalManager.Domain.Enums;

namespace LegalManager.Application.Interfaces;

public interface IIAService
{
    Task<string> TraduzirTextoAsync(string texto, CancellationToken ct = default);
    Task<string> GerarPecaJuridicaAsync(string contexto, string tipoPeca, CancellationToken ct = default);
    /// <summary>Peça que só pode citar os precedentes de <paramref name="listaPrecedentes"/> (base oficial).</summary>
    Task<string> GerarPecaComPrecedentesAsync(string contexto, string tipoPeca, string listaPrecedentes, CancellationToken ct = default);
    /// <summary>Termos jurídicos e sinônimos para buscar jurisprudência sobre o caso (busca por significado).</summary>
    Task<string> ExpandirBuscaJurisprudenciaAsync(string caso, CancellationToken ct = default);
    /// <summary>Números dos itens da lista pertinentes ao caso, do mais ao menos relevante, ou "nenhum".</summary>
    Task<string> EscolherPrecedentesAsync(string caso, string listaNumerada, int maximo, CancellationToken ct = default);
    Task<string> GerarModeloDocumentoAsync(string descricao, CancellationToken ct = default);
    /// <summary>
    /// Triagem de uma publicação: devolve o texto da IA (JSON com tipo, resumo, ato, prazo e
    /// audiência), interpretado por TriagemPublicacaoParser.
    /// </summary>
    Task<string> TriarPublicacaoAsync(string conteudo, CancellationToken ct = default);
    /// <summary>
    /// Textos do resumo diário ("Seu dia"): visão geral do dia e, para cada processo com andamento
    /// novo, o que aconteceu em linguagem simples e o próximo passo. Devolve o JSON da IA,
    /// interpretado por ResumoDiaParser. <paramref name="contexto"/> traz só fatos do banco.
    /// </summary>
    Task<string> ResumirDiaAsync(string contexto, CancellationToken ct = default);
    Task<string> BuscarJurisprudenciaAsync(string tema, CancellationToken ct = default);
    Task<string> GerarResumoProcessoAsync(string contextoProcesso, CancellationToken ct = default);
}
/// <summary>
/// O provedor de IA não entregou uma resposta utilizável (recusa, limite de uso, resposta vazia).
/// A mensagem é para o usuário.
/// </summary>
public class IAIndisponivelException(string message) : Exception(message);
