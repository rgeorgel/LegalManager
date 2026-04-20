# Módulo 03 — Fase 2: Automação Processual

## Visão Geral

A **Fase 2** implementa a automação processual para monitoramento de processos e captura de publicações dos tribunais brasileiros.

| Módulo | Descrição | Status |
|--------|----------|--------|
| Módulo 3.2 | Monitoramento automático (robôs tribunais) | ✅ Completo |
| Módulo 3.4 | Captura de publicações DJE | ✅ Completo |
| Módulo 3.5 | Controle de prazos com calculadora | ✅ Completo |

---

## Módulo 3.2 — Monitoramento Automático (Robôs Tribunais)

### Objetivo

Monitorar automaticamente os processos cadastrados no sistema, consultando a base nacional do DataJud (CNJ) para detectar novos andamentos e notificar o advogado responsável.

### Arquitetura

```
┌─────────────────────────────────────────────────────────────┐
│                    LegalManager API                         │
├─────────────────────────────────────────────────────────────┤
│  MonitoramentoJob (Hangfire — diário às 06:00 UTC)          │
└───────────────────────┬─────────────────────────────────────┘
                        │
                        ▼
┌─────────────────────────────────────────────────────────────┐
│              DataJudAdapter (CNJ)                           │
│  ┌─────────────────────────────────────────────────┐       │
│  │  API DataJud — /api_publica_{indice}/_search    │       │
│  │  Retorna movimentos de todos os tribunais        │       │
│  └─────────────────────────────────────────────────┘       │
└─────────────────────────────────────────────────────────────┘
```

### Componentes

#### 1. DataJudAdapter (`Tribunais/DataJudAdapter.cs`)

Integração com a API REST do DataJud (banco centralizado de movimentações processuais).

**Características:**
- Suporta **27+ tribunais**:
  - Supremo: STF
  - STJ, TSE, STM
  - TRFs: TRF1 a TRF6
  - TJs estaduais: TJSP, TJRJ, TJMG, TJPR, TJPE, etc.
  - TRTs: TRT1 a TRT24
- Infere automaticamente qual tribunal consultar a partir do número CNJ (segmento J.TT)
- Retorna: tribunal, comarca, vara, lista de movimentos

**Mapeamento de tribunais (CNJ → DataJud index):**

| Código CNJ | Tribunal | Index DataJud |
|-----------|---------|------------|
| 9xx | STF | stf |
| 8xx | STJ | stj |
| 5xx | TSE | tse |
| 3xx | STM | stm |
| 2xx.0 | TST | tst |
| 2xx.1-24 | TRT | trt1-trt24 |
| 1xx.1-6 | TRF | trf1-trf6 |
| 6xx | TJ Estadual | ver tabela completa em `DataJudAdapter.cs:159` |

**API Reference:**

```csharp
public interface ITribunalAdapter
{
    string Nome { get; }
    bool SuportaTribunal(string tribunal);
    Task<TribunalConsultaResult> ConsultarAsync(string numeroCNJ, CancellationToken ct);
    Task<TribunalConsultaResult> ConsultarPorTribunalAsync(string numeroCNJ, string tribunal, CancellationToken ct);
}
```

#### 2. MonitoramentoJob (`Jobs/MonitoramentoJob.cs`)

Job Hangfire que executa diariamente às **06:00 UTC** para monitorar processos.

**Fluxo:**

```
1. Buscar processos com Monitorado == true && Status == Ativo
2. Para cada processo:
   a. Consultar DataJud (por tribunal definido ou inferido)
   b. Obter movimentos do DataJud
   c. Comparar com andamentos existentes no banco
   d. Se há novos:
      - Criar registros de Andamento (Fonte = Automatico)
      - Criar Notificacao para advogado responsável
      - Enviar e-mail via Resend
   e. Atualizar UltimoMonitoramento
   f. Atualizar tribunal/vara se necessário
```

**Notificações geradas:**

