# Escavador — Fluxo Completo e Estimativa de Custos

Análise das chamadas de API necessárias para o ciclo de vida de um advogado na plataforma: desde o onboarding até o uso recorrente no mês a mês.

---

## 1. Mapa de Endpoints

### v2 — Implementado

| Endpoint | Função |
|---|---|
| `GET /api/v2/advogado/{oab}/processos?limit=100&page=N` | Lista processos TRF/TRT do advogado (paginado) |
| `GET /api/v2/envolvido/processos?documento={cpfcnpj}` | Lista processos por CPF/CNPJ |
| `POST /api/v2/monitoramento-processos` | Cria monitoramento de processo no tribunal (push webhook) |
| `DELETE /api/v2/monitoramento-processos/{id}` | Remove monitoramento |
| `GET /api/v2/callback/listar` | Lista callbacks pendentes (polling fallback) |
| `POST /api/v2/callback/marcar-como-recebidos` | Marca callbacks como recebidos |

### v1 — Não implementado

| Endpoint | Função |
|---|---|
| `POST /api/v1/processos/oab` | Busca assíncrona direta no site do tribunal (30 min–2 h) |
| `POST /api/v1/monitoramento-de-diarios-oficiais/criar` | Cria monitoramento de termo nos Diários Oficiais |
| `DELETE /api/v1/monitoramento-de-diarios-oficiais/remover` | Remove monitoramento de Diário |
| `GET /api/v1/monitoramento-de-diarios-oficiais/aparicoes` | Lista publicações encontradas |
| `GET /api/v1/saldo-da-api/consultar-saldo` | Consulta saldo de créditos |

---

## 2. Fluxo por Fase

### Fase 1 — Onboarding (ocorre uma vez)

```
Passo 1 — Busca imediata (implementado, parcialmente)
  DataJud  →  GET todos os TJs estaduais                     (paralelo)
  ESAJ     →  scraping TJSP (somente UF = SP)                (paralelo)
  Escavador →  GET /api/v2/advogado/{oab}/processos          (paralelo)
               Cobertura: TRF1-6 · TRT1-24
               ⚠ Limitado a 2 páginas (40 resultados) — deveria paginar tudo

Passo 2 — Busca profunda no site do tribunal (não implementado, opcional)
  POST /api/v1/processos/oab
  → Acessa o site do tribunal diretamente (não depende de indexação)
  → Retorna via callback após 30 min a 2 h
  → Útil para advogados com processos em tribunais pouco indexados
  → Para TRF/TRT, o v2 já cobre bem; DataJud cobre os TJs

Passo 3 — Seleção pelo usuário
  O advogado escolhe 10–20 processos para monitorar
  Nenhuma chamada à API neste passo

Passo 4 — Criar monitoramento nos tribunais (implementado)
  POST /api/v2/monitoramento-processos  ×  N  (10–20 chamadas)
  → Retorna EscavadorMonitoramentoId salvo no banco

Passo 5 — Criar monitoramento em Diários Oficiais (não implementado)
  POST /api/v1/monitoramento-de-diarios-oficiais/criar  ×  2–3 termos:
    • Número OAB  (ex: "123456/SP")  → detecta qualquer publicação DO com o advogado
    • Nome do advogado               → complemento ao número OAB
    • (Opcional) número CNJ de cada processo monitorado  ×  10–20
```

### Fase 2 — Dia a Dia (mês 2 em diante)

O modelo é **totalmente passivo** — a plataforma recebe webhooks, sem fazer chamadas ativas.

```
Fluxo principal (push):
  ← POST /api/escavador/callback

  Eventos de processo no tribunal (14 tipos):
    • novo_andamento / despacho / decisao / sentenca
    • audiencia_marcada
    • processo_arquivado / desarquivado
    • segredo_de_justica_alterado
    • parte_adicionada / parte_removida
    • capa_adicionada / capa_alterada / capa_removida
    • nova_instancia

  Eventos de Diário Oficial (2 tipos — requer monitoramento v1):
    • publicacao_em_diario               → publicação encontrada pelo termo
    • publicacao_identificada_em_diario  → publicação linkada a processo cadastrado

Fallback ativo (implementado — EscavadorCallbackPollingJob, 1×/hora):
  GET  /api/v1/callback/listar
  POST /api/v1/callback/marcar-como-recebidos

Operacional (não implementado, recomendado):
  GET /api/v1/saldo-da-api/consultar-saldo  → alertar quando créditos < threshold
```

