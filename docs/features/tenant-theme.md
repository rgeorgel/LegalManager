# Tenant Theme — White-label por escritório

## Visão geral

Permitir que cada **tenant** (escritório) customize a aparência visual do Causify dentro do mesmo domínio: cor primária, cor da sidebar, logo, accent e (opcionalmente) CSS customizado. Portal admin e portal do cliente herdam o tema do tenant.

## Diagnóstico do estado atual

| Recurso | Local | Estado |
|---|---|---|
| Entidade `Tenant` com `Id`, `Nome`, `LogoUrl` | `src/LegalManager.Domain/Entities/Tenant.cs:5` | ✅ pronto |
| `ITenantContext` resolvendo tenant via JWT claim | `src/LegalManager.Infrastructure/Identity/TenantContext.cs:8` | ✅ pronto |
| `:root` com CSS variables (`--color-primary`, `--color-sidebar`, ...) | `src/LegalManager.API/wwwroot/css/styles.css:1` | ✅ **perfeito** para temas |
| `:root` com mesmas CSS variables no portal cliente | `src/LegalManager.API/wwwroot/cliente/css/portal.css:2` | ✅ pronto |
| `AuthResponseDto.Usuario.NomeEscritorio` retornado no login | `src/LegalManager.Application/DTOs/Auth/LoginDto.cs:17` | parcial (sem tema) |
| `apiFetch` + `setSession` cacheiam `usuario` em sessionStorage | `src/LegalManager.API/wwwroot/js/api.js:7` | ✅ pronto |
| `initLayout()` aplicado em 20 páginas admin | `src/LegalManager.API/wwwroot/js/layout.js:73` | ✅ ponto único |
| `initLayout()` aplicado no portal cliente | `src/LegalManager.API/wwwroot/cliente/js/clienteLayout.js` | ✅ pronto |

**Conclusão**: 70% da infraestrutura já existe. Mudanças são aditivas e localizadas.

## Plano por fase

### Fase 1 — Modelo de dados (~2h)

Adicionar à entidade `Tenant`:

```csharp
public string? PrimaryColor { get; set; }      // hex, ex "#1a56db"
public string? SidebarColor { get; set; }      // hex, ex "#1e2a3b"
public string? AccentColor { get; set; }       // hex
public string? LayoutMode { get; set; }        // "default" | "compact"
public string? CustomCss { get; set; }         // CSS arbitrário (sanitizado em runtime)
```

- Migration EF Core: `AddTenantThemeFields`
- Todos os campos nullable → sem breaking change
- `LogoUrl` já existe e será reutilizado

### Fase 2 — Endpoint público de tema (~3h)

**Novo `TenantsThemeController`**:

| Método | Rota | Auth | Descrição |
|---|---|---|---|
| `GET` | `/api/tenants/current/theme` | Bearer | Retorna tema do tenant do JWT (somente dados públicos) |
| `PUT` | `/api/tenants/current/theme` | Admin | Atualiza tema (apenas Plus/Pro/Max/Enterprise) |
| `POST` | `/api/tenants/current/logo` | Admin | Upload de logo (via OCI Storage existente) |

**Novo `TenantsThemeService`** (Application + Infrastructure) para encapsular leitura/escrita.

**Extensão do `AuthResponseDto`** — adicionar `TenantThemeDto` ao `UsuarioInfoDto`:

```csharp
public record UsuarioInfoDto(
    Guid Id, string Nome, string Email, string Perfil,
    Guid TenantId, string NomeEscritorio, string Plano, DateTime? UltimoAcessoEm,
    TenantThemeDto Tema   // novo, null se não customizado
);
```

### Fase 3 — Frontend bootstrap sem FOUC (~4h)

**Novo `wwwroot/js/theme.js`** (ES module):

- `applyTenantTheme(theme)` — escreve CSS variables no `:root`
- `applyTenantLogo(theme)` — troca `src` de `.header-logo img`
- Lê tema de `sessionStorage` (cacheado no login) → sem GET extra
- Fallback silencioso se sessão/cache ausentes (valores default já estão no CSS)

**Modificar `wwwroot/js/api.js` `setSession`** — extrair `data.usuario.tema` e salvar em `sessionStorage.tenant_theme` separadamente.

**Modificar `wwwroot/js/layout.js` `initLayout`** — chamar `applyTenantTheme(theme)` no início, antes de qualquer `injectSidebarNav()` para evitar flash.

**Modificar `wwwroot/cliente/js/clienteLayout.js`** — mesma chamada para o portal cliente.

### Fase 4 — UI admin de edição (~6h)

Nova página `pages/tema.html` (apenas Plus+):

