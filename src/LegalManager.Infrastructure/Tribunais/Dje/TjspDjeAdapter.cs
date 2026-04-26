using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Web;
using HtmlAgilityPack;
using LegalManager.Application.Interfaces;
using LegalManager.Domain.Enums;
using Microsoft.Extensions.Logging;
using UglyToad.PdfPig;

namespace LegalManager.Infrastructure.Tribunais.Dje;

public class TjspDjeAdapter : IDjeAdapter
{
    private readonly HttpClient _http;
    private readonly ILogger<TjspDjeAdapter> _logger;
    private readonly string _baseUrl = "https://dje.tjsp.jus.br";

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
            var diarios = await ListarDiariosAsync(data, data, ct);
            var todasPublicacoes = new List<DjePublicacao>();

            foreach (var diario in diarios)
            {
                var pubs = await BaixarEProcessarPdfAsync(diario, null, ct);
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

            var diarios = await ListarDiariosAsync(inicio, fim, ct);
            _logger.LogInformation("[TJSP] {Count} edições encontradas para {Nome} entre {Ini} e {Fim}",
                diarios.Count, nome, inicio.ToString("dd/MM/yyyy"), fim.ToString("dd/MM/yyyy"));

            var todasPublicacoes = new List<DjePublicacao>();

            foreach (var diario in diarios)
            {
                var pubs = await BaixarEProcessarPdfAsync(diario, nome, ct);
                todasPublicacoes.AddRange(pubs);

                await Task.Delay(2500, ct);
            }

            _logger.LogInformation("[TJSP] {Count} publicações encontradas para '{Nome}'",
                todasPublicacoes.Count, nome);

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

    private async Task<List<TjspDiarioInfo>> ListarDiariosAsync(
        DateTime dataInicio, DateTime dataFim, CancellationToken ct)
    {
        var url = $"{_baseUrl}/cdje/consultaDiarioDigital";

        var form = new Dictionary<string, string>
        {
            ["dataIni"] = dataInicio.ToString("dd/MM/yyyy"),
            ["dataFim"] = dataFim.ToString("dd/MM/yyyy"),
            ["submit"] = "Pesquisar"
        };

        using var content = new FormUrlEncodedContent(form);
        using var response = await _http.PostAsync(url, content, ct);
        response.EnsureSuccessStatusCode();

        var html = await response.Content.ReadAsStringAsync(ct);
        return ParseDiarios(html);
    }

    private static List<TjspDiarioInfo> ParseDiarios(string html)
    {
        var diarios = new List<TjspDiarioInfo>();
        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        var links = doc.DocumentNode.SelectNodes("//a[contains(@href,'downloadDiarioDigital')]");
        if (links == null) return diarios;

        foreach (var link in links)
        {
            var href = link.GetAttributeValue("href", "");
            var idMatch = Regex.Match(href, @"id=(\d+)");
            if (!idMatch.Success) continue;

            var parentRow = link.ParentNode?.ParentNode;
            var cells = parentRow?.SelectNodes(".//td");
            if (cells == null || cells.Count < 2) continue;

            var dataTexto = cells[0].InnerText.Trim();
            if (!DateTime.TryParse(dataTexto, out var data)) continue;

            diarios.Add(new TjspDiarioInfo(
                Id: idMatch.Groups[1].Value,
                Data: data,
                PdfUrl: $"{link.GetAttributeValue("href", "")}"));
        }

        return diarios;
    }

    private async Task<List<DjePublicacao>> BaixarEProcessarPdfAsync(
        TjspDiarioInfo diario, string? nomeFiltro, CancellationToken ct)
    {
        try
        {
            var url = $"{_baseUrl}/cdje/downloadDiarioDigital?id={diario.Id}&tipo=P";
            using var response = await _http.GetAsync(url, ct);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("[TJSP] Falha ao baixar PDF {Id}: {Status}",
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
            _logger.LogWarning(ex, "[TJSP] Erro ao processar PDF {Id}", diario.Id);
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

    private List<DjePublicacao> ExtrairPublicacoes(string texto, TjspDiarioInfo diario, string? nomeFiltro)
    {
        var publicacoes = new List<DjePublicacao>();
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
                        DataPublicacao: diario.Data,
                        Secao: null,
                        Pagina: null,
                        Tipo: tipo,
                        Titulo: tipo ?? "Publicação",
                        Conteudo: trimmed,
                        NomesEncontrados: nomesEncontrados,
                        UrlOriginal: $"{_baseUrl}/cdje/consultaDiarioDigital?id={diario.Id}",
                        PrazoDias: prazo);
                }
            }

            if (publicacaoAtual != null && !string.IsNullOrEmpty(nomeFiltro))
            {
                if (publicacaoAtual.NomesEncontrados.Count == 0 &&
                    trimmed.Contains(nomeFiltro, StringComparison.OrdinalIgnoreCase))
                {
                    publicacaoAtual.NomesEncontrados.Add(nomeFiltro);
                }

                if (publicacaoAtual.NomesEncontrados.Contains(nomeFiltro) &&
                    publicacaoAtual.Conteudo.Length + trimmed.Length < 5000)
                {
                    publicacaoAtual = publicacaoAtual.WithConteudo(publicacaoAtual.Conteudo + " " + trimmed);
                }
            }
        }

        if (publicacaoAtual != null &&
            publicacaoAtual.NomesEncontrados.Count > 0)
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

    private static string GerarHashDeduplicacao(DjePublicacao pub)
    {
        var input = $"TJSP|{pub.DataPublicacao:yyyyMMdd}|{pub.Tipo}|{pub.Titulo}";
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes)[..32];
    }

    private record TjspDiarioInfo(string Id, DateTime Data, string PdfUrl);
}
