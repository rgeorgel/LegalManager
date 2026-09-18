// Pesquisa de interesse/satisfação — menu flutuante retrátil (mesmo padrão de
// tarefas-widget.js/tour.js: botão na lateral direita, empilhado abaixo dos outros
// widgets já presentes na página) + modal exibido uma vez por sessão logo após o
// login, com as perguntas pendentes cadastradas pelo super admin (ver
// SuperAdminPerguntasController). Se não houver nenhuma pergunta pendente para o
// usuário, o widget simplesmente não é inserido no DOM — nem o botão flutuante
// aparece.
import { apiFetch } from './api.js';
import { layoutWidgetStack } from './widget-stack.js';

const SESSION_MODAL_KEY = 'perguntasModalShownSession';

let cache = null; // Array de perguntas pendentes — mutado conforme o usuário responde
let modalIndex = 0;

export async function injectPerguntasWidget() {
  if (document.getElementById('perguntasWidget')) return;

  cache = await apiFetch('/perguntas/pendentes').catch(() => []);
  if (!cache || cache.length === 0) return;

  injectStyles();
  renderWidget();
  maybeShowLoginModal();
}

// ── Widget flutuante ─────────────────────────────────────────────────────

function renderWidget() {
  if (document.getElementById('perguntasWidget')) return;

  // Empilhamento no mobile continua one-way, de baixo pra cima (sem noção de
  // "centro" na tela pequena) — soma 1 "andar" por widget já presente.
  const before = ['tarefasWidget', 'tourWidget'].filter(id => document.getElementById(id)).length;

  const wrap = document.createElement('div');
  wrap.id = 'perguntasWidget';
  wrap.className = 'pw';
  wrap.style.setProperty('--pw-stack-mobile', `${before * 122}px`);
  wrap.innerHTML = `
    <button type="button" class="pw-toggle" id="pwToggle" title="Sua opinião importa">
      <span class="pw-toggle-icon" aria-hidden="true">💬</span>
      <span class="pw-toggle-label">Perguntas</span>
      <span class="pw-toggle-badge" id="pwBadge" hidden>0</span>
    </button>
    <div class="pw-panel" role="region" aria-label="Perguntas pendentes">
      <div class="pw-panel-header">
        <strong>Sua opinião importa</strong>
        <button type="button" class="pw-panel-close" id="pwClose" title="Recolher">✕</button>
      </div>
      <div class="pw-panel-body" id="pwBody"></div>
    </div>
  `;
  document.body.appendChild(wrap);
  layoutWidgetStack();

  document.getElementById('pwToggle').addEventListener('click', () => toggleWidget());
  document.getElementById('pwClose').addEventListener('click', () => toggleWidget(false));

  renderPanelBody();
  renderBadge();
}

function toggleWidget(force) {
  const wrap = document.getElementById('perguntasWidget');
  if (!wrap) return;
  const open = force !== undefined ? force : !wrap.classList.contains('open');
  wrap.classList.toggle('open', open);
}

function renderBadge() {
  const badge = document.getElementById('pwBadge');
  if (!badge) return;
  badge.hidden = cache.length === 0;
  badge.textContent = cache.length > 99 ? '99+' : String(cache.length);
}

function renderPanelBody() {
  const body = document.getElementById('pwBody');
  if (!body) return;
  body.innerHTML = cache.map(p => cardHtml(p, false)).join('');
  cache.forEach(p => {
    document.querySelector(`[data-send="w-${p.id}"]`)
      ?.addEventListener('click', () => submitCard(p.id, false));
  });
}

// ── Modal de login (uma pergunta por vez) ───────────────────────────────

function maybeShowLoginModal() {
  try {
    if (sessionStorage.getItem(SESSION_MODAL_KEY) === '1') return;
    sessionStorage.setItem(SESSION_MODAL_KEY, '1');
  } catch { /* sessionStorage indisponível — não bloqueia o restante do app */ }

  if (cache.length === 0) return;
  modalIndex = 0;
  ensureModalDom();
  renderModalStep();
  document.getElementById('pwModalOverlay').classList.add('open');
}

