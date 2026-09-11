// Motor de tutorial guiado (spotlight + bolha) do portal admin. Funciona
// através de múltiplas páginas — o app não é SPA — guardando o tour em
// andamento no sessionStorage; cada página, ao chamar initLayout(), invoca
// resumeTourIfActive() para continuar de onde parou.
//
// Não importa nada de layout.js (evita import circular, já que layout.js
// importa este módulo para injetar o widget e retomar o tour); por isso
// getPlano() é reimplementado aqui, igual ao de layout.js.
import { apiFetch, getUser } from './api.js';
import { TOURS } from './tours-config.js';

const ACTIVE_KEY = 'tour_active';
const WAIT_TIMEOUT_MS = 6000;
const WAIT_POLL_MS = 150;

// Cor de escurecimento do spotlight (preenchimento do rect com <mask> em
// ensureDom()). Preto neutro, não uma cor de tema — mesma convenção do
// .modal-overlay (rgba(0,0,0,.5)) — pois uma cor de tema pode ficar quase
// invisível sobre um tenant com tema escuro parecido.
//
// NÃO usar rgb(0,0,0) puro (nem quase-puro, tipo rgb(1,1,1)): verificado
// empiricamente que, especificamente nesta técnica (SVG <rect> com <mask>),
// o Chromium simplesmente não pinta o rgba() quando os 3 canais de cor são
// muito próximos de zero — o elemento existe, o estilo computado confere
// 100% (background/fill, opacity, z-index, sem filter/transform/isolation/
// contain em nenhum ancestral), mas nada aparece na tela. Qualquer canal
// razoavelmente afastado de zero (testado: azul puro, e cinza a partir de
// ~rgb(15,15,15)) renderiza normalmente. Não é um problema de timing/cache
// de <style> — só da cor. Manter os valores baixos, mas nunca abaixo de
// ~15 em nenhum canal.
const DIM_COLOR = 'rgba(18,18,24,.72)';

let _statusPromise = null; // Promise<Set<string>> — cache por carregamento de página
let _dom = null; // { overlay, svg, maskHole, ring, bubble }
let _reposArgs = null;

function getPlano() {
  return getUser()?.plano ?? 'Free';
}

function planoAtende(planRequired) {
  if (!planRequired) return true;
  const plano = getPlano();
  if (planRequired === 'Plus') return plano === 'Plus' || plano === 'Pro';
  if (planRequired === 'Pro') return plano === 'Pro';
  return true;
}

function eligibleSteps(tour) {
  return tour.steps.filter(s => planoAtende(s.planRequired));
}

function findTour(tourId) {
  return TOURS.find(t => t.id === tourId) ?? null;
}

// ── Status de conclusão (servidor é fonte da verdade) ───────────────────

function fetchStatus() {
  if (!_statusPromise) {
    _statusPromise = apiFetch('/tours/status')
      .then(r => new Set(r?.concluidos ?? []))
      .catch(() => new Set());
  }
  return _statusPromise;
}

export async function isTourCompleted(tourId) {
  const set = await fetchStatus();
  return set.has(tourId);
}

async function marcarConcluidoNoServidor(tourId) {
  try {
    await apiFetch('/tours/concluir', { method: 'POST', body: { tourId } });
    (await fetchStatus()).add(tourId);
  } catch { /* best-effort — não bloqueia a experiência do usuário */ }
}

function registrarEvento(tourId, stepId, acao) {
  if (!tourId || !stepId) return;
  apiFetch('/tours/evento', { method: 'POST', body: { tourId, stepId, acao } }).catch(() => {});
}

// ── Estado do tour em andamento (sessionStorage) ────────────────────────

function getActiveState() {
  try {
    const raw = sessionStorage.getItem(ACTIVE_KEY);
    return raw ? JSON.parse(raw) : null;
  } catch { return null; }
}

function setActiveState(state) {
  try { sessionStorage.setItem(ACTIVE_KEY, JSON.stringify(state)); } catch {}
}

function clearActiveState() {
  try { sessionStorage.removeItem(ACTIVE_KEY); } catch {}
}

function currentStep(state) {
  if (!state) return null;
  const tour = findTour(state.tourId);
  if (!tour) return null;
  return eligibleSteps(tour)[state.stepIndex] ?? null;
}

// ── API pública ──────────────────────────────────────────────────────────

