# Causify — LegalManager

## Testes Obrigatórios

Após qualquer mudança em `wwwroot/**`:

```powershell
# Verificação rápida de sintaxe JS (sem backend) — .js e scripts inline dos .html.
# Multiplataforma (não exige PowerShell); é o mesmo check que roda no CI.
node scripts/check-js.mjs

# Versão PowerShell (só arquivos .js)
.\scripts\check-js.ps1

# Suite completa (requer: dotnet run --project src/LegalManager.API)
.\scripts\test-frontend.ps1

# Apenas smoke tests
.\scripts\test-frontend.ps1 -Suite smoke
```

## CI

`.github/workflows/ci.yml` roda em todo push na `main` e em PRs: build + testes unitários e de
integração (.NET, EF InMemory — sem PostgreSQL) e `node scripts/check-js.mjs`. Os testes .NET
coletam cobertura (`tests/coverage.runsettings`); o resumo combinado aparece no Summary do run e o
HTML no artefato `coverage-report`. O job falha se a cobertura de linhas ficar abaixo de 80%
(`COBERTURA_MINIMA` no workflow) — código novo precisa vir com testes. Localmente: `./coverage-test.sh`.

## Datas (prazos e eventos)

Prazos (`Tarefa.Prazo`) e eventos (`Evento.DataHora`) são gravados na hora "de parede"
digitada no formulário, sem conversão de fuso. No backend, compare com o "agora" do escritório,
`FusoHorario.AgoraParede(await db.DoTenantAsync(tenantId))` (fuso em Configurações → Perfil do
Escritório; padrão Brasília, `BrasiliaTime.AgoraParede`) — nunca `DateTime.UtcNow`; no frontend, leia com `dataParede()` de
`/js/utils.js` (não `new Date()`). Carimbos gerados no servidor (`CriadoEm` etc.) são UTC reais.
Texto para pessoas: `BrasiliaTime.PtBr`; formatos de máquina (URL, hash): `CultureInfo.InvariantCulture`.

## Padrão de card (visão em blocos das listagens)

Todo card de listagem usa o componente compartilhado: CSS `.ent-*` em `wwwroot/css/styles.css` e
`cardEntidade()` / `ativarCardsClicaveis()` / `ICONES` em `wwwroot/js/cards.js`. Não crie CSS de
card próprio na página. Referência: `pages/honorarios-contratos.html` (`renderCard`).

- **O card inteiro abre o detalhe** (`href`); o título também é o link (teclado, clique do meio).
  Ctrl/⌘ abre em nova aba; texto selecionado não navega. Chame `ativarCardsClicaveis(container)` uma vez.
- **Sem botões "Ver" nem "Editar"** no card — a edição acontece dentro do detalhe. No rodapé só
  ações secundárias (ex.: `ICONES.download` + "Extrato"), com ícones bem distintos e rótulo quando couber.
- **Faixas de altura fixa** para alinhar cards vizinhos: título + status, selos (`tagsHtml`;
  `ent-encolhe` no selo que pode ser cortado), descrição — cada uma em uma linha com reticências e
  texto completo na dica. Passe `descricao: ''` quando vazia, para reservar a linha.
- **Textos** (parte, vara, responsável...) em `campos`: linhas "rótulo  valor" na largura do card,
  peso normal, uma linha com reticências (no celular quebram). Vazio mostra "—" e a linha fica.
- **Valores curtos** (dinheiro, contagens) em `numeros`: grade de 2–4 colunas, uma linha cada
  (`perigo` pinta de vermelho; `peso` alarga uma coluna; `dica` substitui a dica padrão).
  Não ponha texto longo em `numeros` — corta demais.
- **Peso de fonte contido**: só título e valores curtos em semi-negrito; o resto em peso normal.
- Identificadores (CNJ): `tituloMono: true` — no celular o status desce de linha e o título não é cortado.
- `dados: { id }` vira `data-id` no card; `classe` adiciona modificadores da página (ex.: `favorito`).
- **Rodapé**: à esquerda `meta` (principal em negrito, secundário, `progresso` 0–100); à direita `acoesHtml`.
- **Borda esquerda = estado**: `estado` ok | danger | warn | info | muted.
- Campos `*Html` entram como estão (escape é de quem chama); os demais são escapados pelo componente.
- Cores sempre por variável de tema (`--color-surface`, `--color-text`...), nunca `#fff` fixo.
- Grade `.ent-grid`: no celular vira uma coluna sem estourar a largura (`minmax(0,1fr)`).

Já migradas: Honorários Contratos, Processos (visão em blocos; a lista segue com marcação própria).
Ainda com card próprio, a migrar: Contatos, Tarefas, Publicações, Documentos.

## Backend

- **URL**: http://localhost:5123
- **Rodar**: `dotnet run --project src/LegalManager.API`
- **Migrations**: `dotnet ef database update --project src/LegalManager.Infrastructure --startup-project src/LegalManager.API`
  — o histórico até out/2026 foi consolidado em `Persistence/Migrations/*_Baseline`. Bancos anteriores
  ao squash são marcados como migrados pela API ao iniciar (`MigracoesBaseline.RegistrarAsync`); neles,
  rode a API uma vez antes de usar o `database update`, que recusa reaplicar a baseline.

## Testes Frontend (Playwright)

```
tests/frontend/
  fixtures/index.ts          # Auth: injeta JWT no sessionStorage via addInitScript
  tests/smoke/
    public.spec.ts           # Páginas públicas (sem auth)
    admin.spec.ts            # Todas as páginas do portal admin
    client-portal.spec.ts    # Portal do cliente
    visual.spec.ts           # Regressão visual (baselines em *-snapshots/)
  tests/flows/
    auth.spec.ts             # Login, logout, sessão expirada
    processos.spec.ts        # Fluxos de processos
    contatos.spec.ts         # Fluxos de contatos
```

**Primeira execução:**
```powershell
cp tests/frontend/.env.example tests/frontend/.env
# Editar .env com credenciais
.\scripts\test-frontend.ps1   # instala deps automaticamente
```

## Hooks Automáticos

O arquivo `.claude/settings.json` contém hooks que executam `check-js-after-edit.ps1`
automaticamente após edições em arquivos `.js`, retornando erros de sintaxe para
contexto antes que você continue.

## Estrutura do Projeto

```
src/
  LegalManager.API/            # ASP.NET Core 10 + wwwroot (frontend)
  LegalManager.Application/    # Use cases e DTOs
  LegalManager.Domain/         # Entidades, interfaces
  LegalManager.Infrastructure/ # EF Core, PostgreSQL, serviços externos
tests/
  LegalManager.UnitTests/      # xUnit — controllers, services (mocks)
  LegalManager.IntegrationTests/ # xUnit — workflows com EF InMemory
  frontend/                    # Playwright — testes de UI E2E
scripts/
  check-js.ps1                 # Sintaxe JS (rápido, sem backend)
  test-frontend.ps1            # Suite completa de testes frontend
  hooks/check-js-after-edit.ps1 # Hook chamado automaticamente pelo Claude Code
```

> Ver `AGENTS.md` para instruções compatíveis com outros agentes de IA.
