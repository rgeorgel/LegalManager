// Alternância entre o dashboard novo (/pages/dashboard.html, padrão) e o
// clássico (/pages/dashboard-classico.html). A escolha é salva no banco
// (PUT /api/dashboard/versao) e volta no login em usuario.dashboardVersao, que o
// <head> de dashboard.html lê do sessionStorage para redirecionar ao clássico antes
// de renderizar — assim a preferência vale em qualquer dispositivo. O localStorage
// fica só como reserva (ex.: sessão aberta antes desta mudança).
import { apiFetch } from './api.js';
import { trackEvent } from './analytics.js';

const KEY = 'dashboard_versao';

const VERSOES = {
  novo: { label: '✨ Novo', href: '/pages/dashboard.html' },
  classico: { label: 'Clássico', href: '/pages/dashboard-classico.html' },
};

export async function setDashboardVersao(versao) {
  try { localStorage.setItem(KEY, versao); } catch { /* storage bloqueado */ }
  try {
    const user = JSON.parse(sessionStorage.getItem('user') || 'null');
    if (user) sessionStorage.setItem('user', JSON.stringify({ ...user, dashboardVersao: versao }));
  } catch { /* sessão ilegível: o servidor ainda recebe a escolha */ }
  // Não bloqueia a troca de página se a API falhar: a escolha local já vale nesta sessão.
  await apiFetch('/dashboard/versao', { method: 'PUT', body: { versao } }).catch(() => {});
}

/**
 * Renderiza o seletor "Novo | Clássico" dentro de `container`.
 * @param {HTMLElement} container
 * @param {'novo'|'classico'} atual versão da página que está chamando
 */
export function injectDashboardSwitch(container, atual) {
  if (!container) return;
  injectStyles();
  container.innerHTML = `<div class="dash-versao" role="group" aria-label="Versão do dashboard">
    ${Object.entries(VERSOES).map(([id, v]) => `<button type="button" data-versao="${id}"
      class="${id === atual ? 'active' : ''}" aria-pressed="${id === atual}">${v.label}</button>`).join('')}
  </div>`;
  // Escolha feita antes de a preferência ir para o banco (só no localStorage): sincroniza uma vez.
  try {
    const user = JSON.parse(sessionStorage.getItem('user') || 'null');
    if (user && !user.dashboardVersao && localStorage.getItem(KEY) === atual) setDashboardVersao(atual);
  } catch { /* sem sessão legível: nada a sincronizar */ }
  container.querySelector('.dash-versao').addEventListener('click', e => {
    const btn = e.target.closest('[data-versao]');
    if (!btn || btn.dataset.versao === atual) return;
    const para = btn.dataset.versao;
    btn.disabled = true;
    trackEvent('dashboard_versao_alterada', { de: atual, para });
    setDashboardVersao(para).finally(() => { window.location.href = VERSOES[para].href; });
  });
}

function injectStyles() {
  if (document.getElementById('dashVersaoStyles')) return;
  const s = document.createElement('style');
  s.id = 'dashVersaoStyles';
  // !important nas cores: o preset escuro força `button { color: inherit !important }`.
  s.textContent = `
    .dash-versao { display: inline-flex; gap: 2px; padding: 3px; border-radius: 10px;
      background: color-mix(in srgb, var(--color-text) 6%, var(--color-surface)); border: 1px solid var(--color-border); }
    .dash-versao button { border: none; background: transparent; cursor: pointer; font-size: 12px; font-weight: 600;
      padding: 6px 12px; border-radius: 8px; color: var(--color-text-muted) !important; white-space: nowrap; }
    .dash-versao button:hover { color: var(--color-text) !important; }
    .dash-versao button.active { background: var(--color-surface); color: var(--color-text) !important;
      box-shadow: 0 1px 3px rgba(0,0,0,.2); cursor: default; }
  `;
  document.head.appendChild(s);
}
