# Plano de Implementação: Sistema de Gestão para Escritórios de Advocacia
**Referência de mercado:** Astrea (Aurum) — Plano Smart
**Versão do plano:** 1.2
**Data:** 2026-04-19

---

## Contexto e Objetivo

Este documento descreve o plano de implementação de um sistema SaaS de gestão jurídica para escritórios de advocacia, com funcionalidades equivalentes ao **Plano Smart do Astrea** (aurum.com.br). O sistema deve atender escritórios de pequeno e médio porte, com suporte a até 5 usuários simultâneos, até 500 processos monitorados e 20 GB de armazenamento em nuvem por tenant.

O plano está estruturado em **módulos funcionais**, cada um com seus requisitos detalhados, para que uma IA de desenvolvimento possa implementar cada parte de forma independente e incremental.

---

## Visão Geral da Arquitetura

```
┌─────────────────────────────────────────────────────────┐
│           Frontend Web (HTML + CSS + JS Vanilla)         │
│        (Responsivo — mobile-first, sem framework)        │
└────────────────────────┬────────────────────────────────┘
                         │ HTTP/REST (JSON)
┌────────────────────────▼────────────────────────────────┐
│              ASP.NET Core 10 Web API                     │
│         (Controllers + Minimal APIs + Middleware)        │
└──┬──────────┬──────────┬──────────┬──────────┬──────────┘
   │          │          │          │          │
┌──▼──┐  ┌───▼──┐  ┌────▼──┐  ┌───▼──┐  ┌────▼──┐
│Auth │  │Proc. │  │Finan. │  │IA/ML │  │Notif. │
│JWT  │  │Serv. │  │Serv.  │  │Serv. │  │Serv.  │
└─────┘  └──────┘  └───────┘  └──────┘  └───────┘
              │                    │
     ┌────────▼──────┐    ┌───────▼────────┐
     │  PostgreSQL   │    │  Hangfire Jobs  │
     │  (EF Core 10) │    │ (scraping async)│
     └───────────────┘    └────────────────┘
```

### Stack tecnológica definida

| Camada | Tecnologia | Observações |
|--------|-----------|-------------|
| **Backend** | C# com .NET 10 (ASP.NET Core Web API) | Arquitetura em camadas: API → Application → Domain → Infrastructure |
| **Frontend** | HTML5 + CSS3 + JavaScript (Vanilla) | Sem frameworks. Responsivo com CSS Grid/Flexbox. Fetch API para chamadas REST |
| **ORM** | Entity Framework Core 10 | Code-first com Migrations |
| **Banco de dados** | PostgreSQL | Multitenancy por `TenantId` em todas as tabelas (Row-Level Security) |
| **Armazenamento de arquivos** | Oracle Cloud Infrastructure (OCI) Object Storage — S3 Compatibility API | Para documentos e uploads. Endpoint: `https://<namespace>.compat.objectstorage.<region>.oraclecloud.com`. Usa AWS SDK for .NET (`AWSSDK.S3`) com endpoint customizado |
| **Filas/Jobs assíncronos** | Hangfire + PostgreSQL | Para scraping de tribunais, envio de e-mails, régua de cobrança |
| **Autenticação** | ASP.NET Core Identity + JWT Bearer | Tokens JWT gerados pelo próprio .NET. Sem OAuth externo inicialmente |
| **Pagamentos** | AbacatePay API | PIX QR Code, Checkout transparente, Webhooks, Assinaturas |
| **IA** | Anthropic API (Claude) ou OpenAI | HttpClient nativo do .NET para chamadas REST à API |
| **E-mail transacional** | Resend (`dotnet add package Resend`) | SDK oficial .NET. Registro via `IResend` no DI. Suporta HTML, anexos, webhooks de entrega |
| **Cache** | IMemoryCache (.NET nativo) ou Redis | Cache de consultas frequentes (andamentos, indicadores) |
| **Logging** | Serilog + Seq (ou arquivo estruturado) | Logs estruturados em JSON |

Toda a infra deve ser executada com docker compose para simplificar o setup

### Estrutura de projeto sugerida (.NET Solution)

```
LegalManager.sln
├── src/
│   ├── LegalManager.API            # ASP.NET Core — Controllers, Middleware, Swagger
│   ├── LegalManager.Application    # Use Cases, Services, DTOs, Interfaces
│   ├── LegalManager.Domain         # Entidades, Value Objects, Enums, Domain Events
│   ├── LegalManager.Infrastructure # EF Core, Repositórios, AbacatePay, Hangfire, Email
│   └── LegalManager.Web            # Frontend estático (HTML/CSS/JS) servido pelo próprio .NET
├── tests/
│   ├── LegalManager.UnitTests
│   └── LegalManager.IntegrationTests
└── docker-compose.yml              # PostgreSQL + MinIO + Seq para desenvolvimento local
```

### Convenções do frontend (HTML/CSS/JS Vanilla)

- **Estrutura de arquivos:** `wwwroot/` servido pelo ASP.NET Core como arquivos estáticos
- **CSS:** Arquivo global `styles.css` com variáveis CSS (`--color-primary`, etc.) + arquivos por módulo
- **JavaScript:** Módulos ES6 (`type="module"`), sem bundler. Cada módulo é um arquivo `.js`
- **Comunicação com API:** `fetch()` nativo com interceptors manuais para injeção do token JWT
- **Responsividade:** CSS Grid para layouts, Flexbox para componentes, media queries para breakpoints
- **Breakpoints padrão:** Mobile `<768px`, Tablet `768–1024px`, Desktop `>1024px`
- **Sem dependências externas** além de bibliotecas pontuais via CDN quando necessário (ex: Chart.js para gráficos)

---

## Módulos do Sistema

### MÓDULO 1 — Autenticação e Gestão de Tenants

**Descrição:** Controle de acesso multiusuário por escritório (tenant). Cada escritório é um tenant isolado. Autenticação 100% gerenciada pelo ASP.NET Core Identity com tokens JWT.

**Implementação de autenticação (.NET):**
- Usar **ASP.NET Core Identity** para gerenciamento de usuários (hash de senha, lockout, etc.)
- Geração de **JWT Bearer Token** com `Microsoft.AspNetCore.Authentication.JwtBearer`
- Claims incluídas no token: `sub` (userId), `tenantId`, `role`, `email`, `exp`
- **Refresh Token** armazenado no banco e rotacionado a cada renovação
- Configuração no `Program.cs`:
  ```csharp
  builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
      .AddJwtBearer(options => {
          options.TokenValidationParameters = new TokenValidationParameters {
              ValidateIssuer = true,
              ValidateAudience = true,
              ValidateLifetime = true,
              ValidateIssuerSigningKey = true,
              // ...
          };
      });
  ```
