import { darkCss, paletaModoEscuro } from './tema-escuro.js';

const THEME_STORAGE_KEY = 'tenant_theme';
const DEFAULT_LOGO = '/images/causify-logo-navbar-clara.png';
const DARK_LOGO = '/images/causify-logo-transparente.png';

function isDarkThemeActive() {
  // Lê a cor de fundo efetiva (incluindo overrides de presets Escuro/Dracula)
  // e considera "dark" se a luminância for menor que 50%.
  const bg = getComputedStyle(document.body).backgroundColor;
  const m = bg.match(/rgba?\((\d+),\s*(\d+),\s*(\d+)/);
  if (!m) return false;
  const [, r, g, b] = m;
  const luma = (parseInt(r) * 0.299 + parseInt(g) * 0.587 + parseInt(b) * 0.114) / 255;
  return luma < 0.5;
}

function hexToRgb(hex) {
  if (!hex) return null;
  const m = hex.trim().match(/^#([0-9a-f]{3}|[0-9a-f]{6})$/i);
  if (!m) return null;
  let h = m[1];
  if (h.length === 3) h = h.split('').map(c => c + c).join('');
  return {
    r: parseInt(h.slice(0, 2), 16),
    g: parseInt(h.slice(2, 4), 16),
    b: parseInt(h.slice(4, 6), 16),
  };
}

function rgbToHex(r, g, b) {
  const to = (n) => Math.max(0, Math.min(255, Math.round(n))).toString(16).padStart(2, '0');
  return `#${to(r)}${to(g)}${to(b)}`;
}

function mix(hex, withHex, weight) {
  const a = hexToRgb(hex);
  const b = hexToRgb(withHex);
  if (!a || !b) return hex;
  return rgbToHex(
    a.r * (1 - weight) + b.r * weight,
    a.g * (1 - weight) + b.g * weight,
    a.b * (1 - weight) + b.b * weight,
  );
}

function luminancia({ r, g, b }) {
  const lin = (c) => { c /= 255; return c <= 0.03928 ? c / 12.92 : ((c + 0.055) / 1.055) ** 2.4; };
  return 0.2126 * lin(r) + 0.7152 * lin(g) + 0.0722 * lin(b);
}

// Texto sobre fundo var(--color-primary): branco, a não ser que a primária seja tão clara que o
// branco fique ilegível (contraste < 2:1 — lima, ciano, verde neon, amarelo). Calculado aqui, e não
// só no customCss, porque o customCss é uma cópia salva no escritório e não muda com o preset.
function textoSobrePrimaria(primary) {
  const rgb = hexToRgb(primary);
  if (!rgb) return '#fff';
  const contrasteBranco = 1.05 / (luminancia(rgb) + 0.05);
  return contrasteBranco < 2 ? '#141414' : '#fff';
}

function contraste(hexA, hexB) {
  const a = luminancia(hexToRgb(hexA));
  const b = luminancia(hexToRgb(hexB));
  return (Math.max(a, b) + 0.05) / (Math.min(a, b) + 0.05);
}

// Clareia (fundo escuro) ou escurece (fundo claro) a cor aos poucos, mantendo o matiz, até ela
// ter contraste de texto (4.5:1) com o fundo — a primária do escritório vira link e botão no
// modo oposto ao do tema dele.
function ajustarContraste(hex, fundo, alvo = 4.5) {
  const destino = luminancia(hexToRgb(fundo)) < 0.5 ? '#ffffff' : '#000000';
  for (let w = 0; w <= 1; w += 0.05) {
    const cor = mix(hex, destino, w);
    if (contraste(cor, fundo) >= alvo) return cor;
  }
  return destino;
}

const FUNDO_ESCURO = '#0f172a';
const SUPERFICIE_ESCURA = '#1e293b';
const PRIMARIA_PADRAO = '#1a56db';

// Modo escuro do usuário sobre um tema claro do escritório: mantém logo, layout, destaque e o
// matiz da primária, mas troca o CSS do tema pela base escura (o mesmo visual do preset Escuro).
function versaoEscura(theme) {
  const primaria = ajustarContraste(sanitizeHex(theme.primaryColor) || PRIMARIA_PADRAO, FUNDO_ESCURO);
  const sidebar = sanitizeHex(theme.sidebarColor);
  return {
    ...theme,
    primaryColor: primaria,
    sidebarColor: sidebar && luminancia(hexToRgb(sidebar)) < 0.2 ? sidebar : FUNDO_ESCURO,
    customCss: darkCss(paletaModoEscuro({
      primaryLight: mix(primaria, SUPERFICIE_ESCURA, 0.8),
      onPrimary: textoSobrePrimaria(primaria),
    })),
  };
}

// Modo claro do usuário sobre um tema escuro do escritório: volta ao visual claro padrão
// (sem o CSS do tema), com a primária escurecida o bastante para ler sobre branco.
function versaoClara(theme) {
  return {
    ...theme,
    primaryColor: ajustarContraste(sanitizeHex(theme.primaryColor) || PRIMARIA_PADRAO, '#ffffff'),
    customCss: '',
  };
}

function shadeForPrimary(primary) {
  return {
    dark: mix(primary, '#000000', 0.18),
    light: mix(primary, '#ffffff', 0.86),
  };
}

export function getStoredTheme() {
  try {
    let raw = sessionStorage.getItem(THEME_STORAGE_KEY);
    if (!raw) raw = sessionStorage.getItem('cliente_tenant_theme');
    if (!raw) return null;
    return JSON.parse(raw);
  } catch {
    return null;
  }
}

export function setStoredTheme(theme) {
  if (!theme) {
    sessionStorage.removeItem(THEME_STORAGE_KEY);
    return;
  }
  sessionStorage.setItem(THEME_STORAGE_KEY, JSON.stringify(theme));
}

export function sanitizeHex(value) {
  if (typeof value !== 'string') return null;
  return /^#([0-9a-f]{3}|[0-9a-f]{6})$/i.test(value.trim()) ? value.trim() : null;
}

/** O tema aplicado agora é escuro (fundo da página com luminância < 50%)? */
export function temaEscuroAtivo() {
  return isDarkThemeActive();
}

/**
 * Aplica o tema do escritório. `modo` ("claro" | "escuro", do botão do header — ver
 * js/modo-cor.js) é a preferência do usuário: se o tema do escritório for do outro modo, ele é
 * convertido (versaoEscura / versaoClara) só nesta tela; o tema salvo no tenant não muda.
 * Sem `modo` (editor de tema, portal do cliente), aplica o tema como está.
 */
export function applyTenantTheme(theme, { modo } = {}) {
  if (!theme && !modo) return;
  const base = theme || {};
  aplicarTema(base);
  if (!modo) return;
  const escuro = isDarkThemeActive();
  if (modo === 'escuro' && !escuro) aplicarTema(versaoEscura(base));
  else if (modo === 'claro' && escuro) aplicarTema(versaoClara(base));
}

function aplicarTema(theme) {
  const r = document.documentElement.style;
  // Reaplicar (o botão de modo troca o tema sem recarregar) não pode herdar cores da vez anterior.
  VARS_TEMA.forEach(v => r.removeProperty(v));

  const primary = sanitizeHex(theme.primaryColor);
  const sidebar = sanitizeHex(theme.sidebarColor);
  const accent = sanitizeHex(theme.accentColor);

  if (primary) {
    const shades = shadeForPrimary(primary);
    r.setProperty('--color-primary', primary);
    r.setProperty('--color-primary-dark', shades.dark);
    r.setProperty('--color-primary-light', shades.light);
    r.setProperty('--color-on-primary', textoSobrePrimaria(primary));
  }

  if (sidebar) {
    r.setProperty('--color-sidebar', sidebar);
    r.setProperty('--color-nav', sidebar);
    const sidebarText = sidebar.toLowerCase() === '#ffffff' ? '#111827' : '#d1d9e6';
    r.setProperty('--color-sidebar-text', sidebarText);
    r.setProperty('--color-sidebar-active', primary || '#1a56db');
  }

  if (accent) {
    r.setProperty('--color-accent', accent);
  }

  if (theme.layoutMode === 'compact') {
    document.body.classList.add('theme-compact');
  } else {
    document.body.classList.remove('theme-compact');
  }

  // customCss precisa vir ANTES do isDarkThemeActive(), porque ele é
  // quem sobrescreve --color-bg no :root e define o "escuro" ou "claro"
  // a partir do qual isDarkThemeActive() decide qual logo padrão usar.
  if (typeof theme.customCss === 'string' && theme.customCss.trim()) {
    let style = document.getElementById('tenant-custom-css');
    if (!style) {
      style = document.createElement('style');
      style.id = 'tenant-custom-css';
      document.head.appendChild(style);
    }
    style.textContent = theme.customCss;
  } else {
    const style = document.getElementById('tenant-custom-css');
    if (style) style.textContent = '';
  }

  // Após aplicar customCss o --color-bg já reflete o tema; agora sim
  // podemos escolher o logo padrão correto.
  const userLogo2 = theme.logoUrl || null;
  const defaultLogo2 = isDarkThemeActive() ? DARK_LOGO : DEFAULT_LOGO;
  const finalLogo = userLogo2 || defaultLogo2;
  document.querySelectorAll('.header-logo img').forEach(img => {
    img.src = finalLogo;
    img.alt = userLogo2 ? 'Logo' : 'Causify';
  });
  document.querySelectorAll('.auth-logo img').forEach(img => {
    img.src = finalLogo;
    img.alt = userLogo2 ? 'Logo' : 'Causify';
  });
}

const VARS_TEMA = ['--color-primary', '--color-primary-dark', '--color-primary-light', '--color-on-primary',
  '--color-sidebar', '--color-nav', '--color-sidebar-text', '--color-sidebar-active', '--color-accent'];

export function resetTenantTheme() {
  const r = document.documentElement.style;
  VARS_TEMA.forEach(v => r.removeProperty(v));
  // Restaura a logo padrão apropriada para o tema que ficou aplicado.
  // Depois de limpar customCss o bg volta ao default (claro), então
  // isDarkThemeActive() resolve para false e usamos DEFAULT_LOGO.
  const style = document.getElementById('tenant-custom-css');
  if (style) style.textContent = '';
  const logo = isDarkThemeActive() ? DARK_LOGO : DEFAULT_LOGO;
  document.querySelectorAll('.header-logo img').forEach(img => { img.src = logo; });
  document.body.classList.remove('theme-compact');
}

export async function fetchTenantTheme() {
  try {
    const token = sessionStorage.getItem('access_token')
      || sessionStorage.getItem('sa_access_token');
    if (!token) return null;
    const res = await fetch('/api/tenants/current/theme', {
      headers: { 'Authorization': `Bearer ${token}` }
    });
    if (!res.ok) return null;
    return await res.json();
  } catch {
    return null;
  }
}

export const TENANT_THEME_LOGO_FALLBACK = DEFAULT_LOGO;