function closeModal() {
  document.getElementById('pwModalOverlay')?.classList.remove('open');
}

function ensureModalDom() {
  if (document.getElementById('pwModalOverlay')) return;

  const overlay = document.createElement('div');
  overlay.id = 'pwModalOverlay';
  overlay.className = 'pw-modal-overlay';
  overlay.innerHTML = `
    <div class="pw-modal">
      <div class="pw-modal-header">
        <strong>📢 Sua opinião importa</strong>
        <button type="button" class="pw-modal-close" id="pwModalClose">✕</button>
      </div>
      <div class="pw-modal-body" id="pwModalBody"></div>
      <div class="pw-modal-footer">
        <span class="pw-modal-counter" id="pwModalCounter"></span>
        <div class="pw-modal-actions">
          <button type="button" class="pw-btn pw-btn-secondary" id="pwModalSkip">Agora não</button>
          <button type="button" class="pw-btn pw-btn-send" id="pwModalSend">Enviar</button>
        </div>
      </div>
    </div>
  `;
  document.body.appendChild(overlay);

  overlay.addEventListener('click', e => { if (e.target === overlay) closeModal(); });
  document.getElementById('pwModalClose').addEventListener('click', closeModal);
  document.getElementById('pwModalSkip').addEventListener('click', () => closeModal());
  document.getElementById('pwModalSend').addEventListener('click', () => submitCard(currentModalPergunta()?.id, true));
}

function currentModalPergunta() {
  return cache[modalIndex] ?? null;
}

function renderModalStep() {
  const p = currentModalPergunta();
  if (!p) { closeModal(); return; }

  document.getElementById('pwModalCounter').textContent = `${modalIndex + 1} de ${cache.length}`;
  document.getElementById('pwModalBody').innerHTML = cardHtml(p, true, { hideActions: true });
}

// ── Card de pergunta (compartilhado entre widget e modal) ───────────────

function cardHtml(p, isModal, opts = {}) {
  const prefix = isModal ? 'm' : `w-${p.id}`;
  return `
    <div class="pw-card" id="pw-card-${prefix}">
      <div class="pw-card-title">${esc(p.texto)}</div>
      ${p.descricao ? `<div class="pw-card-desc">${esc(p.descricao)}</div>` : ''}
      <div class="pw-card-input" data-input="${prefix}">
        ${optionsInputHtml(p, prefix)}
      </div>
      <div class="pw-card-error" data-error="${prefix}" hidden></div>
      ${opts.hideActions ? '' : `
        <div class="pw-card-actions">
          <button type="button" class="pw-btn pw-btn-send" data-send="${prefix}">Enviar</button>
        </div>`}
    </div>`;
}

function optionsInputHtml(p, prefix) {
  if (p.tipoResposta === 'EscolhaUnica') {
    return p.opcoes.map(o => `
      <label class="pw-option">
        <input type="radio" name="pw-opt-${prefix}" value="${esc(o)}">
        <span>${esc(o)}</span>
      </label>`).join('');
  }
  if (p.tipoResposta === 'EscolhaMultipla') {
    return p.opcoes.map(o => `
      <label class="pw-option">
        <input type="checkbox" name="pw-opt-${prefix}" value="${esc(o)}">
        <span>${esc(o)}</span>
      </label>`).join('');
  }
  return `<textarea class="pw-textarea" data-textarea="${prefix}" placeholder="Escreva aqui..." rows="3"></textarea>`;
}

