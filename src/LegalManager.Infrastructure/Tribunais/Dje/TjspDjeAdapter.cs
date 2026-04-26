using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using LegalManager.Application.Interfaces;
using LegalManager.Domain.Enums;
using Microsoft.Extensions.Logging;
using UglyToad.PdfPig;

namespace LegalManager.Infrastructure.Tribunais.Dje;

public class TjspDjeAdapter : IDjeAdapter
{
    private readonly HttpClient _http;
    private readonly ILogger<TjspDjeAdapter> _logger;
    private readonly string _baseUrl = "https://esaj.tjsp.jus.br";

    private static readonly Regex RegexProcesso = new(
        @"\d{7}-\d{2}\.\d{4}\.\d\.\d{2}\.\d{4}",
        RegexOptions.Compiled);

    private static readonly Regex RegexPrazo = new(
        @"intim[a]?\s*(?:o?\s*)?(?:a(?:s)?)?\s*(?:advogad[oa]|parte)?" +
        @"(?:\s*,?\s*(?:e|ou)\s*(?:advogad[oa]|parte)?)?" +
        @"(?:\s*(?:de|para|no)\s*)?(?:prazo\s*)?(?:de\s*)?(\d+)\s*dias?",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex RegexTipo = new(
        @"\b(intimação|intimação|publicação|sentença|decisão|despacho|acórdão|audiência|audiencia)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public string Nome => "TJSP - Diário da Justiça Eletrônico";
    public string Sigla => "TJSP";
    public string BaseUrl => _baseUrl;
    public bool SuportaTipo(TipoDje tipo) => tipo == TipoDje.Djus;

    public TjspDjeAdapter(HttpClient http, ILogger<TjspDjeAdapter> logger)
    {
        _http = http;
        _logger = logger;
    }

    public async Task<DjeConsultaResult> ConsultarPublicacoesAsync(
        DateTime data,
        CancellationToken ct = default)
    {
        try
        {
            var cadernos = await ListarCadernosAsync(data, ct);
            var todasPublicacoes = new List<DjePublicacao>();

            foreach (var caderno in cadernos)
            {
                var pubs = await BaixarCadernoAsync(caderno, null, ct);
                todasPublicacoes.AddRange(pubs);
            }

            return new DjeConsultaResult(true, null, todasPublicacoes);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[TJSP] Erro ao consultar publicações em {Data}", data);
            return new DjeConsultaResult(false, ex.Message, []);
        }
    }

    public async Task<DjeConsultaResult> ConsultarPorNomeAsync(
        string nome,
        DateTime? dataInicio = null,
        DateTime? dataFim = null,
        CancellationToken ct = default)
    {
        try
        {
            var inicio = dataInicio ?? DateTime.UtcNow.AddDays(-7);
            var fim = dataFim ?? DateTime.UtcNow;

            var todasPublicacoes = new List<DjePublicacao>();
            int diasProcessados = 0;
            var datasJaProcessadas = new HashSet<DateTime>();

            for (var data = inicio.Date; data <= fim.Date; data = data.AddDays(1))
            {
                ct.ThrowIfCancellationRequested();

                var dataParaConsulta = data;

                if (data.DayOfWeek == DayOfWeek.Saturday)
                    dataParaConsulta = data.AddDays(-1);
                else if (data.DayOfWeek == DayOfWeek.Sunday)
                    dataParaConsulta = data.AddDays(-2);

                if (!datasJaProcessadas.Add(dataParaConsulta.Date))
                    continue;

                var cadernos = await ListarCadernosAsync(dataParaConsulta, ct);

                if (cadernos.Count == 0 && dataParaConsulta.DayOfWeek != DayOfWeek.Friday)
                {
                    var diaAnterior = dataParaConsulta.AddDays(-1);
                    while (diaAnterior.DayOfWeek == DayOfWeek.Saturday || diaAnterior.DayOfWeek == DayOfWeek.Sunday)
                        diaAnterior = diaAnterior.AddDays(-1);
                    if (datasJaProcessadas.Add(diaAnterior.Date))
                    {
                        cadernos = await ListarCadernosAsync(diaAnterior, ct);
                    }
                    else
                    {
                        cadernos = [];
                    }
                }

                foreach (var caderno in cadernos)
                {
                    var pubs = await BaixarCadernoAsync(caderno, nome, ct);
                    todasPublicacoes.AddRange(pubs);
                    await Task.Delay(1500, ct);
                }

                diasProcessados++;
                if (diasProcessados % 10 == 0)
                {
                    _logger.LogInformation("[TJSP] Processados {Dias} dias para '{Nome}'",
                        diasProcessados, nome);
                }
            }

            _logger.LogInformation("[TJSP] {Count} publicações encontradas para '{Nome}' em {Dias} dias",
                todasPublicacoes.Count, nome, diasProcessados);

            return new DjeConsultaResult(true, null, todasPublicacoes);
        }
        catch (OperationCanceledException)
        {
            return new DjeConsultaResult(false, "Operação cancelada", []);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[TJSP] Erro ao consultar por nome: {Nome}", nome);
            return new DjeConsultaResult(false, ex.Message, []);
        }
    }

    public Task<DjeDetalheResult> ObterDetalheAsync(string idPublicacao, CancellationToken ct = default)
    {
        return Task.FromResult(new DjeDetalheResult(true, null, null, idPublicacao));
    }

    private async Task<List<TjspCadernoInfo>> ListarCadernosAsync(DateTime data, CancellationToken ct)
    {
        var url = $"{_baseUrl}/cdje/getListaDeCadernos.do?dtDiario={data:dd/MM/yyyy}";
        Exception? ultimaEx = null;

        for (var tentativa = 0; tentativa < 3; tentativa++)
        {
            if (tentativa > 0)
            {
                var espera = TimeSpan.FromSeconds(Math.Pow(2, tentativa));
                _logger.LogInformation("[TJSP] Retry {N} para {Data} após {Wait}s",
                    tentativa, data.ToString("dd/MM/yyyy"), espera.TotalSeconds);
                await Task.Delay(espera, ct);
            }

            try
            {
                using var response = await _http.GetAsync(url, ct);

                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync(ct);
                    return ParseCadernos(json, data);
                }

                if (response.StatusCode == HttpStatusCode.BadRequest)
                {
                    _logger.LogWarning("[TJSP] BadRequest para {Data} (tentativa {N})",
                        data.ToString("dd/MM/yyyy"), tentativa + 1);
                    ultimaEx = new HttpRequestException($"BadRequest for {data:dd/MM/yyyy}");
                    continue;
                }

                _logger.LogWarning("[TJSP] Falha ao listar cadernos para {Data}: {Status}",
                    data.ToString("dd/MM/yyyy"), response.StatusCode);
                return [];
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                ultimaEx = ex;
            }
        }

        _logger.LogWarning("[TJSP] Todas tentativas exauridas para {Data}", data.ToString("dd/MM/yyyy"));
        return [];
    }

    private static List<TjspCadernoInfo> ParseCadernos(string json, DateTime data)
    {
        var cadernos = new List<TjspCadernoInfo>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            foreach (var elem in doc.RootElement.EnumerateArray())
            {
                cadernos.Add(new TjspCadernoInfo(
                    CdVolume: elem.GetProperty("cdVolume").GetInt32(),
                    NuDiario: elem.GetProperty("nuDiario").GetInt32(),
                    CdCaderno: elem.GetProperty("cdCaderno").GetInt32(),
                    NmCaderno: elem.GetProperty("nmCaderno").GetString() ?? "",
                    Data: data));
            }
        }
        catch
        {
        }
        return cadernos;
    }

