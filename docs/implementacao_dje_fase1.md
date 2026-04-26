# Fase 1 — TJSP, TJRJ, TJMG

Tribunais de maior volume do Brasil. Prioridade máxima.

---

## 1. TJSP — Tribunal de Justiça de São Paulo

**Volume estimado:** ~40.000 processos novos/mês, maior tribunal do Brasil.
**DJE:** https://dje.tjsp.jus.br
**Código:** `TJSP`

### 1.1 Estrutura do Sistema

O TJSP utiliza uma aplicação Java Server Faces (JSF) com backend Elasticsearch. A URL de consulta pública é `https://dje.tjsp.jus.br/cdje/consultaDiarioDigital.jsf`.

### 1.2 API Detetada

O frontend faz requisições para endpoints REST internos:

```
Base: https://dje.tjsp.jus.br
Índice Elasticsearch: /api_publica_tjsp/_search
```

Formato da requisição (JSON):
```json
{
  "query": {
    "bool": {
      "must": [
        { "match": { "numeroProcesso": "0000001-00.2024.8.26.0001" } }
      ]
    }
  },
  "size": 100,
  "from": 0,
  "sort": [{ "dataPublicacao": "desc" }]
}
```

**Importante:** O Elasticsearch expõe `movimentacoes[]` com `data`, `nome`, `codigo`. Cada movimentação é um "movimento" e não uma publicação DJE completa. O TJSP não possui um endpoint público de DJE por nome — é preciso fazer scraping do HTML.

### 1.3 Consulta por Nome — Estratégia

O TJSP **NÃO** oferece busca por nome no DJE via API pública. A estratégia é:

1. **Scraping do HTML** — `https://dje.tjsp.jus.br/cdje/consultaDiarioDigital.jsf`
   - Parâmetros POST: `nomePesquisa`, `dataIni`, `dataFim`
   - Retorna tabela com número do diário, data e link para PDF

2. **Download do PDF** — Cada edição do DJE é um PDF
   - URL padrão: `https://dje.tjsp.jus.br/cdje/downloadDiarioDigital?id={id}&tipo=P`
   - O PDF contém todas as publicações do dia

3. **Extração de texto** — Usar `PdfPig` ou `iTextSharp`:
   ```
   1. Ler cada página do PDF
   2. Buscar linhas que contenham o NomeCaptura
   3. Extrair: número do processo, tipo de publicação, data, texto relevante
   ```

### 1.4 Implementação Proposta

```csharp
public class TjspDjeAdapter : IDjeAdapter
{
    private readonly HttpClient _http;
    private readonly ILogger<TjspDjeAdapter> _logger;
    private readonly string _baseUrl = "https://dje.tjsp.jus.br";

    public string Nome => "TJSP - Diário da Justiça Eletrônico";
    public string Sigla => "TJSP";
    public string BaseUrl => _baseUrl;
    public bool SuportaTipo(TipoDje tipo) => tipo == TipoDje.Djus;

    public async Task<DjeConsultaResult> ConsultarPorNomeAsync(
        string nome,
        DateTime? dataInicio = null,
        DateTime? dataFim = null,
        CancellationToken ct = default)
    {
        var publicacoes = new List<DjePublicacao>();
        var dataIni = dataInicio ?? DateTime.UtcNow.AddDays(-7);
        var dataF = dataFim ?? DateTime.UtcNow;

        // 1. Listar diários disponíveis no período
        var diarios = await ListarDiariosAsync(dataIni, dataF, ct);

        foreach (var diario in diarios)
        {
            // 2. Download do PDF
            var pdfBytes = await DownloadPdfAsync(diario.Id, ct);
            var texto = await ExtrairTextoPdfAsync(pdfBytes, ct);

            // 3. Buscar linhas com o nome
            var linhas = texto.Split('\n');
            foreach (var linha in linhas)
            {
                if (linha.Contains(nome, StringComparison.OrdinalIgnoreCase))
                {
                    var pub = ParsePublicacao(linha, diario);
                    if (pub != null)
                        publicacoes.Add(pub);
                }
            }
        }

        return new DjeConsultaResult(true, null, publicacoes);
    }

    private async Task<List<TjspDiario>> ListarDiariosAsync(
        DateTime dataInicio, DateTime dataFim, CancellationToken ct)
    {
        var form = new Dictionary<string, string>
        {
            ["dataIni"] = dataInicio.ToString("dd/MM/yyyy"),
            ["dataFim"] = dataFim.ToString("dd/MM/yyyy"),
            ["submit"] = "Pesquisar"
        };

        var content = new FormUrlEncodedContent(form);
        var response = await _http.PostAsync(
            $"{_baseUrl}/cdje/consultaDiarioDigital.jsf", content, ct);

        var html = await response.Content.ReadAsStringAsync(ct);
        return ParseDiarios(html);
    }
}
```

