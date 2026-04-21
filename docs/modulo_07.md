# Módulo 07 — Indicadores e Relatórios

## Status: ✅ Completo

---

## Visão Geral

Painel analítico com indicadores consolidados de processos, tarefas, financeiro e timesheet. Dados agregados por tenant em uma única chamada `GET /api/indicadores`. Dashboard principal atualizado com resumo financeiro do mês.

---

## Funcionalidades Implementadas

### 7.1 Indicadores de Processos
- Total geral e por status: Ativos, Encerrados, Suspensos, Arquivados
- Novos processos abertos no mês atual
- Distribuição por área do direito (barras horizontais)
- Distribuição por fase processual (apenas ativos)

### 7.2 Indicadores de Tarefas
- Contagem por status: Pendentes, Em Andamento, Concluídas
- Tarefas atrasadas (prazo < agora, status aberto)
- Concluídas no mês atual
- Abertas por prioridade: Urgente / Alta / Média / Baixa

### 7.3 Indicadores Financeiros (mês atual)
- Receitas e despesas pagas no mês
- Saldo do mês (positivo = verde, negativo = vermelho)
- A receber e a pagar (pendentes)
- Recebíveis vencidos
- **Gráfico de barras** dos últimos 6 meses (receitas vs. despesas)

### 7.4 Indicadores de Timesheet (mês atual)
- Total de horas registradas e número de registros
- Comparativo com mês anterior
- Top 5 processos com mais horas lançadas

---

## Arquitetura

```
GET /api/indicadores
  └── IndicadoresController
        └── IIndicadoresService / IndicadoresService
              └── AppDbContext
                    ├── Processos (GroupBy Area, Fase, Status)
                    ├── Tarefas (GroupBy Status, Prioridade)
                    ├── LancamentosFinanceiros (Sum por tipo/mês)
                    └── RegistrosTempo (Sum minutos por mês/processo)
```

Todos os dados são carregados em paralelo numa única request do frontend.

---

## Endpoints

| Método | Rota | Descrição |
|--------|------|-----------|
| GET | `/api/indicadores` | Retorna todos os indicadores do tenant |

### Estrutura da resposta

```json
{
  "processos": {
    "total": 42, "ativos": 35, "encerrados": 5, "suspensos": 2,
    "arquivados": 0, "novosEsteMes": 3,
    "porArea": [{ "label": "Civil", "count": 18 }, ...],
    "porFase": [{ "label": "Conhecimento", "count": 20 }, ...]
  },
  "tarefas": {
    "pendentes": 12, "emAndamento": 4, "concluidas": 87,
    "atrasadas": 3, "concluidasEsteMes": 9,
    "porPrioridade": [{ "label": "Urgente", "count": 2 }, ...]
  },
  "financeiro": {
    "totalReceitasMes": 15000.00, "totalDespesasMes": 4200.00,
    "saldoMes": 10800.00, "receitasPendentes": 8000.00,
    "despesasPendentes": 1500.00, "receitasVencidas": 2000.00,
    "despesasVencidas": 0,
    "ultimosSeisMeses": [{ "mes": "nov/24", "receitas": 12000, "despesas": 3500 }, ...]
  },
  "timesheet": {
    "minutosEsteMes": 2880, "minutosMesAnterior": 2400,
    "totalRegistrosEsteMes": 22,
    "topProcessos": [{ "label": "0001234-...", "count": 480 }, ...]
  }
}
```

---

## Dashboard Atualizado

O `dashboard.html` agora inclui:
- Seção **Financeiro do mês** com 6 cards: Receitas, Despesas, Saldo, A Receber, A Pagar, Recebíveis Vencidos
- Sidebar injetada dinamicamente pelo `layout.js` (inclui link para Indicadores)
- `loadStats()` chamada uma única vez (remoção do `loadStats()` duplicado)
- Todos os erros de API tratados com `.catch(() => null)` para não bloquear as demais seções

---

## Frontend

| Arquivo | Descrição |
|---------|-----------|
| `/pages/indicadores.html` | Página de indicadores com 4 seções e gráficos de barras |
| `/pages/dashboard.html` | Atualizado com resumo financeiro do mês |
| `/js/layout.js` | Link "📈 Indicadores" adicionado ao nav dinâmico |

### UI da página de Indicadores
- **Barras horizontais** proporcionais ao valor máximo da categoria
- **Gráfico de evolução** financeira (últimos 6 meses) com colunas de receita (verde) e despesa (vermelho)
- Valores monetários formatados em BRL
- Horas formatadas como `Xh Ymin`
- Labels traduzidos (ex: `Trabalhista`, `Conhecimento`, `Média`)
- Carregamento único via `GET /api/indicadores`
