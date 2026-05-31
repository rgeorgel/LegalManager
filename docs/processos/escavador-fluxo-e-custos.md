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

### Preços confirmados (`api.escavador.com/servicos`)

#### Consultas pontuais

| Serviço | Custo |
|---|---|
| Processos do advogado por OAB (`GET /v2/advogado/{oab}/processos`) | R$ 4,50 até 200 itens + R$ 0,05 a cada 200 |
| Processos do envolvido por CPF/CNPJ | R$ 4,50 até 200 itens + R$ 0,05 a cada 200 |
| Resumo do advogado por OAB | R$ 0,40 |
| Capa de um processo por CNJ | R$ 0,05 |
| Envolvidos de um processo | R$ 0,05 |
| Movimentações de um processo | R$ 0,05 |
| Atualização do processo no tribunal | R$ 0,10 |
| Atualização do processo + documentos públicos | R$ 0,20 |
| Atualização do processo + baixar autos | R$ 1,50 |
| Resumo IA de um processo (geração) | R$ 0,08 |
| Resumo IA de um processo (leitura) | R$ 0,05 |

**Custo da busca OAB por total de processos:**

| Processos do advogado | Páginas (limit=100) | Custo |
|---|---|---|
| Até 200 | 2 | R$ 4,50 |
| 201 – 400 | 4 | R$ 4,55 |
| 401 – 600 | 6 | R$ 4,60 |
| 1001 – 1200 | 12 | R$ 4,75 |

> Usar `limit=100` (máximo) minimiza chamadas HTTP sem alterar o custo total.

#### Monitoramentos (assinaturas mensais)

| Serviço | Custo / mês |
|---|---|
| **Monitoramento de processo — atualização diária** (tribunal + DOs) | **R$ 1,76 / processo** |
| **Monitoramento de processo — atualização semanal** (tribunal + DOs) | **R$ 0,32 / processo** |
| **Monitoramento de processo — atualização mensal** (tribunal + DOs) | **R$ 0,08 / processo** |
| **Monitoramento de novos processos por termo** (OAB, CPF, nome) | **R$ 2,20** até 200 itens + R$ 0,05/200 |

> O monitoramento de processo já inclui Diários Oficiais — não é necessário contratar monitoramento de DOs separado para processos já monitorados. O "monitoramento de novos processos" serve para detectar processos novos que mencionem o advogado (por OAB/nome) e ainda não estão no sistema.

---

## 4. Estimativas de Escala

### Premissas do modelo (preços confirmados)

| Item | Diário | Semanal | Mensal |
|---|---|---|---|
| Busca OAB (até 200 proc, uma vez no onboarding) | R$ 4,50 | R$ 4,50 | R$ 4,50 |
| Monitoramento por processo / mês | **R$ 1,76** | **R$ 0,32** | **R$ 0,08** |
| Monitoramento de novos processos por OAB (1 termo) | R$ 2,20 | R$ 2,20 | R$ 2,20 |

**Cobertura do monitoramento de processo**: inclui atualizações do tribunal + Diários Oficiais. Callbacks por andamento não têm cobrança extra visível — incluídos na assinatura.

**Polling fallback** (`GET /api/v2/callback/listar`): não listado na tabela de preços — custo provavelmente nulo ou marginal, não incluído nas estimativas.

**Recomendação de tier por situação:**
- Processo ativo (movimentação nos últimos 90 dias): **Diário** — necessário para prazos de 5 dias
- Processo dormente (sem movimentação há 6+ meses): **Semanal** — suficiente, sem prazos iminentes
- Processo arquivado: **cancelar monitoramento** (ver Estratégia 2, seção 6)

---

### Custo por advogado individual

Setup = busca OAB (R$4,50 fixo, uma vez). Recorrente = N × tier + R$2,20 (monitoramento de OAB para novos processos). Mês 1 = Setup + primeiro mês de recorrente.

#### Recorrente mensal (Mês 2+ em diante)