export async function startTour(tourId) {
  const tour = findTour(tourId);
  if (!tour) return;

  const steps = eligibleSteps(tour);
  if (steps.length === 0) return;

  setActiveState({ tourId, stepIndex: 0, startedAt: new Date().toISOString() });
  await goToCurrentStep();
}

export async function autoStartGeneralTour(tourId = 'geral') {
  if (getActiveState()) return; // já existe um tour em andamento/retomando
  if (await isTourCompleted(tourId)) return;
  await startTour(tourId);
}

export async function resumeTourIfActive() {
  const state = getActiveState();
  if (!state) return;

  const step = currentStep(state);
  if (!step) { clearActiveState(); return; }
  if (step.page !== window.location.pathname) return; // outra página cuida disso

  await renderCurrentStep();
}

export function stopTour(reason = 'pulado') {
  const state = getActiveState();
  if (state) {
    if (reason === 'concluido') {
      marcarConcluidoNoServidor(state.tourId);
      // Hook de limpeza pós-tour (ex.: fechar um modal aberto durante a
      // demonstração, excluir um registro criado só para o passo mostrar
      // algo preenchido). Só roda quando o tour termina de verdade
      // (avançou pelo último passo), não ao pular/fechar no meio.
      const tour = findTour(state.tourId);
      if (typeof tour?.onComplete === 'function') {
        try { tour.onComplete(); } catch { /* não deixa a limpeza quebrar o encerramento do tour */ }
      }
    }
    registrarEvento(state.tourId, currentStep(state)?.id, reason);
  }
  clearActiveState();
  destroyOverlay();
}

export function listAvailableTours() {
  return TOURS;
}

export async function getCompletedTourIds() {
  return [...(await fetchStatus())];
}

// ── Navegação entre passos (inclusive entre páginas) ────────────────────

async function goToCurrentStep() {
  const state = getActiveState();
  if (!state) return;

  const step = currentStep(state);
  if (!step) { stopTour('concluido'); return; }

  if (step.page !== window.location.pathname) {
    window.location.href = step.page;
    return;
  }
  await renderCurrentStep();
}

async function advance(direction) {
  const state = getActiveState();
  if (!state) return;

  registrarEvento(state.tourId, currentStep(state)?.id, direction > 0 ? 'avancou' : 'voltou');

  const nextIndex = state.stepIndex + direction;
  if (nextIndex < 0) return;

  const tour = findTour(state.tourId);
  const steps = tour ? eligibleSteps(tour) : [];
  if (nextIndex >= steps.length) { stopTour('concluido'); return; }

  setActiveState({ ...state, stepIndex: nextIndex });
  await goToCurrentStep();
}

async function renderCurrentStep() {
  const state = getActiveState();
  if (!state) return;
  const tour = findTour(state.tourId);
  if (!tour) return;
  const steps = eligibleSteps(tour);
  const step = steps[state.stepIndex];
  if (!step) { stopTour('concluido'); return; }

  if (typeof step.onBeforeShow === 'function') {
    try { await step.onBeforeShow(); } catch { /* segue mesmo se o hook falhar */ }
  }

  const el = await waitForElement(step.selector);
  if (!el) { advance(1); return; } // elemento nunca apareceu — pula para o próximo passo

  // Rola ANTES de medir/posicionar (sem animação) — se o scroll acontecesse
  // depois, como antes, o retângulo já calculado ficava obsoleto assim que
  // a página rolasse, deixando o realce fora do lugar (às vezes até fora
  // da tela). A própria transição CSS do anel/bolha já suaviza a troca de
  // posição visualmente, então o scroll não precisa ser 'smooth'.
  el.scrollIntoView({ block: 'center' });

  ensureDom();
  positionSpotlight(el, step.spotlightPadding ?? 8);
  renderBubble(tour, steps, state.stepIndex, el, step);

  registrarEvento(tour.id, step.id, 'exibido');
}

function isVisible(el) {
  if (!el) return false;
  const style = window.getComputedStyle(el);
  if (style.display === 'none' || style.visibility === 'hidden') return false;
  const rect = el.getBoundingClientRect();
  return rect.width > 0 && rect.height > 0;
}

function waitForElement(selector) {
  return new Promise(resolve => {
    const start = Date.now();
    const tick = () => {
      const el = document.querySelector(selector);
      if (el && isVisible(el)) return resolve(el);
      if (Date.now() - start > WAIT_TIMEOUT_MS) return resolve(el ?? null);
      setTimeout(tick, WAIT_POLL_MS);
    };
    tick();
  });
}

