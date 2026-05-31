# Fase 2 — TRTs e Outros TJs Estaduais

Tribunais Regionais do Trabalho (TRTs) e tribunais estaduais de médio/grande porte.

---

## Tribunais Desta Fase

| Tribunal | Código | Volume | Prioridade |
|----------|--------|--------|------------|
| TRT-1 (Rio de Janeiro) | TRT1 | Alto | Alta |
| TRT-2 (São Paulo) | TRT2 | Muito Alto | Alta |
| TRT-3 (Minas Gerais) | TRT3 | Alto | Alta |
| TJPR (Paraná) | TJPR | Médio-Alto | Média |
| TJRS (Rio Grande do Sul) | TJRS | Médio-Alto | Média |
| TJSC (Santa Catarina) | TJSC | Médio | Média |

---

## 1. TRT-2 — Tribunal Regional do Trabalho de São Paulo

**Maior TRT do Brasil.** Volume muito alto de publicações.
**DJE:** https://www.trtsp.jus.br — sistema próprio.
**Código:** `TRT2`

### 1.1 Características

O TRT-2 é o maior tribunal trabalhista do Brasil. Utiliza sistema próprio de DJE com busca por nome disponível. Este é o **único tribunal desta fase que oferece busca por nome** de forma mais estruturada.

### 1.2 Estratégia

1. **API de busca pública** — o TRT-2 tem busca por nome de parte/advogado
2. **RSS Feed** — disponibiliza feed RSS para edições
3. **PDF por edição** — downloads diretos de PDF

### 1.3 Implementação

```csharp
public class Trt2DjeAdapter : IDjeAdapter
{
    private readonly HttpClient _http;
    private readonly ILogger<Trt2DjeAdapter> _logger;
    private readonly string _baseUrl = "https://www.trtsp.jus.br";

    public string Nome => "TRT-2 São Paulo - Diário Eletrônico";
    public string Sigla => "TRT2";
    public string BaseUrl => _baseUrl;
    public bool SuportaTipo(TipoDje tipo) => tipo == TipoDje.Djen;

    public async Task<DjeConsultaResult> ConsultarPorNomeAsync(
        string nome,
        DateTime? dataInicio = null,
        DateTime? dataFim = null,
        CancellationToken ct = default)
    {
        var url = $"{_baseUrl}/diario/busca";
        var form = new Dictionary<string, string>
        {
            ["tipoBusca"] = "N",
            ["valorBusca"] = nome,
            ["dataInicio"] = (dataInicio ?? DateTime.UtcNow.AddDays(-7))
                .ToString("dd/MM/yyyy"),
            ["dataFim"] = (dataFim ?? DateTime.UtcNow)
                .ToString("dd/MM/yyyy"),
            ["categoria"] = ""
        };

        var response = await _http.PostAsync(url,
            new FormUrlEncodedContent(form), ct);

        var html = await response.Content.ReadAsStringAsync(ct);
        return ParseResultadoBusca(html, nome);
    }

    public async Task<DjeConsultaResult> ConsultarPublicacoesAsync(
        DateTime data,
        CancellationToken ct = default)
    {
        // Listar todas as publicações do dia (job de rastreamento)
        var url = $"{_baseUrl}/diario/edicao/{data:yyyy-MM-dd}";
        var response = await _http.GetAsync(url, ct);
        var html = await response.Content.ReadAsStringAsync(ct);

        return ParseEdicaoDiaria(html, data);
    }
}
```

### 1.4 Horário de Publicação

TRT-2 publica **de segunda a sexta-feira, a partir das 9h BRT** (12h UTC). Recomendado: **10h BRT (13h UTC)**.

---

## 2. TRT-1 — Tribunal Regional do Trabalho do Rio de Janeiro

**Código:** `TRT1`

### 2.1 Características

O TRT-1 utiliza sistema próprio. Semelhante ao TRT-2, mas com estrutura própria.

### 2.2 Implementação

