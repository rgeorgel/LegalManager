// Botão claro/escuro do cabeçalho do portal admin. É uma preferência do USUÁRIO, não do
// escritório: o tema (cores, logo, CSS) continua o do tenant e o modo só converte o tema para o
// outro lado quando ele não bate (ver applyTenantTheme em js/theme.js). Salvo no banco
// (PUT /api/usuarios/me/modo-cor) e devolvido no login em usuario.modoCor; null = segue o tema
// do escritório.
//
// A troca é animada com a View Transitions API: o modo novo aparece num círculo que cresce a
// partir do botão até cobrir a tela. Sem suporte (ou com "reduzir movimento"), troca na hora.
import { apiFetch, atualizarUsuarioSessao, getUser } from './api.js';
import { applyTenantTheme, getStoredTheme, temaEscuroAtivo } from './theme.js';
import { trackEvent } from './analytics.js';

const DURACAO_MS = 700;

const STYLES = `
  #modoCorBtn { position:relative; width:36px; height:36px; padding:0; border:none; border-radius:999px;
    background:none; color:var(--color-text-muted); cursor:pointer; display:flex; align-items:center;
    justify-content:center; transition:background-color .15s, color .15s; }
  #modoCorBtn:hover { background:var(--color-bg); color:var(--color-text); }
  #modoCorBtn:focus-visible { outline:2px solid var(--color-primary); outline-offset:2px; }
  #modoCorBtn svg { position:absolute; width:20px; height:20px; transition:transform .5s cubic-bezier(.4,0,.2,1), opacity .3s; }
  #modoCorBtn .modo-sol { transform:rotate(0) scale(1); opacity:1; }
  #modoCorBtn .modo-lua { transform:rotate(-90deg) scale(.4); opacity:0; }
  #modoCorBtn.escuro .modo-sol { transform:rotate(90deg) scale(.4); opacity:0; }
  #modoCorBtn.escuro .modo-lua { transform:rotate(0) scale(1); opacity:1; }

  /* Durante a troca: a tela nova entra por um círculo que nasce no centro do botão
     (--modo-cor-x/-y, em % da tela) e cresce até --modo-cor-r, por cima da antiga, sem o
     cross-fade padrão. É animação CSS (e não element.animate depois do "ready") para o recorte já
     valer no 1º quadro — senão a tela nova pisca inteira antes do círculo — e funcionar igual no
     Safari/Firefox. Nada transiciona por dentro, senão o snapshot pega cores no meio do caminho. */
  :root.modo-cor-trocando::view-transition-old(root) { animation:none; mix-blend-mode:normal; z-index:1; }
  :root.modo-cor-trocando::view-transition-new(root) { mix-blend-mode:normal; z-index:2;
    animation:modo-cor-revelar ${DURACAO_MS}ms cubic-bezier(.65,0,.35,1) both; }
  @keyframes modo-cor-revelar {
    from { clip-path:circle(0px at var(--modo-cor-x) var(--modo-cor-y)); }
    to { clip-path:circle(var(--modo-cor-r) at var(--modo-cor-x) var(--modo-cor-y)); }
  }
  :root.modo-cor-trocando *:not(#modoCorBtn svg) { transition:none !important; }
`;

const ICONES = `
  <svg class="modo-sol" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" aria-hidden="true">
    <circle cx="12" cy="12" r="4"/>
    <path d="M12 2v2M12 20v2M4.93 4.93l1.41 1.41M17.66 17.66l1.41 1.41M2 12h2M20 12h2M4.93 19.07l1.41-1.41M17.66 6.34l1.41-1.41"/>
  </svg>
  <svg class="modo-lua" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">
    <path d="M21 12.79A9 9 0 1 1 11.21 3 7 7 0 0 0 21 12.79z"/>
  </svg>`;

/** "claro" | "escuro" | null (segue o tema do escritório). */
export function getModoCor() {
  const modo = getUser()?.modoCor;
  return modo === 'claro' || modo === 'escuro' ? modo : null;
}

