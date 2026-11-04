# AGENTS.md — Causify (LegalManager)

Instruções para agentes de IA que trabalham neste repositório.
Compatível com Claude, OpenAI Codex, Minimax e outros.

---

## Obrigações após mudanças no Frontend

Ao modificar qualquer arquivo em `src/LegalManager.API/wwwroot/` (HTML, JS, CSS):

### 1. Verificar sintaxe JS (sem backend — rápido)

```powershell
.\scripts\check-js.ps1
```
ou via Node.js:
```bash
node tests/frontend/scripts/check-syntax.mjs
```

### 2. Executar testes de smoke (requer API em `http://localhost:5123`)

```powershell
.\scripts\test-frontend.ps1 -Suite smoke
```
ou:
```bash
cd tests/frontend && npx playwright test tests/smoke
```

### 3. Testes ESLint

```powershell
.\scripts\test-frontend.ps1 -Suite lint
```

### 4. Gerar/atualizar testes para nova funcionalidade

| Mudança | Ação |
|---|---|
| Nova página em `wwwroot/pages/` | Adicionar path em `tests/frontend/tests/smoke/admin.spec.ts` |
| Nova página em `wwwroot/cliente/` | Adicionar em `tests/frontend/tests/smoke/client-portal.spec.ts` |
| Novo fluxo de negócio | Criar arquivo em `tests/frontend/tests/flows/` |

---

## Arquitetura do Frontend

| Caminho | Conteúdo |
|---|---|
| `wwwroot/js/` | Módulos JS (ES Modules, sem bundler) |
| `wwwroot/pages/` | Páginas do portal admin (requer auth) |
| `wwwroot/cliente/` | Portal do cliente (auth separada) |
| `wwwroot/superadmin/` | Painel super admin (auth separada, perfil SuperAdmin) |
| `wwwroot/css/` | Estilos globais |

## Export / Import de Tenant (Super Admin)

Painel `/superadmin/tenants.html` (e detail modal) oferece botões **Exportar** e **Importar (substituir)** para replicar um tenant em outro ambiente (ex.: debug de produção).

- **Endpoint**: `GET /api/superadmin/tenants/{id}/export` → arquivo JSON
- **Endpoint**: `POST /api/superadmin/tenants/{id}/import` (multipart `file` + `confirmation` = nome do tenant)
- **Anonimização automática**: emails → `*@causify-replica.com`, telefones → `+5511900000000`
- **Senhas resetadas** para `Causify@Replic@2026!` em todos os usuários importados
- **Documentos no OCI**: apenas metadata (ObjectKey) — arquivos binários não viajam no export
- **Wipe automático**: import apaga todos os dados do tenant destino antes de inserir
- **Auditoria**: cada operação registra `AuditLog` com `EXPORT`/`IMPORT` em `TenantData`
- **Senhas no export**: nunca — apenas reset no import
- Não exporta: `WaitlistEntries`, `RefreshTokens`, `AuditLogs`, `ProcessosImportacaoCache`, `Tenant` do sistema
- Teste E2E: `tests/frontend/tests/flows/tenant-export-import.spec.ts` (precisa de `TEST_SUPERADMIN_TENANT_ORIGEM` e `TEST_SUPERADMIN_TENANT_DESTINO`)

## Autenticação

**Portal Admin:**
- Login: `POST /api/auth/login` — body: `{ email, senha }`
- Resposta: `{ accessToken, refreshToken, usuario }`
- sessionStorage: `access_token`, `refresh_token`, `user`
- Sem token → redireciona para `/login.html`

**Portal do Cliente:**
- Login: `POST /api/portal/login` — body: `{ email, senha }`
- Resposta: `{ accessToken, perfil }`
- sessionStorage: `cliente_token`, `cliente_user`
- Sem token → redireciona para `/cliente/index.html`

**Painel Super Admin:**
- Login: mesmo endpoint `POST /api/auth/login` mas requer perfil `SuperAdmin` na resposta
- sessionStorage: `sa_access_token`, `sa_refresh_token`, `sa_user`
- Sem token ou perfil errado → redireciona para `/superadmin/login.html`

## Backend

- **URL**: `http://localhost:5123`
- **Rodar**: `dotnet run --project src/LegalManager.API`
- **Stack**: ASP.NET Core 10 + PostgreSQL + Entity Framework

## Convenções

- **Sem framework JS** — vanilla ES Modules (`export`/`import`)
- **Sem jQuery, React, Vue** — DOM API nativa
- **Sem bundler** — arquivos servidos diretamente
- **Nome comercial**: Causify (não LegalManager nas UIs)
- **Idioma**: Português (BR) em toda a interface
- sessionStorage para auth — não usar localStorage

## Padrão de card (visão em blocos das listagens)

Todo card de listagem usa o componente compartilhado: CSS `.ent-*` em `wwwroot/css/styles.css` e
`cardEntidade()` / `ativarCardsClicaveis()` / `ICONES` em `wwwroot/js/cards.js`. Não crie CSS de
card próprio na página. Referência: `pages/honorarios-contratos.html` (`renderCard`).