// ── Renderização (overlay de spotlight + bolha) ─────────────────────────

const SVG_NS = 'http://www.w3.org/2000/svg';

function ensureDom() {
  if (_dom) return;
  injectOverlayStyles();

  const overlay = document.createElement('div');
  overlay.className = 'tour-overlay';

  // Escurece tudo ao redor do elemento destacado via SVG + <mask> (um
  // retângulo cobrindo a tela inteira, com um "buraco" recortado pela
  // máscara). Preferido em vez de um único box-shadow com spread de
  // milhares de px (Chromium não rasteriza esse spread — o recorte
  // simplesmente não aparece) ou de 4 divs translúcidas ao redor do
  // recorte. Máscara SVG é a técnica usada por bibliotecas de tour maduras.
  const svg = document.createElementNS(SVG_NS, 'svg');
  svg.setAttribute('class', 'tour-dim-svg');
  const mask = document.createElementNS(SVG_NS, 'mask');
  mask.id = 'tourDimMask';
  const maskBg = document.createElementNS(SVG_NS, 'rect');
  maskBg.setAttribute('x', '0');
  maskBg.setAttribute('y', '0');
  maskBg.setAttribute('width', '100%');
  maskBg.setAttribute('height', '100%');
  maskBg.setAttribute('fill', 'white');
  const maskHole = document.createElementNS(SVG_NS, 'rect');
  maskHole.setAttribute('fill', 'black');
  maskHole.setAttribute('rx', '8');
  mask.append(maskBg, maskHole);
  const defs = document.createElementNS(SVG_NS, 'defs');
  defs.appendChild(mask);
  const dimRect = document.createElementNS(SVG_NS, 'rect');
  dimRect.setAttribute('x', '0');
  dimRect.setAttribute('y', '0');
  dimRect.setAttribute('width', '100%');
  dimRect.setAttribute('height', '100%');
  // Preto quase puro, NÃO rgb(0,0,0) exato — ver o comentário grande em
  // DIM_COLOR (no topo do arquivo) para o motivo, verificado empiricamente.
  dimRect.setAttribute('fill', DIM_COLOR);
  dimRect.setAttribute('mask', 'url(#tourDimMask)');
  svg.append(defs, dimRect);
  overlay.appendChild(svg);

  const ring = document.createElement('div');
  ring.className = 'tour-spotlight-ring';
  overlay.appendChild(ring);

  const bubble = document.createElement('div');
  bubble.className = 'tour-bubble';
  overlay.appendChild(bubble);

  document.body.appendChild(overlay);
  _dom = { overlay, svg, maskHole, ring, bubble };
  window.addEventListener('resize', repositionCurrent);
}

function destroyOverlay() {
  window.removeEventListener('resize', repositionCurrent);
  _reposArgs = null;
  if (!_dom) return;
  _dom.overlay.remove();
  _dom = null;
}

function repositionCurrent() {
  if (!_dom || !_reposArgs) return;
  positionSpotlight(_reposArgs.el, _reposArgs.padding);
  positionBubble(_reposArgs.el, _reposArgs.placement);
}

function positionSpotlight(el, padding) {
  const rect = el.getBoundingClientRect();
  const top = rect.top - padding;
  const left = rect.left - padding;
  const width = rect.width + padding * 2;
  const height = rect.height + padding * 2;

  const { maskHole, ring } = _dom;
  maskHole.setAttribute('x', String(left));
  maskHole.setAttribute('y', String(top));
  maskHole.setAttribute('width', String(Math.max(0, width)));
  maskHole.setAttribute('height', String(Math.max(0, height)));

  setRect(ring, top, left, width, height);
}

function setRect(el, top, left, width, height) {
  el.style.top = `${top}px`;
  el.style.left = `${left}px`;
  el.style.width = `${width}px`;
  el.style.height = `${height}px`;
}