- Middleware customizado de **tenant resolution**: extrai `tenantId` do JWT e injeta no contexto da requisição
- Proteção de rotas via `[Authorize]` e `[Authorize(Roles = "Admin")]`

**Funcionalidades:**
- Cadastro de novo escritório (tenant) com plano de assinatura
- Login por e-mail/senha — retorna JWT (access token 1h) + refresh token (7 dias)
- Renovação de token via `/auth/refresh`
- Logout (revogação do refresh token no banco)
- Gerenciamento de usuários do escritório (convidar, remover, desativar)
- Convite por e-mail: link com token temporário de ativação
- Perfis e permissões por usuário:
  - `Admin` — acesso total ao escritório
  - `Advogado` — acesso a processos e atividades próprias e do escritório
  - `Colaborador` — acesso restrito a tarefas delegadas
  - `Cliente` — acesso somente ao Portal do Cliente (ver Módulo 8)
- Recuperação de senha por e-mail (token de reset com validade de 1h)
- Período de trial gratuito de 10 dias com acesso completo
- Limite de usuários por plano (Plano Smart: 5 usuários)
- **Frontend:** formulário de login em HTML puro, token JWT armazenado em `sessionStorage`, injetado como `Authorization: Bearer <token>` em todo `fetch()`

**Modelos de dados principais (EF Core):**
```csharp
// Tenant
public class Tenant {
    public Guid Id { get; set; }
    public string Nome { get; set; }
    public string? Cnpj { get; set; }
    public PlanoTipo Plano { get; set; }
    public StatusTenant Status { get; set; }
    public DateTime CriadoEm { get; set; }
    public DateTime? TrialExpiraEm { get; set; }
}

// Usuario — estende IdentityUser
public class Usuario : IdentityUser<Guid> {
    public Guid TenantId { get; set; }
    public string Nome { get; set; }
    public PerfilUsuario Perfil { get; set; }
    public bool Ativo { get; set; }
    public Tenant Tenant { get; set; }
}

// RefreshToken
public class RefreshToken {
    public Guid Id { get; set; }
    public Guid UsuarioId { get; set; }
    public string Token { get; set; }
    public DateTime ExpiresAt { get; set; }
    public bool Revogado { get; set; }
}
```

---

### MÓDULO 2 — Gestão de Contatos e Clientes

**Descrição:** CRM jurídico para centralizar todas as informações de clientes, partes e contatos relevantes.

**Funcionalidades:**
- Cadastro de contatos: pessoa física e jurídica
- Campos: nome, CPF/CNPJ, OAB (se advogado), e-mail, telefone, endereço, data de nascimento, observações
- Classificação por tipo: cliente, parte contrária, testemunha, perito, etc.
- Histórico completo de atendimentos vinculados ao contato
- Histórico de processos vinculados ao contato
- Histórico financeiro (honorários, recebimentos) vinculado ao contato
- Busca e filtro avançado por nome, CPF/CNPJ, processo
- Etiquetas (tags) customizáveis para classificação
- Campo de observações e notas internas
- Indicadores de processo por cliente (ver Módulo 9)
- Exportação de lista de contatos (CSV/PDF)

**Modelos de dados principais:**
```
Contato {
  id, tenantId, tipo (PF|PJ), nome, cpfCnpj,
  email, telefone, endereco, tags[], ativo,
  notificacaoHabilitada, criadoEm
}
Atendimento { id, tenantId, contatoId, usuarioId, descricao, data }
```

---

### MÓDULO 3 — Gestão de Processos e Casos

**Descrição:** Módulo central do sistema. Centraliza todo o ciclo de vida de um processo judicial ou caso jurídico.

**Funcionalidades:**

#### 3.1 Cadastro de Processos
- Cadastro manual de processo com número CNJ
- Campos: número do processo, tribunal, vara, comarca, área do direito, tipo de ação, fase processual
- Vinculação a clientes (autor, réu, interessado)
- Vinculação a advogados responsáveis do escritório
- Campo de valor da causa
- Status do processo: ativo, suspenso, arquivado, encerrado
- Registro de decisão e resultado no encerramento
- Campo de observações internas
- Processos cadastrados ilimitados

#### 3.2 Monitoramento Automático de Andamentos
- Busca automática de andamentos via robôs por número CNJ ou OAB
- Integração com APIs/scrapers dos tribunais brasileiros (TJs, TRFs, TSTRs, STF, STJ, etc.)
- Atualização diária dos andamentos
- Suporte a processos em segredo de justiça (com senha)
- Limite de processos monitorados por plano (Plano Smart: 500)
- Notificação de novas movimentações por e-mail

#### 3.3 Andamentos Manuais
- Registro manual de andamentos/movimentações
- Campo de data, tipo, descrição e responsável

#### 3.4 Captura de Publicações e Intimações
- Captura automática de publicações dos Diários Oficiais (DJE, DJSP, DOU, etc.)
- Filtro por nome do advogado/parte (nomes cadastrados no plano)
- Classificação automática por tipo: prazo, audiência, decisão, despacho
- Plano Smart: 3 nomes para captura de publicações
- Tratamento de publicações com IA (identificação automática de tipo e urgência)

#### 3.5 Controle de Prazos Processuais
- Calculadora de prazos processuais integrada (dias úteis, suspensões, feriados)
- Alerta automático de prazos vencendo
- Prazo vinculado a tarefas e audiências
- Visualização de prazos em calendário

#### 3.6 Documentos do Processo
- Upload e organização de documentos por processo
- Tipos: petições, decisões, contratos, provas, etc.
- Armazenamento em **OCI Object Storage** via S3 Compatibility API (ver Módulo 13)
- 20 GB de cota de armazenamento por tenant (Plano Smart)
- Integração com Google Drive (opcional)
- Criação de modelos de documentos reutilizáveis com variáveis dinâmicas (ex: nome do cliente, número do processo)
- Busca por nome de arquivo e tipo