function aplicarModo(modo) {
  applyTenantTheme(getStoredTheme() || getUser()?.tema, { modo });
  atualizarBotao();
}

function atualizarBotao() {
  const btn = document.getElementById('modoCorBtn');
  if (!btn) return;
  const escuro = temaEscuroAtivo();
  btn.classList.toggle('escuro', escuro);
  const rotulo = escuro ? 'Usar modo claro' : 'Usar modo escuro';
  btn.title = rotulo;
  btn.setAttribute('aria-label', rotulo);
  btn.setAttribute('aria-pressed', String(escuro));
}

async function alternar(btn) {
  const novo = temaEscuroAtivo() ? 'claro' : 'escuro';
  // Aba sem renderização (oculta) não tira o snapshot e a transição ficaria parada esperando.
  const semAnimacao = !document.startViewTransition
    || document.visibilityState !== 'visible'
    || window.matchMedia('(prefers-reduced-motion: reduce)').matches;

  if (semAnimacao) {
    aplicarModo(novo);
  } else {
    // Origem e raio em % do snapshot, não em px: em alguns Chromium (visto no Vivaldi em tela
    // Retina) o snapshot da transição fica em pixels do dispositivo, e um "circle(... at 1263px
    // 30px)" nascia na metade do caminho até o botão, com metade do raio. Porcentagem acompanha a
    // escala que o navegador usar.
    const r = btn.getBoundingClientRect();
    const larg = innerWidth;
    const alt = innerHeight;
    const x = r.left + r.width / 2;
    const y = r.top + r.height / 2;
    // Raio até o canto mais distante da tela, para o círculo cobrir tudo. Em clip-path, o raio
    // em % é relativo a √(larg² + alt²) / √2.
    const raio = Math.hypot(Math.max(x, larg - x), Math.max(y, alt - y));
    const raioPct = (raio / (Math.hypot(larg, alt) / Math.SQRT2)) * 100;
    const raiz = document.documentElement;
    raiz.style.setProperty('--modo-cor-x', `${((x / larg) * 100).toFixed(3)}%`);
    raiz.style.setProperty('--modo-cor-y', `${((y / alt) * 100).toFixed(3)}%`);
    raiz.style.setProperty('--modo-cor-r', `${(raioPct + 1).toFixed(3)}%`);
    raiz.classList.add('modo-cor-trocando');
    const transicao = document.startViewTransition(() => aplicarModo(novo));
    // Transição pulada (aba oculta etc.) rejeita o "ready"; o modo já foi aplicado mesmo assim.
    transicao.ready.catch(() => {});
    transicao.finished.finally(() => {
      raiz.classList.remove('modo-cor-trocando');
      ['--modo-cor-x', '--modo-cor-y', '--modo-cor-r'].forEach(v => raiz.style.removeProperty(v));
    });
  }

  atualizarUsuarioSessao({ modoCor: novo });
  trackEvent('modo_cor_alterado', { para: novo });
  try {
    await apiFetch('/usuarios/me/modo-cor', { method: 'PUT', body: { modo: novo } });
  } catch {
    // Falhou ao salvar: vale nesta sessão; no próximo login volta o que está no banco.
  }
}

/** Põe o botão no cabeçalho (.header-user). Chamado por initLayout depois de aplicar o tema. */
export function injectModoCorToggle() {
  const headerUser = document.querySelector('.header-user');
  if (!headerUser || document.getElementById('modoCorBtn')) return;

  if (!document.getElementById('modoCorStyles')) {
    const s = document.createElement('style');
    s.id = 'modoCorStyles';
    s.textContent = STYLES;
    document.head.appendChild(s);
  }

  const btn = document.createElement('button');
  btn.type = 'button';
  btn.id = 'modoCorBtn';
  btn.innerHTML = ICONES;
  headerUser.insertBefore(btn, headerUser.firstChild);
  btn.addEventListener('click', e => { e.stopPropagation(); alternar(btn); });
  atualizarBotao();
}
