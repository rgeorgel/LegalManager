// Personalização do dashboard: o usuário reordena (drag and drop ou botões ‹ ›),
// muda a largura (1–3 colunas) e oculta blocos. O layout é salvo por usuário em
// PUT /api/dashboard/layout e espelhado no localStorage só para aplicar sem
// "piscar" no próximo carregamento — o banco é a fonte da verdade.
//
// Cada bloco é um filho do grid com data-widget="<id>" e data-titulo="<nome>".
// A ordem no HTML é o layout padrão; blocos novos (ausentes do layout salvo)
// entram no fim, e ids salvos que não existem mais são ignorados.
import { apiFetch } from './api.js';
import { trackEvent } from './analytics.js';

const LARGURAS = [1, 2, 3];

/**
 * @param {{ grid: HTMLElement, botao: HTMLElement, userId?: string }} opts
 */
export function initDashboardLayout({ grid, botao, userId }) {
  if (!grid) return;
  injectStyles();

  const cacheKey = `dashboard_layout:${userId ?? 'anon'}`;
  const padrao = lerDoDom(grid); // antes de aplicar qualquer layout salvo
  const idsConhecidos = new Set(padrao.map(w => w.id));
  let editando = false;
  let snapshot = null;
  let arrastando = null;
  let editBar = null;

  // ── Carregamento: cache local primeiro, depois o servidor ─────────────
  const cache = lerCache();
  if (cache) aplicar(cache);

  apiFetch('/dashboard/layout')
    .then(res => {
      if (editando) return; // não atropela uma edição em curso
      const layout = res?.widgets ? normalizar(res.widgets) : padrao;
      aplicar(layout);
      salvarCache(res?.widgets ? layout : null);
    })
    .catch(() => { /* mantém cache/padrão */ });

  botao?.addEventListener('click', () => (editando ? sair(false) : entrar()));

  // ── Layout ────────────────────────────────────────────────────────────
  function lerDoDom(el) {
    return [...el.querySelectorAll(':scope > [data-widget]')].map(card => ({
      id: card.dataset.widget,
      largura: card.classList.contains('d2-w3') ? 3 : card.classList.contains('d2-w2') ? 2 : 1,
      oculto: card.classList.contains('d2-oculto'),
    }));
  }

  function normalizar(salvo) {
    const vistos = new Set();
    const lista = [];
    for (const w of salvo ?? []) {
      if (!w || !idsConhecidos.has(w.id) || vistos.has(w.id)) continue;
      vistos.add(w.id);
      lista.push({
        id: w.id,
        largura: LARGURAS.includes(w.largura) ? w.largura : 1,
        oculto: !!w.oculto,
      });
    }
    for (const w of padrao) if (!vistos.has(w.id)) lista.push({ ...w });
    return lista;
  }

  function aplicar(layout) {
    for (const w of normalizar(layout)) {
      const card = grid.querySelector(`:scope > [data-widget="${w.id}"]`);
      if (!card) continue;
      grid.appendChild(card);
      card.classList.remove('d2-w2', 'd2-w3');
      if (w.largura > 1) card.classList.add(`d2-w${w.largura}`);
      card.classList.toggle('d2-oculto', w.oculto);
      atualizarFerramentas(card);
    }
  }

  const iguais = (a, b) => JSON.stringify(a) === JSON.stringify(b);

  function lerCache() {
    try {
      const raw = localStorage.getItem(cacheKey);
      return raw ? JSON.parse(raw) : null;
    } catch { return null; }
  }

  function salvarCache(layout) {
    try {
      if (layout) localStorage.setItem(cacheKey, JSON.stringify(layout));
      else localStorage.removeItem(cacheKey);
    } catch { /* storage indisponível: só perde o "sem piscar" */ }
  }

  // ── Modo de edição ────────────────────────────────────────────────────
  function entrar() {
    editando = true;
    snapshot = lerDoDom(grid);
    grid.classList.add('d2-editando');
    grid.querySelectorAll(':scope > [data-widget]').forEach(card => {
      garantirFerramentas(card);
      card.draggable = true;
    });
    mostrarEditBar();
    botao?.setAttribute('aria-pressed', 'true');
    if (botao) botao.textContent = '✕ Sair da edição';
    // Leva a barra para o topo, descontando o header fixo.
    const header = parseInt(getComputedStyle(document.documentElement).getPropertyValue('--header-height'), 10) || 60;
    window.scrollTo({ top: editBar.getBoundingClientRect().top + window.scrollY - header - 12, behavior: 'smooth' });
  }

  function sair(manter) {
    if (!manter && snapshot) aplicar(snapshot);
    editando = false;
    snapshot = null;
    grid.classList.remove('d2-editando');
    grid.querySelectorAll(':scope > [data-widget]').forEach(card => { card.draggable = false; });
    editBar?.remove();
    editBar = null;
    botao?.setAttribute('aria-pressed', 'false');
    if (botao) botao.textContent = '⚙️ Personalizar';
  }

  async function salvar(btn) {
    const layout = lerDoDom(grid);
    btn.disabled = true;
    btn.textContent = 'Salvando…';
    try {
      if (iguais(layout, padrao)) {
        await apiFetch('/dashboard/layout', { method: 'DELETE' });
        salvarCache(null);
      } else {
        await apiFetch('/dashboard/layout', { method: 'PUT', body: { widgets: layout } });
        salvarCache(layout);
      }
      trackEvent('dashboard_layout_salvo', {
        ocultos: layout.filter(w => w.oculto).map(w => w.id),
        ordem: layout.map(w => w.id),
      });
      sair(true);
    } catch (err) {
      btn.disabled = false;
      btn.textContent = 'Salvar';
      editBar.querySelector('.d2-editbar-msg').textContent = `Não foi possível salvar: ${err.message}`;
    }
  }

  function mostrarEditBar() {
    editBar = document.createElement('div');
    editBar.className = 'd2-editbar';
    editBar.innerHTML = `
      <div class="d2-editbar-msg">Arraste os blocos ou use as setas para reorganizar. Ajuste a largura (↔) e oculte o que não usa.</div>
      <div class="d2-editbar-acoes">
        <button type="button" class="btn btn-secondary btn-sm" data-acao="padrao">Restaurar padrão</button>
        <button type="button" class="btn btn-secondary btn-sm" data-acao="cancelar">Cancelar</button>
        <button type="button" class="btn btn-primary btn-sm" data-acao="salvar">Salvar</button>
      </div>`;
    editBar.addEventListener('click', e => {
      const btn = e.target.closest('[data-acao]');
      if (!btn) return;
      if (btn.dataset.acao === 'padrao') aplicar(padrao);
      if (btn.dataset.acao === 'cancelar') sair(false);
      if (btn.dataset.acao === 'salvar') salvar(btn);
    });
    grid.before(editBar);
  }

  // ── Ferramentas por bloco (visíveis só no modo de edição) ─────────────
  function garantirFerramentas(card) {
    if (card.querySelector(':scope > .d2-wtools')) return;
    const t = document.createElement('div');
    t.className = 'd2-wtools';
    t.innerHTML = `
      <span class="d2-wtools-handle" title="Arraste para mover">⠿ <span>${esc(card.dataset.titulo || card.dataset.widget)}</span></span>
      <span class="d2-wtools-btns">
        <button type="button" data-w="antes" title="Mover para antes" aria-label="Mover ${esc(card.dataset.titulo)} para antes"><span class="d2-h">‹</span><span class="d2-v">↑</span></button>
        <button type="button" data-w="depois" title="Mover para depois" aria-label="Mover ${esc(card.dataset.titulo)} para depois"><span class="d2-h">›</span><span class="d2-v">↓</span></button>
        <button type="button" data-w="largura" title="Largura (colunas)"></button>
        <button type="button" data-w="ocultar"></button>
      </span>`;
    card.prepend(t);
    atualizarFerramentas(card);
  }

  function atualizarFerramentas(card) {
    const t = card.querySelector(':scope > .d2-wtools');
    if (!t) return;
    const largura = card.classList.contains('d2-w3') ? 3 : card.classList.contains('d2-w2') ? 2 : 1;
    const oculto = card.classList.contains('d2-oculto');
    const bLarg = t.querySelector('[data-w="largura"]');
    bLarg.textContent = `↔ ${largura}`;
    bLarg.setAttribute('aria-label', `Largura: ${largura} coluna(s). Clique para alterar`);
    const bOc = t.querySelector('[data-w="ocultar"]');
    bOc.textContent = oculto ? '🙈 Oculto' : '👁 Visível';
    bOc.title = oculto ? 'Mostrar bloco' : 'Ocultar bloco';
    bOc.setAttribute('aria-pressed', String(oculto));
  }

  grid.addEventListener('click', e => {
    if (!editando) return;
    const btn = e.target.closest('.d2-wtools [data-w]');
    if (!btn) return;
    const card = btn.closest('[data-widget]');
    const acao = btn.dataset.w;
    if (acao === 'antes' && card.previousElementSibling) {
      card.previousElementSibling.before(card);
    } else if (acao === 'depois' && card.nextElementSibling) {
      card.nextElementSibling.after(card);
    } else if (acao === 'largura') {
      const atual = card.classList.contains('d2-w3') ? 3 : card.classList.contains('d2-w2') ? 2 : 1;
      const prox = LARGURAS[(LARGURAS.indexOf(atual) + 1) % LARGURAS.length];
      card.classList.remove('d2-w2', 'd2-w3');
      if (prox > 1) card.classList.add(`d2-w${prox}`);
    } else if (acao === 'ocultar') {
      card.classList.toggle('d2-oculto');
    }
    atualizarFerramentas(card);
    // Mover o nó no DOM tira o foco do botão — devolve para quem usa teclado.
    card.querySelector(`.d2-wtools [data-w="${acao}"]`)?.focus();
  });

  // ── Drag and drop (HTML5 nativo; no toque, os botões ‹ › fazem o papel) ─
  grid.addEventListener('dragstart', e => {
    const card = e.target.closest?.('[data-widget]');
    if (!editando || !card) return;
    arrastando = card;
    card.classList.add('d2-arrastando');
    e.dataTransfer.effectAllowed = 'move';
    e.dataTransfer.setData('text/plain', card.dataset.widget); // Firefox exige setData
  });

  // Escuta no documento e acha o alvo pelas coordenadas (não por e.target): assim a
  // barra de edição fixa (sticky) por cima da 1ª linha e os vãos entre blocos não
  // "engolem" o dragover.
  document.addEventListener('dragover', e => {
    if (!arrastando) return;
    e.preventDefault();
    e.dataTransfer.dropEffect = 'move';
    const alvo = [...grid.querySelectorAll(':scope > [data-widget]')].find(card => {
      if (card === arrastando) return false;
      const r = card.getBoundingClientRect();
      return e.clientX >= r.left && e.clientX <= r.right && e.clientY >= r.top && e.clientY <= r.bottom;
    });
    if (!alvo) return;
    const r = alvo.getBoundingClientRect();
    // Bloco que ocupa a linha inteira: decide pela metade vertical; senão, horizontal.
    const linhaInteira = r.width > grid.clientWidth * 0.9;
    const depois = linhaInteira ? e.clientY > r.top + r.height / 2 : e.clientX > r.left + r.width / 2;
    const ref = depois ? alvo.nextElementSibling : alvo;
    if (ref !== arrastando && ref !== arrastando.nextElementSibling) grid.insertBefore(arrastando, ref);
  });

  document.addEventListener('drop', e => { if (arrastando) e.preventDefault(); });

  grid.addEventListener('dragend', () => {
    arrastando?.classList.remove('d2-arrastando');
    arrastando = null;
  });
}