function renderBubble(tour, steps, index, el, step) {
  const bubble = _dom.bubble;
  const isFirst = index === 0;
  const isLast = index === steps.length - 1;

  bubble.innerHTML = `
    <div class="tour-bubble-header">
      <span class="tour-bubble-title">${esc(step.title)}</span>
      <button type="button" class="tour-bubble-close" id="tourBubbleClose" title="Fechar tutorial">✕</button>
    </div>
    <div class="tour-bubble-body">${step.text}</div>
    <div class="tour-bubble-footer">
      <span class="tour-step-counter">${index + 1} de ${steps.length}</span>
      <div class="tour-bubble-actions">
        ${!isFirst ? '<button type="button" class="btn btn-secondary btn-sm" id="tourBtnBack">Voltar</button>' : ''}
        <button type="button" class="btn btn-secondary btn-sm" id="tourBtnSkip">Pular</button>
        <button type="button" class="btn btn-primary btn-sm" id="tourBtnNext">${isLast ? 'Concluir' : 'Próximo'}</button>
      </div>
    </div>
  `;

  bubble.querySelector('#tourBubbleClose').addEventListener('click', () => stopTour('fechado'));
  bubble.querySelector('#tourBtnSkip').addEventListener('click', () => stopTour('pulado'));
  bubble.querySelector('#tourBtnNext').addEventListener('click', () => advance(1));
  bubble.querySelector('#tourBtnBack')?.addEventListener('click', () => advance(-1));

  const placement = step.placement || 'auto';
  positionBubble(el, placement);
  _reposArgs = { el, padding: step.spotlightPadding ?? 8, placement };
}

function positionBubble(el, placement) {
  const bubble = _dom.bubble;
  const rect = el.getBoundingClientRect();
  const bubbleRect = bubble.getBoundingClientRect();
  const margin = 14;

  let final = placement;
  if (final === 'auto') {
    final = rect.bottom + bubbleRect.height + margin < window.innerHeight ? 'bottom' : 'top';
  }

  let top, left;
  if (final === 'bottom') { top = rect.bottom + margin; left = rect.left; }
  else if (final === 'top') { top = rect.top - bubbleRect.height - margin; left = rect.left; }
  else if (final === 'left') { top = rect.top; left = rect.left - bubbleRect.width - margin; }
  else { top = rect.top; left = rect.right + margin; } // right

  left = Math.max(12, Math.min(left, window.innerWidth - bubbleRect.width - 12));
  top = Math.max(12, Math.min(top, window.innerHeight - bubbleRect.height - 12));

  bubble.style.top = `${top}px`;
  bubble.style.left = `${left}px`;
}

// ── Widget flutuante "Tutorial" (lateral direita, como o de tarefas) ────

export function injectTourWidget() {
  if (document.getElementById('tourWidget')) return;
  injectWidgetStyles();

  const stacked = !!document.getElementById('tarefasWidget');
  const wrap = document.createElement('div');
  wrap.id = 'tourWidget';
  wrap.className = 'tour-widget' + (stacked ? ' stacked' : '');
  wrap.innerHTML = `
    <button type="button" class="tour-widget-toggle" id="tourWidgetToggle" title="Tutorial guiado">
      <span class="tour-widget-icon" aria-hidden="true">🎓</span>
      <span class="tour-widget-label">Tutorial</span>
    </button>
    <div class="tour-widget-panel" role="region" aria-label="Tutorial guiado">
      <div class="tour-widget-panel-header">
        <strong>Tutorial guiado</strong>
        <button type="button" class="tour-widget-panel-close" id="tourWidgetClose">✕</button>
      </div>
      <div class="tour-widget-panel-body" id="tourWidgetBody">
        <div class="tour-widget-loading">Carregando…</div>
      </div>
    </div>
  `;
  document.body.appendChild(wrap);

  document.getElementById('tourWidgetToggle').addEventListener('click', () => toggleWidget());
  document.getElementById('tourWidgetClose').addEventListener('click', () => toggleWidget(false));
}

function toggleWidget(force) {
  const wrap = document.getElementById('tourWidget');
  if (!wrap) return;
  const open = force !== undefined ? force : !wrap.classList.contains('open');
  wrap.classList.toggle('open', open);
  if (open) renderWidgetList();
}

async function renderWidgetList() {
  const body = document.getElementById('tourWidgetBody');
  if (!body) return;

  const completedSet = await fetchStatus();

  body.innerHTML = TOURS.map(tour => {
    const bloqueado = eligibleSteps(tour).length === 0;
    const done = completedSet.has(tour.id);
    const badge = tourPlanBadge(tour);
    return `
      <button type="button" class="tour-list-item" data-tour-id="${tour.id}" data-locked="${bloqueado}">
        <span class="tour-list-item-icon">${done ? '✅' : '▶️'}</span>
        <span class="tour-list-item-label">${esc(tour.label)}</span>
        ${badge}
      </button>`;
  }).join('');

  body.querySelectorAll('.tour-list-item').forEach(btn => {
    btn.addEventListener('click', () => {
      const tourId = btn.dataset.tourId;
      toggleWidget(false);
      if (btn.dataset.locked === 'true') {
        showLockedTourToast(maiorExigencia(findTour(tourId)));
        return;
      }
      startTour(tourId);
    });
  });
}

