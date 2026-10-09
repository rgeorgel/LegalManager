// Assistente de IA — chat flutuante (canto inferior direito) que responde perguntas sobre os
// dados do escritório (POST /api/assistente/perguntar). Visível para todos os usuários do
// escritório (não para o portal do cliente); uso a partir do plano Plus (o backend também
// exige) — no Free o painel abre com uma conversa de exemplo e um convite para o Plus.
// O histórico fica no sessionStorage, por usuário (sobrevive à navegação entre páginas e a
// minimizar/restaurar o painel; some ao fechar a aba). O painel pode ser maximizado para
// respostas grandes (tabelas).
// Cada pergunta/resposta é registrada no servidor (auditoria do Super Admin → Assistente IA),
// agrupada pelo id da conversa.
import { apiFetch } from './api.js';
import { renderMarkdown } from './markdown.js';
import { esc } from './utils.js';
import { trackEvent } from './analytics.js';

const HIST_KEY = 'assistente_historico';
const OPEN_KEY = 'assistente_aberto';
const CONVERSA_KEY = 'assistente_conversa';
const MAXIMIZADO_KEY = 'assistente_maximizado';
const MAX_HIST = 30; // mensagens reenviadas ao servidor (o backend aceita até 40)

const SUGESTOES = [
  'Quais prazos vencem esta semana?',
  'Tenho tarefas atrasadas?',
  'Quais audiências tenho nos próximos 15 dias?',
  'Resumo financeiro do mês',
];

const ROTULOS_FERRAMENTA = {
  buscar_processos: 'processos',
  detalhar_processo: 'processo',
  listar_tarefas: 'tarefas',
  listar_eventos: 'agenda',
  buscar_contatos: 'contatos',
  detalhar_contato: 'contato',
  listar_equipe: 'equipe',
  listar_registros_tempo: 'timesheet',
  resumo_financeiro: 'financeiro',
  listar_lancamentos: 'financeiro',
  listar_contratos_honorario: 'honorários',
  detalhar_contrato_honorario: 'honorários',
  listar_publicacoes: 'publicações',
};

let historico = []; // [{ papel: 'user'|'assistant', conteudo, ferramentas? }]
let enviando = false;
let naoLida = false; // resposta chegou com o painel minimizado
let bloqueado = false; // plano Free: mostra o convite para o Plus em vez do chat
let modo = 'chat'; // 'chat' | 'historico' (lista de conversas anteriores)
let conversas = null; // cache da lista de conversas (recarregada ao abrir o histórico)
// Chaves do sessionStorage por usuário: se outra pessoa fizer login na mesma aba, não vê a
// conversa de quem saiu.
let sufixo = '';
const chave = (k) => `${k}:${sufixo}`;

export function assistenteHabilitado(user) {
  return !!user && user.perfil !== 'Cliente';
}