| Processos monitorados | Diário (R$1,76/proc) | Semanal (R$0,32/proc) |
|---|---|---|
| **1** | R$ 3,96 | R$ 2,52 |
| **5** | R$ 11,00 | R$ 3,80 |
| **10** | R$ 19,80 | R$ 5,40 |
| **25** | R$ 46,20 | R$ 10,20 |
| **50** | R$ 90,20 | R$ 18,20 |
| **100** | R$ 178,20 | R$ 34,20 |
| **500** | R$ 882,20 | R$ 162,20 |

#### Total Mês 1 (setup R$4,50 + primeiro mês de recorrente)

| Processos monitorados | Diário | Semanal |
|---|---|---|
| **1** | R$ 8,46 | R$ 7,02 |
| **5** | R$ 15,50 | R$ 8,30 |
| **10** | R$ 24,30 | R$ 9,90 |
| **25** | R$ 50,70 | R$ 14,70 |
| **50** | R$ 94,70 | R$ 22,70 |
| **100** | R$ 182,70 | R$ 38,70 |
| **500** | R$ 886,80 | R$ 166,80 |

---

### Custo total da plataforma — Matriz de escala

Valores usando **monitoramento diário (R$1,76/proc/mês)** — cenário padrão para processos ativos.

#### Mês 1 — Total (setup + primeiro mês, monitoramento diário)

| | **1 proc** | **5 proc** | **10 proc** | **25 proc** | **50 proc** | **100 proc** | **500 proc** |
|---|---|---|---|---|---|---|---|
| **1 advogado** | R$ 8 | R$ 16 | R$ 24 | R$ 51 | R$ 95 | R$ 183 | R$ 887 |
| **10 advogados** | R$ 85 | R$ 155 | R$ 243 | R$ 507 | R$ 947 | R$ 1.827 | R$ 8.868 |
| **50 advogados** | R$ 423 | R$ 775 | R$ 1.215 | R$ 2.535 | R$ 4.735 | R$ 9.135 | R$ 44.340 |
| **100 advogados** | R$ 846 | R$ 1.550 | R$ 2.430 | R$ 5.070 | R$ 9.470 | R$ 18.270 | R$ 88.680 |

#### Mês 2+ — Recorrente mensal (monitoramento diário)

| | **1 proc** | **5 proc** | **10 proc** | **25 proc** | **50 proc** | **100 proc** | **500 proc** |
|---|---|---|---|---|---|---|---|
| **1 advogado** | R$ 4 | R$ 11 | R$ 20 | R$ 46 | R$ 90 | R$ 178 | R$ 882 |
| **10 advogados** | R$ 40 | R$ 110 | R$ 198 | R$ 462 | R$ 902 | R$ 1.782 | R$ 8.822 |
| **50 advogados** | R$ 198 | R$ 550 | R$ 990 | R$ 2.310 | R$ 4.510 | R$ 8.910 | R$ 44.110 |
| **100 advogados** | R$ 396 | R$ 1.100 | R$ 1.980 | R$ 4.620 | R$ 9.020 | R$ 17.820 | R$ 88.220 |

---

### Faixa de custo por tier de monitoramento

A variável de custo agora é o **tier de monitoramento**, não estimativas. Use diário para processos ativos e semanal para dormentes (ver Estratégia 5, seção 6).

Mês 1 = setup + primeiro mês. Mês 2+ = só recorrente.