async function submitCard(id, isModal) {
  const p = cache.find(x => x.id === id);
  if (!p) return;

  const prefix = isModal ? 'm' : `w-${id}`;
  const container = document.querySelector(`[data-input="${prefix}"]`);
  const errorEl = document.querySelector(`[data-error="${prefix}"]`);
  const sendBtn = isModal
    ? document.getElementById('pwModalSend')
    : document.querySelector(`[data-send="${prefix}"]`);

  let body;
  if (p.tipoResposta === 'EscolhaUnica') {
    const checked = container?.querySelector('input[type=radio]:checked');
    if (!checked) return showCardError(errorEl, 'Selecione uma opção.');
    body = { opcaoEscolhida: checked.value };
  } else if (p.tipoResposta === 'EscolhaMultipla') {
    const checked = [...(container?.querySelectorAll('input[type=checkbox]:checked') ?? [])].map(el => el.value);
    if (checked.length === 0) return showCardError(errorEl, 'Selecione ao menos uma opção.');
    body = { opcoesEscolhidas: checked };
  } else {
    const texto = container?.querySelector('textarea')?.value.trim();
    if (!texto) return showCardError(errorEl, 'Escreva uma resposta.');
    body = { respostaTexto: texto };
  }

  if (sendBtn) sendBtn.disabled = true;
  if (errorEl) errorEl.hidden = true;

  try {
    await apiFetch(`/perguntas/${id}/responder`, { method: 'POST', body });
    cache = cache.filter(x => x.id !== id);
    renderPanelBody();
    renderBadge();

    if (cache.length === 0) {
      document.getElementById('perguntasWidget')?.remove();
      closeModal();
      layoutWidgetStack(); // recentraliza tarefas/tutorial sem o widget de perguntas
      return;
    }

    if (isModal) {
      // O item respondido saiu da lista — o próximo já ocupa este mesmo índice.
      if (modalIndex >= cache.length) modalIndex = cache.length - 1;
      renderModalStep();
    }
  } catch {
    showCardError(errorEl, 'Erro ao enviar. Tente novamente.');
    if (sendBtn) sendBtn.disabled = false;
  }
}

function showCardError(el, msg) {
  if (!el) return;
  el.textContent = msg;
  el.hidden = false;
}

function esc(str) {
  return (str ?? '').replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');
}

// ── Estilos (auto-contidos, mesmo padrão de tarefas-widget.js/tour.js) ──