export function injectAssistente(user) {
  if (!assistenteHabilitado(user) || document.getElementById('assistenteIA')) return;
  sufixo = `${user.tenantId ?? ''}:${user.id ?? ''}`;
  bloqueado = (user.plano ?? 'Free') === 'Free';
  historico = bloqueado ? [] : carregarHistorico();
  injectStyles();

  const wrap = document.createElement('div');
  wrap.id = 'assistenteIA';
  wrap.className = bloqueado ? 'asx asx-bloqueado' : 'asx';
  wrap.innerHTML = `
    <button type="button" class="asx-fab" id="asxFab" title="Assistente de IA" aria-expanded="false" aria-controls="asxPanel">
      <span aria-hidden="true">✨</span><span class="asx-fab-label">Assistente</span>
      <span class="asx-fab-dot" id="asxDot" hidden title="Nova resposta"></span>
    </button>
    <section class="asx-panel" id="asxPanel" role="dialog" aria-label="Assistente de IA" hidden>
      <header class="asx-header">
        <div>
          <strong>Assistente</strong> <span class="asx-beta">beta</span>
          <div class="asx-sub">Pergunte sobre processos, prazos, agenda, contatos e mais</div>
        </div>
        <div class="asx-header-actions">
          <button type="button" class="asx-icon-btn" id="asxHist" title="Conversas anteriores" aria-label="Conversas anteriores" aria-pressed="false">🕘</button>
          <button type="button" class="asx-icon-btn" id="asxNova" title="Nova conversa" aria-label="Nova conversa">↺</button>
          <button type="button" class="asx-icon-btn asx-btn-max" id="asxMax" title="Maximizar" aria-label="Maximizar" aria-pressed="false">⤢</button>
          <button type="button" class="asx-icon-btn" id="asxMin" title="Minimizar (a conversa continua)" aria-label="Minimizar">—</button>
        </div>
      </header>
      <div class="asx-body" id="asxBody" aria-live="polite"></div>
      <form class="asx-form" id="asxForm">
        <textarea id="asxInput" rows="1" maxlength="4000" placeholder="Digite sua pergunta…" aria-label="Pergunta"></textarea>
        <button type="submit" class="asx-send" id="asxSend" title="Enviar">➤</button>
      </form>
      ${bloqueado ? `
      <div class="asx-paywall" role="region" aria-labelledby="asxPaywallTitulo">
        <div class="asx-paywall-card">
          <div class="asx-paywall-icone" aria-hidden="true">✨</div>
          <div class="asx-paywall-selo">Plano Plus</div>
          <h3 id="asxPaywallTitulo">Seu assistente para o dia a dia do escritório</h3>
          <p>Pergunte em linguagem natural e receba as respostas com os dados do seu escritório:</p>
          <ul>
            <li>Prazos e tarefas da semana, atrasados e por responsável</li>
            <li>Audiências e compromissos da agenda</li>
            <li>Clientes em atraso, honorários e financeiro</li>
            <li>Resumo de processos e contatos, com links diretos</li>
          </ul>
          <a class="asx-paywall-cta" id="asxPaywallCta" href="/pages/assinatura.html">Conhecer o plano Plus</a>
          <button type="button" class="asx-link" id="asxPaywallDepois">Agora não</button>
        </div>
      </div>` : ''}
      <div class="asx-aviso">As respostas são geradas por IA e podem conter erros. Confira nos registros. As conversas são registradas para auditoria.</div>
    </section>
  `;
  document.body.appendChild(wrap);
  integrarSuporte(wrap);

  document.getElementById('asxFab').addEventListener('click', () => alternar());
  document.getElementById('asxMin').addEventListener('click', () => alternar(false));
  if (bloqueado) {
    document.getElementById('asxPaywallDepois').addEventListener('click', () => alternar(false));
    document.getElementById('asxPaywallCta').addEventListener('click', () =>
      trackEvent('assistente_upgrade_cta_click', { plano_alvo: 'Plus' }));
  }
  document.getElementById('asxMax').addEventListener('click', () => maximizar());
  document.getElementById('asxPanel').addEventListener('keydown', (e) => {
    if (e.key !== 'Escape') return;
    e.stopPropagation();
    if (wrap.classList.contains('max')) maximizar(false); else alternar(false);
  });
  document.getElementById('asxNova').addEventListener('click', novaConversa);
  document.getElementById('asxHist').addEventListener('click', () => (modo === 'historico' ? voltarAoChat() : abrirHistorico()));
  document.getElementById('asxForm').addEventListener('submit', (e) => {
    e.preventDefault();
    enviar(document.getElementById('asxInput').value);
  });
  const input = document.getElementById('asxInput');
  input.addEventListener('keydown', (e) => {
    if (e.key === 'Enter' && !e.shiftKey) {
      e.preventDefault();
      enviar(input.value);
    }
  });
  input.addEventListener('input', autoResize);
  document.getElementById('asxBody').addEventListener('click', (e) => {
    const chip = e.target.closest('[data-sugestao]');
    if (chip) return enviar(chip.dataset.sugestao);
    const conversa = e.target.closest('[data-conversa]');
    if (conversa) return continuarConversa(conversa.dataset.conversa);
    if (e.target.closest('[data-acao="voltar"]')) return voltarAoChat();
    if (e.target.closest('[data-acao="nova"]')) return novaConversa();
  });

  render();
  if (sessionGet(chave(MAXIMIZADO_KEY)) === '1') maximizar(true);
  if (sessionGet(chave(OPEN_KEY)) === '1' || abrirPelaUrl()) alternar(true);
  else atualizarFab();
}