### 1.5 Horário de Publicação

O TJSP publica **de segunda a sexta-feira, a partir das 8h BRT** (11h UTC). Recomendado rodar o job às **9h BRT (12h UTC)** para garantir que o diário do dia já esteja disponível.

### 1.6 Regex de Extração

Para identificar processos no texto do PDF:
```csharp
private static readonly Regex RegexProcesso = new(
    @"\d{7}-\d{2}\.\d{4}\.\d\.\d{2}\.\d{4}",
    RegexOptions.Compiled);

// Para intimação com prazo:
private static readonly Regex RegexPrazo = new(
    @"intim.*?(?:advogad[ao]|parte).*?(?:no\s*)?prazo\s*(?:de\s*)?(\d+)\s*dias?",
    RegexOptions.IgnoreCase | RegexOptions.Compiled);
```

### 1.7 Limitação known

- Não há API pública oficial — scraping é a única opção
- PDFs podem ser grandes (várias centenas de páginas)
- O TJSP pode bloquear requisições excessivas — implementar delays de 2-5s entre downloads
- É necessário verificar termos de uso para uso automatizado

---

## 2. TJRJ — Tribunal de Justiça do Rio de Janeiro

**Volume estimado:** ~15.000 processos novos/mês, segundo maior do Brasil.
**DJE:** https://www.tjrj.jus.br — usa portal Liferay
**Código:** `TJRJ`

### 2.1 Estrutura do Sistema

O TJRJ utiliza **Liferay DXP** como plataforma. O DJE está integrado ao portal principal em `https://www.tjrj.jus.br`.

O sistema detecta que o DJE está acessível através de estrutura Liferay com páginas dedicadas para `consulta.do?tipo=dje`.

### 2.2 API Detetada

O frontend Liferay utiliza AJAX para carregar listas de diários:

```
Base: https://www.tjrj.jus.br
Endpoint: /c/portal_publications/open_search?groupId={groupId}&delta=50&sort=date desc
```

O conteúdo das publicações é renderizado em HTML dentro do portal.

### 2.3 Consulta por Nome — Estratégia

Semelhante ao TJSP, o TJRJ também **não possui API pública para busca por nome**. Estratégia:

1. **Scraping via portal Liferay** — buscar lista de edições
2. **Download de PDFs** — o TJRJ disponibiliza PDFs por edição
3. **Extração de texto** — PdfPig para extrair conteúdo

### 2.4 Implementação Proposta