- **O card inteiro abre o detalhe** (`href`); o título também é o link (teclado, clique do meio).
  Ctrl/⌘ abre em nova aba; texto selecionado não navega. Chame `ativarCardsClicaveis(container)` uma vez.
  Entidade sem página própria (abre em modal, como a tarefa): `ativarCardsClicaveis(container, { abrir })`
  e um `href` que abra o modal (ex.: `?abrirId=`), para Ctrl/⌘ + clique em nova aba.
- **Sem botões "Ver" nem "Editar"** no card — a edição acontece dentro do detalhe. No rodapé só
  ações secundárias (ex.: `ICONES.download` + "Extrato"), com ícones bem distintos e rótulo quando couber.
- **Faixas de altura fixa** para alinhar cards vizinhos: título + status, selos (`tagsHtml`;
  `ent-encolhe` no selo que pode ser cortado), descrição — cada uma em uma linha com reticências e
  texto completo na dica. Passe `descricao: ''` quando vazia, para reservar a linha.
- **Textos** (parte, vara, responsável...) em `campos`: linhas "rótulo  valor" na largura do card,
  peso normal, uma linha com reticências (no celular quebram). Vazio mostra "—" e a linha fica.
  `valorHtml` para link (ex.: processo); `perigo` pinta de vermelho (ex.: prazo atrasado).
- **Valores curtos** (dinheiro, contagens) em `numeros`: grade de 2–4 colunas, uma linha cada
  (`perigo` pinta de vermelho; `peso` alarga uma coluna; `dica` substitui a dica padrão).
  Não ponha texto longo em `numeros` — corta demais.
- **Peso de fonte contido**: só título e valores curtos em semi-negrito; o resto em peso normal.
- Quando o título é o próprio conteúdo (texto da tarefa): `tituloCompleto: true` — quebra linha e
  aparece inteiro, mesmo no desktop (abre mão do alinhamento do topo).
- Identificadores (CNJ): `tituloMono: true` — no celular o status desce de linha e o título não é cortado.
- `dados: { id }` vira `data-id` no card; `classe` adiciona modificadores da página (ex.: `favorito`).
- **Rodapé**: à esquerda `meta` (principal em negrito, secundário, `progresso` 0–100); à direita `acoesHtml`.
  Sem `meta` nem `acoesHtml`, não há rodapé. Ações da entidade (concluir, excluir...) vão no menu ⋮:
  botão `.ent-card-menu` no `statusHtml` e `abrirMenuCard(botao, itens)` de `js/cards.js` no clique
  (itens `{ icone, rotulo, acao, perigo }`; falsy é ignorado) — não gaste uma linha de rodapé com
  botões. Referência: `cardTarefa` em `pages/tarefas.html`. (Contatos ainda usa um menu próprio.)
  Caixa de seleção (ações em massa) vai em `prefixoHtml`.
- No celular (uma coluna) título e campos quebram linha e aparecem inteiros; no desktop, uma linha.
- **Borda esquerda = estado**: `estado` ok | danger | warn | info | muted.
- Campos `*Html` entram como estão (escape é de quem chama); os demais são escapados pelo componente.
- Cores sempre por variável de tema (`--color-surface`, `--color-text`...), nunca `#fff` fixo.
- Grade `.ent-grid`: no celular vira uma coluna sem estourar a largura (`minmax(0,1fr)`).

- Clicável sem página nem URL (modelo de documento → modal "Usar"): `abrivel: true` + `abrir` em
  `ativarCardsClicaveis`; o card recebe foco e abre com Enter.
- Entidade sem detalhe (publicação): sem `href` — o card não é clicável. Conteúdo longo (texto,
  formulário da sugestão da IA) em `corpoHtml`; numa coluna, não em `.ent-grid`.

Já migradas: Honorários Contratos, Processos, Contatos, Tarefas (visão em blocos; as listas e o
kanban seguem com marcação própria), Publicações e Modelos (Documentos). A lista de arquivos de
Documentos é um explorador (linhas), não card. Pendente: o menu ⋮ de Contatos ainda é próprio
(trocar por `abrirMenuCard`).

## Pitfalls Conhecidos

- Erros de sintaxe JS (ex: `}` extra) só aparecem em runtime → sempre rode `check-js.ps1` após editar `.js`
- `addInitScript` do Playwright injeta tokens **antes** dos scripts da página — é assim que funciona a auth nos testes
- Páginas com `?id=` query param podem exibir mensagem de erro sem ID, mas não devem lançar exceções JS
- Testes de fluxo (`tests/flows/`) requerem dados reais no banco — não são isolados
- Testes visuais precisam de baselines: rode `npm run test:update-snapshots` na primeira vez

## Configuração de Credenciais para Testes

```bash
cp tests/frontend/.env.example tests/frontend/.env
# Editar .env com credenciais reais
```