// "?assistente=1" (ex.: botão da novidade do assistente) abre o painel; o parâmetro sai da URL
// para não reabrir ao recarregar.
function abrirPelaUrl() {
  try {
    const url = new URL(location.href);
    if (url.searchParams.get('assistente') !== '1') return false;
    url.searchParams.delete('assistente');
    history.replaceState(history.state, '', url.pathname + url.search + url.hash);
    return true;
  } catch { return false; }
}

function maximizar(ligar) {
  const wrap = document.getElementById('assistenteIA');
  const max = ligar ?? !wrap.classList.contains('max');
  wrap.classList.toggle('max', max);
  const btn = document.getElementById('asxMax');
  btn.textContent = max ? '⤡' : '⤢';
  btn.title = max ? 'Restaurar tamanho' : 'Maximizar';
  btn.setAttribute('aria-label', btn.title);
  btn.setAttribute('aria-pressed', String(max));
  sessionSet(chave(MAXIMIZADO_KEY), max ? '1' : '0');
  rolarFim();
}

// Botão flutuante: indica conversa em andamento e resposta não lida (chegou minimizado).
function atualizarFab() {
  const fab = document.getElementById('asxFab');
  if (!fab) return;
  document.getElementById('asxDot').hidden = !naoLida;
  fab.title = naoLida ? 'Assistente — nova resposta'
    : enviando ? 'Assistente — consultando os dados…'
    : historico.length ? 'Assistente — continuar conversa' : 'Assistente de IA';
  fab.classList.toggle('asx-fab-ativo', enviando);
}

function alternar(abrir) {
  const panel = document.getElementById('asxPanel');
  const fab = document.getElementById('asxFab');
  const aberto = abrir ?? panel.hidden;
  panel.hidden = !aberto;
  fab.setAttribute('aria-expanded', String(aberto));
  document.getElementById('assistenteIA').classList.toggle('open', aberto);
  sessionSet(chave(OPEN_KEY), aberto ? '1' : '0');
  if (aberto) naoLida = false;
  if (aberto && bloqueado) trackEvent('assistente_paywall_visto', { plano_alvo: 'Plus' });
  atualizarFab();
  // O painel ocupa o canto do chat de suporte: esconde o botão do Crisp enquanto está aberto.
  crisp(aberto ? 'chat:hide' : 'chat:show');
  if (aberto) {
    rolarFim();
    document.getElementById('asxInput').focus();
  }
}

// Chat de suporte (Crisp, carregado em boa parte das páginas) fica no mesmo canto inferior
// direito: o botão do assistente sobe para ficar empilhado acima dele, e some enquanto a
// janela do suporte estiver aberta (senão fica por cima da conversa).
function temSuporte() {
  return Array.isArray(window.$crisp) || typeof window.$crisp?.push === 'function';
}

function crisp(...cmd) {
  if (!temSuporte()) return;
  try { window.$crisp.push(['do', ...cmd]); } catch {}
}

function integrarSuporte(wrap) {
  if (!temSuporte()) return;
  wrap.classList.add('asx-com-suporte');
  try {
    window.$crisp.push(['on', 'chat:opened', () => wrap.classList.add('asx-suporte-aberto')]);
    window.$crisp.push(['on', 'chat:closed', () => wrap.classList.remove('asx-suporte-aberto')]);
  } catch {}
}

function novaConversa() {
  if (enviando) return;
  modo = 'chat';
  historico = [];
  salvarHistorico();
  sessionSet(chave(CONVERSA_KEY), '');
  render();
  document.getElementById('asxInput').focus();
}