    private async Task<int> ObterTotalPaginasAsync(TjspCadernoInfo caderno, CancellationToken ct)
    {
        var url = $"{_baseUrl}/cdje/getListaDeSecoes.do?cdVolume={caderno.CdVolume}&nuDiario={caderno.NuDiario}&cdCaderno={caderno.CdCaderno}";
        using var response = await _http.GetAsync(url, ct);

        if (!response.IsSuccessStatusCode) return 0;

        var json = await response.Content.ReadAsStringAsync(ct);
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement[0].GetInt32();
        }
        catch
        {
            return 0;
        }
    }

    private async Task<List<DjePublicacao>> BaixarCadernoAsync(
        TjspCadernoInfo caderno, string? nomeFiltro, CancellationToken ct)
    {
        var totalPaginas = await ObterTotalPaginasAsync(caderno, ct);
        if (totalPaginas == 0)
        {
            _logger.LogWarning("[TJSP] Nenhuma página encontrada para caderno {Nm}", caderno.NmCaderno);
            return [];
        }

        _logger.LogDebug("[TJSP] Baixando caderno '{Nm}' ({Paginas} páginas)",
            caderno.NmCaderno, totalPaginas);

        var textoCompleto = new StringBuilder();

        for (var pagina = 1; pagina <= totalPaginas; pagina++)
        {
            ct.ThrowIfCancellationRequested();

            var textoPagina = await BaixarPaginaAsync(caderno, pagina, ct);
            if (!string.IsNullOrWhiteSpace(textoPagina))
            {
                textoCompleto.AppendLine(textoPagina);
            }

            if (pagina < totalPaginas)
                await Task.Delay(500, ct);
        }

        return ExtrairPublicacoes(textoCompleto.ToString(), caderno, nomeFiltro);
    }

    private async Task<string> BaixarPaginaAsync(TjspCadernoInfo caderno, int pagina, CancellationToken ct)
    {
        try
        {
            var url = $"{_baseUrl}/cdje/getPaginaDoDiario.do?cdVolume={caderno.CdVolume}&nuDiario={caderno.NuDiario}&cdCaderno={caderno.CdCaderno}&nuSeqpagina={pagina}";
            using var response = await _http.GetAsync(url, ct);

            if (!response.IsSuccessStatusCode)
                return "";

            var pdfBytes = await response.Content.ReadAsByteArrayAsync(ct);
            return ExtractTextFromPdf(pdfBytes);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[TJSP] Erro ao baixar página {Pag} do caderno {Nm}", pagina, caderno.NmCaderno);
            return "";
        }
    }

    private static string ExtractTextFromPdf(byte[] pdfBytes)
    {
        try
        {
            using var stream = new MemoryStream(pdfBytes);
            using var document = PdfDocument.Open(stream);
            var sb = new StringBuilder();

            foreach (var page in document.GetPages())
            {
                var text = page.Text;
                if (!string.IsNullOrWhiteSpace(text))
                    sb.AppendLine(text);
            }

            return sb.ToString();
        }
        catch
        {
            return string.Empty;
        }
    }

    private List<DjePublicacao> ExtrairPublicacoes(string texto, TjspCadernoInfo caderno, string? nomeFiltro)
    {
        var publicacoes = new List<DjePublicacao>();
        if (string.IsNullOrWhiteSpace(texto)) return publicacoes;

        var linhas = texto.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        DjePublicacao? publicacaoAtual = null;

        foreach (var linha in linhas)
        {
            var trimmed = linha.Trim();
            if (string.IsNullOrWhiteSpace(trimmed)) continue;

            var processoMatch = RegexProcesso.Match(trimmed);
            var contemNome = !string.IsNullOrEmpty(nomeFiltro) &&
                             trimmed.Contains(nomeFiltro, StringComparison.OrdinalIgnoreCase);

            if (processoMatch.Success || contemNome)
            {
                var numeroProcesso = processoMatch.Success ? processoMatch.Value : null;
                var tipo = ExtrairTipo(trimmed);
                var prazo = ExtrairPrazo(trimmed);
                var nomesEncontrados = !string.IsNullOrEmpty(nomeFiltro) &&
                                       trimmed.Contains(nomeFiltro, StringComparison.OrdinalIgnoreCase)
                    ? new List<string> { nomeFiltro }
                    : new List<string>();

                if (numeroProcesso != null || contemNome)
                {
                    publicacaoAtual = new DjePublicacao(
                        Id: Guid.NewGuid().ToString(),
                        SiglaTribunal: "TJSP",
                        DataPublicacao: caderno.Data,
                        Secao: caderno.NmCaderno,
                        Pagina: null,
                        Tipo: tipo,
                        Titulo: tipo ?? "Publicação",
                        Conteudo: trimmed,
                        NomesEncontrados: nomesEncontrados,
                        UrlOriginal: $"{_baseUrl}/cdje",
                        PrazoDias: prazo);
                }
            }

            if (publicacaoAtual != null && !string.IsNullOrEmpty(nomeFiltro))
            {
                if (publicacaoAtual.NomesEncontrados.Count == 0 &&
                    trimmed.Contains(nomeFiltro, StringComparison.OrdinalIgnoreCase))
                {
                    publicacaoAtual = publicacaoAtual with { NomesEncontrados = new List<string> { nomeFiltro } };
                }

                if (publicacaoAtual.NomesEncontrados.Contains(nomeFiltro) &&
                    publicacaoAtual.Conteudo.Length + trimmed.Length < 5000)
                {
                    publicacaoAtual = publicacaoAtual.WithConteudo(publicacaoAtual.Conteudo + " " + trimmed);
                }
            }
        }

        if (publicacaoAtual != null && publicacaoAtual.NomesEncontrados.Count > 0)
        {
            publicacoes.Add(publicacaoAtual);
        }

        return publicacoes;
    }

    private static string? ExtrairTipo(string texto)
    {
        var match = RegexTipo.Match(texto);
        if (!match.Success) return null;
        var tipo = match.Groups[1].Value.ToLowerInvariant();
        return tipo switch
        {
            "intimação" or "intimacao" => "Intimação",
            "publicação" or "publicacao" => "Publicação",
            "sentença" or "sentenca" => "Sentença",
            "decisão" or "decisao" => "Decisão",
            "despacho" => "Despacho",
            "acórdão" or "acordao" => "Acórdão",
            "audiência" or "audiencia" => "Audiência",
            _ => match.Groups[1].Value
        };
    }

    private static decimal? ExtrairPrazo(string texto)
    {
        var match = RegexPrazo.Match(texto);
        if (!match.Success || !int.TryParse(match.Groups[1].Value, out var dias))
            return null;
        return dias;
    }

    private record TjspCadernoInfo(int CdVolume, int NuDiario, int CdCaderno, string NmCaderno, DateTime Data);
}