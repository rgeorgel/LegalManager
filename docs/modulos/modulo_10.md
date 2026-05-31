# Módulo 10 — Integrações e Automações com IA

**Versão:** 1.0  
**Data de implementação:** 2026-04-22  
**Módulo de referência do plano:** MÓDULO 10 (FASE 5)

---

## Visão Geral

Este módulo implementa funcionalidades avançadas de Inteligência Artificial para automatizar tarefas repetitivas do escritório de advocacia, incluindo tradução de andamentos, geração de peças jurídicas e classificação automática de publicações.

---

## Funcionalidades Implementadas

### 10.1 — Tradução de Andamentos com IA

Traduz andamentos processuais de "juridiquês" para linguagem acessível ao cliente.

**Características:**
- Integração com LLM (Claude/OpenAI) para tradução em linguagem clara
- Opção de revisão prévia pelo advogado antes do envio ao cliente
- Sistema de créditos: **5 créditos de cortesia** (Plano Smart)
- Habilitação por cliente (`IAHabilitada` no contato)
- Envio automático da tradução por e-mail ao cliente

**Prompt base utilizado:**
> "Você é um assistente jurídico. Traduza o seguinte andamento processual para uma linguagem clara, simples e não técnica, como se o advogado estivesse explicando diretamente ao cliente. Não use termos jurídicos sem explicação. Inclua o que aconteceu e qual é o próximo passo esperado, se houver."

**Endpoints:**
```
POST /api/ia/traduzir-andamento        — Traduz andamento
GET  /api/ia/traduzir-andamento/{id}  — Obtém tradução existente
```

**Entidade:** `TraducaoAndamento`

---

### 10.2 — Gerador de Peças Jurídicas com IA

Gera rascunhos de peças processuais completas com fundamentação legal.

**Características:**
- Tipos de peça suportados: PeticaoInicial, Contestacao, Recurso, AlegacoesFinais, Pedido, Manifestacao, Memoriais
- Busca automática de jurisprudência relevante integrada
- Sugestão de teses jurídicas
- Sistema de créditos: **2 créditos de cortesia** (Plano Smart)

**Endpoints:**
```
POST /api/ia/gerar-peca              — Gera peça jurídica
GET  /api/ia/pecas-geradas          — Lista peças geradas
GET  /api/ia/pecas-geradas/{id}      — Detalhe da peça
```

**Entidade:** `PecaGerada`

---

### 10.3 — Classificação Automática de Publicações com IA

Classifica publicações capturadas automaticamente e identifica urgência.

**Classificações possíveis:**
- `Prazo` — publicação que estabelece prazo para ato processual
- `Audiencia` — pauta de audiência ou redesignação
- `Decisao` — decisão interlocutória ou sentença
- `Despacho` — simples despachos de mérito
- `Intimacao` — intimação para cumprimento de obrigação
- `Outro` — não se encaixa nas categorias acima

**Recursos adicionais:**
- Identificação de urgência automática
- Sugestão de ação/tarefa baseada na publicação

**Entidade:** `Publicacao` (campo `ClassificacaoIA` existente)

---

### 10.4 — Integração WhatsApp (Reservado para futura implementação)

Funcionalidade reservada para fase futura (não implementada nesta versão).

---

## Entidades de Domínio

### CreditoAI

Controla a quantidade de créditos disponíveis por tenant e por tipo de operação.

| Campo | Tipo | Descrição |
|-------|------|-----------|
| Id | Guid | Identificador único |
| TenantId | Guid | Tenant proprietário |
| Tipo | TipoCreditoAI | Tipo de crédito (TraducaoAndamento, GeracaoPeca, ClassificacaoPublicacao) |
| QuantidadeTotal | int | Total de créditos concedidos |
| QuantidadeUsada | int | Créditos já utilizados |
| Origem | OrigemCreditoAI | Cortesai ou Turbo |
| ExpiraEm | DateTime? | Data de expiração (null = sem expiração) |

### TraducaoAndamento

Registra cada tradução realizada.

| Campo | Tipo | Descrição |
|-------|------|-----------|
| Id | Guid | Identificador único |
| AndamentoId | Guid | Andamento traduzido |
| TenantId | Guid | Tenant |
| SolicitadoPorId | Guid | Usuário que solicitou |
| ClienteId | Guid? | Cliente que recebeu (se enviado) |
| TextoOriginal | string | Texto original do andamento |
| TextoTraduzido | string | Texto traduzido pela IA |
| EnviadoAoCliente | bool | Indica se foi enviado por e-mail |
| RevisadoPreviamente | bool | Indica se houve revisão antes do envio |
| CriadoEm | DateTime | Data/hora da criação |

### PecaGerada