function esc(s) {
  return String(s ?? '').replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;');
}

function injectStyles() {
  if (document.getElementById('dashLayoutStyles')) return;
  const s = document.createElement('style');
  s.id = 'dashLayoutStyles';
  // !important nas cores dos botões: o preset escuro força `button { color: inherit !important }`.
  s.textContent = `
    .d2-wtools { display: none; }
    .d2-editando > [data-widget] { outline: 2px dashed var(--color-border); outline-offset: -2px; cursor: grab; }
    .d2-editando > [data-widget]:hover { outline-color: var(--color-primary); }
    .d2-editando > [data-widget] > :not(.d2-wtools) { pointer-events: none; user-select: none; }
    .d2-editando > .d2-oculto { display: block; opacity: .45; }
    .d2-editando > .d2-arrastando { opacity: .3; }
    .d2-editando .d2-wtools {
      display: flex; align-items: center; justify-content: space-between; gap: 8px; flex-wrap: wrap;
      position: relative; z-index: 3; margin: -8px -8px 12px; padding: 6px 8px; border-radius: 10px;
      background: color-mix(in srgb, var(--color-primary) 12%, var(--color-surface));
    }
    .d2-wtools-handle { font-size: 12px; font-weight: 700; color: var(--color-text); display: flex; gap: 6px; align-items: center; min-width: 0; }
    .d2-wtools-handle span { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .d2-wtools-btns { display: flex; gap: 4px; flex-wrap: wrap; }
    .d2-wtools-btns button {
      border: 1px solid var(--color-border); background: var(--color-surface); color: var(--color-text) !important;
      border-radius: 8px; font-size: 12px; font-weight: 600; padding: 4px 8px; min-width: 30px; cursor: pointer;
    }
    .d2-wtools-btns button:hover { border-color: var(--color-primary); }
    .d2-wtools-btns [aria-pressed="true"] { color: var(--color-text-muted) !important; }
    .d2-editbar {
      display: flex; align-items: center; justify-content: space-between; gap: 12px; flex-wrap: wrap;
      position: sticky; top: calc(var(--header-height) + 8px); z-index: 20; margin-bottom: 16px; padding: 12px 16px;
      border-radius: 14px; border: 1px solid var(--color-primary); background: var(--color-surface); box-shadow: var(--shadow-md);
    }
    .d2-editbar-msg { font-size: 13px; color: var(--color-text); }
    .d2-editbar-acoes { display: flex; gap: 8px; flex-wrap: wrap; }
    .d2-wtools-btns .d2-v { display: none; }
    @media (max-width: 700px) {
      .d2-editbar { top: calc(var(--header-height) + 4px); }
      /* Uma coluna só: largura não se aplica e a ordem é vertical. */
      .d2-wtools-btns [data-w="largura"] { display: none; }
      .d2-wtools-btns .d2-h { display: none; }
      .d2-wtools-btns .d2-v { display: inline; }
    }
  `;
  document.head.appendChild(s);
}
