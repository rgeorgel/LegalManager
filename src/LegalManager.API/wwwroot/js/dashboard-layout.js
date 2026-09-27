// Personalização do dashboard: o usuário reordena (drag and drop ou setas),
// muda a largura (1–3 colunas) e a altura (½, 1 ou 2 — onde a seção permite) e oculta itens. O layout
// é salvo por usuário em PUT /api/dashboard/layout e espelhado no localStorage
// só para aplicar sem "piscar" no próximo carregamento — o banco é a fonte da
// verdade.
//
// O dashboard tem várias seções (ex.: KPIs do topo e o grid de blocos), cada
// uma salva numa chave do layout ({ kpis: [...], widgets: [...] }). Um item é
// um filho direto do grid da seção com data-<attr>="<id>" e data-titulo="<nome>".
// A ordem no HTML é o padrão; itens novos (ausentes do layout salvo) entram no
// fim, e ids salvos que não existem mais são ignorados.
import { apiFetch } from './api.js';
import { trackEvent } from './analytics.js';

import { esc } from './utils.js';
const LARGURAS = [1, 2, 3];
// Altura em "blocos": ½ = uma linha do grid, 1 = duas (padrão), 2 = quatro (ver .d2-grid em dashboard.html).
const ALTURAS = [1, 2, 0.5];
const CLASSE_ALTURA = { 0.5: 'd2-hm', 2: 'd2-h2' };
const ROTULO_ALTURA = { 0.5: '½', 1: '1', 2: '2' };

/**
 * @typedef {{ chave: string, grid: HTMLElement, attr: string, larguras?: boolean, alturas?: boolean, compacto?: boolean }} Secao
 * @param {{ secoes: Secao[], botao: HTMLElement, userId?: string }} opts
 */