```csharp
public class Trt1DjeAdapter : IDjeAdapter
{
    private readonly HttpClient _http;
    private readonly ILogger<Trt1DjeAdapter> _logger;
    private readonly string _baseUrl = "https://www.trtrio.jus.br";

    public string Nome => "TRT-1 Rio de Janeiro - Diário Eletrônico";
    public string Sigla => "TRT1";
    public string BaseUrl => _baseUrl;
    public bool SuportaTipo(TipoDje tipo) => tipo == TipoDje.Djen;

    public async Task<DjeConsultaResult> ConsultarPorNomeAsync(
        string nome,
        DateTime? dataInicio = null,
        DateTime? dataFim = null,
        CancellationToken ct = default)
    {
        var url = $"{_baseUrl}/diario/busca?nome={Uri.EscapeDataString(nome)}";

        if (dataInicio.HasValue)
            url += $"&dataIni={dataInicio:dd/MM/yyyy}";
        if (dataFim.HasValue)
            url += $"&dataFim={dataFim:dd/MM/yyyy}";

        var response = await _http.GetAsync(url, ct);
        var html = await response.Content.ReadAsStringAsync(ct);

        return ParseResultado(html, nome);
    }

    public async Task<DjeConsultaResult> ConsultarPublicacoesAsync(
        DateTime data,
        CancellationToken ct = default)
    {
        var url = $"{_baseUrl}/diario/edicao/{data:yyyy-MM-dd}";
        var response = await _http.GetAsync(url, ct);
        var html = await response.Content.ReadAsStringAsync(ct);

        return ParseEdicao(html, data);
    }
}
```

### 2.3 Horário de Publicação

TRT-1 publica **de segunda a sexta-feira, a partir das 9h BRT**. Recomendado: **10h BRT (13h UTC)**.

---

## 3. TRT-3 — Tribunal Regional do Trabalho de Minas Gerais

**Código:** `TRT3`

### 3.1 Características

O TRT-3 (Minas Gerais) é o terceiro maior TRT. Utiliza sistema próprio.

### 3.2 Implementação

```csharp
public class Trt3DjeAdapter : IDjeAdapter
{
    private readonly HttpClient _http;
    private readonly ILogger<Trt3DjeAdapter> _logger;
    private readonly string _baseUrl = "https://www.trt3.jus.br";

    public string Nome => "TRT-3 Minas Gerais - Diário Eletrônico";
    public string Sigla => "TRT3";
    public string BaseUrl => _baseUrl;
    public bool SuportaTipo(TipoDje tipo) => tipo == TipoDje.Djen;

    public async Task<DjeConsultaResult> ConsultarPorNomeAsync(
        string nome,
        DateTime? dataInicio = null,
        DateTime? dataFim = null,
        CancellationToken ct = default)
    {
        var url = $"{_baseUrl}/diario/busca";

        var form = new Dictionary<string, string>
        {
            ["palavra"] = nome,
            ["dataPublicacaoInicio"] = (dataInicio ?? DateTime.UtcNow.AddDays(-7))
                .ToString("dd/MM/yyyy"),
            ["dataPublicacaoFim"] = (dataFim ?? DateTime.UtcNow)
                .ToString("dd/MM/yyyy")
        };

        var response = await _http.PostAsync(url,
            new FormUrlEncodedContent(form), ct);

        var html = await response.Content.ReadAsStringAsync(ct);
        return ParseResultado(html, nome);
    }
}
```

### 3.3 Horário de Publicação

TRT-3 publica **de segunda a sexta-feira, a partir das 9h BRT**. Recomendado: **10h BRT (13h UTC)**.

---

## 4. TJPR — Tribunal de Justiça do Paraná

**Código:** `TJPR`

### 4.1 Características

O TJPR utiliza sistema próprio de DJE. Portal bem estruturado com sistema de busca.

### 4.2 Implementação

