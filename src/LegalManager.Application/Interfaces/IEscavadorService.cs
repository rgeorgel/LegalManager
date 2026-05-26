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
    DateTime? DataAjuizamento
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

public enum TipoEscavadorBusca { OAB, CPFCNPJ }
public enum ScopeEscavadorTribunal { TRF, TRT, Ambos }

public interface IEscavadorService
{
    Task<EscavadorPagedResult<EscavadorProcessoDto>> BuscarPorOabAsync(
        string oab, string uf, int pagina = 1, CancellationToken ct = default);

    Task<EscavadorPagedResult<EscavadorProcessoDto>> BuscarPorCpfCnpjAsync(
        string cpfCnpj, int pagina = 1, CancellationToken ct = default);

    Task<EscavadorMonitoramentoDto?> CriarMonitoramentoAsync(
        string numeroCNJ, CancellationToken ct = default);

    Task<bool> RemoverMonitoramentoAsync(long id, CancellationToken ct = default);

    Task<EscavadorPagedResult<EscavadorCallbackDto>> ListarCallbacksPendentesAsync(
        int pagina = 1, CancellationToken ct = default);

    Task MarcarCallbacksRecebidosAsync(IEnumerable<long> ids, CancellationToken ct = default);
}