**Modelos de dados principais:**
```
Processo {
  id, tenantId, numeroCNJ, tribunal, vara, comarca,
  areaDosDireito, tipoAcao, fase, status,
  valorCausa, clienteId, advogadoResponsavelId,
  monitorado, criadoEm, encerradoEm, decisao, resultado
}
Andamento { id, processoId, data, tipo, descricao, fonte (auto|manual), traduzidoIA }
Publicacao { id, tenantId, processoId, diario, data, conteudo, classificacaoIA, tipo }
Documento { id, processoId, nome, url, tamanho, tipo, criadoEm }
ModeloDocumento { id, tenantId, nome, conteudo, variaveis[] }
```

---

### MÓDULO 4 — Gestão de Atividades (Tarefas, Eventos e Agenda)

**Descrição:** Controle completo das atividades do escritório, incluindo tarefas, audiências, reuniões e prazos.

**Funcionalidades:**

#### 4.1 Tarefas
- Criação de tarefas vinculadas a processos, clientes ou de forma independente
- Campos: título, descrição, responsável, prazo, prioridade, status
- Delegação de tarefas entre usuários do escritório
- Comentários e menções (@usuário) em tarefas
- Etiquetas (tags) para classificação
- Atividades predefinidas (templates de checklist por tipo de caso)
- Filtro de atividades por equipe
- Gráfico de atividades por equipe

#### 4.2 Eventos e Audiências
- Cadastro de audiências, reuniões, perícias vinculadas a processos
- Campos: data, hora, local, tipo, responsável, observações
- Centralização de atividades dentro de prazos e audiências
- Notificação antecipada (ex: 1 dia antes, 1 hora antes)

#### 4.3 Agenda e Calendário
- Visualização em calendário mensal, semanal e diário
- Integração com Google Calendar (opcional)
- Visualização de todos os eventos, tarefas e prazos em um único calendário
- Filtro por usuário, processo, área

#### 4.4 Gestão Kanban
- Visualização kanban das atividades com colunas customizáveis
- Colunas padrão: A Fazer / Fazendo / Concluído
- Drag-and-drop entre colunas
- Filtro por responsável, processo, etiqueta

**Modelos de dados principais:**
```
Tarefa {
  id, tenantId, processoId, clienteId, titulo, descricao,
  responsavelId, prazo, prioridade, status, tags[], colunKanban
}
Evento { id, tenantId, processoId, tipo, titulo, dataHora, local, responsavelId }
Comentario { id, tarefaId, usuarioId, texto, mencoes[], criadoEm }
```

---

### MÓDULO 5 — Gestão Financeira

**Descrição:** Controle financeiro completo do escritório, incluindo honorários, despesas, cobranças e relatórios. Pagamentos processados via **AbacatePay**.

**Funcionalidades:**

#### 5.1 Lançamentos Financeiros
- Registro de receitas (honorários iniciais, êxito, mensalidades, consultas)
- Registro de despesas (custas processuais, diárias, deslocamento, etc.)
- Vinculação de lançamentos a processos e clientes
- Controle financeiro por processo (visão de lucratividade por caso)
- Categorias customizáveis de receita e despesa

#### 5.2 Cobranças e Pagamentos via AbacatePay
A integração é feita via `HttpClient` do .NET consumindo a API REST da AbacatePay (`https://api.abacatepay.com/v2`). Autenticação via header `Authorization: Bearer <API_KEY>`.

**Fluxo de cobrança:**

1. **Criar cliente no AbacatePay** (se não existir):
   - `POST /customers/create` — campos: `name`, `email`, `taxId` (CPF/CNPJ), `cellphone`
   - Clientes são únicos por CPF/CNPJ; a API retorna o existente se já cadastrado
   - Armazenar o `customerId` retornado na entidade `Contato` do sistema

2. **Gerar cobrança PIX** (checkout transparente):
   - `POST /transparents/create`
   - Campos: `amount` (em centavos), `description`, `expiresIn` (segundos), `customer`, `metadata`
   - Retorna: `brCode` (copia-e-cola), `brCodeBase64` (QR Code em base64), `id` do PIX
   - Exibir QR Code para o cliente na tela via tag `<img src="data:image/png;base64,...">`

3. **Verificar status de pagamento:**
   - `GET /transparents/check?id={pixId}` — retorna status: `PENDING`, `PAID`, `EXPIRED`
   - Polling no frontend a cada 5 segundos enquanto o modal de pagamento estiver aberto
   - Ou aguardar o **Webhook** (preferencial para confirmação definitiva)

4. **Webhook de confirmação:**
   - AbacatePay envia `POST` para endpoint configurado (`/api/webhooks/abacatepay`)
   - Validar a autenticidade via `WEBHOOK_SECRET` no header
   - Ao receber evento de pagamento confirmado: atualizar status do lançamento para `Pago`
   - Exemplo de evento: `billing.paid`

5. **Assinaturas recorrentes** (para cobrança mensal de honorários):
   - Criar produto com `cycle: MONTHLY`: `POST /products/create`
   - Criar checkout vinculado ao produto: `POST /checkouts/create`
   - AbacatePay gerencia a recorrência automaticamente

**Funcionalidades da régua de cobrança:**
- Job Hangfire que roda diariamente verificando cobranças vencendo em X dias
- Envio automático de e-mail de lembrete ao cliente com link/QR Code
- Configuração de régua: lembrete N dias antes, no dia, N dias depois do vencimento

#### 5.3 Contratos e Honorários
- Cadastro de contrato de honorários vinculado ao cliente e ao processo
- Tipos de contrato: fixo, por hora, êxito, misto
- Parcelamento de honorários com controle de parcelas
- Cada parcela gera um `LancamentoFinanceiro` com vencimento

#### 5.4 Dashboard Financeiro
- Visão geral de receitas e despesas por período
- Fluxo de caixa
- Inadimplência (cobranças em atraso)
- Honorários a receber
- Relatórios exportáveis (PDF/CSV)

**Modelos de dados principais (EF Core):**
```csharp
public class LancamentoFinanceiro {
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid? ProcessoId { get; set; }
    public Guid? ClienteId { get; set; }
    public TipoLancamento Tipo { get; set; } // Receita | Despesa
    public string Categoria { get; set; }
    public decimal Valor { get; set; }
    public DateTime DataVencimento { get; set; }
    public DateTime? DataPagamento { get; set; }
    public StatusLancamento Status { get; set; } // Pendente | Pago | Vencido | Cancelado
    public string? Descricao { get; set; }
    public string? AbacatePayPixId { get; set; }    // ID retornado pela AbacatePay
    public string? AbacatePayCustomerId { get; set; }
}

public class ContratoHonorarios {
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid ClienteId { get; set; }
    public Guid? ProcessoId { get; set; }
    public TipoContrato Tipo { get; set; } // Fixo | PorHora | Exito | Misto
    public decimal Valor { get; set; }
    public List<LancamentoFinanceiro> Parcelas { get; set; }
}
```

