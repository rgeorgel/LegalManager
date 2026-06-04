using System.Text.Json.Serialization;

namespace LegalManager.Application.Interfaces;

public record EscavadorProcessoDto(
    long Id,
    string? Numero,
    string? SiglaTribunal,
    string? NomeTribunal,
    string? Vara,
    string? Comarca,
    string? Classe,
    string? Assuntos,
    DateTime? DataAjuizamento,
    string? JsonBruto = null
);

public record EscavadorMonitoramentoDto(long Id, string? Status);

public record EscavadorCallbackDto(
    long Id,
    string Tipo,
    long? MonitoramentoId,
    string? NumeroCNJ,
    string? Descricao,
    DateTime? DataAndamento,
    DateTime CriadoEm
);

public record EscavadorPagedResult<T>(
    IReadOnlyList<T> Data,
    int Total,
    int PaginaAtual,
    int UltimaPagina,
    bool TemProxima
);

/// <summary>
/// Movimentacao as returned by the V1 callback (and by per-process polling).
/// Captures the publicacao shape that <see cref="PublicacaoMapper"/> consumes.
/// </summary>
public record EscavadorMovimentacaoDto(
    long Id,
    string Uuid,
    DateTime? Data,
    string? ConteudoHtml,
    string? Snippet,
    string? Tipo,
    string? Diario,
    long? DiarioId,
    string? DiarioSigla,
    string? OrigemEstado,
    string? Link,
    string? LinkApi,
    string? LinkPdf,
    string? LinkPdfApi,
    string? NumeroCnj,
    string? JsonBruto
);

/// <summary>
/// Slim publication record returned by the OAB search endpoint.
/// </summary>
public record EscavadorPublicacaoDto(
    long Id,
    string Uuid,
    string? NumeroCnj,
    DateTime Data,
    string? Diario,
    string? Snippet,
    string? LinkPdf,
    string? JsonBruto
);

public enum TipoEscavadorBusca { OAB, CPFCNPJ }
public enum ScopeEscavadorTribunal { TRF, TRT, Ambos }

public interface IEscavadorService
{
    Task<EscavadorPagedResult<EscavadorProcessoDto>> BuscarPorOabAsync(
        string oab, string uf, int pagina = 1, CancellationToken ct = default);

    Task<EscavadorPagedResult<EscavadorProcessoDto>> BuscarPorCpfCnpjAsync(
        string cpfCnpj, int pagina = 1, CancellationToken ct = default);

    Task<EscavadorMonitoramentoDto?> CriarMonitoramentoAsync(
        string numeroCNJ, string? frequencia = null, CancellationToken ct = default);

    Task<bool> RemoverMonitoramentoAsync(long id, CancellationToken ct = default);
    Task<bool> RemoverMonitoramentoTermoAsync(long id, CancellationToken ct = default);

    Task<EscavadorPagedResult<EscavadorCallbackDto>> ListarCallbacksPendentesAsync(
        int pagina = 1, CancellationToken ct = default);

    Task MarcarCallbacksRecebidosAsync(IEnumerable<long> ids, CancellationToken ct = default);

    /// <summary>
    /// Ack a single callback by its UUID (newer Escavador API uses uuid, older uses long id).
    /// </summary>
    Task MarcarCallbackRecebidoAsync(string uuid, CancellationToken ct = default);

    /// <summary>
    /// Fetch a single movimentacao by its UUID. Returns null if not found.
    /// </summary>
    Task<EscavadorMovimentacaoDto?> BuscarMovimentacoesPorUuidAsync(
        string uuid, CancellationToken ct = default);

    /// <summary>
    /// List recent movimentacoes for a CNJ since the given date (inclusive).
    /// Used by the polling job to backfill missed publications.
    /// </summary>
    Task<EscavadorPagedResult<EscavadorMovimentacaoDto>> ListarMovimentacoesPorProcessoAsync(
        string numeroCNJ, DateTime? desde, int pagina = 1, CancellationToken ct = default);

    /// <summary>
    /// Search publications by OAB + UF in a date range. Used by the "minha caixa" feature.
    /// </summary>
    Task<EscavadorPagedResult<EscavadorPublicacaoDto>> BuscarPublicacoesPorOabAsync(
        string oab, string uf, DateTime de, DateTime ate, int pagina = 1, CancellationToken ct = default);

    /// <summary>
    /// Cria um Monitoramento TERMO no Escavador para monitorar publicações de uma OAB.
    /// O Escavador passa a empurrar publicações via webhook (já tratado em ReceberCallback).
    /// Retorna o ID do monitoramento remoto, ou null em caso de falha.
    /// </summary>
    Task<EscavadorMonitoramentoDto?> CriarMonitoramentoOabAsync(
        string uf, string numero, string? nomeAdvogado, CancellationToken ct = default);
}
