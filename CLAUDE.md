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
integração (.NET, EF InMemory — sem PostgreSQL) e `node scripts/check-js.mjs`.

## Datas (prazos e eventos)

Prazos (`Tarefa.Prazo`) e eventos (`Evento.DataHora`) são gravados na hora "de parede"
digitada no formulário, sem conversão de fuso. No backend, compare com o "agora" do escritório,
`FusoHorario.AgoraParede(await db.DoTenantAsync(tenantId))` (fuso em Configurações → Perfil do
Escritório; padrão Brasília, `BrasiliaTime.AgoraParede`) — nunca `DateTime.UtcNow`; no frontend, leia com `dataParede()` de
`/js/utils.js` (não `new Date()`). Carimbos gerados no servidor (`CriadoEm` etc.) são UTC reais.
Texto para pessoas: `BrasiliaTime.PtBr`; formatos de máquina (URL, hash): `CultureInfo.InvariantCulture`.

## Backend

- **URL**: http://localhost:5123
- **Rodar**: `dotnet run --project src/LegalManager.API`
- **Migrations**: `dotnet ef database update --project src/LegalManager.Infrastructure --startup-project src/LegalManager.API`

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
