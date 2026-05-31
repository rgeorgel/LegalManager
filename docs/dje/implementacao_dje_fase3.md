# Fase 3 — STJ, STF, TST, TSE (Superiores)

Tribunais Superiores e Tribunais Regionais do Trabalho restantes. Publicam no **DJEN** (Diário da Justiça Eletrônico Nacional), exceto o TST que tem sistema próprio.

---

## Tribunais Desta Fase

| Tribunal | Código | Volume | Prioridade |
|----------|--------|--------|------------|
| STJ — Superior Tribunal de Justiça | STJ | Médio | Alta |
| STF — Supremo Tribunal Federal | STF | Médio | Alta |
| TST — Tribunal Superior do Trabalho | TST | Alto | Alta |
| TSE — Tribunal Superior Eleitoral | TSE | Médio | Média |
| TRT-4 (Rio Grande do Sul) | TRT4 | Médio | Média |
| TRT-5 (Bahia) | TRT5 | Médio | Média |
| TRT-6 (Pernambuco) | TRT6 | Médio | Média |
| TRT-8 (Pará/Amapá) | TRT8 | Médio | Média |
| TRT-9 (Paraná) | TRT9 | Médio | Média |
| TRT-10 (DF/Tocantins) | TRT10 | Médio | Média |
| TRT-11 (Amazonas/Roraima) | TRT11 | Médio | Baixa |
| TRT-12 (SC) | TRT12 | Médio | Baixa |
| TRT-13 (Paraíba) | TRT13 | Médio | Baixa |
| TRT-14 (Rondônia/Acre) | TRT14 | Médio | Baixa |
| TRT-15 (Campinas) | TRT15 | Médio | Baixa |
| TRT-16 (Maranhão) | TRT16 | Médio | Baixa |
| TRT-17 (ES) | TRT17 | Médio | Baixa |
| TRT-18 (GO) | TRT18 | Médio | Baixa |
| TRT-19 (AL) | TRT19 | Médio | Baixa |
| TRT-20 (SE) | TRT20 | Médio | Baixa |
| TRT-21 (RN) | TRT21 | Médio | Baixa |
| TRT-22 (PI) | TRT22 | Médio | Baixa |
| TRT-23 (MT) | TRT23 | Médio | Baixa |
| TRT-24 (MS) | TRT24 | Médio | Baixa |

---

## 1. STJ — Superior Tribunal de Justiça

**Maior tribunal superior em volume de publicações.**
**DJE:** https://www.stj.jus.br — integra o DJEN.
**Código:** `STJ`

### 1.1 Características

O STJ utiliza o **DJEN** (Diário da Justiça Eletrônico Nacional) que é o sistema unificado de publicação dos tribunais superiores. Existe busca por nome mas é preciso consultar através do portal do DJEN.

O DJEN está disponível em: https://www.jusbrasil.com.br/diarios/DJEN (não-oficial) ou via portal do STJ diretamente.

### 1.2 API Detetada

O STJ utiliza sistema próprio de publicação. Endpoint conhecido:

```
https://www.stj.jus.br/publicacoes/dje
```

### 1.3 Implementação

```csharp
public class StjDjeAdapter : IDjeAdapter
{
    private readonly HttpClient _http;
    private readonly ILogger<StjDjeAdapter> _logger;
    private readonly string _baseUrl = "https://www.stj.jus.br";

    public string Nome => "STJ - Diário da Justiça Eletrônico Nacional";
    public string Sigla => "STJ";
    public string BaseUrl => _baseUrl;
    public bool SuportaTipo(TipoDje tipo)
        => tipo == TipoDje.Djen;

    public async Task<DjeConsultaResult> ConsultarPorNomeAsync(
        string nome,
        DateTime? dataInicio = null,
        DateTime? dataFim = null,
        CancellationToken ct = default)
    {
        // STJ publica via portal próprio
        var url = $"{_baseUrl}/publicacoes/dje/busca";
        var form = new Dictionary<string, string>
        {
            ["termo"] = nome,
            ["dataInicio"] = (dataInicio ?? DateTime.UtcNow.AddDays(-7))
                .ToString("dd/MM/yyyy"),
            ["dataFim"] = (dataFim ?? DateTime.UtcNow)
                .ToString("dd/MM/yyyy"),
            ["tribunal"] = "STJ"
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
        // Listar edições do DJEN para o STJ
        var url = $"{_baseUrl}/publicacoes/dje/edicao/{data:yyyy-MM-dd}?tribunal=STJ";
        var response = await _http.GetAsync(url, ct);
        var html = await response.Content.ReadAsStringAsync(ct);

        var publicacoes = ParseEdicaoDjen(html, data, "STJ");
        return new DjeConsultaResult(true, null, publicacoes);
    }
}
```

