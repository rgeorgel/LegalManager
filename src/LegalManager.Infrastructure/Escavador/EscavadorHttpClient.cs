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
        _logger.LogInformation("[Escavador] Buscando processos por OAB {Oab}/{Uf}", oab, uf);
        var firstUrl = $"/api/v2/advogado/processos?oab_estado={Uri.EscapeDataString(uf)}&oab_numero={Uri.EscapeDataString(oab)}";
        return await FetchAllByCursor(firstUrl, ct);
    }

    public async Task<EscavadorPagedResult<EscavadorProcessoDto>> BuscarPorCpfCnpjAsync(
        string cpfCnpj, int pagina = 1, CancellationToken ct = default)
    {
        var limpo = new string(cpfCnpj.Where(char.IsDigit).ToArray());
        _logger.LogInformation("[Escavador] Buscando processos por CPF/CNPJ");
        var firstUrl = $"/api/v2/envolvido/processos?documento={Uri.EscapeDataString(limpo)}";
        return await FetchAllByCursor(firstUrl, ct);
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

    // Follows cursor pagination (links.next) and returns all pages merged into one result
    private async Task<EscavadorPagedResult<EscavadorProcessoDto>> FetchAllByCursor(
        string firstUrl, CancellationToken ct)
    {
        var all = new List<EscavadorProcessoDto>();
        string? nextUrl = firstUrl;
        const int maxPages = 10;
        var pageCount = 0;

        while (nextUrl != null && pageCount < maxPages)
        {
            var (items, next) = await FetchOnePage(nextUrl, ct);
            all.AddRange(items);
            nextUrl = next;
            pageCount++;
        }

        return new EscavadorPagedResult<EscavadorProcessoDto>(all, all.Count, 1, 1, false);
    }

    private async Task<(List<EscavadorProcessoDto> Items, string? NextUrl)> FetchOnePage(
        string url, CancellationToken ct)
    {
        try
        {
            var resp = await _http.GetAsync(url, ct);
            if (!resp.IsSuccessStatusCode)
            {
                _logger.LogWarning("[Escavador] {S} em GET {Url}", resp.StatusCode, url);
                return ([], null);
            }
            var json = await resp.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var items = new List<EscavadorProcessoDto>();
            if (root.TryGetProperty("items", out var itemsEl))
            {
                foreach (var item in itemsEl.EnumerateArray())
                {
                    var rawText = item.GetRawText();
                    var processoData = JsonSerializer.Deserialize<ProcessoData>(rawText, JsonOpts);
                    if (processoData != null && !string.IsNullOrWhiteSpace(processoData.NumeroCnj))
                        items.Add(MapProcesso(processoData, rawText));
                }
            }

            string? nextUrl = null;
            if (root.TryGetProperty("links", out var linksEl) &&
                linksEl.TryGetProperty("next", out var nextEl) &&
                nextEl.ValueKind == JsonValueKind.String)
            {
                nextUrl = nextEl.GetString();
            }

            return (items, nextUrl);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Escavador] Erro em GET {Url}", url);
            return ([], null);
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
            var wrapper = JsonSerializer.Deserialize<EscavadorListWrapper<CallbackData>>(json, JsonOpts);
            if (wrapper == null) return EmptyCallbacks();
            var data = wrapper.Data?.Select(MapCallback).ToList() ?? [];
            return BuildPaged(data, wrapper.Meta);
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

    private static EscavadorProcessoDto MapProcesso(ProcessoData r, string jsonBruto)
    {
        var capa = r.Fontes?.FirstOrDefault()?.Capa;
        return new EscavadorProcessoDto(
            Id: 0,
            Numero: r.NumeroCnj,
            SiglaTribunal: r.UnidadeOrigem?.TribunalSigla,
            NomeTribunal: r.UnidadeOrigem?.Nome,
            Vara: capa?.OrgaoJulgador ?? r.UnidadeOrigem?.Nome,
            Comarca: r.UnidadeOrigem?.Cidade,
            Classe: capa?.Classe,
            Assuntos: capa?.Assunto,
            DataAjuizamento: r.DataInicio,
            JsonBruto: jsonBruto
        );
    }

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

    // Matches the actual Escavador v2 response shape
    private sealed class ProcessoData
    {
        [JsonPropertyName("numero_cnj")] public string? NumeroCnj { get; set; }
        [JsonPropertyName("data_inicio")] public DateTime? DataInicio { get; set; }
        [JsonPropertyName("unidade_origem")] public UnidadeOrigemRef? UnidadeOrigem { get; set; }
        [JsonPropertyName("titulo_polo_ativo")] public string? TituloPoloAtivo { get; set; }
        [JsonPropertyName("titulo_polo_passivo")] public string? TituloPoloPassivo { get; set; }
        [JsonPropertyName("fontes")] public List<FonteData>? Fontes { get; set; }
    }

    private sealed class UnidadeOrigemRef
    {
        [JsonPropertyName("tribunal_sigla")] public string? TribunalSigla { get; set; }
        [JsonPropertyName("nome")] public string? Nome { get; set; }
        [JsonPropertyName("cidade")] public string? Cidade { get; set; }
    }

    private sealed class FonteData
    {
        [JsonPropertyName("capa")] public CapaData? Capa { get; set; }
    }

    private sealed class CapaData
    {
        [JsonPropertyName("classe")] public string? Classe { get; set; }
        // Real API returns assunto as a plain string, not an object
        [JsonPropertyName("assunto")] public string? Assunto { get; set; }
        [JsonPropertyName("valor_causa")] public ValorCausaRef? ValorCausa { get; set; }
        [JsonPropertyName("orgao_julgador")] public string? OrgaoJulgador { get; set; }
    }

    private sealed class ValorCausaRef
    {
        // Real API returns valor as a string ("2510.3800"), not a number
        [JsonPropertyName("valor")] public string? Valor { get; set; }
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
