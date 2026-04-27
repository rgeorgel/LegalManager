# Arquitetura de Adapters DJE — Guia Comum

## Visão Geral

Cada tribunal possui um adapter específico que implementa a interface comum `IDjeAdapter`. Esta interface define o contrato para captura de publicações oficiais de diários da justiça.

> **Nota de Migração (2026):** O sistema mudou de "captura por nome de advogado/parte" para "monitoramento por número de processo CNJ". A entidade `NomeCaptura` foi substituída por `ProcessoMonitorado`.

## Arquitetura Atual — ProcessoMonitorado

### Entidade

```csharp
public class ProcessoMonitorado
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string NumeroCNJ { get; set; }  // formato: 0000000-00.0000.0.00.0000
    public string? NomeExibicao { get; set; }  // apelido opcional para facilitar identificação
    public bool Ativo { get; set; } = true;
    public DateTime CriadoEm { get; set; }
    public Tenant Tenant { get; set; }
}
```

### Limites por Plano

| Plano | Limite de Processos Monitorados |
|-------|----------------------------------|
| Free | 40 |
| Pro | 500 |
| Enterprise | 500 |

> Anteriormente o plano Free não permitia captura (`MaxNomesCaptura = 0`). Agora Free permite monitoramento de processos via DataJud.

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
        string termo,  // número CNJ ou nome
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
    string SiglaTribunal,
    DateTime DataPublicacao,
    string Secao,
    int? Pagina,
    string? Tipo,
    string Titulo,
    string Conteudo,
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

### 1. Busca por Número de Processo (atual)

```
Fluxo: ProcessosMonitorados do tenant → Adapter.ConsultarPorNomeAsync()
                                         ↓
                               Parsing do HTML/PDF/JSON
                                         ↓
                               Regex para extrair número CNJ
                                         ↓
                               DjePublicacao + Publicacao no DB
```

### 2. Busca por Data (complementar)

```
Fluxo: Job executa às 08:00 UTC (05:00 BRT)
       → Para cada DJE publicado na data
         → Verificar se contém ProcessosMonitorados
         → Se sim, criar Publicacao
```

## Jobs de Captura

### DjeJob — Monitoramento por Número CNJ

```csharp
public class DjeJob
{
    public async Task ExecutarAsync(CancellationToken ct)
    {
        var processos = await _context.ProcessosMonitorados
            .Where(p => p.Ativo)
            .Select(p => new { p.Id, p.TenantId, p.NumeroCNJ })
            .ToListAsync(ct);

        foreach (var processo in processos)
        {
            foreach (var adapter in _adapters)
            {
                var resultado = await adapter.ConsultarPorNomeAsync(
                    processo.NumeroCNJ,
                    dataInicio: DateTime.UtcNow.AddDays(-7),
                    dataFim: DateTime.UtcNow,
                    ct);
                // ...
            }
        }
    }
}
```

### CapturaPublicacaoJob — Monitoramento via DataJud

Este job busca andamentos de tipo `Publicacao` ou `Intimacao` no DataJud (CNJ) e os correlaciona com os `ProcessosMonitorados` do tenant:

```csharp
public async Task ExecutarAsync()
{
    var processos = await _context.ProcessosMonitorados
        .Where(p => p.Ativo)
        .Select(p => new { p.Id, p.TenantId, p.NumeroCNJ })
        .ToListAsync();

    var andamentos = await _context.Andamentos
        .Where(a => a.TenantId == tenantId &&
                    a.Fonte == FonteAndamento.Automatico &&
                    (a.Tipo == TipoAndamento.Publicacao || a.Tipo == TipoAndamento.Intimacao) &&
                    a.Data >= agora.AddDays(-7))
        .ToListAsync();

    // Para cada andamento, verificar se o processo está em ProcessosMonitorados
    // Se sim, criar Publicacao e notificar advogado responsável
}
```

## Deduplicação

