# Módulo 11 — Refinamento Mobile-Responsive

## Visão Geral

Este módulo implementa o refinamento da experiência mobile do LegalManager, garantindo que todas as telas sejam funcionais e visualmente corretas em viewports de dispositivos móveis (375px, 390px, 414px) e tablets (768px, 769px–1024px).

| Módulo | Descrição | Status |
|--------|-----------|--------|
| Módulo 11 | Refinamento mobile-responsive (HTML/CSS/JS) | ✅ Completo |

---

## Arquitetura Responsive

### Abordagem

- **Mobile-first**: CSS desenvolvido primeiro para mobile, expandindo para desktop com media queries
- **Vanilla CSS**: Sem frameworks, usando CSS Custom Properties (variáveis) para consistência
- **Breakpoints definidos**:
  - `375px` — Small phones (iPhone SE, older Android)
  - `390px` — Standard phones (iPhone 14, Pixel 7)
  - `414px` — Large phones (iPhone 14 Pro Max, Pixel 7 Pro)
  - `768px` — Tablets / Mobile Desktop boundary
  - `769px–1024px` — Tablet landscape
  - `>1024px` — Desktop

### Estrutura de Arquivos

| Arquivo | Descrição |
|---------|-----------|
| `wwwroot/css/styles.css` | CSS principal com todas as regras responsive |
| `wwwroot/js/layout.js` | JavaScript de navegação, bottom nav e menu mobile |
| `wwwroot/pages/*.html` | Páginas HTML que usam as classes CSS |

---

## Componentes Implementados

### 1. Bottom Navigation Bar

**Arquivo**: `styles.css` (`.bottom-nav`), `layout.js` (`injectBottomNav()`)

Barra de navegação fixa na parte inferior da tela, visível apenas em dispositivos móveis (< 768px).

**Itens fixos**:
| Ícone | Label | Ação |
|-------|-------|------|
| 📊 | Dashboard | `/pages/dashboard.html` |
| ⚖️ | Processos | `/pages/processos.html` |
| ✅ | Tarefas | `/pages/tarefas.html` |
| 📅 | Agenda | `/pages/agenda.html` |
| ☰ | Menu | Abre sidebar |

**Características**:
- Altura: 60px + `env(safe-area-inset-bottom)` para iPhone
- Ícones grandes (20px) para fácil toque
- Estado ativo com cor primária (`#1a56db`)
- Estado pressed com feedback visual (`:active`)
- Sombra superior sutil para separação visual

**CSS**:
```css
.bottom-nav {
  position: fixed;
  bottom: 0;
  left: 0;
  right: 0;
  height: var(--bottom-nav-height);
  background: var(--color-surface);
  border-top: 1px solid var(--color-border);
  z-index: 100;
  padding-bottom: env(safe-area-inset-bottom);
}
.bottom-nav-item {
  flex: 1;
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  min-height: 44px;
}
```

### 2. Sidebar com Overlay

**Arquivo**: `styles.css` (`.sidebar-overlay`), `layout.js` (`setupMobileMenu()`)

Sidebar deslizante com backdrop semi-transparente.

**Comportamento**:
1. Usuário clica no hamburger (☰) no header
2. Overlay aparece atrás do sidebar (fade in 250ms)
3. Sidebar desliza da esquerda para a posição visível
4. Toque no overlay fecha o sidebar
5. Toque em qualquer link do sidebar fecha o sidebar

**CSS**:
```css
.sidebar-overlay {
  display: none;
  position: fixed;
  inset: 0;
  top: var(--header-height);
  background: rgba(0,0,0,0.5);
  z-index: 199;
  opacity: 0;
  transition: opacity 0.25s ease;
}
.sidebar-overlay.visible {
  display: block;
  opacity: 1;
}
.sidebar {
  position: fixed;
  left: -100%;
  transition: left 0.25s ease;
}
.sidebar.open { left: 0; }
```

**JavaScript**:
```javascript
function setupMobileMenu() {
  hamburger?.addEventListener('click', () => toggleMobileMenu());
  overlay?.addEventListener('click', closeMobileMenu);
}
```

### 3. Touch Optimizations

**Arquivo**: `styles.css`

Todos os elementos interativos têm touch target mínimo de 44x44px conforme guideline Apple HIG.

**Implementações**:
| Elemento | Min Height | Min Width |
|---------|-----------|-----------|
| Buttons (`.btn`) | 44px | 44px |
| Form inputs (`.form-control`) | 44px | — |
| Hamburger button | 44px | 44px |
| Modal close | 44px | 44px |
| Pagination buttons | 44px | 44px |
| Nav links (`.sidebar-nav li a`) | 44px | — |
| Bottom nav items | 44px | — |

**Active states**:
```css
.btn:active { transform: scale(0.98); }
.sidebar-nav li a:active { background: rgba(255,255,255,0.15); }
.hamburger:active { background: var(--color-border); }
.bottom-nav-item:active { background: var(--color-bg); }
```

### 4. Responsive Tables → Cards

**Arquivo**: `styles.css` (linha ~325)

Em mobile, tables são transformadas em cards visualmente.

**Transformação**:
1. `<thead>` é ocultado
2. Cada `<tr>` vira um card com border e border-radius
3. Cada `<td>` é display:flex com label à esquerda e valor à direita
4. Labels são extraídos do atributo `data-label`

**Exemplo HTML**:
```html
<td data-label="CNJ">1234567-89.2024.1.00.0000</td>
```

**Resultado em mobile**:
```
CNJ         1234567-89.2024.1.00.0000
Tribunal    TJSP
Status      Ativo
```

### 5. Full-Screen Modals

**Arquivo**: `styles.css`

Em mobile, modais ocupam 100% da tela.

