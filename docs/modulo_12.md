# Módulo 12 — Configurações do Escritório e Plano de Assinatura

**Implementado em:** 2026-04-22
**Status:** ✅ Implementado

---

## Visão Geral

O Módulo 12 implementa o painel administrativo para configuração do escritório e gestão do plano de assinatura, conforme especificado no [implementation_plan.md](./implementation_plan.md).

## Funcionalidades Implementadas

### 1. Perfil do Escritório
- [x] Edição de nome, CNPJ e endereço do escritório (Admin only)
- [x] Persistência na entidade `Tenant`

### 2. Gestão de Usuários
- [x] Listagem de usuários com perfil e status (Admin only)
- [x] Convite de novos usuários por e-mail
- [x] Alteração de perfil (Admin/Advogado/Colaborador)
- [x] Desativação de usuários
- [x] Proteção contra auto-desativação

### 3. Áreas de Atuação
- [x] CRUD completo de áreas de atuação (`AreaAtuacao`)
- [x] Campos: nome (obrigatório, único por tenant) e descrição (opcional)
- [x] Endpoint: `GET/POST/PUT/DELETE /api/configuracoes/areas-atuacao`
- [x] Bloqueio para non-Admins

### 4. Categorias Financeiras
- [x] CRUD completo de categorias (`CategoriaFinanceira`)
- [x] Tipos: `Receita` e `Despesa`
- [x] Exibição em duas colunas no frontend
- [x] Endpoint: `GET/POST/PUT/DELETE /api/configuracoes/categorias-financeiras`
- [x] Bloqueio para non-Admins

### 5. Gestão do Plano de Assinatura
- [x] Página `/pages/assinatura.html` com cards Free/Pro
- [x] Toggle Mensal/Anual com 20% de desconto
- [x] Integração com AbacatePay para checkout
- [x] Cancelamento de assinatura (mantém acesso até fim do período)
- [x] Status de trial, cancelado e ativo exibidos corretamente

### 6. Visualização de Consumo de Recursos
- [x] Grid de uso na página de configurações
- [x] Indicadores: Processos, Usuários, Tarefas abertas, Contatos, Armazenamento
- [x] Barras de progresso com percentual de uso
- [x] Limites por plano (Free: 40 processos, 1 usuário, 1GB; Pro: 500 processos, 5 usuários, 20GB)

### 7. Nomes para Captura de Publicações (Free vs Pro)
- [x] Cadastro de até 3 nomes para monitoramento
- [x] Overlay Pro para usuários Free (plano limitante)
- [x] Toggle ativo/pausado por nome

### 8. Preferências de Notificações
- [x] Tabela com checkboxes para alertas in-app e por e-mail
- [x] Tipos: Tarefas, Eventos, Prazos processuais, Publicações, Trial, Gerais
- [x] Persistência em `PreferenciasNotificacao`

### 9. Histórico de Faturamento
- [x] Entidade `Faturamento` para rastreamento de pagamentos
- [x] Gravação automática ao confirmar pagamento via webhook
- [x] Tabela de histórico na página de assinatura

---

## Modelo de Dados

### Entidade: `AreaAtuacao`
```csharp
public class AreaAtuacao
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string Nome { get; set; }        // max 100, único por tenant
    public string? Descricao { get; set; } // max 500
    public bool Ativo { get; set; } = true
    public DateTime CriadoEm { get; set; }
    public Tenant Tenant { get; set; }    // FK → Tenant
}
```

### Entidade: `CategoriaFinanceira`
```csharp
public class CategoriaFinanceira
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string Nome { get; set; }                  // max 100
    public TipoCategoriaFinanceira Tipo { get; set; }  // Receita | Despesa
    public bool Ativo { get; set; } = true
    public DateTime CriadoEm { get; set; }
    public Tenant Tenant { get; set; }                // FK → Tenant
}

public enum TipoCategoriaFinanceira { Receita, Despesa }
```

### Entidade: `Faturamento`
```csharp
public class Faturamento
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string BillingId { get; set; }           // ID do AbacatePay
    public string Periodo { get; set; }             // "Mensal" | "Anual"
    public decimal Valor { get; set; }              // em reais
    public string Moeda { get; set; } = "BRL"
    public StatusFaturamento Status { get; set; }  // Pendente | Pago | Cancelado | Expirado
    public DateTime? DataPagamento { get; set; }
    public DateTime DataCriacao { get; set; }
    public string? Descricao { get; set; }
    public Tenant Tenant { get; set; }              // FK → Tenant
}

public enum StatusFaturamento { Pendente, Pago, Cancelado, Expirado }
```

---

## Endpoints da API

### Configurações Extras (ConfiguracoesExtrasController)

