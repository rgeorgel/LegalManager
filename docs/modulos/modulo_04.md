# Módulo 04 — Gestão de Atividades (Tarefas, Eventos, Agenda e Kanban)

## Status: ✅ Completo

---

## Visão Geral

Controle completo das atividades do escritório: tarefas com prioridade e responsável, eventos/audiências vinculados a processos, calendário visual e notificações automáticas de prazo.

---

## Funcionalidades Implementadas

### 4.1 Tarefas

- Criação de tarefas vinculadas a processos, clientes ou de forma independente
- Campos: título, descrição, prazo (`DateTime`), prioridade (`Baixa`, `Media`, `Alta`, `Urgente`), status, tags
- Status do ciclo de vida: `Pendente` → `EmAndamento` → `Concluida` | `Cancelada`
- Responsável vinculado a um usuário do tenant
- Etiquetas (tags) customizáveis — tabela `TarefaTag`
- Soft delete — tarefas não são removidas, apenas canceladas
- Filtros: status, prioridade, responsável, processo, prazo vencendo

### 4.2 Eventos e Audiências

- Cadastro de eventos vinculados a processos
- Campos: título, tipo (`Audiencia`, `Reuniao`, `Pericia`, `Prazo`, `Outro`), data/hora, local, responsável, observações
- Alertas automáticos enviados por e-mail 1 dia antes do evento (via `AlertasJob`)

### 4.3 Agenda e Calendário

- Visualização de todos os eventos do tenant em calendário mensal (grade HTML)
- Listagem de próximos eventos ordenados por data
- Filtro por mês/ano via query string
- Exibição de tarefas com prazo no mesmo calendário

### Alertas Automáticos (Hangfire)

`AlertasJob` — executa diariamente às 08:00 UTC:
- Verifica tarefas com prazo vencendo hoje ou amanhã → envia e-mail ao responsável
- Verifica eventos com data amanhã → envia e-mail ao responsável
- Verifica prazos processuais vencendo nos próximos 3 dias → envia e-mail ao advogado

---

## Arquitetura

```
TarefasController    → ITarefaService → AppDbContext (Tarefas, TarefaTags)
EventosController    → IEventoService → AppDbContext (Eventos)
AlertasJob (Hangfire) → INotificacaoService + IEmailService
```

---

## Endpoints — Tarefas

| Método | Rota | Descrição |
|--------|------|-----------|
| GET | `/api/tarefas` | Listar tarefas (filtros: status, prioridade, responsavel, processoId) |
| GET | `/api/tarefas/{id}` | Buscar tarefa |
| POST | `/api/tarefas` | Criar tarefa |
| PUT | `/api/tarefas/{id}` | Atualizar tarefa |
| DELETE | `/api/tarefas/{id}` | Cancelar tarefa |

## Endpoints — Eventos

| Método | Rota | Descrição |
|--------|------|-----------|
| GET | `/api/eventos` | Listar eventos (filtros: mes, ano, processoId) |
| GET | `/api/eventos/{id}` | Buscar evento |
| POST | `/api/eventos` | Criar evento |
| PUT | `/api/eventos/{id}` | Atualizar evento |
| DELETE | `/api/eventos/{id}` | Remover evento |

---

## Modelos de Dados (EF Core)

```csharp
Tarefa {
  Id, TenantId, ProcessoId?, ContatoId?, ResponsavelId,
  Titulo, Descricao, Prazo, Prioridade, Status,
  CriadoEm, ConcluidoEm?
}

TarefaTag { Id, TarefaId, Tag }

Evento {
  Id, TenantId, ProcessoId?, ResponsavelId,
  Titulo, Tipo, DataHora, Local?, Observacoes, CriadoEm
}
```

---

## Migration

`InitialCreate` — cria `Tarefas`, `TarefaTags`, `Eventos`

---

## Testes

`TarefaServiceTests.cs` — cobre criação, isolamento de tenant, filtros de status e prazo

---

## Frontend

| Arquivo | Descrição |
|---------|-----------|
| `/pages/tarefas.html` | Lista de tarefas com filtros, badges de prioridade/status e modal de criação/edição |
| `/pages/agenda.html` | Calendário mensal + listagem de próximos eventos |
| `/js/tarefas.js` | CRUD de tarefas |
| `/js/agenda.js` | Navegação mensal, renderização de calendário e CRUD de eventos |

**Calendário:** grade de 7 colunas (Seg–Dom) com células clicáveis para adicionar eventos. Eventos do dia exibidos como chips coloridos por tipo.

---

### 4.4 Kanban

- Visualização de tarefas em 3 colunas: **Pendente**, **Em Andamento**, **Concluída**
- Drag-and-drop via HTML5 Drag API — arrastar card entre colunas move a tarefa
- Atualização otimista (UI atualiza imediatamente, reverte em caso de erro)
- Cards coloridos por prioridade (borda lateral: vermelho urgente → verde baixa)
- Exibe prazo, responsável e badge de atrasada
- Novo endpoint: `PATCH /api/tarefas/{id}/mover` com body `{ "status": "EmAndamento" }`

| Arquivo | Descrição |
|---------|-----------|
| `/pages/kanban.html` | Quadro Kanban com drag-and-drop |
