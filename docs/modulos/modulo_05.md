# Módulo 05 — Gestão Financeira

## Status: ✅ Completo

---

## Visão Geral

Controle de receitas e despesas do escritório, vinculadas a processos ou contatos. Inclui dashboard de resumo financeiro, registro de pagamentos e filtros por tipo e status.

---

## Funcionalidades Implementadas

- Criação de lançamentos: **Receita** ou **Despesa**
- Categorias: Honorário, Custas, Perícia, Depósito, Reembolso, Salário, Aluguel, Software, Marketing, Multa, Outro
- Vínculo opcional com processo e/ou contato
- Status do ciclo: `Pendente` → `Pago` | `Vencido` | `Cancelado`
- Marcação de pagamento via `POST /api/financeiro/{id}/pagar` com data opcional
- Cancelamento via `POST /api/financeiro/{id}/cancelar`
- Edição de categoria, valor, vencimento e descrição
- **Resumo financeiro** com totais de receitas/despesas pagas, saldo, pendentes e vencidos
- Filtros: tipo (Receita/Despesa), status, processo, contato
- Paginação (padrão 20 por página)
- Isolamento por tenant

---

## Arquitetura

```
FinanceiroController → IFinanceiroService → AppDbContext (LancamentosFinanceiros)
```

---

## Endpoints

| Método | Rota | Descrição |
|--------|------|-----------|
| GET | `/api/financeiro` | Listar lançamentos (filtros: tipo, status, processoId, contatoId) |
| GET | `/api/financeiro/resumo` | Resumo financeiro (filtros: ano, mes) |
| GET | `/api/financeiro/{id}` | Buscar lançamento |
| POST | `/api/financeiro` | Criar lançamento |
| PUT | `/api/financeiro/{id}` | Atualizar lançamento |
| POST | `/api/financeiro/{id}/pagar` | Marcar como pago |
| POST | `/api/financeiro/{id}/cancelar` | Cancelar lançamento |

---

## Modelos de Dados (EF Core)

```csharp
LancamentoFinanceiro {
  Id, TenantId, ProcessoId?, ContatoId?,
  Tipo (Receita|Despesa), Categoria, Valor,
  Descricao?, DataVencimento, DataPagamento?,
  Status (Pendente|Pago|Vencido|Cancelado), CriadoEm
}
```

**Configurações EF:**
- Precisão decimal `(18,2)` para Valor
- Índices em `(TenantId, DataVencimento)` e `(TenantId, Status)`
- FK para Processo com `SetNull` (processo pode ser excluído sem perder o lançamento)
- FK para Contato com `SetNull`

---

## Migration

`AddFase3_FinanceiroTimesheet` — cria tabela `LancamentosFinanceiros`

---

## Frontend

| Arquivo | Descrição |
|---------|-----------|
| `/pages/financeiro.html` | Dashboard de resumo + lista de lançamentos + modal de criação/edição |

### Funcionalidades da UI
- 6 cards de resumo: Receitas Pagas, Despesas Pagas, Saldo (colorido por positivo/negativo), A Receber, A Pagar, Receitas Vencidas
- Filtro por tipo e status com paginação
- Modal de criação/edição com categorias dinâmicas por tipo
- Botões de ação inline: Pagar, Editar, Cancelar por status do lançamento
- Valores formatados em BRL (R$)
