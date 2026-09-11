// Menu flutuante retrátil de tarefas — mostra as tarefas pendentes e em
// andamento do usuário logado em qualquer página do portal admin, permitindo
// avançar o status (Pendente → Em Andamento → Concluída) sem sair da página.
// Disponível a partir do plano Plus (chamado por initLayout() em layout.js).
import { apiFetch, getUser } from './api.js';

const OPEN_KEY = 'tarefasWidgetOpen';
const REFRESH_MS = 120000;
const PAGE_SIZE = 30;

const PRIORIDADE_COR = {
  Urgente: '#dc2626',
  Alta: '#ea580c',
  Media: '#ca8a04',
  Baixa: '#16a34a',
};

const PRIORIDADE_LABEL = { Baixa: 'Baixa', Media: 'Média', Alta: 'Alta', Urgente: 'Urgente' };
const STATUS_LABEL = { Pendente: 'Pendente', EmAndamento: 'Em Andamento', Concluida: 'Concluída', Cancelada: 'Cancelada' };

let tasks = { Pendente: [], EmAndamento: [] };
let totals = { Pendente: 0, EmAndamento: 0 };
let loading = false;
let refreshTimer = null;

export function injectTarefasWidget() {
  if (document.getElementById('tarefasWidget')) return;

  injectStyles();

  const wrap = document.createElement('div');
  wrap.id = 'tarefasWidget';
  wrap.className = 'tw';
  if (localStorage.getItem(OPEN_KEY) === '1') wrap.classList.add('open');

  wrap.innerHTML = `
    <button type="button" class="tw-toggle" id="twToggle" title="Minhas tarefas">
      <span class="tw-toggle-icon" aria-hidden="true">✅</span>
      <span class="tw-toggle-label">Tarefas</span>
      <span class="tw-toggle-badge" id="twBadge" hidden>0</span>
    </button>
    <div class="tw-panel" role="region" aria-label="Minhas tarefas">
      <div class="tw-panel-header">
        <strong>Minhas Tarefas</strong>
        <button type="button" class="tw-panel-close" id="twClose" title="Recolher">✕</button>
      </div>
      <div class="tw-panel-body" id="twBody">
        <div class="tw-loading">Carregando…</div>
      </div>
      <div class="tw-panel-footer">
        <a href="/pages/tarefas.html?tab=kanban">Ver quadro completo →</a>
      </div>
    </div>
  `;
  document.body.appendChild(wrap);

  // Fica FORA de #tarefasWidget (que tem `transform`) e é anexado direto ao
  // <body>: um `transform` em ancestral vira containing block para
  // position:fixed, então dentro de .tw o overlay ficaria confinado à
  // caixinha do widget em vez de cobrir a tela inteira.
  const overlay = document.createElement('div');
  overlay.className = 'modal-overlay';
  overlay.id = 'twDetailOverlay';
  overlay.innerHTML = `
    <div class="modal" style="max-width:420px">
      <div class="modal-header">
        <span class="modal-title">Detalhes da tarefa</span>
        <button type="button" class="modal-close" id="twDetailClose">✕</button>
      </div>
      <div class="modal-body" id="twDetailBody"></div>
      <div class="modal-footer">
        <button type="button" class="btn btn-secondary" id="twDetailCloseBtn">Fechar</button>
        <a href="#" id="twDetailGoBoard" class="btn btn-primary">Ver no quadro →</a>
      </div>
    </div>
  `;
  document.body.appendChild(overlay);

  document.getElementById('twToggle').addEventListener('click', () => toggle());
  document.getElementById('twClose').addEventListener('click', () => toggle(false));
  document.getElementById('twDetailClose').addEventListener('click', closeDetail);
  document.getElementById('twDetailCloseBtn').addEventListener('click', closeDetail);
  overlay.addEventListener('click', e => {
    if (e.target === e.currentTarget) closeDetail();
  });

  loadTasks();
  refreshTimer = setInterval(loadTasks, REFRESH_MS);
}

