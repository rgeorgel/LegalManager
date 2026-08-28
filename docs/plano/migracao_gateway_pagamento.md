# Plano de Migração de Gateway de Pagamento — Substituição do AbacatePay

**Motivo:** AbacatePay deixou de aceitar transações via cartão de crédito. Cartão é o meio essencial para as assinaturas recorrentes (planos Plus/Pro/Max) do Causify — sem ele, não há como cobrar renovação mensal automática.

**Data:** 2026-08-27
**Status:** Decidido — **Stripe**. Implementação inicial concluída em 2026-08-27 (sem clientes pagantes, então a migração foi um corte direto, sem período de transição). Ver seção 6.

> Nota sobre a decisão: as seções 2–3 abaixo ainda registram a comparação original com gateways brasileiros (Asaas/Vindi). Na prática, como não havia clientes pagantes, o argumento de "menor atrito de migração" que favorecia um gateway BR-nativo não se aplicou — a escolha foi feita puramente por fit técnico. A assinatura recorrente do Causify sempre foi cartão-only (Pix só era usado nos checkouts avulsos), que é exatamente o ponto forte da Stripe, e a proration de upgrade passou a ser nativa da Stripe em vez de calculada manualmente no backend.

---

## 1. Como o AbacatePay é usado hoje (baseline)

Mapeamento do que precisa ser substituído, para nenhuma funcionalidade ficar pra trás:

| Funcionalidade | Onde está no código |
|---|---|
| Criar/reaproveitar cliente (nome, email, CNPJ/CPF) | `AbacatePayService.CriarOuObterClienteAsync` |
| Criar/reaproveitar produto (plano mensal, valor fixo) | `AbacatePayService.ObterOuCriarProdutoAsync` |
| Checkout de **assinatura recorrente** (cartão) | `AbacatePayService.CriarBillingAsync` → `IniciarCheckout` em `AssinaturaController` |
| Checkout **avulso** (créditos de IA, cartão + PIX) | `CriarCheckoutUnicoAsync` |
| Checkout **prorado** para upgrade de plano | `CriarCheckoutProradoAsync` |
| Cancelamento de billing/assinatura | `CancelarBillingAsync` |
| Webhook (pagamento confirmado, renovação, cancelamento) | `WebhookController.AbacatePay` — eventos `checkout.completed`, `billing.paid`, `subscription.completed/renewed/cancelled` |
| Verificação de assinatura HMAC do webhook | `AbacatePayService.VerificarAssinatura` |
| Config | `appsettings.json` → `AbacatePay:ApiKey/WebhookSecret/BaseUrl` |
| Dado persistido no tenant | `Tenant.AbacatePayBillingId`, `Tenant.PeriodoBilling`, `Tenant.BillingCycleStart` |

Pontos que qualquer substituto precisa cobrir:
1. Assinatura recorrente **com cartão** (cobrança automática mensal, retry em caso de falha, e idealmente atualização de cartão sem novo checkout).
2. Checkout avulso com **cartão + PIX** (compra de créditos de IA).
3. Suporte a **upgrade prorado** (cobrança única de valor calculado + troca do plano recorrente).
4. **Webhooks assinados** (HMAC ou equivalente) para os eventos de pagamento confirmado/renovado/cancelado.
5. Cliente com CNPJ/CPF (`taxId`) — obrigatório para NF-e/nota fiscal e para AbacatePay/gateways BR.
6. Moeda BRL, valores em centavos.

---

## 2. Opções avaliadas

Critério: gateways que operam no Brasil, aceitam cartão de crédito recorrente + PIX, têm API REST documentada e suportam webhooks — foco em SaaS B2B de pequeno/médio porte.

### 2.1 Especialistas em assinatura recorrente (Brasil)

| Gateway | Cartão recorrente | PIX/Boleto | API de assinatura nativa | Observações |
|---|---|---|---|---|
| **Vindi** (Locaweb) | ✅ (tokenização, retry automático, dunning) | ✅ | ✅ Nativa — feita para SaaS | Focada 100% em recorrência. Boa opção "trocar por equivalente". Taxas por transação + possível mensalidade. |
| **Asaas** | ✅ | ✅ | ✅ (assinaturas + cobrança única) | Muito usada por SaaS pequenos/médios no BR, API simples, sandbox bom, split de pagamento nativo (útil se algum dia tiver comissionamento). |
| **Iugu** | ✅ | ✅ | ✅ | Similar ao Vindi/Asaas, boa doc, usada por bastante SaaS B2B. |
| **Pagar.me** (Stone) | ✅ | ✅ | ✅ (subscriptions API) | Backing da Stone, robusto, boa reputação de estabilidade; onboarding/KYC pode ser mais burocrático. |

### 2.2 Gateways globais com presença forte no Brasil