Registra cada peça jurídica gerada.

| Campo | Tipo | Descrição |
|-------|------|-----------|
| Id | Guid | Identificador único |
| TenantId | Guid | Tenant |
| ProcessoId | Guid? | Processo relacionado |
| GeradoPorId | Guid | Usuário que gerou |
| Tipo | TipoPecaJuridica | Tipo da peça |
| DescricaoSolicitacao | string | Descrição da solicitação |
| ConteudoGerado | string | Conteúdo gerado pela IA |
| JurisprudenciaCitada | string? | Jurisprudência encontrada |
| TesesSugeridas | string? | Teses sugeridas |
| CriadoEm | DateTime | Data/hora da criação |

### Alterações em Entidades Existentes

#### Contato
- Adicionado campo `IAHabilitada` (bool, default false) — indica se o cliente recebe andamentos traduzidos por IA

---

## Endpoints da API

###IAController (`/api/ia`)

| Método | Endpoint | Descrição |
|--------|----------|-----------|
| POST | `/traduzir-andamento` | Traduz um andamento processual |
| GET | `/traduzir-andamento/{andamentoId}` | Obtém tradução de um andamento |
| POST | `/gerar-peca` | Gera uma peça jurídica |
| GET | `/pecas-geradas` | Lista peças geradas (com paginação) |
| GET | `/pecas-geradas/{id}` | Obtém detalhe de uma peça |

### CreditosController (`/api/creditos`)

| Método | Endpoint | Descrição |
|--------|----------|-----------|
| GET | `/` | Retorna todos os créditos do tenant |

---

## Configuração

### Variáveis de Ambiente (.env)

```env
# Provedor da IA: "Anthropic" | "OpenAI"
IA_PROVIDER=Anthropic

# Chave da API do provedor selecionado
IA_API_KEY=sk-ant-xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx

# Modelo a ser utilizado
# Anthropic: claude-3-5-sonnet-latest | claude-3-haiku-latest
# OpenAI:   gpt-4o | gpt-4o-mini | gpt-4-turbo
IA_MODEL=claude-3-5-sonnet-latest

# Timeout em segundos (opcional, padrão: 30)
# IA_TIMEOUT_SECONDS=30

# URL base da API (opcional — para proxies)
# IA_BASE_URL=https://api.anthropic.com/v1
```

---

## Créditos de IA

### Créditos Padrão (Plano Smart)

| Tipo | Quantidade | Origem |
|------|-----------|--------|
| Tradução de Andamentos | 5 | Cortesia |
| Geração de Peças | 2 | Cortesia |
| Classificação de Publicações | Ilimitado | — |

### Verificação de Créditos

Antes de qualquer operação de IA, o sistema verifica se há créditos disponíveis. Se não houver, uma exceção `InvalidOperationException` é lançada com a mensagem "Créditos de [tipo] esgotados."

---

## Novos Serviços

### IAService (Infrastructure)

Implementa `IIAService` e faz a comunicação direta com as APIs de IA (Claude ou OpenAI).

**Métodos:**
- `TraduzirTextoAsync` — traduz texto para linguagem acessível
- `GerarPecaJuridicaAsync` — gera peça jurídica
- `ClassificarPublicacaoAsync` — classifica publicação e retorna urgência
- `BuscarJurisprudenciaAsync` — busca jurisprudência sobre um tema

### CreditoService (Infrastructure)

Implementa `ICreditoService` e gerencia o ciclo de vida dos créditos.

**Métodos:**
- `ObterCreditosAsync` — retorna todos os créditos do tenant
- `TemCreditoDisponivelAsync` — verifica se há créditos disponíveis
- `ConsumirCreditoAsync` — consome créditos após uso
- `InicializarCreditosPadraoAsync` — inicializa créditos ao criar tenant

### TraducaoService (Infrastructure)

Implementa `ITraducaoService` e orquestra a tradução de andamentos.

### PecaJuridicaService (Infrastructure)

Implementa `IPecaJuridicaService` e orquestra a geração de peças.

---

## Modelo de Dados — Relacionamento

```
Tenant
  └── CreditosAI (1:N)
        └── Tipo: TraducaoAndamento | GeracaoPeca | ClassificacaoPublicacao

Tenant
  └── TraducoesAndamentos (1:N)
        ├── Andamento (N:1)
        ├── SolicitadoPor (N:1)
        └── Cliente (N:1, opcional)

Tenant
  └── PecasGeradas (1:N)
        ├── Processo (N:1, opcional)
        └── GeradoPor (N:1)
```

---

## Fluxos Principais

### Fluxo de Tradução de Andamento

