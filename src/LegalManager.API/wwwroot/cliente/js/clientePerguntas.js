// Pesquisa de interesse/satisfação — versão do portal do cliente. Mesmo
// comportamento e HTML/CSS de /js/perguntas-widget.js (portal admin), só que
// falando com a mesma API compartilhada (/api/perguntas) através do token do
// portal do cliente (cliente_token), não do token do portal admin — por isso
// não reaproveita apiFetch de /js/api.js. Sem outros widgets flutuantes no
// portal do cliente, então não há empilhamento a calcular.
import { getToken, clearSession } from './clienteApi.js';

let cache = null;

async function perguntasFetch(path, options = {}) {
  const token = getToken();
  const headers = {
    'Content-Type': 'application/json',
    ...(token ? { Authorization: `Bearer ${token}` } : {}),
  };

  let body = options.body;
  if (body != null && typeof body === 'object') body = JSON.stringify(body);

  const res = await fetch(`/api${path}`, { ...options, headers, body });

  if (res.status === 401) {
    clearSession();
    window.location.href = '/cliente/index.html';
    throw new Error('Unauthorized');
  }
  if (!res.ok) {
    let msg = `HTTP ${res.status}`;
    try { const b = await res.json(); msg = b.message || b.title || msg; } catch { /* corpo sem JSON */ }
    throw new Error(msg);
  }
  if (res.status === 204) return null;
  return res.json();
}

export async function injectPerguntasWidget() {
  if (document.getElementById('perguntasWidget')) return;

  cache = await perguntasFetch('/perguntas/pendentes').catch(() => []);
  if (!cache || cache.length === 0) return;

  injectStyles();
  renderWidget();
}

// ── Widget flutuante ─────────────────────────────────────────────────────

function renderWidget() {
  if (document.getElementById('perguntasWidget')) return;

  const wrap = document.createElement('div');
  wrap.id = 'perguntasWidget';
  wrap.className = 'pw';
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
  body.innerHTML = cache.map(p => cardHtml(p)).join('');
  cache.forEach(p => {
    document.querySelector(`[data-send="w-${p.id}"]`)
      ?.addEventListener('click', () => submitCard(p.id));
  });
}

// ── Card de pergunta ─────────────────────────────────────────────────────

function cardHtml(p) {
  const prefix = `w-${p.id}`;
  return `
    <div class="pw-card" id="pw-card-${prefix}">
      <div class="pw-card-title">${esc(p.texto)}</div>
      ${p.descricao ? `<div class="pw-card-desc">${esc(p.descricao)}</div>` : ''}
      <div class="pw-card-input" data-input="${prefix}">
        ${optionsInputHtml(p, prefix)}
      </div>
      <div class="pw-card-error" data-error="${prefix}" hidden></div>
      <div class="pw-card-actions">
        <button type="button" class="pw-btn pw-btn-send" data-send="${prefix}">Enviar</button>
      </div>
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

async function submitCard(id) {
  const p = cache.find(x => x.id === id);
  if (!p) return;

  const prefix = `w-${id}`;
  const container = document.querySelector(`[data-input="${prefix}"]`);
  const errorEl = document.querySelector(`[data-error="${prefix}"]`);
  const sendBtn = document.querySelector(`[data-send="${prefix}"]`);

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
    await perguntasFetch(`/perguntas/${id}/responder`, { method: 'POST', body });
    cache = cache.filter(x => x.id !== id);
    renderPanelBody();
    renderBadge();

    if (cache.length === 0) {
      document.getElementById('perguntasWidget')?.remove();
      return;
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

// ── Estilos (auto-contidos, mesmo padrão do portal admin) ────────────────

function injectStyles() {
  if (document.getElementById('perguntasWidgetStyles')) return;
  const s = document.createElement('style');
  s.id = 'perguntasWidgetStyles';
  s.textContent = `
    .pw { position: fixed; top: 50%; right: 0; transform: translateY(-50%); z-index: 930; display: flex; align-items: center; }
    .pw-toggle {
      display: flex; flex-direction: column; align-items: center; gap: 8px; width: 34px;
      background: var(--color-sidebar, var(--color-nav)); color: var(--color-sidebar-text, #d1d9e6) !important; border: none; cursor: pointer;
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

    @media (max-width: 768px) {
      .pw { top: auto; bottom: 16px; transform: none; }
      .pw-panel { top: auto; bottom: 0; transform: translateX(12px); max-height: min(420px, calc(100vh - 96px)); }
      .pw.open .pw-panel { transform: translateX(0); }
    }
  `;
  document.head.appendChild(s);
}
