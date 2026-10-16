// Pesquisa de interesse/satisfação — menu flutuante retrátil (mesmo padrão de
// tarefas-widget.js/tour.js: botão na lateral direita, empilhado abaixo dos outros
// widgets já presentes na página), com as perguntas pendentes cadastradas pelo
// super admin (ver SuperAdminPerguntasController). Se não houver nenhuma pergunta
// pendente para o usuário, o widget simplesmente não é inserido no DOM — nem o
// botão flutuante aparece. As perguntas só aparecem quando o usuário clica no
// botão flutuante para abrir o painel.
import { apiFetch } from './api.js';
import { layoutWidgetStack } from './widget-stack.js';
import { getMascote } from './mascotes.js';

import { esc } from './utils.js';
let cache = null; // Array de perguntas pendentes — mutado conforme o usuário responde

export async function injectPerguntasWidget() {
  if (document.getElementById('perguntasWidget')) return;

  cache = await apiFetch('/perguntas/pendentes').catch(() => []);
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
  layoutWidgetStack();

  document.getElementById('pwToggle').addEventListener('click', () => toggleWidget());
  document.getElementById('pwClose').addEventListener('click', () => toggleWidget(false));

  renderPanelBody();
  renderBadge();
  agendarPuxao();
}

function toggleWidget(force) {
  const wrap = document.getElementById('perguntasWidget');
  if (!wrap) return;
  const open = force !== undefined ? force : !wrap.classList.contains('open');
  wrap.classList.toggle('open', open);
  if (open) pararPuxao();
}

// ── Mascote puxando o menu ───────────────────────────────────────────────
// De tempos em tempos, com o menu fechado, o mascote do assistente de IA aparece ao lado do
// botão de perguntas, aponta para ele e o puxa algumas vezes, como quem quer abri-lo. O desenho
// é o mesmo da aba do assistente (já com a escolha do usuário); sem assistente na página
// (ex.: portal do cliente), não há mascote e nada acontece. Para de vez quando o usuário abre o
// menu nesta página ou depois de PUXAO_MAX vezes.
const PUXAO_PRIMEIRO_MS = 15000;
const PUXAO_INTERVALO_MS = 45000;
const PUXAO_DURACAO_MS = 4400; // casa com as animações .pw-puxa-* em injectStyles
const PUXAO_MAX = 4;
let puxaoTimer = null;
let puxoes = 0;

function agendarPuxao(ms = PUXAO_PRIMEIRO_MS) {
  clearTimeout(puxaoTimer);
  if (puxoes >= PUXAO_MAX || matchMedia('(prefers-reduced-motion: reduce)').matches) return;
  puxaoTimer = setTimeout(() => {
    if (podePuxar()) { puxoes++; puxarMenu(); }
    agendarPuxao(PUXAO_INTERVALO_MS);
  }, ms);
}

function pararPuxao() {
  clearTimeout(puxaoTimer);
  puxoes = PUXAO_MAX;
  removerPuxador();
}

// Só com o botão de perguntas na tela e nada aberto por cima: painel do assistente, balão de
// convite do mascote ou outro menu lateral.
function podePuxar() {
  const toggle = document.getElementById('pwToggle');
  if (document.hidden || !toggle || toggle.getBoundingClientRect().width === 0) return false;
  if (document.getElementById('perguntasWidget')?.classList.contains('open')) return false;
  if (document.querySelector('#tarefasWidget.open, #tourWidget.open, .asx-chamando')) return false;
  const painelIA = document.getElementById('asxPanel');
  if (painelIA && !painelIA.hidden) return false;
  return !!document.querySelector('#assistenteIA .asx-fab-mascote svg');
}

function puxarMenu() {
  const wrap = document.getElementById('perguntasWidget');
  const toggle = document.getElementById('pwToggle');
  const fab = document.querySelector('#assistenteIA .asx-fab-mascote');
  if (!wrap || !toggle || !fab) return;
  const mascote = getMascote(fab.dataset.mascote);
  removerPuxador();

  const r = toggle.getBoundingClientRect();
  const el = document.createElement('button');
  el.type = 'button';
  el.id = 'pwPuxador';
  el.className = 'pw-puxa';
  el.title = 'Responder as perguntas';
  el.setAttribute('aria-label', 'Responder as perguntas');
  // Fica à esquerda do botão, centrado na altura dele; o braço cobre o espaço até a borda.
  el.style.left = `${r.left - 66}px`;
  el.style.top = `${r.top + r.height / 2 - 26}px`;
  el.innerHTML = `
    <span class="pw-puxa-balao">Tem perguntas pra você!</span>
    <span class="pw-puxa-corpo">
      <span class="pw-puxa-braco" aria-hidden="true">${mascote.braco}</span>
      <span class="pw-puxa-mascote" aria-hidden="true">${mascote.svg}</span>
    </span>`;
  el.addEventListener('click', () => toggleWidget(true));
  document.body.appendChild(el);
  wrap.classList.add('pw-puxado');
  el._fim = setTimeout(removerPuxador, PUXAO_DURACAO_MS);
}