| Escala | 10 proc | 25 proc | 50 proc | 100 proc |
|---|---|---|---|---|
| **1 adv — Mês 1 (diário)** | R$ 24 | R$ 51 | R$ 95 | R$ 183 |
| **1 adv — Mês 1 (semanal)** | R$ 10 | R$ 15 | R$ 23 | R$ 39 |
| **1 adv — Mês 2+ (diário)** | R$ 20 | R$ 46 | R$ 90 | R$ 178 |
| **1 adv — Mês 2+ (semanal)** | R$ 5 | R$ 10 | R$ 18 | R$ 34 |
| **10 adv — Mês 2+ (diário)** | R$ 198 | R$ 462 | R$ 902 | R$ 1.782 |
| **10 adv — Mês 2+ (semanal)** | R$ 54 | R$ 102 | R$ 182 | R$ 342 |
| **50 adv — Mês 2+ (diário)** | R$ 990 | R$ 2.310 | R$ 4.510 | R$ 8.910 |
| **50 adv — Mês 2+ (semanal)** | R$ 270 | R$ 510 | R$ 910 | R$ 1.710 |
| **100 adv — Mês 2+ (diário)** | R$ 1.980 | R$ 4.620 | R$ 9.020 | R$ 17.820 |
| **100 adv — Mês 2+ (semanal)** | R$ 540 | R$ 1.020 | R$ 1.820 | R$ 3.420 |

---

### Observações críticas

**O tier de monitoramento é o principal lever de custo.** Com preços confirmados, a diferença entre diário (R$1,76) e semanal (R$0,32) é 5,5× por processo. Para uma base de 100 advogados com 50 processos cada, usar diário em todos custa **R$9.020/mês** contra **R$1.820/mês** com semanal — a decisão de tier deve ser consciente.

**Recomendações antes de ir a produção:**
1. Confirmar se callbacks por andamento têm custo adicional (não visível na tabela de serviços) observando o header `Creditos-Utilizados` nas respostas
2. Implementar lógica de tier automático (diário ↔ semanal) com base em data do último andamento
3. Definir limite máximo de processos com monitoramento diário por plano da Causify
4. Considerar absorver o custo de monitoramento na margem do plano em vez de cobrar por processo

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

## 6. Estratégias de Redução de Custo

As estratégias abaixo reduzem o custo da API sem expor o advogado ao risco de perder prazos. São apresentadas em ordem de facilidade de implementação.

---

### Estratégia 1 — Reduzir polling fallback de 1h para 6h

**Economia**: ~R$ 1,80/mês por advogado (-83% do custo de polling)

**Por que é seguro**: o polling é apenas um fallback para webhooks não entregues. Prazos processuais no Brasil são de 5, 10 ou 15 dias corridos — uma janela de até 6 horas para capturar um webhook perdido é completamente negligenciável.

**Implementação**: trocar o cron de `"0 * * * *"` para `"0 */6 * * *"` em `EscavadorCallbackPollingJob`.

**Risco**: praticamente nulo. O webhook em tempo real continua sendo o canal principal.

---

### Estratégia 2 — Deletar monitoramento ao arquivar processo

**Economia**: R$ 1,25/mês por processo encerrado (o maior driver de custo recorrente)

**Por que é seguro**: processo arquivado não gera novos prazos. O Escavador já envia o evento `processo_arquivado` via webhook — basta tratá-lo.

**Implementação**:
- Em `EscavadorController.ReceberCallback`, ao receber `tipo == "processo_arquivado"`:
  - Chamar `DELETE /api/v2/monitoramento-processos/{EscavadorMonitoramentoId}`
  - Setar `Processo.Monitorado = false` e `Processo.EscavadorMonitoramentoId = null`
- Job de limpeza mensal (Hangfire) para processos já arquivados no banco sem esse tratamento anterior.

**Risco**: nulo. Processo encerrado não tem prazo futuro.

---

### Estratégia 3 — Monitorar só o número OAB no Diário Oficial (não o nome)

**Economia**: ~R$ 0,60–0,70/mês por advogado (manutenção do segundo termo + metade dos callbacks)

**Por que é seguro**: o número de OAB (`123456/SP`) é específico o suficiente para capturar todas as publicações que mencionam o advogado. O nome completo gera falsos positivos em nomes comuns e dobra o custo sem aumentar a cobertura relevante.

**Implementação**: ao criar monitoramentos de Diário no `OnboardingController.Importar`, criar apenas 1 termo (OAB) em vez de 2 (OAB + nome).