function maiorExigencia(tour) {
  if (!tour) return 'Plus';
  if (tour.steps.some(s => s.planRequired === 'Pro')) return 'Pro';
  if (tour.steps.some(s => s.planRequired === 'Plus')) return 'Plus';
  return null;
}

function tourPlanBadge(tour) {
  // Só mostra o selo quando o tour inteiro fica bloqueado no plano atual
  // (nenhum passo elegível) — ex: "Cálculo de Prazos" no Free. Um tour com
  // alguns passos Plus/Pro misturados com passos livres (ex: "Documentos",
  // onde só o passo de Modelos exige Pro) continua útil e inicia
  // normalmente, então não deve carregar o selo de bloqueio.
  if (eligibleSteps(tour).length > 0) return '';
  const exigencia = maiorExigencia(tour);
  return exigencia ? `<span class="tour-list-item-badge">${exigencia}</span>` : '';
}

function showLockedTourToast(planRequired) {
  const plano = planRequired ?? 'Plus';
  let toast = document.getElementById('tourLockedToast');
  if (!toast) {
    toast = document.createElement('div');
    toast.id = 'tourLockedToast';
    toast.className = 'tour-locked-toast';
    document.body.appendChild(toast);
  }
  toast.innerHTML = `
    <span style="font-size:20px">⭐</span>
    <div>
      <div class="tour-locked-toast-title">Funcionalidade ${esc(plano)}</div>
      <div class="tour-locked-toast-text">Este tutorial cobre um recurso disponível a partir do plano ${esc(plano)}.</div>
    </div>
    <a href="/pages/assinatura.html" class="tour-locked-toast-cta">Ver ${esc(plano)}</a>
  `;
  toast.classList.add('show');
  clearTimeout(toast._timeout);
  toast._timeout = setTimeout(() => toast.classList.remove('show'), 4500);
}

function esc(str) {
  return (str ?? '').replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');
}

// Estilos do overlay do tour (spotlight + bolha): injetados uma vez por
// carregamento de página, como injectWidgetStyles().
function injectOverlayStyles() {
  if (document.getElementById('tourOverlayStyles')) return;
  const s = document.createElement('style');
  s.id = 'tourOverlayStyles';
  s.textContent = `
    .tour-overlay { position: fixed; inset: 0; z-index: 10500; pointer-events: none; }
    .tour-dim-svg { position: fixed; inset: 0; width: 100%; height: 100%; pointer-events: none; }
    .tour-spotlight-ring {
      position: fixed; border-radius: 8px; pointer-events: none;
      box-shadow: 0 0 0 3px var(--color-primary), 0 4px 20px rgba(0,0,0,.35);
      transition: top .2s ease, left .2s ease, width .2s ease, height .2s ease;
    }
    .tour-bubble {
      position: fixed; z-index: 10501; width: 320px; max-width: calc(100vw - 24px);
      background: var(--color-surface); border: 1px solid var(--color-border);
      border-radius: var(--radius); box-shadow: var(--shadow-md); pointer-events: auto;
    }
    .tour-bubble-header { display: flex; align-items: flex-start; justify-content: space-between; gap: 8px; padding: 14px 14px 0 16px; }
    .tour-bubble-title { font-weight: 700; font-size: 14px; color: var(--color-text); }
    .tour-bubble-close { background: none; border: none; cursor: pointer; font-size: 13px; color: var(--color-text-muted); padding: 2px; line-height: 1; }
    .tour-bubble-close:hover { color: var(--color-text); }
    .tour-bubble-body { padding: 10px 16px 4px; font-size: 13px; color: var(--color-text-muted); line-height: 1.5; }
    .tour-bubble-footer { display: flex; align-items: center; justify-content: space-between; gap: 8px; padding: 12px 16px 16px; }
    .tour-step-counter { font-size: 11px; color: var(--color-text-muted); white-space: nowrap; }
    .tour-bubble-actions { display: flex; gap: 6px; }

    @media (max-width: 768px) {
      .tour-bubble { width: calc(100vw - 24px); }
    }
  `;
  document.head.appendChild(s);
}