---

## 3. Custos

### Confirmado pela documentação pública

| Ação | Custo |
|---|---|
| Busca OAB v2 — primeiros 200 processos | **R$ 4,50** (taxa base) |
| Blocos adicionais de 200 processos | **+ R$ 0,05** por bloco |
| Mesma busca repetida no mesmo dia | só o bloco, sem taxa base |

**Exemplos:**

| Processos do advogado | Páginas (limit=100) | Custo |
|---|---|---|
| Até 200 | 2 | R$ 4,50 |
| 201 – 400 | 4 | R$ 4,55 |
| 401 – 600 | 6 | R$ 4,60 |
| 1001 – 1200 | 12 | R$ 4,75 |

> Usar `limit=100` (máximo) minimiza chamadas HTTP sem alterar o custo total.

### Não públicos — confirmar no dashboard `api.escavador.com/servicos`

Os valores abaixo não constam na documentação pública. São estimativas baseadas em padrões de mercado para APIs similares.

| Ação | Endpoint | Estimativa |
|---|---|---|
| Criar monitoramento no tribunal | `POST /v2/monitoramento-processos` | R$ 0,10 – 0,50 / criação |
| Manutenção mensal por monitoramento ativo | — | R$ 0,50 – 2,00 / processo / mês |
| Callback recebido (andamento, decisão etc.) | webhook | R$ 0,01 – 0,10 / evento |
| Criar monitoramento em Diário Oficial | `POST /v1/monitoramento-de-diarios-oficiais/criar` | R$ 0,05 – 0,20 / termo |
| Publicação encontrada em Diário | webhook `publicacao_em_diario` | R$ 0,05 – 0,30 / aparição |
| Busca assíncrona v1 no tribunal | `POST /v1/processos/oab` | R$ 1,00 – 5,00 / busca |

---

## 4. Estimativas de Escala

### Premissas do modelo

| Item | Otimista | Provável | Pessimista |
|---|---|---|---|
| Busca OAB (até 200 proc) | R$ 4,50 | R$ 4,50 | R$ 4,50 |
| Criação de monitoramento / processo | R$ 0,10 | R$ 0,30 | R$ 0,50 |
| Manutenção mensal / processo monitorado | R$ 0,50 | R$ 1,25 | R$ 2,00 |
| Callback de andamento recebido | R$ 0,01 | R$ 0,05 | R$ 0,10 |
| Criação de monitoramento Diário (2 termos) | R$ 0,10 | R$ 0,25 | R$ 0,40 |
| Callbacks de Diários (~8/mês) | R$ 0,40 | R$ 1,40 | R$ 2,40 |
| Polling fallback (~720 GETs/mês) | R$ 0,72 | R$ 2,16 | R$ 3,60 |

Hipóteses de comportamento: **3 andamentos/processo/mês** (média esperada para processo ativo).

---

### Custo por advogado individual

#### Setup único (onboarding — ocorre só no Mês 1)

| Processos monitorados | Otimista | Provável | Pessimista |
|---|---|---|---|
| **1** | R$ 4,70 | R$ 5,05 | R$ 5,40 |
| **5** | R$ 5,10 | R$ 6,25 | R$ 7,40 |
| **10** | R$ 5,60 | R$ 7,75 | R$ 9,90 |
| **25** | R$ 7,10 | R$ 12,25 | R$ 17,40 |
| **50** | R$ 9,60 | R$ 19,75 | R$ 29,90 |
| **100** | R$ 14,60 | R$ 34,75 | R$ 54,90 |
| **500** | R$ 54,70 | R$ 154,85 | R$ 255,00 |