- Color pickers nativos (`<input type="color">`) para `PrimaryColor`/`SidebarColor`/`AccentColor`
- Upload de logo (drag-and-drop + preview) via `POST /api/tenants/current/logo`
- Select para `LayoutMode` (`default` / `compact`)
- Textarea para `CustomCss` (opcional, Pro+)
- Botão "salvar" → `PUT /api/tenants/current/theme`
- Botão "visualizar" que aplica tema ao vivo via `applyTenantTheme`
- Mensagem de erro se CustomCss tiver propriedades perigosas (`expression(`, `behavior:`, `javascript:`)

Adicionar entrada na sidebar admin (`layout.js` → `NAV_GROUPS`).

### Fase 5 — Subdomínios (NÃO IMPLEMENTADO — fora do escopo)

`escritorio-x.causify.com.br` → DNS wildcard + cert SSL wildcard + middleware pré-auth.

Complexidade alta (~16h), separada para iteração futura.

## Estimativa total (Fases 1–4)

| Fase | Horas | Risco |
|---|---|---|
| 1. Modelo + migration | 2h | Baixo (campos nullable) |
| 2. Endpoint + DTO + service | 3h | Baixo |
| 3. Frontend bootstrap | 4h | Médio (FOUC + 20 páginas) |
| 4. UI admin | 6h | Baixo |
| **TOTAL** | **~15h** | **Baixo-médio** |

## Riscos e mitigações

| Risco | Mitigação |
|---|---|
| FOUC (flash of unstyled content) ao navegar entre páginas | CSS variables aplicadas em `initLayout()` antes de `injectSidebarNav()`; tema cacheado em sessionStorage evita GET |
| `CustomCss` malicioso (XSS, exfiltration) | Whitelist em regex removendo `expression(`, `behavior:`, `javascript:`, `@import` |
| Mudar logo quebrando layout (imagem gigante) | CSS força `max-height: 40px` em `.header-logo img` |
| Cor com baixo contraste (acessibilidade) | Aviso visual no color picker (não bloqueia) |
| Tema aplicado após token ainda permitiria identificação | `applyTenantTheme` é idempotente e tolerante a campos ausentes |

## Critérios de aceite (Fases 1–4)

- [ ] Migration aplica sem erros em banco existente (campos nullable)
- [ ] `GET /api/tenants/current/theme` retorna 200 com tema (mesmo que todos defaults)
- [ ] `PUT /api/tenants/current/theme` valida plano Plus+ (403 caso contrário)
- [ ] Login retorna `usuario.tema` com cores customizadas
- [ ] Após login, primeira página renderizada já tem tema aplicado (sem flash)
- [ ] Portal cliente herda tema do tenant (mesmas CSS variables)
- [ ] Upload de logo substitui `LogoUrl` e reflete em todas as 20 páginas admin
- [ ] `CustomCss` com `expression(` é rejeitado pelo backend
- [ ] Trocar tema não exige reload — `applyTenantTheme()` reaplica ao vivo

## Arquivos a criar / modificar

### Criar

- `src/LegalManager.Application/DTOs/Tenants/TenantThemeDto.cs`
- `src/LegalManager.Application/DTOs/Tenants/UpdateTenantThemeDto.cs`
- `src/LegalManager.Application/Interfaces/ITenantsThemeService.cs`
- `src/LegalManager.Infrastructure/Services/TenantsThemeService.cs`
- `src/LegalManager.API/Controllers/TenantsThemeController.cs`
- `src/LegalManager.Infrastructure/Persistence/Migrations/<timestamp>_AddTenantThemeFields.cs`
- `wwwroot/js/theme.js`
- `wwwroot/js/tema.js`
- `wwwroot/pages/tema.html`
- `tests/frontend/tests/smoke/admin.spec.ts` (atualizar para incluir `tema.html`)

### Modificar

- `src/LegalManager.Domain/Entities/Tenant.cs` (5 campos novos)
- `src/LegalManager.Application/DTOs/Auth/LoginDto.cs` (`UsuarioInfoDto` ganha `Tema`)
- `src/LegalManager.Infrastructure/Services/AuthService.cs:298` (passa tema)
- `src/LegalManager.Infrastructure/Services/PortalClienteService.cs:52` (passa tema no `ClientePerfilDto`)
- `src/LegalManager.Application/DTOs/PortalCliente/PortalClienteDto.cs` (`ClientePerfilDto` ganha `TenantThemeDto`)
- `src/LegalManager.API/wwwroot/js/api.js` (cacheia tema em sessionStorage)
- `src/LegalManager.API/wwwroot/js/layout.js` (aplica tema + entrada sidebar)
- `src/LegalManager.API/wwwroot/cliente/js/clienteLayout.js` (aplica tema)
- `src/LegalManager.API/Program.cs` (registra `ITenantsThemeService`)