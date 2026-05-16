using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using LegalManager.Application.DTOs.Onboarding;
using LegalManager.Application.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace LegalManager.Infrastructure.Tribunais;

public class DataJudAdapter : ITribunalAdapter
{
    private readonly HttpClient _http;
    private readonly ILogger<DataJudAdapter> _logger;

    private static readonly Dictionary<string, string> TribunalIndex = new(StringComparer.OrdinalIgnoreCase)
    {
        { "STF",  "stf"  }, { "STJ",  "stj"  }, { "TST",  "tst"  },
        { "TSE",  "tse"  }, { "STM",  "stm"  },
        { "TRF1", "trf1" }, { "TRF2", "trf2" }, { "TRF3", "trf3" },
        { "TRF4", "trf4" }, { "TRF5", "trf5" }, { "TRF6", "trf6" },
        { "TJAC", "tjac" }, { "TJAL", "tjal" }, { "TJAM", "tjam" },
        { "TJAP", "tjap" }, { "TJBA", "tjba" }, { "TJCE", "tjce" },
        { "TJDF", "tjdf" }, { "TJES", "tjes" }, { "TJGO", "tjgo" },
        { "TJMA", "tjma" }, { "TJMG", "tjmg" }, { "TJMS", "tjms" },
        { "TJMT", "tjmt" }, { "TJPA", "tjpa" }, { "TJPB", "tjpb" },
        { "TJPE", "tjpe" }, { "TJPI", "tjpi" }, { "TJPR", "tjpr" },
        { "TJRJ", "tjrj" }, { "TJRN", "tjrn" }, { "TJRO", "tjro" },
        { "TJRR", "tjrr" }, { "TJRS", "tjrs" }, { "TJSC", "tjsc" },
        { "TJSE", "tjse" }, { "TJSP", "tjsp" }, { "TJTO", "tjto" },
        { "TRT1",  "trt1"  }, { "TRT2",  "trt2"  }, { "TRT3",  "trt3"  },
        { "TRT4",  "trt4"  }, { "TRT5",  "trt5"  }, { "TRT6",  "trt6"  },
        { "TRT7",  "trt7"  }, { "TRT8",  "trt8"  }, { "TRT9",  "trt9"  },
        { "TRT10", "trt10" }, { "TRT11", "trt11" }, { "TRT12", "trt12" },
        { "TRT13", "trt13" }, { "TRT14", "trt14" }, { "TRT15", "trt15" },
        { "TRT16", "trt16" }, { "TRT17", "trt17" }, { "TRT18", "trt18" },
        { "TRT19", "trt19" }, { "TRT20", "trt20" }, { "TRT21", "trt21" },
        { "TRT22", "trt22" }, { "TRT23", "trt23" }, { "TRT24", "trt24" },
    };

    public string Nome => "DataJud (CNJ)";

    public DataJudAdapter(HttpClient http, ILogger<DataJudAdapter> logger)
    {
        _http = http;
        _logger = logger;
    }

    public bool SuportaTribunal(string tribunal) =>
        !string.IsNullOrWhiteSpace(tribunal) && TribunalIndex.ContainsKey(tribunal.Trim());

    public async Task<TribunalConsultaResult> ConsultarAsync(string numeroCNJ, CancellationToken ct = default)
    {
        var tribKey = InferirTribunal(numeroCNJ);
        if (tribKey == null)
            return ResultadoVazio();

        return await ConsultarTribunalAsync(numeroCNJ, tribKey, ct);
    }

    public async Task<TribunalConsultaResult> ConsultarPorTribunalAsync(
        string numeroCNJ, string tribunal, CancellationToken ct = default)
    {
        if (!TribunalIndex.TryGetValue(tribunal.Trim(), out var idx))
            return ResultadoVazio();

        return await ConsultarTribunalAsync(numeroCNJ, idx, ct);
    }

    private static TribunalConsultaResult ResultadoVazio() =>
        new(false, null, null, null, [], null, null, null, null, null, null, null, null, null, null,
            null, null, null, null, null, null, null, null, null, null, null, null);