```css
@media (max-width: 768px) {
  .modal {
    position: fixed;
    inset: 0;
    max-width: none;
    max-height: none;
    border-radius: 0;
    padding-top: env(safe-area-inset-top);
    padding-bottom: env(safe-area-inset-bottom);
  }
}
```

---

## Breakpoints Detalhados

### 414px — Large Phones

```css
@media (max-width: 414px) {
  .header-logo { font-size: 16px; }
  .header-user span:not(.header-user-name) { display: none; }
  .page-title { font-size: 20px; }
  .main-content { padding: 14px; }
}
```

### 390px — Standard Phones

```css
@media (max-width: 390px) {
  .header { padding: 0 12px; }
  .header-user { gap: 4px; font-size: 12px; }
  .main-content { padding: 12px; }
  .page-header { margin-bottom: 16px; }
  .card-body { padding: 14px; }
}
```

### 375px — Small Phones

```css
@media (max-width: 375px) {
  .page-title { font-size: 18px; }
  .main-content { padding: 10px; }
  .filter-bar { gap: 8px; }
  .bottom-nav-item span:first-child { font-size: 18px; }
  .bottom-nav-item { font-size: 9px; }
}
```

### 768px — Mobile/Desktop Boundary

```css
@media (max-width: 768px) {
  .hamburger { display: flex; }
  .app-layout { padding-bottom: var(--bottom-nav-height); }
  .sidebar { /* fixed positioning */ }
  .bottom-nav { display: block; }
  /* table transformation, modal full-screen, etc. */
}
```

### 769px–1024px — Tablet

```css
@media (min-width: 769px) and (max-width: 1024px) {
  :root { --sidebar-width: 200px; }
  .main-content { padding: 20px; }
}
```

---

## Layout Grid

O layout usa CSS Grid para o `.app-layout`:

```css
.app-layout {
  display: grid;
  grid-template-columns: var(--sidebar-width) 1fr;
  grid-template-rows: var(--header-height) 1fr;
  min-height: 100vh;
}
```

Em mobile, transforma para:

```css
.app-layout {
  grid-template-columns: 1fr;
  grid-template-rows: var(--header-height) 1fr;
  padding-bottom: var(--bottom-nav-height); /* espaço para bottom nav */
}
```

---

## CSS Custom Properties

Variáveis CSS usadas no sistema responsive:

```css
:root {
  --sidebar-width: 240px;        /* Desktop sidebar width */
  --header-height: 60px;          /* Header height */
  --bottom-nav-height: 60px;      /* Bottom nav height (mobile) */
  --radius: 8px;                 /* Border radius */
  /* Colors... */
}

@media (min-width: 769px) and (max-width: 1024px) {
  :root { --sidebar-width: 200px; }  /* Tablet sidebar */
}
```

---

## JavaScript Layout System

### `initLayout()` — Entry Point

```javascript
export function initLayout() {
  if (!isLoggedIn()) {
    window.location.href = '/login.html';
    return;
  }
  injectSidebarNav();
  injectBottomNav();
  setupMobileMenu();
  injectNotificationBell();
}
```

### `injectSidebarNav()` — Dynamic Navigation

- Gera sidebar items do array `NAV_ITEMS`
- Marca item ativo baseado em `window.location.pathname`
- Adiciona badge PRO para itens restritos
- Adiciona event listeners para links bloqueados e fechamento do menu

### `injectBottomNav()` — Mobile Navigation Bar

- Cria e injeta `.bottom-nav` no DOM
- Marca item ativo baseado na URL atual
- "Menu" item chama `toggleMobileMenu()` em vez de navegação
- Remove nav duplicada se já existir (`id="bottomNav"`)

### `setupMobileMenu()` — Hamburger + Overlay Logic

- Vincula hamburger button ao toggle
- Cria overlay backdrop dinamicamente
- Click no overlay chama `closeMobileMenu()`
- Expõe `window.closeMobileMenu()` para uso externo

---

## Safe Areas (iOS)

O sistema respeita safe areas de dispositivos iOS:

```css
.modal {
  padding-top: env(safe-area-inset-top);
  padding-bottom: env(safe-area-inset-bottom);
}

.bottom-nav {
  padding-bottom: env(safe-area-inset-bottom);
}

.modal-overlay {
  padding: env(safe-area-inset-bottom);
}
```

---

## Validação de Viewports

| Viewport | Dispositivo | Status |
|----------|-------------|--------|
| 375px | iPhone SE, small Android | ✅ |
| 390px | iPhone 14, Pixel 7 | ✅ |
| 414px | iPhone 14 Pro Max, Pixel 7 Pro | ✅ |
| 768px | iPad Mini, tablets | ✅ |
| 1024px | iPad landscape | ✅ |
| >1024px | Desktop | ✅ |

---

## Referências de Código

| Arquivo | Descrição |
|---------|-----------|
| `src/.../wwwroot/css/styles.css` | CSS principal com regras responsive |
| `src/.../wwwroot/js/layout.js` | JavaScript de layout e navegação |
| `src/.../wwwroot/pages/dashboard.html` | Exemplo de página com CSS inline para cards |
| `src/.../wwwroot/pages/processos.html` | Lista de processos (transformação table→card) |
| `src/.../wwwroot/pages/kanban.html` | Kanban board com scroll horizontal |

---

## Notas de Implementação

1. **CSS Grid vs Flexbox**: Layout principal usa Grid, componentes usam Flexbox
2. **Backdrop não é injetado no CSS**: É criado/destruído via JavaScript para evitar pollution
3. **Bottom nav sempre recriada**: Garante sincronização com URL atual após navegação
4. **Toast reposicionado**: `showUpgradeToast()` usa `bottom: 80px` para evitar sobreposição com bottom nav
5. **Form font-size 16px**: Previne zoom automático em iOS ao focar em inputs