| Gateway | Cartão recorrente | PIX/Boleto | API de assinatura nativa | Observações |
|---|---|---|---|---|
| **Stripe** | ✅ (Subscriptions API é referência de mercado) | ✅ Pix (via Stripe Brasil, cobertura ainda mais nova) | ✅ Excelente — Products/Prices/Subscriptions/Invoices | Melhor DX/documentação do mercado, SDKs oficiais .NET. Liquidação em BRL direto para conta BR desde a expansão local. Boa escolha se quiser internacionalizar depois. |
| **Mercado Pago** | ✅ (Preapproval/assinaturas) | ✅ | ✅ (parcial — API de assinatura menos madura que as acima) | Marca muito reconhecida pelo usuário final (confiança na hora de digitar cartão), Checkout Pro pronto. |

### 2.3 Descartadas para este caso
- **PagBank/PagSeguro, Cielo, GetNet**: focados em adquirência tradicional/e-commerce, API de assinatura mais burocrática, menos aderente a SaaS.
- **Efí (Gerencianet)**: forte em Pix/boleto, cartão recorrente existe mas é menos o foco do produto.

### 2.4 Recomendação

**Curto prazo (menor esforço de migração, perfil parecido com AbacatePay):** **Asaas** ou **Vindi**.
Ambos têm modelo de API parecido com o do AbacatePay (customers → products/plans → checkouts/subscriptions → webhooks), CNPJ/CPF nativo, PIX + cartão no mesmo checkout, e são usados por muitos SaaS brasileiros de porte parecido ao Causify. **Asaas** tende a ter o onboarding mais rápido (aprovação de conta) e um free tier de testes mais simples; **Vindi** é mais "enterprise" em recorrência (dunning, retry automático de cartão recusado).

**Médio/longo prazo, se o produto for crescer/internacionalizar:** **Stripe**. Melhor API, documentação e SDK .NET do mercado, e hoje já liquida em BRL para contas brasileiras. Custo de migração é o mesmo porte dos outros (é preciso reescrever a camada `IAbacatePayService`), mas o resultado fica mais fácil de manter e evoluir (ex.: portal de billing pronto, faturas em PDF automáticas, upgrade/downgrade nativo com proration calculado pela própria Stripe — hoje isso é feito manualmente no `AssinaturaController`).

> Sugestão prática: escolher **1 gateway BR (Asaas ou Vindi) para migrar já** (resolve o bloqueio de cartão com menor atrito) e manter a avaliação da Stripe como evolução futura, já que a arquitetura proposta abaixo isola o gateway atrás de uma interface — trocar de novo no futuro fica barato.

---

## 3. Plano de substituição

### Princípio geral
Manter a interface `IAbacatePayService` (ou renomeá-la para algo neutro, ex. `IPaymentGatewayService`) e trocar apenas a implementação. Isso limita o blast radius a: 1 novo `Service`, ajustes no `WebhookController`, config, e migração de dados (`Tenant.AbacatePayBillingId` → id genérico).

### Fase 0 — Preparação (sem impacto em produção)
1. Criar conta sandbox no gateway escolhido (Asaas ou Vindi), validar taxas, prazos de repasse e KYC/documentação necessária para o CNPJ do Causify.
2. Confirmar com o gateway: suporte a cartão recorrente tokenizado, webhook assinado (HMAC ou similar), split não é necessário hoje mas verificar se está disponível para o futuro.
3. Definir nome genérico para abstração: `IPaymentGatewayService` (evita reengessar tudo em "AbacatePay" de novo).

### Fase 1 — Abstração e nova implementação
1. Renomear/generalizar a interface `IAbacatePayService` → `IPaymentGatewayService` (mesmos métodos: `CriarBillingAsync`, `CancelarBillingAsync`, `CriarCheckoutUnicoAsync`, `CriarCheckoutProradoAsync`), mantendo os DTOs de input/output (`CriarBillingInput`, `AbacatePayBillingResult` → renomear para `GatewayCheckoutResult`, por ex.).
2. Implementar `AsaasService` (ou `VindiService`) seguindo o mesmo contrato:
   - `CriarOuObterClienteAsync` → endpoint de customers do novo gateway (equivalente a `customers/create` do Abacate).
   - `ObterOuCriarProdutoAsync` → plano recorrente (`subscriptions` no Asaas usa `billingType: CREDIT_CARD` + `cycle: MONTHLY` direto na assinatura, sem produto separado — ajustar modelagem).
   - `CriarCheckoutUnicoAsync` → cobrança avulsa (Asaas: `payments` com `billingType: UNDEFINED` para permitir escolha de cartão/Pix no checkout).
   - `CriarCheckoutProradoAsync` → cobrança avulsa de valor calculado (igual ao acima) + atualização da assinatura recorrente para o novo valor/plano.
   - `CancelarBillingAsync` → cancelamento de assinatura/cobrança.
   - Verificação de assinatura de webhook conforme o mecanismo do novo gateway (Asaas usa token fixo no header `asaas-access-token`; Vindi usa Basic Auth + validação por IP/token configurável — checar doc atualizada no momento da implementação).
