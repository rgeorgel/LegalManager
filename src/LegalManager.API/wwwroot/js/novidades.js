// Painel "Novidades" (✨) do cabeçalho do portal admin: lista as novidades do produto
// cadastradas pelo super admin (superadmin/novidades.html). O selo conta as publicadas
// depois da última vez que o usuário abriu o painel; abrir zera o contador
// (POST /novidades/visto). Ver NovidadesController.
//
// Novidades com TourId ganham o botão "Me mostra", que abre um tour curto da
// funcionalidade (tours `oculto` em tours-config.js). As marcadas como destaque também
// aparecem uma vez num modal no dashboard (mostrarDestaqueNovidade).
import { apiFetch } from './api.js';
import { trackEvent } from './analytics.js';
import { renderMarkdown } from './markdown.js';
import { startTour } from './tour.js';
import { TOURS } from './tours-config.js';
import { esc } from './utils.js';

const temTour = id => !!id && TOURS.some(t => t.id === id);

const STYLES = `
  #novidadesBtn { position:relative; cursor:pointer; padding:6px; display:flex; align-items:center;
    user-select:none; background:none; border:none; font-size:18px; line-height:1; }
  #novidadesBadge { position:absolute; top:2px; right:0; background:#f59e0b; color:#0f172a;
    border-radius:999px; font-size:10px; font-weight:700; min-width:16px; height:16px;
    line-height:16px; text-align:center; padding:0 4px; }
  #novidadesOverlay { position:fixed; inset:0; z-index:10001; background:rgba(0,0,0,.35);
    opacity:0; transition:opacity .2s; }
  #novidadesOverlay.open { opacity:1; }
  #novidadesPanel { position:fixed; top:0; right:0; bottom:0; z-index:10002; width:400px;
    max-width:100vw; background:var(--color-surface, #fff); color:var(--color-text, #111827);
    box-shadow:-8px 0 24px rgba(0,0,0,.15); display:flex; flex-direction:column;
    transform:translateX(100%); transition:transform .25s ease; }
  #novidadesPanel.open { transform:none; }
  #novidadesPanel[hidden] { display:none; }
  .novidades-head { display:flex; align-items:center; justify-content:space-between;
    padding:16px 18px; border-bottom:1px solid var(--color-border, #e5e7eb); }
  .novidades-head strong { font-size:16px; }
  .novidades-close { background:none; border:none; font-size:18px; cursor:pointer;
    color:var(--color-text-muted, #6b7280); padding:4px 8px; }
  .novidades-list { overflow-y:auto; flex:1; padding:4px 0 16px; }
  .novidade { padding:16px 18px; border-bottom:1px solid var(--color-border, #e5e7eb); }
  .novidade-meta { display:flex; align-items:center; gap:6px; font-size:11px;
    color:var(--color-text-muted, #6b7280); margin-bottom:4px; }
  .novidade-tag { font-size:10px; font-weight:600; padding:1px 6px; border-radius:100px;
    letter-spacing:.04em; text-transform:uppercase; }
  .novidade-tag.nova { background:var(--color-primary, #1a56db); color:#fff; }
  .novidade-tag.plano { background:#f59e0b; color:#0f172a; }
  .novidade-titulo { font-size:14px; font-weight:600; margin:0 0 6px; }
  .novidade-img { display:block; width:100%; border-radius:8px; margin:8px 0;
    border:1px solid var(--color-border, #e5e7eb); }
  .novidade-desc { font-size:13px; line-height:1.5; }
  .novidade-desc p { margin:0 0 8px; }
  .novidade-desc ul, .novidade-desc ol { margin:0 0 8px; padding-left:20px; }
  .novidade-acoes { display:flex; align-items:center; gap:14px; flex-wrap:wrap; margin-top:4px; }
  .novidade-link { font-size:13px; font-weight:600; }
  .novidade-tour { background:none; border:none; padding:0; cursor:pointer; font-size:13px;
    font-weight:600; color:var(--color-primary, #1a56db); }
  #novidadeDestaqueOverlay .modal { max-width:480px; }
  #novidadeDestaqueOverlay .novidade-desc { font-size:14px; }
  .novidade-destaque-rotulo { font-size:11px; font-weight:700; letter-spacing:.06em; text-transform:uppercase;
    color:var(--color-primary, #1a56db); margin-bottom:4px; }
  .novidades-vazio { padding:32px 18px; text-align:center; font-size:13px;
    color:var(--color-text-muted, #6b7280); }
`;

const fmtData = iso => new Date(iso).toLocaleDateString('pt-BR', { day: '2-digit', month: 'short', year: 'numeric' });

