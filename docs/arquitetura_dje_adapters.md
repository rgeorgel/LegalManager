# Arquitetura de Adapters DJE — Guia Comum

## Visão Geral

Cada tribunal possui um adapter específico que implementa a interface comum `IDjeAdapter`. Esta interface define o contrato para captura de publicações oficiais.

## Interface Comum

```csharp
public interface IDjeAdapter
{
    string Nome { get; }
    string Sigla { get; }
    string BaseUrl { get; }
    bool SuportaTipo(TipoDje tipo); // Djus, Djen, Dou

    Task<DjeConsultaResult> ConsultarPublicacoesAsync(
        DateTime data,
        CancellationToken ct = default);

    Task<DjeConsultaResult> ConsultarPorNomeAsync(
        string nome,
        DateTime? dataInicio = null,
        DateTime? dataFim = null,
        CancellationToken ct = default);

    Task<DjeDetalheResult> ObterDetalheAsync(
        string idPublicacao,
        CancellationToken ct = default);
}
```

## Tipos de Diário Suportados

```csharp
public enum TipoDje
{
    Djus,   // Diário da Justiça — tribunais estaduais
    Djen,   // Diário da Justiça Eletrônico Nacional (STJ, STF, etc.)
    Dou     // Diário Oficial da União
}
```

## Resultado Padrão

```csharp
public record DjePublicacao(
    string Id,
    string Tribunal,
    DateTime DataPublicacao,
    string Secao,
    string Pagina,
    string Tipo,
    string Titulo,
    stringementario,
    List<string> NomesEncontrados,
    string UrlOriginal,
    decimal? PrazoDias);

public record DjeConsultaResult(
    bool Sucesso,
    string? Erro,
    List<DjePublicacao> Publicacoes);

public record DjeDetalheResult(
    bool Sucesso,
    string? Erro,
    string? TextoIntegral,
    string? HashDje);
```

## Padrão de Implementação por Adapter

### 1. Busca por Nome (mais comum)

```
Fluxo: NomesCaptura do tenant → Adapter.ConsultarPorNomeAsync()
                                         ↓
                               Parsing do HTML/PDF/JSON
                                         ↓
                               Regex para extrair processo + nome
                                         ↓
                               Matching com processos do tenant
                                         ↓
                               DjePublicacao + Publicacao no DB
```

### 2. Busca por Data (complementar)

```
Fluxo: Job executa às 08:00 UTC (05:00 BRT)
       → Para cada Djenum publicado na data
         → Verificar se contém NomeCaptura
         → Se sim, criar Publicacao
```

## Componentes Compartilhados

### DjeJob (base)

```csharp
public abstract class DjeJobBase
{
    protected readonly AppDbContext _context;
    protected readonly IDjeAdapter _adapter;
    protected readonly IEmailService _emailService;
    protected readonly ILogger _logger;

    protected async Task ExecutarCapturaAsync()
    {
        var nomes = await _context.NomesCaptura
            .Where(n => n.Ativo)
            .ToListAsync();

        foreach (var nome in nomes)
        {
            var resultado = await _adapter.ConsultarPorNomeAsync(
                nome.Nome,
                dataInicio: DateTime.UtcNow.AddDays(-7));

            foreach (var pub in resultado.Publicacoes)
            {
                await ProcessarPublicacaoAsync(nome.TenantId, pub);
            }
        }
    }
}
```

### Deduplicação

```csharp
// Evitar duplicatas — hash do conteúdo
public static string GerarHashDeduplicacao(DjePublicacao pub)
    => $"{pub.Tribunal}|{pub.DataPublicacao:yyyyMMdd}|{pub.Tipo}|{pub.Titulo}".Sha256();
```

### Configuração de Rate Limiting

Cada adapter deve implementar backoff exponencial:

```csharp
private async Task<T> ComRetry<T>(
    Func<Task<T>> action,
    int maxRetries = 3)
{
    for (int i = 0; i < maxRetries; i++)
    {
        try
        {
            return await action();
        }
        catch (HttpRequestException ex) when (i < maxRetries - 1)
        {
            await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, i)));
        }
    }
    throw;
}
```

### Configuração (appsettings.json)

```json
{
  "DjeAdapters": {
    " Tjsp": {
      "Enabled": true,
      "BaseUrl": "https://dje.tjsp.jus.br",
      "HorarioPublicacao": "08:00",
      "TimeoutSegundos": 30,
      "MaxResults": 500
    }
  }
}
```

## Classificação com IA

Todas as publicações são classificadas via `IAService.ClassificarPublicacaoAsync()`:

```
Prompt: "Classifique esta publicação judicial em tipo (Intimação/Prazo/Sentença/Acordao/Outro)
e urgência (Urgente=true se prazo < 5 dias). Retorne JSON: {tipo, urgente, resumo: max 200 chars}"
```

## Scheduling Recomendado

| Horário | Job | Motivo |
|---------|-----|--------|
| 08:00 UTC (05:00 BRT) | Djen (STJ, STF) | STJ publica às 6h BRT |
| 09:00 UTC (06:00 BRT) | Djus TJSP, TJRJ, TJMG | Maioria publica às 7-8h BRT |
| 10:00 UTC (07:00 BRT) | Dou | Disponível em todo dia útil |
| 11:00 UTC (08:00 BRT) | TRTs, outros TJs | Públicos após manhã |

**Nota:** Publicações só são consideradas válidas em **dia útil** (feriados são pulados).

## Deduplicação e Consistência

- Cada publicação é identificada por `HashDje` = SHA256(tribunal + data + tipo + título)
- Antes de inserir, verificar se já existe `Publicacao.HashDje == hash`
- Guardar também `IdExterno` (ID fornecido pelo tribunal) para re-busca