    private async Task<TribunalConsultaResult> ConsultarTribunalAsync(
        string numeroCNJ, string indexSuffix, CancellationToken ct)
    {
        var endpoint = $"/api_publica_{indexSuffix}/_search";
        _logger.LogInformation("DataJud: Consultando {Endpoint} para CNJ {NumCNJ}", endpoint, numeroCNJ);

        var body = new
        {
            query = new { match = new { numeroProcesso = numeroCNJ } },
            size = 1
        };

        try
        {
            var json = JsonSerializer.Serialize(body);
            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };

            _logger.LogInformation("DataJud: Enviando request para {Endpoint}", endpoint);
            var response = await _http.SendAsync(request, ct);
            _logger.LogInformation("DataJud: Resposta status {Status}", response.StatusCode);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("DataJud retornou {Status} para {NumCNJ}", response.StatusCode, numeroCNJ);
                return ResultadoVazio();
            }

            var result = await response.Content.ReadFromJsonAsync<DataJudResponse>(
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }, ct);

            if (result?.Hits?.HitsData == null || result.Hits.HitsData.Count == 0)
            {
                _logger.LogInformation("DataJud: Nenhum resultado para {NumCNJ}", numeroCNJ);
                return ResultadoVazio();
            }

            var source = result.Hits.HitsData[0].Source;
            if (source == null)
                return ResultadoVazio();

            var movimentos = (source.Movimentos ?? [])
                .Select(m => new TribunalMovimento(
                    Descricao: m.Nome ?? "Movimento sem descrição",
                    Data: m.DataHora,
                    TipoNome: m.Nome ?? "Outro",
                    CodigoCNJ: m.Codigo,
                    OrgaoJulgador: m.OrgaoJulgador?.Nome,
                    Complementos: (m.ComplementosTabelados ?? []).Select(c =>
                        new TribunalMovimentoComplemento(c.Codigo, c.Valor, c.Nome ?? "", c.Descricao ?? "")).ToList(),
                    ComplementosNaoEstruturados: m.ComplementosNaoTabelados,
                    CamposNaoEstruturados: m.CamposNaoEstruturados))
                .OrderBy(m => m.Data)
                .ToList();

            var assuntos = (source.Assuntos ?? []).Select(a => a.Nome ?? "").Where(n => !string.IsNullOrEmpty(n)).ToList();
            var assuntosCodigos = (source.Assuntos ?? []).Select(a => a.Codigo).Where(c => c.HasValue).Select(c => c!.Value).ToList();

            var partes = (source.Partes ?? []).Select(p => new TribunalParte(
                Nome: p.Nome ?? "",
                Cpf: p.Cpf,
                Cnpj: p.Cnpj,
                OAB: p.OAB,
                Polo: p.Polo)).ToList();

            _logger.LogInformation("DataJud: Encontrado processo {NumCNJ} com {Count} movimentos, {PartesCount} partes", numeroCNJ, movimentos.Count, partes.Count);

            return new TribunalConsultaResult(
                Encontrado: true,
                NomeTribunal: source.Tribunal,
                Vara: source.OrgaoJulgador?.Nome,
                Comarca: null,
                Movimentos: movimentos,
                Classe: source.Classe?.Nome,
                Assuntos: assuntos,
                DataAjuizamento: ParseData(source.DataAjuizamento),
                Grau: source.Grau,
                Partes: partes,
                ValorCaixa: source.ValorCaixa,
                DataDistribuicao: ParseData(source.DataDistribuicao),
                Numero: source.Numero,
                SiglaTribunal: source.SiglaTribunal,
                Segmento: source.Segmento,
                Ementa: source.TxtEmenta,
                Decisao: source.TxtDecisao,
                Observacao: source.TxtObservacao,
                Relator: source.Relator?.Nome,
                TipoDecisao: source.TipoDecisao,
                ResultadoJulgamento: source.ResultadoJulgamento,
                CodigoClasse: source.CodigoClasse,
                NivelSigilo: source.NivelSigilo,
                Instancia: source.Instancia,
                DataJulgamento: ParseData(source.DataJulgamento),
                DataPublicacao: ParseData(source.DataPublicacao),
                DataHoraUltimaAtualizacao: ParseData(source.DataHoraUltimaAtualizacao),
                AssuntosCodigos: assuntosCodigos);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao consultar DataJud para {NumCNJ}", numeroCNJ);
            return ResultadoVazio();
        }
    }

    private static readonly Dictionary<string, string[]> TribunaisPorUF =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["AC"] = ["tjac", "trf1", "trt14"],
            ["AL"] = ["tjal", "trf5", "trt19"],
            ["AM"] = ["tjam", "trf1", "trt11"],
            ["AP"] = ["tjap", "trf1", "trt8"],
            ["BA"] = ["tjba", "trf1", "trt5"],
            ["CE"] = ["tjce", "trf5", "trt7"],
            ["DF"] = ["tjdft", "trf1", "trt10"],
            ["ES"] = ["tjes", "trf2", "trt17"],
            ["GO"] = ["tjgo", "trf1", "trt18"],
            ["MA"] = ["tjma", "trf1", "trt16"],
            ["MG"] = ["tjmg", "trf1", "trt3"],
            ["MS"] = ["tjms", "trf3", "trt24"],
            ["MT"] = ["tjmt", "trf1", "trt23"],
            ["PA"] = ["tjpa", "trf1", "trt8"],
            ["PB"] = ["tjpb", "trf5", "trt13"],
            ["PE"] = ["tjpe", "trf5", "trt6"],
            ["PI"] = ["tjpi", "trf1", "trt22"],
            ["PR"] = ["tjpr", "trf4", "trt9"],
            ["RJ"] = ["tjrj", "trf2", "trt1"],
            ["RN"] = ["tjrn", "trf5", "trt21"],
            ["RO"] = ["tjro", "trf1", "trt14"],
            ["RR"] = ["tjrr", "trf1", "trt11"],
            ["RS"] = ["tjrs", "trf4", "trt4"],
            ["SC"] = ["tjsc", "trf4", "trt12"],
            ["SE"] = ["tjse", "trf5", "trt20"],
            ["SP"] = ["trf3", "trt2", "trt15"], // TJSP não envia advocacia ao DataJud — usa ESAJ
            ["TO"] = ["tjto", "trf1", "trt10"],
        };

    public async Task<List<ProcessoOabPreviewDto>> BuscarPorOabAsync(
        string numeroOAB, string uf, CancellationToken ct = default)
    {
        if (!TribunaisPorUF.TryGetValue(uf.Trim(), out var tribunais))
            return [];

        var todos = tribunais.ToArray();
        var resultados = new System.Collections.Concurrent.ConcurrentBag<ProcessoOabPreviewDto>();

        const int concorrencia = 5;
        for (var i = 0; i < todos.Length; i += concorrencia)
        {
            var lote = todos.Skip(i).Take(concorrencia);
            var tarefas = lote.Select(idx => BuscarPorOabNoTribunalAsync(numeroOAB, uf, idx, ct));
            var respostas = await Task.WhenAll(tarefas);
            foreach (var lista in respostas)
                foreach (var p in lista)
                    resultados.Add(p);
        }

        return resultados
            .GroupBy(p => p.NumeroCNJ)
            .Select(g => g.First())
            .OrderByDescending(p => p.DataAjuizamento)
            .ToList();
    }

    private async Task<List<ProcessoOabPreviewDto>> BuscarPorOabNoTribunalAsync(
        string numeroOAB, string uf, string idx, CancellationToken ct)
    {
        var endpoint = $"/api_publica_{idx}/_search";
        var processos = new List<ProcessoOabPreviewDto>();
        object[]? searchAfter = null;
        const int pageSize = 100;

        while (true)
        {
            var body = MontarQueryOab(numeroOAB, uf, pageSize, searchAfter);
            var json = JsonSerializer.Serialize(body);

            DataJudResponse? result = null;
            for (var tentativa = 0; tentativa < 3; tentativa++)
            {
                try
                {
                    using var req = new HttpRequestMessage(HttpMethod.Post, endpoint)
                    {
                        Content = new StringContent(json, Encoding.UTF8, "application/json")
                    };
                    var resp = await _http.SendAsync(req, ct);

                    if (resp.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                    {
                        await Task.Delay(TimeSpan.FromSeconds(tentativa + 1), ct);
                        continue;
                    }
                    if (!resp.IsSuccessStatusCode)
                    {
                        var errBody = await resp.Content.ReadAsStringAsync(ct);
                        _logger.LogWarning("DataJud OAB: {Status} em {Idx}: {Body}",
                            resp.StatusCode, idx, errBody.Length > 500 ? errBody[..500] : errBody);
                        return processos;
                    }

                    result = await resp.Content.ReadFromJsonAsync<DataJudResponse>(
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true }, ct);
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "DataJud OAB: erro no tribunal {Idx} tentativa {T}", idx, tentativa);
                    if (tentativa == 2) return processos;
                }
            }

            var hits = result?.Hits?.HitsData;
            if (hits == null || hits.Count == 0) break;

            foreach (var h in hits.Where(h => h.Source?.Numero != null))
                processos.Add(new ProcessoOabPreviewDto(
                    NumeroCNJ: h.Source!.Numero!,
                    Tribunal: h.Source.SiglaTribunal ?? idx.ToUpperInvariant(),
                    Vara: h.Source.OrgaoJulgador?.Nome,
                    Classe: h.Source.Classe?.Nome,
                    DataAjuizamento: ParseDataIso(h.Source.DataAjuizamento),
                    Grau: h.Source.Grau
                ));

            if (hits.Count < pageSize) break;

            var ultimo = hits[^1];
            if (ultimo.Sort == null || ultimo.Sort.Count == 0) break;
            searchAfter = ultimo.Sort.Select(e => (object)e.ToString()).ToArray();
        }

        return processos;
    }

    private static object MontarQueryOab(string numeroOAB, string uf, int size, object[]? searchAfter)
    {
        var queryBase = new
        {
            query = new
            {
                @bool = new
                {
                    must = new object[]
                    {
                        new
                        {
                            nested = new
                            {
                                path = "advocacia",
                                query = new
                                {
                                    @bool = new
                                    {
                                        must = new object[]
                                        {
                                            new { match = new Dictionary<string, object> { ["advocacia.numeroOAB"] = numeroOAB.Trim() } },
                                            new { match = new Dictionary<string, object> { ["advocacia.ufOAB"] = uf.Trim().ToUpperInvariant() } }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            },
            size,
            sort = new object[]
            {
                new Dictionary<string, object> { ["dataAjuizamento"] = new { order = "desc" } },
                new Dictionary<string, object> { ["_id"] = new { order = "asc" } }
            },
            _source = new[] { "numero", "classe", "orgaoJulgador", "dataAjuizamento", "siglaTribunal", "grau" }
        };

        if (searchAfter == null)
            return queryBase;

        return new
        {
            queryBase.query,
            queryBase.size,
            queryBase.sort,
            queryBase._source,
            search_after = searchAfter
        };
    }

    private static DateTime? ParseData(string? data)
    {
        if (string.IsNullOrEmpty(data)) return null;
        return DateTime.TryParseExact(data, "yyyyMMddHHmmss", null, System.Globalization.DateTimeStyles.None, out var dt) ? dt : null;
    }

    private static DateTime? ParseDataIso(string? data)
    {
        if (string.IsNullOrEmpty(data)) return null;
        return DateTime.TryParse(data, null, System.Globalization.DateTimeStyles.RoundtripKind, out var dt) ? dt : null;
    }

    private static string? InferirTribunal(string numeroCNJ)
    {
        var normalized = numeroCNJ.Replace("-", "").Replace(".", "");
        if (normalized.Length < 13) return null;

        if (normalized.Length == 20)
        {
            // CNJ format NNNNNNNDDAAAAJTTOOOO — J at index 13, TT at 14-15
            if (!int.TryParse(normalized.Substring(13, 1), out var j) ||
                !int.TryParse(normalized.Substring(14, 2), out var tt))
                return null;

            if (j == 6) return MapearTJEstadual(tt);
            if (j == 8 && tt == 26) return "tjsp";
            if (j == 8) return "stj";
            if (j == 9) return "stf";
            if (j == 5) return "tse";
            if (j == 3) return "stm";
            if (j == 2 && tt == 0) return "tst";
            if (j == 2 && tt >= 1 && tt <= 24) return $"trt{tt}";
            if (j == 1 && tt >= 1 && tt <= 6) return $"trf{tt}";
            if (j == 4 && tt >= 1 && tt <= 6) return $"trf{tt}";
        }

        var parts = numeroCNJ.Replace("-", ".").Split('.');
        if (parts.Length < 6) return null;

        // Split format: NNNNNNN.DD.AAAA.J.TT.OOOO — J at parts[3], TT at parts[4]
        if (int.TryParse(parts[3], out var j2) && int.TryParse(parts[4], out var tt2))
        {
            if (j2 == 6) return MapearTJEstadual(tt2);
            if (j2 == 8 && tt2 == 26) return "tjsp";
            if (j2 == 8) return "stj";
            if (j2 == 2 && tt2 == 0) return "tst";
            if (j2 == 2 && tt2 >= 1 && tt2 <= 24) return $"trt{tt2}";
            if (j2 == 1 && tt2 >= 1 && tt2 <= 6) return $"trf{tt2}";
        }

        return null;
    }

    private static string? MapearTJEstadual(int tt) => tt switch
    {
        1  => "tjac", 2  => "tjal", 3  => "tjap", 4  => "tjam",
        5  => "tjba", 6  => "tjce", 7  => "tjdf", 8  => "tjes",
        9  => "tjgo", 10 => "tjma", 11 => "tjmt", 12 => "tjms",
        13 => "tjmg", 14 => "tjpa", 15 => "tjpb", 16 => "tjpr",
        17 => "tjpe", 18 => "tjpi", 19 => "tjrn", 20 => "tjrs",
        21 => "tjro", 22 => "tjrr", 23 => "tjsc", 24 => "tjse",
        25 => "tjsp", 26 => "tjto", 27 => "tjrj",
        _ => null
    };

    private sealed class DataJudResponse
    {
        public DataJudHits? Hits { get; set; }
    }

    private sealed class DataJudHits
    {
        [System.Text.Json.Serialization.JsonPropertyName("hits")]
        public List<DataJudHit>? HitsData { get; set; }
        public DataJudTotal? Total { get; set; }
    }

    private sealed class DataJudTotal
    {
        public int Value { get; set; }
    }

    private sealed class DataJudHit
    {
        [System.Text.Json.Serialization.JsonPropertyName("_source")]
        public DataJudSource? Source { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("sort")]
        public List<System.Text.Json.JsonElement>? Sort { get; set; }
    }

    private sealed class DataJudSource
    {
        public string? Tribunal { get; set; }
        public DataJudOrgao? OrgaoJulgador { get; set; }
        public List<DataJudMovimento>? Movimentos { get; set; }
        public DataJudClasse? Classe { get; set; }
        public List<DataJudAssunto>? Assuntos { get; set; }
        public string? DataAjuizamento { get; set; }
        public string? Grau { get; set; }
        public List<DataJudParte>? Partes { get; set; }
        public decimal? ValorCaixa { get; set; }
        public string? DataDistribuicao { get; set; }
        public string? Numero { get; set; }
        public string? SiglaTribunal { get; set; }
        public string? Segmento { get; set; }
        public string? TxtEmenta { get; set; }
        public string? TxtDecisao { get; set; }
        public string? TxtObservacao { get; set; }
        public DataJudRelator? Relator { get; set; }
        public string? TipoDecisao { get; set; }
        public string? ResultadoJulgamento { get; set; }
        public int? CodigoClasse { get; set; }
        public int? NivelSigilo { get; set; }
        public string? Instancia { get; set; }
        public string? DataJulgamento { get; set; }
        public string? DataPublicacao { get; set; }
        public string? DataHoraUltimaAtualizacao { get; set; }
    }

    private sealed class DataJudRelator
    {
        public int? Codigo { get; set; }
        public string? Nome { get; set; }
    }

    private sealed class DataJudParte
    {
        public string? Nome { get; set; }
        public string? Cpf { get; set; }
        public string? Cnpj { get; set; }
        public string? OAB { get; set; }
        public string? Polo { get; set; }
    }

    private sealed class DataJudOrgao
    {
        public string? Nome { get; set; }
    }

    private sealed class DataJudClasse
    {
        public string? Nome { get; set; }
    }

    private sealed class DataJudAssunto
    {
        public int? Codigo { get; set; }
        public string? Nome { get; set; }
    }

    private sealed class DataJudMovimento
    {
        public int? Codigo { get; set; }
        public string? Nome { get; set; }
        public DateTime DataHora { get; set; }
        public DataJudOrgao? OrgaoJulgador { get; set; }
        public List<DataJudComplementoTabulado>? ComplementosTabelados { get; set; }
        public List<string>? ComplementosNaoTabelados { get; set; }
        public Dictionary<string, string>? CamposNaoEstruturados { get; set; }
    }

    private sealed class DataJudComplementoTabulado
    {
        public int Codigo { get; set; }
        public int? Valor { get; set; }
        public string? Nome { get; set; }
        public string? Descricao { get; set; }
    }
}