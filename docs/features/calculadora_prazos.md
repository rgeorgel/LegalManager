# Spec — Calculadora de Prazos

**Status:** Em definição
**Data:** 20/05/2026

---

## Visão geral

Incorporar a Calculadora de Prazos ao sistema como funcionalidade nativa, com persistência de feriados no banco de dados, exibição dos feriados na tela de configurações e agrupamento das calculadoras no menu lateral sob um novo grupo **Calculadoras**.

---

## 1. Banco de dados — tabela `feriados`

Criar nova tabela para armazenar os feriados utilizados pelo cálculo de prazos.

### DDL

```sql
CREATE TABLE feriados (
  id          SERIAL PRIMARY KEY,
  data        DATE        NOT NULL,
  nome        VARCHAR(120) NOT NULL,
  tipo        VARCHAR(20)  NOT NULL CHECK (tipo IN ('nacional', 'estadual', 'municipal')),
  uf          CHAR(2)      NULL,        -- obrigatório quando tipo = 'estadual' ou 'municipal'
  municipio   VARCHAR(100) NULL,        -- obrigatório quando tipo = 'municipal'
  ativo       BOOLEAN      NOT NULL DEFAULT TRUE,
  criado_em   TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
  atualizado_em TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX idx_feriados_data  ON feriados (data);
CREATE INDEX idx_feriados_tipo  ON feriados (tipo);
CREATE INDEX idx_feriados_ativo ON feriados (ativo);
```

### Regras de negócio

- `uf` é obrigatório quando `tipo` for `estadual` ou `municipal`; deve ser nulo quando `tipo` for `nacional`.
- `municipio` é obrigatório quando `tipo` for `municipal`; deve ser nulo nos demais casos.
- Não podem existir dois registros com a mesma combinação de `(data, tipo, uf, municipio)`.
- O campo `ativo` permite desativar um feriado sem excluí-lo, preservando histórico.
- Feriados nacionais de 2025 e 2026 devem ser inseridos via seed/migration inicial (ver lista no Anexo A).

### Seed inicial

Incluir migration com os feriados nacionais de 2025 e 2026 já mapeados. Ver **Anexo A** ao final deste documento.

---

## 2. Tela de configurações — aba Feriados

### Localização

Dentro da tela de **Configurações** do sistema, adicionar uma nova aba chamada **Feriados**, posicionada após as abas existentes.

### Comportamento

A aba é **somente leitura** para usuários comuns. O objetivo é oferecer visibilidade dos feriados que estão sendo considerados nos cálculos.

### O que exibir

- Lista paginada dos feriados cadastrados, ordenada por data.
- Colunas: **Data**, **Nome**, **Tipo** (badge colorido: nacional / estadual / municipal), **UF**, **Município**, **Status** (ativo / inativo).
- Filtros no topo: busca por nome, filtro por tipo, filtro por ano.
- Contador de registros no rodapé da lista.
- Não exibir botões de adicionar, editar ou excluir.

### Observação para administradores

Se houver perfil de administrador no sistema, avaliar em uma etapa futura a possibilidade de permitir que administradores gerenciem os feriados diretamente por esta tela. Fora do escopo desta entrega.

---

## 3. Menu lateral — grupo Calculadoras

### Estrutura atual relevante

O menu lateral atualmente contém os itens **Honorários** (sem grupo definido ou em grupo existente). A calculadora de prazos ainda não existe no sistema.

### Alteração

Criar um novo grupo no menu lateral chamado **Calculadoras** e mover / incluir os seguintes itens dentro dele:

| Item | Situação | Rota sugerida |
|---|---|---|
| Honorários | Mover para o novo grupo | `/calculadoras/honorarios` |
| Calculadora de Prazos | Novo item | `/calculadoras/prazos` |

O grupo deve aparecer colapsável (se o menu já suportar grupos colapsáveis) e exibir um ícone representativo — sugestão: ícone de calculadora ou balança.

### Ordem dos grupos no menu

Posicionar o grupo **Calculadoras** após os itens principais de operação do sistema (ex.: Dashboard, Processos, Clientes) e antes de itens secundários (ex.: Relatórios, Configurações). Ajustar conforme a hierarquia de uso existente.

---

## 4. Calculadora de Prazos — página

### Rota

`/calculadoras/prazos`

### Comportamento

Implementar a calculadora conforme o protótipo já desenvolvido (`calculadora-prazos.html`), com as seguintes adaptações para integração ao sistema:

**Fonte dos feriados**
Em vez de usar `localStorage`, a calculadora deve buscar os feriados via API a partir da tabela `feriados` do banco. Carregar no mount do componente e manter em memória durante a sessão.

**Endpoint sugerido**
```
GET /api/feriados?ativo=true
```
Retorno:
```json
[
  { "id": 1, "data": "2026-01-01", "nome": "Confraternização Universal", "tipo": "nacional" },
  ...
]
```

**Sem gestão de feriados na calculadora**
Remover a aba "Feriados" e o formulário de adição que existiam no protótipo standalone. A gestão de feriados passa a ser feita pela tela de Configurações (futuramente por administradores). A calculadora é apenas consumidora.

**Layout**
Adaptar o layout para seguir o design system do sistema (cores, tipografia, componentes), mantendo a estrutura funcional do protótipo.

---

## 5. Critérios de aceite

- [ ] Migration criada e executável sem erros em ambiente de staging.
- [ ] Seed com feriados nacionais 2025/2026 inserida corretamente.
- [ ] Aba "Feriados" visível em Configurações, com listagem e filtros funcionando.
- [ ] Grupo "Calculadoras" aparece no menu com os dois itens: Honorários e Calculadora de Prazos.
- [ ] Calculadora de Prazos calcula corretamente em modo dias úteis e dias corridos.
- [ ] Feriados carregados via API refletem o que está no banco.
- [ ] Feriados inativos não são considerados no cálculo.
- [ ] Timeline de resultado exibe corretamente os feriados e fins de semana do período.

---

## 6. Fora do escopo desta entrega

- Interface para cadastro/edição de feriados por administradores.
- Feriados estaduais e municipais pré-cadastrados (apenas nacionais no seed).
- Histórico de cálculos realizados.
- Exportação/importação de feriados pela interface.

---

## Anexo A — Feriados nacionais para seed

### 2025

| Data | Nome |
|---|---|
| 01/01/2025 | Confraternização Universal |
| 18/04/2025 | Sexta-feira Santa |
| 21/04/2025 | Tiradentes |
| 01/05/2025 | Dia do Trabalho |
| 19/06/2025 | Corpus Christi |
| 07/09/2025 | Independência do Brasil |
| 12/10/2025 | Nossa Senhora Aparecida |
| 02/11/2025 | Finados |
| 15/11/2025 | Proclamação da República |
| 20/11/2025 | Consciência Negra |
| 25/12/2025 | Natal |

### 2026

| Data | Nome |
|---|---|
| 01/01/2026 | Confraternização Universal |
| 03/04/2026 | Sexta-feira Santa |
| 21/04/2026 | Tiradentes |
| 01/05/2026 | Dia do Trabalho |
| 04/06/2026 | Corpus Christi |
| 07/09/2026 | Independência do Brasil |
| 12/10/2026 | Nossa Senhora Aparecida |
| 02/11/2026 | Finados |
| 15/11/2026 | Proclamação da República |
| 20/11/2026 | Consciência Negra |
| 25/12/2026 | Natal |