3. Registrar `AsaasService`/`VindiService` no DI (`Program.cs`), com `HttpClient` nomeado e header de autenticação vindo de `appsettings` (`Asaas:ApiKey`, `Asaas:BaseUrl`, `Asaas:WebhookToken` — nomes equivalentes ao bloco `AbacatePay` atual).
4. Ajustar `WebhookController`:
   - Novo endpoint `api/webhooks/asaas` (manter `api/webhooks/abacatepay` funcionando em paralelo durante a transição — ver Fase 3).
   - Mapear eventos do novo gateway para os mesmos handlers já existentes (`HandlePagamentoConfirmado`, `HandleSubscriptionCancelada`, `HandleCreditosComprados`), adaptando os nomes de evento (ex.: Asaas manda `PAYMENT_CONFIRMED`, `PAYMENT_RECEIVED`, `PAYMENT_OVERDUE` em vez de `checkout.completed`/`billing.paid`).

### Fase 2 — Dados e modelo
1. Migração EF: renomear coluna `Tenant.AbacatePayBillingId` → `Tenant.BillingId` (ou manter e apenas parar de usá-la para novos tenants — mais seguro para não quebrar histórico/relatórios).
2. Manter os registros históricos de `Faturamentos` como estão (não dependem do gateway específico).
3. Não é necessário migrar assinaturas ativas retroativamente no gateway novo automaticamente — ver estratégia de corte na Fase 3.

### Fase 3 — Corte de tráfego (rollout)
Duas estratégias possíveis:

- **Corte direto (recomendado dado o volume provavelmente pequeno de tenants pagantes):**
  1. Fixar uma data de corte. A partir dela, todo **novo** checkout (`IniciarCheckout`, `ComprarCreditos`) usa o novo gateway.
  2. Assinaturas que já estavam ativas no AbacatePay continuam recebendo renovação por lá **até vencerem** (o AbacatePay ainda deve aceitar Pix/renovações já criadas, mesmo sem aceitar novo cartão — confirmar com o suporte deles antes de decidir).
  3. Quando o tenant precisar renovar ou fizer upgrade, o sistema já direciona pro novo gateway (o código de `IniciarCheckout` já cancela billing anterior antes de criar um novo — esse fluxo se mantém, só troca o service).
  4. Manter os dois webhooks (`/api/webhooks/abacatepay` e o novo) ativos durante um período de transição (30-60 dias) até zerar assinaturas antigas.

- **Migração ativa (mais trabalhoso, mas zera dependência do AbacatePay mais rápido):**
  1. Contatar tenants com assinatura ativa por email pedindo para recadastrar o cartão no novo checkout (reautorização é praticamente obrigatória entre gateways diferentes — não dá para portar token de cartão de um provedor para outro).
  2. Rodar um job que identifica tenants com `Status = Ativo` e `AbacatePayBillingId != null`, ordenados por `BillingCycleStart`, e dispara um checkout de "atualização de forma de pagamento" alguns dias antes do vencimento de cada um.

Dado o contexto (poucos tenants pagantes, provavelmente), a estratégia de **corte direto com transição de 30-60 dias** é a mais simples e de menor risco.

### Fase 4 — Testes
1. Testes de integração (`tests/LegalManager.IntegrationTests`) cobrindo o novo `AsaasService`/`VindiService` com HttpClient mockado (padrão já usado para outros serviços externos do projeto, ex. Escavador/DataJud).
2. Testar manualmente em sandbox do gateway escolhido: checkout de assinatura nova, checkout de créditos avulso (cartão e Pix), upgrade prorado, cancelamento, e os 3 eventos de webhook.
3. Rodar `.\scripts\test-frontend.ps1` para garantir que a tela `pages/assinatura.html` continua funcionando (ela consome `checkoutUrl` retornado pela API — não deve precisar mudar, já que o contrato do endpoint REST do Causify não muda).

### Fase 5 — Descomissionar AbacatePay
1. Quando não houver mais nenhum tenant com `AbacatePayBillingId` ativo, remover `AbacatePayService`, o endpoint `api/webhooks/abacatepay` e as configs `AbacatePay:*`.
2. Atualizar este documento com o resultado final.

---

## 4. Esforço estimado