export function injectNovidadesButton() {
  const headerUser = document.querySelector('.header-user');
  if (!headerUser || document.getElementById('novidadesBtn')) return;

  if (!document.getElementById('novidadesStyles')) {
    const s = document.createElement('style');
    s.id = 'novidadesStyles';
    s.textContent = STYLES;
    document.head.appendChild(s);
  }

  const btn = document.createElement('button');
  btn.type = 'button';
  btn.id = 'novidadesBtn';
  btn.title = 'Novidades';
  btn.setAttribute('aria-label', 'Novidades');
  btn.innerHTML = '<span>✨</span><span id="novidadesBadge" hidden></span>';
  headerUser.insertBefore(btn, headerUser.firstChild);
  btn.addEventListener('click', e => { e.stopPropagation(); abrirPainel(); });

  carregarContador();
}

async function carregarContador() {
  try {
    atualizarSelo(await apiFetch('/novidades/count'));
  } catch { /* silencioso: o painel é acessório */ }
}

function atualizarSelo(count) {
  const badge = document.getElementById('novidadesBadge');
  if (!badge) return;
  badge.hidden = !(count > 0);
  badge.textContent = count > 9 ? '9+' : String(count);
}

function garantirPainel() {
  let panel = document.getElementById('novidadesPanel');
  if (panel) return panel;

  const overlay = document.createElement('div');
  overlay.id = 'novidadesOverlay';
  overlay.hidden = true;
  overlay.addEventListener('click', fecharPainel);

  panel = document.createElement('aside');
  panel.id = 'novidadesPanel';
  panel.hidden = true;
  panel.setAttribute('role', 'dialog');
  panel.setAttribute('aria-label', 'Novidades');
  panel.innerHTML = `
    <div class="novidades-head">
      <strong>✨ Novidades</strong>
      <button type="button" class="novidades-close" aria-label="Fechar">✕</button>
    </div>
    <div class="novidades-list" id="novidadesList"></div>`;
  panel.querySelector('.novidades-close').addEventListener('click', fecharPainel);
  panel.addEventListener('click', e => {
    const link = e.target.closest('a.novidade-link');
    if (link) trackEvent('novidade_link_clicado', { novidade_id: link.dataset.id, url: link.getAttribute('href') });
    const tour = e.target.closest('button.novidade-tour');
    if (tour) {
      trackEvent('novidade_tour_iniciado', { novidade_id: tour.dataset.id, tour_id: tour.dataset.tour, origem: 'painel' });
      fecharPainel();
      startTour(tour.dataset.tour);
    }
  });

  document.body.append(overlay, panel);
  document.addEventListener('keydown', e => { if (e.key === 'Escape') fecharPainel(); });
  return panel;
}

async function abrirPainel() {
  const panel = garantirPainel();
  const overlay = document.getElementById('novidadesOverlay');
  const list = document.getElementById('novidadesList');
  list.innerHTML = '<div class="novidades-vazio">Carregando...</div>';

  overlay.hidden = false;
  panel.hidden = false;
  requestAnimationFrame(() => { overlay.classList.add('open'); panel.classList.add('open'); });

  let itens;
  try {
    itens = await apiFetch('/novidades');
  } catch {
    list.innerHTML = '<div class="novidades-vazio" style="color:var(--color-danger, #e02424)">Erro ao carregar as novidades.</div>';
    return;
  }

  list.innerHTML = itens.length === 0
    ? '<div class="novidades-vazio">Nenhuma novidade por enquanto.</div>'
    : itens.map(renderItem).join('');

  const naoLidas = itens.filter(n => n.nova).length;
  trackEvent('novidades_abertas', { nao_lidas: naoLidas });
  if (naoLidas > 0 || !document.getElementById('novidadesBadge')?.hidden) {
    atualizarSelo(0);
    apiFetch('/novidades/visto', { method: 'POST' }).catch(() => {});
  }
}

function renderItem(n) {
  const tags = [
    n.nova ? '<span class="novidade-tag nova">Novo</span>' : '',
    n.planoMinimo && n.planoMinimo !== 'Free' ? `<span class="novidade-tag plano">${esc(n.planoMinimo)}</span>` : '',
  ].join('');
  return `
    <article class="novidade">
      <div class="novidade-meta">${tags}<span>${fmtData(n.publicadaEm)}</span></div>
      <h3 class="novidade-titulo">${esc(n.titulo)}</h3>
      ${n.imagemUrl ? `<img class="novidade-img" src="${esc(n.imagemUrl)}" alt="" loading="lazy">` : ''}
      <div class="novidade-desc">${renderMarkdown(n.descricao)}</div>
      <div class="novidade-acoes">
        ${temTour(n.tourId) ? `<button type="button" class="novidade-tour" data-id="${esc(n.id)}" data-tour="${esc(n.tourId)}">▶ Me mostra</button>` : ''}
        ${n.linkUrl ? `<a class="novidade-link" data-id="${esc(n.id)}" href="${esc(n.linkUrl)}">${esc(n.linkTexto || 'Experimentar')} →</a>` : ''}
      </div>
    </article>`;
}

