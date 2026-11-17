# API pública do Contratos (v1): webhooks, sites do embed e listagem por alteração

> **Entregue pelo Contratos em 17/11/2026** (veja "Mudanças" na documentação). O formato publicado
> é o deste pedido. Ainda acrescentou `include=signers` na listagem, que o Causify usa para não
> abrir cada contrato. O item da listagem traz `sendError` como booleano: quando vem `true`, o
> Causify abre o contrato para ler o motivo.

Pedido do Causify para a fase 3 da integração. Hoje quem conecta o Causify ao Contratos faz três
passos de copiar e colar: cola a chave no Causify, cadastra o webhook no Contratos e traz o segredo
de volta, e cadastra o site do Causify no embed de templates. A API pública só aceita chave de API,
e webhooks e sites do embed só existem nas rotas internas da tela (`/api/v1/integrations/...`, com
sessão de Admin).

Com as rotas abaixo, a pessoa só cola a chave e o Causify faz o resto.

| # | Pedido | Para quê | Quebra compatibilidade? |
|---|--------|----------|-------------------------|
| 1 | `GET/POST/DELETE /webhooks` e `POST /webhooks/{id}/test` | Conectar sem copiar URL e segredo | Não (rotas novas) |
| 2 | `GET/POST/DELETE /embed/origins` | Liberar o editor de templates sem passo manual | Não (rotas novas) |
| 3 | `updatedFrom` e `sort=updated_asc` em `GET /contracts`, mais `updatedAt` e `hasSignedDocument` no item | Conferência periódica mais barata | Não (filtro e campos novos) |

O Causify já está pronto para os itens 1 e 2 e detecta sozinho quando as rotas existirem.

**Enquanto não existirem:** a API responde `404 route_not_found`, e o Causify mostra os passos
manuais de hoje. Por isso, **mantenha o código `route_not_found`** para rotas que não existem.

Todas as rotas usam o mesmo Bearer `ctr_…`, os mesmos erros (Problem Details com `code`) e o mesmo
limite de 120 requisições por minuto das rotas atuais.

---

## 1. Webhooks pela API

Reaproveita o `IIntegrationService` da tela: mesmas validações, o mesmo limite de 10 webhooks por
conta e o mesmo segredo cifrado.

### `GET /webhooks`

Os webhooks da conta (os da tela e os criados pela API).

```json
[
  {
    "id": "0193b0a1-…",
    "url": "https://app.causify.com.br/api/assinatura-digital/webhook/5c1e…",
    "description": "Causify (assinatura eletrônica)",
    "events": [],
    "enabled": true,
    "createdAt": "2026-11-18T12:00:00Z",
    "lastSuccessAt": "2026-11-18T12:05:00Z",
    "lastFailureAt": null,
    "failureStreak": 0
  }
]
```

`events: []` (ou omitido) = todos os eventos, inclusive os criados no futuro, como na tela.

### `POST /webhooks`

```json
{ "url": "https://app.causify.com.br/api/assinatura-digital/webhook/5c1e…", "description": "Causify (assinatura eletrônica)" }
```

`events` é opcional: omitido = todos.

**Resposta `201`:** o segredo vem **só aqui**, como na tela.

```json
{
  "webhook": { "id": "0193b0a1-…", "url": "https://…", "description": "…", "events": [], "enabled": true, "createdAt": "…" },
  "secret": "whsec_…"
}
```

**Erros:**

| Status | `code` | Quando |
|---|---|---|
| 422 | `validation` (ou `invalid_url`) | URL não é https, aponta para rede interna etc. (mesmas regras da tela) |
| 409 | `too_many_endpoints` | A conta já tem 10 webhooks |
| 403 | `api_not_in_plan` | Como nas outras rotas |

Duas observações:

- **Não precisa impedir URL repetida.** O Causify lista os webhooks e apaga o que tiver a mesma URL
  antes de criar o novo, porque o segredo antigo não pode ser lido de novo.
- **`CreatedBy`:** pode ser o usuário dono da chave. Na tela, vale mostrar "criado pela API (chave
  Causify)" para o Admin saber de onde veio.

### `DELETE /webhooks/{id}` → `204`

O Causify chama ao desconectar ou trocar a chave. Para um id inexistente, o esperado é `404 not_found`.

### `POST /webhooks/{id}/test` → `202`

Faz a mesma coisa que o botão "Enviar teste" da tela: dispara `webhook.test`. O Causify usa no
botão "Enviar teste" da tela Integrações.

### Segurança

A chave já dá acesso a todos os contratos da conta, então poder criar um webhook com ela não abre
dados novos. Mesmo assim, duas precauções:

- Registrar na linha do tempo, ou no histórico de entregas, que o webhook foi criado pela API, e com
  qual chave.
- Ao revogar a chave, **manter** os webhooks criados por ela. É o que acontece com os da tela e evita
  surpresa. O Causify apaga o dele ao desconectar.

---

## 2. Sites do embed pela API

Reaproveita `EmbedOrigins` e a rota `/integrations/embed-origins` da tela, com o mesmo limite de 10
sites e as mesmas regras (https; `http://` só para localhost; curinga `https://*.dominio`).

### `GET /embed/origins`

```json
[ { "id": "0193…", "origin": "https://app.causify.com.br", "createdAt": "2026-11-18T12:00:00Z" } ]
```