<details>
<summary>Composição do setup</summary>

Setup = busca OAB (R$4,50 fixo) + criação dos monitoramentos de tribunal × N + 2 monitoramentos de Diário. O custo é **dominado pela taxa fixa da busca** — variações dependem só do número de processos selecionados.
</details>

#### Recorrente mensal (Mês 2+ em diante)

| Processos monitorados | Otimista | Provável | Pessimista |
|---|---|---|---|
| **1** | R$ 1,65 | R$ 4,98 | R$ 8,30 |
| **5** | R$ 3,77 | R$ 10,64 | R$ 17,50 |
| **10** | R$ 6,42 | R$ 17,71 | R$ 29,00 |
| **25** | R$ 14,37 | R$ 38,94 | R$ 63,50 |
| **50** | R$ 27,62 | R$ 74,31 | R$ 121,00 |
| **100** | R$ 54,12 | R$ 145,06 | R$ 236,00 |
| **500** | R$ 266,12 | R$ 711,06 | R$ 1.156,00 |

<details>
<summary>Composição do recorrente</summary>

Recorrente = manutenção mensal dos N monitoramentos + callbacks de andamentos (3/proc/mês) + callbacks de Diários (~8/mês) + polling fallback (~720 GETs/mês). A **manutenção mensal por processo é o maior driver de custo** — e o de maior incerteza, pois não está publicado. O cenário pessimista assume R$2,00/processo/mês.
</details>

#### Total Mês 1 = Setup + Recorrente

O primeiro mês é mais caro que os seguintes por incluir os dois componentes.

| Processos monitorados | Otimista | Provável | Pessimista |
|---|---|---|---|
| **1** | R$ 6,35 | R$ 10,03 | R$ 13,70 |
| **5** | R$ 8,87 | R$ 16,89 | R$ 24,90 |
| **10** | R$ 12,02 | R$ 25,46 | R$ 38,90 |
| **25** | R$ 21,47 | R$ 51,19 | R$ 80,90 |
| **50** | R$ 37,22 | R$ 94,06 | R$ 150,90 |
| **100** | R$ 68,72 | R$ 179,81 | R$ 290,90 |
| **500** | R$ 320,82 | R$ 865,91 | R$ 1.411,00 |

---

### Custo total da plataforma — Matriz de escala

Os valores abaixo usam o cenário **Provável** (coluna do meio acima).

#### Mês 1 — Total (setup + recorrente, cenário Provável)

| | **1 proc** | **5 proc** | **10 proc** | **25 proc** | **50 proc** | **100 proc** | **500 proc** |
|---|---|---|---|---|---|---|---|
| **1 advogado** | R$ 10 | R$ 17 | R$ 25 | R$ 51 | R$ 94 | R$ 180 | R$ 866 |
| **10 advogados** | R$ 100 | R$ 169 | R$ 255 | R$ 512 | R$ 941 | R$ 1.798 | R$ 8.659 |
| **50 advogados** | R$ 502 | R$ 845 | R$ 1.273 | R$ 2.560 | R$ 4.703 | R$ 8.991 | R$ 43.296 |
| **100 advogados** | R$ 1.003 | R$ 1.689 | R$ 2.546 | R$ 5.119 | R$ 9.406 | R$ 17.981 | R$ 86.591 |

#### Mês 2+ — Recorrente mensal (cenário Provável)

| | **1 proc** | **5 proc** | **10 proc** | **25 proc** | **50 proc** | **100 proc** | **500 proc** |
|---|---|---|---|---|---|---|---|
| **1 advogado** | R$ 5 | R$ 11 | R$ 18 | R$ 39 | R$ 74 | R$ 145 | R$ 711 |
| **10 advogados** | R$ 50 | R$ 106 | R$ 177 | R$ 389 | R$ 743 | R$ 1.451 | R$ 7.111 |
| **50 advogados** | R$ 249 | R$ 532 | R$ 886 | R$ 1.947 | R$ 3.716 | R$ 7.253 | R$ 35.553 |
| **100 advogados** | R$ 498 | R$ 1.064 | R$ 1.771 | R$ 3.894 | R$ 7.431 | R$ 14.506 | R$ 71.106 |

