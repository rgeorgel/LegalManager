using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
using LegalManager.Application.DTOs.Onboarding;
using Microsoft.Extensions.Logging;

namespace LegalManager.Infrastructure.Tribunais;

public class EsajTjspProcessosAdapter
{
    private readonly HttpClient _http;
    private readonly ILogger<EsajTjspProcessosAdapter> _logger;

    public HttpClient HttpClient => _http;

    private static readonly Regex RegexProximaPagina = new(
        @"pageNumber=(\d+)", RegexOptions.Compiled);

    private static string? ExtractQueryParam(string url, string param)
    {
        if (string.IsNullOrEmpty(url)) return null;
        var match = Regex.Match(url, $@"[?&]{param}=([^&]+)");
        return match.Success ? match.Groups[1].Value : null;
    }

    public EsajTjspProcessosAdapter(HttpClient http, ILogger<EsajTjspProcessosAdapter> logger)
    {
        _http = http;
        _logger = logger;
    }

    public async Task<List<ProcessoOabPreviewDto>> BuscarPorOabAsync(
        string numeroOAB, string uf, CancellationToken ct = default)
    {
        var resultados = new List<ProcessoOabPreviewDto>();
        var valorConsulta = $"{numeroOAB.Trim()}{uf.Trim().ToUpperInvariant()}";

        foreach (var grau in new[] { "cpopg", "cposg" })
        {
            var processos = await BuscarNoGrauAsync(grau, valorConsulta, ct);
            resultados.AddRange(processos);
        }

        return resultados
            .GroupBy(p => p.NumeroCNJ)
            .Select(g => g.First())
            .ToList();
    }

