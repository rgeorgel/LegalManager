// Chat de suporte (Crisp) no portal logado: o balão flutuante fica escondido (atrapalhava o
// conteúdo e os botões do canto inferior direito) e o chat abre pelo botão "Suporte" do header.
// Ao fechar a janela, o Crisp volta a ficar escondido. Como o balão não aparece, o botão faz o
// papel dele: contador de mensagens não lidas e um preview da resposta do atendente.
//
// O Crisp guarda um único handler por evento — quem precisar saber quando o chat abre/fecha
// (ex.: o assistente de IA) assina aqui com onSuporte(), em vez de registrar no $crisp.
import { esc } from './utils.js';

const ouvintes = { aberto: [], fechado: [] };
let naoLidas = 0;

const STYLES = `
  #suporteBtn { position:relative; display:inline-flex; align-items:center; gap:6px; height:32px;
    padding:0 12px; border:1px solid var(--color-border); border-radius:999px; background:#fff;
    color:var(--color-text); font-size:13px; font-weight:500; cursor:pointer; white-space:nowrap;
    transition:background .15s, border-color .15s; }
  #suporteBtn:hover { background:var(--color-bg); border-color:#d1d5db; }
  #suporteBtn .suporte-ico { font-size:15px; line-height:1; }
  #suporteBadge { position:absolute; top:-6px; right:-6px; min-width:18px; height:18px; padding:0 5px;
    border-radius:999px; background:#dc2626; color:#fff; border:2px solid #fff; font-size:10px;
    font-weight:700; line-height:14px; text-align:center; box-sizing:border-box; }
  #suporteBadge[hidden] { display:none; }
  #suportePreview { position:fixed; top:calc(var(--header-height) + 8px); right:16px; z-index:1200;
    width:320px; max-width:calc(100vw - 32px); background:#fff; border:1px solid var(--color-border);
    border-radius:12px; box-shadow:0 12px 32px rgba(0,0,0,.16); padding:12px 14px; cursor:pointer;
    display:flex; gap:10px; align-items:flex-start; animation:suporte-entra .2s ease-out; }
  #suportePreview[hidden] { display:none; }
  #suportePreview:hover { border-color:#d1d5db; }
  @keyframes suporte-entra { from { opacity:0; transform:translateY(-6px); } to { opacity:1; transform:none; } }
  .suporte-prev-avatar { width:36px; height:36px; border-radius:50%; flex-shrink:0; object-fit:cover;
    background:var(--color-primary-light); color:var(--color-primary); display:flex; align-items:center;
    justify-content:center; font-weight:700; font-size:15px; }
  .suporte-prev-corpo { flex:1; min-width:0; }
  .suporte-prev-nome { font-size:13px; font-weight:600; color:var(--color-text); }
  .suporte-prev-nome small { font-weight:400; color:var(--color-text-muted); }
  .suporte-prev-texto { margin-top:2px; font-size:13px; color:var(--color-text); line-height:1.4;
    display:-webkit-box; -webkit-box-orient:vertical; -webkit-line-clamp:3; overflow:hidden; overflow-wrap:anywhere; }
  .suporte-prev-acao { margin-top:6px; font-size:12px; font-weight:600; color:var(--color-primary); }
  .suporte-prev-fechar { background:none; border:none; color:var(--color-text-muted); font-size:18px;
    line-height:1; cursor:pointer; padding:0 2px; margin:-4px -4px 0 0; }
  .suporte-prev-fechar:hover { color:var(--color-text); }
  @media (max-width: 768px) {
    #suporteBtn { padding:0 8px; }
    #suporteBtn .suporte-txt { display:none; }
    #suportePreview { right:12px; left:12px; width:auto; max-width:none; }
  }
`;

export function temSuporte() {
  return Array.isArray(window.$crisp) || typeof window.$crisp?.push === 'function';
}

function crisp(...cmd) {
  if (!temSuporte()) return;
  try { window.$crisp.push(cmd); } catch { /* Crisp indisponível: o botão só não faz nada */ }
}

function crispGet(chave) {
  try { return typeof window.$crisp?.get === 'function' ? window.$crisp.get(chave) : undefined; }
  catch { return undefined; }
}

function chatAberto() {
  try { return typeof window.$crisp?.is === 'function' && window.$crisp.is('chat:opened'); }
  catch { return false; }
}

function emitir(evento) {
  ouvintes[evento].forEach(fn => { try { fn(); } catch { /* ouvinte não derruba os outros */ } });
}

export function onSuporte(evento, fn) {
  ouvintes[evento]?.push(fn);
}