---

### Faixa completa (otimista → pessimista)

Para dimensionamento de budget, considere a faixa abaixo ao invés do ponto médio.

Mês 1 = setup + recorrente (sempre maior que Mês 2+). Mês 2+ = só recorrente.

| Escala | 1 proc | 5 proc | 10 proc | 25 proc | 50 proc | 100 proc | 500 proc |
|---|---|---|---|---|---|---|---|
| **1 adv — Mês 1** | R$ 6 – R$ 14 | R$ 9 – R$ 25 | R$ 12 – R$ 39 | R$ 21 – R$ 81 | R$ 37 – R$ 151 | R$ 69 – R$ 291 | R$ 321 – R$ 1.411 |
| **1 adv — Mês 2+** | R$ 2 – R$ 8 | R$ 4 – R$ 18 | R$ 6 – R$ 29 | R$ 14 – R$ 64 | R$ 28 – R$ 121 | R$ 54 – R$ 236 | R$ 266 – R$ 1.156 |
| **10 adv — Mês 1** | R$ 64 – R$ 137 | R$ 89 – R$ 249 | R$ 120 – R$ 390 | R$ 215 – R$ 809 | R$ 372 – R$ 1.509 | R$ 687 – R$ 2.909 | R$ 3.208 – R$ 14.110 |
| **10 adv — Mês 2+** | R$ 17 – R$ 83 | R$ 38 – R$ 175 | R$ 64 – R$ 290 | R$ 144 – R$ 635 | R$ 276 – R$ 1.210 | R$ 541 – R$ 2.360 | R$ 2.661 – R$ 11.560 |
| **50 adv — Mês 1** | R$ 318 – R$ 685 | R$ 444 – R$ 1.245 | R$ 601 – R$ 1.945 | R$ 1.074 – R$ 4.045 | R$ 1.861 – R$ 7.545 | R$ 3.436 – R$ 14.545 | R$ 16.041 – R$ 70.550 |
| **50 adv — Mês 2+** | R$ 83 – R$ 415 | R$ 189 – R$ 875 | R$ 321 – R$ 1.450 | R$ 719 – R$ 3.175 | R$ 1.381 – R$ 6.050 | R$ 2.706 – R$ 11.800 | R$ 13.306 – R$ 57.800 |
| **100 adv — Mês 1** | R$ 635 – R$ 1.370 | R$ 887 – R$ 2.490 | R$ 1.202 – R$ 3.890 | R$ 2.147 – R$ 8.090 | R$ 3.722 – R$ 15.090 | R$ 6.872 – R$ 29.090 | R$ 32.082 – R$ 141.100 |
| **100 adv — Mês 2+** | R$ 165 – R$ 830 | R$ 377 – R$ 1.750 | R$ 642 – R$ 2.900 | R$ 1.437 – R$ 6.350 | R$ 2.762 – R$ 12.100 | R$ 5.412 – R$ 23.600 | R$ 26.612 – R$ 115.600 |

---

### Observações críticas

**A manutenção mensal é o risco principal.** O custo recorrente por processo monitorado não está publicado na documentação pública. Se for R$2,00/processo/mês (cenário pessimista), uma base de 100 advogados com 50 processos cada chega a **R$12.100/mês** só de API — o que pode ser inviável dependendo do ticket médio da plataforma.

**Recomendações antes de ir a produção:**
1. Acessar `api.escavador.com/servicos` com credenciais para confirmar todos os valores unitários
2. Criar 2–3 monitoramentos de teste e observar o header `Creditos-Utilizados` nas respostas
3. Definir um teto máximo de processos monitorados por plano da Causify com base nos valores reais
4. Considerar não repassar o custo de manutenção 1:1 — absorver na margem do plano ou criar um tier "monitoramento" separado

---

## 5. O que Falta Implementar

### A · Paginação completa no onboarding

**Impacto**: advogados com mais de 40 processos federais/trabalhistas não veem todos no onboarding.

