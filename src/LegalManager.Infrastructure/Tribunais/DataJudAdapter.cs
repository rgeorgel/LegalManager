using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using LegalManager.Application.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace LegalManager.Infrastructure.Tribunais;

public class DataJudAdapter : ITribunalAdapter
{
    private readonly HttpClient _http;
    private readonly ILogger<DataJudAdapter> _logger;

    // DataJud endpoint key mapping — tribunal sigla (uppercase) → index suffix
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
        // Derive tribunal from CNJ number segment J.TT if not already determined upstream
        // Try all possible matching indices (search uses the numero directly in each)
        var tribKey = InferirTribunal(numeroCNJ);
        if (tribKey == null)
            return new TribunalConsultaResult(false, null, null, null, []);

        return await ConsultarTribunalAsync(numeroCNJ, tribKey, ct);
    }

    public async Task<TribunalConsultaResult> ConsultarPorTribunalAsync(
        string numeroCNJ, string tribunal, CancellationToken ct = default)
    {
        if (!TribunalIndex.TryGetValue(tribunal.Trim(), out var idx))
            return new TribunalConsultaResult(false, null, null, null, []);

        return await ConsultarTribunalAsync(numeroCNJ, idx, ct);
    }

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
                return new TribunalConsultaResult(false, null, null, null, []);
            }

            var result = await response.Content.ReadFromJsonAsync<DataJudResponse>(
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }, ct);

            if (result?.Hits?.HitsData == null || result.Hits.HitsData.Count == 0)
            {
                _logger.LogInformation("DataJud: Nenhum resultado para {NumCNJ}", numeroCNJ);
                return new TribunalConsultaResult(false, null, null, null, []);
            }

            var source = result.Hits.HitsData[0].Source;
            if (source == null)
                return new TribunalConsultaResult(false, null, null, null, []);

            var movimentos = (source.Movimentos ?? [])
                .Select(m => new TribunalMovimento(
                    Descricao: m.Nome ?? "Movimento sem descrição",
                    Data: m.DataHora,
                    TipoNome: m.Nome ?? "Outro",
                    CodigoCNJ: m.Codigo))
                .OrderBy(m => m.Data)
                .ToList();

            _logger.LogInformation("DataJud: Encontrado processo {NumCNJ} com {Count} movimentos", numeroCNJ, movimentos.Count);

            return new TribunalConsultaResult(
                Encontrado: true,
                NomeTribunal: source.Tribunal,
                Vara: source.OrgaoJulgador?.Nome,
                Comarca: null,
                Movimentos: movimentos);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao consultar DataJud para {NumCNJ}", numeroCNJ);
            return new TribunalConsultaResult(false, null, null, null, []);
        }
    }

    // Infers DataJud index from CNJ number
    // CNJ format: NNNNNNN-DD.AAAA.J.TT.OOOO or NNNNNNNN-DD.AAAA.J.TT.OOOO
    // For 20-digit CNJ: 10845953720228260100 -> parts[4]=8 (J), parts[5]=26 (TT)
    // J=6 + TT=25 -> TJSP, J=8 + TT=26 -> TJSP (DataJud uses non-standard mapping for some courts)
    private static string? InferirTribunal(string numeroCNJ)
    {
        var normalized = numeroCNJ.Replace("-", "").Replace(".", "");
        if (normalized.Length < 13) return null;

        if (normalized.Length == 20)
        {
            if (!int.TryParse(normalized.Substring(11, 1), out var j) ||
                !int.TryParse(normalized.Substring(12, 2), out var tt))
                return null;

            if (j == 6) return MapearTJEstadual(tt);
            if (j == 8 && tt == 26) return "tjsp"; // DataJud non-standard mapping
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

        if (int.TryParse(parts[4], out var j2) && int.TryParse(parts[5], out var tt2))
        {
            if (j2 == 6) return MapearTJEstadual(tt2);
            if (j2 == 8 && tt2 == 26) return "tjsp";
            if (j2 == 8) return "stj";
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

    // Response models
    private sealed class DataJudResponse
    {
        public DataJudHits? Hits { get; set; }
    }

    private sealed class DataJudHits
    {
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
    }

    private sealed class DataJudSource
    {
        public string? Tribunal { get; set; }
        public DataJudOrgao? OrgaoJulgador { get; set; }
        public List<DataJudMovimento>? Movimentos { get; set; }
    }

    private sealed class DataJudOrgao
    {
        public string? Nome { get; set; }
    }

    private sealed class DataJudMovimento
    {
        public int? Codigo { get; set; }
        public string? Nome { get; set; }
        public DateTime DataHora { get; set; }
    }
}