- Tipo: `NovoAndamento`
- Título: `Novo andamento — {numeroCNJ}`
- URL: `/pages/processo-detalhe.html?id={processoId}`

#### 3. Modelo de Dados

**Tabela: Processos**
- `Monitorado` (bool): Define se o processo deve ser monitorado
- `UltimoMonitoramento` (DateTime?): Última consulta ao DataJud
- `Tribunal` (string, nullable): Sigla do tribunal (STJ, TJSP, etc.)

**Tabela: Andamentos**
- `Fonte` (enum): `Manual` ou `Automatico`
- `Tipo` (enum): Mapeado a partir da descrição no DataJud

### Configuração

```json
{
  "DataJud": {
    "ApiKey": ""
  }
}
```

A key padrão de exemplo do DataJud (`cDZHYzlZa0JadVREZDJCendOM3Yw`) é usada como fallback. Em produção, configurar `DataJud:ApiKey` no `appsettings.json` ou variável de ambiente.

---

## Módulo 3.4 — Captura de Publicações DJE

### Objetivo

Capturar automaticamente publicações de processos nos Diários Oficiais/DJe, classificá-las com IA (Claude) e notificar os advogados responsáveis. O tenant configura até **3 nomes** (advogados ou partes) para monitoramento.

### Arquitetura

```
┌─────────────────────────────────────────────────────────────┐
│  CapturaPublicacaoJob (Hangfire — diário às 07:00 UTC)      │
│  (executa após MonitoramentoJob, reutiliza andamentos auto)  │
└──────────────┬──────────────────────────┬───────────────────┘
               │                          │
               ▼                          ▼
   ┌───────────────────┐      ┌───────────────────────┐
   │  NomesCaptura DB  │      │   Andamentos DB        │
   │  (nomes do tenant)│      │  (Fonte = Automatico,  │
   └───────────────────┘      │   Tipo = Publicacao/   │
                              │   Intimacao, últimos    │
                              │   7 dias)              │
                              └──────────┬─────────────┘
                                         │
                                         ▼
                              ┌───────────────────────┐
                              │  Anthropic API        │
                              │  (claude-haiku-4-5)   │
                              │  Classifica tipo,     │
                              │  urgência e resumo    │
                              └──────────┬─────────────┘
                                         │
                                         ▼
                              ┌───────────────────────┐
                              │  Publicacoes DB       │
                              │  + Notificacao        │
                              │  + E-mail Resend      │
                              └───────────────────────┘
```

### Componentes

#### 1. NomeCaptura (`Domain/Entities/NomeCaptura.cs`)

Armazena os nomes que o tenant configura para monitoramento de publicações.

```csharp
public class NomeCaptura
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string Nome { get; set; }   // ex: "João Silva", "SILVA ADVOGADOS"
    public bool Ativo { get; set; }
    public DateTime CriadoEm { get; set; }
    public Tenant Tenant { get; set; }
}
```

**Limite:** 3 nomes por tenant (Plano Smart). Validado em `NomeCapturaService`.

#### 2. Publicacao (atualizada)

Dois novos campos adicionados à entidade e tabela:

```csharp
public class Publicacao
{
    // ... campos existentes ...
    public bool Urgente { get; set; }          // true se IA detectou prazo < 5 dias
    public string? ClassificacaoIA { get; set; } // resumo gerado pelo Claude (max 500 chars)
}
```

#### 3. NomeCapturaService (`Services/NomeCapturaService.cs`)

CRUD para gerenciamento de nomes de captura com validação de limite.

```csharp
public interface INomeCapturaService
{
    Task<IEnumerable<NomeCapturaResponseDto>> GetAllAsync(CancellationToken ct);
    Task<NomeCapturaResponseDto> CreateAsync(CreateNomeCapturaDto dto, CancellationToken ct);
    Task ToggleAtivoAsync(Guid id, CancellationToken ct);
    Task DeleteAsync(Guid id, CancellationToken ct);
}
```