- **Arquivo**: `OnboardingController.BuscarEscavadorOabAsync`
- **Mudança**: substituir `pagina <= 2` por loop até `resultado.TemProxima == false`, com cap de segurança (ex: 20 páginas = 2000 processos)
- **Custo**: R$ 0,05 por cada 200 processos acima dos primeiros 200 — impacto negligenciável

### B · Monitoramento de Diários Oficiais

**Impacto**: sem isso, a plataforma não detecta publicações em DJE, DJSP, DOU etc. — crítico para contagem de prazos.

Mudanças necessárias:

| Camada | O que adicionar |
|---|---|
| `IEscavadorService` | `CriarMonitoramentoDiarioAsync(tipo, valor, origensIds[])` e `RemoverMonitoramentoDiarioAsync(id)` |
| `EscavadorHttpClient` | Implementar os dois métodos usando base URL v1 |
| `OnboardingController.Importar` | Após importar processo Escavador, criar monitoramentos de Diário para OAB + nome do advogado (idempotente: só na primeira importação do tenant) |
| `EscavadorController.ReceberCallback` | Tratar tipos `publicacao_em_diario` e `publicacao_identificada_em_diario` → criar `Andamento` com tipo `Publicacao` |
| Banco / Entidade | Tabela `MonitoramentosDiario` ou colunas no `ApplicationUser` para armazenar os IDs dos monitoramentos de Diário por advogado |

**Termos a monitorar por advogado** (ordem de prioridade):
1. Número OAB (ex: `"123456/SP"`) — obrigatório
2. Nome completo do advogado — complementar
3. Número CNJ de cada processo monitorado — opcional, aumenta cobertura mas multiplica custo

### C · Busca assíncrona v1 (opcional)

Vai ao site do tribunal diretamente, sem depender da indexação do Escavador. Útil para:
- Advogados com processos em tribunais com baixa cobertura v2
- Onboarding de advogados que não encontraram processos esperados via v2

**Fluxo**: `POST /api/v1/processos/oab` → Hangfire aguarda callback → notifica advogado. Custo alto (R$ 1–5 por busca) — sugestão: oferecer como "busca aprofundada" opcional na UI.

---

## 6. Decisões em Aberto

| # | Questão | Opções |
|---|---|---|
| 1 | Diários: monitorar só OAB+nome ou também cada CNJ? | Só OAB+nome (2 termos, menor custo) vs. + CNJs (cobertura total, +R$ 0,75–3,00/mês por advogado) |
| 2 | Paginação no onboarding: limitar em quantas páginas? | Cap 20 (2.000 proc.) parece seguro; latência aceitável |
| 3 | Busca v1 assíncrona: automática ou sob demanda? | Recomendado: botão "Busca aprofundada" ou automático se v2 retornar < 5 resultados |
| 4 | Plano de créditos: quanto comprar? | Depende dos custos exatos do painel — confirmar antes de ir a produção |
| 5 | Polling fallback: manter 1×/hora? | OK para produção; reduzir para 1×/6h se custo de GET for alto |

---

## Fontes

- [Documentação API v1](https://api.escavador.com/v1/docs/)
- [Documentação API v2](https://api.escavador.com/v2/docs/)
- [Cobrança na API — Suporte](https://suporte-api.escavador.com/hc/pt-br/articles/13615780917531-Como-funciona-a-cobran%C3%A7a-na-API)
- [Cobrança v2 OAB — Suporte](https://suporte-api.escavador.com/hc/pt-br/articles/49180507067163-Entendendo-a-Cobran%C3%A7a-na-API-v2-Listar-Processos-por-Pessoa-ou-Advogado-OAB)
- [Callbacks e Buscas Assíncronas v1 — Suporte](https://suporte-api.escavador.com/hc/pt-br/articles/17873166021403-Callbacks-e-Buscas-Ass%C3%ADncronas-na-v1-do-Escavador)
- [SDK Python do Escavador (exemplos de endpoints)](https://github.com/Escavador/escavador-python)