```csharp
public class TjprDjeAdapter : IDjeAdapter
{
    private readonly HttpClient _http;
    private readonly ILogger<TjprDjeAdapter> _logger;
    private readonly string _baseUrl = "https://www.tjpr.jus.br";

    public string Nome => "TJPR - Diário da Justiça Eletrônico";
    public string Sigla => "TJPR";
    public string BaseUrl => _baseUrl;
    public bool SuportaTipo(TipoDje tipo) => tipo == TipoDje.Djus;

    public async Task<DjeConsultaResult> ConsultarPorNomeAsync(
        string nome,
        DateTime? dataInicio = null,
        DateTime? dataFim = null,
        CancellationToken ct = default)
    {
        // TJPR tem portal estruturado com busca
        var url = $"{_baseUrl}/diario-oficial/consulta";
        var form = new Dictionary<string, string>
        {
            ["nome"] = nome,
            ["dataInicio"] = (dataInicio ?? DateTime.UtcNow.AddDays(-7))
                .ToString("dd/MM/yyyy"),
            ["dataFim"] = (dataFim ?? DateTime.UtcNow)
                .ToString("dd/MM/yyyy"),
            ["tipo"] = "parte"
        };

        var response = await _http.PostAsync(url,
            new FormUrlEncodedContent(form), ct);

        var html = await response.Content.ReadAsStringAsync(ct);
        return ParseResultado(html, nome);
    }

    public async Task<DjeConsultaResult> ConsultarPublicacoesAsync(
        DateTime data,
        CancellationToken ct = default)
    {
        // Listar edições do dia
        var url = $"{_baseUrl}/diario-oficial/edicao/{data:yyyy-MM-dd}";
        var response = await _http.GetAsync(url, ct);
        var html = await response.Content.ReadAsStringAsync(ct);

        var publicacoes = new List<DjePublicacao>();

        // Parsing do HTML para extrair publicações
        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        foreach (var row in doc.QuerySelectorAll("table.publicacoes tr"))
        {
            var pub = ParseRow(row, data);
            if (pub != null)
                publicacoes.Add(pub);
        }

        return new DjeConsultaResult(true, null, publicacoes);
    }
}
```

### 4.3 Horário de Publicação

TJPR publica **de segunda a sexta-feira, a partir das 8h BRT** (11h UTC). Recomendado: **9h BRT (12h UTC)**.

---

## 5. TJRS — Tribunal de Justiça do Rio Grande do Sul

**Código:** `TJRS`

### 5.1 Características

O TJRS é um dos maiores tribunais do Sul do Brasil. Utiliza sistema próprio com portal bem documentado.

### 5.2 Implementação

```csharp
public class TjrsDjeAdapter : IDjeAdapter
{
    private readonly HttpClient _http;
    private readonly ILogger<TjrsDjeAdapter> _logger;
    private readonly string _baseUrl = "https://www.tjrs.jus.br";

    public string Nome => "TJRS - Diário da Justiça Eletrônico";
    public string Sigla => "TJRS";
    public string BaseUrl => _baseUrl;
    public bool SuportaTipo(TipoDje tipo) => tipo == TipoDje.Djus;

    public async Task<DjeConsultaResult> ConsultarPorNomeAsync(
        string nome,
        DateTime? dataInicio = null,
        DateTime? dataFim = null,
        CancellationToken ct = default)
    {
        // TJRS disponibiliza busca pública
        var url = $"{_baseUrl}/busca/diario";
        var form = new Dictionary<string, string>
        {
            ["termo"] = nome,
            ["dataInicial"] = (dataInicio ?? DateTime.UtcNow.AddDays(-7))
                .ToString("dd/MM/yyyy"),
            ["dataFinal"] = (dataFim ?? DateTime.UtcNow)
                .ToString("dd/MM/yyyy"),
            ["tipoBusca"] = "N"  // Nome
        };

        var response = await _http.PostAsync(url,
            new FormUrlEncodedContent(form), ct);

        var html = await response.Content.ReadAsStringAsync(ct);
        return ParseResultado(html, nome);
    }
}
```

### 5.3 Horário de Publicação

TJRS publica **de segunda a sexta-feira, a partir das 8h BRT** (11h UTC). Recomendado: **9h BRT (12h UTC)**.

---

## 6. TJSC — Tribunal de Justiça de Santa Catarina

**Código:** `TJSC`

### 6.1 Características

O TJSC utiliza sistema próprio de DJE. Portal bem estruturado.

### 6.2 Implementação