async function enviar(texto) {
  texto = (texto || '').trim();
  if (!texto || enviando || bloqueado) return;

  const input = document.getElementById('asxInput');
  input.value = '';
  autoResize.call(input);

  historico.push({ papel: 'user', conteudo: texto });
  salvarHistorico();
  enviando = true;
  render();
  atualizarFab();

  try {
    const mensagens = historico.slice(-MAX_HIST).map(m => ({ papel: m.papel, conteudo: m.conteudo }));
    const conversaId = sessionGet(chave(CONVERSA_KEY)) || null;
    const r = await apiFetch('/assistente/perguntar', { method: 'POST', body: { mensagens, conversaId } });
    if (r.conversaId) sessionSet(chave(CONVERSA_KEY), r.conversaId);
    historico.push({ papel: 'assistant', conteudo: r.resposta, ferramentas: r.ferramentasUsadas || [] });
  } catch (err) {
    historico.push({ papel: 'assistant', conteudo: err.message || 'Não foi possível obter resposta.', erro: true });
  } finally {
    enviando = false;
    salvarHistorico();
    if (document.getElementById('asxPanel').hidden) naoLida = true;
    render();
    atualizarFab();
    if (!document.getElementById('asxPanel').hidden) input.focus();
  }
}

// ── Conversas anteriores ──────────────────────────────────────────────────

async function abrirHistorico() {
  if (enviando || bloqueado) return;
  modo = 'historico';
  conversas = null;
  render();
  try {
    conversas = await apiFetch('/assistente/conversas?limite=50');
  } catch (err) {
    conversas = { erro: err.message || 'Não foi possível carregar as conversas.' };
  }
  if (modo === 'historico') render();
}

function voltarAoChat() {
  modo = 'chat';
  render();
  document.getElementById('asxInput').focus();
}

async function continuarConversa(id) {
  if (enviando) return;
  if (id === sessionGet(chave(CONVERSA_KEY))) return voltarAoChat();
  const body = document.getElementById('asxBody');
  body.innerHTML = '<div class="asx-vazio">Carregando conversa…</div>';
  try {
    const msgs = await apiFetch(`/assistente/conversas/${encodeURIComponent(id)}`);
    historico = msgs.flatMap(m => [
      { papel: 'user', conteudo: m.pergunta },
      m.sucesso
        ? { papel: 'assistant', conteudo: m.resposta || '', ferramentas: m.ferramentasUsadas || [] }
        : { papel: 'assistant', conteudo: m.resposta || 'Não foi possível obter resposta.', erro: true },
    ]);
    sessionSet(chave(CONVERSA_KEY), id);
    salvarHistorico();
    voltarAoChat();
  } catch (err) {
    body.innerHTML = `<div class="asx-msg asx-bot asx-erro">${esc(err.message || 'Não foi possível abrir a conversa.')}</div>`;
  }
}

function fmtQuando(iso) {
  const d = new Date(iso);
  const hoje = new Date();
  const ontem = new Date(hoje.getFullYear(), hoje.getMonth(), hoje.getDate() - 1);
  const hora = d.toLocaleTimeString('pt-BR', { hour: '2-digit', minute: '2-digit' });
  if (d.toDateString() === hoje.toDateString()) return `Hoje, ${hora}`;
  if (d.toDateString() === ontem.toDateString()) return `Ontem, ${hora}`;
  return d.toLocaleDateString('pt-BR', { day: '2-digit', month: '2-digit', year: d.getFullYear() === hoje.getFullYear() ? undefined : 'numeric' }) + `, ${hora}`;
}

