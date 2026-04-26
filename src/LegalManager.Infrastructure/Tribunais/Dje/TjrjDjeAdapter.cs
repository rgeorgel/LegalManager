using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
using LegalManager.Application.Interfaces;
using LegalManager.Domain.Enums;
using Microsoft.Extensions.Logging;
using UglyToad.PdfPig;

namespace LegalManager.Infrastructure.Tribunais.Dje;

public class TjrjDjeAdapter : IDjeAdapter
{
    private readonly HttpClient _http;
    private readonly ILogger<TjrjDjeAdapter> _logger;
    private readonly string _baseUrl = "https://www.tjrj.jus.br";

    private static readonly Regex RegexProcesso = new(
        @"\d{7}-\d{2}\.\d{4}\.\d\.\d{2}\.\d{4}",
        RegexOptions.Compiled);

    private static readonly Regex RegexPrazo = new(
        @"intim[oa](?:s)?(?:\s*(?:as?|o)?\s*advogad[oa]|parte)?" +
        @"(?:\s*,?\s*(?:e|ou)\s*(?:advogad[oa]|parte))?" +
        @"(?:\s*(?:de|para|no)\s*)?(?:prazo\s*)?(?:de\s*)?(\d+)\s*dias?",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex RegexTipo = new(
        @"\b(intimação|intimacao|publicação|sentença|decisão|despacho|acórdão|acordao|audiência|audiencia)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public string Nome => "TJRJ - Diário da Justiça Eletrônico";
    public string Sigla => "TJRJ";
    public string BaseUrl => _baseUrl;
    public bool SuportaTipo(TipoDje tipo) => tipo == TipoDje.Djus;

    public TjrjDjeAdapter(HttpClient http, ILogger<TjrjDjeAdapter> logger)
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
            var diarios = await ListarDiariosAsync(data, data, ct);
            var todas = new List<DjePublicacao>();

            foreach (var diario in diarios)
            {
                var pubs = await BaixarEProcessarPdfAsync(diario, null, ct);
                todas.AddRange(pubs);
            }

            return new DjeConsultaResult(true, null, todas);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[TJRJ] Erro ao consultar publicações em {Data}", data);
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

            var diarios = await ListarDiariosAsync(inicio, fim, ct);
            _logger.LogInformation("[TJRJ] {Count} edições encontradas entre {Ini} e {Fim}",
                diarios.Count, inicio.ToString("dd/MM/yyyy"), fim.ToString("dd/MM/yyyy"));

            var todas = new List<DjePublicacao>();

            foreach (var diario in diarios)
            {
                var pubs = await BaixarEProcessarPdfAsync(diario, nome, ct);
                todas.AddRange(pubs);
                await Task.Delay(2000, ct);
            }

            _logger.LogInformation("[TJRJ] {Count} publicações encontradas para '{Nome}'",
                todas.Count, nome);

            return new DjeConsultaResult(true, null, todas);
        }
        catch (OperationCanceledException)
        {
            return new DjeConsultaResult(false, "Operação cancelada", []);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[TJRJ] Erro ao consultar por nome: {Nome}", nome);
            return new DjeConsultaResult(false, ex.Message, []);
        }
    }

    public Task<DjeDetalheResult> ObterDetalheAsync(string idPublicacao, CancellationToken ct = default)
    {
        return Task.FromResult(new DjeDetalheResult(true, null, null, idPublicacao));
    }

    private async Task<List<TjrjDiarioInfo>> ListarDiariosAsync(
        DateTime dataInicio, DateTime dataFim, CancellationToken ct)
    {
        var diarios = new List<TjrjDiarioInfo>();

        var url = $"{_baseUrl}/c/portal_publications/open_search";
        var page = 0;

        while (true)
        {
            var form = new Dictionary<string, string>
            {
                ["groupId"] = "10136",
                ["delta"] = "50",
                ["sort"] = "date desc",
                ["cur"] = (page + 1).ToString()
            };

            using var content = new FormUrlEncodedContent(form);
            using var response = await _http.PostAsync(url, content, ct);

            if (!response.IsSuccessStatusCode)
                break;

            var html = await response.Content.ReadAsStringAsync(ct);
            var encontrados = ParseDiarios(html);

            if (encontrados.Count == 0)
                break;

            diarios.AddRange(encontrados.Where(d =>
                d.Data >= dataInicio.Date && d.Data <= dataFim.Date));

            page++;
            await Task.Delay(500, ct);
        }

        return diarios.DistinctBy(d => d.Id).ToList();
    }