```csharp
// Evitar duplicatas — hash do conteúdo
public static string GerarHash(DjePublicacao pub)
    => SHA256($"{pub.SiglaTribunal}|{pub.DataPublicacao:yyyyMMdd}|{pub.Tipo}|{pub.Titulo}");

// Antes de inserir, verificar se já existe
var jaExiste = await _context.Publicacoes
    .AnyAsync(p => p.HashDje == hash, ct);
```

## Status das Fontes de Dados

### TJSP — Djus

| Aspecto | Status |
|---------|--------|
| Fonte | `esaj.tjsp.jus.br/cdje/getListaDeCadernos.do` |
| Dados disponíveis | ~9 meses (a partir de julho/2025) |
| Busca por nome | ❌ Apenas busca por nome (desatualizado) |
| Busca por processo | ⚠️ Limitado a processos themselves no PDF |

### JusBrasil — Diários (API Oficial)

| Aspecto | Status |
|---------|--------|
| API | `api.jusbrasil.com.br/docs/` — Requires subscription |
| Cobertura | DJE completo (1990-presente) |
| Busca por nome | ✅ Suporta |
| Cust | 💰 Plano pago |

### JusBrasil — Adapter Playwright (Em Desenvolvimento)

| Aspecto | Status |
|---------|--------|
| Método | Microsoft.Playwright + Chromium headless |
| Objetivo | Bypass Cloudflare para scraping |
| Problema | Cloudflare detecta headless → "Just a moment..." challenge |
| Conclusão | **Não viável** para produção sem API oficial |

### CNJ DataJud

| Aspecto | Status |
|---------|--------|
| Fonte | `https:// Judiciary.jus.br/api/publica/v2` |
| Dados | Apenas jurisprudência/decisões, não DJE |
| Uso | Andamentos de processos (via `CapturaPublicacaoJob`) |

## Roadmap de Integração

### Fase 1 — Atual (✅ Implementado)

- `ProcessoMonitorado` entity + `IProcessoMonitoradoService`
- `DjeJob` consulta adapters com número CNJ (não nome)
- `CapturaPublicacaoJob` busca andamentos no DataJud
- UI em `/pages/configuracoes.html` — "Processos Monitorados"

### Fase 2 — JusBrasil API (🛒 Quando clientes pagantes)

Implementar integração com API oficial JusBrasil quando houver receita para justificar o custo:

1. Assinar plano JusBrasil Pro/Enterprise
2. Implementar `JusBrasilApiAdapter` com autenticação por API key
3. Migrar busca por número CNJ para API oficial (sem scraping)
4. Manter fallback para TJSP adapter (dados públicos)

### Limitações Atuais

- **TJSP**: só dados de ~9 meses (limitação do próprio tribunal)
- **JusBrasil**: sem API key, scraping é bloqueado por Cloudflare
- **DataJud**: não contém DJE, apenas jurisprudência

## Scheduling Recomendado

| Horário | Job | Motivo |
|---------|-----|--------|
| 07:00 UTC (04:00 BRT) | CapturaPublicacaoJob | Busca andamentos DataJud |
| 09:00 UTC (06:00 BRT) | DjeJob | Captura DJE por número CNJ |

**Nota:** Publicações só são consideradas válidas em **dia útil** (sábado→sexta, domingo→sexta).

## Configuração de Rate Limiting

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

## Classificação com IA

Publicações são classificadas automaticamente:

```
Prompt: "Classifique esta publicação judicial em tipo (Intimação/Prazo/Sentença/Acordao/Outro)
e urgência (Urgente=true se prazo < 5 dias). Retorne JSON: {tipo, urgente, resumo: max 200 chars}"
```

## Histórico de Mudanças

| Data | Mudança |
|------|---------|
| 2026-04-27 | `NomeCaptura` → `ProcessoMonitorado`. Busca por número CNJ em vez de nome. |
| 2026-04-27 | JusBrasil Playwright adapter implementado (bloqueado por Cloudflare) |
| 2026-04-27 | Plano Free agora permite monitoramento de processos (40 limites) |
| 2026-04-22 | `NomeCaptura` criado com limite de 3 nomes no plano Free |