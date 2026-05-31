# Módulo 08 — Portal do Cliente

## Status: ✅ Completo

---

## Visão Geral

Área exclusiva para clientes acessarem o andamento de seus processos via navegador, sem expor o sistema interno do escritório. Autenticação separada, token JWT próprio, frontend isolado em `/cliente/`.

---

## Funcionalidades Implementadas

### Acesso do Escritório (Office-side)
- Criação de acesso ao portal diretamente da ficha do contato (`POST /api/contatos/{id}/portal-acesso`)
- Ao criar o acesso, e-mail automático é enviado ao cliente com as credenciais e link do portal
- Redefinição de senha: re-criar acesso sobrescreve credenciais e reenvia e-mail
- Consulta de status do acesso (ativo, último login, e-mail)
- Revogação de acesso (`DELETE /api/contatos/{id}/portal-acesso`)
- Um contato pode ter no máximo um acesso por tenant
- E-mail único por tenant (não pode repetir entre contatos diferentes)

### Autenticação do Cliente
- Login via `POST /api/portal/login` com e-mail e senha
- Retorna JWT com claims: `sub` (acessoId), `contatoId`, `tenantId`, `role=Cliente`, `tipo=portal`, `nome`, `email`
- Token válido por 24 horas (sem refresh token — sessão de curta duração)
- Senha armazenada com hash via `IPasswordHasher<AcessoCliente>` (PBKDF2, igual ao Identity)
- Credenciais isoladas das credenciais do escritório (tabela `AcessosCliente` separada)
- Token armazenado em `sessionStorage` com chave `cliente_token` (separado do token do escritório)
- Último acesso registrado a cada login (`UltimoAcessoEm`)

### Dados Acessíveis pelo Cliente
- **Perfil:** nome, e-mail, telefone (`GET /api/portal/me`)
- **Meus Processos:** lista de processos onde o contato é parte, excluindo arquivados (`GET /api/portal/meus-processos`)
- **Detalhe do Processo:** número CNJ, tribunal, comarca, área, fase, status, tipo de parte (`GET /api/portal/meus-processos/{id}`)
- **Andamentos:** histórico completo de andamentos do processo com resumo de IA quando disponível (`GET /api/portal/meus-processos/{id}/andamentos`)

### Segurança
- Todos os endpoints protegidos com `[Authorize(Roles = "Cliente")]`
- Todas as queries filtradas por `contatoId + tenantId` extraídos do JWT — cliente não acessa processos de outros contatos ou tenants
- Erros de credencial retornam `401` com mensagem genérica (não revela se o e-mail existe)

---

## Arquitetura

```
/api/portal/*
  └── PortalClienteController
        └── PortalClienteService (IPortalClienteService)
              ├── AppDbContext (AcessosCliente, Processos, ProcessoPartes, Andamentos)
              ├── IPasswordHasher<AcessoCliente>
              ├── IConfiguration (JWT settings)
              └── IEmailService (boas-vindas com credenciais)

/api/contatos/{id}/portal-acesso
  └── ContatosController → IPortalClienteService
```

---

## Endpoints

### Portal (público + autenticado com role=Cliente)

| Método | Rota | Auth | Descrição |
|--------|------|------|-----------|
| POST | `/api/portal/login` | — | Login do cliente — retorna JWT |
| GET | `/api/portal/me` | Cliente | Perfil do cliente |
| GET | `/api/portal/meus-processos` | Cliente | Lista de processos do cliente |
| GET | `/api/portal/meus-processos/{id}` | Cliente | Detalhe do processo |
| GET | `/api/portal/meus-processos/{id}/andamentos` | Cliente | Andamentos do processo |

### Office-side (requer autenticação de escritório)

| Método | Rota | Descrição |
|--------|------|-----------|
| POST | `/api/contatos/{id}/portal-acesso` | Criar/redefinir acesso ao portal |
| GET | `/api/contatos/{id}/portal-acesso` | Consultar acesso (retorna 404 se não existe) |
| DELETE | `/api/contatos/{id}/portal-acesso` | Revogar acesso |

---

## Modelos de Dados (EF Core)

```csharp
AcessoCliente {
  Id, TenantId, ContatoId, Email, SenhaHash,
  Ativo, CriadoEm, UltimoAcessoEm
}
```

**Configurações EF (`AcessoClienteConfiguration`):**
- Índice único em `(TenantId, ContatoId)` — um acesso por contato por tenant
- Índice único em `(TenantId, Email)` — e-mail único por tenant
- Cascade delete de Tenant e Contato

---

## E-mail de Boas-Vindas

Disparado automaticamente ao criar ou redefinir o acesso:
- Destinatário: e-mail do acesso
- Conteúdo: nome do cliente, nome do escritório, e-mail e senha inicial, botão "Acessar Portal"
- Enviado como fire-and-forget (não bloqueia o retorno da API)
- Novo e-mail enviado sempre que as credenciais são redefinidas

---

## Migration

`AddAcessoCliente` — cria tabela `AcessosCliente` com índices únicos

---

## Testes

`PortalClienteServiceTests.cs` — 14 casos cobrindo:

| Grupo | Casos |
|-------|-------|
| `LoginAsync` | Credenciais corretas, senha errada, e-mail inexistente, conta inativa, atualização de `UltimoAcessoEm` |
| `CriarAcessoAsync` | Novo acesso, atualização de existente, e-mail em uso, contato não encontrado, disparo de e-mail |
| `RevogarAcessoAsync` | Remove acesso, lança `KeyNotFoundException` se não existe |
| `GetAcessoAsync` | Retorna info, retorna null quando inexistente |
| `GetMeusProcessosAsync` | Isolamento por contato/tenant, exclusão de processos arquivados |

---

## Frontend — `/cliente/`

Layout distinto do escritório: top-nav horizontal (sem sidebar), mobile-first, CSS próprio em `/cliente/css/portal.css`.

| Arquivo | Descrição |
|---------|-----------|
| `/cliente/index.html` | Login do cliente |
| `/cliente/dashboard.html` | Resumo: total de processos, total de andamentos, últimos processos |
| `/cliente/processos.html` | Lista completa de processos do cliente |
| `/cliente/processo-detalhe.html` | Detalhe do processo + lista de andamentos com resumo de IA |
| `/cliente/js/clienteApi.js` | Camada de API: `login`, `logout`, `clienteApiFetch`, `portalApi` — usa `cliente_token` em sessionStorage |
| `/cliente/js/clienteLayout.js` | Nav responsiva, `requireAuth`, helpers de formatação e badges |
| `/cliente/css/portal.css` | Estilos do portal: auth-card, top-nav, processo-cards, andamentos, mobile nav |

### Roteamento
- `/cliente/` e `/cliente` redirecionam para `/cliente/index.html` via middleware em `Program.cs`
- Arquivos estáticos servidos pelo `UseStaticFiles` padrão — sem SPA fallback
- Token 401 redireciona para `/cliente/index.html`

### Separação de sessão
- Token do portal armazenado em `sessionStorage` com chave `cliente_token`
- Token do escritório usa chave `access_token`
- As duas sessões podem coexistir no mesmo navegador sem conflito