function removerPuxador() {
  const el = document.getElementById('pwPuxador');
  if (el) { clearTimeout(el._fim); el.remove(); }
  document.getElementById('perguntasWidget')?.classList.remove('pw-puxado');
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
    await apiFetch(`/perguntas/${id}/responder`, { method: 'POST', body });
    cache = cache.filter(x => x.id !== id);
    renderPanelBody();
    renderBadge();

    if (cache.length === 0) {
      pararPuxao();
      document.getElementById('perguntasWidget')?.remove();
      layoutWidgetStack(); // recentraliza tarefas/tutorial sem o widget de perguntas
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

    /* Mascote puxando o menu (ver puxarMenu). Linha do tempo, em 4.4s: entra por trás do botão
       (0–.5s), aponta com o braço (.5–1.8s), dá três puxões (1.8–3.6s) e volta (3.6–4.4s). O
       botão recua junto com cada puxão (.pw-puxado). */
    .pw-puxa {
      position: fixed; z-index: 931; width: 52px; height: 52px; padding: 0;
      background: none; border: none; cursor: pointer; color: var(--color-text);
    }
    .pw-puxa-corpo {
      position: absolute; inset: 0; display: block;
      animation: pw-puxa-entra-sai 4.4s ease-in-out both, pw-puxa-puxoes .6s ease-in-out 1.8s 3;
    }
    .pw-puxa-mascote {
      position: relative; display: block; width: 100%; height: 100%; filter: drop-shadow(0 2px 3px rgba(0,0,0,.25));
      transform-origin: 50% 100%; animation: pw-puxa-inclina .6s ease-in-out 1.8s 3;
    }
    /* Braço desenhado por mascote (MASCOTES[].braco): atrás do corpo, com o ombro escondido nele
       e a ponta encostando na borda do botão. Aponta girando no ombro; nos puxões, estica. */
    .pw-puxa-braco {
      position: absolute; left: 34px; top: 28px; width: 34px; height: 17px; display: block;
      filter: drop-shadow(0 1px 1.5px rgba(0,0,0,.2)); transform-origin: 0 50%;
      animation: pw-puxa-aponta .43s ease-in-out .5s 3, pw-puxa-estica .6s ease-in-out 1.8s 3;
    }
    .pw-puxa-balao {
      position: absolute; bottom: calc(100% + 6px); right: -6px; white-space: nowrap;
      padding: 4px 9px; border-radius: 10px; font-size: 11.5px; font-weight: 600; line-height: 1.3;
      background: var(--color-surface); color: var(--color-text); border: 1px solid var(--color-border);
      box-shadow: 0 4px 12px rgba(0,0,0,.14); animation: pw-puxa-balao 4.4s ease-out both;
    }
    .pw-puxado .pw-toggle { animation: pw-puxa-botao .6s ease-in-out 1.8s 3; }
    @keyframes pw-puxa-entra-sai {
      0% { opacity: 0; transform: translateX(40px) scale(.6); }
      11%, 82% { opacity: 1; transform: none; }
      100% { opacity: 0; transform: translateX(40px) scale(.6); }
    }
    @keyframes pw-puxa-aponta { 0%, 100% { transform: rotate(0); } 50% { transform: rotate(-16deg) translateX(-3px); } }
    @keyframes pw-puxa-estica { 0%, 100% { transform: scaleX(1); } 45% { transform: scaleX(1.1) rotate(2deg); } }
    @keyframes pw-puxa-puxoes { 0%, 100% { transform: translateX(0); } 45% { transform: translateX(-9px); } }
    @keyframes pw-puxa-inclina { 0%, 100% { transform: rotate(0); } 45% { transform: rotate(-12deg); } }
    @keyframes pw-puxa-botao { 0%, 100% { transform: translateX(0); } 45% { transform: translateX(-7px); } }
    @keyframes pw-puxa-balao {
      0%, 8% { opacity: 0; transform: translateY(4px); }
      14%, 80% { opacity: 1; transform: none; }
      90%, 100% { opacity: 0; }
    }
    @media (prefers-reduced-motion: reduce) { .pw-puxa { display: none; } }

    @media (max-width: 768px) {
      .pw { top: auto; bottom: calc(var(--bottom-nav-height, 0px) + 16px + var(--widget-bottom-mobile, 0px)); transform: none; }
      .pw-panel { top: auto; bottom: 0; transform: translateX(12px); max-height: min(420px, calc(100vh - var(--bottom-nav-height, 0px) - 96px)); }
      .pw.open .pw-panel { transform: translateX(0); }
    }
  `;
  document.head.appendChild(s);
}