```
1. Cliente (advogado) chama POST /api/ia/traduzir-andamento
2. Sistema verifica créditos disponíveis
3. IAService chama API de IA com prompt de tradução
4. Resultado salvo em TraducaoAndamento
5. Campo DescricaoTraduzidaIA do Andamento atualizado
6. Se Enviaremail=true e Cliente.IAHabilitada=true:
   - E-mail enviado ao cliente com conteúdo traduzido
7. Crédito consumido
```

### Fluxo de Geração de Peça

```
1. Cliente (advogado) chama POST /api/ia/gerar-peca
2. Sistema verifica créditos disponíveis
3. Se ProcessoId informado, contexto do processo incluído
4. IAService.geraPecaJuridicaAsync() → conteúdo da peça
5. IAService.buscarJurisprudenciaAsync() → jurisprudência
6. Resultado salvo em PecaGerada
7. Crédito consumido
```

---

## Limitações e Considerações

1. **Créditos finitos** — Sistema de créditos por tenant. Operações são bloqueadas quando créditos esgotam.

2. **Revisão obrigatória** — A opção `RevisaoPrevia` permite advogado revisar tradução antes de enviar ao cliente.

3. ** multitenancy** — Todos os serviços respeitam o `TenantContext` para isolação de dados.

4. **IA apenas para clientes habilitados** — Apenas contatos com `IAHabilitada=true` recebem traduções por e-mail.

5. **Timeout configurável** — Chamadas à API de IA têm timeout padrão de 30 segundos.

---

## Interface do Usuário (Frontend)

### Geração de Peças Jurídicas (10.2)

A funcionalidade de geração de peças é acessível a partir da tela de detalhe do processo.

#### Acesso

Na página de detalhe do processo (`processo-detalhe.html`), um botão **📄 Gerar Peça** está disponível na barra de ações do processo.

#### Modal de Solicitação

Ao clicar em "Gerar Peça", abre-se um modal com:

1. **Exibição de créditos** — mostra quantos créditos de geração estão disponíveis
2. **Tipo de Peça** — dropdown com os tipos disponíveis:
   - Petição Inicial
   - Contestação
   - Recurso
   - Alegações Finais
   - Memoriais
   - Pedido
   - Manifestação
3. **Descrição/Contexto** — textarea para descrever o objetivo da peça e fatos relevantes

#### Modal de Resultado

Após a geração, um segundo modal exibe:

1. **Badge "Peça gerada por IA"** — indicando que é um rascunho
2. **Tipo da peça** — nome do tipo gerado
3. **Conteúdo da peça** — texto completo em área rolável com fonte serifada
4. **Jurisprudência Citada** — quando disponível, exibida abaixo do conteúdo
5. **Ação Copiar Texto** — copia o conteúdo para a área de transferência

#### Estados da UI

| Estado | Comportamento |
|--------|--------------|
| Créditos disponíveis | Botão "Gerar Peça" habilitado, modal permite envio |
| Créditos esgotados | Botão "Gerar Peça" habilitado mas campos desabilitados, mensagem informativa |
| Gerando | Botão mostra spinner + "Gerando...", campos desabilitados |
| Sucesso | Modal de resultado aberto com peça gerada |
| Erro | Mensagem de erro exibida no modal, pode tentar novamente |

#### Arquivos JavaScript Envolvidos

- `wwwroot/js/ia.js` — módulo ES6 com funções para chamadas à API de IA
- `wwwroot/js/api.js` — wrapper de fetch com tratamento de token JWT

#### Fluxo de Interação

```
1. Usuário clica em "📄 Gerar Peça"
2. Sistema carrega créditos do tenant
3. Modal de solicitação abre (campos vazios)
4. Usuário seleciona tipo + descreve contexto
5. Usuário clica em "🔮 Gerar Peça"
6. Loading: spinner no botão, campos desabilitados
7. API retorna peça gerada
8. Modal de resultado exibe conteúdo + jurisprudência
9. Usuário pode copiar texto ou fechar
```

---

## Arquivos Criados/Modificados

### Novos Arquivos

**Domain:**
- `Entities/CreditoAI.cs`
- `Entities/TraducaoAndamento.cs`
- `Entities/PecaGerada.cs`
- `Enums/EnumsIA.cs`

**Application:**
- `DTOs/IA/TraducaoDto.cs`
- `DTOs/IA/PecaJuridicaDto.cs`
- `DTOs/IA/CreditoDto.cs`
- `DTOs/IA/ClassificacaoDto.cs`
- `Interfaces/IIAService.cs`
- `Interfaces/ITraducaoService.cs`
- `Interfaces/ICreditoService.cs`
- `Interfaces/IPecaJuridicaService.cs`