| Método | Endpoint | Descrição | Auth |
|--------|----------|-----------|------|
| GET | `/api/configuracoes/areas-atuacao` | Lista áreas do tenant | Auth |
| POST | `/api/configuracoes/areas-atuacao` | Cria área | Admin |
| PUT | `/api/configuracoes/areas-atuacao/{id}` | Atualiza área | Admin |
| DELETE | `/api/configuracoes/areas-atuacao/{id}` | Remove área | Admin |
| GET | `/api/configuracoes/categorias-financeiras` | Lista categorias | Auth |
| POST | `/api/configuracoes/categorias-financeiras` | Cria categoria | Admin |
| PUT | `/api/configuracoes/categorias-financeiras/{id}` | Atualiza categoria | Admin |
| DELETE | `/api/configuracoes/categorias-financeiras/{id}` | Remove categoria | Admin |

### Assinatura (AssinaturaController)

| Método | Endpoint | Descrição | Auth |
|--------|----------|-----------|------|
| GET | `/api/assinatura` | Status da assinatura | Auth |
| GET | `/api/assinatura/historico` | Lista de faturamentos | Auth |
| POST | `/api/assinatura/iniciar` | Inicia checkout AbacatePay | Admin |
| POST | `/api/assinatura/cancelar` | Cancela assinatura | Admin |

### Webhook

| Método | Endpoint | Descrição | Auth |
|--------|----------|-----------|------|
| POST | `/api/webhooks/abacatepay` | Recebe eventos de pagamento | None (validação por secret) |

---

## Arquivos Criados/Modificados

### Novos Arquivos

| Arquivo | Descrição |
|---------|-----------|
| `src/LegalManager.Domain/Entities/AreaAtuacao.cs` | Entidade de área de atuação |
| `src/LegalManager.Domain/Entities/CategoriaFinanceira.cs` | Entidade de categoria financeira |
| `src/LegalManager.Domain/Entities/Faturamento.cs` | Entidade de registro de faturamento |
| `src/LegalManager.Infrastructure/Persistence/Configurations/AreaAtuacaoConfiguration.cs` | Configuração EF para AreaAtuacao |
| `src/LegalManager.Infrastructure/Persistence/Configurations/CategoriaFinanceiraConfiguration.cs` | Configuração EF para CategoriaFinanceira |
| `src/LegalManager.Infrastructure/Persistence/Configurations/FaturamentoConfiguration.cs` | Configuração EF para Faturamento |
| `src/LegalManager.Infrastructure/Persistence/DesignTimeDbContextFactory.cs` | Factory para migrations em design-time |
| `src/LegalManager.API/Controllers/ConfiguracoesExtrasController.cs` | Endpoints CRUD para áreas e categorias |

### Arquivos Modificados

| Arquivo | Alteração |
|---------|-----------|
| `src/LegalManager.Infrastructure/Persistence/AppDbContext.cs` | Adicionado DbSet para AreasAtuacao, CategoriasFinanceiras e Faturamentos |
| `src/LegalManager.API/Controllers/AssinaturaController.cs` | Adicionado endpoint GET /historico e gravação de Faturamento no webhook |
| `src/LegalManager.API/wwwroot/pages/configuracoes.html` | Adicionadas seções de Áreas de Atuação e Categorias Financeiras |
| `src/LegalManager.API/wwwroot/pages/assinatura.html` | Adicionado histórico de faturamento |

### Migration

| Arquivo | Descrição |
|---------|-----------|
| `src/LegalManager.Infrastructure/Migrations/20260422190945_Modulo12_ConfiguracoesExtras.cs` | Migration para criar tabelas AreasAtuacao, CategoriasFinanceiras e Faturamentos |

---

## Limites por Plano

| Recurso | Free | Pro |
|---------|------|-----|
| Processos monitorados | 40 | 500 |
| Usuários | 1 | 5 |
| Armazenamento | 1 GB | 20 GB |
| Nomes para captura | 0 (bloqueado) | 3 |
| Portal do Cliente | ❌ | ✅ |
| Controle financeiro | ❌ | ✅ |
| Indicadores avançados | ❌ | ✅ |

---

## Notas de Implementação

1. **Multitenancy**: Todas as entidades nuevas incluyen `TenantId` como FK obligatorio, garantindo isolamento total entre escritórios.

2. **Validação Pro/Free**: A seção de nomes de captura exibe overlay Pro para usuários Free, impedindo cadastro acima do limite.

3. **Webhook AbacatePay**: Ao confirmar pagamento, o sistema cria um registro em `Faturamento` com status `Pago` para rastreamento histórico.

4. **Categories Unique Constraint**: A combinação `(TenantId, Nome, Tipo)` é única, impedindo duplicatas de categorias com mesmo nome no mesmo tipo.

5. **Design-Time Factory**: A classe `DesignTimeDbContextFactory` permite que o `dotnet ef migrations` funcione sem configurar injeção de dependência completa.