### 1.4 Horário de Publicação

O STJ publica **de segunda a sexta-feira, a partir das 6h BRT** (9h UTC). Este é o tribunal superior que publica mais cedo. Recomendado: **8h BRT (11h UTC)** para garantir que o DJEN do dia já está disponível.

---

## 2. STF — Supremo Tribunal Federal

**Código:** `STF`

### 2.1 Características

O STF utiliza o DJEN para publicações. O volume de publicações é menor que o STJ (somente processos constitucionais). O STF é um dos mais importantes mas com menor volume.

### 2.2 Implementação

```csharp
public class StfDjeAdapter : IDjeAdapter
{
    private readonly HttpClient _http;
    private readonly ILogger<StfDjeAdapter> _logger;
    private readonly string _baseUrl = "https://portal.stf.jus.br";

    public string Nome => "STF - Supremo Tribunal Federal";
    public string Sigla => "STF";
    public string BaseUrl => _baseUrl;
    public bool SuportaTipo(TipoDje tipo)
        => tipo == TipoDje.Djen;

    public async Task<DjeConsultaResult> ConsultarPorNomeAsync(
        string nome,
        DateTime? dataInicio = null,
        DateTime? dataFim = null,
        CancellationToken ct = default)
    {
        // STF utiliza portal próprio
        var url = $"{_baseUrl}/diario/busca";
        var form = new Dictionary<string, string>
        {
            ["nome"] = nome,
            ["dataInicio"] = (dataInicio ?? DateTime.UtcNow.AddDays(-7))
                .ToString("dd/MM/yyyy"),
            ["dataFim"] = (dataFim ?? DateTime.UtcNow)
                .ToString("dd/MM/yyyy")
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
        var url = $"{_baseUrl}/diario/edicao/{data:yyyy-MM-dd}";
        var response = await _http.GetAsync(url, ct);
        var html = await response.Content.ReadAsStringAsync(ct);

        return new DjeConsultaResult(true, null,
            ParseEdicaoDjen(html, data, "STF"));
    }
}
```

### 2.3 Horário de Publicação

O STF publica **de segunda a sexta-feira, a partir das 7h BRT** (10h UTC). Recomendado: **9h BRT (12h UTC)**.

---

## 3. TST — Tribunal Superior do Trabalho

**Maior tribunal trabalhista em volume.**
**Código:** `TST`

### 3.1 Características

O TST tem sistema próprio de DJE (não usa DJEN). É o maior volume de publicações trabalhistas. Oferece busca por nome.

### 3.2 API Detetada

```
Base: https://www.tst.jus.br
Busca: /diario/busca
Edição: /diario/edicao/{data}
```

### 3.3 Implementação

```csharp
public class TstDjeAdapter : IDjeAdapter
{
    private readonly HttpClient _http;
    private readonly ILogger<TstDjeAdapter> _logger;
    private readonly string _baseUrl = "https://www.tst.jus.br";

    public string Nome => "TST - Tribunal Superior do Trabalho";
    public string Sigla => "TST";
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
            ["dataInicial"] = (dataInicio ?? DateTime.UtcNow.AddDays(-7))
                .ToString("dd/MM/yyyy"),
            ["dataFinal"] = (dataFim ?? DateTime.UtcNow)
                .ToString("dd/MM/yyyy")
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
        var url = $"{_baseUrl}/diario/edicao/{data:yyyy-MM-dd}";
        var response = await _http.GetAsync(url, ct);
        var html = await response.Content.ReadAsStringAsync(ct);

        var publicacoes = ParseEdicaoTst(html, data);
        return new DjeConsultaResult(true, null, publicacoes);
    }
}
```