function renderHistorico(body) {
  const atual = sessionGet(chave(CONVERSA_KEY));
  const topo = `
    <div class="asx-hist-topo">
      <button type="button" class="asx-link" data-acao="voltar">← Voltar à conversa</button>
      <button type="button" class="asx-link" data-acao="nova">+ Nova conversa</button>
    </div>
    <div class="asx-hist-titulo">Conversas anteriores</div>`;
  if (conversas == null) { body.innerHTML = topo + '<div class="asx-vazio">Carregando…</div>'; return; }
  if (conversas.erro) { body.innerHTML = topo + `<div class="asx-msg asx-bot asx-erro">${esc(conversas.erro)}</div>`; return; }
  if (conversas.length === 0) { body.innerHTML = topo + '<div class="asx-vazio">Você ainda não tem conversas anteriores.</div>'; return; }
  body.innerHTML = topo + '<ul class="asx-hist">' + conversas.map(c => `
    <li>
      <button type="button" class="asx-hist-item${c.conversaId === atual ? ' atual' : ''}" data-conversa="${esc(c.conversaId)}">
        <span class="asx-hist-nome">${esc(c.titulo)}</span>
        <span class="asx-hist-meta">${fmtQuando(c.ultimaEm)} · ${c.perguntas} pergunta${c.perguntas === 1 ? '' : 's'}${c.conversaId === atual ? ' · atual' : ''}</span>
      </button>
    </li>`).join('') + '</ul>';
  body.scrollTop = 0;
}

// Plano Free: conversa de exemplo (desfocada, atrás do convite) e controles desativados.
function renderExemplo(body) {
  ['asxSend', 'asxNova', 'asxHist', 'asxInput'].forEach(id => { document.getElementById(id).disabled = true; });
  document.getElementById('asxInput').placeholder = 'Disponível a partir do plano Plus';
  body.setAttribute('aria-hidden', 'true');
  body.innerHTML = `
    <div class="asx-msg asx-user">Quais prazos vencem esta semana?</div>
    <div class="asx-msg asx-bot"><div class="asx-md">${renderMarkdown(
      'Você tem **3 prazos** nesta semana:\n\n' +
      '| Prazo | Processo | Responsável |\n|---|---|---|\n' +
      '| 14/10 | Contestação — 0001234-56.2026 | Ana |\n' +
      '| 15/10 | Réplica — 0004567-89.2025 | Bruno |\n' +
      '| 17/10 | Recurso — 0007890-12.2026 | Ana |\n\n' +
      'O mais urgente é a **contestação**, que vence na terça.')}</div></div>
    <div class="asx-msg asx-user">Quais clientes estão com honorários em atraso?</div>
    <div class="asx-msg asx-bot"><div class="asx-md">${renderMarkdown(
      '2 clientes estão em atraso: **Carlos M.** (R$ 1.253,60) e **Beatriz L.** (R$ 81,42).')}</div></div>`;
}

function render() {
  const body = document.getElementById('asxBody');
  if (!body) return;
  document.getElementById('asxSend').disabled = enviando;
  document.getElementById('asxNova').disabled = enviando;
  document.getElementById('asxHist').disabled = enviando;
  document.getElementById('asxHist').setAttribute('aria-pressed', String(modo === 'historico'));
  document.getElementById('asxForm').hidden = modo === 'historico';

  if (bloqueado) { renderExemplo(body); return; }
  if (modo === 'historico') { renderHistorico(body); return; }

  if (historico.length === 0 && !enviando) {
    body.innerHTML = `
      <div class="asx-vazio">
        <p>Olá! Posso consultar os dados do escritório para você. Experimente:</p>
        <div class="asx-sugestoes">
          ${SUGESTOES.map(s => `<button type="button" class="asx-chip" data-sugestao="${esc(s)}">${esc(s)}</button>`).join('')}
        </div>
      </div>`;
    return;
  }

  body.innerHTML = historico.map(m => {
    if (m.papel === 'user') return `<div class="asx-msg asx-user">${esc(m.conteudo).replace(/\n/g, '<br>')}</div>`;
    if (m.erro) return `<div class="asx-msg asx-bot asx-erro">${esc(m.conteudo)}</div>`;
    const fontes = [...new Set((m.ferramentas || []).map(f => ROTULOS_FERRAMENTA[f] || f))];
    return `<div class="asx-msg asx-bot">
        <div class="asx-md">${renderMarkdown(m.conteudo)}</div>
        ${fontes.length ? `<div class="asx-fontes">Consultou: ${esc(fontes.join(', '))}</div>` : ''}
      </div>`;
  }).join('') + (enviando
    ? '<div class="asx-msg asx-bot asx-pensando"><span></span><span></span><span></span> Consultando os dados…</div>'
    : '');
  rolarFim();
}