```csharp
public class TjrjDjeAdapter : IDjeAdapter
{
    private readonly HttpClient _http;
    private readonly ILogger<TjrjDjeAdapter> _logger;
    private readonly string _baseUrl = "https://www.tjrj.jus.br";

    public string Nome => "TJRJ - Diário da Justiça Eletrônico";
    public string Sigla => "TJRJ";
    public string BaseUrl => _baseUrl;
    public bool SuportaTipo(TipoDje tipo) => tipo == TipoDje.Djus;

    public async Task<DjeConsultaResult> ConsultarPorNomeAsync(
        string nome,
        DateTime? dataInicio = null,
        DateTime? dataFim = null,
        CancellationToken ct = default)
    {
        // O TJRJ não tem endpoint de busca por nome
        // Estratégia: buscar edições por data e vasculhar PDFs
        var publicacoes = new List<DjePublicacao>();

        var diarios = await ListarEditionsAsync(
            dataInicio ?? DateTime.UtcNow.AddDays(-7),
            dataFim ?? DateTime.UtcNow,
            ct);

        foreach (var diario in diarios)
        {
            var pdfTexto = await DownloadAndExtractAsync(diario.PdfUrl, ct);
            var matches = ExtrairMatches(pdfTexto, nome);

            foreach (var match in matches)
            {
                publicacoes.Add(new DjePublicacao(
                    Id: match.Id,
                    Tribunal: "TJRJ",
                    DataPublicacao: diario.DataPublicacao,
                    Secao: match.Secao,
                    Pagina: match.Pagina,
                    Tipo: match.Tipo,
                    Titulo: match.Titulo,
                    Conteudo: match.Texto,
                    NomesEncontrados: new List<string> { nome },
                    UrlOriginal: match.UrlOriginal,
                    PrazoDias: ExtrairPrazo(match.Texto)));
            }
        }

        return new DjeConsultaResult(true, null, publicacoes);
    }

    private async Task<string> DownloadAndExtractAsync(
        string pdfUrl, CancellationToken ct)
    {
        var bytes = await _http.GetByteArrayAsync(pdfUrl, ct);
        // Usar PdfPig para extração
        using var stream = new MemoryStream(bytes);
        var texto = PdfExtractor.ExtractText(stream);
        return texto;
    }
}
```

### 2.5 Horário de Publicação

O TJRJ publica **de segunda a sexta-feira, a partir das 9h BRT** (12h UTC). Recomendado rodar às **10h BRT (13h UTC)**.

### 2.6 Estrutura do Nome do Processo

O TJRJ utiliza o formato CNJ padrão:
```
NNNNNNN-DD.AAAA.J.TT.OOOO
```
Exatamente igual ao padrão nacional.

---

## 3. TJMG — Tribunal de Justiça de Minas Gerais

**Volume estimado:** ~25.000 processos novos/mês, terceiro maior.
**DJE:** https://www.tjmg.jus.br — sistema próprio (Lumis)
**Código:** `TJMG`

### 3.1 Estrutura do Sistema

O TJMG utiliza **Lumis XP** como plataforma de portal. O DJE é mencionado explicitamente no menu: "DJe, DJEN e Domicílio Judicial" (`https://www.tjmg.jus.br/portal-tjmg/diarios-de-justica-eletronicos-djen/`).

### 3.2 API Detetada

O sistema Lumis expõe conteúdo via endpoints类似 a:

```
Base: https://www.tjmg.jus.br
Busca: /portal-tjmg/diarios-de-justica-eletronicos-djen/busca?termo={nome}
```

O conteúdo da página indica que há URLs de busca específicas para DJEN (Diário da Justiça Eletrônico Nacional) e DJE estadual.

### 3.3 Consulta por Nome — Estratégia

O TJMG também não possui API pública documentada. Estratégia:

1. **Scraping do portal** — buscar por nome na página de DJE
2. **Download de PDFs** — disponíveis por data/edição
3. **Extração com PdfPig**

### 3.4 Implementação Proposta

```csharp
public class TjmgDjeAdapter : IDjeAdapter
{
    private readonly HttpClient _http;
    private readonly ILogger<TjmgDjeAdapter> _logger;
    private readonly string _baseUrl = "https://www.tjmg.jus.br";

    public string Nome => "TJMG - Diário da Justiça Eletrônico";
    public string Sigla => "TJMG";
    public string BaseUrl => _baseUrl;
    public bool SuportaTipo(TipoDje tipo)
        => tipo == TipoDje.Djus || tipo == TipoDje.Djen;

    public async Task<DjeConsultaResult> ConsultarPorNomeAsync(
        string nome,
        DateTime? dataInicio = null,
        DateTime? dataFim = null,
        CancellationToken ct = default)
    {
        // Busca por nome no portal DJE do TJMG
        var url = $"{_baseUrl}/portal-tjmg/diarios-de-justica-eletronicos-djen/busca";
        var form = new Dictionary<string, string>
        {
            ["termo"] = nome,
            ["dataInicio"] = (dataInicio ?? DateTime.UtcNow.AddDays(-7))
                .ToString("dd/MM/yyyy"),
            ["dataFim"] = (dataFim ?? DateTime.UtcNow)
                .ToString("dd/MM/yyyy"),
            ["tipoBusca"] = "nome"
        };

        var response = await _http.PostAsync(url,
            new FormUrlEncodedContent(form), ct);

        var html = await response.Content.ReadAsStringAsync(ct);
        return ParseResultado(html);
    }

    public async Task<DjeConsultaResult> ConsultarPublicacoesAsync(
        DateTime data,
        CancellationToken ct = default)
    {
        // Listar todas as publicações de uma data específica
        // Útil para o job de captura diária
        var diarios = await ListarDiariosPorDataAsync(data, ct);
        var publicacoes = new List<DjePublicacao>();

        foreach (var diario in diarios)
        {
            var texto = await DownloadPdfAsync(diario.PdfUrl, ct);
            publicacoes.AddRange(ParsePublicacoes(texto, diario));
        }

        return new DjeConsultaResult(true, null, publicacoes);
    }
}
```