**Risco**: baixo. Publicações identificam o advogado pelo número de OAB por padrão.

---

### Estratégia 4 — Suspender monitoramento de processos dormentes

**Economia**: depende do perfil — carteiras antigas podem ter 30–50% dos processos sem movimentação há mais de 6 meses

**Por que é seguro**: processos dormentes raramente têm prazo iminente. A plataforma pode pausar o monitoramento Escavador e verificar o DataJud passivamente a cada 30 dias. Se houver movimentação, reativa o monitoramento automaticamente.

**Implementação**:
- Critério de dormência: sem `Andamento` com `DataOcorrencia` nos últimos 180 dias
- Job mensal (Hangfire): para processos dormentes com `Monitorado = true`, chama `DELETE /api/v2/monitoramento-processos/{id}`, seta `Monitorado = false`
- Ao receber andamento via DataJud polling para um processo não monitorado: reativa com `POST /api/v2/monitoramento-processos`
- UI: mostrar badge "Monitoramento pausado" no processo-detalhe com botão para reativar manualmente

**Risco**: moderado — requer critério de reativação robusto. Não implementar sem o mecanismo de reativação automática.

---

---

### Estratégia 5 — Usar monitoramento semanal para processos dormentes

**Economia**: R$ 1,44/processo dormente/mês (de R$1,76 para R$0,32 = -82%)

**Por que é seguro**: processos sem movimentação há 6+ meses raramente têm prazo correndo. A atualização semanal garante detecção dentro de 7 dias — mais que suficiente para prazos de 10 ou 15 dias. Processos com prazos de 5 dias **devem permanecer em diário**.

**Implementação**:
- Job mensal (Hangfire): identifica processos com `Andamento.DataOcorrencia < hoje - 180 dias`
- Para cada um: atualiza o monitoramento Escavador para tier semanal (endpoint a confirmar na API v2)
- Ao receber novo andamento via webhook: reativa automaticamente para diário
- UI: badge "Monitoramento semanal" no processo-detalhe com botão para forçar diário

**Risco**: baixo para processos genuinamente dormentes. O trigger de reativação pelo webhook garante que, ao retomar atividade, o processo volta ao tier adequado no mesmo dia.

---

### Impacto combinado — 25 processos monitorados (todos diário como baseline)

| Estratégia | Economia/mês | Complexidade |
|---|---|---|
| 1 · Polling 6h | marginal (custo não listado) | Baixa — 1 linha |
| 2 · Auto-cancelar monitoramento ao arquivar | R$ 1,76 × proc. arquivados | Baixa — novo case no webhook |
| 3 · 1 termo Diário em vez de 2 | R$ 2,20 (economiza 1 termo) | Baixa — remover 1 chamada no onboarding |
| 4 · Suspender monitoramento de processos dormentes | R$ 1,76 × proc. dormentes (cancelar) | Média — job + reativação |
| 5 · Tier semanal para processos dormentes | R$ 1,44 × proc. dormentes | Média — job + reativação + atualização de tier |
| **4 ou 5** | **R$ 7,20 – R$ 10,56** (20–30% da carteira) | |

> **Estratégia 4 vs 5**: prefira a 5 (tier semanal) à 4 (cancelar) para processos dormentes que ainda podem ter movimentação eventual. Cancele só o que foi explicitamente arquivado (Estratégia 2).

**Custo base** (25 proc., todos diário): R$ 46,20/mês
**Com estratégias 2 + 3 + 5** (30% dormentes, 1 termo Diário): ~ R$ 46,20 − R$ 2,20 − R$ 10,56 ≈ **R$ 33,44/mês** (−28%)

---

## 7. Decisões em Aberto