### `POST /embed/origins`

```json
{ "origin": "https://app.causify.com.br" }
```

**Respostas:**

- `201` com o site criado.
- **Idempotente:** se o site já estiver cadastrado, responder `200` com o existente, não `409`.

**Erros:**

| Status | `code` | Quando |
|---|---|---|
| 422 | `validation` (ou `invalid_origin`) | Não é uma origem válida (tem caminho, http fora de localhost etc.) |
| 409 | `too_many_origins` | A conta já tem 10 sites |

### `DELETE /embed/origins/{id}` → `204`

Fecha as sessões abertas desse site, como a tela já faz.

### Como o Causify usa

- **Ao conectar:** lista os sites e cadastra o do Causify, se ainda não estiver na lista.
- **Ao abrir o editor:** se `POST /embed/sessions` responder `409 embed_origin_required`, o Causify
  cadastra o site e tenta de novo uma vez.
- **Ao desconectar:** o site **não** é apagado. Ele só autoriza o domínio do Causify a mostrar o
  iframe, e outra integração da mesma conta pode estar usando.

---

## 3. Conferência mais barata: listagem por data de alteração

### Como é hoje

O webhook é o caminho principal. Como reserva, a cada 2 horas o Causify confere os contratos em
aberto (e os concluídos sem PDF) com `GET /contracts/{id}`: **uma chamada por contrato**, até 300 por
rodada, todas na cota de 120 por minuto da chave.

Um escritório com 100 contratos aguardando assinatura gera cerca de 1.200 chamadas por dia. Quase
todas voltam sem mudança nenhuma.

### O que pedimos

Em `GET /contracts`:

| Parâmetro | Tipo | Descrição |
|---|---|---|
| `updatedFrom` | data e hora ISO 8601 (`2026-11-18T10:00:00Z`) | Só contratos com `updatedAt >= updatedFrom` |
| `sort` | `updated_asc` (novo valor) | Do mais antigo para o mais novo por `updatedAt`, para paginar sem perder nada |

No item da lista (`ContractListItem`), incluir:

| Campo | Para quê |
|---|---|
| `updatedAt` | Saber onde parar e onde começar na próxima rodada |
| `hasSignedDocument` | Saber se o PDF assinado já pode ser baixado, sem abrir o contrato |
| `expiresAt` (opcional) | Mostrar o prazo no painel sem abrir o contrato |

`updatedAt` deve mudar em toda alteração que gera evento: envio, visualização, assinatura, recusa,
conclusão, cancelamento, PDF pronto e falha de envio.

### Como o Causify vai usar

1. Por escritório, a cada rodada:
   `GET /contracts?updatedFrom={última conferência − 5 min}&sort=updated_asc&pageSize=100`,
   paginando até `hasMore = false`. Na maioria das rodadas é **uma chamada** por escritório.
2. Para cada contrato da lista que o Causify acompanha e cujo `updatedAt` é mais novo que o que ele
   guarda: `GET /contracts/{id}`, para ter os signatários.
3. Guarda o maior `updatedAt` visto como ponto de partida da próxima rodada.

No exemplo de 100 contratos em aberto: de cerca de 1.200 chamadas por dia para 12, mais uma por
contrato que de fato mudou.

**Alternativa sem `GET /contracts/{id}`:** incluir `signers` (resumidos) no item da lista com um
parâmetro, por exemplo `include=signers`. Se for simples, melhor ainda, mas não é necessário.

---

## Como conferir

Usar uma chave do plano Empresa no ambiente de teste.

1. **Webhooks:**
   - `POST /webhooks` com uma URL https → `201` com `secret` `whsec_…`.
   - `GET /webhooks` mostra o webhook.
   - `POST /webhooks/{id}/test` → `202`; o endereço recebe `webhook.test` assinado com o segredo.
   - `DELETE /webhooks/{id}` → `204`.
2. **Sites do embed:**
   - `POST /embed/origins` com `https://app.exemplo.com` → `201`.
   - O mesmo `POST` de novo → `200` com o mesmo id.
   - `POST /embed/sessions` com esse `parentOrigin` funciona.
3. **Listagem:**
   - Criar um contrato e anotar a hora.
   - Assinar como um signatário.
   - `GET /contracts?updatedFrom={hora anotada}&sort=updated_asc` traz o contrato, com `updatedAt`
     depois da assinatura.
   - Com `updatedFrom` posterior, a lista vem vazia.
4. **Rota inexistente:** antes de publicar, `GET /webhooks` numa versão antiga responde
   `404 route_not_found`. É o que faz o Causify mostrar o passo manual.

Registrar as novidades em "Mudanças" da documentação.

---

## Referência: código do Contratos que pode ser reaproveitado

- `IntegrationsEndpoints.cs`: as rotas internas `/integrations/webhooks` e
  `/integrations/embed-origins` já fazem tudo isso com sessão de Admin. Falta expor na API pública
  (`PublicApiEndpoints.cs`) com a chave, no formato acima.
- `IntegrationService`: tem `MaxEndpoints = 10`, `too_many_endpoints`, o segredo cifrado e
  `CreatedWebhookEndpointDto(Endpoint, Secret)`. Na API pública, o objeto se chama `webhook`.
- `EmbedOrigins.MaxPerOrganization`: as validações de origem.
