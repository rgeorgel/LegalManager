// Dashboard (pages/dashboard.html): KPIs, blocos e widgets do dia a dia.
// Layout personalizável em /js/dashboard-layout.js; helpers em /js/utils.js.
import { initLayout, getPlano, isPlanoFree, showUpgradeToast } from '/js/layout.js';
import { apiFetch, isLoggedIn, getUser } from '/js/api.js';
import { initOnboarding, initOnboardingModal } from '/js/onboarding.js';
import { initTrialBoasVindasModal } from '/js/trial-boas-vindas.js';
import { mostrarDestaqueNovidade } from '/js/novidades.js';
import { injectDashboardSwitch } from '/js/dashboard-versao.js';
import { esc, brl as brlOuTraco, dataParede, isoLocal } from '/js/utils.js';
import { initDashboardLayout } from '/js/dashboard-layout.js';

if (!isLoggedIn()) {
  window.location.href = '/login.html';
  throw new Error('not authenticated');
}

initLayout();
initOnboardingModal();
// Mesmo encadeamento do dashboard clássico: trial de boas-vindas → onboarding (importar por OAB)
// → novidade em destaque. Um modal por vez.
initTrialBoasVindasModal(() => initOnboarding(() => mostrarDestaqueNovidade()));
injectDashboardSwitch(document.getElementById('dashVersao'), 'novo');