function injectStyles() {
  if (document.getElementById('perguntasWidgetStyles')) return;
  const s = document.createElement('style');
  s.id = 'perguntasWidgetStyles';
  s.textContent = `
    .pw {
      position: fixed; top: 50%; right: 0; z-index: 930; display: flex; align-items: center;
      transform: translateY(-50%) translateY(var(--widget-offset, 0px));
    }
    .pw-toggle {
      display: flex; flex-direction: column; align-items: center; gap: 8px; width: 34px;
      background: var(--color-sidebar); color: var(--color-sidebar-text) !important; border: none; cursor: pointer;
      border-radius: 10px 0 0 10px; padding: 12px 7px; box-shadow: var(--shadow-md);
    }
    .pw-toggle:hover { filter: brightness(1.2); }
    .pw-toggle-icon { font-size: 15px; line-height: 1; }
    .pw-toggle-label {
      writing-mode: vertical-rl; transform: rotate(180deg);
      font-size: 11px; font-weight: 700; letter-spacing: .04em; line-height: 1;
    }
    .pw-toggle-badge {
      background: #dc2626; color: #fff; font-size: 10px; font-weight: 700;
      min-width: 16px; height: 16px; line-height: 16px; text-align: center; border-radius: 999px; padding: 0 4px;
    }
    .pw-panel {
      position: absolute; right: 100%; top: 50%; margin-right: 8px;
      transform: translateY(-50%) translateX(12px); opacity: 0; pointer-events: none;
      transition: transform .18s ease, opacity .18s ease;
      width: 320px; max-width: calc(100vw - 24px); max-height: min(520px, calc(100vh - 48px));
      background: var(--color-surface); border: 1px solid var(--color-border); border-radius: var(--radius);
      box-shadow: var(--shadow-md); display: flex; flex-direction: column; overflow: hidden;
    }
    .pw.open .pw-panel { transform: translateY(-50%) translateX(0); opacity: 1; pointer-events: auto; }
    .pw-panel-header {
      display: flex; align-items: center; justify-content: space-between; gap: 8px;
      padding: 10px 12px; border-bottom: 1px solid var(--color-border); font-size: 13px; color: var(--color-text);
    }
    .pw-panel-close { background: none; border: none; cursor: pointer; font-size: 13px; color: var(--color-text-muted) !important; padding: 4px; line-height: 1; }
    .pw-panel-close:hover { color: var(--color-text) !important; }
    .pw-panel-body { flex: 1; overflow-y: auto; padding: 10px 12px; display: flex; flex-direction: column; gap: 12px; }

    .pw-card-title { font-size: 13px; font-weight: 700; color: var(--color-text); }
    .pw-card-desc { font-size: 12px; color: var(--color-text-muted); margin-top: 3px; }
    .pw-card-input { margin-top: 10px; display: flex; flex-direction: column; gap: 6px; }
    .pw-option {
      display: flex; align-items: center; gap: 8px; font-size: 13px; color: var(--color-text);
      border: 1px solid var(--color-border); border-radius: 6px; padding: 7px 10px; cursor: pointer;
    }
    .pw-option:hover { background: var(--color-bg); }
    .pw-textarea {
      width: 100%; font: inherit; font-size: 13px; color: var(--color-text); background: var(--color-bg);
      border: 1px solid var(--color-border); border-radius: 6px; padding: 8px 10px; resize: vertical;
    }
    .pw-textarea:focus { outline: none; border-color: var(--color-primary); }
    .pw-card-error { color: var(--color-danger); font-size: 11px; margin-top: 6px; }
    .pw-card-actions { margin-top: 10px; text-align: right; }
    .pw-btn {
      font-size: 12px; font-weight: 600; border-radius: 6px; padding: 6px 14px; cursor: pointer; border: 1px solid transparent;
    }
    .pw-btn:disabled { opacity: .6; cursor: default; }
    .pw-btn-send { background: var(--color-primary); color: #fff !important; }
    .pw-btn-send:hover:not(:disabled) { filter: brightness(1.08); }
    .pw-btn-secondary { background: none; border-color: var(--color-border); color: var(--color-text-muted) !important; }
    .pw-btn-secondary:hover { background: var(--color-bg); }

    .pw-card + .pw-card { border-top: 1px solid var(--color-border); padding-top: 12px; }

    /* Modal de login — auto-contido (não depende de .modal-overlay/.modal globais). */
    .pw-modal-overlay {
      display: none; position: fixed; inset: 0; z-index: 10600; background: rgba(15,23,42,.55);
      align-items: center; justify-content: center; padding: 16px;
    }
    .pw-modal-overlay.open { display: flex; }
    .pw-modal {
      width: 420px; max-width: 100%; max-height: calc(100vh - 32px); overflow-y: auto;
      background: var(--color-surface); border-radius: var(--radius); box-shadow: var(--shadow-md);
      display: flex; flex-direction: column;
    }
    .pw-modal-header {
      display: flex; align-items: center; justify-content: space-between; gap: 8px;
      padding: 16px 18px; border-bottom: 1px solid var(--color-border); font-size: 15px; color: var(--color-text);
    }
    .pw-modal-close { background: none; border: none; cursor: pointer; font-size: 15px; color: var(--color-text-muted); padding: 2px; line-height: 1; }
    .pw-modal-close:hover { color: var(--color-text); }
    .pw-modal-body { padding: 18px; }
    .pw-modal-body .pw-card-title { font-size: 15px; }
    .pw-modal-body .pw-card-desc { font-size: 13px; }
    .pw-modal-footer {
      display: flex; align-items: center; justify-content: space-between; gap: 12px;
      padding: 14px 18px; border-top: 1px solid var(--color-border);
    }
    .pw-modal-counter { font-size: 12px; color: var(--color-text-muted); white-space: nowrap; }
    .pw-modal-actions { display: flex; gap: 8px; }

    @media (max-width: 768px) {
      .pw { top: auto; bottom: calc(var(--bottom-nav-height, 0px) + 16px + var(--pw-stack-mobile, 0px)); transform: none; }
      .pw-panel { top: auto; bottom: 0; transform: translateX(12px); max-height: min(420px, calc(100vh - var(--bottom-nav-height, 0px) - 96px)); }
      .pw.open .pw-panel { transform: translateX(0); }
    }
  `;
  document.head.appendChild(s);
}
