using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using LegalManager.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace LegalManager.Infrastructure.Escavador;

public class EscavadorHttpClient : IEscavadorService
{
    private readonly HttpClient _http;
    private readonly ILogger<EscavadorHttpClient> _logger;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public EscavadorHttpClient(HttpClient http, ILogger<EscavadorHttpClient> logger)
    {
        _http = http;
        _logger = logger;
    }

    public async Task<EscavadorPagedResult<EscavadorProcessoDto>> BuscarPorOabAsync(
        string oab, string uf, int pagina = 1, CancellationToken ct = default)
    {
        var url = $"/api/v2/advogado/{Uri.EscapeDataString(oab)}/processos?uf={Uri.EscapeDataString(uf)}&page={pagina}";
        _logger.LogInformation("[Escavador] Buscando processos por OAB {Oab}/{Uf} página {P}", oab, uf, pagina);
        return await FetchProcessosPaged(url, ct);
    }

    public async Task<EscavadorPagedResult<EscavadorProcessoDto>> BuscarPorCpfCnpjAsync(
        string cpfCnpj, int pagina = 1, CancellationToken ct = default)
    {
        var limpo = new string(cpfCnpj.Where(char.IsDigit).ToArray());
        var url = $"/api/v2/envolvido/processos?documento={Uri.EscapeDataString(limpo)}&page={pagina}";
        _logger.LogInformation("[Escavador] Buscando processos por CPF/CNPJ página {P}", pagina);
        return await FetchProcessosPaged(url, ct);
    }