export function initDashboardLayout({ secoes, botao, userId }) {
  secoes = secoes.filter(s => s.grid);
  if (!secoes.length) return;
  injectStyles();

  for (const s of secoes) {
    s.sel = `:scope > [data-${s.attr}]`;
    s.id = el => el.getAttribute(`data-${s.attr}`);
    s.padrao = lerDoDom(s); // antes de aplicar qualquer layout salvo
    s.ids = new Set(s.padrao.map(w => w.id));
    s.grid.classList.add('d2-secao');
    if (s.compacto) s.grid.classList.add('d2-secao-compacta');
  }

  const cacheKey = `dashboard_layout:${userId ?? 'anon'}`;
  let editando = false;
  let snapshot = null;
  let arrastando = null; // { secao, el }
  let editBar = null;

  // ── Carregamento: cache local primeiro, depois o servidor ─────────────
  const cache = lerCache();
  if (cache) aplicarTudo(cache);

  apiFetch('/dashboard/layout')
    .then(res => {
      if (editando) return; // não atropela uma edição em curso
      aplicarTudo(res ?? {});
      salvarCache(temAlgo(res) ? res : null);
    })
    .catch(() => { /* mantém cache/padrão */ });

  botao?.addEventListener('click', () => (editando ? sair(false) : entrar()));

  // ── Layout ────────────────────────────────────────────────────────────
  function larguraDe(el) {
    return el.classList.contains('d2-w3') ? 3 : el.classList.contains('d2-w2') ? 2 : 1;
  }

  function alturaDe(el) {
    return el.classList.contains('d2-hm') ? 0.5 : el.classList.contains('d2-h2') ? 2 : 1;
  }

  function definirAltura(el, altura) {
    el.classList.remove('d2-hm', 'd2-h2');
    if (CLASSE_ALTURA[altura]) el.classList.add(CLASSE_ALTURA[altura]);
  }

  function lerDoDom(s) {
    return [...s.grid.querySelectorAll(s.sel)].map(el => ({
      id: s.id(el),
      largura: larguraDe(el),
      altura: alturaDe(el),
      oculto: el.classList.contains('d2-oculto'),
    }));
  }

  const lerTudo = () => Object.fromEntries(secoes.map(s => [s.chave, lerDoDom(s)]));
  const temAlgo = layout => !!layout && secoes.some(s => Array.isArray(layout[s.chave]));

  function normalizar(s, salvo) {
    const vistos = new Set();
    const lista = [];
    for (const w of salvo ?? []) {
      if (!w || !s.ids.has(w.id) || vistos.has(w.id)) continue;
      vistos.add(w.id);
      lista.push({
        id: w.id,
        largura: s.larguras && LARGURAS.includes(w.largura) ? w.largura : 1,
        altura: s.alturas && ALTURAS.includes(w.altura) ? w.altura : 1,
        oculto: !!w.oculto,
      });
    }
    for (const w of s.padrao) if (!vistos.has(w.id)) lista.push({ ...w });
    return lista;
  }

  function aplicar(s, itens) {
    for (const w of normalizar(s, itens ?? s.padrao)) {
      const el = s.grid.querySelector(`:scope > [data-${s.attr}="${w.id}"]`);
      if (!el) continue;
      s.grid.appendChild(el);
      el.classList.remove('d2-w2', 'd2-w3');
      if (w.largura > 1) el.classList.add(`d2-w${w.largura}`);
      definirAltura(el, w.altura);
      el.classList.toggle('d2-oculto', w.oculto);
      atualizarFerramentas(s, el);
    }
  }

  // Aceita também o formato antigo do cache (só a lista de blocos).
  function aplicarTudo(layout) {
    const obj = Array.isArray(layout) ? { widgets: layout } : layout;
    for (const s of secoes) aplicar(s, obj?.[s.chave]);
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
    snapshot = lerTudo();
    for (const s of secoes) {
      s.grid.classList.add('d2-editando');
      s.grid.querySelectorAll(s.sel).forEach(el => {
        garantirFerramentas(s, el);
        el.draggable = true;
      });
    }
    mostrarEditBar();
    botao?.setAttribute('aria-pressed', 'true');
    if (botao) botao.textContent = '✕ Sair da edição';
    // Leva a barra para o topo, descontando o header fixo.
    const header = parseInt(getComputedStyle(document.documentElement).getPropertyValue('--header-height'), 10) || 60;
    window.scrollTo({ top: editBar.getBoundingClientRect().top + window.scrollY - header - 12, behavior: 'smooth' });
  }

  function sair(manter) {
    if (!manter && snapshot) aplicarTudo(snapshot);
    editando = false;
    snapshot = null;
    for (const s of secoes) {
      s.grid.classList.remove('d2-editando');
      s.grid.querySelectorAll(s.sel).forEach(el => { el.draggable = false; });
    }
    editBar?.remove();
    editBar = null;
    botao?.setAttribute('aria-pressed', 'false');
    if (botao) botao.textContent = '⚙️ Personalizar';
  }

  async function salvar(btn) {
    // Seções iguais ao padrão vão como null: blocos novos seguem a ordem padrão.
    const layout = Object.fromEntries(secoes.map(s => {
      const atual = lerDoDom(s);
      return [s.chave, iguais(atual, s.padrao) ? null : atual];
    }));
    btn.disabled = true;
    btn.textContent = 'Salvando…';
    try {
      if (!secoes.some(s => layout[s.chave])) {
        await apiFetch('/dashboard/layout', { method: 'DELETE' });
        salvarCache(null);
      } else {
        await apiFetch('/dashboard/layout', { method: 'PUT', body: layout });
        salvarCache(layout);
      }
      const tudo = lerTudo();
      trackEvent('dashboard_layout_salvo', Object.fromEntries(secoes.flatMap(s => [
        [`${s.chave}_ordem`, tudo[s.chave].map(w => w.id)],
        [`${s.chave}_ocultos`, tudo[s.chave].filter(w => w.oculto).map(w => w.id)],
      ])));
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
      <div class="d2-editbar-msg">Arraste os indicadores e blocos ou use as setas para reorganizar. Ajuste a largura (↔), a altura (↕) e oculte o que não usa.</div>
      <div class="d2-editbar-acoes">
        <button type="button" class="btn btn-secondary btn-sm" data-acao="padrao">Restaurar padrão</button>
        <button type="button" class="btn btn-secondary btn-sm" data-acao="cancelar">Cancelar</button>
        <button type="button" class="btn btn-primary btn-sm" data-acao="salvar">Salvar</button>
      </div>`;
    editBar.addEventListener('click', e => {
      const btn = e.target.closest('[data-acao]');
      if (!btn) return;
      if (btn.dataset.acao === 'padrao') for (const s of secoes) aplicar(s, s.padrao);
      if (btn.dataset.acao === 'cancelar') sair(false);
      if (btn.dataset.acao === 'salvar') salvar(btn);
    });
    secoes[0].grid.before(editBar);
  }

  // ── Ferramentas por item (visíveis só no modo de edição) ──────────────
  function garantirFerramentas(s, el) {
    if (el.querySelector(':scope > .d2-wtools')) return;
    const titulo = esc(el.dataset.titulo || s.id(el));
    const t = document.createElement('div');
    t.className = 'd2-wtools';
    t.innerHTML = `
      <span class="d2-wtools-handle" title="Arraste para mover">⠿ <span>${titulo}</span></span>
      <span class="d2-wtools-btns">
        <button type="button" data-w="antes" title="Mover para antes" aria-label="Mover ${titulo} para antes"><span class="d2-h">‹</span><span class="d2-v">↑</span></button>
        <button type="button" data-w="depois" title="Mover para depois" aria-label="Mover ${titulo} para depois"><span class="d2-h">›</span><span class="d2-v">↓</span></button>
        ${s.larguras ? '<button type="button" data-w="largura" title="Largura (colunas)"></button>' : ''}
        ${s.alturas ? '<button type="button" data-w="altura" title="Altura (½, 1 ou 2 blocos)"></button>' : ''}
        <button type="button" data-w="ocultar"></button>
      </span>`;
    el.prepend(t);
    atualizarFerramentas(s, el);
  }

  function atualizarFerramentas(s, el) {
    const t = el.querySelector(':scope > .d2-wtools');
    if (!t) return;
    const bLarg = t.querySelector('[data-w="largura"]');
    if (bLarg) {
      bLarg.textContent = `↔ ${larguraDe(el)}`;
      bLarg.setAttribute('aria-label', `Largura: ${larguraDe(el)} coluna(s). Clique para alterar`);
    }
    const bAlt = t.querySelector('[data-w="altura"]');
    if (bAlt) {
      const rotulo = ROTULO_ALTURA[alturaDe(el)];
      bAlt.textContent = `↕ ${rotulo}`;
      bAlt.setAttribute('aria-label', `Altura: ${rotulo} bloco(s). Clique para alterar`);
    }
    const oculto = el.classList.contains('d2-oculto');
    const bOc = t.querySelector('[data-w="ocultar"]');
    bOc.textContent = s.compacto ? (oculto ? '🙈' : '👁') : (oculto ? '🙈 Oculto' : '👁 Visível');
    bOc.title = oculto ? 'Mostrar' : 'Ocultar';
    bOc.setAttribute('aria-label', `${oculto ? 'Mostrar' : 'Ocultar'} ${el.dataset.titulo || ''}`.trim());
    bOc.setAttribute('aria-pressed', String(oculto));
  }

  const secaoDe = el => secoes.find(s => s.grid === el?.parentElement);

  for (const s of secoes) {
    // Em edição, clicar no item não navega — inclusive nos botões das ferramentas,
    // já que o KPI é um <a> e o clique num botão dentro dele seguiria o link.
    s.grid.addEventListener('click', e => {
      if (!editando) return;
      e.preventDefault();
      const btn = e.target.closest('.d2-wtools [data-w]');
      if (!btn) return;
      const el = btn.closest(`[data-${s.attr}]`);
      const acao = btn.dataset.w;
      if (acao === 'antes' && el.previousElementSibling) {
        el.previousElementSibling.before(el);
      } else if (acao === 'depois' && el.nextElementSibling) {
        el.nextElementSibling.after(el);
      } else if (acao === 'largura') {
        const prox = LARGURAS[(LARGURAS.indexOf(larguraDe(el)) + 1) % LARGURAS.length];
        el.classList.remove('d2-w2', 'd2-w3');
        if (prox > 1) el.classList.add(`d2-w${prox}`);
      } else if (acao === 'altura') {
        definirAltura(el, ALTURAS[(ALTURAS.indexOf(alturaDe(el)) + 1) % ALTURAS.length]);
      } else if (acao === 'ocultar') {
        el.classList.toggle('d2-oculto');
      }
      atualizarFerramentas(s, el);
      // Mover o nó no DOM tira o foco do botão — devolve para quem usa teclado.
      el.querySelector(`.d2-wtools [data-w="${acao}"]`)?.focus();
    }, true);

    // ── Drag and drop (HTML5 nativo; no toque, as setas fazem o papel) ──
    s.grid.addEventListener('dragstart', e => {
      const el = e.target.closest?.(`[data-${s.attr}]`);
      if (!editando || !el || secaoDe(el) !== s) return;
      arrastando = { secao: s, el };
      el.classList.add('d2-arrastando');
      e.dataTransfer.effectAllowed = 'move';
      e.dataTransfer.setData('text/plain', s.id(el)); // Firefox exige setData
    });
  }

  // Escuta no documento e acha o alvo pelas coordenadas (não por e.target): assim a
  // barra de edição fixa (sticky) e os vãos entre itens não "engolem" o dragover.
  // Só reordena dentro da própria seção.
  document.addEventListener('dragover', e => {
    if (!arrastando) return;
    e.preventDefault();
    e.dataTransfer.dropEffect = 'move';
    const { secao: s, el: arr } = arrastando;
    const alvo = [...s.grid.querySelectorAll(s.sel)].find(el => {
      if (el === arr) return false;
      const r = el.getBoundingClientRect();
      return e.clientX >= r.left && e.clientX <= r.right && e.clientY >= r.top && e.clientY <= r.bottom;
    });
    if (!alvo) return;
    const r = alvo.getBoundingClientRect();
    // Item que ocupa a linha inteira: decide pela metade vertical; senão, horizontal.
    const linhaInteira = r.width > s.grid.clientWidth * 0.9;
    const depois = linhaInteira ? e.clientY > r.top + r.height / 2 : e.clientX > r.left + r.width / 2;
    const ref = depois ? alvo.nextElementSibling : alvo;
    if (ref !== arr && ref !== arr.nextElementSibling) s.grid.insertBefore(arr, ref);
  });

  document.addEventListener('drop', e => { if (arrastando) e.preventDefault(); });

  document.addEventListener('dragend', () => {
    arrastando?.el.classList.remove('d2-arrastando');
    arrastando = null;
  });
}


function injectStyles() {
  if (document.getElementById('dashLayoutStyles')) return;
  const s = document.createElement('style');
  s.id = 'dashLayoutStyles';
  // !important nas cores dos botões: o preset escuro força `button { color: inherit !important }`.
  s.textContent = `
    .d2-wtools { display: none; }
    .d2-editando > * { outline: 2px dashed var(--color-border); outline-offset: -2px; cursor: grab; }
    .d2-editando > *:hover { outline-color: var(--color-primary); }
    .d2-editando > * > :not(.d2-wtools) { pointer-events: none; user-select: none; }
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
    .d2-wtools-btns .d2-v { display: none; }
    /* Seção compacta (KPIs): ferramentas numa linha só, sem o título (já está no card). */
    .d2-secao-compacta.d2-editando .d2-wtools { margin: -6px -8px 8px; padding: 4px; flex-wrap: nowrap; }
    .d2-secao-compacta .d2-wtools-handle span { display: none; }
    .d2-secao-compacta .d2-wtools-btns { flex-wrap: nowrap; gap: 2px; }
    .d2-secao-compacta .d2-wtools-btns button { padding: 2px 6px; min-width: 26px; }
    .d2-editbar {
      display: flex; align-items: center; justify-content: space-between; gap: 12px; flex-wrap: wrap;
      position: sticky; top: calc(var(--header-height) + 8px); z-index: 20; margin-bottom: 16px; padding: 12px 16px;
      border-radius: 14px; border: 1px solid var(--color-primary); background: var(--color-surface); box-shadow: var(--shadow-md);
    }
    .d2-editbar-msg { font-size: 13px; color: var(--color-text); }
    .d2-editbar-acoes { display: flex; gap: 8px; flex-wrap: wrap; }
    @media (max-width: 700px) {
      .d2-editbar { top: calc(var(--header-height) + 4px); }
      /* Grid de blocos vira uma coluna: largura e altura não se aplicam e a ordem é vertical. */
      .d2-secao:not(.d2-secao-compacta) .d2-wtools-btns :is([data-w="largura"], [data-w="altura"]) { display: none; }
      .d2-secao:not(.d2-secao-compacta) .d2-wtools-btns .d2-h { display: none; }
      .d2-secao:not(.d2-secao-compacta) .d2-wtools-btns .d2-v { display: inline; }
    }
  `;
  document.head.appendChild(s);
}