    private List<TjrjDiarioInfo> ParseDiarios(string html)
    {
        var diarios = new List<TjrjDiarioInfo>();
        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        var items = doc.DocumentNode.SelectNodes(
            "//*[contains(@class,'publication-item') or contains(@class,'diario-item')] | //tr[@data-id] | //a[contains(@href,'download')]");
        if (items == null) return diarios;

        foreach (var item in items)
        {
            var link = item.SelectSingleNode(".//a[contains(@href,'download')]") ?? item;
            var href = link.GetAttributeValue("href", "");

            if (!href.Contains("download")) continue;

            var idMatch = Regex.Match(href, @"\?id=(\d+)");
            if (!idMatch.Success) continue;

            var dataTexto = item.SelectSingleNode(".//*[contains(@class,'date')]")?.InnerText
                             ?? item.InnerText;

            if (!DateTime.TryParse(dataTexto.Trim(), out var data))
                data = DateTime.UtcNow;

            diarios.Add(new TjrjDiarioInfo(
                Id: idMatch.Groups[1].Value,
                Data: data,
                PdfUrl: href.StartsWith("http") ? href : BaseUrl + href));
        }

        return diarios;
    }

    private async Task<List<DjePublicacao>> BaixarEProcessarPdfAsync(
        TjrjDiarioInfo diario, string? nomeFiltro, CancellationToken ct)
    {
        try
        {
            using var response = await _http.GetAsync(diario.PdfUrl, ct);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("[TJRJ] Falha ao baixar PDF {Id}: {Status}",
                    diario.Id, response.StatusCode);
                return [];
            }

            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var memoryStream = new MemoryStream();
            await stream.CopyToAsync(memoryStream, ct);
            var pdfBytes = memoryStream.ToArray();

            var texto = ExtractTextFromPdf(pdfBytes);
            return ExtrairPublicacoes(texto, diario, nomeFiltro);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[TJRJ] Erro ao processar PDF {Id}", diario.Id);
            return [];
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

    private List<DjePublicacao> ExtrairPublicacoes(string texto, TjrjDiarioInfo diario, string? nomeFiltro)
    {
        var publicacoes = new List<DjePublicacao>();
        var linhas = texto.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        DjePublicacao? atual = null;

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
                var nomes = !string.IsNullOrEmpty(nomeFiltro) &&
                            trimmed.Contains(nomeFiltro, StringComparison.OrdinalIgnoreCase)
                    ? new List<string> { nomeFiltro }
                    : new List<string>();

                atual = new DjePublicacao(
                    Id: Guid.NewGuid().ToString(),
                    SiglaTribunal: "TJRJ",
                    DataPublicacao: diario.Data,
                    Secao: null,
                    Pagina: null,
                    Tipo: tipo,
                    Titulo: tipo ?? "Publicação",
                    Conteudo: trimmed,
                    NomesEncontrados: nomes,
                    UrlOriginal: diario.PdfUrl,
                    PrazoDias: prazo);
            }

            if (atual != null && !string.IsNullOrEmpty(nomeFiltro))
            {
                if (atual.NomesEncontrados.Count == 0 &&
                    trimmed.Contains(nomeFiltro, StringComparison.OrdinalIgnoreCase))
                {
                    atual.NomesEncontrados.Add(nomeFiltro);
                }

                if (atual.NomesEncontrados.Contains(nomeFiltro) &&
                    atual.Conteudo.Length + trimmed.Length < 5000)
                {
                    atual = atual.WithConteudo(atual.Conteudo + " " + trimmed);
                }
            }
        }

        if (atual != null && atual.NomesEncontrados.Count > 0)
        {
            publicacoes.Add(atual);
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

    private record TjrjDiarioInfo(string Id, DateTime Data, string PdfUrl);
}
