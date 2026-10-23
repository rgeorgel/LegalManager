using LegalManager.Domain.Enums;

namespace LegalManager.Application.Interfaces;

public interface IIAService
{
    Task<string> TraduzirTextoAsync(string texto, CancellationToken ct = default);
    Task<string> GerarPecaJuridicaAsync(string contexto, string tipoPeca, CancellationToken ct = default);
    Task<string> GerarModeloDocumentoAsync(string descricao, CancellationToken ct = default);
    /// <summary>
    /// Triagem de uma publicação: devolve o texto da IA (JSON com tipo, resumo, ato, prazo e
    /// audiência), interpretado por TriagemPublicacaoParser.
    /// </summary>
    Task<string> TriarPublicacaoAsync(string conteudo, CancellationToken ct = default);
    Task<string> BuscarJurisprudenciaAsync(string tema, CancellationToken ct = default);
    Task<string> GerarResumoProcessoAsync(string contextoProcesso, CancellationToken ct = default);
}