**Regras:**
- Limite de 3 nomes por tenant — lança `InvalidOperationException` se excedido
- Nome duplicado (mesmo tenant) não é permitido — índice único `(TenantId, Nome)`
- `ToggleAtivoAsync` pausa/reativa um nome sem removê-lo

#### 4. CapturaPublicacaoJob (`Jobs/CapturaPublicacaoJob.cs`)

Job Hangfire que executa diariamente às **07:00 UTC** (após o MonitoramentoJob).

**Fluxo:**

```
1. Carregar todos os NomesCaptura ativos (todos os tenants)
2. Para cada tenant:
   a. Buscar processos do tenant com partes que correspondem aos nomes configurados
      (match por Contato.Nome contendo o NomeCaptura.Nome, case-insensitive)
   b. Buscar andamentos dos últimos 7 dias, tipo Publicacao ou Intimacao,
      Fonte = Automatico, nos processos candidatos
   c. Para cada andamento novo (não presente em Publicacoes):
      - Chamar Anthropic API (Claude Haiku) para classificar
      - Criar registro em Publicacoes com Tipo, Urgente, ClassificacaoIA
   d. Notificar advogado responsável (Notificacao + e-mail)
3. Salvar tudo
```

**Fallback local** (quando a API do Claude não responde):

| Palavra-chave no texto | Tipo inferido |
|----------------------|---------------|
| "prazo", "recurso" | Prazo |
| "audiên", "julgament" | Audiencia |
| "decis", "senten", "acórd" | Decisao |
| "despacho" | Despacho |
| "intim" | Intimacao |
| outros | Outro |

#### 5. NomesCapturaController (`Controllers/NomesCapturaController.cs`)

| Endpoint | Método | Descrição |
|---------|--------|----------|
| `/api/nomes-captura` | GET | Listar nomes do tenant |
| `/api/nomes-captura` | POST | Adicionar nome (máx 3) |
| `/api/nomes-captura/{id}/toggle` | PATCH | Ativar/pausar nome |
| `/api/nomes-captura/{id}` | DELETE | Remover nome |

#### 6. PublicacoesController (`Controllers/PublicacoesController.cs`)

Sem alteração nos endpoints — os novos campos `urgente` e `classificacaoIA` são incluídos automaticamente na resposta.

| Endpoint | Método | Descrição |
|---------|--------|----------|
| `/api/publicacoes` | GET | Listar com filtros (status, tipo, data de/até, paginação) |
| `/api/publicacoes/{id}` | GET | Detalhes |
| `/api/publicacoes/{id}/lida` | PATCH | Marcar como lida |
| `/api/publicacoes/{id}/arquivar` | PATCH | Arquivar |
| `/api/publicacoes/nao-lidas/count` | GET | Contador de não lidas |

### Integração com Claude (Anthropic API)

**Configuração:**

```json
{
  "Anthropic": {
    "ApiKey": ""
  }
}
```

**HttpClient registrado em `Program.cs`:**

```csharp
builder.Services.AddHttpClient("Anthropic", client =>
{
    client.BaseAddress = new Uri("https://api.anthropic.com");
    client.DefaultRequestHeaders.Add("x-api-key", apiKey);
    client.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");
    client.Timeout = TimeSpan.FromSeconds(30);
});
```

**Modelo usado:** `claude-haiku-4-5-20251001` (custo baixo, adequado para classificação em lote)

**Prompt enviado ao Claude:**

```
Analise o texto abaixo, que é um andamento/movimento processual capturado de um
diário oficial ou sistema judicial.

Classifique-o respondendo APENAS com um JSON no formato:
{"tipo": "<Prazo|Audiencia|Decisao|Despacho|Intimacao|Outro>", "urgente": <true|false>, "resumo": "<resumo em 1 frase>"}

- tipo: categoria que melhor descreve o movimento
- urgente: true se há prazo < 5 dias ou audiência iminente
- resumo: resumo claro e objetivo do que aconteceu

Texto: {primeiros 1000 chars do conteúdo}
```

