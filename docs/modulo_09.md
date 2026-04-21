# Módulo 09 — Notificações e Alertas

## Status: ✅ Completo

---

## Visão Geral

Sistema de notificações in-app com alertas automatizados por job diário e preferências individuais por usuário. O sino de notificações no cabeçalho exibe contagem em tempo real com polling a cada 60 segundos. Usuários controlam quais alertas recebem (sistema e e-mail) por tipo na tela de Configurações.

---

## Funcionalidades Implementadas

### 9.1 Notificações In-App
- Sino no header com badge de contagem (polling 60s)
- Dropdown com as últimas 20 notificações não lidas
- Marcar notificação individual como lida
- Marcar todas como lidas
- Clique na notificação navega para URL relacionada

### 9.2 Alertas Automatizados (Hangfire — 08:00 UTC diário)
- **Prazos de tarefas**: 5, 3 e 1 dia(s) antes + no dia do vencimento
- **Eventos da agenda**: 1 dia antes
- **Trial expirando**: 7, 3 e 1 dia(s) antes (apenas admins)
- **Prazos processuais**: 5, 3 e 1 dia(s) antes

### 9.3 Preferências de Notificações
- Configurações individuais por usuário (não por tenant)
- Toggles separados para "no sistema" e "por e-mail" por categoria:
  - Tarefas (prazos)
  - Eventos (agenda)
  - Prazos processuais
  - Publicações / andamentos
  - Aviso de trial (apenas in-app; e-mail trial sempre enviado)
  - Notificações gerais
- Padrão: tudo habilitado (GetOrCreate com defaults `true`)
- Persistido na tabela `PreferenciasNotificacoes` (único por usuário/tenant)

### 9.4 Histórico de Notificações
- `GET /api/notificacoes/historico?page=1&pageSize=20` — todas (lidas e não lidas)

---

## Arquitetura

```
AlertasJob (Hangfire a cada 3 horas: "0 */3 * * *")
  └── IPreferenciasNotificacaoService.PermiteInAppAsync / PermiteEmailAsync
        └── AppDbContext.PreferenciasNotificacoes (GetOrCreate)
  └── CriarNotificacaoAsync(chaveDedup)
        └── AnyAsync(ChaveDedup) → ignora se já existe
        └── AppDbContext.Notificacoes

GET /api/notificacoes/preferencias → IPreferenciasNotificacaoService.GetAsync
PUT /api/notificacoes/preferencias → IPreferenciasNotificacaoService.AtualizarAsync
```

---

## Endpoints

| Método | Rota | Descrição |
|--------|------|-----------|
| GET | `/api/notificacoes` | Notificações não lidas (últimas 20) |
| GET | `/api/notificacoes/count` | Contagem de não lidas |
| GET | `/api/notificacoes/historico` | Histórico paginado (todas) |
| POST | `/api/notificacoes/{id}/lida` | Marcar uma como lida |
| POST | `/api/notificacoes/marcar-todas-lidas` | Marcar todas como lidas |
| GET | `/api/notificacoes/preferencias` | Obter preferências do usuário logado |
| PUT | `/api/notificacoes/preferencias` | Atualizar preferências |

---

## Entidades

### `Notificacao` (atualizada)
| Campo | Tipo | Descrição |
|-------|------|-----------|
| ChaveDedup | string? | Chave única por alerta (`{tipo}-{id}-{dias}d-{yyyyMMdd}`). Índice único parcial (WHERE NOT NULL). Impede reenvio mesmo com job rodando múltiplas vezes ao dia. |

### `PreferenciasNotificacao`
| Campo | Tipo | Descrição |
|-------|------|-----------|
| Id | Guid | PK |
| TenantId | Guid | FK → Tenant |
| UsuarioId | Guid | FK → Usuario |
| TarefasInApp | bool | Alertas de tarefas no sistema |
| TarefasEmail | bool | Alertas de tarefas por e-mail |
| EventosInApp | bool | Alertas de eventos no sistema |
| EventosEmail | bool | Alertas de eventos por e-mail |
| PrazosInApp | bool | Prazos processuais no sistema |
| PrazosEmail | bool | Prazos processuais por e-mail |
| PublicacoesInApp | bool | Publicações/andamentos no sistema |
| PublicacoesEmail | bool | Publicações/andamentos por e-mail |
| TrialInApp | bool | Trial expirando no sistema |
| GeralInApp | bool | Notificações gerais no sistema |
| AtualizadoEm | DateTime | Última atualização |

Índice único em `(TenantId, UsuarioId)`.

---

## Arquivos

| Arquivo | Descrição |
|---------|-----------|
| `Domain/Entities/PreferenciasNotificacao.cs` | Entidade de preferências |
| `Infrastructure/Configurations/PreferenciasNotificacaoConfiguration.cs` | EF config |
| `Application/DTOs/Notificacoes/PreferenciasNotificacaoDto.cs` | DTOs de get/update |
| `Application/Interfaces/IPreferenciasNotificacaoService.cs` | Interface do serviço |
| `Infrastructure/Services/PreferenciasNotificacaoService.cs` | Implementação com GetOrCreate |
| `Infrastructure/Services/NotificacaoService.cs` | Adicionado `GetHistoricoAsync` |
| `Infrastructure/Jobs/AlertasJob.cs` | Verifica preferências antes de notificar/enviar e-mail; passa `ChaveDedup` em todas as chamadas; cron alterado para `"0 */3 * * *"` (a cada 3h) |
| `API/Controllers/NotificacoesController.cs` | Adicionados endpoints de preferências e histórico |
| `wwwroot/pages/configuracoes.html` | Seção "Preferências de Notificações" com tabela de toggles |
| `Domain/Entities/Notificacao.cs` | Campo `ChaveDedup` adicionado |
| `Infrastructure/Configurations/NotificacaoConfiguration.cs` | Índice único parcial em `ChaveDedup` |
| Migration `AddNotificacaoChaveDedup` | Coluna + índice único parcial no banco |

---

## Frontend

A seção de Preferências na tela de Configurações exibe uma tabela com:
- Linha por categoria de alerta
- Coluna "No sistema" (checkbox)
- Coluna "Por e-mail" (checkbox ou "—" se não aplicável)
- Botão "Salvar preferências" que faz `PUT /api/notificacoes/preferencias`
- Feedback de sucesso/erro inline

O sidebar foi atualizado para usar injeção dinâmica via `layout.js` (removido hardcoded).