```csharp
public class TjscDjeAdapter : IDjeAdapter
{
    private readonly HttpClient _http;
    private readonly ILogger<TjscDjeAdapter> _logger;
    private readonly string _baseUrl = "https://www.tjsc.jus.br";

    public string Nome => "TJSC - Diário da Justiça Eletrônico";
    public string Sigla => "TJSC";
    public string BaseUrl => _baseUrl;
    public bool SuportaTipo(TipoDje tipo) => tipo == TipoDje.Djus;

    public async Task<DjeConsultaResult> ConsultarPorNomeAsync(
        string nome,
        DateTime? dataInicio = null,
        DateTime? dataFim = null,
        CancellationToken ct = default)
    {
        var url = $"{_baseUrl}/diario-oficial/busca";
        var form = new Dictionary<string, string>
        {
            ["nomeBusca"] = nome,
            ["dataIni"] = (dataInicio ?? DateTime.UtcNow.AddDays(-7))
                .ToString("dd/MM/yyyy"),
            ["dataFim"] = (dataFim ?? DateTime.UtcNow)
                .ToString("dd/MM/yyyy")
        };

        var response = await _http.PostAsync(url,
            new FormUrlEncodedContent(form), ct);

        var html = await response.Content.ReadAsStringAsync(ct);
        return ParseResultado(html, nome);
    }
}
```

### 6.3 Horário de Publicação

TJSC publica **de segunda a sexta-feira, a partir das 8h BRT** (11h UTC). Recomendado: **9h BRT (12h UTC)**.

---

## 7. Padrões Comuns — Fase 2

### 7.1 Parsing de HTML Genérico

```csharp
protected List<DjePublicacao> ParseHtmlTable(string html, DateTime dataEdicao)
{
    var doc = new HtmlDocument();
    doc.LoadHtml(html);

    var publicacoes = new List<DjePublicacao>();

    var rows = doc.QuerySelectorAll("table tr, .publicacao-row, .result-item");
    foreach (var row in rows)
    {
        var cells = row.QuerySelectorAll("td, .cell");
        if (cells.Count < 3) continue;

        var texto = row.InnerText;
        if (string.IsNullOrWhiteSpace(texto)) continue;

        var processo = ExtractProcesso(texto);
        var tipo = ExtractTipo(texto);
        var conteudo = ExtractConteudo(texto);

        publicacoes.Add(new DjePublicacao(
            Id: Guid.NewGuid().ToString(),
            Tribunal: Sigla,
            DataPublicacao: dataEdicao,
            Secao: cells.Count > 2 ? cells[2].InnerText.Trim() : "",
            Pagina: cells.Count > 3 ? cells[3].InnerText.Trim() : "",
            Tipo: tipo,
            Titulo: tipo,
            Conteudo: conteudo,
            NomesEncontrados: new List<string>(),
            UrlOriginal: "",
            PrazoDias: ExtractPrazo(conteudo)));
    }

    return publicacoes;
}
```

### 7.2 Extração de Número de Processo

```csharp
protected static readonly Regex RegexCNPJ = new(
    @"\d{7}-\d{2}\.\d{4}\.\d\.\d{2}\.\d{4}",
    RegexOptions.Compiled);

protected static readonly Regex RegexCPF = new(
    @"\d{3}\.\d{3}\.\d{3}-\d{2}",
    RegexOptions.Compiled);

protected static string? ExtractProcesso(string texto)
{
    var match = RegexCNPJ.Match(texto);
    return match.Success ? match.Value : null;
}
```

### 7.3 Scheduling — Fase 2

| Adapter | Horário Recomendado (BRT) | Horário UTC |
|---------|---------------------------|-------------|
| Trt2DjeAdapter | 10:00 | 13:00 |
| Trt1DjeAdapter | 10:30 | 13:30 |
| Trt3DjeAdapter | 10:30 | 13:30 |
| TjprDjeAdapter | 09:00 | 12:00 |
| TjrsDjeAdapter | 09:30 | 12:30 |
| TjscDjeAdapter | 09:30 | 12:30 |

---

## 8. Estimativa de Esforço

| Adapter | Complexidade | Tempo Estimado |
|---------|-------------|----------------|
| Trt2DjeAdapter | Média | 1-2 dias |
| Trt1DjeAdapter | Média | 1-2 dias |
| Trt3DjeAdapter | Média | 1-2 dias |
| TjprDjeAdapter | Alta | 2 dias |
| TjrsDjeAdapter | Alta | 2 dias |
| TjscDjeAdapter | Alta | 2 dias |
| **Total Fase 2** | | **10-14 dias** |