function rolarFim() {
  const body = document.getElementById('asxBody');
  if (body) body.scrollTop = body.scrollHeight;
}

function autoResize() {
  this.style.height = 'auto';
  this.style.height = `${Math.min(this.scrollHeight, 120)}px`;
}

function carregarHistorico() {
  try {
    const h = JSON.parse(sessionGet(chave(HIST_KEY)) || '[]');
    return Array.isArray(h) ? h : [];
  } catch { return []; }
}

function salvarHistorico() {
  sessionSet(chave(HIST_KEY), JSON.stringify(historico.slice(-MAX_HIST)));
}

function sessionGet(k) { try { return sessionStorage.getItem(k); } catch { return null; } }
function sessionSet(k, v) { try { sessionStorage.setItem(k, v); } catch {} }

function injectStyles() {
  if (document.getElementById('assistenteStyles')) return;
  const s = document.createElement('style');
  s.id = 'assistenteStyles';
  s.textContent = `
    .asx { position: fixed; right: 24px; bottom: 24px; z-index: 960; }
    .asx-fab {
      display: flex; align-items: center; gap: 6px; border: none; cursor: pointer;
      background: var(--color-primary); color: #fff !important; font-weight: 600; font-size: 14px;
      padding: 12px 16px; border-radius: 999px; box-shadow: var(--shadow-md);
    }
    .asx-fab:hover { background: var(--color-primary-dark); }
    .asx-fab { position: relative; }
    .asx-fab-dot {
      position: absolute; top: 2px; right: 2px; width: 12px; height: 12px; border-radius: 50%;
      background: var(--color-danger); border: 2px solid var(--color-surface);
    }
    .asx-fab-dot[hidden] { display: none; }
    .asx-fab-ativo { animation: asx-pulse-fab 1.4s infinite ease-in-out; }
    @keyframes asx-pulse-fab { 0%, 100% { box-shadow: var(--shadow-md); } 50% { box-shadow: 0 0 0 6px var(--color-primary-light); } }
    .asx.open .asx-fab { display: none; }
    /* Acima do botão do Crisp (60px + margem de 20px), com folga. */
    .asx.asx-com-suporte:not(.open) { bottom: 96px; right: 20px; }
    .asx.asx-suporte-aberto:not(.open) { display: none; }
    .asx-panel {
      width: 400px; height: min(620px, calc(100vh - 48px));
      background: var(--color-surface); border: 1px solid var(--color-border); border-radius: 12px;
      box-shadow: 0 10px 30px rgba(0,0,0,.18); display: flex; flex-direction: column; overflow: hidden;
      color: var(--color-text);
    }
    .asx-panel[hidden] { display: none; }
    .asx-panel { position: relative; }
    .asx-bloqueado .asx-body { filter: blur(3px); user-select: none; pointer-events: none; }
    .asx-paywall {
      position: absolute; inset: 0; z-index: 2; overflow-y: auto;
      display: flex; align-items: center; justify-content: center; padding: 88px 20px 20px;
      background: linear-gradient(to bottom,
        color-mix(in srgb, var(--color-surface) 35%, transparent),
        color-mix(in srgb, var(--color-surface) 88%, transparent));
    }
    /* Cabeçalho (minimizar/maximizar) continua acessível acima do convite. */
    .asx-bloqueado .asx-header { position: relative; z-index: 3; background: var(--color-surface); }
    .asx-paywall-card {
      background: var(--color-surface); color: var(--color-text); border: 1px solid var(--color-border);
      border-radius: 12px; box-shadow: 0 10px 30px rgba(0,0,0,.15); padding: 22px 20px 16px;
      max-width: 340px; width: 100%; text-align: center; display: flex; flex-direction: column; align-items: center; gap: 8px;
    }
    .asx-paywall-icone { font-size: 28px; line-height: 1; }
    .asx-paywall-selo {
      font-size: 10.5px; font-weight: 700; text-transform: uppercase; letter-spacing: .05em;
      background: #fef3c7; color: #92400e; padding: 2px 8px; border-radius: 999px;
    }
    .asx-paywall-card h3 { font-size: 15px; line-height: 1.35; margin: 2px 0 0; }
    .asx-paywall-card p { font-size: 12.5px; color: var(--color-text-muted); }
    .asx-paywall-card ul { text-align: left; font-size: 12.5px; padding-left: 18px; display: flex; flex-direction: column; gap: 3px; align-self: stretch; }
    .asx-paywall-cta {
      margin-top: 6px; display: block; align-self: stretch; text-decoration: none; font-weight: 700; font-size: 14px;
      background: var(--color-primary); color: #fff !important; padding: 10px 14px; border-radius: 8px;
    }
    .asx-paywall-cta:hover { background: var(--color-primary-dark); }
    /* Maximizado: ocupa quase a tela toda (respostas com tabelas grandes). */
    .asx.open.max { inset: 24px; z-index: 1100; }
    .asx.open.max .asx-panel { width: 100%; height: 100%; box-shadow: 0 0 0 100vmax rgba(0,0,0,.35), 0 10px 30px rgba(0,0,0,.25); }
    .asx.max .asx-body { padding: 20px max(20px, calc((100% - 1100px) / 2)); }
    .asx.max .asx-msg { max-width: 100%; font-size: 14px; }
    .asx.max .asx-user { max-width: 75%; }
    .asx.max .asx-md table { font-size: 13px; }
    .asx.max .asx-md th, .asx.max .asx-md td { padding: 6px 10px; }
    .asx.max .asx-form { padding: 12px max(16px, calc((100% - 1100px) / 2)); }
    .asx-header {
      display: flex; justify-content: space-between; align-items: flex-start; gap: 8px;
      padding: 12px 14px; border-bottom: 1px solid var(--color-border); font-size: 14px;
    }
    .asx-sub { font-size: 12px; color: var(--color-text-muted); margin-top: 2px; }
    .asx-beta {
      font-size: 10px; font-weight: 700; text-transform: uppercase; letter-spacing: .04em;
      background: var(--color-primary-light); color: var(--color-primary); padding: 1px 6px; border-radius: 999px;
    }
    .asx-header-actions { display: flex; gap: 2px; }
    .asx-icon-btn {
      background: none; border: none; cursor: pointer; font-size: 15px; line-height: 1;
      color: var(--color-text-muted) !important; padding: 6px; border-radius: 6px;
    }
    .asx-icon-btn:hover:not(:disabled) { background: var(--color-bg); color: var(--color-text) !important; }
    .asx-icon-btn:disabled { opacity: .4; cursor: default; }
    .asx-body { flex: 1; overflow-y: auto; padding: 14px; display: flex; flex-direction: column; gap: 10px; background: var(--color-bg); }
    .asx-msg { max-width: 92%; font-size: 13.5px; line-height: 1.5; padding: 9px 12px; border-radius: 12px; overflow-wrap: anywhere; }
    .asx-user { align-self: flex-end; background: var(--color-primary); color: #fff; border-bottom-right-radius: 4px; }
    .asx-bot { align-self: flex-start; background: var(--color-surface); border: 1px solid var(--color-border); border-bottom-left-radius: 4px; }
    .asx-erro { border-color: var(--color-danger); color: var(--color-danger); }
    .asx-md p { margin: 0 0 6px; } .asx-md p:last-child { margin-bottom: 0; }
    .asx-md ul, .asx-md ol { margin: 4px 0 6px 18px; }
    .asx-md li { margin: 2px 0; }
    .asx-md h1, .asx-md h2, .asx-md h3, .asx-md h4 { font-size: 14px; margin: 8px 0 4px; }
    .asx-md table { border-collapse: collapse; font-size: 12.5px; margin: 6px 0; display: block; overflow-x: auto; }
    .asx-md th, .asx-md td { border: 1px solid var(--color-border); padding: 4px 6px; text-align: left; }
    .asx-md code { font-size: 12px; background: var(--color-bg); padding: 1px 4px; border-radius: 4px; }
    .asx-md a { color: var(--color-primary); }
    .asx-fontes { margin-top: 6px; font-size: 11px; color: var(--color-text-muted); }
    .asx-pensando { color: var(--color-text-muted); display: flex; align-items: center; gap: 4px; }
    .asx-pensando span {
      width: 6px; height: 6px; border-radius: 50%; background: var(--color-text-muted);
      animation: asx-pulse 1s infinite ease-in-out;
    }
    .asx-pensando span:nth-child(2) { animation-delay: .15s; }
    .asx-pensando span:nth-child(3) { animation-delay: .3s; margin-right: 4px; }
    @keyframes asx-pulse { 0%, 80%, 100% { opacity: .25; } 40% { opacity: 1; } }
    .asx-vazio { font-size: 13.5px; color: var(--color-text-muted); }
    .asx-form[hidden] { display: none; }
    .asx-icon-btn[aria-pressed="true"] { background: var(--color-primary-light); }
    .asx-hist-topo { display: flex; justify-content: space-between; gap: 8px; }
    .asx-link { background: none; border: none; cursor: pointer; font-size: 12.5px; color: var(--color-primary) !important; padding: 2px 0; }
    .asx-link:hover { text-decoration: underline; }
    .asx-hist-titulo { font-size: 11px; font-weight: 700; text-transform: uppercase; letter-spacing: .04em; color: var(--color-text-muted); }
    .asx-hist { list-style: none; display: flex; flex-direction: column; gap: 6px; }
    .asx-hist-item {
      width: 100%; text-align: left; cursor: pointer; display: flex; flex-direction: column; gap: 3px;
      background: var(--color-surface); border: 1px solid var(--color-border); border-radius: 8px; padding: 9px 12px;
      color: var(--color-text) !important; font: inherit;
    }
    .asx-hist-item:hover { border-color: var(--color-primary); }
    .asx-hist-item.atual { border-color: var(--color-primary); background: var(--color-primary-light); }
    .asx-hist-nome { font-size: 13px; font-weight: 600; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .asx-hist-meta { font-size: 11.5px; color: var(--color-text-muted); }
    .asx-sugestoes { display: flex; flex-wrap: wrap; gap: 6px; margin-top: 10px; }
    .asx-chip {
      background: var(--color-surface); border: 1px solid var(--color-border); border-radius: 999px;
      padding: 6px 10px; font-size: 12.5px; cursor: pointer; color: var(--color-text) !important; text-align: left;
    }
    .asx-chip:hover { border-color: var(--color-primary); color: var(--color-primary) !important; }
    .asx-form { display: flex; gap: 8px; align-items: flex-end; padding: 10px 12px; border-top: 1px solid var(--color-border); }
    .asx-form textarea {
      flex: 1; resize: none; border: 1px solid var(--color-border); border-radius: 8px; padding: 8px 10px;
      font: inherit; font-size: 13.5px; max-height: 120px; background: var(--color-surface); color: var(--color-text);
    }
    .asx-form textarea:focus { outline: none; border-color: var(--color-primary); }
    .asx-send {
      border: none; background: var(--color-primary); color: #fff !important; border-radius: 8px;
      width: 38px; height: 38px; cursor: pointer; font-size: 15px; flex-shrink: 0;
    }
    .asx-send:disabled { opacity: .5; cursor: default; }
    .asx-aviso { font-size: 10.5px; color: var(--color-text-muted); text-align: center; padding: 0 12px 8px; }
    @media (max-width: 768px) {
      .asx { right: 16px; bottom: calc(var(--bottom-nav-height) + 16px); }
      .asx.asx-com-suporte:not(.open) { right: 16px; bottom: calc(var(--bottom-nav-height) + 88px); }
      .asx-fab-label { display: none; }
      .asx-fab { padding: 12px 14px; }
      .asx.open { inset: 0; right: 0; bottom: 0; z-index: 1100; }
      .asx-panel { width: 100%; height: 100%; border-radius: 0; border: none; }
      .asx-btn-max { display: none; } /* no celular o painel já ocupa a tela toda */
      .asx.open.max { inset: 0; }
      .asx.open.max .asx-panel { box-shadow: none; }
    }
  `;
  document.head.appendChild(s);
}
