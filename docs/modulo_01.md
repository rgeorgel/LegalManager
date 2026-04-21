# Módulo 01 — Autenticação e Gestão de Tenants

## Status: ✅ Completo

---

## Visão Geral

Controle de acesso multiusuário por escritório (tenant). Cada escritório é um tenant isolado. Autenticação gerenciada pelo ASP.NET Core Identity com tokens JWT.

---

## Funcionalidades Implementadas

### Tenants
- Cadastro de novo escritório com nome, CNPJ e plano de assinatura
- Período de trial gratuito de 10 dias com acesso completo
- Status do tenant: `Trial`, `Ativo`, `Inadimplente`, `Cancelado`
- Planos: `Smart`

### Autenticação
- Login por e-mail/senha — retorna `accessToken` (JWT, 1h) + `refreshToken` (7 dias)
- Renovação de token via `POST /api/auth/refresh`
- Logout com revogação do refresh token no banco
- Recuperação de senha via e-mail com token de reset (validade: 1h)
- Aceite de convite via link com token temporário

### Gestão de Usuários
- Convite de usuário por e-mail: gera token, envia link de ativação
- Listagem de usuários do escritório
- Ativação/desativação de usuários
- Perfis de permissão: `Admin`, `Advogado`, `Colaborador`
- Limite de 5 usuários por plano Smart (validado na criação)

### Segurança
- Senha com hash via ASP.NET Core Identity (PBKDF2)
- Lockout após 5 tentativas falhas (15 min)
- JWT com claims: `sub`, `tenantId`, `role`, `email`, `nome`
- Refresh token rotacionado a cada renovação
- Middleware `TenantContext` — extrai `tenantId` do JWT e injeta no escopo da requisição

---

## Arquitetura

```
AuthController
  └── AuthService
        ├── UserManager<Usuario> (ASP.NET Identity)
        ├── IEmailService (convite, reset senha, boas-vindas)
        └── AppDbContext (RefreshTokens, Tenants)

TenantContext (ITenantContext)
  └── Lê TenantId e UserId do ClaimsPrincipal via IHttpContextAccessor
```

---

## Endpoints

| Método | Rota | Descrição |
|--------|------|-----------|
| POST | `/api/auth/register` | Cadastrar novo escritório + admin |
| POST | `/api/auth/login` | Login — retorna JWT + refresh token |
| POST | `/api/auth/refresh` | Renovar access token |
| POST | `/api/auth/logout` | Revogar refresh token |
| POST | `/api/auth/forgot-password` | Solicitar reset de senha |
| POST | `/api/auth/reset-password` | Confirmar nova senha |
| POST | `/api/auth/aceitar-convite` | Ativar conta via convite |
| GET | `/api/usuarios` | Listar usuários do tenant |
| POST | `/api/usuarios/convidar` | Convidar novo usuário |
| PATCH | `/api/usuarios/{id}/toggle` | Ativar/desativar usuário |

---

## Modelos de Dados (EF Core)

```csharp
Tenant { Id, Nome, Cnpj, Plano, Status, CriadoEm, TrialExpiraEm }
Usuario : IdentityUser<Guid> { TenantId, Nome, Perfil, Ativo, CriadoEm }
RefreshToken { Id, UsuarioId, Token, ExpiresAt, Revogado, CriadoEm }
ConviteUsuario { Id, TenantId, Email, Perfil, Token, ExpiresAt, Aceito }
```

---

## Migration

`InitialCreate` — cria tabelas de Identity + Tenant + RefreshToken + ConviteUsuario

---

## Testes

- `ContatoServiceTests` cobre isolamento de tenant (base de testes unitários do projeto)
- Validação de token JWT configurada em `TokenValidationParameters`

---

## Frontend

| Arquivo | Descrição |
|---------|-----------|
| `/index.html` | Tela de login |
| `/register.html` | Cadastro de escritório |
| `/forgot-password.html` | Recuperação de senha |
| `/reset-password.html` | Nova senha via token |
| `/aceitar-convite.html` | Aceite de convite |
| `/pages/usuarios.html` | Gestão de usuários |
| `/js/auth.js` | Login, logout, refresh, convite |
| `/js/api.js` | `apiFetch` com injeção de JWT e refresh automático |