**Serviço de integração AbacatePay (C#):**
```csharp
// Infrastructure/Payments/AbacatePayService.cs
public class AbacatePayService : IPaymentService {
    private readonly HttpClient _httpClient;

    public async Task<PixCobrancaResult> GerarPixAsync(decimal valor, string descricao,
        string? clienteAbacateId, CancellationToken ct) {
        var payload = new {
            amount = (int)(valor * 100), // centavos
            description = descricao,
            expiresIn = 3600, // 1 hora
            customer = clienteAbacateId != null ? new { id = clienteAbacateId } : null
        };
        var response = await _httpClient.PostAsJsonAsync("/transparents/create", payload, ct);
        // ... deserializar e retornar
    }
}
```

---

### MÓDULO 6 — Cronômetro e Timesheet

**Descrição:** Controle de horas trabalhadas por processo e por usuário, essencial para cobrança por hora.

**Funcionalidades:**
- Cronômetro integrado (iniciar/pausar/parar) vinculado a processo ou tarefa
- Registro manual de horas
- Visualização de timesheet por período (dia, semana, mês)
- Relatório de horas por processo, por usuário e por cliente
- Exportação de timesheet (PDF/CSV)
- Cálculo automático de valor baseado em taxa horária configurada por usuário

**Modelos de dados principais:**
```
RegistroTempo {
  id, tenantId, usuarioId, processoId, tarefaId,
  inicio, fim, duracao (min), descricao, manual (bool)
}
TaxaHoraria { id, tenantId, usuarioId, valorHora }
```

---

### MÓDULO 7 — Indicadores e Relatórios

**Descrição:** Painéis analíticos para suporte à decisão estratégica do escritório.

**Funcionalidades:**

#### 7.1 Indicadores Pessoais
- Total de processos ativos por advogado
- Tarefas abertas e concluídas por período
- Horas trabalhadas por período
- Prazos próximos do vencimento
- Taxa de cumprimento de tarefas

#### 7.2 Indicadores Gerais de Processo
- Total de processos por status (ativo, encerrado, suspenso)
- Distribuição por área do direito
- Distribuição por tribunal/comarca
- Novos processos por período
- Processos encerrados com resultado (ganhos/perdidos)

#### 7.3 Indicadores de Processo por Cliente
- Processos ativos por cliente
- Honorários recebidos por cliente
- Histórico de casos por cliente

#### 7.4 Indicadores Estratégicos
- Receita total por período
- Margem por processo
- Clientes mais rentáveis
- Desempenho da equipe (produtividade)
- Gráficos de atividades por equipe

**Modelos de dados principais:**
- Todos gerados via queries analíticas nos módulos existentes (não requerem tabelas adicionais além de views/materialized views)

---

### MÓDULO 8 — Portal do Cliente

**Descrição:** Espaço exclusivo de acesso para os clientes do escritório, sem precisar expor o sistema interno.

**Funcionalidades:**
- Acesso via link web (área separada no mesmo sistema, rota `/cliente/...`)
- Login do cliente com e-mail e senha (credenciais separadas do escritório)
- Visualização de processos vinculados ao cliente
- Visualização de andamentos processuais em linguagem simplificada (com IA, se habilitado)
- Upload seguro de documentos pelo cliente
- Recebimento de notificações de novos andamentos
- Histórico de comunicações com o escritório
- Visualização de boletos e status de pagamento

**Modelos de dados principais:**
```
AcessoCliente { id, tenantId, contatoId, email, senha (hash), ativo }
```

---

### MÓDULO 9 — Notificações e Alertas

**Descrição:** Sistema de notificações multicanal para manter usuários e clientes informados.

**Funcionalidades:**
- Alertas automáticos de novos andamentos processuais
- Alertas de prazos vencendo (configurável: 1, 3, 5 dias antes)
- Alertas de audiências e eventos próximos
- Alertas de publicações capturadas
- Notificações por e-mail (template customizável por escritório)
- Notificações para clientes via Portal do Cliente
- Central de notificações in-app (sino com histórico)
- Configuração de preferências por usuário (quais alertas receber e por qual canal)

---

### MÓDULO 10 — Integrações e Automações com IA

**Descrição:** Funcionalidades avançadas que utilizam IA para automatizar tarefas repetitivas.

**Funcionalidades:**

#### 10.1 Tradução de Andamentos com IA
- Integração com LLM (Claude/OpenAI) para traduzir juridiquês em linguagem acessível
- Envio automático da tradução ao cliente por e-mail ou Portal do Cliente
- Opção de revisão prévia pelo advogado antes do envio
- Sistema de créditos por tradução (5 créditos de cortesia no Plano Smart)
- Habilitação por cliente (advogado escolhe quais clientes recebem)
- Etiqueta visual na ficha do cliente indicando se IA está habilitada

**Prompt base para a IA:**
> "Você é um assistente jurídico. Traduza o seguinte andamento processual para uma linguagem clara, simples e não técnica, como se o advogado estivesse explicando diretamente ao cliente. Não use termos jurídicos sem explicação. Inclua o que aconteceu e qual é o próximo passo esperado, se houver. Andamento: [TEXTO_DO_ANDAMENTO]"

#### 10.2 Gerador de Peças Jurídicas com IA
- Interface para descrever o processo e o tipo de peça desejada
- IA gera rascunho completo da peça (petição inicial, contestação, recurso, etc.)
- Busca de jurisprudência relevante integrada
- Sugestão de teses jurídicas
- Plano Smart: 2 créditos de cortesia
- Sistema de créditos adicional via "Turbos" (pacotes extras)

#### 10.3 Classificação Automática de Publicações com IA
- IA classifica publicações capturadas automaticamente como: prazo, audiência, decisão, despacho, outros
- Priorização automática de publicações urgentes
- Sugestão automática de tarefas baseada na publicação (ex: publicação de prazo → cria tarefa de recurso)

#### 10.4 Integração com WhatsApp (opcional/turbo)
- Conexão do WhatsApp profissional do advogado ao sistema
- Envio de boletos, andamentos e mensagens diretamente pelo sistema
- Histórico de conversas centralizado no sistema
- Envio de mensagens de aniversário automáticas

---

### MÓDULO 11 — Responsividade e Experiência Mobile Web

**Descrição:** O sistema é 100% web (sem app nativo). Todas as telas devem funcionar perfeitamente em dispositivos móveis usando o navegador do celular.

**Diretrizes obrigatórias de responsividade:**

#### Layout e CSS
- Usar **CSS Grid** para estruturas de página e **Flexbox** para componentes internos
- Definir breakpoints padrão com media queries:
  ```css
  /* Mobile first — estilos base para < 768px */
  @media (min-width: 768px) { /* tablet */ }
  @media (min-width: 1024px) { /* desktop */ }
  ```
- Sidebar retrátil em mobile (hamburger menu)
- Tabelas responsivas: scroll horizontal em mobile ou transformação em cards
- Botões de ação com área de toque mínima de 44x44px
- Fontes mínimas de 16px para inputs (evita zoom automático no iOS)

#### Navegação mobile
- Menu hambúrguer que colapsa a sidebar de navegação
- Barra inferior de navegação rápida nas telas principais (mobile only)
- Gestos de swipe para fechar modais (JS nativo com Touch Events)

#### Componentes adaptados para mobile
- Modais: tela cheia em mobile (`position: fixed; inset: 0`)
- Datepickers: usar `<input type="date">` nativo (melhor UX mobile)
- Formulários: campos em coluna única em mobile
- Kanban: scroll horizontal com cards de largura fixa
- Gráficos (Chart.js): redimensionamento automático com `responsive: true`

#### Telas validadas em mobile (obrigatório)
- Login
- Dashboard (indicadores em cards empilhados)
- Lista de processos (tabela → cards em mobile)
- Detalhe do processo
- Lista e detalhe de tarefas
- Kanban
- Agenda/calendário
- Financeiro (lançamentos)
- QR Code PIX (centralizado, tamanho adequado para scan)

---

### MÓDULO 12 — Configurações do Escritório e Plano de Assinatura

**Descrição:** Painel administrativo para configuração do escritório e gestão do plano.

**Funcionalidades:**
- Perfil do escritório (nome, CNPJ, logo, endereço)
- Gestão de usuários (convidar, definir permissões, remover)
- Configuração de áreas de atuação do escritório
- Configuração de categorias financeiras customizadas
- Gestão do plano de assinatura (upgrade, cancelamento)
- Visualização do consumo de recursos (processos monitorados, usuários, armazenamento)
- Gestão de nomes para captura de publicações (Plano Smart: 3 nomes)
- Faturamento e histórico de pagamentos
- Integração com Google Drive (configuração de OAuth)

---

### MÓDULO 13 — Armazenamento de Arquivos (OCI Object Storage)

**Descrição:** Todo upload de documentos jurídicos é armazenado no **Oracle Cloud Infrastructure Object Storage**, acessado via S3 Compatibility API usando o **AWS SDK for .NET** com endpoint customizado.

**Como funciona o OCI S3 Compatibility API:**
- O OCI Object Storage expõe um endpoint S3-compatível no formato:
  `https://<namespace>.compat.objectstorage.<region>.oraclecloud.com`
- Autenticação via **Customer Secret Key** (Access Key + Secret Key) gerada no console OCI
- Usa **AWS Signature Version 4** — idêntico ao S3 da AWS
- O AWS SDK for .NET funciona nativamente, bastando configurar o `ServiceURL` customizado

**Configuração no .NET (`Program.cs`):**
```csharp
// appsettings.json
{
  "OciStorage": {
    "Namespace": "mynamespace",
    "Region": "sa-saopaulo-1",
    "BucketName": "legal-manager-docs",
    "AccessKey": "...",
    "SecretKey": "..."
  }
}

// Infrastructure/Storage/OciStorageService.cs
using Amazon.S3;
using Amazon.S3.Model;

public class OciStorageService : IStorageService
{
    private readonly IAmazonS3 _s3Client;
    private readonly string _bucketName;

    public OciStorageService(IConfiguration config)
    {
        var oci = config.GetSection("OciStorage");
        var s3Config = new AmazonS3Config
        {
            ServiceURL = $"https://{oci["Namespace"]}.compat.objectstorage.{oci["Region"]}.oraclecloud.com",
            ForcePathStyle = true, // obrigatório para OCI
            AuthenticationRegion = oci["Region"]
        };
        _s3Client = new AmazonS3Client(oci["AccessKey"], oci["SecretKey"], s3Config);
        _bucketName = oci["BucketName"]!;
    }

    public async Task<string> UploadAsync(Stream fileStream, string objectKey,
        string contentType, CancellationToken ct)
    {
        var request = new PutObjectRequest
        {
            BucketName = _bucketName,
            Key = objectKey,         // ex: "tenant-guid/processos/proc-guid/peticao.pdf"
            InputStream = fileStream,
            ContentType = contentType,
            ServerSideEncryptionMethod = ServerSideEncryptionMethod.AES256
        };
        await _s3Client.PutObjectAsync(request, ct);
        return objectKey;
    }

    public async Task<Stream> DownloadAsync(string objectKey, CancellationToken ct)
    {
        var response = await _s3Client.GetObjectAsync(_bucketName, objectKey, ct);
        return response.ResponseStream;
    }

    public async Task<string> GetPresignedUrlAsync(string objectKey, int expiresInMinutes = 30)
    {
        var request = new GetPreSignedUrlRequest
        {
            BucketName = _bucketName,
            Key = objectKey,
            Expires = DateTime.UtcNow.AddMinutes(expiresInMinutes)
        };
        return _s3Client.GetPreSignedURL(request); // URL temporária para download seguro
    }

    public async Task DeleteAsync(string objectKey, CancellationToken ct)
        => await _s3Client.DeleteObjectAsync(_bucketName, objectKey, ct);
}
```

**Pacote NuGet necessário:**
```
dotnet add package AWSSDK.S3
```

**Convenção de nomeação de objetos (object keys):**
```
{tenantId}/processos/{processoId}/{timestamp}_{nomeArquivo}
{tenantId}/contratos/{contratoId}/{timestamp}_{nomeArquivo}
{tenantId}/clientes/{clienteId}/{timestamp}_{nomeArquivo}
{tenantId}/modelos/{modeloId}/{timestamp}_{nomeArquivo}
```
Essa estrutura garante isolamento por tenant e organização por entidade.

**Configuração do bucket OCI (passos no console OCI):**
1. Criar bucket `legal-manager-docs` na região desejada (recomendado: `sa-saopaulo-1`)
2. Configurar **Lifecycle Policy** para expirar objetos temporários após 7 dias (se necessário)
3. Gerar **Customer Secret Key** (Access Key / Secret Key) em: `Identity → Users → <usuário> → Customer Secret Keys`
4. Copiar o **namespace** do tenancy em: `Object Storage → Namespace`
5. Configurar as policies de IAM OCI para o usuário de serviço ter acesso ao bucket

**Modelo de dados para rastreamento de arquivos:**
```csharp
public class Documento
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid? ProcessoId { get; set; }
    public Guid? ClienteId { get; set; }
    public string Nome { get; set; }          // nome original do arquivo
    public string ObjectKey { get; set; }     // chave no OCI (para download/delete)
    public string ContentType { get; set; }   // "application/pdf", "image/jpeg", etc.
    public long TamanhoBytes { get; set; }
    public TipoDocumento Tipo { get; set; }   // Peticao | Decisao | Contrato | Prova | Outro
    public DateTime CriadoEm { get; set; }
    public Guid UploadadoPorId { get; set; }
}
```

**Controle de cota de armazenamento por tenant:**
```csharp
// Antes de cada upload, verificar cota disponível
public async Task<bool> VerificarCotaAsync(Guid tenantId, long novoArquivoBytes)
{
    var usadoBytes = await _context.Documentos
        .Where(d => d.TenantId == tenantId)
        .SumAsync(d => d.TamanhoBytes);
    var cotaBytes = 20L * 1024 * 1024 * 1024; // 20 GB — Plano Smart
    return (usadoBytes + novoArquivoBytes) <= cotaBytes;
}
```

---

### MÓDULO 14 — Envio de E-mails (Resend)

**Descrição:** Todos os e-mails transacionais do sistema (notificações, convites, régua de cobrança, reset de senha, andamentos ao cliente) são enviados via **Resend**, usando o SDK oficial .NET.

**Pacote NuGet:**
```
dotnet add package Resend
```

**Configuração no `Program.cs`:**
```csharp
// appsettings.json
{
  "Resend": {
    "ApiToken": "re_xxxxxxxxxxxxxxxxxxxx",
    "FromEmail": "noreply@seudominio.com.br",
    "FromName": "LegalManager"
  }
}

// Program.cs
builder.Services.AddOptions();
builder.Services.AddHttpClient<ResendClient>();
builder.Services.Configure<ResendClientOptions>(o =>
{
    o.ApiToken = builder.Configuration["Resend:ApiToken"]!;
});
builder.Services.AddTransient<IResend, ResendClient>();
```

**Serviço de e-mail no sistema:**
```csharp
// Application/Services/EmailService.cs
public class EmailService : IEmailService
{
    private readonly IResend _resend;
    private readonly IConfiguration _config;

    public EmailService(IResend resend, IConfiguration config)
    {
        _resend = resend;
        _config = config;
    }

    private EmailMessage CriarMensagem(string para, string assunto, string htmlBody)
    {
        var msg = new EmailMessage();
        msg.From = $"{_config["Resend:FromName"]} <{_config["Resend:FromEmail"]}>";
        msg.To.Add(para);
        msg.Subject = assunto;
        msg.HtmlBody = htmlBody;
        return msg;
    }

    public async Task EnviarConviteUsuarioAsync(string email, string nomeEscritorio,
        string linkConvite, CancellationToken ct)
    {
        var html = EmailTemplates.Convite(nomeEscritorio, linkConvite);
        await _resend.EmailSendAsync(CriarMensagem(email, $"Convite para {nomeEscritorio}", html));
    }

    public async Task EnviarResetSenhaAsync(string email, string linkReset, CancellationToken ct)
    {
        var html = EmailTemplates.ResetSenha(linkReset);
        await _resend.EmailSendAsync(CriarMensagem(email, "Redefinição de senha", html));
    }

    public async Task EnviarAndamentoClienteAsync(string email, string nomeCliente,
        string numeroProcesso, string andamentoTraduzido, CancellationToken ct)
    {
        var html = EmailTemplates.AndamentoProcessual(nomeCliente, numeroProcesso, andamentoTraduzido);
        await _resend.EmailSendAsync(CriarMensagem(email,
            $"Atualização do processo {numeroProcesso}", html));
    }

    public async Task EnviarCobrancaAsync(string email, string nomeCliente,
        decimal valor, DateTime vencimento, string pixBrCode, CancellationToken ct)
    {
        var html = EmailTemplates.Cobranca(nomeCliente, valor, vencimento, pixBrCode);
        await _resend.EmailSendAsync(CriarMensagem(email,
            $"Cobrança de honorários — vencimento {vencimento:dd/MM/yyyy}", html));
    }
}
```

**Templates de e-mail (HTML inline):**
- Todos os templates ficam em `Application/Email/Templates/`
- Usar HTML com estilos inline (melhor compatibilidade entre clientes de e-mail)
- Template base responsivo com logo do escritório, paleta de cores e footer com endereço

**E-mails transacionais do sistema:**

| Evento | Assunto | Destinatário |
|--------|---------|-------------|
| Cadastro de novo escritório | Bem-vindo ao LegalManager | Admin do tenant |
| Convite de usuário | Convite para `{escritório}` | Novo usuário |
| Reset de senha | Redefinição de senha | Usuário |
| Novo andamento processual | Atualização do processo `{número}` | Cliente |
| Andamento traduzido por IA | Novidades sobre seu processo | Cliente |
| Lembrete de cobrança (antes) | Lembrete: cobrança vence em `{N}` dias | Cliente |
| Lembrete de cobrança (dia) | Cobrança vence hoje | Cliente |
| Lembrete de cobrança (depois) | Cobrança em atraso — `{N}` dias | Cliente |
| Prazo processual vencendo | Prazo em `{N}` dias: `{processo}` | Advogado responsável |
| Publicação capturada | Nova publicação: `{tipo}` | Advogado responsável |
| Trial expirando | Seu período de teste termina em `{N}` dias | Admin do tenant |

**Envio via Hangfire (assíncrono — obrigatório):**
```csharp
// Nunca enviar e-mail de forma síncrona em uma requisição HTTP
// Sempre enfileirar como job Hangfire
BackgroundJob.Enqueue<EmailService>(s =>
    s.EnviarAndamentoClienteAsync(email, nomeCliente, numero, texto, CancellationToken.None));
```

---

## Fases de Implementação

### FASE 1 — MVP Core (Sprints 1–6, ~3 meses)
**Objetivo:** Sistema funcional com as funcionalidades essenciais de gestão processual.

| Módulo | Prioridade |
|--------|-----------|
| Módulo 1 — Autenticação e Tenants | 🔴 Crítico |
| Módulo 2 — Contatos e Clientes | 🔴 Crítico |
| Módulo 3 — Gestão de Processos (cadastro manual + andamentos manuais) | 🔴 Crítico |
| Módulo 4 — Tarefas e Agenda (básico) | 🔴 Crítico |
| Módulo 9 — Notificações (e-mail) | 🟡 Importante |
| Módulo 12 — Configurações básicas do escritório | 🟡 Importante |

**Entregáveis da Fase 1:**
- CRUD completo de processos, clientes e tarefas
- Sistema de login multiusuário por escritório
- Notificações por e-mail básicas
- Interface web responsiva

---

### FASE 2 — Automação Processual (Sprints 7–10, ~2 meses)
**Objetivo:** Monitoramento automático de processos e captura de publicações.

| Módulo | Prioridade |
|--------|-----------|
| Módulo 3.2 — Monitoramento automático (robôs tribunais) | 🔴 Crítico |
| Módulo 3.4 — Captura de publicações DJE | 🔴 Crítico |
| Módulo 3.5 — Controle de prazos com calculadora | 🟡 Importante |

**Entregáveis da Fase 2:**
- Integração com ao menos 10 tribunais principais (TJSP, TJRJ, TRF1, TRF3, STJ, STF, etc.)
- Jobs assíncronos de scraping com Redis/BullMQ
- Alertas automáticos de novos andamentos

---

### FASE 3 — Financeiro e Produtividade (Sprints 11–14, ~2 meses)
**Objetivo:** Módulos financeiros e de produtividade completos.

| Módulo | Prioridade |
|--------|-----------|
| Módulo 5 — Gestão Financeira completa | 🔴 Crítico |
| Módulo 6 — Cronômetro e Timesheet | 🟡 Importante |
| Módulo 4.4 — Kanban | 🟡 Importante |
| Módulo 7 — Indicadores e Relatórios | 🟡 Importante |

**Entregáveis da Fase 3:**
- Emissão de boletos com PIX (integração AbacatePay)
- Régua de cobrança automática
- Dashboards financeiros
- Visualização kanban

---

### FASE 4 — Portal do Cliente e Experiência Mobile Web (Sprints 15–18, ~2 meses)
**Objetivo:** Experiência do cliente e refinamento da responsividade mobile.

| Módulo | Prioridade |
|--------|-----------|
| Módulo 8 — Portal do Cliente | 🔴 Crítico |
| Módulo 11 — Refinamento mobile-responsive (HTML/CSS) | 🟡 Importante |

**Entregáveis da Fase 4:**
- Portal do Cliente como área separada no mesmo sistema web (rota `/cliente/...`)
- Todos os módulos validados em viewport mobile (375px, 390px, 414px)

---

### FASE 5 — IA e Automações Avançadas (Sprints 19–22, ~2 meses)
**Objetivo:** Diferenciais competitivos com IA.

| Módulo | Prioridade |
|--------|-----------|
| Módulo 10.1 — Tradução de andamentos com IA | 🔴 Crítico |
| Módulo 10.3 — Classificação de publicações com IA | 🟡 Importante |
| Módulo 10.2 — Gerador de peças jurídicas com IA | 🟢 Desejável |
| Módulo 10.4 — Integração WhatsApp | 🟢 Desejável |

**Entregáveis da Fase 5:**
- Pipeline de tradução de andamentos com LLM
- Gerador de peças jurídicas
- Classificação automática de publicações

---

## Requisitos Não Funcionais

### Segurança
- Autenticação JWT com refresh tokens
- Dados em trânsito: HTTPS/TLS 1.3
- Dados em repouso: criptografia AES-256
- Isolamento total entre tenants (row-level security no PostgreSQL)
- LGPD: consentimento, direito ao esquecimento, logs de auditoria
- Backup automático diário com retenção de 30 dias
- 2FA opcional para usuários admin

### Performance
- Tempo de resposta da API < 500ms para 95% das requisições
- Suporte a até 500 usuários simultâneos (escala horizontal)
- Jobs de scraping assíncronos, sem impacto na experiência do usuário

### Disponibilidade
- SLA mínimo de 99.5% de uptime
- Deploy em múltiplas zonas de disponibilidade (AWS ou GCP)
- Health checks e auto-restart de serviços críticos

### Observabilidade
- Logs estruturados (JSON) com correlação por request ID
- Métricas de aplicação (Prometheus/Grafana)
- Rastreamento de erros (Sentry)
- Auditoria de ações dos usuários (quem fez o quê e quando)

---

## Integrações Externas Necessárias

| Integração | Finalidade | Complexidade |
|------------|-----------|-------------|
| APIs dos Tribunais Brasileiros (DataJud/CNJ, TJs, TRFs) | Monitoramento de processos | 🔴 Alta |
| Diários Oficiais (DJE estaduais, DOU) | Captura de publicações | 🔴 Alta |
| **AbacatePay API** (`api.abacatepay.com/v2`) | PIX QR Code, Checkout transparente, Assinaturas, Webhooks | 🟢 Baixa |
| **OCI Object Storage** (S3 Compatibility API) | Armazenamento de documentos jurídicos — via `AWSSDK.S3` com endpoint OCI | 🟢 Baixa |
| **Resend** (`dotnet add package Resend`) | Todos os e-mails transacionais do sistema | 🟢 Baixa |
| Google Drive API | Integração opcional de documentos | 🟡 Média |
| Anthropic API (Claude) | IA para tradução de andamentos e geração de peças | 🟡 Média |
| WhatsApp Business API | Envio de mensagens ao cliente (fase futura) | 🔴 Alta |

### Detalhamento da integração OCI Object Storage

**Endpoint S3-compatível:** `https://<namespace>.compat.objectstorage.<region>.oraclecloud.com`
**SDK .NET:** `AWSSDK.S3` com `ForcePathStyle = true` e `ServiceURL` apontando para OCI
**Autenticação:** Customer Secret Key (Access Key + Secret Key) gerada no console OCI
**Região recomendada para BR:** `sa-saopaulo-1`
**Recursos suportados:** PutObject, GetObject, DeleteObject, GetPreSignedURL, ListObjects

### Detalhamento da integração AbacatePay

**Base URL:** `https://api.abacatepay.com/v2`
**Autenticação:** `Authorization: Bearer <API_KEY>` em todas as requisições
**Valores monetários:** sempre em centavos (ex: `R$ 100,00` → `10000`)
**Envelope de resposta:** `{ "data": {...}, "success": true, "error": null }`
**Dev Mode:** ambiente sandbox disponível para testes sem movimentação real

| Endpoint | Método | Uso no sistema |
|----------|--------|---------------|
| `/customers/create` | POST | Cadastrar cliente do escritório no AbacatePay |
| `/customers/list` | GET | Listar clientes cadastrados |
| `/transparents/create` | POST | Gerar PIX QR Code para cobrança |
| `/transparents/check` | GET | Verificar status do PIX (`PENDING`/`PAID`/`EXPIRED`) |
| `/transparents/simulate-payment` | POST | Simular pagamento em dev mode |
| `/products/create` | POST | Criar produto para assinatura recorrente |
| `/checkouts/create` | POST | Criar checkout para assinatura mensal |
| `/webhooks` (receber) | POST | Receber confirmações de pagamento (`billing.paid`) |

---

## Modelo de Dados — Relacionamento Geral

```
Tenant
  ├── Usuarios (1:N)
  ├── Contatos (1:N)
  │     └── Atendimentos (1:N)
  ├── Processos (1:N)
  │     ├── Andamentos (1:N)
  │     ├── Publicacoes (1:N)
  │     ├── Documentos (1:N)
  │     ├── Tarefas (1:N)
  │     ├── Eventos (1:N)
  │     ├── LancamentosFinanceiros (1:N)
  │     └── RegistrosTempo (1:N)
  ├── ModelosDocumento (1:N)
  ├── CategoriaFinanceira (1:N)
  └── ConfiguracoesTenant (1:1)
```

---

## Critérios de Aceitação por Módulo

Cada módulo deve ser considerado concluído somente quando:

1. **Funcionalidade implementada** conforme especificação deste documento
2. **Testes unitários** cobrindo pelo menos 80% do código de negócio
3. **Testes de integração** para todos os endpoints de API
4. **Documentação de API** atualizada (OpenAPI/Swagger)
5. **Revisão de segurança** — sem SQL injection, sem exposição de dados de outros tenants
6. **Validação de LGPD** — dados sensíveis tratados corretamente

---

## Glossário de Termos Jurídicos Relevantes

| Termo | Definição no contexto do sistema |
|-------|----------------------------------|
| Andamento processual | Movimentação registrada no processo judicial em um tribunal |
| Publicação | Comunicação oficial nos Diários Oficiais (intimações, decisões) |
| Intimação | Comunicação formal para que uma das partes tome ciência ou pratique um ato |
| Prazo processual | Período legal para praticar um ato processual, contado em dias úteis |
| OAB | Registro do advogado na Ordem dos Advogados do Brasil |
| CNJ | Número único do processo judicial no formato do Conselho Nacional de Justiça |
| Vara | Unidade judiciária responsável por processar determinados tipos de causas |
| Comarca | Circunscrição judiciária de primeiro grau |
| Honorários | Remuneração do advogado pelos serviços jurídicos prestados |
| Timesheet | Registro detalhado de horas trabalhadas por atividade |
| Peça jurídica | Documento processual como petição, recurso, contestação, etc. |
| Plano Smart | Plano de referência do Astrea com até 500 processos, 5 usuários, 20 GB |

---

## Notas para a IA Implementadora

> Este documento foi criado para ser lido por uma IA de desenvolvimento. Ao implementar cada módulo, observe:

1. **Stack é fixa:** C# com .NET 10 no backend, HTML/CSS/JS Vanilla no frontend. Não sugerir nem usar frameworks de frontend (React, Vue, Angular, etc.)
2. **Autenticação:** usar exclusivamente ASP.NET Core Identity + JWT Bearer. O token JWT deve conter `tenantId` como claim para que todos os serviços saibam de qual escritório é a requisição
3. **Multitenancy é crítico** — nunca um tenant deve ver dados de outro. Usar `TenantId` como filtro obrigatório em todas as queries EF Core. Considerar um `ITenantContext` injetável que lê o `tenantId` do JWT
4. **Frontend:** todo código JS deve ser modular (ES6 modules), usar `fetch()` nativo com um wrapper que injeta o header `Authorization: Bearer <token>` automaticamente. Token armazenado em `sessionStorage`
5. **Responsividade:** toda tela deve ser desenvolvida e validada em viewport mobile (390px) antes de considerar concluída. Usar CSS mobile-first
6. **AbacatePay:** valores sempre em centavos. Implementar o webhook endpoint e validar a assinatura antes de processar. Armazenar o `pixId` retornado para polling de status
7. **Os robôs de scraping de tribunais** são a parte mais frágil do sistema — sites de tribunais mudam frequentemente. Usar uma arquitetura de adaptadores (`ITribunalAdapter`) para facilitar manutenção de cada tribunal individualmente
8. **Jobs assíncronos com Hangfire:** scraping de processos, envio de e-mails e régua de cobrança devem ser sempre jobs Hangfire, nunca chamadas síncronas no ciclo da requisição HTTP
9. **LGPD:** dados de clientes e processos são dados sensíveis. Implementar logs de auditoria desde o Módulo 1 (quem acessou/alterou o quê e quando)
10. **EF Core:** usar repositórios com `IRepository<T>` genérico. Nunca expor `DbContext` diretamente nos controllers. Aplicar global query filter por `TenantId` na configuração do contexto
11. **OCI Object Storage:** usar `AWSSDK.S3` com `ForcePathStyle = true` e `ServiceURL` apontando para o endpoint OCI. O bucket deve ter uma estrutura de pastas por `tenantId` para garantir isolamento lógico. Sempre usar URLs pré-assinadas (`GetPreSignedURL`) para servir arquivos ao frontend — nunca expor o bucket diretamente
12. **Resend:** registrar `IResend` no DI via `AddTransient`. Nunca chamar `EmailSendAsync` de forma síncrona em uma requisição HTTP — sempre enfileirar como job Hangfire. Templates de e-mail em HTML com estilos inline, armazenados em `Application/Email/Templates/`

---

*Fim do Plano de Implementação v1.0*