function fecharPainel() {
  const panel = document.getElementById('novidadesPanel');
  const overlay = document.getElementById('novidadesOverlay');
  if (!panel || panel.hidden) return;
  panel.classList.remove('open');
  overlay.classList.remove('open');
  setTimeout(() => { panel.hidden = true; overlay.hidden = true; }, 250);
}

// ── Modal da novidade em destaque ─────────────────────────────────────────

/**
 * Mostra, uma vez, a novidade em destaque que o usuário ainda não viu (GET /novidades/destaque).
 * Chamado pelo dashboard no fim da fila de modais de entrada (trial → onboarding → este),
 * para não empilhar com eles. Não aparece com um tour em andamento.
 */
export async function mostrarDestaqueNovidade() {
  try {
    if (sessionStorage.getItem('tour_active')) return;
  } catch { /* storage bloqueado: segue */ }
  if (document.getElementById('novidadeDestaqueOverlay')) return;

  let n;
  try {
    n = await apiFetch('/novidades/destaque');
  } catch { return; }
  if (!n?.id) return;

  if (!document.getElementById('novidadesStyles')) {
    const s = document.createElement('style');
    s.id = 'novidadesStyles';
    s.textContent = STYLES;
    document.head.appendChild(s);
  }

  const tour = temTour(n.tourId);
  const overlay = document.createElement('div');
  overlay.id = 'novidadeDestaqueOverlay';
  overlay.className = 'modal-overlay open';
  overlay.innerHTML = `
    <div class="modal" role="dialog" aria-modal="true" aria-labelledby="novidadeDestaqueTitulo">
      <div class="modal-header">
        <div>
          <div class="novidade-destaque-rotulo">✨ Novidade</div>
          <span class="modal-title" id="novidadeDestaqueTitulo">${esc(n.titulo)}</span>
        </div>
        <button type="button" class="modal-close" data-acao="fechar" aria-label="Fechar">✕</button>
      </div>
      <div class="modal-body">
        ${n.imagemUrl ? `<img class="novidade-img" src="${esc(n.imagemUrl)}" alt="">` : ''}
        <div class="novidade-desc">${renderMarkdown(n.descricao)}</div>
      </div>
      <div class="modal-footer">
        <button type="button" class="btn btn-secondary" data-acao="depois">Depois</button>
        ${tour
          ? '<button type="button" class="btn btn-primary" data-acao="tour">▶ Me mostra</button>'
          : n.linkUrl
            ? `<a class="btn btn-primary" data-acao="link" href="${esc(n.linkUrl)}">${esc(n.linkTexto || 'Experimentar')}</a>`
            : '<button type="button" class="btn btn-primary" data-acao="fechar">Entendi</button>'}
      </div>
    </div>`;
  document.body.appendChild(overlay);
  trackEvent('novidade_destaque_exibido', { novidade_id: n.id });

  let fechado = false;
  const fechar = acao => {
    if (fechado) return Promise.resolve();
    fechado = true;
    overlay.remove();
    document.removeEventListener('keydown', onKey);
    trackEvent('novidade_destaque_fechado', { novidade_id: n.id, acao });
    return apiFetch('/novidades/destaque/visto', { method: 'POST' }).catch(() => {});
  };
  const onKey = e => { if (e.key === 'Escape') fechar('esc'); };
  document.addEventListener('keydown', onKey);

  overlay.addEventListener('click', e => {
    if (e.target === overlay) return fechar('fora');
    const alvo = e.target.closest('[data-acao]');
    if (!alvo) return;
    const acao = alvo.dataset.acao;
    // Link e tour podem trocar de página: grava o "visto" antes, senão a navegação
    // cancela a requisição e o modal volta na próxima visita ao dashboard.
    if (acao === 'link') {
      e.preventDefault();
      fechar(acao).then(() => { window.location.href = alvo.getAttribute('href'); });
    } else if (acao === 'tour') {
      trackEvent('novidade_tour_iniciado', { novidade_id: n.id, tour_id: n.tourId, origem: 'destaque' });
      fechar(acao).then(() => startTour(n.tourId));
    } else {
      fechar(acao);
    }
  });
}