| Item | Estimativa |
|---|---|
| Conta + sandbox no novo gateway | 0,5 dia (depende do KYC do gateway) |
| Nova implementação do service + DI + config | 1,5–2 dias |
| Ajustes no `WebhookController` (mapeamento de eventos) | 0,5–1 dia |
| Migração de dados (`Tenant`) | 0,5 dia |
| Testes de integração + manuais em sandbox | 1 dia |
| Rollout com período de transição | Sem esforço extra de dev, só monitoramento |
| **Total dev** | **~4-5 dias úteis** |

---

## 5. Próximos passos imediatos (histórico — ver seção 6 para o que foi feito)
1. ~~Decidir entre Asaas e Vindi~~ → decidido: Stripe (seção 1, nota acima).
2. ~~Confirmar com o AbacatePay se assinaturas já ativas continuam renovando~~ → não se aplicou, sem clientes pagantes.
3. ~~Abrir branch e começar pela Fase 1~~ → feito diretamente em `main` (ver seção 6).

---

## 6. Implementação — o que foi feito (2026-08-27)

Como não havia clientes pagantes, foi feito um **corte direto**: o AbacatePay foi removido por completo na mesma mudança (sem período de transição/Fase 5 separada).

**Arquivos principais:**
- `src/LegalManager.Application/Interfaces/IStripeService.cs` — nova interface (métodos: `CriarCheckoutAssinaturaAsync`, `AtualizarAssinaturaAsync`, `CancelarAssinaturaAsync`, `CriarCheckoutAvulsoAsync`).
- `src/LegalManager.Infrastructure/Services/StripeService.cs` — implementação com `Stripe.net` (pacote adicionado ao `LegalManager.Infrastructure.csproj`).
- `src/LegalManager.API/Controllers/AssinaturaController.cs` — `AssinaturaController` e `WebhookController` (`POST /api/webhooks/stripe`) reescritos para Stripe.
- `src/LegalManager.Domain/Entities/Tenant.cs` — novos campos `StripeCustomerId`/`StripeSubscriptionId` (campo `AbacatePayBillingId` mantido só como legado histórico, sem uso).
- Migration EF `AddStripeFieldsToTenant`.
- `src/LegalManager.API/wwwroot/pages/assinatura.html` e `superadmin/tenants.html` — ajustados para o novo fluxo.
- `appsettings.json`, `docker-compose.yml`, `.env` / `.env.example` — bloco `AbacatePay:*` trocado por `Stripe:ApiKey` / `Stripe:WebhookSecret`.
- Removidos: `AbacatePayService.cs`, `IAbacatePayService.cs`, `AbacatePayServiceTests.cs`.

**Mudança de design importante — upgrade/downgrade de plano:**
O upgrade deixou de ser "cancela assinatura antiga + novo checkout prorado + recria assinatura no webhook" (fluxo do AbacatePay). Agora:
1. `POST /api/assinatura/iniciar` calcula o preview local (crédito/valor prorado, sem chamar a Stripe) e retorna `requerConfirmacao: true` quando o tenant já tem assinatura Stripe ativa.
2. `POST /api/assinatura/confirmar-upgrade` chama `Subscription.Update` da Stripe com `proration_behavior: create_prorations` — a Stripe calcula a proration de verdade e cobra na hora, no cartão já salvo, sem redirect.

Para tenant **sem** assinatura ativa (primeira assinatura), o fluxo continua com redirect para Checkout Session hospedada pela Stripe (`checkoutUrl`), igual ao padrão antigo do AbacatePay.

**O que falta para ir para produção:**
1. Colar a chave de teste da Stripe (`sk_test_...`) em `.env` (`STRIPE_API_KEY`) ou `Stripe__ApiKey` na sua sessão local — o valor já está vazio no `appsettings.json`/`.env.example` por segurança.
2. Criar um webhook endpoint no [Dashboard da Stripe](https://dashboard.stripe.com/test/webhooks) apontando para `.../api/webhooks/stripe`, assinando os eventos `checkout.session.completed`, `invoice.paid`, `customer.subscription.deleted`, e colar o "Signing secret" em `STRIPE_WEBHOOK_SECRET`.
3. Rodar a migration (`dotnet ef database update`) contra o banco local/staging.
4. Testar o fluxo ponta a ponta em modo teste: assinar um plano (cartão de teste `4242 4242 4242 4242`), confirmar upgrade, cancelar, comprar créditos avulsos.
5. Antes de ir para produção: trocar `sk_test_...` pela chave live e reconfigurar o webhook endpoint para a URL de produção com a chave live.
6. Cobertura de teste: `tests/LegalManager.UnitTests/NewControllerTests.cs` foi reescrito para `IStripeService`/Stripe (39 testes, todos passando). Não há testes de integração dedicados ao `StripeService` em si ainda (o padrão do projeto para isso, usado no `AbacatePayServiceTests` antigo, seria mockar o `HttpMessageHandler` por trás do `StripeClient`).