// Temas escuros são presets com customCss (ver presets.js) — não há classe
// global, então detecta pela luminância do fundo, como theme.js faz.
function detectarTemaEscuro() {
  const m = getComputedStyle(document.body).backgroundColor.match(/rgba?\((\d+),\s*(\d+),\s*(\d+)/);
  const escuro = !!m && (m[1] * 0.299 + m[2] * 0.587 + m[3] * 0.114) / 255 < 0.5;
  document.querySelector('.d2').classList.toggle('d2-dark', escuro);
}
detectarTemaEscuro();

const user = getUser();

// Aplica o layout personalizado (KPIs e blocos: ordem, largura, altura e visibilidade) o quanto antes.
initDashboardLayout({
  secoes: [
    { chave: 'kpis', grid: document.getElementById('d2Kpis'), attr: 'kpi', compacto: true },
    { chave: 'widgets', grid: document.getElementById('d2Grid'), attr: 'widget', larguras: true, alturas: true },
  ],
  botao: document.getElementById('d2Personalizar'),
  userId: user?.id,
});
const plano = getPlano();
const free = isPlanoFree();
const planoPro = ['Pro', 'Max', 'Enterprise'].includes(plano);

if (free) {
  // Atalhos para telas de planos pagos ganham o selo do plano e, no clique, o aviso de upgrade.
  document.querySelectorAll('.d2-atalhos a[data-plano]').forEach(a => {
    a.classList.add('bloq');
    a.title += ` — disponível no plano ${a.dataset.plano}`;
    a.addEventListener('click', e => {
      e.preventDefault();
      showUpgradeToast(a.dataset.plano);
    });
  });
}

// ── Helpers ────────────────────────────────────────────
const $ = id => document.getElementById(id);
const brl = v => brlOuTraco(v || 0);
const brlCompact = v => (v || 0) >= 10000
  ? 'R$ ' + (v / 1000).toLocaleString('pt-BR', { maximumFractionDigits: 1 }) + ' mil'
  : brl(v);
const pad = n => String(n).padStart(2, '0');
const isoDate = d => `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`;
const startOfDay = d => { const x = new Date(d); x.setHours(0, 0, 0, 0); return x; };
const addDays = (d, n) => { const x = new Date(d); x.setDate(x.getDate() + n); return x; };
// Prazos, eventos e andamentos estão na hora "de parede" de Brasília → dataParede (ver utils.js).
const daysUntil = iso => Math.round((startOfDay(dataParede(iso)) - startOfDay(new Date())) / 86400000);
const fmtDur = min => {
  if (!min) return '0h';
  const h = Math.floor(min / 60), m = min % 60;
  return h > 0 ? `${h}h${m ? ' ' + pad(m) + 'min' : ''}` : `${m}min`;
};
const plural = (n, s, p) => `${n} ${n === 1 ? s : p}`;
const lock = (el, titulo, texto, alvo = 'Plus') => {
  el.insertAdjacentHTML('beforeend', `<div class="d2-lock">
    <div style="font-size:24px">🔒</div>
    <strong>${esc(titulo)}</strong>
    <span>${esc(texto)} Disponível a partir do plano <b style="color:#f59e0b">${esc(alvo)}</b>.</span>
    <a href="/pages/assinatura.html">Fazer upgrade</a>
  </div>`);
};

function kpiBloqueado(linkId, valorId, subId, alvo) {
  $(linkId).classList.add('locked');
  $(linkId).href = '/pages/assinatura.html';
  $(valorId).textContent = '🔒';
  $(subId).textContent = `plano ${alvo}`;
}

function prazoChip(iso) {
  if (!iso) return '<span class="d2-chip gray">Sem prazo</span>';
  const d = daysUntil(iso);
  if (d < 0) return `<span class="d2-chip red">${plural(-d, 'dia', 'dias')} atrasada</span>`;
  if (d === 0) return '<span class="d2-chip red">Hoje</span>';
  if (d === 1) return '<span class="d2-chip amber">Amanhã</span>';
  if (d <= 5) return `<span class="d2-chip amber">em ${d} dias</span>`;
  return `<span class="d2-chip gray">${dataParede(iso).toLocaleDateString('pt-BR', { day: '2-digit', month: 'short' })}</span>`;
}

const PRIO_COR = { Urgente: '#dc2626', Alta: '#ea580c', Media: '#ca8a04', Baixa: '#94a3b8' };
const AREA_LABEL = {
  Civil: 'Civil', Trabalhista: 'Trabalhista', Criminal: 'Criminal', Tributario: 'Tributário',
  Previdenciario: 'Previdenciário', Administrativo: 'Administrativo', Consumidor: 'Consumidor',
  Familia: 'Família', Empresarial: 'Empresarial', Ambiental: 'Ambiental', Imobiliario: 'Imobiliário', Outro: 'Outro'
};
const FASE_LABEL = {
  Conhecimento: 'Conhecimento', Recursal: 'Recursal', Execucao: 'Execução', Cumprimento: 'Cumprimento',
  InqueritoPolicial: 'Inquérito Policial', InvestigacaoDefensiva: 'Invest. Defensiva', Outro: 'Outro'
};
const PRIO_LABEL = { Urgente: 'Urgente', Alta: 'Alta', Media: 'Média', Baixa: 'Baixa' };

// ── Atalhos: legenda mostra o nome do atalho sob o mouse/foco ─────────
const atalhoDica = $('d2AtalhoDica');
const atalhos = document.querySelector('.d2-atalhos');
const mostrarDica = e => { const a = e.target.closest('a'); if (a) atalhoDica.textContent = a.title; };
const limparDica = () => { atalhoDica.textContent = 'Criar novo…'; };
atalhos.addEventListener('mouseover', mostrarDica);
atalhos.addEventListener('focusin', mostrarDica);
atalhos.addEventListener('mouseleave', limparDica);
atalhos.addEventListener('focusout', limparDica);

// ── Hero ───────────────────────────────────────────────
const agora = new Date();
const hora = agora.getHours();
const saud = hora < 12 ? 'Bom dia' : hora < 18 ? 'Boa tarde' : 'Boa noite';
// Pronome de tratamento no início ("Dr. Sidney Morbidelli") fica junto do primeiro nome.
const [tratamento, ...resto] = (user?.nome ?? '').trim().split(/\s+/);
const ehTratamento = /^(dra?|sra?|srta|profa?|me|ma|exm[oa]|il?m[oa])\.?$/i.test(tratamento ?? '');
const primeiroNome = ehTratamento && resto.length ? `${tratamento} ${resto[0]}` : tratamento;
$('d2Saudacao').textContent = primeiroNome ? `${saud}, ${primeiroNome}!` : `${saud}!`;
$('d2Hoje').textContent = agora.toLocaleDateString('pt-BR', { weekday: 'long', day: 'numeric', month: 'long', year: 'numeric' });

const resumo = { prazosHoje: null, eventosHoje: null, atrasadas: null };
function renderResumo() {
  if (Object.values(resumo).some(v => v === null)) return;
  const partes = [];
  if (resumo.prazosHoje) partes.push(`<strong>${plural(resumo.prazosHoje, 'prazo vence', 'prazos vencem')} hoje</strong>`);
  if (resumo.eventosHoje) partes.push(`${plural(resumo.eventosHoje, 'compromisso', 'compromissos')} na agenda`);
  if (resumo.atrasadas) partes.push(`<span style="color:var(--d2-red);font-weight:600">${plural(resumo.atrasadas, 'item atrasado', 'itens atrasados')}</span>`);
  $('d2Resumo').innerHTML = partes.length
    ? `Hoje você tem ${partes.join(', ').replace(/, ([^,]*)$/, ' e $1')}.`
    : 'Nenhum prazo ou compromisso para hoje. Bom momento para adiantar o trabalho! ✨';
}

// ── Prazos e tarefas ───────────────────────────────────
const tt = { prazos: [], minhas: [], atrasadas: [], aba: 'prazos' };

// Totais e listas calculados no backend (GET /api/tarefas/dashboard), com
// limites de dia no horário de Brasília.
async function loadTarefas() {
  const d = await apiFetch('/tarefas/dashboard?dias=7&limite=15').catch(() => null);
  const tot = d?.totais ?? { abertas: 0, emAndamento: 0, atrasadas: 0, prazos: 0, prazosHoje: 0, prazosProximosDias: 0, minhas: 0 };
  tt.prazos = d?.prazos ?? [];
  tt.minhas = d?.minhas ?? [];
  tt.atrasadas = d?.atrasadas ?? [];

  $('ttPrazosN').textContent = tot.prazos || '';
  $('ttMinhasN').textContent = tot.minhas || '';
  $('ttAtrasadasN').textContent = tot.atrasadas || '';

  // KPIs
  $('kTarefas').textContent = tot.abertas;
  $('kTarefasSub').textContent = `${tot.emAndamento} em andamento`;
  $('kAtrasadas').textContent = tot.atrasadas;
  $('kPrazos').textContent = tot.prazosProximosDias;
  $('kPrazosSub').textContent = tot.prazosHoje ? `${plural(tot.prazosHoje, 'vence', 'vencem')} hoje` : 'nenhum vence hoje';

  resumo.prazosHoje = tot.prazosHoje;
  resumo.atrasadas = tot.atrasadas;
  renderResumo();
  renderTarefas();
}

function renderTarefas() {
  const list = tt[tt.aba];
  const el = $('ttList');
  if (!list.length) {
    el.innerHTML = `<div class="d2-empty">${tt.aba === 'atrasadas' ? '🎉 Nada atrasado. Tudo em dia!' : 'Nenhum item em aberto.'}</div>`;
    return;
  }
  el.innerHTML = list.map(t => {
    const meta = [
      t.origem === 'Evento' ? '📅 Prazo (agenda)' : t.tipo === 'Prazo' ? '⏳ Prazo' : null,
      t.numeroCNJProcesso ? esc(t.numeroCNJProcesso) : null,
      t.nomeContato ? esc(t.nomeContato) : null,
      t.nomeResponsavel ? '👤 ' + esc(t.nomeResponsavel) : null,
    ].filter(Boolean).join(' · ');
    // Prazos também vêm de eventos da agenda (origem "Evento"): sem status, então
    // não têm "concluir" e abrem na agenda em vez da tela de tarefas.
    const evento = t.origem === 'Evento';
    const href = evento
      ? `/pages/agenda.html?view=dia&date=${isoDate(dataParede(t.prazo))}&abrirId=${t.id}`
      : `/pages/tarefas.html?abrirId=${t.id}`;
    return `<div class="d2-item" data-id="${t.id}">
      <div class="d2-prio" style="background:${evento ? '#d97706' : PRIO_COR[t.prioridade] ?? '#94a3b8'}" title="${evento ? 'Prazo na agenda' : 'Prioridade ' + esc(t.prioridade)}"></div>
      ${evento
        ? '<span class="d2-check d2-check-evento" title="Prazo cadastrado na agenda">📅</span>'
        : `<button class="d2-check" data-concluir="${t.id}" title="Concluir" aria-label="Concluir ${esc(t.titulo)}"></button>`}
      <div class="d2-item-body">
        <a class="d2-item-title" href="${href}" title="${esc(t.titulo)}">${esc(t.titulo)}</a>
        <div class="d2-item-meta">${meta || '&nbsp;'}</div>
      </div>
      ${prazoChip(t.prazo)}
    </div>`;
  }).join('');
}

document.querySelectorAll('.d2-tab').forEach(b => b.addEventListener('click', () => {
  tt.aba = b.dataset.tt;
  document.querySelectorAll('.d2-tab').forEach(x => x.classList.toggle('active', x === b));
  renderTarefas();
}));

$('ttList').addEventListener('click', async e => {
  const btn = e.target.closest('[data-concluir]');
  if (!btn) return;
  const id = btn.dataset.concluir;
  btn.classList.add('done');
  btn.textContent = '✓';
  btn.disabled = true;
  try {
    await apiFetch(`/tarefas/${id}/concluir`, { method: 'POST' });
    const row = btn.closest('.d2-item');
    row.style.transition = 'opacity .3s';
    row.style.opacity = '0';
    setTimeout(() => loadTarefas(), 300);
  } catch (err) {
    btn.classList.remove('done');
    btn.textContent = '';
    btn.disabled = false;
    alert('Não foi possível concluir: ' + err.message);
  }
});

// ── Agenda: próximos 7 dias ────────────────────────────
// Janela móvel de 7 dias a partir de hoje (como os "Próximos eventos" do
// dashboard clássico). Sem dia selecionado, lista tudo da janela agrupado por
// dia; clicar num dia filtra, clicar de novo volta para a lista completa.
const ag = { inicio: startOfDay(new Date()), selecionado: null, eventos: [] };
const hm = d => d.toLocaleTimeString('pt-BR', { hour: '2-digit', minute: '2-digit' });

async function loadAgenda(inicio = ag.inicio) {
  ag.inicio = startOfDay(inicio);
  ag.selecionado = null;
  const fim = addDays(ag.inicio, 7);
  renderSemana();
  $('agEvents').innerHTML = '<div class="d2-skel"></div><div class="d2-skel"></div>';
  const itens = await apiFetch(`/agenda?de=${isoLocal(ag.inicio)}&ate=${isoLocal(fim)}`).catch(() => []);
  ag.eventos = (itens ?? [])
    .filter(e => e.tipo !== 'Tarefa')
    .sort((a, b) => dataParede(a.dataHora) - dataParede(b.dataHora));
  renderSemana();
  renderEventos();

  const hoje = startOfDay(new Date());
  if (+ag.inicio === +hoje && resumo.eventosHoje === null) {
    const doDia = ag.eventos.filter(e => isoDate(dataParede(e.dataHora)) === isoDate(hoje));
    resumo.eventosHoje = doDia.length;
    $('kEventos').textContent = doDia.length;
    const futuros = doDia.filter(e => dataParede(e.dataHora) > new Date());
    $('kEventosSub').textContent = futuros.length
      ? `próximo às ${hm(dataParede(futuros[0].dataHora))}`
      : doDia.length ? 'todos já passaram' : `${plural(ag.eventos.length, 'compromisso', 'compromissos')} em 7 dias`;
    renderResumo();
  }
}

function renderSemana() {
  const hoje = startOfDay(new Date());
  const hojeIso = isoDate(hoje);
  const selIso = ag.selecionado ? isoDate(ag.selecionado) : null;
  const comEvento = new Set(ag.eventos.map(e => isoDate(dataParede(e.dataHora))));
  const fmt = d => d.toLocaleDateString('pt-BR', { day: 'numeric', month: 'short' }).replace('.', '');
  $('agMes').textContent = +ag.inicio === +hoje
    ? '📅 Próximos 7 dias'
    : `📅 ${fmt(ag.inicio)} – ${fmt(addDays(ag.inicio, 6))}`;
  $('agWeek').innerHTML = Array.from({ length: 7 }, (_, i) => {
    const d = addDays(ag.inicio, i);
    const iso = isoDate(d);
    const cls = ['d2-day', iso === hojeIso && 'today', iso === selIso && 'selected', comEvento.has(iso) && 'has'].filter(Boolean).join(' ');
    return `<button class="${cls}" data-dia="${iso}" aria-pressed="${iso === selIso}">
      <span class="d2-day-dow">${d.toLocaleDateString('pt-BR', { weekday: 'short' }).replace('.', '')}</span>
      <span class="d2-day-num">${d.getDate()}</span>
      <span class="d2-day-dot"></span>
    </button>`;
  }).join('');
}

function rotuloDia(d) {
  const diff = Math.round((startOfDay(d) - startOfDay(new Date())) / 86400000);
  if (diff === 0) return 'Hoje';
  if (diff === 1) return 'Amanhã';
  return d.toLocaleDateString('pt-BR', { weekday: 'short', day: 'numeric', month: 'short' }).replace(/\./g, '');
}

function renderEventos() {
  const selIso = ag.selecionado ? isoDate(ag.selecionado) : null;
  const list = selIso ? ag.eventos.filter(e => isoDate(dataParede(e.dataHora)) === selIso) : ag.eventos;
  const el = $('agEvents');
  if (!list.length) {
    const dataAgenda = selIso ?? isoDate(ag.inicio);
    el.innerHTML = `<div class="d2-empty">${selIso ? 'Nenhum compromisso neste dia.' : 'Nenhum compromisso nestes 7 dias.'}<br>
      <a href="/pages/agenda.html?view=${selIso ? 'dia' : 'semana'}&date=${dataAgenda}" style="font-weight:600">Abrir agenda</a></div>`;
    return;
  }
  let ultimoDia = null;
  el.innerHTML = list.map(ev => {
    const dt = dataParede(ev.dataHora);
    const iso = isoDate(dt);
    const grupo = !selIso && iso !== ultimoDia ? `<div class="d2-day-group">${rotuloDia(dt)}</div>` : '';
    ultimoDia = iso;
    const fim = ev.dataHoraFim ? dataParede(ev.dataHoraFim) : null;
    const meta = [esc(ev.tipo), fim ? `até ${hm(fim)}` : null, ev.local ? '📍 ' + esc(ev.local) : null, ev.numeroCNJProcesso ? esc(ev.numeroCNJProcesso) : null].filter(Boolean).join(' · ');
    return `${grupo}<a class="d2-event" href="/pages/agenda.html?view=dia&date=${iso}&abrirId=${ev.id}">
      <div class="d2-event-time">${hm(dt)}</div>
      <div class="d2-event-bar" style="background:${esc(ev.cor || 'var(--color-primary)')}"></div>
      <div class="d2-item-body">
        <div class="d2-item-title">${esc(ev.titulo)}</div>
        <div class="d2-item-meta" title="${meta.replace(/"/g, '&quot;')}">${meta}</div>
      </div>
    </a>`;
  }).join('');
}

$('agWeek').addEventListener('click', e => {
  const b = e.target.closest('[data-dia]');
  if (!b) return;
  const dia = new Date(b.dataset.dia + 'T00:00:00');
  ag.selecionado = ag.selecionado && isoDate(ag.selecionado) === b.dataset.dia ? null : dia;
  renderSemana();
  renderEventos();
});
$('agPrev').addEventListener('click', () => loadAgenda(addDays(ag.inicio, -7)));
$('agNext').addEventListener('click', () => loadAgenda(addDays(ag.inicio, 7)));
$('agHoje').addEventListener('click', () => loadAgenda(new Date()));

// ── Notificações ───────────────────────────────────────
const NOTIF_ICON = { PrazoTarefa: '⏳', PrazoEvento: '📅', TrialExpirando: '⭐', Geral: '📣', NovoAndamento: '⚖️', TarefaAtrasada: '⚠️' };

function tempoRelativo(iso) {
  const min = Math.round((Date.now() - new Date(iso)) / 60000);
  if (min < 1) return 'agora';
  if (min < 60) return `há ${min} min`;
  const h = Math.round(min / 60);
  if (h < 24) return `há ${h}h`;
  const d = Math.round(h / 24);
  return d === 1 ? 'ontem' : `há ${d} dias`;
}

async function loadNotificacoes() {
  const list = await apiFetch('/notificacoes').catch(() => []) ?? [];
  const el = $('ntList');
  $('ntCount').textContent = list.length;
  $('ntCount').style.display = list.length ? '' : 'none';
  $('ntLimpar').style.display = list.length ? '' : 'none';
  if (!list.length) {
    el.innerHTML = '<div class="d2-empty">🔕 Nenhuma notificação nova.</div>';
    return;
  }
  el.innerHTML = list.slice(0, 10).map(n => `
    <div class="d2-notif">
      <div class="d2-notif-icon">${NOTIF_ICON[n.tipo] ?? '🔔'}</div>
      <div class="d2-item-body">
        <div style="display:flex;justify-content:space-between;gap:8px">
          ${n.url
            ? `<a class="d2-item-title" href="${esc(n.url)}" data-lida="${n.id}">${esc(n.titulo)}</a>`
            : `<span class="d2-item-title">${esc(n.titulo)}</span>`}
          <span class="d2-muted" style="font-size:11px;white-space:nowrap">${tempoRelativo(n.criadaEm)}</span>
        </div>
        <div class="d2-notif-msg">${esc(n.mensagem)}</div>
        <button class="d2-link-btn" data-lida="${n.id}" style="margin-top:4px;font-size:11px">Marcar como lida</button>
      </div>
    </div>`).join('');
}

$('ntList').addEventListener('click', async e => {
  const t = e.target.closest('[data-lida]');
  if (!t) return;
  const isLink = t.tagName === 'A';
  if (isLink) e.preventDefault();
  await apiFetch(`/notificacoes/${t.dataset.lida}/lida`, { method: 'POST' }).catch(() => null);
  if (isLink) { window.location.href = t.getAttribute('href'); return; }
  loadNotificacoes();
});
$('ntLimpar').addEventListener('click', async () => {
  await apiFetch('/notificacoes/marcar-todas-lidas', { method: 'POST' }).catch(() => null);
  loadNotificacoes();
});

// ── Últimas movimentações + KPI ────────────────────────
const TIPO_ANDAMENTO_ICON = { Despacho: '📝', Decisao: '⚖️', Sentenca: '🏛️', Acordao: '🏛️', Audiencia: '🗣️', Peticao: '📄', Intimacao: '📬', Publicacao: '📰', Outro: '•' };

async function loadProcessos() {
  const [ativos, ultimos] = await Promise.all([
    apiFetch('/processos?status=Ativo&pageSize=1').catch(() => null),
    apiFetch('/processos/ultimos-andamentos?limite=6').catch(() => null),
  ]);
  $('kProcessos').textContent = ativos?.total ?? 0;
  const items = ultimos ?? [];
  const el = $('prList');
  if (!items.length) {
    el.innerHTML = '<div class="d2-empty">Nenhuma movimentação registrada.<br><a href="/pages/processos.html" style="font-weight:600">Cadastrar ou importar por OAB</a></div>';
    return;
  }
  el.innerHTML = items.map(p => {
    const recente = daysUntil(p.dataAndamento) >= -7;
    const meta = [p.nomeCliente, AREA_LABEL[p.areaDireito] ?? p.areaDireito, p.tribunal].filter(Boolean).map(esc).join(' · ');
    return `<a class="d2-item" href="/pages/processo-detalhe.html?id=${p.processoId}" style="color:inherit">
      <div class="d2-item-body">
        <div style="display:flex;justify-content:space-between;gap:8px;align-items:center">
          <span class="d2-item-title" style="font-variant-numeric:tabular-nums">${esc(p.numeroCNJ)}</span>
          <span class="d2-chip ${recente ? 'green' : 'gray'}" title="${dataParede(p.dataAndamento).toLocaleString('pt-BR')}">${tempoRelativo(dataParede(p.dataAndamento))}</span>
        </div>
        <div class="d2-notif-msg" style="margin-top:2px;color:var(--color-text)">${TIPO_ANDAMENTO_ICON[p.tipoAndamento] ?? '•'} ${esc(p.descricaoAndamento)}</div>
        ${meta ? `<div class="d2-item-meta">${meta}</div>` : ''}
      </div>
    </a>`;
  }).join('');
}

// ── Processos favoritos (do usuário logado) ────────────
const STATUS_CHIP = { Ativo: 'green', Suspenso: 'amber', Arquivado: 'gray', Encerrado: 'gray' };

async function loadFavoritos() {
  const r = await apiFetch('/processos?favoritos=true&pageSize=8').catch(() => null);
  const items = r?.items ?? [];
  const count = $('fvCount');
  count.textContent = r?.total ?? 0;
  count.style.display = r?.total ? '' : 'none';
  const el = $('fvList');
  if (!items.length) {
    el.innerHTML = '<div class="d2-empty">Nenhum processo favorito.<br>Marque com ☆ na <a href="/pages/processos.html" style="font-weight:600">lista de processos</a>.</div>';
    return;
  }
  el.innerHTML = items.map(p => {
    const meta = [p.nomeCliente, AREA_LABEL[p.areaDireito] ?? p.areaDireito, p.tribunal].filter(Boolean).map(esc).join(' · ');
    return `<a class="d2-item" href="/pages/processo-detalhe.html?id=${p.id}" style="color:inherit">
      <div class="d2-item-body">
        <div style="display:flex;justify-content:space-between;gap:8px;align-items:center">
          <span class="d2-item-title" style="font-variant-numeric:tabular-nums">${esc(p.numeroCNJ)}</span>
          <span class="d2-chip ${STATUS_CHIP[p.status] ?? 'gray'}">${esc(p.status)}</span>
        </div>
        ${meta ? `<div class="d2-item-meta">${meta}</div>` : ''}
      </div>
    </a>`;
  }).join('');
}

// ── Indicadores (Plus+): processos, tarefas, financeiro, timesheet ──
/** Barras horizontais de { label, count }; `rotulos` traduz o label e `cores` colore por label. */
function renderBars(el, itens, { rotulos = {}, cores = {}, fmt = String, vazio = 'Sem dados', max: limite = 6 } = {}) {
  if (!itens?.length) { el.innerHTML = `<div class="d2-empty">${esc(vazio)}</div>`; return; }
  const max = Math.max(...itens.map(x => x.count), 1);
  el.innerHTML = itens.slice(0, limite).map(x => {
    const nome = rotulos[x.label] ?? x.label;
    const cor = cores[x.label] ? `;background:${cores[x.label]}` : '';
    return `
    <div class="d2-bar-row" title="${esc(nome)}: ${esc(fmt(x.count))}">
      <div class="d2-bar-label">${esc(nome)}</div>
      <div class="d2-bar-track"><div class="d2-bar-fill" style="width:${Math.max(3, x.count / max * 100)}%${cor}"></div></div>
      <div class="d2-bar-val">${esc(fmt(x.count))}</div>
    </div>`;
  }).join('');
}

function renderTarefasResumo(t) {
  $('trPendentes').textContent = t.pendentes;
  $('trAndamento').textContent = t.emAndamento;
  $('trConcluidasMes').textContent = t.concluidasEsteMes;
  $('trAtrasadas').textContent = t.atrasadas;
  renderBars($('trPrioridade'), t.porPrioridade, { rotulos: PRIO_LABEL, cores: PRIO_COR, vazio: 'Nenhuma tarefa aberta.' });
}

function renderTimesheet(ts) {
  $('tsHoras').textContent = fmtDur(ts.minutosEsteMes);
  $('tsRegistros').textContent = ts.totalRegistrosEsteMes;
  renderBars($('tsTop'), ts.topProcessos, { fmt: fmtDur, max: 5, vazio: 'Nenhuma hora lançada em processos este mês.' });
}

function renderFinanceiroMes(f) {
  $('fmReceitas').textContent = brlCompact(f.totalReceitasMes);
  $('fmDespesas').textContent = brlCompact(f.totalDespesasMes);
  $('fmAReceber').textContent = brlCompact(f.receitasPendentes);
  $('fmAPagar').textContent = brlCompact(f.despesasPendentes);
}

function renderFinanceiro(f) {
  const saldo = $('fnSaldo');
  saldo.textContent = brlCompact(f.saldoMes);
  saldo.style.color = f.saldoMes >= 0 ? 'var(--d2-green)' : 'var(--d2-red)';
  $('fnVencidos').textContent = brlCompact(f.receitasVencidas);
  const meses = f.ultimosSeisMeses ?? [];
  const max = Math.max(...meses.flatMap(m => [m.receitas, m.despesas]), 1);
  $('fnTrend').innerHTML = meses.map(m => `
    <div class="d2-trend-col">
      <div class="d2-trend-bars">
        <div style="height:${m.receitas / max * 100}%;background:var(--color-primary)" title="${esc(m.mes)} — Receitas: ${brl(m.receitas)}"></div>
        <div style="height:${m.despesas / max * 100}%;background:#94a3b8" title="${esc(m.mes)} — Despesas: ${brl(m.despesas)}"></div>
      </div>
      <div class="d2-trend-label">${esc(m.mes)}</div>
    </div>`).join('');
}

async function loadIndicadores() {
  if (free) {
    lock($('finCard'), 'Financeiro do escritório', 'Receitas, despesas e saldo mês a mês.');
    lock($('trCard'), 'Resumo de tarefas', 'Tarefas por status e prioridade.');
    lock($('tsCard'), 'Timesheet do mês', 'Horas lançadas e processos que mais consomem tempo.');
    lock($('faCard'), 'Processos por fase', 'Distribuição dos processos ativos por fase processual.');
    lock($('fmCard'), 'Financeiro do mês', 'Receitas, despesas, contas a receber e a pagar.');
    $('arBars').innerHTML = '<div class="d2-empty">Distribuição por área disponível no plano Plus.</div>';
    $('tmMes').textContent = '—';
    $('tmMesAnt').textContent = '—';
    kpiBloqueado('kSaldoLink', 'kSaldo', 'kSaldoSub', 'Plus');
    kpiBloqueado('kHorasLink', 'kHoras', 'kHorasSub', 'Plus');
    kpiBloqueado('kEncLink', 'kEncerrados', 'kEncerradosSub', 'Plus');
    kpiBloqueado('kSusLink', 'kSuspensos', 'kSuspensosSub', 'Plus');
    return;
  }
  try {
    const d = await apiFetch('/indicadores');
    const saldo = d.financeiro.saldoMes;
    $('kSaldo').textContent = brlCompact(saldo);
    $('kSaldo').style.color = saldo >= 0 ? 'var(--d2-green)' : 'var(--d2-red)';
    $('kSaldoSub').textContent = d.financeiro.receitasVencidas
      ? `${brlCompact(d.financeiro.receitasVencidas)} a receber vencido`
      : 'receitas − despesas pagas';
    const ts = d.timesheet;
    $('kHoras').textContent = fmtDur(ts.minutosEsteMes);
    const dif = ts.minutosEsteMes - ts.minutosMesAnterior;
    $('kHorasSub').textContent = ts.minutosMesAnterior ? `${dif >= 0 ? '+' : '−'}${fmtDur(Math.abs(dif))} vs. mês anterior` : 'neste mês';
    $('kProcessosSub').textContent = d.processos.novosEsteMes
      ? `+${d.processos.novosEsteMes} este mês`
      : `${d.processos.suspensos} suspensos`;
    $('kEncerrados').textContent = d.processos.encerrados;
    $('kSuspensos').textContent = d.processos.suspensos;
    renderBars($('arBars'), d.processos.porArea, { rotulos: AREA_LABEL });
    renderBars($('faBars'), d.processos.porFase, { rotulos: FASE_LABEL, vazio: 'Nenhum processo ativo.' });
    renderTarefasResumo(d.tarefas);
    renderTimesheet(d.timesheet);
    renderFinanceiro(d.financeiro);
    renderFinanceiroMes(d.financeiro);
    $('tmMes').textContent = fmtDur(d.timesheet.minutosEsteMes);
    $('tmMesAnt').textContent = fmtDur(d.timesheet.minutosMesAnterior);
  } catch {
    for (const id of ['arBars', 'faBars', 'trPrioridade', 'tsTop']) {
      $(id).innerHTML = '<div class="d2-empty">Não foi possível carregar.</div>';
    }
  }
}

// ── Honorários de contrato (Plus+) ─────────────────────
async function loadHonorarios() {
  if (free) {
    lock($('honCard'), 'Honorários de contrato', 'Parcelas, inadimplência e meta mensal de recebimento.');
    kpiBloqueado('kHonLink', 'kHonorarios', 'kHonorariosSub', 'Plus');
    return;
  }
  try {
    const h = await apiFetch('/honorarios/contratos/dashboard');
    $('kHonorarios').textContent = brlCompact(h.totalAReceber);
    $('kHonorariosSub').innerHTML = h.totalEmAtraso
      ? `<span style="color:var(--d2-red);font-weight:600">${brlCompact(h.totalEmAtraso)} em atraso</span>`
      : 'nada em atraso';
    $('honRecebido').textContent = brl(h.recebidoNoMes);
    $('honAReceber').textContent = brlCompact(h.totalAReceber);
    $('honAtraso').textContent = brlCompact(h.totalEmAtraso);
    $('honAtivos').textContent = h.contratosAtivos;
    $('honAtrasados').textContent = h.contratosAtrasados;

    const meta = h.metaMensal;
    const pct = meta ? Math.round((h.alcancadoMes / meta) * 100) : null;
    $('honPct').textContent = pct === null ? '—' : `${pct}%`;
    $('honMeta').innerHTML = meta
      ? `de ${brl(meta)} (meta)`
      : 'Meta mensal não definida · <a href="/pages/honorarios-config.html">definir</a>';
    const arc = $('honRingArc');
    const C = 2 * Math.PI * 40;
    arc.setAttribute('stroke-dasharray', C.toFixed(1));
    arc.style.strokeDashoffset = (C * (1 - Math.min(pct ?? 0, 100) / 100)).toFixed(1);
    if (pct !== null && pct >= 100) arc.setAttribute('stroke', 'var(--d2-green)');

    const inad = (h.inadimplentes ?? []).slice(0, 3);
    $('honInadimplentes').innerHTML = inad.length
      ? `<div class="d2-mini-label" style="margin-bottom:2px">Maiores inadimplências</div>` + inad.map(i => `
          <a class="d2-row-line" href="/pages/honorarios-contrato-detalhe.html?id=${i.contratoId}" style="color:inherit">
            <span style="overflow:hidden;text-overflow:ellipsis;white-space:nowrap">${esc(i.nomeContato)} <span class="d2-muted">· ${plural(i.parcelasVencidas, 'parcela', 'parcelas')}</span></span>
            <strong style="color:var(--d2-red);white-space:nowrap">${brl(i.valorEmAtraso)}</strong>
          </a>`).join('')
      : '';
  } catch {
    $('kHonorarios').textContent = '—';
  }
}

// ── Calculadora de prazos (Plus+) ──────────────────────
let calcTipo = 'DiasUteis';
$('calcInicio').value = isoDate(new Date());
$('calcTipo').addEventListener('click', e => {
  const b = e.target.closest('[data-tipo]');
  if (!b) return;
  calcTipo = b.dataset.tipo;
  $('calcTipo').querySelectorAll('button').forEach(x => x.classList.toggle('active', x === b));
});
document.querySelectorAll('.d2-presets button').forEach(b => b.addEventListener('click', () => {
  $('calcDias').value = b.dataset.d;
  calcular();
}));
$('calcBtn').addEventListener('click', calcular);
$('calcDias').addEventListener('keydown', e => { if (e.key === 'Enter') calcular(); });

async function calcular() {
  const inicio = $('calcInicio').value;
  const dias = parseInt($('calcDias').value, 10);
  const out = $('calcResult');
  if (!inicio || !dias || dias < 1) {
    out.style.display = '';
    out.textContent = 'Informe a data de início e a quantidade de dias.';
    return;
  }
  const btn = $('calcBtn');
  btn.disabled = true;
  btn.textContent = 'Calculando…';
  try {
    const r = await apiFetch('/prazos/calcular', {
      method: 'POST',
      body: { dataInicio: inicio, quantidadeDias: dias, tipoCalculo: calcTipo },
    });
    const final = new Date(r.dataFinal.substring(0, 10) + 'T12:00:00');
    const faltam = daysUntil(final);
    const feriados = r.feriadosNoIntervalo ?? [];
    out.style.display = '';
    out.innerHTML = `
      <div style="opacity:.85">Prazo final</div>
      <strong>${final.toLocaleDateString('pt-BR', { weekday: 'long', day: '2-digit', month: 'long', year: 'numeric' })}</strong>
      <div style="opacity:.85">${faltam < 0 ? `venceu há ${plural(-faltam, 'dia', 'dias')}` : faltam === 0 ? 'vence hoje' : `faltam ${plural(faltam, 'dia corrido', 'dias corridos')}`}
      ${feriados.length ? ` · ${plural(feriados.length, 'feriado considerado', 'feriados considerados')}` : ''}</div>
      ${feriados.length ? `<div style="font-size:11px;opacity:.75;margin-top:4px">${feriados.slice(0, 4).map(esc).join(' · ')}${feriados.length > 4 ? '…' : ''}</div>` : ''}`;
  } catch (err) {
    out.style.display = '';
    out.textContent = err.message || 'Erro ao calcular.';
  } finally {
    btn.disabled = false;
    btn.textContent = 'Calcular';
  }
}
if (free) lock($('calcCard'), 'Calculadora de prazos', 'Dias úteis com feriados nacionais e forenses.');

// ── Cronômetro (timesheet) ─────────────────────────────
let tmAtivo = null;
let tmInterval = null;

function tick() {
  if (!tmAtivo) { $('tmClock').textContent = '00:00:00'; return; }
  const s = Math.max(0, Math.floor((Date.now() - new Date(tmAtivo.inicio)) / 1000));
  $('tmClock').textContent = `${pad(Math.floor(s / 3600))}:${pad(Math.floor(s / 60) % 60)}:${pad(s % 60)}`;
}

function renderTimer() {
  clearInterval(tmInterval);
  const clock = $('tmClock');
  const btn = $('tmBtn');
  const desc = $('tmDesc');
  if (tmAtivo) {
    clock.classList.add('running');
    const ctx = tmAtivo.numeroProcesso || tmAtivo.tituloTarefa;
    $('tmStatus').innerHTML = `<span class="d2-pulse"></span> Em andamento${ctx ? ' · ' + esc(ctx) : ''}`;
    desc.value = tmAtivo.descricao ?? '';
    btn.textContent = '■ Parar';
    btn.className = 'btn btn-danger btn-sm';
    tmInterval = setInterval(tick, 1000);
  } else {
    clock.classList.remove('running');
    $('tmStatus').textContent = 'Nenhum registro em andamento';
    btn.textContent = '▶ Iniciar';
    btn.className = 'btn btn-primary btn-sm';
  }
  tick();
}

async function loadTimer() {
  tmAtivo = await apiFetch('/timesheet/ativo').catch(() => null);
  renderTimer();
}

$('tmBtn').addEventListener('click', async () => {
  const btn = $('tmBtn');
  btn.disabled = true;
  const descricao = $('tmDesc').value.trim() || null;
  try {
    if (tmAtivo) {
      await apiFetch('/timesheet/parar', { method: 'POST', body: { descricao } });
      tmAtivo = null;
      $('tmDesc').value = '';
      if (!free) loadIndicadores();
    } else {
      tmAtivo = await apiFetch('/timesheet/iniciar', { method: 'POST', body: { descricao } });
    }
    renderTimer();
  } catch (err) {
    alert(err.message);
  } finally {
    btn.disabled = false;
  }
});

// ── Aniversariantes ────────────────────────────────────
async function loadAniversariantes() {
  const list = await apiFetch(`/contatos/aniversariantes?mes=${new Date().getMonth() + 1}`).catch(() => []) ?? [];
  const el = $('anList');
  const hojeDia = new Date().getDate();
  const ordenados = list
    .map(c => ({ ...c, dia: parseInt(String(c.dataNascimento).substring(8, 10), 10) }))
    .sort((a, b) => a.dia - b.dia);
  // Próximos primeiro; os que já passaram vão para o fim
  const prox = [...ordenados.filter(c => c.dia >= hojeDia), ...ordenados.filter(c => c.dia < hojeDia)].slice(0, 5);
  if (!prox.length) { el.innerHTML = '<div class="d2-muted">Nenhum aniversariante este mês.</div>'; return; }
  el.innerHTML = prox.map(c => `
    <a class="d2-row-line" href="/pages/contato-detalhe.html?id=${c.id}" style="color:inherit">
      <span style="overflow:hidden;text-overflow:ellipsis;white-space:nowrap">${esc(c.nome)}</span>
      <span class="d2-chip ${c.dia === hojeDia ? 'green' : c.dia < hojeDia ? 'gray' : 'amber'}">${c.dia === hojeDia ? '🎉 Hoje' : `dia ${c.dia}`}</span>
    </a>`).join('');
}

// ── Publicações (Pro+) — substitui subtítulo do KPI de processos
async function loadPublicacoes() {
  if (!planoPro) { kpiBloqueado('kPubLink', 'kPublicacoes', 'kPublicacoesSub', 'Pro'); return; }
  const r = await apiFetch('/publicacoes/nao-lidas/count').catch(() => null);
  $('kPublicacoes').textContent = r?.count ?? '—';
}

async function loadContatos() {
  const r = await apiFetch('/contatos?pageSize=1').catch(() => null);
  $('kContatos').textContent = r?.total ?? '—';
}

// Importação por OAB concluída em background (barra inferior): recarrega o que muda
// com os processos novos e a notificação de conclusão, sem exigir F5.
window.addEventListener('importacao:concluida', () => {
  loadProcessos();
  loadFavoritos();
  loadIndicadores();
  loadNotificacoes();
});

// ── Boot ───────────────────────────────────────────────
await Promise.all([
  loadTarefas(),
  loadAgenda(),
  loadNotificacoes(),
  loadProcessos(),
  loadFavoritos(),
  loadIndicadores(),
  loadHonorarios(),
  loadTimer(),
  loadAniversariantes(),
  loadContatos(),
]);
loadPublicacoes();