function closeDetail() {
  document.getElementById('twDetailOverlay')?.classList.remove('open');
}

function showDetail(id, status) {
  const t = tasks[status]?.find(item => item.id === id);
  if (!t) return;

  const prazo = t.prazo ? new Date(t.prazo).toLocaleString('pt-BR') : '–';
  const tags = t.tags?.length ? t.tags.map(tag => `<span class="tag">${esc(tag)}</span>`).join(' ') : '–';
  const descricao = t.descricao
    ? esc(t.descricao).replace(/\n/g, '<br>')
    : '<span style="color:var(--color-text-muted)">Sem descrição</span>';

  document.getElementById('twDetailBody').innerHTML = `
    <div class="detail-row"><span class="detail-label">Título</span><span class="detail-value">${esc(t.titulo)}</span></div>
    <div class="detail-row"><span class="detail-label">Prioridade</span><span class="detail-value">${PRIORIDADE_LABEL[t.prioridade] ?? t.prioridade}</span></div>
    <div class="detail-row"><span class="detail-label">Status</span><span class="detail-value">${STATUS_LABEL[status] ?? status}</span></div>
    <div class="detail-row"><span class="detail-label">Prazo</span><span class="detail-value">${prazo}</span></div>
    <div class="detail-row"><span class="detail-label">Responsável</span><span class="detail-value">${t.nomeResponsavel ? esc(t.nomeResponsavel) : '–'}</span></div>
    ${t.numeroCNJProcesso ? `<div class="detail-row"><span class="detail-label">Processo</span><span class="detail-value">${esc(t.numeroCNJProcesso)}</span></div>` : ''}
    ${t.nomeContato ? `<div class="detail-row"><span class="detail-label">Contato</span><span class="detail-value">${esc(t.nomeContato)}</span></div>` : ''}
    <div class="detail-row"><span class="detail-label">Tags</span><span class="detail-value">${tags}</span></div>
    <div style="margin-top:12px"><span class="detail-label">Descrição</span><div style="margin-top:4px">${descricao}</div></div>
  `;
  document.getElementById('twDetailGoBoard').href = `/pages/tarefas.html?tab=kanban&abrirId=${id}`;
  document.getElementById('twDetailOverlay').classList.add('open');
}

function toggle(force) {
  const wrap = document.getElementById('tarefasWidget');
  if (!wrap) return;
  const open = force !== undefined ? force : !wrap.classList.contains('open');
  wrap.classList.toggle('open', open);
  localStorage.setItem(OPEN_KEY, open ? '1' : '0');
  if (open) loadTasks();
}

async function loadTasks() {
  if (!document.getElementById('tarefasWidget') || loading) return;
  const user = getUser();
  if (!user?.id) return;

  loading = true;
  try {
    const [pend, and] = await Promise.all([
      apiFetch(`/tarefas?status=Pendente&responsavelId=${user.id}&pageSize=${PAGE_SIZE}`),
      apiFetch(`/tarefas?status=EmAndamento&responsavelId=${user.id}&pageSize=${PAGE_SIZE}`),
    ]);
    tasks.Pendente = pend.items ?? [];
    tasks.EmAndamento = and.items ?? [];
    totals.Pendente = pend.total ?? tasks.Pendente.length;
    totals.EmAndamento = and.total ?? tasks.EmAndamento.length;
    render();
  } catch (err) {
    const body = document.getElementById('twBody');
    if (body) body.innerHTML = '<div class="tw-error">Não foi possível carregar suas tarefas.</div>';
    console.error('Erro ao carregar tarefas do widget', err);
  } finally {
    loading = false;
  }
}