### 3.4 Horário de Publicação

O TST publica **de segunda a sexta-feira, a partir das 8h BRT** (11h UTC). Recomendado: **9h BRT (12h UTC)**.

---

## 4. TSE — Tribunal Superior Eleitoral

**Código:** `TSE`

### 4.1 Características

O TSE utiliza o DJEN para publicações. Volume menor, concentrado em períodos eleitorais.

### 4.2 Implementação

```csharp
public class TseDjeAdapter : IDjeAdapter
{
    private readonly HttpClient _http;
    private readonly ILogger<TseDjeAdapter> _logger;
    private readonly string _baseUrl = "https://www.tse.jus.br";

    public string Nome => "TSE - Tribunal Superior Eleitoral";
    public string Sigla => "TSE";
    public string BaseUrl => _baseUrl;
    public bool SuportaTipo(TipoDje tipo)
        => tipo == TipoDje.Djen;

    public async Task<DjeConsultaResult> ConsultarPorNomeAsync(
        string nome,
        DateTime? dataInicio = null,
        DateTime? dataFim = null,
        CancellationToken ct = default)
    {
        var url = $"{_baseUrl}/diario/busca";
        var form = new Dictionary<string, string>
        {
            ["termo"] = nome,
            ["dataInicio"] = (dataInicio ?? DateTime.UtcNow.AddDays(-7))
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

### 4.3 Horário de Publicação

O TSE publica **de segunda a sexta-feira, a partir das 7h BRT** (10h UTC). Recomendado: **9h BRT (12h UTC)**.

---

## 5. Padrão — Adapters TRT Genérico

Para os TRTs restantes (TRT-4 ao TRT-24), a implementação segue um padrão genérico com variação mínima:

```csharp
public class TrtGenericoAdapter : IDjeAdapter
{
    private readonly HttpClient _http;
    private readonly ILogger _logger;
    private readonly string _sigla;
    private readonly string _baseUrl;

    public string Nome => $"{_sigla} - Diário Eletrônico";
    public string Sigla => _sigla;
    public string BaseUrl => _baseUrl;
    public bool SuportaTipo(TipoDje tipo) => tipo == TipoDje.Djen;

    public TrtGenericoAdapter(string sigla, string baseUrl,
        HttpClient http, ILogger logger)
    {
        _sigla = sigla;
        _baseUrl = baseUrl;
        _http = http;
        _logger = logger;
    }

