// Editor de templates do Contratos dentro do Causify (embed). O servidor cria a sessão com a chave
// de API do escritório (POST /assinatura-digital/embed/sessoes) e devolve só a URL, que abre uma vez
// num iframe. A página do Contratos avisa por postMessage o que acontece (template salvo, publicado,
// arquivado; sessão vencida); conferimos a origem, a janela e a sessão antes de usar.
import { apiFetch } from './api.js';
import { esc } from './utils.js';

const API = '/assinatura-digital/embed/sessoes';
const MUDANCAS = ['template.created', 'template.saved', 'template.published', 'template.archived', 'template.unarchived'];
const MAX_RENOVACOES = 3;

let overlay = null;
let sessao = null;       // SessaoEmbedDto em uso
let opcoes = {};         // { templateId, aoMudar, aoFechar }
let renovacoes = 0;
let publicou = false;

/**
 * Abre o editor de templates.
 * @param {object} [o]
 * @param {string} [o.templateId]  Abre direto no editor deste template.
 * @param {(tipo: string, template: object) => void} [o.aoMudar]  Template criado, salvo, publicado ou arquivado.
 * @param {(houveMudanca: boolean) => void} [o.aoFechar]  Ao fechar; true se algo foi publicado ou arquivado.
 */
export async function abrirEditorTemplates(o = {}) {
  opcoes = o;
  renovacoes = 0;
  publicou = false;
  montar();
  overlay.classList.add('open');
  document.body.style.overflow = 'hidden';
  await iniciarSessao(o.templateId);
}

export const editorTemplatesAberto = () => !!overlay?.classList.contains('open');

function montar() {
  if (overlay) return;
  overlay = document.createElement('div');
  overlay.className = 'modal-overlay';
  overlay.id = 'ctrEmbed';
  overlay.innerHTML = `
    <div class="ctr-embed" role="dialog" aria-modal="true" aria-labelledby="ctrEmbedTitulo">
      <div class="ctr-embed-topo">
        <div>
          <h2 class="ctr-embed-titulo" id="ctrEmbedTitulo">Templates de contrato</h2>
          <div class="ctr-embed-sub" id="ctrEmbedSub">Contratos · assinatura eletrônica</div>
        </div>
        <button type="button" class="modal-close" data-ctr="fechar" aria-label="Fechar">&times;</button>
      </div>
      <div class="ctr-embed-corpo">
        <div class="ctr-embed-aviso" id="ctrEmbedAviso" role="status">Abrindo o editor…</div>
        <iframe id="ctrEmbedFrame" class="ctr-embed-frame" title="Editor de templates do Contratos" hidden
          referrerpolicy="strict-origin-when-cross-origin"
          sandbox="allow-scripts allow-same-origin allow-forms allow-popups allow-popups-to-escape-sandbox allow-downloads allow-modals"></iframe>
      </div>
    </div>`;
  document.body.appendChild(overlay);
  overlay.addEventListener('click', e => {
    if (e.target.closest('[data-ctr="fechar"]')) fechar();
    else if (e.target.closest('[data-ctr="tentar"]')) iniciarSessao(opcoes.templateId);
  });
  overlay.addEventListener('keydown', e => {
    e.stopPropagation();
    if (e.key === 'Escape') fechar();
  });
  window.addEventListener('message', aoReceber);
}

function aviso(html) {
  const el = overlay.querySelector('#ctrEmbedAviso');
  el.innerHTML = html;
  el.hidden = !html;
}

async function iniciarSessao(templateId) {
  const frame = overlay.querySelector('#ctrEmbedFrame');
  frame.hidden = true;
  frame.removeAttribute('src');
  aviso('Abrindo o editor…');
  encerrar();
  try {
    sessao = await apiFetch(API, { method: 'POST', body: { templateId: templateId || null } });
  } catch (err) {
    sessao = null;
    aviso(`<div class="alert alert-error" style="margin:0">${esc(err?.message || 'Não foi possível abrir o editor de templates.')}</div>
      <button type="button" class="btn btn-secondary btn-sm" data-ctr="tentar" style="margin-top:12px">Tentar de novo</button>`);
    return;
  }
  const pode = sessao.permissoes || [];
  overlay.querySelector('#ctrEmbedSub').textContent = pode.includes('publish')
    ? 'Você pode criar, editar e publicar templates.'
    : pode.includes('edit')
      ? 'Você pode criar e editar rascunhos; a publicação fica com um advogado ou administrador.'
      : 'Somente leitura.';
  frame.src = sessao.url;
  frame.hidden = false;
  // Se o "ready" não chegar (ex.: site não cadastrado no Contratos), o aviso continua visível.
  aviso('Carregando o editor do Contratos…');
}

function aoReceber(event) {
  if (!sessao || !overlay) return;
  const frame = overlay.querySelector('#ctrEmbedFrame');
  if (event.origin !== sessao.origem || event.source !== frame.contentWindow) return;
  const d = event.data;
  if (!d || d.source !== 'contratos' || (d.sessionId && d.sessionId !== sessao.id)) return;

  if (d.type === 'ready') {
    aviso('');
  } else if (d.type === 'session.expired') {
    if (renovacoes++ < MAX_RENOVACOES) iniciarSessao(opcoes.templateId);
    else aviso('<div class="alert alert-error" style="margin:0">A sessão do editor terminou. Feche e abra de novo.</div>');
  } else if (MUDANCAS.includes(d.type)) {
    if (d.type !== 'template.saved') publicou = true;
    if (d.template?.id) opcoes.templateId = d.template.id;
    try { opcoes.aoMudar?.(d.type, d.template || {}); } catch (e) { console.error(e); }
  }
}

/** Encerra a sessão no Contratos (sem esperar): o link do iframe deixa de funcionar. */
function encerrar() {
  if (!sessao?.id) return;
  const id = sessao.id;
  sessao = null;
  apiFetch(`${API}/${encodeURIComponent(id)}`, { method: 'DELETE' }).catch(() => {});
}

function fechar() {
  if (!overlay) return;
  overlay.classList.remove('open');
  document.body.style.overflow = '';
  const frame = overlay.querySelector('#ctrEmbedFrame');
  frame.removeAttribute('src');
  frame.hidden = true;
  encerrar();
  try { opcoes.aoFechar?.(publicou); } catch (e) { console.error(e); }
}
