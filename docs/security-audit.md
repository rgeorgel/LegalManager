# Security Audit — Causify/LegalManager

Scan date: 2026-05-25  
Scope: backend (ASP.NET Core 10), frontend (wwwroot JS), infrastructure/config

Checkbox legend:
- `[ ]` open — needs to be fixed
- `[x]` done or N/A — resolved, false positive, or a deliberate design choice (reason noted inline)

---

## Critical

- [x] **C1 — Hangfire dashboard open to everyone**  
  `src/LegalManager.Infrastructure/Jobs/HangfireAuthorizationFilter.cs`  
  `Authorize()` always returns `true`. Any network request to `/hangfire` can view job history, retry/delete jobs, and read execution logs that may contain tenant data.  
  **Fix:** check `context.GetHttpContext().User.IsInRole("SuperAdmin")` and `IsAuthenticated`.

- [x] **C2 — JWT secret placeholder committed** *(false positive)*  
  `src/LegalManager.API/appsettings.json:13` — placeholder value is overridden by env var in production.

- [x] **C3 — Dev DB credentials in appsettings.json** *(false positive)*  
  `postgres:postgres` is the standard local dev default. Production overrides this via env vars / secrets manager. Not a real exposure.

- [x] **C4 — SuperAdmin password hardcoded as fallback** *(false positive)*  
  `src/LegalManager.API/Program.cs:314` — fallback value is overridden by env var in production.

---

## High

- [x] **H1 — No rate limiting on auth endpoints**  
  `src/LegalManager.API/Controllers/AuthController.cs`  
  `/login`, `/register`, `/forgot-password`, `/reset-password` accept unlimited requests — brute force and credential stuffing.  
  **Fix:** `AddRateLimiter` (fixed-window) + `[EnableRateLimiting("auth")]` on those four endpoints; return `429` on breach.

- [x] **H2 — API keys in appsettings.json** *(false positive)*  
  All secret fields in the committed file are empty strings (`"ApiToken": ""`, `"ApiKey": ""`, etc.). Actual keys are injected at runtime via environment variables. This is the correct pattern.

- [x] **H3 — Plaintext portal password sent in email** *(not necessary)*  
  `src/LegalManager.Infrastructure/Services/EmailService.cs:175` — `<strong>Senha:</strong> {senha}` is embedded in the welcome email.  
  Email is transmitted unencrypted and stored in the client's inbox permanently.  
  **Fix:** send a single-use setup link with a short-lived signed token; client sets their own password on first visit.

- [x] **H4 — Webhook secret exposed in query string** *(not necessary)*  
  `src/LegalManager.API/Controllers/AssinaturaController.cs:224` — `[FromQuery] string? secret`.  
  Query strings are recorded by every proxy, load balancer, and WAF log. Additionally, if `AbacatePay:WebhookSecret` is empty the check is skipped and **any caller is accepted**.  
  **Fix:** accept secret in `Authorization` or `X-Webhook-Secret` header only; fail-closed (reject if header is absent).

- [ ] **H5 — File upload accepts any MIME type**  
  `src/LegalManager.API/Controllers/DocumentosController.cs:52-81`  
  No content-type or file-extension whitelist. Executables, scripts, and HTML files can be uploaded.  
  **Fix:** whitelist allowed types (PDF, DOCX, XLSX, PNG, JPG, JPEG); validate magic bytes, not only the `Content-Type` header.

- [x] **H6 — Missing security response headers**  
  `src/LegalManager.API/Program.cs` — no header middleware configured.  
  Missing: `Strict-Transport-Security`, `X-Frame-Options: DENY`, `X-Content-Type-Options: nosniff`, `Content-Security-Policy`, `Referrer-Policy`.  
  **Fix:** add a `app.Use()` middleware near the top of the pipeline that sets all five headers; call `app.UseHsts()` outside `IsDevelopment()`.

- [x] **H7 — OCI storage shells out to curl/bash (injection risk)** *(false positive)*  
  `src/LegalManager.Infrastructure/Storage/OciStorageService.cs`  
  The `objectKey` is built entirely from server-generated values: tenant `Guid`, entity `Guid`, and `safeFileName` which is sanitized with `Regex.Replace(fileName, @"[^A-Za-z0-9._\-]", "_")`. No user-controlled special characters can survive into the shell command. Not exploitable with current code.

---

## Medium