### Migration

**Migration:** `AddNomeCapturaAndPublicacaoIA`

Alterações no banco:
- Nova tabela `NomesCaptura` com colunas: `Id`, `TenantId`, `Nome`, `Ativo`, `CriadoEm`
- Índices: `(TenantId, Nome)` unique, `(TenantId, Ativo)`
- FK para `Tenants` com `OnDelete = Cascade`
- Novas colunas em `Publicacoes`: `Urgente` (boolean), `ClassificacaoIA` (varchar 500)

### Frontend

#### publicacoes.html / publicacoes.js

Adicionados à interface:
- **Badge vermelho "🔴 URGENTE"** exibido no card quando `urgente = true`
- **Borda vermelha** no card urgente (`.pub-card-urgente`)
- **Bloco de resumo IA** em destaque roxo com ícone 🤖 quando `classificacaoIA` está preenchido
- Estilos: `.pub-badge-urgente`, `.pub-ia-resumo`, `.pub-card-urgente`

#### configuracoes.html

Nova seção **"Nomes para Captura de Publicações"** com:
- Lista dos nomes cadastrados com status Ativo/Pausado
- Botão "Adicionar nome" (limitado a 3 — erro exibido ao exceder)
- Botões "Pausar/Ativar" e "Remover" por nome
- Link para a página de Publicações
- Mensagem informando o limite de 3 nomes (Plano Smart)

### Hangfire — Ordem de Execução

| Job | Horário (UTC) | Dependência |
|-----|--------------|-------------|
| `MonitoramentoJob` | 06:00 | — |
| `CapturaPublicacaoJob` | 07:00 | Usa andamentos gerados pelo MonitoramentoJob |
| `AlertasJob` | 08:00 | — |

---

## Módulo 3.5 — Controle de Prazos (Completo)

### Objetivo

Cálculo automático de prazos processuais considerando dias úteis, feriados nacionais e estaduais.

### Implementação

#### Backend

- `PrazosController`: CRUD de prazos
- `PrazosController.Calcular`: Endpoint de calculadora
  - Parâmetros: data início, quantidade de dias, tipo (dias úteis vs corridos)
  - Retorna: data final calculada, lista de feriados no intervalo

#### Frontend

- `pages/prazos.html`:
  - Cards de prazos com cores por urgência (vermelho < 3 dias, amarelo < 7 dias, verde > 7 dias)
  - Calculadora de prazos integrada
  - Modal para criar/editar prazos

---

## Referências de Código

| Arquivo | Descrição |
|---------|----------|
| `src/.../Controllers/PublicacoesController.cs` | Endpoints de publicações |
| `src/.../Controllers/NomesCapturaController.cs` | Endpoints de nomes de captura |
| `src/.../Services/PublicacaoService.cs` | Lógica de publicações |
| `src/.../Services/NomeCapturaService.cs` | Lógica de nomes de captura |
| `src/.../Jobs/MonitoramentoJob.cs` | Job de monitoramento de processos |
| `src/.../Jobs/CapturaPublicacaoJob.cs` | Job de captura de publicações + IA |
| `src/.../Tribunais/DataJudAdapter.cs` | Integração DataJud CNJ |
| `src/.../Entities/NomeCaptura.cs` | Entidade NomeCaptura |
| `src/.../Entities/Publicacao.cs` | Entidade Publicacao (com Urgente e ClassificacaoIA) |
| `src/.../wwwroot/pages/publicacoes.html` | Frontend — lista de publicações |
| `src/.../wwwroot/pages/configuracoes.html` | Frontend — gestão de nomes de captura |

## Referências Externas

- [API DataJud (CNJ)](https://www.cnj.jus.br/transparencia/eureka-apis/)
- [Anthropic API — Messages](https://docs.anthropic.com/en/api/messages)