function render() {
  renderBadge();

  const body = document.getElementById('twBody');
  if (!body) return;

  if (totals.Pendente === 0 && totals.EmAndamento === 0) {
    body.innerHTML = '<div class="tw-empty-all">🎉 Tudo em dia! Nenhuma tarefa pendente ou em andamento.</div>';
    return;
  }

  const sections = [
    { status: 'EmAndamento', label: 'Em andamento', items: tasks.EmAndamento, total: totals.EmAndamento },
    { status: 'Pendente', label: 'Pendentes', items: tasks.Pendente, total: totals.Pendente },
  ];

  body.innerHTML = sections.map(sec => `
    <div class="tw-section">
      <div class="tw-section-title">${sec.label} <span class="tw-count">${sec.total}</span></div>
      ${sec.items.length
        ? sec.items.map(t => cardHtml(t, sec.status)).join('')
        : '<div class="tw-empty">Nenhuma tarefa aqui.</div>'}
      ${sec.total > sec.items.length
        ? `<div class="tw-more"><a href="/pages/tarefas.html?tab=kanban">+${sec.total - sec.items.length} tarefa(s) — ver todas</a></div>`
        : ''}
    </div>
  `).join('');

  body.querySelectorAll('[data-move]').forEach(btn => {
    btn.addEventListener('click', () => moveTask(btn.dataset.id, btn.dataset.move, btn.dataset.from));
  });
  body.querySelectorAll('[data-detail]').forEach(el => {
    el.addEventListener('click', () => showDetail(el.dataset.detail, el.dataset.status));
  });
}

function renderBadge() {
  const badge = document.getElementById('twBadge');
  if (!badge) return;
  const total = totals.Pendente + totals.EmAndamento;
  if (total > 0) {
    badge.textContent = total > 99 ? '99+' : String(total);
    badge.hidden = false;
  } else {
    badge.hidden = true;
  }
}

function cardHtml(t, status) {
  const prazo = formatPrazo(t.prazo);
  const cor = PRIORIDADE_COR[t.prioridade] ?? '#9ca3af';
  const atrasadaBadge = t.atrasada ? '<span class="tw-atrasada">Atrasada</span>' : '';

  const actionsHtml = status === 'Pendente'
    ? `<button type="button" class="tw-btn tw-btn-advance" data-move="EmAndamento" data-id="${t.id}" data-from="Pendente">▶ Iniciar</button>`
    : `<button type="button" class="tw-btn tw-btn-back" data-move="Pendente" data-id="${t.id}" data-from="EmAndamento" title="Voltar para pendente">↩</button>
       <button type="button" class="tw-btn tw-btn-done" data-move="Concluida" data-id="${t.id}" data-from="EmAndamento">✔ Concluir</button>`;

  return `
    <div class="tw-card" style="border-left-color:${cor}">
      <div class="tw-card-title" data-detail="${t.id}" data-status="${status}" title="${esc(t.titulo)}">${esc(t.titulo)}</div>
      <div class="tw-card-meta">
        ${prazo ? `<span class="${t.atrasada ? 'tw-overdue' : ''}">📅 ${prazo}</span>` : ''}
        ${t.numeroCNJProcesso ? `<span>⚖️ ${esc(t.numeroCNJProcesso)}</span>` : ''}
        ${atrasadaBadge}
      </div>
      <div class="tw-card-actions">${actionsHtml}</div>
    </div>`;
}

async function moveTask(id, toStatus, fromStatus) {
  const list = tasks[fromStatus];
  const idx = list ? list.findIndex(t => t.id === id) : -1;
  const removed = idx > -1 ? list.splice(idx, 1)[0] : null;
  if (removed) totals[fromStatus] = Math.max(0, totals[fromStatus] - 1);

  if (removed && toStatus in tasks) {
    tasks[toStatus] = [{ ...removed, status: toStatus }, ...tasks[toStatus]];
    totals[toStatus] = (totals[toStatus] ?? 0) + 1;
  }
  render();

  try {
    await apiFetch(`/tarefas/${id}/mover`, { method: 'PATCH', body: { status: toStatus } });
  } catch (err) {
    console.error('Erro ao mover tarefa', err);
    await loadTasks();
  }
}

function formatPrazo(prazo) {
  if (!prazo) return null;
  const d = new Date(prazo);
  return isNaN(d) ? null : d.toLocaleDateString('pt-BR', { day: '2-digit', month: '2-digit' });
}