- [ ] **M1 — XSS: innerHTML with unescaped server data**  
  `wwwroot/pages/processo-detalhe.html:971` — `${pt.nomeContato}` (no `esc()`)  
  `wwwroot/pages/processo-detalhe.html:1004` — `${a.descricao}` (no `esc()`)  
  `wwwroot/pages/processo-detalhe.html:1013,1069,1091,1143` — `${err.message}` inserted via `innerHTML`  
  A server-side value containing `<script>` or event handler markup would execute in the browser.  
  **Fix:** apply `esc()` to all interpolated server values; use `el.textContent = err.message` instead of `innerHTML` for error strings.

- [ ] **M2 — Open redirect via notification URL**  
  `wwwroot/js/layout.js:330-336` — `window.location.href = item.dataset.url` with no scheme validation.  
  A `javascript:alert(1)` or `//evil.com` value from the server would redirect users to attacker-controlled destinations.  
  **Fix:** accept only relative paths (`/`) or `https://causify.com.br`; drop anything else.

- [ ] **M3 — Unsafe inline onclick handlers**  
  `wwwroot/pages/processo-detalhe.html:974,1010` — `onclick="removerParte('${pt.contatoId}')"` etc.  
  A quote character in a DB-stored ID value would break the JS context.  
  **Fix:** store the ID in a `data-id` attribute and attach the handler via `addEventListener`.

- [ ] **M4 — AllowedHosts wildcard**  
  `src/LegalManager.API/appsettings.json:8` — `"AllowedHosts": "*"`  
  Enables Host header injection (forged password-reset links, cache poisoning).  
  **Fix:** set to the production domain(s) e.g. `"app.causify.com.br;causify.com.br"` in the production config.

- [x] **M5 — CORS AllowAnyHeader + AllowAnyMethod** *(false positive)*  
  The policy is already scoped to a single `WithOrigins(...)` value. `AllowAnyHeader` + `AllowAnyMethod` is the standard pattern for single-origin SPA APIs. No action needed.

- [ ] **M6 — Weak password policy**  
  `src/LegalManager.API/Program.cs:44-45` — `RequireNonAlphanumeric = false`, minimum length 8.  
  **Fix:** enable `RequireNonAlphanumeric = true`; raise `RequiredLength` to 12.

- [x] **M7 — API keys in DefaultRequestHeaders (logging risk)** *(false positive)*  
  `UseSerilogRequestLogging()` logs incoming requests only, not outgoing HttpClient calls. The API keys set on HttpClient `DefaultRequestHeaders` are never written to Serilog output.

- [x] **M8 — Portal JWT expiry 24 hours** *(by design)*  
  Legal portal clients log in infrequently; a 24-hour session window is a deliberate UX tradeoff. Not a significant attack surface given the portal's read-only nature.

- [x] **M9 — No Content-Security-Policy header**  
  Covered by H6. Without CSP, any XSS can load external scripts from arbitrary origins.  
  **Fix:** set as part of the H6 header middleware: `Content-Security-Policy: default-src 'self'; script-src 'self' https://www.googletagmanager.com https://client.crisp.chat`.

- [x] **M10 — Domain layer package version mismatch**  
  `src/LegalManager.Domain/LegalManager.Domain.csproj:10` — `Microsoft.AspNetCore.Identity 2.3.1` alongside `net10.0` target.  
  **Fix:** upgrade to `Microsoft.AspNetCore.Identity 10.0.0`.

---

## Low

- [x] **L1 — JWT stored in sessionStorage** *(acceptable)*  
  `sessionStorage` is safer than `localStorage` against persistent XSS. Switching to HttpOnly cookies would require significant backend refactoring. Accept this tradeoff while keeping XSS surface minimal (see M1–M3).

- [ ] **L2 — No explicit `[AllowAnonymous]` on portal login endpoint**  
  `src/LegalManager.API/Controllers/PortalClienteController.cs:30` — implicit public access is correct but undocumented in code.  
  **Fix:** add `[AllowAnonymous]` for code clarity and auditing.

- [ ] **L3 — DebugController not gated by environment**  
  Exposes raw ESAJ HTML responses. Should be removed or wrapped in `if (app.Environment.IsDevelopment())`.

- [ ] **L4 — `console.error` leaks API error details**  
  `wwwroot/js/tarefas.js:41`, `wwwroot/js/publicacoes.js:32` — error objects may contain API paths or internal messages visible in browser DevTools.  
  **Fix:** log to a dev-only channel or strip sensitive fields before logging.

- [x] **L5 — HTTP frontend URL in config** *(false positive)*  
  `appsettings.json:23` — `http://localhost:5000` is the local dev address. Production overrides this with an HTTPS URL via env var.