export function abrirSuporte() {
  atualizarNaoLidas(0);
  esconderPreview();
  crisp('do', 'chat:show');
  crisp('do', 'chat:open');
}

export function fecharSuporte() {
  crisp('do', 'chat:close');
  crisp('do', 'chat:hide');
  emitir('fechado');
}

function atualizarNaoLidas(n) {
  naoLidas = Math.max(0, n || 0);
  const badge = document.getElementById('suporteBadge');
  if (badge) {
    badge.hidden = naoLidas === 0;
    badge.textContent = naoLidas > 9 ? '9+' : String(naoLidas);
  }
  const btn = document.getElementById('suporteBtn');
  if (btn) {
    const titulo = naoLidas
      ? `Suporte — ${naoLidas} mensage${naoLidas === 1 ? 'm nova' : 'ns novas'}`
      : 'Falar com o suporte';
    btn.title = titulo;
    btn.setAttribute('aria-label', titulo);
  }
}

// Texto da mensagem do atendente: mensagens de texto vêm como string; anexos, áudio,
// botões etc. vêm como objeto — mostra um resumo genérico.
function textoDaMensagem(msg) {
  if (typeof msg?.content === 'string' && msg.content.trim()) return msg.content.trim();
  const tipos = { file: '📎 Enviou um arquivo', audio: '🎤 Enviou um áudio', animation: 'Enviou um GIF' };
  return tipos[msg?.type] || 'Enviou uma nova mensagem';
}

function mostrarPreview(msg) {
  const prev = document.getElementById('suportePreview');
  if (!prev) return;
  const nome = msg?.user?.nickname || 'Suporte';
  const avatar = msg?.user?.avatar;
  const extra = naoLidas > 1 ? ` · ${naoLidas} novas` : '';
  prev.innerHTML = `
    ${avatar
      ? `<img class="suporte-prev-avatar" src="${esc(avatar)}" alt="">`
      : `<div class="suporte-prev-avatar">${esc(nome.charAt(0).toUpperCase())}</div>`}
    <div class="suporte-prev-corpo">
      <div class="suporte-prev-nome">${esc(nome)} <small>· Suporte Causify${extra}</small></div>
      <div class="suporte-prev-texto">${esc(textoDaMensagem(msg))}</div>
      <div class="suporte-prev-acao">Responder</div>
    </div>
    <button type="button" class="suporte-prev-fechar" aria-label="Dispensar">×</button>`;
  prev.hidden = false;
}

function esconderPreview() {
  const prev = document.getElementById('suportePreview');
  if (prev) prev.hidden = true;
}

export function injectSuporteButton() {
  const headerUser = document.querySelector('.header-user');
  if (!temSuporte() || !headerUser || document.getElementById('suporteBtn')) return;

  if (!document.getElementById('suporteStyles')) {
    const s = document.createElement('style');
    s.id = 'suporteStyles';
    s.textContent = STYLES;
    document.head.appendChild(s);
  }

  const btn = document.createElement('button');
  btn.type = 'button';
  btn.id = 'suporteBtn';
  btn.innerHTML = '<span class="suporte-ico">💬</span><span class="suporte-txt">Suporte</span><span id="suporteBadge" hidden></span>';
  headerUser.insertBefore(btn, headerUser.firstChild);
  btn.addEventListener('click', e => { e.stopPropagation(); abrirSuporte(); });
  atualizarNaoLidas(0);

  const prev = document.createElement('div');
  prev.id = 'suportePreview';
  prev.hidden = true;
  prev.setAttribute('role', 'status');
  document.body.appendChild(prev);
  prev.addEventListener('click', e => {
    if (e.target.closest('.suporte-prev-fechar')) { esconderPreview(); return; }
    abrirSuporte();
  });

  crisp('do', 'chat:hide');
  crisp('on', 'chat:opened', () => { atualizarNaoLidas(0); esconderPreview(); emitir('aberto'); });
  crisp('on', 'chat:closed', () => { crisp('do', 'chat:hide'); emitir('fechado'); });
  // Com a janela fechada, a resposta do atendente aparece no contador do botão e num preview
  // abaixo do header (o que o balão do Crisp fazia). Prefere a contagem do próprio Crisp.
  crisp('on', 'message:received', msg => {
    if (chatAberto()) return;
    const doCrisp = Number(crispGet('chat:unread:count'));
    atualizarNaoLidas(doCrisp > naoLidas ? doCrisp : naoLidas + 1);
    mostrarPreview(msg);
  });
  crisp('on', 'session:loaded', () => {
    const doCrisp = Number(crispGet('chat:unread:count'));
    if (doCrisp > 0) atualizarNaoLidas(doCrisp);
  });
}