| # | Questão | Opções |
|---|---|---|
| 1 | Diários: monitorar só OAB+nome ou também cada CNJ? | Só OAB+nome (2 termos, menor custo) vs. + CNJs (cobertura total, +R$ 0,75–3,00/mês por advogado) |
| 2 | Paginação no onboarding: limitar em quantas páginas? | Cap 20 (2.000 proc.) parece seguro; latência aceitável |
| 3 | Busca v1 assíncrona: automática ou sob demanda? | Recomendado: botão "Busca aprofundada" ou automático se v2 retornar < 5 resultados |
| 4 | Plano de créditos: quanto comprar? | Depende dos custos exatos do painel — confirmar antes de ir a produção |
| 5 | Polling fallback: manter 1×/hora? | Custo de GET não listado na tabela de serviços — provavelmente nulo; reduzir para 1×/6h por precaução |
| 6 | Tier semanal: critério de dormência? | Sugestão: 180 dias sem andamento; ajustar por tipo de processo (trabalhista costuma ter prazos mais curtos) |
| 7 | Atualização de tier via API: endpoint disponível? | Confirmar se `PUT /api/v2/monitoramento-processos/{id}` aceita mudança de frequência ou se é necessário recriar |

---

## Casos Reais — Análise de Custo por Carteira

> Análise feita em 2026-05-30. Hoje = 30/05/2026.

---

### Caso A — 25 processos (carteira antiga, muitos inativos)

**Dados:**

| Processo | Andamentos | Última data | Categoria |
|---|---|---|---|
| cb81e3ca | 173 | 2026-05-21 | 🔴 Ativo (18 dias) |
| 5f864304 | 107 | 2026-05-20 | 🔴 Ativo (20 dias) |
| 369bc06b | 37 | 2026-05-06 | 🔴 Ativo (34 dias) |
| 0c1167a4 | 506 | 2026-05-05 | 🔴 Ativo (35 dias) |
| ac7dbc50 | 61 | 2026-04-15 | 🔴 Ativo (55 dias) |
| 71642ada | 150 | 2026-04-14 | 🔴 Ativo (56 dias) |
| 4cdfe088 | 65 | 2026-04-09 | 🔴 Ativo (61 dias) |
| eba060d4 | 41 | 2026-03-23 | 🔴 Ativo (78 dias) |
| 27a4b5d7 | 136 | 2026-02-03 | 🟡 Dormente (116 dias) |
| 055f8beb | 88 | 2026-02-03 | 🟡 Dormente (116 dias) |
| 61e3f298 | 222 | 2026-01-28 | 🟡 Dormente (122 dias) |
| 513b3208 | 58 | 2025-08-20 | 🔵 Inativo (284 dias) |
| 6fd98f6c | 61 | 2025-05-23 | 🔵 Inativo (372 dias) |
| 96ec5bd4 | 97 | 2025-05-16 | 🔵 Inativo (379 dias) |
| 859130c0 | 280 | 2024-12-06 | 🔵 Inativo (540 dias) |
| c6ffa565 | 25 | 2022-05-25 | 🔵 Inativo (~1460 dias) |
| 8f81e92d | 151 | 2022-05-11 | 🔵 Inativo (~1474 dias) |
| 2a46d570 | 65 | 2021-07-05 | 🔵 Inativo (~1790 dias) |
| 6542551d | 69 | 2020-10-01 | 🔵 Inativo (~2050 dias) |
| de9981ab | 41 | 2020-08-25 | 🔵 Inativo (~2120 dias) |
| bef3867e | 116 | 2020-01-30 | 🔵 Inativo (~2290 dias) |
| 1f272043 | 111 | 2020-01-16 | 🔵 Inativo (~2300 dias) |
| 820c359a | 27 | 2019-01-21 | 🔵 Inativo (~2670 dias) |
| cc62a0e9 | 44 | 2018-08-29 | 🔵 Inativo (~2820 dias) |
| 2f815425 | 37 | 2018-07-19 | 🔵 Inativo (~2880 dias) |

**Classificação:**

| Categoria | Qtd | Critério | Tier |
|---|---|---|---|
| 🔴 Ativos | 15 | < 90 dias sem movimentação | Diário (R$ 1,76) |
| 🟡 Dormentes | 3 | 90–180 dias sem movimentação | Semanal (R$ 0,32) |
| 🔵 Inativos | 7 | > 180 dias sem movimentação | Semanal (R$ 0,32) |

