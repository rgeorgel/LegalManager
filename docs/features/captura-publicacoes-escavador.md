# Captura de Publicações via Escavador

> Documentação técnica completa da feature de captura automática de publicações em Diários Oficiais brasileiros via integração com a API do [Escavador](https://api.escavador.com/).

**Status**: ✅ Produção (Pro+)
**Última atualização**: 2026-06-03
**Migrations envolvidas**: `20260603001241_CapturaPublicacoesEscavador`, `20260603003553_DropProcessoMonitorado`, `20260603..._AddOabSyncFields`

---

## 1. Contexto e Motivação

A captura de publicações em Diários Oficiais é o coração do produto para advogados brasileiros: é assim que eles ficam sabendo de intimações, decisões, despachos e prazos sem precisar consultar cada tribunal manualmente.

### Antes (estado pré-Escavador)
- Tabela DJe-específica (`ProcessoMonitorado`) replicava processos cadastrados
- `CapturaPublicacaoJob` (DJe-side) fazia scraping de TJSP/TJRJ/TJMG/JusBrasil
- Pipeline em **duas etapas**: Andamento → Publicacao (frágil, perdia dados)
- Sem unificação entre fontes

### Agora (estado pós-Escavador)
- **Escavador é a fonte única** de publicações (webhook push + polling backstop)
- `Publicacao` é a entidade primária; `Andamento` é criado em paralelo (compat com `processo-detalhe.html`)
- Idempotência por `UuidExterno` (Escavador callback UUID)
- Classificação IA assíncrona (Anthropic Haiku)
- OABs cadastradas viram **monitoramentos remotos** no Escavador (Opção B / push)

---

## 2. Arquitetura Geral

```
┌─────────────────────────────────────────────────────────────────────┐
│                      CAUSIFY (nosso backend)                         │
├─────────────────────────────────────────────────────────────────────┤
│                                                                       │
│  ┌─────────────────┐    ┌──────────────────┐    ┌──────────────┐    │
│  │  TenantOabs     │    │ Publicacoes      │    │  Andamentos  │    │
│  │  (OABs locais)  │    │ (entidade única) │    │ (compat UI)  │    │
│  └────────┬────────┘    └────────▲─────────┘    └──────▲───────┘    │
│           │                       │                       │          │
│           │  POST                │  INSERT                │ INSERT  │
│           │  /api/v1/            │  Publicacao +          │ Andamento│
│           │  monitoramentos      │  Andamento             │          │
│           ▼                       │                       │          │
│  ┌─────────────────┐              │                       │          │
│  │ EscavadorClient │              │                       │          │
│  │ (HTTP + Polly)  │              │                       │          │
│  └────────┬────────┘              │                       │          │
│           │                       │                       │          │
└───────────┼───────────────────────┼───────────────────────┼──────────┘
            │                       │                       │
            │ POST                  │ POST                  │
            ▼                       ▼                       │
      ┌─────────────────────────────────────┐                 │
      │          Escavador API              │                 │
      │  https://api.escavador.com         │                 │
      │                                     │                 │
      │  • Cadastra Monitoramento TERMO    │                 │
      │  • Retorna ID + status              │                 │
      │  • Empurra publicações via webhook  │                 │
      └─────────────────┬───────────────────┘                 │
                        │                                     │
                        │ POST /api/escavador/callback       │
                        │ (Bearer + JSON payload)             │
                        │                                     │
                        ▼                                     │
      ┌─────────────────────────────────────┐                 │
      │  EscavadorController                │                 │
      │  • Valida Bearer                     │                 │
      │  • Plan-gate                         │                 │
      │  • Idempotência (UuidExterno)        │                 │
      │  • Cria Publicacao (primária)        │─────────────────┘
      │  • Cria Andamento (compat UI)        │
      │  • Notifica OAB.UserId OU Advogado  │
      │  • Ack via /api/v2/callbacks/recebidos
      └─────────────────────────────────────┘
```

### Decisão arquitetural: Webhook > Polling

| Aspecto | Webhook (push) | Polling (pull) |
|---|---|---|
| Latência | Segundos | Até 24h (1x/dia) |
| Custo de API | Grátis na entrega | Cobra por chamada |
| Dependência | URL HTTPS pública | Não precisa |
| Confiabilidade | Pode cair se webhook off | Sempre roda |

**Solução adotada**: Webhook primário + Polling como backstop. Configurável via `Escavador:ModoCaptura` (`Webhook` | `Polling` | `Hybrid` — default).

---

## 3. Componentes

### 3.1 Domain Layer

| Arquivo | Conteúdo |
|---|---|
| `Entities/TenantOab.cs` | OAB cadastrada pelo tenant (Id, TenantId, UserId?, Uf, Numero, Nome, Ativo, EscavadorMonitoramentoId?, UltimoSyncEm?, SyncError?) |
| `Entities/Publicacao.cs` | Publicacao (estendida com FonteCaptura, UuidExterno, LinkEscavador, LinkPdf, Snippet, Tribunal, DiarioSigla, etc) |
| `Enums/Enums.cs` | `FonteCaptura { DJe, Escavador, Manual }` |
| `PlanoRestricoes.cs` | `MaxOabsMonitoradas(plano)`: Pro=1, Max=3, Enterprise=3 |

### 3.2 Application Layer

| Arquivo | Conteúdo |
|---|---|
| `Interfaces/ITenantOabService.cs` | CRUD OAB + SincronizarAsync + SincronizarTodasAsync |
| `Interfaces/IEscavadorService.cs` | 9 métodos: busca, monitoramento, callbacks, movimentações, busca por OAB, criar monitoramento OAB |

### 3.3 Infrastructure Layer

| Arquivo | Conteúdo |
|---|---|
| `Escavador/EscavadorHttpClient.cs` | Implementação real (V1 + V2) |
| `Escavador/EscavadorMockClient.cs` | Mock para dev (Escavador:UseMock=true) |
| `Services/TenantOabService.cs` | CRUD + sync remoto + validação de limite |
| `Services/PublicacaoMapper.cs` | Converte EscavadorMovimentacaoDto → Publicacao |
| `Services/PublicacaoClassificacaoService.cs` | Job async que classifica publicações Escavador com Haiku |
| `Jobs/EscavadorMovimentacoesPollingJob.cs` | Polling backstop (cria Publicacao por CNJ) |
| `Jobs/EscavadorOabSyncJob.cs` | Retry diário de OABs sem sync remoto |
| `Persistence/Configurations/TenantOabConfiguration.cs` | EF mapping (índices únicos) |
| `Persistence/Configurations/PublicacaoConfiguration.cs` | EF mapping (índice único parcial UuidExterno) |

### 3.4 API Layer

| Arquivo | Endpoints |
|---|---|
| `Controllers/EscavadorController.cs` | `POST /api/escavador/callback` (webhook) |
| `Controllers/EscavadorPublicacoesController.cs` | `GET /api/escavador/publicacoes/busca?oab=&uf=&de=&ate=` |
| `Controllers/TenantOabsController.cs` | CRUD + `POST /{id}/sincronizar` + `POST /sincronizar-todas` |
| `Controllers/PublicacoesController.cs` | `GET /api/publicacoes?fonteCaptura=...` (filtro adicionado) |

### 3.5 Frontend

| Arquivo | Função |
|---|---|
| `wwwroot/pages/oabs.html` | Página de gestão de OABs |
| `wwwroot/js/oabs.js` | CRUD + sync + modal melhorado |
| `wwwroot/pages/publicacoes.html` | Lista publicações com filtro de fonte |
| `wwwroot/js/publicacoes.js` | Badges por FonteCaptura (📡 Escavador / 📰 DJe / ✍️ Manual) |
| `wwwroot/js/layout.js` | Item de menu "👨‍⚖️ OABs Monitoradas" |

---

## 4. Fluxos Detalhados

### 4.1 Cadastro de OAB → Monitoramento Remoto (push)

```
1. Usuário preenche modal em /pages/oabs.html
   - Nome, UF, Número, Advogado vinculado (opcional), Ativo
2. JS envia POST /api/tenant-oabs
3. TenantOabsController → TenantOabService.CriarAsync:
   a. ValidarPlano() (Pro/Max/Enterprise)
   b. ValidarLimiteOabs() (Pro ≤ 1, Max ≤ 3, etc)
   c. ValidarOab() (UF válida, 3-7 dígitos)
   d. Validar duplicata (UF+Numero únicos por tenant)
   e. INSERT em TenantOabs (local)
   f. SincronizarRemotoAsync() — best effort:
      - Chama IEscavadorService.CriarMonitoramentoOabAsync(uf, numero, nome)
      - POST /api/v1/monitoramentos com:
        {
          "tipo": "TERMO",
          "termo": "{numero}",
          "variacoes": ["{numero}", "{numero}/{UF}", "OAB/{UF} {numero}", "OAB {numero}/{UF}"],
          "termos_auxiliares": [{"condicao": "CONTEM", "termo": "{nome}"}]
        }
      - Se sucesso: TenantOab.EscavadorMonitoramentoId = mon.Id
      - Se falha: TenantOab.SyncError = mensagem (badge mostra "Erro de sync")
4. Retorna 201 com DTO (inclui Sincronizada=true/false, EscavadorMonitoramentoId)
5. UI atualiza tabela; badge fica "Sincronizada" se sucesso
```

**Variações da OAB** — o Escavador usa match exato no campo `termo`. Para garantir que diferentes formatos da OAB (`123456`, `123456/SP`, `OAB/SP 123456`) sejam detectados, enviamos todas como `variacoes`. O `termos_auxiliares` (nome do advogado) reduz falsos positivos.

### 4.2 Webhook → Publicacao (push primário)

```
1. Escavador detecta nova publicação para um monitoramento cadastrado
2. Escavador faz POST https://app.causify.com.br/api/escavador/callback
   Headers: Authorization: Bearer {CallbackSecret}
   Body: EscavadorCallbackWebhookPayload (V1 shape)
3. EscavadorController.ReceberCallback:
   a. ValidarCallbackToken() — compara Bearer com Escavador:CallbackSecret
      - Em produção, throw se CallbackSecret estiver vazio
   b. Extrai uuid, numeroCNJ, monitoramentoId, conteudo
   c. Encontra TenantOab por MonitoramentoId OU Processo por CNJ
      - Se nem um nem outro: 200 OK (ack silencioso)
   d. Plan-gate: PlanoRestricoes.PermiteCapturacaoPublicacoes(plano)
      - Se Free/Plus: ack silencioso (evita retry infinito)
   e. Estratégias 4 e 5 (se há Processo):
      - processo_arquivado → CancelarMonitoramentoAsync
      - MonitoramentoSemanal → UpgradeParaDiarioAsync
      - !Monitorado → ReativarMonitoramentoAsync
   f. Idempotência: SELECT WHERE UuidExterno = {uuid}
      - Se existe: ack e retorna
   g. Cria Publicacao (via PublicacaoMapper ou MontarPublicacaoOab)
   h. Cria Andamento (se há Processo) — compat com processo-detalhe.html
   i. Cria Notificacao:
      - Destinatário = OAB.UserId (se OAB) OU AdvogadoResponsavelId (se Processo)
      - OAB tem prioridade quando ambos existem
   j. SaveChangesAsync() + MarcarCallbackRecebidoAsync(uuid) — ack
4. UI mostra nova publicação em /pages/publicacoes.html com badge 📡 Escavador
```

### 4.3 Polling → Publicacao (backstop)

```
EscavadorMovimentacoesPollingJob (Hangfire, daily 06:00 UTC):
1. SELECT Processos WHERE Monitorado AND Status=Ativo
2. Para cada processo:
   a. Verifica plan (Free/Plus → skip)
   b. Calcula `desde` = MAX(Publicacoes.DataPublicacao WHERE TenantId AND NumeroCNJ) ?? now-7d
   c. GET /api/v2/processos/{cnj}/movimentacoes?de={desde}
   d. Para cada movimentação nova:
      - Dedupe por UuidExterno
      - Cria Publicacao + Andamento + Notificacao
   e. Update UltimoMonitoramento
```

**Nota**: o polling cria publicações APENAS de CNJ de Processos (não OABs). OABs são push-only via webhook.

### 4.4 Classificação IA (assíncrona)

```
PublicacaoClassificacaoService (Hangfire, every 5 min):
1. SELECT Publicacoes WHERE FonteCaptura=Escavador
                            AND ClassificacaoIA IS NULL
                            AND CapturaEm >= now-1h
   LIMIT 100
2. Para cada uma, chama Anthropic Haiku:
   Prompt: "Analise o texto da publicação e classifique como Prazo/Audiencia/Decisao/Despacho/Intimacao/Outro,
            indique se urgente (prazo < 5 dias), e dê um resumo em 1 frase"
3. UPDATE Publicacao.Tipo, Urgente, ClassificacaoIA
4. SaveChanges
```

Vantagem: webhook/polling não bloqueiam esperando a IA. Classificação roda em background.

### 4.5 Sync OAB (retry diário)

```
EscavadorOabSyncJob (Hangfire, daily 05:00 UTC, antes do polling):
1. Para cada tenant distinto com OABs ativas:
   a. SELECT TenantOabs WHERE Ativo AND (EscavadorMonitoramentoId IS NULL OR SyncError IS NOT NULL)
   b. Para cada uma, tenta criarMonitoramentoOabAsync novamente
   c. Atualiza EscavadorMonitoramentoId ou SyncError
```

Cura automática de OABs que falharam no cadastro inicial (Escavador offline, rate limit, etc).

### 4.6 Busca OAB (on-demand)

```
GET /api/escavador/publicacoes/busca?oab=&uf=&de=&ate=
1. Plan-gate
2. Resolve lista de OABs a consultar:
   - Se oab+uf na query: usa só esses
   - Senão: lista todas as TenantOabs ativas do tenant
3. Task.WhenAll: chama /api/v1/oab/{uf}/{numero}/publicacoes para cada
4. Dedupe por Uuid
5. Update UltimaVerificacao em todas as TenantOabs consultadas
6. Retorna { items, total }
```

---

## 5. API REST

### 5.1 OABs

| Método | Endpoint | Descrição | Auth | Plan |
|---|---|---|---|---|
| GET | `/api/tenant-oabs` | Lista OABs do tenant | JWT | Qualquer |
| POST | `/api/tenant-oabs` | Cria OAB (+ sync remoto) | JWT | Pro+ |
| PUT | `/api/tenant-oabs/{id}` | Atualiza OAB | JWT | Pro+ |
| DELETE | `/api/tenant-oabs/{id}` | Remove OAB (deleta remoto) | JWT | Pro+ |
| POST | `/api/tenant-oabs/{id}/sincronizar` | Retry sync individual | JWT | Pro+ |
| POST | `/api/tenant-oabs/sincronizar-todas` | Retry sync em massa | JWT | Pro+ |

**POST body**:
```json
{
  "userId": "uuid-opcional",
  "uf": "SP",
  "numero": "123456",
  "nome": "Dr. João da Silva",
  "ativo": true
}
```

**Respostas de erro**:
- `400` — UF/número inválido
- `402` — Plano não permite (Free/Plus)
- `409` — OAB já cadastrada (UF+Numero duplicado)
- `400` — Limite de OABs atingido para o plano

### 5.2 Publicações

| Método | Endpoint | Descrição |
|---|---|---|
| GET | `/api/publicacoes?fonteCaptura=Escavador&status=Nova` | Lista (com filtro de fonte) |
| GET | `/api/publicacoes/{id}` | Detalhe |
| PATCH | `/api/publicacoes/{id}/lida` | Marca como lida |
| PATCH | `/api/publicacoes/{id}/arquivar` | Arquiva |
| GET | `/api/publicacoes/nao-lidas/count` | Contador (sidebar badge) |

### 5.3 Busca Escavador

| Método | Endpoint | Descrição |
|---|---|---|
| GET | `/api/escavador/publicacoes/busca?oab=&uf=&de=&ate=` | Busca on-demand no Escavador |

### 5.4 Webhook

| Método | Endpoint | Auth | Descrição |
|---|---|---|---|
| POST | `/api/escavador/callback` | Bearer (Escavador:CallbackSecret) | Recebe publicações do Escavador |

---

## 6. Limites por Plano

| Plano | Publicações permitidas? | OABs cadastradas (max) | Monitoramentos CNJ (max) |
|---|---|---|---|
| Free | ❌ | 0 | 20 |
| Plus | ❌ | 0 | 20 |
| Pro | ✅ | 1 | 100 |
| Max | ✅ | 3 | 250 |
| Enterprise | ✅ | 3 | 500 |

**`MaxOabsMonitoradas(plano)`** — fonte única da verdade em `PlanoRestricoes.cs`. Aplicado em:
- `CriarAsync` (novo cadastro)
- `AtualizarAsync` quando inativa → ativa (ativação)

**Publicações automáticas (webhook + polling)** NÃO são limitadas por plano — uma OAB cadastrada pode gerar N publicações/dia sem custo extra para o tenant (custo é por chamada de API do Escavador, e o webhook de entrega é grátis na V1).

---

## 7. Configuração

### 7.1 `appsettings.json`

```json
{
  "Escavador": {
    "ApiToken": "",                  // Bearer token do Escavador
    "CallbackSecret": "",            // Secret do webhook (definido no painel Escavador)
    "BaseUrl": "https://api.escavador.com",
    "UseMock": false,               // true = EscavadorMockClient (dev offline)
    "ModoCaptura": "Hybrid",        // Webhook | Polling | Hybrid
    "RateLimitPerSecond": 8,        // Token-bucket (8 req/s = 480/min, abaixo do limite de 500/min)
    "PollingCron": "0 6 * * *",     // Polling backstop (CNJ, não OAB)
    "OabSyncCron": "0 5 * * *",     // Retry diário de OABs sem sync
    "OabSyncEnabled": true,         // Liga/desliga o job de sync
    "WebhookUrl": "https://app.causify.com.br/api/escavador/callback"
  }
}
```

### 7.2 Variáveis de ambiente (produção)

Sobreescrevem appsettings via double-underscore:
```bash
Escavador__ApiToken=escv_live_xxx
Escavador__CallbackSecret=um-secret-aleatorio-de-32-chars
Escavador__UseMock=false
```

### 7.3 Painel do Escavador (admin)

Configurar a URL de callback para apontar para `{BaseUrl}/api/escavador/callback` e gerar um secret. Setar `Escavador:CallbackSecret` no nosso backend com o mesmo secret.

---

## 8. Idempotência e Segurança

### 8.1 Idempotência por UUID

- Toda publicação criada via webhook/polling tem `UuidExterno` populado
- Índice único parcial: `UNIQUE (TenantId, UuidExterno) WHERE UuidExterno IS NOT NULL`
- Antes de criar, fazemos `AnyAsync(UuidExterno == uuid)` — se já existe, ack e sai
- O `EscavadorCallbackPollingJob` antigo (legado) foi removido; a deduplicação é feita via `MarcarCallbackRecebidoAsync` no webhook

### 8.2 Validação do Bearer Token (webhook)

```csharp
if (string.IsNullOrWhiteSpace(secret)) {
    if (!_env.IsDevelopment())
        throw new InvalidOperationException("Escavador:CallbackSecret não configurado em produção.");
    return true; // dev convenience
}
var auth = Request.Headers.Authorization.ToString();
return string.Equals(auth, $"Bearer {secret}", StringComparison.Ordinal);
```

Em produção, `CallbackSecret` é **obrigatório** — o app crasha no startup se estiver vazio. Em dev, aceita sem auth.

### 8.3 Plan-gate (multi-camada)

| Camada | Onde | O que faz |
|---|---|---|
| Controller (webhook) | `EscavadorController.ReceberCallback` | Free/Plus: ack silencioso, sem criar |
| Controller (busca) | `EscavadorPublicacoesController.BuscarPorOab` | Free/Plus: 402 |
| Controller (CRUD) | `TenantOabsController` | Free/Plus: 402 |
| Service (criar) | `TenantOabService.CriarAsync` | Free/Plus: throw |
| Service (atualizar) | `TenantOabService.AtualizarAsync` | Free/Plus: throw |
| Polling job | `EscavadorMovimentacoesPollingJob` | Free/Plus: skip |

---

## 9. Testes

### 9.1 Unitários (xUnit + EF InMemory)

- 725 testes passando
- Cobrem: `TenantOabService`, `PublicacaoService`, `TenantOabsController`, `EscavadorController` (parcial), `DjeAdapter` (refatorado para usar `Processo.Monitorado`)

### 9.2 Integração (xUnit + WebApplicationFactory)

- 25 testes passando
- Testam fluxos end-to-end com PostgreSQL

### 9.3 Smoke E2E (Playwright)

`tests/frontend/tests/smoke/oabs.spec.ts`:
- Login como Pro
- Navegar para `/pages/oabs.html`
- Criar OAB
- Verificar badge "Sincronizada"
- Verificar UI do modal

### 9.4 Manual (cenários recomendados)

```bash
# 1. Subir com mock
Escavador__UseMock=true dotnet run --project src/LegalManager.API

# 2. Criar OAB
curl -X POST http://localhost:5123/api/tenant-oabs \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"uf":"SP","numero":"123456","nome":"Dr. Teste","ativo":true}'
# Esperado: 201 Created, response.sincronizada=true

# 3. Simular webhook (precisa de uma OAB cadastrada primeiro)
curl -X POST http://localhost:5123/api/escavador/callback \
  -H "Authorization: Bearer test-secret" \
  -H "Content-Type: application/json" \
  -d @sample-v1-callback.json
# Esperado: 200 OK, nova Publicacao em /pages/publicacoes.html com badge Escavador

# 4. Verificar idempotência (mesmo UUID)
curl -X POST ... (mesma payload) # deve retornar 200 mas não criar nova
```

---

## 10. Troubleshooting

### Publicação não aparece após webhook
1. Verificar logs: `[Escavador] ...callback...`
2. Confirmar que `Escavador:CallbackSecret` está configurado igual ao painel do Escavador
3. Em dev, verificar `__EFMigrationsHistory` para ver se as migrations `CapturaPublicacoesEscavador` e `AddOabSyncFields` foram aplicadas
4. Conferir se a OAB tem `EscavadorMonitoramentoId` (se null, badge "Pendente" — clicar em "Sincronizar")

### OAB não sincroniza
1. Ver `SyncError` na linha da OAB (passar mouse sobre o badge)
2. Possíveis causas:
   - **Token inválido**: `Escavador:ApiToken` errado ou expirado
   - **Rate limit**: 500 req/min atingido (improvável com 1 OAB)
   - **OAB duplicada no Escavador**: o `termo` + `variacoes` já existe como monitoramento de outro tenant
3. Solução: clicar em "⟳ Sincronizar" para retry manual

### Webhook retorna 401
- Token `Escavador:CallbackSecret` no nosso backend ≠ secret configurado no painel do Escavador
- Atualizar um lado e o outro

### UI mostra "PRO" mas o cadastro funciona
- O link no sidebar está atrás de `pro: true` em `layout.js`
- OABs podem ser cadastradas via API mesmo sem o link visível (ver `PermiteCapturacaoPublicacoes` no JWT claim `plano`)

### Job de sync não roda
- Verificar `/hangfire` (autenticado) → ver se `escavador-oab-sync` está registrado
- Cron está em `appsettings.json` em `Escavador:OabSyncCron`
- Logs: `[EscavadorOabSyncJob] Concluído: N OK, M erros`

### Migrations não aplicam
- `dotnet ef database update --project src/LegalManager.Infrastructure --startup-project src/LegalManager.API`
- Se houver erro de coluna inexistente (ex: `IdExterno`): rodar `psql \d "Publicacoes"` para ver schema real e ajustar migration

---

## 11. Métricas e Observabilidade

### Logs estruturados (Serilog)

Todos os jobs e serviços logam em formato estruturado. Tags importantes:
- `[Escavador]` — chamadas HTTP ao Escavador
- `[EscavadorPolling]` — polling job
- `[EscavadorOabSyncJob]` — sync diário de OABs
- `[EscavadorCallbackPollingJob]` — (legado, removido)
- `[PublicacaoClassificacao]` — classificação IA
- `[TenantOab]` — CRUD de OABs

### Métricas sugeridas (futuro)
- `escavador.webhook.received.count` (por dia)
- `escavador.webhook.created.count` (por dia)
- `escavador.webhook.skipped.limit.count` (Free/Plus)
- `escavador.polling.duration.ms`
- `escavador.oabs.sincronizadas.count` (por dia)
- `publicacao.classificacao.ia.duration.ms`

---

## 12. Próximos Passos (TODOs conhecidos)

- [ ] Cache de `tribunais` (origens) — atualmente não cacheado, escavador retorna lista grande
- [ ] Tela de detalhes do `Publicacao` mostrando payload bruto do Escavador (`JsonBruto`)
- [ ] Refresh token do Escavador (token atual é estático)
- [ ] Notificações por email em publicações críticas (atualmente só in-app)
- [ ] Filtro por `OrigemEstado` (UF) na lista de publicações
- [ ] Sincronização bidirecional — permitir usuário editar OAB local sem perder o monitoramento remoto (hoje, edita UF/Numero/Nome → recria)
- [ ] Job de cleanup de monitoramentos órfãos no Escavador (caso delete local falhe)
- [ ] Suporte a múltiplos `Diario` no mesmo monitoramento (Escavador suporta `origens_ids`)

---

## 13. Referências

- [Escavador — Documentação V1](https://api.escavador.com/v1/docs/)
- [Escavador — Documentação V2](https://api.escavador.com/v2/docs/)
- [Escavador Python SDK (referência de paths)](https://github.com/Escavador/escavador-python)
- [CNJ Resolução 65/2008 (formato CNJ)](https://atos.cnj.jus.br/atos/detalhar/119)
- [docs/processos/escavador-fluxo-e-custos.md](./processos/escavador-fluxo-e-custos.md) — análise de custos e fluxos
- [docs/processos/importar_processos_por_oab.md](./processos/importar_processos_por_oab.md) — importação por OAB (caso de uso relacionado)
- Plano original: `C:\Users\ricky\.claude\plans\quero-implementar-a-funcionalidade-quirky-kay.md`
