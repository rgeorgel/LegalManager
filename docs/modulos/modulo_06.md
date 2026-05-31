# Módulo 06 — Cronômetro e Timesheet

## Status: ✅ Completo

---

## Visão Geral

Controle de horas trabalhadas por processo ou tarefa. Cronômetro em tempo real com start/stop, e lançamentos manuais para horas retroativas.

---

## Funcionalidades Implementadas

- **Cronômetro em tempo real:** iniciar, parar — apenas um cronômetro ativo por usuário por vez
- **Lançamento manual:** informar início e fim diretamente (validação: fim > início)
- **Duração calculada automaticamente** em minutos ao parar o cronômetro
- Vínculo opcional com processo e/ou tarefa
- Listagem com filtros por data (de/até) e paginação
- Exclusão de registros (exceto cronômetro ativo)
- Isolamento por tenant

---

## Arquitetura

```
TimesheetController → ITimesheetService → AppDbContext (RegistrosTempo)
```

---

## Endpoints

| Método | Rota | Descrição |
|--------|------|-----------|
| GET | `/api/timesheet` | Listar registros (filtros: usuarioId, processoId, de, ate) |
| GET | `/api/timesheet/{id}` | Buscar registro |
| GET | `/api/timesheet/ativo` | Cronômetro ativo do usuário atual |
| POST | `/api/timesheet/iniciar` | Iniciar cronômetro |
| POST | `/api/timesheet/parar` | Parar cronômetro ativo |
| POST | `/api/timesheet/manual` | Criar lançamento manual |
| PUT | `/api/timesheet/{id}` | Atualizar descrição/vínculo |
| DELETE | `/api/timesheet/{id}` | Excluir registro |

---

## Modelos de Dados (EF Core)

```csharp
RegistroTempo {
  Id, TenantId, UsuarioId, ProcessoId?, TarefaId?,
  Inicio, Fim?, DuracaoMinutos?,
  Descricao?, EmAndamento, CriadoEm
}
```

**Configurações EF:**
- Índices em `(TenantId, UsuarioId)` e `(TenantId, EmAndamento)`
- FK para Processo com `SetNull`
- FK para Tarefa com `SetNull`

---

## Migration

`AddFase3_FinanceiroTimesheet` — cria tabela `RegistrosTempo`

---

## Frontend

| Arquivo | Descrição |
|---------|-----------|
| `/pages/timesheet.html` | Cronômetro em tempo real + listagem de registros + modal de lançamento manual |

### Funcionalidades da UI
- Display de cronômetro HH:MM:SS atualizado a cada segundo
- Botões Iniciar/Parar que alternam conforme estado
- Filtro por intervalo de datas
- Duração exibida como "Xh Ymin" ou "Ymin"
- Exclusão inline de registros
- Modal de lançamento manual com `datetime-local` inputs