### 3.5 Horário de Publicação

O TJMG publica **de segunda a sexta-feira, a partir das 8h BRT** (11h UTC). Recomendado rodar às **9h30 BRT (12h30 UTC)**.

### 3.6 Notas Adicionais

- O TJMG tem integração com **DJEN** (Diário da Justiça Eletrônico Nacional) que agrega publicações de todos os tribunais de Minas Gerais
- O sistema Lumis pode ter controle de sessão/captcha — verificar necessidade de cookies
- Publicações do TJMG seguem formato semelhante ao TJSP

---

## 4. Validações Comuns à Fase 1

### 4.1 Detecção de Feriado

```csharp
private bool IsFeriado(DateTime data, string estado)
{
    // Feriados fixos + móveis ( Carnaval, Páscoa, etc.)
    if (data.DayOfWeek == DayOfWeek.Saturday ||
        data.DayOfWeek == DayOfWeek.Sunday)
        return true;

    // Feriados nacionais fixos
    var feriados = new[] {
        new DateTime(data.Year, 1, 1),   // Ano Novo
        new DateTime(data.Year, 4, 21),  // Tiradentes
        new DateTime(data.Year, 5, 1),   // Dia do Trabalho
        new DateTime(data.Year, 9, 7),   // Independência
        new DateTime(data.Year, 10, 12), // Nossa Senhora Aparecida
        new DateTime(data.Year, 11, 2),  // Finados
        new DateTime(data.Year, 11, 15), // Proclamação da República
        new DateTime(data.Year, 12, 25) // Natal
    };

    return feriados.Any(f => f.Date == data.Date);
}
```

### 4.2 Cache Local de Diários Baixados

```csharp
// Para evitar re-download do mesmo PDF no mesmo dia
private static readonly MemoryCache _cache = new();
private async Task<string> GetPdfContentCachedAsync(string pdfUrl, TimeSpan expiry)
{
    return await _cache.GetOrCreateAsync(pdfUrl, async entry =>
    {
        entry.AbsoluteExpirationRelativeToNow = expiry;
        var bytes = await _http.GetByteArrayAsync(pdfUrl);
        using var stream = new MemoryStream(bytes);
        return PdfExtractor.ExtractText(stream);
    });
}
```

### 4.3 Progresso e Logging

```csharp
_logger.LogInformation(
    "[{Adapter}] {Nome}: {Count} publicações encontradas em {Diarios} diários",
    Sigla, nomeBusca, publicacoes.Count, diarios.Count);
```

---

## 5. Dependências NuGet Adicionais

Adicionar ao projeto `LegalManager.Infrastructure`:

```xml
<PackageReference Include="PdfPig" Version="0.1.9" />
<PackageReference Include="HtmlAgilityPack" Version="1.11.67" />
```

---

## 6. Estimativa de Esforço

| Adapter | Complexidade | Tempo Estimado |
|---------|-------------|----------------|
| TjspDjeAdapter | Alta (scraping + PDF) | 3-4 dias |
| TjrjDjeAdapter | Alta (scraping + PDF) | 2-3 dias |
| TjmgDjeAdapter | Alta (scraping + PDF) | 2-3 dias |
| DjeJobBase | Média | 1 dia |
| Integração + testes | Alta | 2 dias |
| **Total Fase 1** | | **10-13 dias** |
