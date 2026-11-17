# Ajustes na API pública do Contratos (v1)

Pedidos que saíram da integração do Causify com a API pública do Contratos (`/api/public/v1`).
Os testes foram feitos no ambiente de teste (`contratos-teste.georgel.ca`) em 16/11/2026, com uma
chave de API do plano Empresa.

| # | Ajuste | Prioridade | Quebra compatibilidade? |
|---|--------|------------|-------------------------|
| 1 | `deliveryMethod` volta como `"whats_app"` em vez de `"whatsapp"` | Alta | Não (corrige para o valor documentado) |
| 2 | Enviar contrato cancelado responde `already_sent` em vez de `already_final` | Média | Não |
| 3 | `signLink` do signatário vem sempre `null` | Alta | Não (campo já existe) |

---

## 1. `deliveryMethod` volta como `"whats_app"`

### O que acontece

A entrada aceita `"whatsapp"`, que é o valor documentado. Nas respostas, porém, o mesmo signatário
volta com `"whats_app"`:

```http
POST /api/public/v1/contracts
Content-Type: application/json

{ ..., "signers": [ { "name": "Testemunha", "phone": "+55 11 90000-0000", "deliveryMethod": "whatsapp" } ] }
```

```json
"signers": [
  { "id": "…", "name": "Testemunha", "deliveryMethod": "whats_app", "status": "pending", … }
]
```

Visto em `POST /contracts` e `GET /contracts/{id}`. Como é o mesmo objeto contrato, deve
acontecer também em `/send`, `/cancel` e no `data.contract` dos webhooks.

A API também aceita `"whats_app"` na entrada, sem dar `invalid_json`.

### Por que importa

- A documentação, a seção "Convenções" e o OpenAPI dizem `email | whatsapp | sms`.
- Quem compara com `"whatsapp"`, como diz a documentação, erra: por exemplo, ao mostrar o canal na
  tela ou ao decidir pedir telefone em vez de e-mail.
- O Causify já contorna isso: converte `whats_app` em `whatsapp` ao ler. Outros clientes da API
  não vão saber que precisam fazer o mesmo.

### Causa provável

O conversor de enum para snake_case trata `WhatsApp` como duas palavras (`Whats` + `App`).

### Correção sugerida

- Serializar o canal como `"whatsapp"` em todas as respostas e nos webhooks. Um nome explícito no
  membro do enum (atributo de nome no JSON) ou um mapeamento próprio para esse enum resolve.
- Na entrada, continuar aceitando `"whats_app"` por um tempo, para quem já se adaptou ao valor
  errado.
- Registrar a correção em "Mudanças" da documentação.

### Como conferir

1. Criar um rascunho (`"send": false`) com um signatário `"deliveryMethod": "whatsapp"`.
2. `GET /contracts/{id}` → `signers[].deliveryMethod` deve ser `"whatsapp"`.

---

## 2. Enviar um contrato cancelado responde `already_sent`

### O que acontece

```http
POST /api/public/v1/contracts/{id}/send      (contrato com status "cancelled", nunca enviado)
```

```json
{ "title": "Este contrato já foi enviado.", "status": 409, "code": "already_sent" }
```

O contrato foi cancelado ainda como rascunho, então nunca foi enviado. A mensagem e o código não
correspondem ao estado dele.

### Por que importa

- O `code` serve para o sistema decidir o que fazer. Quem trata `already_sent` como "já está em
  andamento, só acompanhe" vai esperar assinaturas de um contrato que nunca vai ser assinado.
- O `title` é mostrado ao usuário final ("pode ser mostrado ao usuário", segundo a documentação),
  e aqui ele está errado.

### Correção sugerida

Em `POST /contracts/{id}/send`, quando o contrato está em estado final, responder com o código que
já existe para isso:

| Estado atual | Resposta esperada |
|---|---|
| `sent`, `partially_signed` | 409 `already_sent` — "Este contrato já foi enviado." (como hoje) |
| `completed`, `declined`, `expired`, `cancelled` | 409 `already_final` — "Este contrato já foi finalizado." |

O código `already_final` já está na tabela de erros da documentação, hoje citado só para o
cancelamento. Basta ampliar a descrição para valer também no envio.

### Como conferir

1. Criar um rascunho (`"send": false`).
2. `POST /contracts/{id}/cancel` → 200, `status: "cancelled"`.
3. `POST /contracts/{id}/send` → deve responder 409 `already_final`.

---

## 3. `signLink` vem sempre `null`

### O que acontece

Nos contratos enviados (inclusive os concluídos, assinados por e-mail e por WhatsApp), todos os
signatários vêm com `"signLink": null` em `GET /contracts/{id}` e nos webhooks.

A documentação diz que o campo é "o link curto de assinatura quando o provedor gera um; pode vir
nulo".

### Por que importa

O Causify agora tem, no portal do cliente, um cartão **"Contratos para assinar"** com o botão
**"Assinar agora"**. O botão usa o `signLink` do signatário que corresponde ao cliente. Com o link
sempre nulo:

- o botão nunca aparece;
- o cliente só vê "o link foi enviado para o seu e-mail/WhatsApp" e precisa procurar a mensagem.

Para quem integra, ter o link permite também reenviar por um canal próprio (o portal, um app, o
WhatsApp do escritório) sem depender só do envio do Contratos.

### Correção sugerida

1. **Preencher `signLink`** quando o contrato for entregue ao provedor (evento
   `sent_to_provider` / `contract.sent`). Pela linha do tempo, o provedor é o Autentique, cuja API
   costuma devolver um link por assinatura ao criar o documento; vale confirmar na resposta que o
   Contratos já recebe.
2. **Quando o link vale:**
   - enquanto o signatário puder assinar (`pending`, `viewed`);
   - `null` depois que ele assinar ou recusar, ou quando o contrato for finalizado ou cancelado.
3. **Segurança:**
   - o link deve abrir a assinatura só daquele signatário;
   - deve deixar de funcionar com o cancelamento ou a expiração;
   - é desejável que a página de assinatura confirme a identidade (CPF, token) como já faz hoje.
4. **Se o provedor não gerar link para algum canal** (ex.: SMS), manter `null` e dizer isso na
   documentação.
5. **Opcional:** mandar um evento `signer.link_ready` (ou reaproveitar `contract.sent`) quando os
   links estiverem disponíveis, já que a entrega ao provedor "acontece em segundos" depois do envio.

### Como conferir

1. Criar e enviar um contrato (`"send": true`) com um signatário por e-mail.
2. Depois do evento `contract.sent`, `GET /contracts/{id}` → `signers[0].signLink` deve vir
   preenchido (`https://…`).
3. Depois que esse signatário assinar → `signLink` volta a `null`.

---

## Como o Causify usa esses campos (para referência)

- **`deliveryMethod`:** mostra o canal de cada signatário e decide se pede e-mail ou celular ao
  reenviar.
- **`code` dos erros 409:** `already_final` faz o Causify atualizar o estado do contrato e avisar
  que ele não pode mais ser cancelado ou enviado.
- **`signLink`:** é mostrado só para o próprio cliente, no portal do cliente, e só enquanto o
  contrato está aberto e ele ainda não assinou.