**Infrastructure:**
- `Services/IAService.cs`
- `Services/CreditoService.cs`
- `Services/TraducaoService.cs`
- `Services/PecaJuridicaService.cs`
- `Services/SeedService.cs`
- `Persistence/Configurations/CreditoAIConfiguration.cs`
- `Persistence/Configurations/TraducaoAndamentoConfiguration.cs`
- `Persistence/Configurations/PecaGeradaConfiguration.cs`

**API:**
- `Controllers/IAController.cs`
- `Controllers/CreditosController.cs`
- `Controllers/SeedController.cs`

**Frontend:**
- `wwwroot/js/ia.js` — novo módulo para chamadas à API de IA
- `wwwroot/pages/processo-detalhe.html` — adicionado botão "Gerar Peça", modais de geração e resultado

### Arquivos Modificados

- `Domain/Entities/Contato.cs` — adicionado campo `IAHabilitada`
- `Domain/Enums/Enums.cs` — adicionados enums de publicação
- `Application/Interfaces/IEmailService.cs` — adicionado método `EnviarAndamentoTraduzidoAsync`
- `Infrastructure/Services/EmailService.cs` — implementado novo método de e-mail
- `Infrastructure/Persistence/AppDbContext.cs` — adicionados novos DbSets
- `Infrastructure/Persistence/Configurations/ContatoConfiguration.cs` — adicionado índice e default
- `API/Program.cs` — registrados novos serviços

### Migration

- `Modulo10_IA` — cria tabelas `CreditosAI`, `TraducoesAndamentos`, `PecasGeradas` e adiciona coluna `IAHabilitada` em `Contatos`

---

## Seed — Dados de Demonstração

Para permitir que o sistema seja apresentado em demos para clientes, existem endpoints para gerar e remover dados de exemplo para um tenant específico.

### Endpoints

| Endpoint | Método | Descrição |
|----------|--------|-----------|
| `/api/seed/gerar?tenantId={guid}` | POST | Gera dados demo para o tenant |
| `/api/seed/desfazer?tenantId={guid}` | DELETE | Remove dados demo do tenant |

### Autorização

Apenas usuários com perfil **Admin** podem acessar estes endpoints.

### Dados Gerados

O endpoint de geração cria dados realistas para todas as entidades:

| Entidade | Quantidade | Descrição |
|----------|-----------|-----------|
| **Contatos** | 8 | Clientes e partes contrárias comTags (VIP, Recorrente, Novo) |
| **Processos** | 12 | Processos em diversos tribunais (TJSP, TJMG, TJRJ, TRF1, TRF3), áreas do direito, status e fases |
| **Andamentos** | 3-10 por processo | Andamentos manuais com descrições realistas |
| **Tarefas** | 2-4 por processo | Tarefas em todos os status (Pendente, EmAndamento, Concluida) com prioridades variadas |
| **Eventos** | 10 | Eventos nos próximos 30 dias (audiências, reuniões, perícias, prazos) |
| **Lançamentos Financeiros** | ~25 | Receitas (honorários) e despesas (custas, perícias, software) dos últimos 3 meses |
| **Notificações** | 5 | Notificações de demonstração |

### Validações

- **Gerar**: retorna erro se dados já existirem para o tenant
- **Desfazer**: remove todos os dados demo sem confirmação (deletar em cascata)

### Exemplo de Uso

```bash
# Gerar dados demo
curl -X POST "https://api.seudominio.com/api/seed/gerar?tenantId=3fa85f64-5717-4562-b3fc-2c963f66afa6" \
  -H "Authorization: Bearer <token_admin>"

# Remover dados demo
curl -X DELETE "https://api.seudominio.com/api/seed/desfazer?tenantId=3fa85f64-5717-4562-b3fc-2c963f66afa6" \
  -H "Authorization: Bearer <token_admin>"
```

### Serviço (Backend)

O `SeedService` (`Infrastructure/Services/SeedService.cs`) é responsável por toda a lógica de geração e remoção de dados, utilizando um `Random` com seed fixo (42) para garantir dados consistentes em todas as execuções.

---

## Endpoints Resumidos

| Endpoint | Método | Descrição |
|----------|--------|-----------|
| `/api/ia/traduzir-andamento` | POST | Traduz andamento com IA |
| `/api/ia/traduzir-andamento/{id}` | GET | Obtém tradução |
| `/api/ia/gerar-peca` | POST | Gera peça jurídica |
| `/api/ia/pecas-geradas` | GET | Lista peças geradas |
| `/api/ia/pecas-geradas/{id}` | GET | Detalhe de peça |
| `/api/creditos` | GET | Lista créditos do tenant |
| `/api/seed/gerar` | POST | Gera dados demo |
| `/api/seed/desfazer` | DELETE | Remove dados demo |

---

*Documentação gerada em: 2026-04-22*
