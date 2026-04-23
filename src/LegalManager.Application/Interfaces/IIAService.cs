using LegalManager.Domain.Enums;

namespace LegalManager.Application.Interfaces;

public interface IIAService
{
    Task<string> TraduzirTextoAsync(string texto, CancellationToken ct = default);
    Task<string> GerarPecaJuridicaAsync(string contexto, string tipoPeca, CancellationToken ct = default);
    Task<(TipoPublicacao tipo, string classificacao, bool urgente, string? sugestaoTarefa)> ClassificarPublicacaoAsync(
        string conteudo, string? numeroCNJ = null, CancellationToken ct = default);
    Task<string> BuscarJurisprudenciaAsync(string tema, CancellationToken ct = default);
}