    public async Task<DjeConsultaResult> ConsultarPorNomeAsync(
        string nome,
        DateTime? dataInicio = null,
        DateTime? dataFim = null,
        CancellationToken ct = default)
    {
        // Padrão para todos os TRTs
        var url = $"{_baseUrl}/diario/busca";
        var form = new Dictionary<string, string>
        {
            ["palavra"] = nome,
            ["dataInicial"] = (dataInicio ?? DateTime.UtcNow.AddDays(-7))
                .ToString("dd/MM/yyyy"),
            ["dataFinal"] = (dataFim ?? DateTime.UtcNow)
                .ToString("dd/MM/yyyy")
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
        var url = $"{_baseUrl}/diario/edicao/{data:yyyy-MM-dd}";
        var response = await _http.GetAsync(url, ct);
        var html = await response.Content.ReadAsStringAsync(ct);

        return new DjeConsultaResult(true, null,
            ParseEdicaoPadrao(html, data));
    }
}
```

### 5.1 Configuração de TRTs Restantes

```csharp
// Registro no DI
services.AddHttpClient<Trt4DjeAdapter>("TRT4",
    c => c.BaseAddress = new Uri("https://www.trt4.jus.br"));
services.AddHttpClient<Trt5DjeAdapter>("TRT5",
    c => c.BaseAddress = new Uri("https://www.trt5.jus.br"));
services.AddHttpClient<Trt6DjeAdapter>("TRT6",
    c => c.BaseAddress = new Uri("https://www.trt6.jus.br"));
// ... e assim por diante
```

### 5.2 Base URLs dos TRTs

| TRT | Base URL |
|-----|----------|
| TRT-4 | https://www.trt4.jus.br |
| TRT-5 | https://www.trt5.jus.br |
| TRT-6 | https://www.trt6.jus.br |
| TRT-7 | https://www.trt7.jus.br |
| TRT-8 | https://www.trt8.jus.br |
| TRT-9 | https://www.trt9.jus.br |
| TRT-10 | https://www.trt10.jus.br |
| TRT-11 | https://www.trt11.jus.br |
| TRT-12 | https://www.trt12.jus.br |
| TRT-13 | https://www.trt13.jus.br |
| TRT-14 | https://www.trt14.jus.br |
| TRT-15 | https://www.trt15.jus.br |
| TRT-16 | https://www.trt16.jus.br |
| TRT-17 | https://www.trt17.jus.br |
| TRT-18 | https://www.trt18.jus.br |
| TRT-19 | https://www.trt19.jus.br |
| TRT-20 | https://www.trt20.jus.br |
| TRT-21 | https://www.trt21.jus.br |
| TRT-22 | https://www.trt22.jus.br |
| TRT-23 | https://www.trt23.jus.br |
| TRT-24 | https://www.trt24.jus.br |

### 5.3 Horário de Publicação — Todos os TRTs

A maioria dos TRTs publica **de segunda a sexta-feira, entre 8h-10h BRT**. Recomendado: rodar jobs entre **10h-11h BRT (13h-14h UTC)**.

---

## 6. DJEN — Diário da Justiça Eletrônico Nacional

O DJEN é o sistema que agrega publicações dos tribunais superiores. URL: https://www.jusbrasil.com.br/diarios/DJEN (não-oficial, mas é a forma mais prática de acessar).

### 6.1 Estratégia DJEN

```csharp
public class DjenAdapter : IDjeAdapter
{
    public string Nome => "DJEN - Diário da Justiça Eletrônico Nacional";
    public string Sigla => "DJEN";
    public string BaseUrl => "https://www.jusbrasil.com.br/diarios/DJEN";
    public bool SuportaTipo(TipoDje tipo)
        => tipo == TipoDje.Djen;

    // Implementação via scraping do JusBrasil
    // ATENÇÃO: termos de uso do JusBrasil podem restringir uso comercial
}
```

**Nota Importante:** O JusBrasil é um agregador não-oficial. Para uso comercial, é necessário verificar os termos de uso. Alternativamente, consultar diretamente nos portais de cada tribunal superior.

---

## 7. Scheduling — Fase 3

| Adapter | Horário Recomendado (BRT) | Horário UTC |
|---------|---------------------------|-------------|
| StjDjeAdapter | 08:00 | 11:00 |
| StfDjeAdapter | 09:00 | 12:00 |
| TstDjeAdapter | 09:00 | 12:00 |
| TseDjeAdapter | 09:00 | 12:00 |
| TRTs (em grupo) | 10:00-11:00 | 13:00-14:00 |

---

## 8. Estimativa de Esforço

| Adapter | Complexidade | Tempo Estimado |
|---------|-------------|----------------|
| StjDjeAdapter | Média | 1 dia |
| StfDjeAdapter | Média | 1 dia |
| TstDjeAdapter | Média | 1 dia |
| TseDjeAdapter | Média | 1 dia |
| TRT Genérico (base) | Alta | 2 dias |
| TRT-4 a TRT-24 (18 adapters) | Baixa cada | 0.5 dia cada |
| **Total Fase 3** | | **15-18 dias** |

---

## 9. Cronograma Total — Todas as Fases

| Fase | Conteúdo | Estimativa |
|------|----------|------------|
| Arquitetura | Interface IDjeAdapter, DjeJobBase, padrões comuns | 2-3 dias |
| Fase 1 | TJSP, TJRJ, TJMG | 10-13 dias |
| Fase 2 | TRT-1, TRT-2, TRT-3, TJPR, TJRS, TJSC | 10-14 dias |
| Fase 3 | STJ, STF, TST, TSE + TRTs restantes | 15-18 dias |
| **Total** | | **37-48 dias** |
