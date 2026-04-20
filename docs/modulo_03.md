# Módulo 03 — Fase 2: Automação Processual

## Visão Geral

A **Fase 2** implementa a automação processual para monitoramento de processos e captura de publicações dos tribunais brasileiros.

| Módulo | Descrição | Status |
|--------|----------|--------|
| Módulo 3.2 | Monitoramento automático (robôs tribunais) | ✅ Completo |
| Módulo 3.4 | Captura de publicações DJE | ⚠️ Parcial |
| Módulo 3.5 | Controle de prazos com calculadora | ✅ Completo |

---

## Módulo 3.2 — Monitoramento Automático (Robôs Tribunais)

### Objetivo

Monitorar automaticamente os processos cadastrados no sistema, consultando a base nacional do DataJud (CNJ) para detectar novos andamentos e notificar o advogado responsável.

### Arquitetura

```
┌─────────────────────────────────────────────────────────────┐
│                    LegalManager API                       │
├─────────────────────────────────────────────────────────────┤
│  MonitoramentoJob (Job assíncrono - Horário/Intervalo)       │
└───────────────────────┬───────────────────────────────────┘
                        │
                        ▼
┌─────────────────────────────────────────────────────────────┐
│              DataJudAdapter (CNJ)                          │
│  ┌─────────────────────────────────────────────────┐     │
│  │  API DataJud — /api_publica_{indice}/_search   │     │
│  │  Retorna movimentos de todos os tribunais         │     │
│  └─────────────────────────────────────────────────┘     │
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
  - TJs estaduais: TJSP, TJRJ, TJMG, TJMG, TJPR, TJPE, etc.
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

Job que executa periodicamente para monitorar processos.

**Fluxo:**

```
1. Buscar processos com Monitorado == true && Status == Ativo
2. Para cada processo:
   a. Consultar DataJud (por tribunal definido ou inferido)
   b. Obter movimentos do DataJud
   c. Comparar com andamentos existentes no banco
   d. Se há novos:
      - Criar registros de Andamento
      - Criar Notificacao para advogado responsável
      - Enviar e-mail (se configurado)
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
- `UltimoMonitoramento` (DateTime): Última consulta ao DataJud
- `Tribunal` (string, nullable): Sigla do tribunal (STJ, TJSP, etc.)

**Tabela: Andamentos**
- `Fonte` (enum): `Manual` ou `Automatico`
- `Tipo` (enum): Mapeado a partir da descrição no DataJud

### Configuração

```json
{
  "DataJud": {
    "ApiKey": "cDZHYzlZa0JadVREZDJCendOM3Yw",
    "BaseUrl": "https://esaj.tjsp.jus.br"
  }
}
```

A API key default é a key pública de exemplo do DataJud. Em produção, configurar no `appsettings.json`.

---

## Módulo 3.4 — Captura de Publicações DJE

### Objetivo

Capturar automaticamente as publicações dos Diários Oficiais da Justiça (DJE/DJ-e) para gerar alertas de prazos, audiências, decisões, etc.

### Status: Parcial

A infraestrutura base existe, mas **não há job de captura automática** implementado.

### O que existe:

#### 1. Entidade Publicacao (`Domain/Entities/Publicacao.cs`)

```csharp
public class Publicacao
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid? ProcessoId { get; set; }
    public string? NumeroCNJ { get; set; }
    public string Diario { get; set; }          // ex: DJE-TJSP, DJE-TJRJ
    public DateTime DataPublicacao { get; set; }
    public string Conteudo { get; set; }
    public TipoPublicacao Tipo { get; set; }     // Prazo, Audiencia, Decisao, Despacho, Intimacao, Outro
    public StatusPublicacao Status { get; set; }   // Nova, Lida, Arquivada
    public DateTime CapturaEm { get; set; }
}
```

#### 2. API (`Controllers/PublicacoesController.cs`)

| Endpoint | Método | Descrição |
|---------|--------|----------|
| `/api/publicacoes` | GET | Listar com filtros |
| `/api/publicacoes/{id}` | GET | Detalhes |
| `/api/publicacoes/{id}/lida` | PATCH | Marcar como lida |
| `/api/publicacoes/{id}/arquivar` | PATCH | Arquivar |
| `/api/publicacoes/nao-lidas/count` | GET | Contador de não lidas |

#### 3. Frontend (`pages/publicacoes.html`, `js/publicacoes.js`)

- Interface para listar publicações
- Filtros: status, tipo, data de/até
- Ações: marcar como lida, arquivar, expandir conteúdo
- Badge contador de não lidas no header

### O que falta implementar:

Para completar o módulo 3.4, seria necessário:

1. **Job de captura** — Similar ao MonitoramentoJob, mas para DJE
2. **Integração feeds DJE** — Cada tribunal tem endpoint próprio:
   - TJSP: `https://esaj.tjsp.jus.br/cjsg/peticionario/consultaPublicacaoAction.do`
   - TJRJ: `https://www3.tjrj.jus.br/nosso-bairro/filtroPublicacao.do`
   - TRF1-6, STJ, STF: feeds próprios
3. **Parser de conteúdo** — Extrair tipo (prazo, intimação, etc.), data límite, número CNJ
4. **Associação a processo** — Vincular ao processo correto pelo número CNJ

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

## Próximos Passos

Para completar o Módulo 3.4:

1. Implementar `PublicacaoCapturaJob`
2. Definir tribunal por tribunal:
   - Primeiro: TJSP (maior volume)
   - Segundo: TJRJ
   - Terceiro: TRF3
3. Testar com dados reais
4. Mapear para cada diário os tipos de publicação

---

## Referências

- Código: `src/LegalManager.API/Controllers/PublicacoesController.cs`
- Código: `src/LegalManager.Infrastructure/Services/PublicacaoService.cs`
- Código: `src/LegalManager.Infrastructure/Tribunais/DataJudAdapter.cs`
- Código: `src/LegalManager.Infrastructure/Jobs/MonitoramentoJob.cs`
- API DataJud: https://www.cnj.jus.br/transparencia/eureka-apis/