    public async Task<EscavadorMonitoramentoDto?> CriarMonitoramentoAsync(
        string numeroCNJ, CancellationToken ct = default)
    {
        _logger.LogInformation("[Escavador] Criando monitoramento para {CNJ}", numeroCNJ);
        try
        {
            var body = JsonSerializer.Serialize(new { numero_processo = numeroCNJ });
            using var req = new HttpRequestMessage(HttpMethod.Post, "/api/v2/monitoramento-processos")
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };
            var resp = await _http.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode)
            {
                _logger.LogWarning("[Escavador] Falha ao criar monitoramento para {CNJ}: {S}", numeroCNJ, resp.StatusCode);
                return null;
            }
            var json = await resp.Content.ReadAsStringAsync(ct);
            var doc = JsonSerializer.Deserialize<EscavadorSingleWrapper<MonitoramentoData>>(json, JsonOpts);
            if (doc?.Data == null) return null;
            return new EscavadorMonitoramentoDto(doc.Data.Id, doc.Data.Status);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Escavador] Erro ao criar monitoramento para {CNJ}", numeroCNJ);
            return null;
        }
    }

    public async Task<bool> RemoverMonitoramentoAsync(long id, CancellationToken ct = default)
    {
        _logger.LogInformation("[Escavador] Removendo monitoramento {Id}", id);
        try
        {
            var resp = await _http.DeleteAsync($"/api/v2/monitoramento-processos/{id}", ct);
            return resp.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Escavador] Erro ao remover monitoramento {Id}", id);
            return false;
        }
    }

    public async Task<EscavadorPagedResult<EscavadorCallbackDto>> ListarCallbacksPendentesAsync(
        int pagina = 1, CancellationToken ct = default)
    {
        var url = $"/api/v2/callbacks?page={pagina}";
        return await FetchCallbacksPaged(url, ct);
    }

    public async Task MarcarCallbacksRecebidosAsync(IEnumerable<long> ids, CancellationToken ct = default)
    {
        var lista = ids.ToList();
        if (lista.Count == 0) return;
        _logger.LogInformation("[Escavador] Marcando {N} callbacks como recebidos", lista.Count);
        try
        {
            var body = JsonSerializer.Serialize(new { ids = lista });
            using var req = new HttpRequestMessage(HttpMethod.Post, "/api/v2/callbacks/recebidos")
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };
            var resp = await _http.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode)
                _logger.LogWarning("[Escavador] Falha ao marcar callbacks: {S}", resp.StatusCode);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Escavador] Erro ao marcar callbacks como recebidos");
        }
    }

    private async Task<EscavadorPagedResult<EscavadorProcessoDto>> FetchProcessosPaged(
        string url, CancellationToken ct)
    {
        try
        {
            var resp = await _http.GetAsync(url, ct);
            if (!resp.IsSuccessStatusCode)
            {
                _logger.LogWarning("[Escavador] {S} em GET {Url}", resp.StatusCode, url);
                return EmptyProcessos();
            }
            var json = await resp.Content.ReadAsStringAsync(ct);
            var doc = JsonSerializer.Deserialize<EscavadorListWrapper<ProcessoData>>(json, JsonOpts);
            if (doc == null) return EmptyProcessos();
            var data = doc.Data?.Select(MapProcesso).ToList() ?? [];
            return BuildPaged(data, doc.Meta);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Escavador] Erro em GET {Url}", url);
            return EmptyProcessos();
        }
    }

    private async Task<EscavadorPagedResult<EscavadorCallbackDto>> FetchCallbacksPaged(
        string url, CancellationToken ct)
    {
        try
        {
            var resp = await _http.GetAsync(url, ct);
            if (!resp.IsSuccessStatusCode)
            {
                _logger.LogWarning("[Escavador] {S} em GET {Url}", resp.StatusCode, url);
                return EmptyCallbacks();
            }
            var json = await resp.Content.ReadAsStringAsync(ct);
            var doc = JsonSerializer.Deserialize<EscavadorListWrapper<CallbackData>>(json, JsonOpts);
            if (doc == null) return EmptyCallbacks();
            var data = doc.Data?.Select(MapCallback).ToList() ?? [];
            return BuildPaged(data, doc.Meta);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Escavador] Erro em GET {Url}", url);
            return EmptyCallbacks();
        }
    }

    private static EscavadorPagedResult<EscavadorProcessoDto> EmptyProcessos() =>
        new([], 0, 1, 1, false);

    private static EscavadorPagedResult<EscavadorCallbackDto> EmptyCallbacks() =>
        new([], 0, 1, 1, false);

    private static EscavadorPagedResult<T> BuildPaged<T>(List<T> data, EscavadorMeta? meta) =>
        new(data,
            meta?.Total ?? data.Count,
            meta?.CurrentPage ?? 1,
            meta?.LastPage ?? 1,
            (meta?.CurrentPage ?? 1) < (meta?.LastPage ?? 1));

    private static EscavadorProcessoDto MapProcesso(ProcessoData r) => new(
        r.Id,
        r.Numero ?? r.NumeroProcesso,
        r.SiglaTribunal ?? r.Tribunal?.Sigla,
        r.Tribunal?.Nome,
        r.Vara,
        r.Comarca,
        r.Classe?.Nome,
        r.Assunto?.Nome,
        r.DataAjuizamento
    );

    private static EscavadorCallbackDto MapCallback(CallbackData r) => new(
        r.Id,
        r.Tipo ?? string.Empty,
        r.MonitoramentoId,
        r.Processo?.Numero ?? r.Processo?.NumeroProcesso,
        r.Conteudo?.Descricao ?? r.Conteudo?.Texto,
        r.Conteudo?.Data,
        r.CriadoEm ?? DateTime.UtcNow
    );

    // ─── Internal API response models ────────────────────────────────────────

    private sealed class EscavadorListWrapper<T>
    {
        [JsonPropertyName("data")] public List<T>? Data { get; set; }
        [JsonPropertyName("meta")] public EscavadorMeta? Meta { get; set; }
    }

    private sealed class EscavadorSingleWrapper<T>
    {
        [JsonPropertyName("data")] public T? Data { get; set; }
    }

    private sealed class EscavadorMeta
    {
        [JsonPropertyName("current_page")] public int CurrentPage { get; set; }
        [JsonPropertyName("last_page")] public int LastPage { get; set; }
        [JsonPropertyName("total")] public int Total { get; set; }
    }

    private sealed class ProcessoData
    {
        [JsonPropertyName("id")] public long Id { get; set; }
        [JsonPropertyName("numero")] public string? Numero { get; set; }
        [JsonPropertyName("numero_processo")] public string? NumeroProcesso { get; set; }
        [JsonPropertyName("sigla_tribunal")] public string? SiglaTribunal { get; set; }
        [JsonPropertyName("tribunal")] public TribunalRef? Tribunal { get; set; }
        [JsonPropertyName("vara")] public string? Vara { get; set; }
        [JsonPropertyName("comarca")] public string? Comarca { get; set; }
        [JsonPropertyName("classe")] public NomeRef? Classe { get; set; }
        [JsonPropertyName("assunto")] public NomeRef? Assunto { get; set; }
        [JsonPropertyName("data_ajuizamento")] public DateTime? DataAjuizamento { get; set; }
    }

    private sealed class TribunalRef
    {
        [JsonPropertyName("sigla")] public string? Sigla { get; set; }
        [JsonPropertyName("nome")] public string? Nome { get; set; }
    }

    private sealed class NomeRef
    {
        [JsonPropertyName("nome")] public string? Nome { get; set; }
    }

    private sealed class MonitoramentoData
    {
        [JsonPropertyName("id")] public long Id { get; set; }
        [JsonPropertyName("status")] public string? Status { get; set; }
    }

    private sealed class CallbackData
    {
        [JsonPropertyName("id")] public long Id { get; set; }
        [JsonPropertyName("tipo")] public string? Tipo { get; set; }
        [JsonPropertyName("monitoramento_id")] public long? MonitoramentoId { get; set; }
        [JsonPropertyName("processo")] public ProcessoRef? Processo { get; set; }
        [JsonPropertyName("conteudo")] public CallbackConteudo? Conteudo { get; set; }
        [JsonPropertyName("created_at")] public DateTime? CriadoEm { get; set; }
    }

    private sealed class ProcessoRef
    {
        [JsonPropertyName("numero")] public string? Numero { get; set; }
        [JsonPropertyName("numero_processo")] public string? NumeroProcesso { get; set; }
    }

    private sealed class CallbackConteudo
    {
        [JsonPropertyName("descricao")] public string? Descricao { get; set; }
        [JsonPropertyName("texto")] public string? Texto { get; set; }
        [JsonPropertyName("data")] public DateTime? Data { get; set; }
    }
}