**Custos:**

| Cenário | Ativos | Dormentes | Inativos | OAB | Total |
|---|---|---|---|---|---|
| Atual (tudo diário) | 15 × R$ 1,76 = R$ 26,40 | 3 × R$ 1,76 = R$ 5,28 | 7 × R$ 1,76 = R$ 12,32 | R$ 2,20 | **R$ 46,20** |
| Com Estratégia 5 | 15 × R$ 1,76 = R$ 26,40 | 3 × R$ 0,32 = R$ 0,96 | 7 × R$ 0,32 = R$ 2,24 | R$ 2,20 | **R$ 31,80** |

**Resultado:** Economia de R$ 14,40/mês (−31%).

---

### Caso B — 11 processos (carteira nova, poucos inativos)

**Dados:**

| Processo | Andamentos | Última data | Categoria |
|---|---|---|---|
| 28454789 | 451 | 2026-05-27 | 🔴 Ativo (3 dias) |
| 0d4967ce | 146 | 2026-05-22 | 🔴 Ativo (8 dias) |
| 662651a2 | 75 | 2026-05-21 | 🔴 Ativo (9 dias) |
| b891196b | 283 | 2026-04-23 | 🔴 Ativo (37 dias) |
| 915998a5 | 37 | 2026-03-16 | 🔴 Ativo (75 dias) |
| 6723c4a9 | 157 | 2024-09-19 | 🔵 Inativo (253 dias) |
| 0296d083 | 50 | 2023-08-25 | 🔵 Inativo (644 dias) |
| eeacdccb | 114 | 2023-02-02 | 🔵 Inativo (818 dias) |
| accc10ca | 267 | 2022-10-24 | 🔵 Inativo (954 dias) |
| 78f97055 | 52 | 2021-09-28 | 🔵 Inativo (~1270 dias) |
| d86d5e3a | 46 | 2020-08-04 | 🔵 Inativo (~1750 dias) |

**Classificação:**

| Categoria | Qtd | Critério | Tier |
|---|---|---|---|
| 🔴 Ativos | 5 | < 90 dias sem movimentação | Diário (R$ 1,76) |
| 🔵 Inativos | 6 | > 180 dias sem movimentação | Semanal (R$ 0,32) |
| 🟡 Dormentes | 0 | — | — |

**Custos:**

| Cenário | Ativos | Inativos | OAB | Total |
|---|---|---|---|---|
| Atual (tudo diário) | 5 × R$ 1,76 = R$ 8,80 | 6 × R$ 1,76 = R$ 10,56 | R$ 2,20 | **R$ 21,56** |
| Com Estratégia 5 | 5 × R$ 1,76 = R$ 8,80 | 6 × R$ 0,32 = R$ 1,92 | R$ 2,20 | **R$ 12,92** |

**Resultado:** Economia de R$ 8,64/mês (−40%).

---

## Fontes

- [Documentação API v1](https://api.escavador.com/v1/docs/)
- [Documentação API v2](https://api.escavador.com/v2/docs/)
- [Cobrança na API — Suporte](https://suporte-api.escavador.com/hc/pt-br/articles/13615780917531-Como-funciona-a-cobran%C3%A7a-na-API)
- [Cobrança v2 OAB — Suporte](https://suporte-api.escavador.com/hc/pt-br/articles/49180507067163-Entendendo-a-Cobran%C3%A7a-na-API-v2-Listar-Processos-por-Pessoa-ou-Advogado-OAB)
- [Callbacks e Buscas Assíncronas v1 — Suporte](https://suporte-api.escavador.com/hc/pt-br/articles/17873166021403-Callbacks-e-Buscas-Ass%C3%ADncronas-na-v1-do-Escavador)
- [SDK Python do Escavador (exemplos de endpoints)](https://github.com/Escavador/escavador-python)
