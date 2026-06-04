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
        var firstUrl = $"/api/v2/advogado/processos?oab_estado={Uri.EscapeDataString(uf)}&oab_numero={Uri.EscapeDataString(oab)}&limit=100";
        return await FetchAllByCursor(firstUrl, ct);
    }

    public async Task<EscavadorPagedResult<EscavadorProcessoDto>> BuscarPorCpfCnpjAsync(
        string cpfCnpj, int pagina = 1, CancellationToken ct = default)
    {
        var limpo = new string(cpfCnpj.Where(char.IsDigit).ToArray());
        _logger.LogInformation("[Escavador] Buscando processos por CPF/CNPJ");
        var firstUrl = $"/api/v2/envolvido/processos?documento={Uri.EscapeDataString(limpo)}&limit=100";
        return await FetchAllByCursor(firstUrl, ct);
    }

    public async Task<EscavadorMonitoramentoDto?> CriarMonitoramentoAsync(
        string numeroCNJ, string? frequencia = null, CancellationToken ct = default)
    {
        _logger.LogInformation("[Escavador] Criando monitoramento para {CNJ} frequencia={F}", numeroCNJ, frequencia ?? "diaria");
        try
        {
            var body = frequencia != null
                ? JsonSerializer.Serialize(new { numero = numeroCNJ, frequencia })
                : JsonSerializer.Serialize(new { numero = numeroCNJ });
            using var req = new HttpRequestMessage(HttpMethod.Post, "/api/v2/monitoramentos/processos")
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };
            var resp = await _http.SendAsync(req, ct);
            var json = await resp.Content.ReadAsStringAsync(ct);
            if (!resp.IsSuccessStatusCode)
            {
                _logger.LogWarning("[Escavador] Falha ao criar monitoramento para {CNJ}: {S} — {Body}", numeroCNJ, resp.StatusCode, json);
                return null;
            }
            // v2 retorna o objeto diretamente na raiz (sem wrapper "data")
            var doc = JsonSerializer.Deserialize<MonitoramentoProcessoV2Response>(json, JsonOpts);
            if (doc == null || doc.Id == 0)
            {
                _logger.LogWarning("[Escavador] Monitoramento processo criado mas resposta não contém id — Body: {Body}",
                    json.Length > 300 ? json[..300] : json);
                return null;
            }
            _logger.LogInformation("[Escavador] Monitoramento processo {CNJ} criado com id={Id}", numeroCNJ, doc.Id);
            return new EscavadorMonitoramentoDto(doc.Id, doc.Status);
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
            var resp = await _http.DeleteAsync($"/api/v2/monitoramentos/processos/{id}", ct);
            if (!resp.IsSuccessStatusCode)
            {
                var body = await resp.Content.ReadAsStringAsync(ct);
                _logger.LogWarning("[Escavador] Falha ao remover monitoramento {Id}: {S} — {Body}",
                    id, resp.StatusCode, body.Length > 200 ? body[..200] : body);
            }
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

    public async Task MarcarCallbackRecebidoAsync(string uuid, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(uuid)) return;
        _logger.LogInformation("[Escavador] Marcando callback uuid={Uuid} como recebido", uuid);
        try
        {
            var body = JsonSerializer.Serialize(new { uuids = new[] { uuid } });
            using var req = new HttpRequestMessage(HttpMethod.Post, "/api/v2/callbacks/recebidos")
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };
            var resp = await _http.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode)
                _logger.LogWarning("[Escavador] Falha ao marcar callback {Uuid}: {S}", uuid, resp.StatusCode);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Escavador] Erro ao marcar callback {Uuid}", uuid);
        }
    }

    public async Task<EscavadorMovimentacaoDto?> BuscarMovimentacoesPorUuidAsync(
        string uuid, CancellationToken ct = default)
    {
        _logger.LogInformation("[Escavador] Buscando movimentacao uuid={Uuid}", uuid);
        try
        {
            var resp = await _http.GetAsync($"/api/v2/movimentacoes/{Uri.EscapeDataString(uuid)}", ct);
            if (!resp.IsSuccessStatusCode)
            {
                _logger.LogWarning("[Escavador] {S} ao buscar movimentacao {Uuid}", resp.StatusCode, uuid);
                return null;
            }
            var json = await resp.Content.ReadAsStringAsync(ct);
            var wrapper = JsonSerializer.Deserialize<EscavadorSingleWrapper<MovimentacaoData>>(json, JsonOpts);
            return wrapper?.Data is { } m ? MapMovimentacao(m, json) : null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Escavador] Erro ao buscar movimentacao {Uuid}", uuid);
            return null;
        }
    }

    public async Task<EscavadorPagedResult<EscavadorMovimentacaoDto>> ListarMovimentacoesPorProcessoAsync(
        string numeroCNJ, DateTime? desde, int pagina = 1, CancellationToken ct = default)
    {
        _logger.LogInformation("[Escavador] Listando movimentacoes CNJ={CNJ} desde={Desde}", numeroCNJ, desde);
        var de = desde ?? DateTime.UtcNow.AddDays(-7);
        var url = $"/api/v2/processos/{Uri.EscapeDataString(numeroCNJ)}/movimentacoes?page={pagina}&de={de:yyyy-MM-dd}";
        return await FetchMovimentacoesPaged(url, ct);
    }

    public async Task<EscavadorPagedResult<EscavadorPublicacaoDto>> BuscarPublicacoesPorOabAsync(
        string oab, string uf, DateTime de, DateTime ate, int pagina = 1, CancellationToken ct = default)
    {
        _logger.LogInformation("[Escavador] Buscando publicacoes OAB {Uf}/{Oab} de={De:yyyy-MM-dd} ate={Ate:yyyy-MM-dd}",
            uf, oab, de, ate);
        var url = $"/api/v1/oab/{Uri.EscapeDataString(uf.ToUpperInvariant())}/{Uri.EscapeDataString(oab)}/publicacoes" +
                  $"?de={de:yyyy-MM-dd}&ate={ate:yyyy-MM-dd}&page={pagina}";
        return await FetchPublicacoesPaged(url, ct);
    }

    private async Task<EscavadorPagedResult<EscavadorMovimentacaoDto>> FetchMovimentacoesPaged(
        string url, CancellationToken ct)
    {
        try
        {
            var resp = await _http.GetAsync(url, ct);
            if (!resp.IsSuccessStatusCode)
            {
                _logger.LogWarning("[Escavador] {S} em GET {Url}", resp.StatusCode, url);
                return new EscavadorPagedResult<EscavadorMovimentacaoDto>([], 0, 1, 1, false);
            }
            var json = await resp.Content.ReadAsStringAsync(ct);
            var wrapper = JsonSerializer.Deserialize<EscavadorListWrapper<MovimentacaoData>>(json, JsonOpts);
            if (wrapper == null) return new EscavadorPagedResult<EscavadorMovimentacaoDto>([], 0, 1, 1, false);
            var data = wrapper.Data?.Select(m => MapMovimentacao(m, "{}")).ToList() ?? [];
            return new EscavadorPagedResult<EscavadorMovimentacaoDto>(
                data, data.Count, wrapper.Meta?.CurrentPage ?? 1, wrapper.Meta?.LastPage ?? 1,
                (wrapper.Meta?.CurrentPage ?? 1) < (wrapper.Meta?.LastPage ?? 1));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Escavador] Erro em GET {Url}", url);
            return new EscavadorPagedResult<EscavadorMovimentacaoDto>([], 0, 1, 1, false);
        }
    }

    private async Task<EscavadorPagedResult<EscavadorPublicacaoDto>> FetchPublicacoesPaged(
        string url, CancellationToken ct)
    {
        try
        {
            var resp = await _http.GetAsync(url, ct);
            if (!resp.IsSuccessStatusCode)
            {
                _logger.LogWarning("[Escavador] {S} em GET {Url}", resp.StatusCode, url);
                return new EscavadorPagedResult<EscavadorPublicacaoDto>([], 0, 1, 1, false);
            }
            var json = await resp.Content.ReadAsStringAsync(ct);
            var wrapper = JsonSerializer.Deserialize<EscavadorListWrapper<PublicacaoData>>(json, JsonOpts);
            if (wrapper == null) return new EscavadorPagedResult<EscavadorPublicacaoDto>([], 0, 1, 1, false);
            var data = wrapper.Data?.Select(MapPublicacao).ToList() ?? [];
            return new EscavadorPagedResult<EscavadorPublicacaoDto>(
                data, data.Count, wrapper.Meta?.CurrentPage ?? 1, wrapper.Meta?.LastPage ?? 1,
                (wrapper.Meta?.CurrentPage ?? 1) < (wrapper.Meta?.LastPage ?? 1));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Escavador] Erro em GET {Url}", url);
            return new EscavadorPagedResult<EscavadorPublicacaoDto>([], 0, 1, 1, false);
        }
    }

    private static EscavadorMovimentacaoDto MapMovimentacao(MovimentacaoData r, string jsonBruto) => new(
        r.Id, r.Uuid ?? string.Empty, r.Data,
        r.Conteudo, r.Snippet, r.Tipo, r.Diario,
        r.DiarioId, r.DiarioSigla, r.OrigemEstado,
        r.Link, r.LinkApi, r.LinkPdf, r.LinkPdfApi, r.NumeroCnj, jsonBruto);

    private static EscavadorPublicacaoDto MapPublicacao(PublicacaoData r) => new(
        r.Id, r.Uuid ?? string.Empty, r.NumeroCnj, r.Data ?? DateTime.UtcNow,
        r.Diario, r.Snippet, r.LinkPdf, "{}");

    public async Task<EscavadorMonitoramentoDto?> CriarMonitoramentoOabAsync(
        string uf, string numero, string? nomeAdvogado, CancellationToken ct = default)
    {
        _logger.LogInformation("[Escavador] Criando monitoramento OAB {Uf}/{Numero}", uf, numero);
        try
        {
            var ufUpper = (uf ?? "").ToUpperInvariant();
            // Máximo 3 variações aceitas pela API. Priorizamos as formas mais comuns em diários.
            var variacoes = new[]
            {
                $"{numero}/{ufUpper}",
                $"OAB/{ufUpper} {numero}",
                $"OAB {numero}/{ufUpper}"
            };

            // termos_auxiliares filtra para garantir que a publicação realmente se refere
            // ao advogado certo (evita falsos positivos de OABs com mesmo número em UFs diferentes).
            var termosAuxiliares = new List<object>();
            if (!string.IsNullOrWhiteSpace(nomeAdvogado))
            {
                termosAuxiliares.Add(new { condicao = "CONTEM", termo = nomeAdvogado });
            }

            var body = new
            {
                tipo = "termo",
                monitorar_em_todos_diarios = true,
                termo = numero,
                variacoes,
                termos_auxiliares = termosAuxiliares.ToArray()
            };
            var json = JsonSerializer.Serialize(body);
            using var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/monitoramentos")
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
            var resp = await _http.SendAsync(req, ct);
            var respJson = await resp.Content.ReadAsStringAsync(ct);
            if (!resp.IsSuccessStatusCode)
            {
                // "Você já monitora este termo" — monitoramento existe remotamente mas não temos o ID local.
                // Busca o monitoramento existente pela listagem para recuperar o ID.
                if ((int)resp.StatusCode == 422 && respJson.Contains("monitora este termo", StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogInformation("[Escavador] Monitoramento OAB {Uf}/{Numero} já existe remotamente, buscando ID...", ufUpper, numero);
                    return await BuscarMonitoramentoExistentePorTermoAsync(numero, ct);
                }
                _logger.LogWarning("[Escavador] Falha ao criar monitoramento OAB {Uf}/{Numero}: {S} — {Body}",
                    ufUpper, numero, resp.StatusCode, respJson);
                throw new HttpRequestException($"Escavador {(int)resp.StatusCode}: {respJson}");
            }
            var doc = JsonSerializer.Deserialize<EscavadorMonitoramentoV1Wrapper>(respJson, JsonOpts);
            if (doc?.Monitoramento == null)
            {
                _logger.LogWarning("[Escavador] Monitoramento OAB criado (200) mas resposta não contém monitoramento.id — Body: {Body}", respJson);
                return null;
            }
            _logger.LogInformation("[Escavador] Monitoramento OAB {Uf}/{Numero} criado com id={Id}", ufUpper, numero, doc.Monitoramento.Id);
            return new EscavadorMonitoramentoDto(doc.Monitoramento.Id, doc.Monitoramento.Status);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Escavador] Erro ao criar monitoramento OAB {Uf}/{Numero}", uf, numero);
            return null;
        }
    }

    private async Task<EscavadorMonitoramentoDto?> BuscarMonitoramentoExistentePorTermoAsync(
        string termo, CancellationToken ct)
    {
        try
        {
            var resp = await _http.GetAsync("/api/v1/monitoramentos", ct);
            var json = await resp.Content.ReadAsStringAsync(ct);
            if (!resp.IsSuccessStatusCode)
            {
                _logger.LogWarning("[Escavador] Falha ao listar monitoramentos: {S}", resp.StatusCode);
                return null;
            }
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            JsonElement items = default;
            if (root.TryGetProperty("items", out var itemsKey)) items = itemsKey;
            else if (root.TryGetProperty("data", out var data)) items = data;
            else if (root.TryGetProperty("monitoramentos", out var mons)) items = mons;
            else
            {
                _logger.LogWarning("[Escavador] GET /monitoramentos: formato inesperado — Body: {Body}",
                    json.Length > 500 ? json[..500] : json);
                return null;
            }

            foreach (var item in items.EnumerateArray())
            {
                var itemTermo = item.TryGetProperty("termo", out var t) ? t.GetString() : null;
                if (string.Equals(itemTermo, termo, StringComparison.OrdinalIgnoreCase)
                    && item.TryGetProperty("id", out var idProp)
                    && idProp.TryGetInt64(out var id))
                {
                    var status = item.TryGetProperty("status", out var s) ? s.GetString() : null;
                    _logger.LogInformation("[Escavador] Monitoramento existente encontrado: id={Id} termo={Termo}", id, termo);
                    return new EscavadorMonitoramentoDto(id, status);
                }
            }
            _logger.LogWarning("[Escavador] Monitoramento para termo={Termo} não encontrado na listagem", termo);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Escavador] Erro ao buscar monitoramento existente para termo={Termo}", termo);
            return null;
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

    // Resposta do POST /api/v1/monitoramentos (termo) — chave é "monitoramento", não "data"
    private sealed class EscavadorMonitoramentoV1Wrapper
    {
        [JsonPropertyName("monitoramento")] public MonitoramentoData? Monitoramento { get; set; }
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

    // Resposta do POST /api/v2/monitoramentos/processos — id na raiz, sem wrapper "data"
    private sealed class MonitoramentoProcessoV2Response
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

    private sealed class MovimentacaoData
    {
        [JsonPropertyName("id")] public long Id { get; set; }
        [JsonPropertyName("uuid")] public string? Uuid { get; set; }
        [JsonPropertyName("data")] public DateTime? Data { get; set; }
        [JsonPropertyName("conteudo")] public string? Conteudo { get; set; }
        [JsonPropertyName("snippet")] public string? Snippet { get; set; }
        [JsonPropertyName("tipo")] public string? Tipo { get; set; }
        [JsonPropertyName("diario")] public string? Diario { get; set; }
        [JsonPropertyName("diario_id")] public long? DiarioId { get; set; }
        [JsonPropertyName("diario_sigla")] public string? DiarioSigla { get; set; }
        [JsonPropertyName("origem_estado")] public string? OrigemEstado { get; set; }
        [JsonPropertyName("link")] public string? Link { get; set; }
        [JsonPropertyName("link_api")] public string? LinkApi { get; set; }
        [JsonPropertyName("link_pdf")] public string? LinkPdf { get; set; }
        [JsonPropertyName("link_pdf_api")] public string? LinkPdfApi { get; set; }
        [JsonPropertyName("numero_cnj")] public string? NumeroCnj { get; set; }
    }

    private sealed class PublicacaoData
    {
        [JsonPropertyName("id")] public long Id { get; set; }
        [JsonPropertyName("uuid")] public string? Uuid { get; set; }
        [JsonPropertyName("numero_cnj")] public string? NumeroCnj { get; set; }
        [JsonPropertyName("data")] public DateTime? Data { get; set; }
        [JsonPropertyName("diario")] public string? Diario { get; set; }
        [JsonPropertyName("snippet")] public string? Snippet { get; set; }
        [JsonPropertyName("link_pdf")] public string? LinkPdf { get; set; }
    }
}