function esc(str) {
  return (str ?? '').replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');
}

function injectStyles() {
  if (document.getElementById('tarefasWidgetStyles')) return;
  const s = document.createElement('style');
  s.id = 'tarefasWidgetStyles';
  s.textContent = `
    .tw { position: fixed; top: 50%; right: 0; transform: translateY(-50%); z-index: 950; display: flex; align-items: center; }
    /* Usa o par --color-sidebar/--color-sidebar-text (não --color-primary)
       porque esse é o único par de cores no design system com contraste
       garantido em qualquer tema — inclusive nos temas escuros predefinidos,
       onde --color-primary costuma ser uma cor viva/clara (ciano, rosa neon
       etc.) pensada para texto SOBRE fundo escuro, não como fundo com texto
       em cima. O !important é necessário porque os temas escuros aplicam
       'button { color: inherit !important; }' globalmente (ver presets.js).
       Cuidado: sem crase aqui dentro — este bloco inteiro é um template
       literal de JS, uma crase literal fecharia a string prematuramente. */
    .tw-toggle {
      display: flex; flex-direction: column; align-items: center; gap: 8px; width: 34px;
      background: var(--color-sidebar); color: var(--color-sidebar-text) !important; border: none; cursor: pointer;
      border-radius: 10px 0 0 10px; padding: 12px 7px; box-shadow: var(--shadow-md);
    }
    .tw-toggle:hover { filter: brightness(1.2); }
    .tw-toggle-icon { font-size: 15px; line-height: 1; }
    /* Texto na vertical (o writing-mode fica só nesse <span> folha, não no
       container flex — misturar writing-mode com o eixo do flex container
       faz o botão colapsar pra tamanho zero em alguns navegadores). */
    .tw-toggle-label {
      writing-mode: vertical-rl; transform: rotate(180deg);
      font-size: 11px; font-weight: 700; letter-spacing: .04em; line-height: 1;
    }
    /* Fica no fluxo normal do flex column, nunca em position:absolute — o
       botão já encosta na borda direita da viewport, então qualquer offset
       negativo pra direita (right:-Npx) empurra o badge pra fora da tela e
       ele fica cortado, sem erro nenhum no console. */
    .tw-toggle-badge {
      background: #dc2626; color: #fff; font-size: 10px; font-weight: 700;
      min-width: 16px; height: 16px; line-height: 16px; text-align: center; border-radius: 999px; padding: 0 4px;
    }
    .tw-panel {
      position: absolute; right: 100%; top: 50%; margin-right: 8px;
      transform: translateY(-50%) translateX(12px); opacity: 0; pointer-events: none;
      transition: transform .18s ease, opacity .18s ease;
      width: 320px; max-width: calc(100vw - 24px); max-height: min(560px, calc(100vh - 48px));
      background: var(--color-surface); border: 1px solid var(--color-border); border-radius: var(--radius);
      box-shadow: var(--shadow-md); display: flex; flex-direction: column; overflow: hidden;
    }
    .tw.open .tw-panel { transform: translateY(-50%) translateX(0); opacity: 1; pointer-events: auto; }
    .tw-panel-header {
      display: flex; align-items: center; justify-content: space-between; gap: 8px;
      padding: 10px 12px; border-bottom: 1px solid var(--color-border); font-size: 13px; color: var(--color-text);
    }
    .tw-panel-close { background: none; border: none; cursor: pointer; font-size: 13px; color: var(--color-text-muted) !important; padding: 4px; line-height: 1; }
    .tw-panel-close:hover { color: var(--color-text) !important; }
    .tw-panel-body { flex: 1; overflow-y: auto; padding: 10px 12px; }
    .tw-panel-footer { padding: 8px 12px; border-top: 1px solid var(--color-border); text-align: center; }
    .tw-panel-footer a { font-size: 12px; font-weight: 600; }
    .tw-loading, .tw-error, .tw-empty, .tw-empty-all { color: var(--color-text-muted); font-size: 12px; text-align: center; padding: 16px 4px; }
    .tw-empty-all { font-size: 13px; padding: 24px 8px; }
    .tw-error { color: var(--color-danger); }
    .tw-section + .tw-section { margin-top: 16px; }
    .tw-section-title { font-size: 11px; font-weight: 700; text-transform: uppercase; letter-spacing: .05em; color: var(--color-text-muted); margin-bottom: 8px; display: flex; align-items: center; gap: 6px; }
    .tw-count { background: var(--color-bg); border: 1px solid var(--color-border); color: var(--color-text-muted); font-size: 10px; font-weight: 700; padding: 1px 6px; border-radius: 999px; }
    .tw-card { background: var(--color-bg); border: 1px solid var(--color-border); border-left: 3px solid #9ca3af; border-radius: 6px; padding: 8px 10px; margin-bottom: 8px; }
    .tw-card-title { font-size: 12px; font-weight: 600; color: var(--color-text); cursor: pointer; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .tw-card-title:hover { color: var(--color-primary); }
    .tw-card-meta { display: flex; flex-wrap: wrap; gap: 6px; font-size: 11px; color: var(--color-text-muted); margin-top: 4px; }
    .tw-overdue { color: var(--color-danger); font-weight: 600; }
    .tw-atrasada { background: #fde8e8; color: var(--color-danger); font-size: 10px; font-weight: 700; padding: 1px 6px; border-radius: 999px; }
    .tw-card-actions { display: flex; gap: 6px; margin-top: 8px; }
    /* Os !important abaixo existem pelo mesmo motivo do .tw-toggle acima:
       sobreviver ao 'button { color: inherit !important; }' que os temas
       escuros aplicam globalmente. --color-primary-light/-dark também foram
       trocados por --color-primary puro (texto) sobre fundo transparente —
       aquele par depende de sombreamento calculado a partir da cor primária
       do tenant (ver theme.js/shadeForPrimary) e podia render um azul-escuro
       sobre azul-escuro nos temas escuros. --color-primary já é usado como
       cor de texto em vários lugares do app (links, hovers) então é seguro
       aqui também. */
    .tw-btn { font-size: 11px; font-weight: 600; border-radius: 6px; padding: 4px 8px; cursor: pointer; border: 1px solid transparent; }
    .tw-btn-advance { background: transparent; border-color: var(--color-primary); color: var(--color-primary) !important; }
    .tw-btn-advance:hover { filter: brightness(1.3); }
    .tw-btn-done { background: var(--color-success); color: #fff !important; margin-left: auto; }
    .tw-btn-done:hover { opacity: .88; }
    .tw-btn-back { background: none; border-color: var(--color-border); color: var(--color-text-muted) !important; }
    .tw-btn-back:hover { background: var(--color-border); }
    .tw-more { text-align: center; margin-top: 4px; }
    .tw-more a { font-size: 11px; }

    /* Modal de detalhes — reaproveita .modal-overlay/.modal/.btn etc. de
       css/styles.css (carregado em todas as páginas do portal); só as
       classes internas do corpo (detail-row, tag) são escopadas aqui. */
    #twDetailBody .detail-row { display: flex; gap: 8px; margin-bottom: 8px; font-size: 14px; }
    #twDetailBody .detail-label { font-weight: 600; color: var(--color-text-muted); min-width: 90px; }
    #twDetailBody .detail-value { flex: 1; }
    #twDetailBody .tag { display: inline-block; background: var(--color-bg); color: var(--color-text); font-size: 11px; padding: 2px 8px; border-radius: 999px; margin: 0 4px 4px 0; }

    @media (max-width: 768px) {
      .tw { top: auto; bottom: calc(var(--bottom-nav-height, 0px) + 16px); transform: none; }
      .tw-panel { top: auto; bottom: 0; transform: translateX(12px); max-height: min(420px, calc(100vh - var(--bottom-nav-height, 0px) - 96px)); }
      .tw.open .tw-panel { transform: translateX(0); }
    }
  `;
  document.head.appendChild(s);
}
