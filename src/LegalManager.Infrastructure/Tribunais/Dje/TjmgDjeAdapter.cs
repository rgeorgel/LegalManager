using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
using LegalManager.Application.Interfaces;
using LegalManager.Domain.Enums;
using Microsoft.Extensions.Logging;
using UglyToad.PdfPig;

namespace LegalManager.Infrastructure.Tribunais.Dje;

public class TjmgDjeAdapter : IDjeAdapter
{
    private readonly HttpClient _http;
    private readonly ILogger<TjmgDjeAdapter> _logger;
    private readonly string _baseUrl = "https://www.tjmg.jus.br";

    private static readonly Regex RegexProcesso = new(
        @"\d{7}-\d{2}\.\d{4}\.\d\.\d{2}\.\d{4}",
        RegexOptions.Compiled);

    private static readonly Regex RegexPrazo = new(
        @"intim[oa]?(?:\s+(?:a|o|as|os))?\s*(?:advogad[oa]|parte)?" +
        @"(?:,?\s*(?:e|ou)\s*(?:advogad[oa]|parte))?" +
        @"(?:\s+(?:de|para|no))?\s*(?:prazo\s*)?(?:de\s*)?(\d+)\s*dias?",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex RegexTipo = new(
        @"\b(intimação|intimacao|publicação|sentença|decisão|despacho|acórdão|acordao|audiência|audiencia)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public string Nome => "TJMG - Diário da Justiça Eletrônico";
    public string Sigla => "TJMG";
    public string BaseUrl => _baseUrl;
    public bool SuportaTipo(TipoDje tipo) => tipo == TipoDje.Djus || tipo == TipoDje.Djen;

    public TjmgDjeAdapter(HttpClient http, ILogger<TjmgDjeAdapter> logger)
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
            var diarios = await ListarDiariosPorDataAsync(data, ct);
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
            _logger.LogError(ex, "[TJMG] Erro ao consultar publicações em {Data}", data);
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
            _logger.LogInformation("[TJMG] {Count} edições encontradas entre {Ini} e {Fim}",
                diarios.Count, inicio.ToString("dd/MM/yyyy"), fim.ToString("dd/MM/yyyy"));

            var todas = new List<DjePublicacao>();

            foreach (var diario in diarios)
            {
                var pubs = await BaixarEProcessarPdfAsync(diario, nome, ct);
                todas.AddRange(pubs);
                await Task.Delay(2000, ct);
            }

            _logger.LogInformation("[TJMG] {Count} publicações encontradas para '{Nome}'",
                todas.Count, nome);

            return new DjeConsultaResult(true, null, todas);
        }
        catch (OperationCanceledException)
        {
            return new DjeConsultaResult(false, "Operação cancelada", []);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[TJMG] Erro ao consultar por nome: {Nome}", nome);
            return new DjeConsultaResult(false, ex.Message, []);
        }
    }

    public Task<DjeDetalheResult> ObterDetalheAsync(string idPublicacao, CancellationToken ct = default)
    {
        return Task.FromResult(new DjeDetalheResult(true, null, null, idPublicacao));
    }

    private async Task<List<TjmgDiarioInfo>> ListarDiariosAsync(
        DateTime dataInicio, DateTime dataFim, CancellationToken ct)
    {
        var diarios = new List<TjmgDiarioInfo>();
        var dataAtual = dataFim;

        while (dataAtual >= dataInicio)
        {
            if (dataAtual.DayOfWeek != DayOfWeek.Saturday &&
                dataAtual.DayOfWeek != DayOfWeek.Sunday &&
                !IsFeriado(dataAtual))
            {
                var encontrados = await ListarDiariosPorDataAsync(dataAtual, ct);
                diarios.AddRange(encontrados);
            }

            dataAtual = dataAtual.AddDays(-1);
            await Task.Delay(500, ct);
        }

        return diarios.DistinctBy(d => d.Id).ToList();
    }

    private async Task<List<TjmgDiarioInfo>> ListarDiariosPorDataAsync(DateTime data, CancellationToken ct)
    {
        var diarios = new List<TjmgDiarioInfo>();

        try
        {
            var url = $"{_baseUrl}/portal-tjmg/diarios-de-justica-eletronicos-djen/edicoes";

            var form = new Dictionary<string, string>
            {
                ["data"] = data.ToString("dd/MM/yyyy")
            };

            using var content = new FormUrlEncodedContent(form);
            using var response = await _http.PostAsync(url, content, ct);

            if (!response.IsSuccessStatusCode)
            {
                var diarioUrl = $"{_baseUrl}/portal-tjmg/diarios-de-justica-eletronicos-djen/{data:yyyy-MM-dd}";
                using var r2 = await _http.GetAsync(diarioUrl, ct);

                if (!r2.IsSuccessStatusCode)
                    return diarios;

                var html2 = await r2.Content.ReadAsStringAsync(ct);
                return ParseDiarios(html2);
            }

            var html = await response.Content.ReadAsStringAsync(ct);
            diarios = ParseDiarios(html);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[TJMG] Erro ao listar diários de {Data}", data);
        }

        return diarios;
    }

    private static List<TjmgDiarioInfo> ParseDiarios(string html)
    {
        var diarios = new List<TjmgDiarioInfo>();
        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        var links = doc.DocumentNode.SelectNodes(
            "//a[contains(@href,'download') or contains(@href,'pdf') or contains(@href,'diario')]");
        foreach (var link in links)
        {
            var href = link.GetAttributeValue("href", "");
            if (string.IsNullOrEmpty(href)) continue;

            var idMatch = Regex.Match(href, @"\d{8,}");
            var id = idMatch.Success ? idMatch.Value : Guid.NewGuid().ToString();

            var dataTexto = link.GetAttributeValue("data-data", "") ??
                            link.ParentNode?.GetAttributeValue("data-data", "") ??
                            "";

            DateTime.TryParse(dataTexto, out var data);

            diarios.Add(new TjmgDiarioInfo(
                Id: id,
                Data: data == default ? DateTime.UtcNow : data,
                PdfUrl: href.StartsWith("http") ? href : $"https://www.tjmg.jus.br{href}"));
        }

        return diarios;
    }

    private async Task<List<DjePublicacao>> BaixarEProcessarPdfAsync(
        TjmgDiarioInfo diario, string? nomeFiltro, CancellationToken ct)
    {
        try
        {
            using var response = await _http.GetAsync(diario.PdfUrl, ct);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("[TJMG] Falha ao baixar PDF {Id}: {Status}",
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
            _logger.LogWarning(ex, "[TJMG] Erro ao processar PDF {Id}", diario.Id);
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

    private List<DjePublicacao> ExtrairPublicacoes(string texto, TjmgDiarioInfo diario, string? nomeFiltro)
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
                    SiglaTribunal: "TJMG",
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

    private static bool IsFeriado(DateTime data)
    {
        if (data.DayOfWeek == DayOfWeek.Saturday ||
            data.DayOfWeek == DayOfWeek.Sunday)
            return true;

        var feriados = new[]
        {
            (data.Year, 1, 1),
            (data.Year, 4, 21),
            (data.Year, 5, 1),
            (data.Year, 9, 7),
            (data.Year, 10, 12),
            (data.Year, 11, 2),
            (data.Year, 11, 15),
            (data.Year, 12, 25)
        };

        return feriados.Any(f => new DateTime(f.Item1, f.Item2, f.Item3).Date == data.Date);
    }

    private record TjmgDiarioInfo(string Id, DateTime Data, string PdfUrl);
}
