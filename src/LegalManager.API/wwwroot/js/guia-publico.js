// Páginas públicas da Central Causify (/guias, HTML gerado em GuiasHtml.cs). Sem login tudo
// funciona como página estática; com a sessão do portal (sessionStorage desta aba) o header
// mostra "Abrir o Causify", o botão do guia leva direto à tela e o "Útil" passa a curtir.
// Nada aqui redireciona sozinho: o login só é pedido quando a pessoa clica em "Útil".
import { isLoggedIn, setSession } from './api.js';

const script = document.currentScript || document.querySelector('script[src$="/js/guia-publico.js"]');
const slug = script?.dataset.slug;
const $ = id => document.getElementById(id);

function token() {
  try { return sessionStorage.getItem('access_token'); } catch { return null; }
}

async function renovarSessao() {
  let rt = null;
  try { rt = sessionStorage.getItem('refresh_token'); } catch { /* sem storage */ }
  if (!rt) return false;
  try {
    const res = await fetch('/api/auth/refresh', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ refreshToken: rt }),
    });
    if (!res.ok) return false;
    setSession(await res.json());
    return true;
  } catch {
    return false;
  }
}

// fetch com o token da aba; renova uma vez no 401. Devolve null se não houver sessão válida.
async function chamar(path, method = 'GET') {
  if (!token()) return null;
  const enviar = () => fetch(path, { method, headers: { Authorization: `Bearer ${token()}` } });
  let res = await enviar();
  if (res.status === 401 && await renovarSessao()) res = await enviar();
  if (res.status === 401 || res.status === 403) return null;
  if (!res.ok) throw new Error(`HTTP ${res.status}`);
  return res.json();
}

function irParaLogin() {
  const volta = encodeURIComponent(location.pathname + location.search);
  location.href = `/login.html?redirect=${volta}`;
}

// ── Header e CTA ────────────────────────────────────────────────────────────

// Menu do celular (hambúrguer), como na home: abre/fecha e fecha ao escolher um link ou com Esc.
const btnMenu = $('btnMenu');
const menuCelular = $('menuCelular');
function abrirMenu(abrir) {
  menuCelular.classList.toggle('open', abrir);
  btnMenu.classList.toggle('active', abrir);
  btnMenu.setAttribute('aria-expanded', String(abrir));
  btnMenu.setAttribute('aria-label', abrir ? 'Fechar menu' : 'Abrir menu');
}
if (btnMenu && menuCelular) {
  btnMenu.addEventListener('click', () => abrirMenu(!menuCelular.classList.contains('open')));
  menuCelular.addEventListener('click', e => { if (e.target.closest('a')) abrirMenu(false); });
  document.addEventListener('keydown', e => {
    if (e.key === 'Escape' && menuCelular.classList.contains('open')) { abrirMenu(false); btnMenu.focus(); }
  });
}

const logado = isLoggedIn();
if (logado) {
  const abrirCausify = '<a href="/pages/dashboard.html" class="btn btn-primary">Abrir o Causify</a>';
  const cta = $('navCta');
  if (cta) cta.innerHTML = abrirCausify;
  const menuCta = $('menuCta');
  if (menuCta) menuCta.innerHTML = abrirCausify;

  const ctaGuia = $('ctaGuia');
  if (ctaGuia?.dataset.logado) ctaGuia.href = ctaGuia.dataset.logado;
  const ctaSub = $('ctaSub');
  if (ctaSub) ctaSub.textContent = '';
}

// ── "Útil" ──────────────────────────────────────────────────────────────────

const btnUtil = $('btnUtil');
const contador = $('contadorUtil');
const dica = $('dicaUtil');

function mostrarEstado({ curtiu, total }) {
  btnUtil.setAttribute('aria-pressed', String(curtiu));
  contador.textContent = total > 0 ? String(total) : '';
  dica.textContent = curtiu ? 'Obrigado! Isso nos ajuda a escrever mais guias assim.' : 'Este guia ajudou? Marque como útil.';
}

if (btnUtil) {
  const id = btnUtil.dataset.id;

  if (logado) {
    dica.textContent = 'Este guia ajudou? Marque como útil.';
    chamar(`/api/guias/${id}/curtida`).then(e => { if (e) mostrarEstado(e); }).catch(() => {});
  }

  btnUtil.addEventListener('click', async () => {
    if (!token()) return irParaLogin();
    const curtiu = btnUtil.getAttribute('aria-pressed') === 'true';
    btnUtil.disabled = true;
    try {
      const estado = await chamar(`/api/guias/${id}/curtida`, curtiu ? 'DELETE' : 'PUT');
      if (!estado) return irParaLogin();
      mostrarEstado(estado);
      window.posthog?.capture?.(curtiu ? 'guia_descurtido' : 'guia_curtido', { slug });
    } catch {
      dica.textContent = 'Não foi possível registrar agora. Tente de novo em instantes.';
    } finally {
      btnUtil.disabled = false;
    }
  });
}

// ── Copiar link ─────────────────────────────────────────────────────────────

const btnCopiar = $('btnCopiar');
btnCopiar?.addEventListener('click', async () => {
  const original = btnCopiar.textContent;
  try {
    await navigator.clipboard.writeText(btnCopiar.dataset.url || location.href);
    btnCopiar.textContent = 'Link copiado!';
  } catch {
    btnCopiar.textContent = 'Não foi possível copiar';
  }
  setTimeout(() => { btnCopiar.textContent = original; }, 2000);
});