// Estilos do widget flutuante "Tutorial": injetados uma vez, no carregamento
// da página (chamado por injectTourWidget(), a partir de initLayout()).
function injectWidgetStyles() {
  if (document.getElementById('tourWidgetStyles')) return;
  const s = document.createElement('style');
  s.id = 'tourWidgetStyles';
  s.textContent = `
    /* ── Widget flutuante "Tutorial" (lateral direita) ── */
    .tour-widget { position: fixed; top: 50%; right: 0; transform: translateY(-50%); z-index: 940; display: flex; align-items: center; }
    .tour-widget.stacked { transform: translateY(-50%) translateY(130px); }
    .tour-widget-toggle {
      display: flex; flex-direction: column; align-items: center; gap: 8px; width: 34px;
      background: var(--color-primary); color: #fff; border: none; cursor: pointer;
      border-radius: 10px 0 0 10px; padding: 12px 7px; box-shadow: var(--shadow-md);
    }
    .tour-widget-toggle:hover { background: var(--color-primary-dark); }
    .tour-widget-icon { font-size: 15px; line-height: 1; }
    .tour-widget-label {
      writing-mode: vertical-rl; transform: rotate(180deg);
      font-size: 11px; font-weight: 700; letter-spacing: .04em; line-height: 1;
    }
    .tour-widget-panel {
      position: absolute; right: 100%; top: 50%; margin-right: 8px;
      transform: translateY(-50%) translateX(12px); opacity: 0; pointer-events: none;
      transition: transform .18s ease, opacity .18s ease;
      width: 260px; max-width: calc(100vw - 24px);
      background: var(--color-surface); border: 1px solid var(--color-border); border-radius: var(--radius);
      box-shadow: var(--shadow-md); overflow: hidden;
    }
    .tour-widget.open .tour-widget-panel { transform: translateY(-50%) translateX(0); opacity: 1; pointer-events: auto; }
    .tour-widget-panel-header {
      display: flex; align-items: center; justify-content: space-between; gap: 8px;
      padding: 10px 12px; border-bottom: 1px solid var(--color-border); font-size: 13px; color: var(--color-text);
    }
    .tour-widget-panel-close { background: none; border: none; cursor: pointer; font-size: 13px; color: var(--color-text-muted); padding: 4px; line-height: 1; }
    .tour-widget-panel-close:hover { color: var(--color-text); }
    .tour-widget-panel-body { max-height: 320px; overflow-y: auto; padding: 6px; }
    .tour-widget-loading { color: var(--color-text-muted); font-size: 12px; text-align: center; padding: 16px 4px; }
    .tour-list-item {
      display: flex; align-items: center; gap: 8px; width: 100%; text-align: left;
      background: none; border: none; cursor: pointer; padding: 9px 8px; border-radius: 6px;
      font-size: 13px; color: var(--color-text);
    }
    .tour-list-item:hover { background: var(--color-bg); }
    .tour-list-item-icon { flex-shrink: 0; }
    .tour-list-item-label { flex: 1; }
    .tour-list-item-badge {
      background: #f59e0b; color: #0f172a; font-size: 10px; font-weight: 700;
      padding: 1px 6px; border-radius: 100px; letter-spacing: .04em;
    }

    .tour-locked-toast {
      display: none; position: fixed; bottom: 80px; left: 24px; z-index: 9999;
      background: #0f172a; border: 1px solid #f59e0b; color: #e2e8f0;
      border-radius: 12px; padding: 14px 18px; box-shadow: 0 8px 32px rgba(0,0,0,.4);
      align-items: center; gap: 12px; max-width: 340px;
    }
    .tour-locked-toast.show { display: flex; }
    .tour-locked-toast-title { font-weight: 700; font-size: 13px; color: #f59e0b; }
    .tour-locked-toast-text { font-size: 12px; margin-top: 2px; }
    .tour-locked-toast-cta {
      margin-left: auto; background: #f59e0b; color: #0f172a; font-size: 11px; font-weight: 600;
      padding: 4px 10px; border-radius: 6px; text-decoration: none; white-space: nowrap;
    }

    @media (max-width: 768px) {
      .tour-widget { top: auto; bottom: calc(var(--bottom-nav-height, 0px) + 16px); transform: none; }
      .tour-widget.stacked { bottom: calc(var(--bottom-nav-height, 0px) + 16px + 122px); transform: none; }
      .tour-widget-panel { top: auto; bottom: 0; transform: translateX(12px); }
      .tour-widget.open .tour-widget-panel { transform: translateX(0); }
    }
  `;
  document.head.appendChild(s);
}