    private async Task<List<ProcessoOabPreviewDto>> BuscarNoGrauAsync(
        string grau, string valorConsulta, CancellationToken ct)
    {
        var processos = new List<ProcessoOabPreviewDto>();
        var page = 1;

        while (true)
        {
            var url = $"/{grau}/search.do"
                + $"?conversationId="
                + $"&cbPesquisa=NUMOAB"
                + $"&dadosConsulta.valorConsulta={Uri.EscapeDataString(valorConsulta)}"
                + $"&dadosConsulta.tipoNuProcesso=UNIFICADO"
                + $"&numeroDigitoAnoUnificado="
                + $"&foroNumeroUnificado="
                + $"&dadosConsulta.valorConsultaNuUnificado="
                + $"&pageNumber={page}";

            string html;
            try
            {
                var resp = await _http.GetAsync(url, ct);
                if (!resp.IsSuccessStatusCode)
                {
                    _logger.LogWarning("ESAJ {Grau} retornou {Status} para OAB {OAB}", grau, resp.StatusCode, valorConsulta);
                    break;
                }
                html = await resp.Content.ReadAsStringAsync(ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro ao buscar ESAJ {Grau} OAB {OAB}", grau, valorConsulta);
                break;
            }

            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            // Detectar "não encontrado"
            var semResultado = doc.DocumentNode.SelectSingleNode("//*[contains(@id,'mensagemRetorno')]");
            if (semResultado != null && semResultado.InnerText.Contains("não encontr", StringComparison.OrdinalIgnoreCase))
                break;

            var links = doc.DocumentNode.SelectNodes("//a[@class='linkProcesso']");
            if (links == null || links.Count == 0)
                break;

            var grauLabel = grau == "cpopg" ? "G1" : "G2";

            foreach (var link in links)
            {
                var numero = WebUtility.HtmlDecode(link.InnerText.Trim());
                if (string.IsNullOrWhiteSpace(numero)) continue;

                var href = link.GetAttributeValue("href", "");
                var codigo = ExtractQueryParam(href, "processo.codigo");
                var foro = ExtractQueryParam(href, "processo.foro");

                processos.Add(new ProcessoOabPreviewDto(
                    NumeroCNJ: numero,
                    Tribunal: "TJSP",
                    Vara: null,
                    Classe: null,
                    DataAjuizamento: null,
                    Grau: grauLabel,
                    Codigo: codigo,
                    Foro: foro
                ));
            }

            // Verificar se há próxima página
            var proximaBtn = doc.DocumentNode.SelectSingleNode("//a[contains(@class,'linkProximaPagina') or contains(text(),'Próxima')]");
            if (proximaBtn == null)
                break;

            page++;
            await Task.Delay(300, ct); // pausa gentil ao ESAJ
        }

        return processos;
    }

    public async Task<ProcessoEsajDetalheDto?> ObterDetalhesAsync(
        string numeroProcesso, string grau, CancellationToken ct = default,
        string? codigo = null, string? foro = null)
    {
        var endpoint = grau == "G2" ? "cposg" : "cpopg";
        string url;
        if (!string.IsNullOrEmpty(codigo) && !string.IsNullOrEmpty(foro))
            url = $"/{endpoint}/show.do?processo.codigo={Uri.EscapeDataString(codigo)}&processo.foro={Uri.EscapeDataString(foro)}&uuidCaptcha=";
        else
            url = $"/{endpoint}/show.do?processo.codigo=&processo.foro=&processo.numero={Uri.EscapeDataString(numeroProcesso)}&uuidCaptcha=";

        string html;
        try
        {
            var resp = await _http.GetAsync(url, ct);
            if (!resp.IsSuccessStatusCode) return null;
            html = await resp.Content.ReadAsStringAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao buscar detalhe ESAJ {Numero}", numeroProcesso);
            return null;
        }

        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        // Processo sigiloso: popupSenha visível (sem display:none)
        var popupSenha = doc.GetElementbyId("popupSenha");
        if (popupSenha != null)
        {
            var style = popupSenha.GetAttributeValue("style", "");
            if (!style.Contains("display: none") && !style.Contains("display:none"))
                return new ProcessoEsajDetalheDto { NumeroProcesso = numeroProcesso, Sigiloso = true };
        }

        string Texto(string id) =>
            WebUtility.HtmlDecode(doc.GetElementbyId(id)?.InnerText.Trim() ?? "");

        string TextoInner(string id)
        {
            var node = doc.GetElementbyId(id);
            if (node == null) return "";
            var span = node.SelectSingleNode(".//span[@title]");
            return WebUtility.HtmlDecode(span?.GetAttributeValue("title", "") ?? node.InnerText.Trim());
        }

        var dto = new ProcessoEsajDetalheDto
        {
            NumeroProcesso = numeroProcesso,
            Situacao = Texto("labelSituacaoProcesso"),
            Classe = Texto("classeProcesso"),
            Assunto = Texto("assuntoProcesso"),
            Area = TextoInner("areaProcesso"),
            Foro = Texto("foroProcesso"),
            Vara = Texto("varaProcesso"),
            Juiz = Texto("juizProcesso"),
            DataDistribuicao = Texto("dataHoraDistribuicaoProcesso"),
            ValorAcao = Texto("valorAcaoProcesso"),
            Grau = grau
        };

        // Partes
        var tabelaPartes = doc.GetElementbyId("tablePartesPrincipais");
        if (tabelaPartes != null)
        {
            foreach (var tr in tabelaPartes.SelectNodes(".//tr[contains(@class,'fundo')]") ?? Enumerable.Empty<HtmlNode>())
            {
                var tdLabel = tr.SelectSingleNode(".//span[contains(@class,'tipoDeParticipacao')]");
                var tdNome = tr.SelectSingleNode(".//td[@class='nomeParteEAdvogado']");
                if (tdNome == null) continue;

                var polo = WebUtility.HtmlDecode(tdLabel?.InnerText.Trim() ?? "").Replace("\u00A0", "");
                var parteHtml = tdNome.InnerHtml;
                var partes = parteHtml.Split(new[] { "<br", "</td>" }, StringSplitOptions.RemoveEmptyEntries);

                var nomeDoc = new HtmlDocument();
                nomeDoc.LoadHtml(partes[0]);
                var nomeParte = WebUtility.HtmlDecode(nomeDoc.DocumentNode.InnerText.Trim()).Replace("\u00A0", "");

                var advogadosRaw = partes.Length > 1 ? partes[1] : "";
                var advogadoMatch = System.Text.RegularExpressions.Regex.Match(
                    advogadosRaw, @"Advogado[as]?:\s*([^\s<][^<]*)\s*(?:<|$)",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                var advogados = advogadoMatch.Success
                    ? new List<string> { WebUtility.HtmlDecode(advogadoMatch.Groups[1].Value.Trim()) }
                    : new List<string>();

                dto.Partes.Add(new ParteEsajDto { Nome = nomeParte, Polo = polo, Advogados = advogados });
            }
        }

        // Movimentos
        var movRe = System.Text.RegularExpressions.Regex.Match(
            doc.DocumentNode.OuterHtml,
            @"containerMovimentacao",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (!movRe.Success) return dto;

        var todasLinhas = doc.DocumentNode.SelectNodes(".//tr[contains(@class,'containerMovimentacao')]");
        if (todasLinhas == null) return dto;

        foreach (var tr in todasLinhas)
        {
            var tdData = tr.SelectSingleNode(".//td[@class='dataMovimentacao']");
            var tdDesc = tr.SelectSingleNode(".//td[@class='descricaoMovimentacao']");
            if (tdDesc == null) continue;

            var descHtml = tdDesc.InnerHtml;

            var tituloMatch = System.Text.RegularExpressions.Regex.Match(
                descHtml, @"<a[^>]*>\s*([^\s<][^<]*)\s*<\/a>",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            var titulo = tituloMatch.Success
                ? WebUtility.HtmlDecode(tituloMatch.Groups[1].Value.Trim())
                : "";

            var italicMatch = System.Text.RegularExpressions.Regex.Match(
                descHtml, @"font-style:\s*italic[^>]*>([\s\S]*?)<\/span>",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            var complemento = italicMatch.Success
                ? WebUtility.HtmlDecode(italicMatch.Groups[1].Value.Trim())
                : "";

            var dataStr = WebUtility.HtmlDecode(tdData?.InnerText.Trim() ?? "");
            DateTime.TryParseExact(dataStr, "dd/MM/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dataMovimento);

            dto.Movimentos.Add(new MovimentoEsajDto
            {
                Data = dataMovimento == default ? null : dataMovimento,
                Titulo = titulo,
                Complemento = complemento
            });
        }

        return dto;
    }
}

public class ProcessoEsajDetalheDto
{
    public string NumeroProcesso { get; set; } = "";
    public bool Sigiloso { get; set; }
    public string Situacao { get; set; } = "";
    public string Classe { get; set; } = "";
    public string Assunto { get; set; } = "";
    public string Area { get; set; } = "";
    public string Foro { get; set; } = "";
    public string Vara { get; set; } = "";
    public string Juiz { get; set; } = "";
    public string DataDistribuicao { get; set; } = "";
    public string ValorAcao { get; set; } = "";
    public string Grau { get; set; } = "G1";
    public List<ParteEsajDto> Partes { get; set; } = [];
    public List<MovimentoEsajDto> Movimentos { get; set; } = [];
}

public class ParteEsajDto
{
    public string Nome { get; set; } = "";
    public string Polo { get; set; } = "";
    public List<string> Advogados { get; set; } = [];
}

public class MovimentoEsajDto
{
    public DateTime? Data { get; set; }
    public string Titulo { get; set; } = "";
    public string Complemento { get; set; } = "";
    public string? OrgaoJulgador { get; set; }
}
