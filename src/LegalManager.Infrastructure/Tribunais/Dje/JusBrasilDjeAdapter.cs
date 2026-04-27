using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using LegalManager.Application.Interfaces;
using LegalManager.Domain.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;

namespace LegalManager.Infrastructure.Tribunais.Dje;

public class JusBrasilDjeAdapter : IDjeAdapter
{
    private readonly IPlaywright _playwright;
    private readonly ILogger<JusBrasilDjeAdapter> _logger;
    private const string BaseUrlValue = "https://www.jusbrasil.com.br/diarios";
    private const int MaxDiasLookback = 365;

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

    public string Nome => "JusBrasil - Diários da Justiça";
    public string Sigla => "JB";
    public string BaseUrl => BaseUrlValue;
    public bool SuportaTipo(TipoDje tipo) => tipo == TipoDje.Djus || tipo == TipoDje.Djen;

    public JusBrasilDjeAdapter(ILogger<JusBrasilDjeAdapter> logger)
    {
        _playwright = Microsoft.Playwright.Playwright.CreateAsync().GetAwaiter().GetResult();
        _logger = logger;
    }

    public async Task<DjeConsultaResult> ConsultarPublicacoesAsync(
        DateTime data,
        CancellationToken ct = default)
    {
        try
        {
            return await ConsultarPorNomeAsync("", data.Date, data.Date, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[JusBrasil] Erro ao consultar publicações em {Data}", data);
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
            var agora = DateTime.UtcNow;
            var maxInicio = agora.AddDays(-MaxDiasLookback);
            var inicio = (dataInicio ?? agora.AddDays(-7)) > maxInicio
                ? (dataInicio ?? agora.AddDays(-7))
                : maxInicio;
            var fim = dataFim ?? agora;

            if (inicio > maxInicio)
            {
                _logger.LogInformation(
                    "[JusBrasil] Data inicio {Input} anterior ao limite de {Limite}, usando {Limite}",
                    dataInicio?.ToString("dd/MM/yyyy") ?? "7 dias atrás",
                    maxInicio.ToString("dd/MM/yyyy"),
                    maxInicio.ToString("dd/MM/yyyy"));
            }

            var todasPublicacoes = new List<DjePublicacao>();

            var browser = await _playwright.Chromium.LaunchAsync(
                new BrowserTypeLaunchOptions { Headless = true });

            var context = await browser.NewContextAsync(
                new BrowserNewContextOptions
                {
                    UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36",
                    Locale = "pt-BR"
                });

            var page = await context.NewPageAsync();

            var dataFormatada = inicio.ToString("yyyy-MM-dd");
            var query = Uri.EscapeDataString(nome);
            var searchUrl = string.IsNullOrWhiteSpace(nome)
                ? $"{BaseUrlValue}/DJSP?date={dataFormatada}"
                : $"{BaseUrlValue}/DJSP?q={query}&date={dataFormatada}";

            _logger.LogInformation("[JusBrasil] Acessando: {Url}", searchUrl);

            IResponse? response = null;
            page.Response += (_, r) => { if (r.Url.Contains("jusbrasil")) response = r; };

            await page.GotoAsync(searchUrl,
                new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });

            await page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);
            await Task.Delay(2000, ct);

            var tituloPagina = await page.TitleAsync();
            _logger.LogInformation("[JusBrasil] Página carregada: {Title}", tituloPagina);

            if (tituloPagina.Contains("error-404", StringComparison.OrdinalIgnoreCase) ||
                tituloPagina.Contains("Cloudflare", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("[JusBrasil] Página bloqueada por Cloudflare ou não encontrada");
                await browser.CloseAsync();
                return new DjeConsultaResult(false, "Bloqueado por Cloudflare", []);
            }

            var resultItems = await page.Locator("article, .result-item, .diario-item").AllAsync();

            _logger.LogInformation("[JusBrasil] {Count} resultados encontrados na página", resultItems.Count);

            foreach (var item in resultItems)
            {
                try
                {
                    var texto = await item.InnerTextAsync();
                    var href = await item.Locator("a[href]").First.GetAttributeAsync("href");

                    if (string.IsNullOrWhiteSpace(texto)) continue;

                    var processoMatch = RegexProcesso.Match(texto);
                    var numeroProcesso = processoMatch.Success ? processoMatch.Value : null;

                    var tipo = ExtrairTipo(texto);
                    var prazo = ExtrairPrazo(texto);

                    if (numeroProcesso != null || texto.Contains(nome, StringComparison.OrdinalIgnoreCase))
                    {
                        var pub = new DjePublicacao(
                            Id: Guid.NewGuid().ToString(),
                            SiglaTribunal: "JB",
                            DataPublicacao: inicio,
                            Secao: "DJSP",
                            Pagina: null,
                            Tipo: tipo,
                            Titulo: tipo ?? "Publicação",
                            Conteudo: texto.Length > 5000 ? texto[..5000] : texto,
                            NomesEncontrados: !string.IsNullOrEmpty(nome)
                                ? new List<string> { nome }
                                : new List<string>(),
                            UrlOriginal: href != null ? $"https://www.jusbrasil.com.br{href}" : BaseUrlValue,
                            PrazoDias: prazo);

                        todasPublicacoes.Add(pub);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "[JusBrasil] Erro ao extrair item");
                }
            }

            if (string.IsNullOrWhiteSpace(nome))
            {
                var hashSet = new HashSet<string>();
                var dedup = new List<DjePublicacao>();
                foreach (var pub in todasPublicacoes)
                {
                    var hash = GerarHash(pub);
                    if (hashSet.Add(hash)) dedup.Add(pub);
                }
                todasPublicacoes = dedup;
            }

            _logger.LogInformation("[JusBrasil] {Count} publicações encontradas para '{Nome}'",
                todasPublicacoes.Count, nome);

            await browser.CloseAsync();
            return new DjeConsultaResult(true, null, todasPublicacoes);
        }
        catch (PlaywrightException ex)
        {
            _logger.LogError(ex, "[JusBrasil] Erro Playwright: {Msg}", ex.Message);
            return new DjeConsultaResult(false, ex.Message, []);
        }
        catch (OperationCanceledException)
        {
            return new DjeConsultaResult(false, "Operação cancelada", []);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[JusBrasil] Erro ao consultar por nome: {Nome}", nome);
            return new DjeConsultaResult(false, ex.Message, []);
        }
    }

    public Task<DjeDetalheResult> ObterDetalheAsync(string idPublicacao, CancellationToken ct = default)
    {
        return Task.FromResult(new DjeDetalheResult(true, null, null, idPublicacao));
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

    private static string GerarHash(DjePublicacao pub)
    {
        var input = $"{pub.SiglaTribunal}|{pub.DataPublicacao:yyyy-MM-dd}|{pub.Tipo}|{pub.Conteudo}";
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes)[..32];
    }
}
