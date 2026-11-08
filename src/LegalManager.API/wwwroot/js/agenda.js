// Agenda (/pages/agenda.html): semana/dia em grade com cartões em tons pastel (sobrepostos lado a
// lado) e linha do "agora"; mês; Itinerário (faixas por responsável ou tipo, prazos como marcos);
// lista. Arrastar (ímã de 15 min), esticar pela borda e desenhar num horário vazio para criar —
// eventos via PUT /eventos/{id}, prazos/tarefas via PATCH /tarefas/{id}/agenda (prazo + duração).
// Filtros por tipo, "Só os meus", atalhos de teclado e desfazer.
import { initLayout } from './layout.js';
import { apiFetch, getUser } from './api.js';
import { esc, dataParede, isoLocal, abrirNovoPelaUrl } from './utils.js';

initLayout();

// ── Constantes ──────────────────────────────────────────────────────────
const TIPOS = ['Audiencia', 'Reuniao', 'Pericia', 'Despacho', 'Outro', 'Tarefa'];
const TIPO_LABEL = {
  Audiencia: 'Audiência', Reuniao: 'Reunião', Pericia: 'Perícia',
  Prazo: 'Prazo', Despacho: 'Despacho', Outro: 'Outro', Tarefa: 'Prazos e tarefas'
};
const VIEWS = ['dia', 'semana', 'mes', 'itinerario', 'lista'];
const DIAS_CURTOS = ['Dom', 'Seg', 'Ter', 'Qua', 'Qui', 'Sex', 'Sáb'];
const MESES = ['Janeiro', 'Fevereiro', 'Março', 'Abril', 'Maio', 'Junho', 'Julho', 'Agosto', 'Setembro', 'Outubro', 'Novembro', 'Dezembro'];
const PALETA = ['#6366f1', '#0ea5e9', '#10b981', '#f59e0b', '#ec4899', '#8b5cf6', '#14b8a6', '#f97316'];
const HORA_PX = 52;              // altura de 1h na grade (dia/semana)
const PASSO_MIN = 15;            // ímã ao arrastar/esticar/desenhar
const HORA_INICIAL = 7;          // a grade abre rolada até aqui (ou até o 1º item, se antes)
const DIAS_ITINERARIO = 14;
const LINHA_IT = 34;             // altura de cada linha de barras no itinerário
const diaPxIt = () => (celular ? 104 : 136);

// ── Preferências (só conveniência local; falha silenciosa) ───────────────
const lerPref = (k, padrao) => { try { const v = localStorage.getItem(`agenda.${k}`); return v == null ? padrao : JSON.parse(v); } catch { return padrao; } };
const gravarPref = (k, v) => { try { localStorage.setItem(`agenda.${k}`, JSON.stringify(v)); } catch { /* storage bloqueado */ } };

// ── Estado ──────────────────────────────────────────────────────────────
const _params = new URLSearchParams(location.search);
const celular = matchMedia('(max-width: 640px)').matches;
let view = VIEWS.includes(_params.get('view')) ? _params.get('view') : (celular ? 'dia' : 'semana');
let data = _params.get('date') ? new Date(_params.get('date') + 'T00:00:00') : hojeZero();
if (Number.isNaN(data.getTime())) data = hojeZero();
let pendingAbrirId = _params.get('abrirId');

let itens = [];
let seqCarga = 0;
const ocultos = new Set(lerPref('ocultos', []));
let soMeus = lerPref('soMeus', false);
let agruparPor = lerPref('agrupar', 'responsavel');

let editingId = null;
let editingProcessoId = null;
let fantasmaAtual = null;
let suprimirAte = 0;

// ── Datas ───────────────────────────────────────────────────────────────
function hojeZero() { const d = new Date(); d.setHours(0, 0, 0, 0); return d; }
function diaZero(d) { const x = new Date(d); x.setHours(0, 0, 0, 0); return x; }
function addDias(d, n) { const x = new Date(d); x.setDate(x.getDate() + n); return x; }
function addMin(d, n) { return new Date(d.getTime() + n * 60000); }
const pad = n => String(n).padStart(2, '0');
const ymd = d => `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`;
const deYmd = s => new Date(`${s}T00:00:00`);
const mesmoDia = (a, b) => a && b && a.getFullYear() === b.getFullYear() && a.getMonth() === b.getMonth() && a.getDate() === b.getDate();
const diasEntre = (a, b) => Math.round((diaZero(b) - diaZero(a)) / 86400000);
const hm = d => `${pad(d.getHours())}:${pad(d.getMinutes())}`;
const hmMin = m => `${pad(Math.floor(m / 60) % 24)}:${pad(m % 60)}`;
const minutosDoDia = d => d.getHours() * 60 + d.getMinutes();
const clamp = (v, a, b) => Math.max(a, Math.min(b, v));
const cap = s => s.charAt(0).toUpperCase() + s.slice(1);
const duracaoTxt = min => { const h = Math.floor(min / 60), m = Math.round(min % 60); return h ? `${h}h${m ? pad(m) : ''}` : `${m} min`; };

function semanaIso(d) {
  const t = new Date(Date.UTC(d.getFullYear(), d.getMonth(), d.getDate()));
  const dia = t.getUTCDay() || 7;
  t.setUTCDate(t.getUTCDate() + 4 - dia);
  const ano = new Date(Date.UTC(t.getUTCFullYear(), 0, 1));
  return Math.ceil(((t - ano) / 86400000 + 1) / 7);
}

// Período da visão. `fim` é exclusivo. No mês, a grade cobre semanas inteiras.
function periodo(v, d) {
  const ini = diaZero(d);
  if (v === 'dia') return { ini, fim: addDias(ini, 1) };
  if (v === 'semana' || v === 'itinerario') {
    const dom = addDias(ini, -ini.getDay());
    return { ini: dom, fim: addDias(dom, v === 'semana' ? 7 : DIAS_ITINERARIO) };
  }
  const mesIni = new Date(ini.getFullYear(), ini.getMonth(), 1);
  const mesFim = new Date(ini.getFullYear(), ini.getMonth() + 1, 1);
  if (v === 'lista') return { ini: mesIni, fim: mesFim, mesIni, mesFim };
  return { ini: addDias(mesIni, -mesIni.getDay()), fim: addDias(mesFim, (7 - mesFim.getDay()) % 7), mesIni, mesFim };
}

function diasDe(p) { const r = []; for (let d = new Date(p.ini); d < p.fim; d = addDias(d, 1)) r.push(d); return r; }

// ── Itens ───────────────────────────────────────────────────────────────
// Cor do item: a escolhida na tarefa (ou a do tipo de evento), vinda do backend. Só "#rrggbb".
const corItem = it => /^#[0-9a-f]{6}$/i.test(it.cor ?? '') ? it.cor : '#6b7280';

function normalizar(it, k) {
  const tarefa = it.tipo === 'Tarefa';
  return {
    ...it, k,
    ini: dataParede(it.dataHora),
    fim: it.dataHoraFim ? dataParede(it.dataHoraFim) : null,
    tarefa,
    // Prevista ainda não existe como tarefa: não dá para mover.
    editavel: !it.prevista,
    atrasada: tarefa && it.status === 'Atrasada',
    passado: !tarefa && it.status === 'Passado',
  };
}

// Sem fim: evento ocupa 1h e tarefa 30 min, só para desenhar.
function fimVisual(it) {
  if (it.fim && it.fim > it.ini) return it.fim;
  return addMin(it.ini, it.tarefa ? 30 : 60);
}

const visiveis = () => itens.filter(it => it.ini && !ocultos.has(it.tipo));
const itemDe = el => itens.find(it => String(it.k) === el?.dataset.k);

function faixaHora(it) {
  if (it.tarefa && !it.fim) return `até ${hm(it.ini)}`;
  return it.fim ? `${hm(it.ini)} – ${hm(it.fim)}` : hm(it.ini);
}

function classesItem(it, base) {
  return [base, it.tarefa && 'tarefa', it.prevista && 'prevista', it.atrasada && 'atrasada',
    it.passado && 'passado', it.editavel && 'editavel'].filter(Boolean).join(' ');
}

const marcaTarefa = it => it.tarefa ? `<span class="a2-flag">${it.recorrenciaId ? '🔁' : '⚑'}</span>` : '';

// ── Carga ───────────────────────────────────────────────────────────────
function syncUrl() {
  const p = new URLSearchParams(location.search);
  p.set('view', view);
  p.set('date', ymd(data));
  p.delete('abrirId');
  history.replaceState(null, '', `${location.pathname}?${p}`);
}

async function carregar() {
  const p = periodo(view, data);
  syncUrl();
  marcarView();
  const params = new URLSearchParams({ de: isoLocal(p.ini), ate: isoLocal(new Date(p.fim - 1000)), previstas: 'true' });
  const eu = getUser();
  if (soMeus && eu?.id) params.set('responsavelId', eu.id);
  const pedido = ++seqCarga;
  try {
    const lista = await apiFetch(`/agenda?${params}`);
    if (pedido !== seqCarga) return;
    itens = (lista || []).map(normalizar);
  } catch (e) {
    if (pedido !== seqCarga) return;
    console.error(e);
    itens = [];
    aviso('Não foi possível carregar a agenda.', { erro: true });
  }
  render();
}

// ── Render geral ────────────────────────────────────────────────────────
// Mesma visão e mesmo período (ex.: depois de arrastar, filtrar ou salvar): a rolagem fica onde
// estava. Só ao trocar de visão ou de período ela vai para o ponto inicial (manhã, hoje...).
const ROLAGENS = ['a2Scroll', 'a2ItScroll', 'a2ListaScroll'];
let chaveRender = null;

function render() {
  fecharPopover();
  const p = periodo(view, data);
  const chave = `${view}|${ymd(p.ini)}`;
  const rolagem = chave === chaveRender
    ? Object.fromEntries(ROLAGENS.map(id => { const el = document.getElementById(id); return [id, el && [el.scrollTop, el.scrollLeft]]; }))
    : null;
  chaveRender = chave;
  renderCabecalho(p);
  renderFiltros();
  const c = document.getElementById('a2Conteudo');
  if (view === 'dia') c.innerHTML = renderGrade([diaZero(data)]);
  else if (view === 'semana') c.innerHTML = renderGrade(diasDe(p));
  else if (view === 'mes') c.innerHTML = renderMes(p);
  else if (view === 'itinerario') c.innerHTML = renderItinerario(p);
  else c.innerHTML = renderLista();
  if (rolagem) {
    atualizarAgora();
    ROLAGENS.forEach(id => {
      const el = document.getElementById(id);
      if (el && rolagem[id]) [el.scrollTop, el.scrollLeft] = rolagem[id];
    });
  } else depoisDeRender(c);
  renderDicas();
  abrirPendente(c);
}

function renderCabecalho(p) {
  const titulo = document.getElementById('a2Titulo');
  const sub = document.getElementById('a2Sub');
  const ultimo = addDias(p.fim, -1);
  const ref = p.mesIni ?? (view === 'dia' ? data : p.ini);
  let nome = MESES[ref.getMonth()];
  if ((view === 'semana' || view === 'itinerario') && p.ini.getMonth() !== ultimo.getMonth())
    nome = `${MESES[p.ini.getMonth()].slice(0, 3)} – ${MESES[ultimo.getMonth()].slice(0, 3)}`;
  titulo.innerHTML = `${esc(nome)}<span class="ano">${ultimo.getFullYear()}</span>`;

  const n = visiveis().length;
  const qtd = n ? `${n} ${n === 1 ? 'item' : 'itens'}` : 'nada marcado';
  const curto = d => d.toLocaleDateString('pt-BR', { day: 'numeric', month: 'short' }).replace('.', '');
  let txt;
  if (view === 'dia') txt = `${cap(data.toLocaleDateString('pt-BR', { weekday: 'long', day: 'numeric', month: 'long' }))} · Semana ${semanaIso(data)}`;
  else if (view === 'semana') txt = `Semana ${semanaIso(addDias(p.ini, 1))} · ${curto(p.ini)} – ${curto(ultimo)}`;
  else if (view === 'itinerario') txt = `${curto(p.ini)} – ${curto(ultimo)} · faixas por ${agruparPor === 'tipo' ? 'tipo' : 'responsável'}`;
  else txt = MESES[p.mesIni.getMonth()] + (view === 'lista' ? ' · lista' : '');
  sub.textContent = `${txt} · ${qtd}`;
}

function marcarView() {
  document.querySelectorAll('.a2-seg [data-view]').forEach(b => {
    const ativo = b.dataset.view === view;
    b.classList.toggle('ativo', ativo);
    b.setAttribute('aria-pressed', String(ativo));
  });
}

function corDoTipo(tipo) {
  const it = itens.find(i => i.tipo === tipo && !i.prevista);
  return it ? corItem(it) : '#9ca3af';
}

function renderFiltros() {
  const el = document.getElementById('a2Filtros');
  const contagem = Object.fromEntries(TIPOS.map(t => [t, itens.filter(i => i.tipo === t).length]));
  const chips = TIPOS.filter(t => contagem[t] || ocultos.has(t)).map(t => `
    <button type="button" class="a2-chip${ocultos.has(t) ? ' off' : ''}" data-tipo="${t}" style="--c:${corDoTipo(t)}"
      aria-pressed="${!ocultos.has(t)}" title="Mostrar/ocultar · Alt+clique mostra só este">
      <i></i>${esc(TIPO_LABEL[t])} <span class="n">${contagem[t]}</span>
    </button>`).join('');
  const agrupar = view === 'itinerario' ? `
    <div class="a2-seg" role="group" aria-label="Faixas">
      <button type="button" data-agrupar="responsavel" class="${agruparPor !== 'tipo' ? 'ativo' : ''}">Por responsável</button>
      <button type="button" data-agrupar="tipo" class="${agruparPor === 'tipo' ? 'ativo' : ''}">Por tipo</button>
    </div>` : '';
  el.innerHTML = `${chips}<span class="esp"></span>${agrupar}
    <button type="button" class="a2-chip liga${soMeus ? ' on' : ''}" id="a2SoMeus" aria-pressed="${soMeus}">👤 Só os meus</button>`;
}

function renderDicas() {
  const el = document.getElementById('a2Dicas');
  const arrastar = view === 'dia' || view === 'semana'
    ? '<span>Arraste compromissos e prazos para mover (ímã de 15 min) · puxe a borda de baixo para mudar a duração · desenhe num horário vazio para criar</span>'
    : view === 'mes' || view === 'itinerario' ? '<span>Arraste um compromisso ou prazo para outro dia</span>' : '';
  el.innerHTML = `${arrastar}<span><kbd>←</kbd> <kbd>→</kbd> navegar · <kbd>T</kbd> hoje · <kbd>D</kbd> <kbd>S</kbd> <kbd>M</kbd> <kbd>I</kbd> <kbd>L</kbd> visões · <kbd>N</kbd> novo</span>`;
}

// ── Semana / dia ────────────────────────────────────────────────────────
// Agrupa itens que se sobrepõem e reparte a largura entre eles (colunas lado a lado).
function layoutColunas(lista) {
  const ord = lista.map(it => {
    const a = minutosDoDia(it.ini);
    const fv = fimVisual(it);
    const b = mesmoDia(fv, it.ini) ? Math.max(a + 20, minutosDoDia(fv)) : 24 * 60;
    return { it, a, b };
  }).sort((x, y) => x.a - y.a || y.b - x.b);
  const res = [];
  let grupo = [], fimGrupo = -1;
  const fechar = () => {
    const ncol = Math.max(...grupo.map(g => g.col)) + 1;
    // Cada item se estende pelas colunas livres à sua direita durante o seu horário.
    grupo.forEach(g => {
      g.ncol = ncol;
      g.span = 1;
      for (let c = g.col + 1; c < ncol && !grupo.some(o => o.col === c && o.a < g.b && o.b > g.a); c++) g.span++;
    });
    res.push(...grupo);
    grupo = [];
  };
  for (const e of ord) {
    if (grupo.length && e.a >= fimGrupo) { fechar(); fimGrupo = -1; }
    const usadas = new Set(grupo.filter(g => g.b > e.a).map(g => g.col));
    let c = 0; while (usadas.has(c)) c++;
    e.col = c;
    grupo.push(e);
    fimGrupo = Math.max(fimGrupo, e.b);
  }
  if (grupo.length) fechar();
  return res;
}

function htmlEvento(e, largo) {
  const { it } = e;
  const top = e.a / 60 * HORA_PX;
  const alt = Math.max(22, (e.b - e.a) / 60 * HORA_PX - 2);
  const larg = 100 / e.ncol;
  const curto = alt < 40;
  const extra = largo && !curto
    ? [it.local && `📍 ${esc(it.local)}`, it.nomeResponsavel && `👤 ${esc(it.nomeResponsavel)}`].filter(Boolean).join(' · ')
    : (!curto && it.local ? esc(it.local) : '');
  return `<div class="${classesItem(it, 'a2-ev')}${curto ? ' curto' : ''}" data-k="${it.k}" tabindex="0"
    style="--c:${corItem(it)};top:${top}px;height:${alt}px;left:calc(${e.col * larg}% + 2px);width:calc(${e.span * larg}% - 4px)"
    title="${esc(it.titulo)}">
    <div class="t">${marcaTarefa(it)}${esc(it.titulo)}</div>
    <div class="h">${faixaHora(it)}${extra ? ` · ${extra}` : ''}</div>
    ${it.editavel ? `<div class="alca" title="${it.tarefa ? 'Arraste para mudar a duração' : 'Arraste para mudar o fim'}"></div>` : ''}
  </div>`;
}

function renderGrade(dias) {
  const hoje = hojeZero();
  const largo = dias.length === 1;
  const head = dias.map(d => `
    <div class="a2-dh${mesmoDia(d, hoje) ? ' hoje' : ''}" data-ir-dia="${ymd(d)}" title="Abrir o dia">
      <span>${DIAS_CURTOS[d.getDay()]}</span><b>${d.getDate()}</b>
    </div>`).join('');
  const horas = Array.from({ length: 24 }, (_, h) => h ? `<span style="top:${h * HORA_PX}px">${pad(h)}:00</span>` : '').join('');
  const cols = dias.map(d => {
    const doDia = visiveis().filter(it => mesmoDia(it.ini, d));
    const fds = d.getDay() === 0 || d.getDay() === 6;
    return `<div class="a2-col${fds ? ' fds' : ''}${mesmoDia(d, hoje) ? ' hoje' : ''}" data-dia="${ymd(d)}">
      ${layoutColunas(doDia).map(e => htmlEvento(e, largo)).join('')}
    </div>`;
  }).join('');
  return `<div class="a2-card" style="--n:${dias.length};--h:${HORA_PX}px">
    <div class="a2-grade-head"><div></div>${head}</div>
    <div class="a2-scroll" id="a2Scroll">
      <div class="a2-corpo" style="height:${24 * HORA_PX}px">
        <div class="a2-horas">${horas}<span class="a2-agora-pill" hidden></span></div>
        ${cols}
      </div>
    </div>
  </div>`;
}

function atualizarAgora() {
  const scroll = document.getElementById('a2Scroll');
  if (!scroll) return;
  scroll.querySelectorAll('.a2-agora').forEach(e => e.remove());
  const agora = new Date();
  const col = scroll.querySelector(`.a2-col[data-dia="${ymd(agora)}"]`);
  const pill = scroll.querySelector('.a2-agora-pill');
  if (!col) { pill.hidden = true; return; }
  const y = minutosDoDia(agora) / 60 * HORA_PX;
  col.insertAdjacentHTML('beforeend', `<div class="a2-agora" style="top:${y}px"></div>`);
  pill.hidden = false;
  pill.style.top = `${y}px`;
  pill.textContent = hm(agora);
}
setInterval(atualizarAgora, 60000);

function rolarGradeInicio() {
  const scroll = document.getElementById('a2Scroll');
  if (!scroll) return;
  const doPeriodo = visiveis().map(it => it.ini.getHours());
  const agora = new Date();
  const temHoje = !!scroll.querySelector(`.a2-col[data-dia="${ymd(agora)}"]`);
  // Hoje visível: centra perto do agora (sem pular os primeiros compromissos da manhã).
  let hora = Math.min(HORA_INICIAL, ...doPeriodo);
  if (temHoje && agora.getHours() - 2 > hora && !doPeriodo.some(h => h < agora.getHours())) hora = agora.getHours() - 2;
  scroll.scrollTop = Math.max(0, (hora - 0.5) * HORA_PX);
}

// ── Mês ─────────────────────────────────────────────────────────────────
function renderMes(p) {
  const hoje = hojeZero();
  const maxPills = celular ? 2 : 3;
  const head = DIAS_CURTOS.map(d => `<div>${d}</div>`).join('');
  const cells = diasDe(p).map(d => {
    const doDia = visiveis().filter(it => mesmoDia(it.ini, d));
    const fora = d.getMonth() !== p.mesIni.getMonth();
    const fds = d.getDay() === 0 || d.getDay() === 6;
    const pills = doDia.slice(0, maxPills).map(it => `
      <div class="${classesItem(it, 'a2-pill')}" data-k="${it.k}" tabindex="0" style="--c:${corItem(it)}" title="${esc(it.titulo)}">
        <span class="h">${hm(it.ini)}</span><span class="t">${marcaTarefa(it)}${esc(it.titulo)}</span>
      </div>`).join('');
    const mais = doDia.length > maxPills ? `<button type="button" class="a2-mais" data-ir-dia="${ymd(d)}">+${doDia.length - maxPills} mais</button>` : '';
    return `<div class="a2-dia${fora ? ' fora' : ''}${fds ? ' fds' : ''}${mesmoDia(d, hoje) ? ' hoje' : ''}" data-dia="${ymd(d)}">
      <button type="button" class="num" data-ir-dia="${ymd(d)}" title="Abrir o dia">${d.getDate()}</button>
      ${pills}${mais}
    </div>`;
  }).join('');
  return `<div class="a2-card"><div class="a2-mes-head">${head}</div><div class="a2-mes">${cells}</div></div>`;
}

// ── Itinerário ──────────────────────────────────────────────────────────
const iniciais = nome => nome.split(/\s+/).filter(Boolean).slice(0, 2).map(p => p[0]).join('').toUpperCase() || '?';
function corDoNome(nome) { let h = 0; for (const c of nome) h = (h * 31 + c.charCodeAt(0)) >>> 0; return PALETA[h % PALETA.length]; }

function renderItinerario(p) {
  const dias = diasDe(p);
  const nd = dias.length;
  const diaPx = diaPxIt();
  const rotPx = celular ? 150 : 220;
  const hoje = hojeZero();
  const eu = getUser();
  const vis = visiveis();

  const chave = it => agruparPor === 'tipo' ? it.tipo : (it.nomeResponsavel || 'Sem responsável');
  const grupos = new Map();
  vis.forEach(it => { const k = chave(it); if (!grupos.has(k)) grupos.set(k, []); grupos.get(k).push(it); });
  const nomes = [...grupos.keys()].sort((a, b) => {
    if (agruparPor === 'tipo') return TIPOS.indexOf(a) - TIPOS.indexOf(b);
    if (a === 'Sem responsável') return 1;
    if (b === 'Sem responsável') return -1;
    if (a === eu?.nome) return -1;
    if (b === eu?.nome) return 1;
    return a.localeCompare(b, 'pt-BR');
  });

  const head = dias.map(d => `
    <div class="a2-it-dh${mesmoDia(d, hoje) ? ' hoje' : ''}${d.getDay() === 0 && d > p.ini ? ' semana' : ''}" data-ir-dia="${ymd(d)}" title="Abrir o dia">
      ${DIAS_CURTOS[d.getDay()]}<b>${d.getDate()}</b>
    </div>`).join('');

  const iHoje = diasEntre(p.ini, new Date());
  const linhaHoje = iHoje >= 0 && iHoje < nd
    ? `<div class="a2-it-hoje" style="left:${(iHoje + minutosDoDia(new Date()) / 1440) * diaPx}px" title="Agora"></div>` : '';
  const fundo = dias.map(d => `<div class="${d.getDay() === 0 || d.getDay() === 6 ? 'fds' : ''}${d.getDay() === 0 && d > p.ini ? ' semana' : ''}"></div>`).join('') + linhaHoje;

  const faixas = nomes.map(nome => {
    const lista = grupos.get(nome).slice().sort((a, b) => a.ini - b.ini);
    const cor = agruparPor === 'tipo' ? corDoTipo(nome) : corDoNome(nome);
    // Empacota em linhas: cada item ocupa do dia de início ao dia do fim; vizinhos no mesmo dia descem.
    const linhas = [];
    const barras = lista.map(it => {
      const s = clamp(diasEntre(p.ini, it.ini), 0, nd - 1);
      const f = clamp(diasEntre(p.ini, addMin(fimVisual(it), -1)), s, nd - 1);
      let r = linhas.findIndex(fimLinha => fimLinha < s);
      if (r < 0) { r = linhas.length; linhas.push(-1); }
      linhas[r] = f;
      return `<div class="${classesItem(it, 'a2-bar')}" data-k="${it.k}" tabindex="0" title="${esc(it.titulo)}"
        style="--c:${corItem(it)};left:${s * diaPx + 4}px;width:${(f - s + 1) * diaPx - 8}px;top:${r * LINHA_IT + 8}px">
        ${it.tarefa ? '<i class="a2-losango"></i>' : ''}<span class="h">${hm(it.ini)}</span><span class="t">${esc(it.titulo)}</span>
      </div>`;
    }).join('');
    const nEv = lista.filter(i => !i.tarefa).length;
    const nPr = lista.length - nEv;
    const nAtr = lista.filter(i => i.atrasada).length;
    const passados = lista.filter(i => i.ini < new Date()).length;
    const info = [nEv && `${nEv} compromisso${nEv > 1 ? 's' : ''}`, nPr && `${nPr} prazo${nPr > 1 ? 's' : ''}`].filter(Boolean).join(' · ');
    const rotulo = agruparPor === 'tipo' ? TIPO_LABEL[nome] ?? nome : nome;
    return `<div class="a2-faixa" style="--c:${cor}">
      <div class="a2-faixa-rot">
        <span class="a2-av">${agruparPor === 'tipo' ? '●' : esc(iniciais(nome))}</span>
        <div style="min-width:0;flex:1">
          <div class="nome" title="${esc(rotulo)}">${esc(rotulo)}</div>
          <div class="info">${info}${nAtr ? ` · <span style="color:var(--color-danger)">${nAtr} atrasado${nAtr > 1 ? 's' : ''}</span>` : ''}</div>
          <div class="barra" title="${passados} de ${lista.length} já passaram"><span style="width:${Math.round(passados / lista.length * 100)}%"></span></div>
        </div>
      </div>
      <div class="a2-faixa-trilho" data-trilho style="min-height:${Math.max(1, linhas.length) * LINHA_IT + 12}px">${barras}</div>
    </div>`;
  }).join('');

  return `<div class="a2-card" style="--d:${diaPx}px;--r:${rotPx}px">
    <div class="a2-it-rolagem" id="a2ItScroll">
      <div class="a2-it-conteudo" style="width:${rotPx + nd * diaPx}px">
        <div class="a2-it-head"><div class="a2-it-canto">${nomes.length} faixa${nomes.length === 1 ? '' : 's'}</div>${head}</div>
        <div class="a2-it-corpo">
          <div class="a2-it-fundo">${fundo}</div>
          ${faixas || `<div class="a2-vazio" style="position:relative">Nada marcado nestas duas semanas.</div>`}
        </div>
      </div>
    </div>
  </div>`;
}

// ── Lista ───────────────────────────────────────────────────────────────
function renderLista() {
  const vis = visiveis().slice().sort((a, b) => a.ini - b.ini);
  if (!vis.length) return '<div class="a2-card a2-vazio">Nada marcado neste mês.</div>';
  const hoje = hojeZero();
  const porDia = new Map();
  vis.forEach(it => { const k = ymd(it.ini); if (!porDia.has(k)) porDia.set(k, []); porDia.get(k).push(it); });
  const html = [...porDia.entries()].map(([k, lista]) => {
    const d = deYmd(k);
    const dif = diasEntre(hoje, d);
    const rel = dif === 0 ? 'Hoje' : dif === 1 ? 'Amanhã' : dif === -1 ? 'Ontem' : '';
    const titulo = cap(d.toLocaleDateString('pt-BR', { weekday: 'long', day: 'numeric', month: 'long' }));
    return `<div class="a2-lg${dif === 0 ? ' hoje' : ''}" data-dia-lista="${k}">${rel ? `${rel} · ` : ''}${esc(titulo)}</div>` +
      lista.map(it => `
      <div class="${classesItem(it, 'a2-li')}" data-k="${it.k}" tabindex="0" style="--c:${corItem(it)}">
        <div class="h">${faixaHora(it)}</div>
        <div style="min-width:0">
          <div class="t">${marcaTarefa(it)}${esc(it.titulo)}</div>
          <div class="m">${[TIPO_LABEL[it.tipo] ?? it.tipo, it.local && `📍 ${esc(it.local)}`,
            it.nomeResponsavel && `👤 ${esc(it.nomeResponsavel)}`, it.numeroCNJProcesso && `⚖️ ${esc(it.numeroCNJProcesso)}`].filter(Boolean).join(' · ')}</div>
        </div>
      </div>`).join('');
  }).join('');
  return `<div class="a2-card"><div class="a2-lista" id="a2ListaScroll">${html}</div></div>`;
}

// ── Depois de renderizar: rolagem inicial ───────────────────────────────
function depoisDeRender(c) {
  if (view === 'dia' || view === 'semana') { atualizarAgora(); rolarGradeInicio(); }
  if (view === 'itinerario') {
    const sc = document.getElementById('a2ItScroll');
    const p = periodo(view, data);
    const i = diasEntre(p.ini, new Date());
    const diaPx = diaPxIt();
    if (sc && i > 1 && i < DIAS_ITINERARIO) sc.scrollLeft = (i - 1) * diaPx;
  }
  if (view === 'lista') {
    // Rola só dentro do cartão (a página fica no topo, com os controles à vista).
    const sc = document.getElementById('a2ListaScroll');
    const alvo = c.querySelector(`[data-dia-lista="${ymd(new Date())}"]`);
    if (sc && alvo) sc.scrollTop = alvo.offsetTop - sc.offsetTop - 8;
  }
}

// ── Interação: clique, arrastar, desenhar ───────────────────────────────
function suprimirClique() { suprimirAte = Date.now() + 300; }

const conteudo = document.getElementById('a2Conteudo');

conteudo.addEventListener('click', e => {
  if (Date.now() < suprimirAte) return;
  const irDia = e.target.closest('[data-ir-dia]');
  if (irDia) { data = deYmd(irDia.dataset.irDia); view = 'dia'; carregar(); return; }
  const el = e.target.closest('[data-k]');
  if (el) { e.stopPropagation(); abrirPopover(itemDe(el), el); }
});

conteudo.addEventListener('keydown', e => {
  if (e.key !== 'Enter' && e.key !== ' ') return;
  const el = e.target.closest('[data-k]');
  if (!el) return;
  e.preventDefault();
  abrirPopover(itemDe(el), el);
});

conteudo.addEventListener('dblclick', e => {
  if (e.target.closest('[data-k]')) return;
  const col = e.target.closest('.a2-col');
  if (col) {
    const r = col.getBoundingClientRect();
    const m = clamp(Math.floor((e.clientY - r.top) / HORA_PX * 2) * 30, 0, 23 * 60);
    const ini = addMin(deYmd(col.dataset.dia), m);
    abrirCriar(ini, addMin(ini, 60));
    return;
  }
  const dia = e.target.closest('.a2-dia');
  if (dia) { const ini = addMin(deYmd(dia.dataset.dia), 9 * 60); abrirCriar(ini, addMin(ini, 60)); }
});

conteudo.addEventListener('pointerdown', e => {
  if (e.button !== 0 || e.pointerType === 'touch') return; // no toque, só toque = abrir
  const ev = e.target.closest('.a2-ev');
  if (ev) { arrastarNaGrade(e, ev); return; }
  const pill = e.target.closest('.a2-pill');
  if (pill) { arrastarNoMes(e, pill); return; }
  const bar = e.target.closest('.a2-bar');
  if (bar) { arrastarNoItinerario(e, bar); return; }
  const col = e.target.closest('.a2-col');
  if (col && e.target === col) desenharNaGrade(e, col);
});

// Liga pointermove/up no elemento capturado e devolve uma função para desligar.
function capturar(el, e, mover, soltar) {
  el.setPointerCapture(e.pointerId);
  const fim = ev => { el.removeEventListener('pointermove', mover); el.removeEventListener('pointerup', fim); el.removeEventListener('pointercancel', fim); soltar(ev); };
  el.addEventListener('pointermove', mover);
  el.addEventListener('pointerup', fim);
  el.addEventListener('pointercancel', fim);
}

function autoRolar(scroll, ev) {
  if (!scroll) return;
  const r = scroll.getBoundingClientRect();
  if (ev.clientY > r.bottom - 36) scroll.scrollTop += 14;
  else if (ev.clientY < r.top + 36) scroll.scrollTop -= 14;
}

function arrastarNaGrade(e, el) {
  const it = itemDe(el);
  if (!it?.editavel) return;
  e.preventDefault();
  const modo = e.target.closest('.alca') ? 'esticar' : 'mover';
  const scroll = document.getElementById('a2Scroll');
  const cols = [...document.querySelectorAll('.a2-col')];
  const fv = fimVisual(it);
  const durMin = Math.round((fv - it.ini) / 60000);
  const iniMin = minutosDoDia(it.ini);
  const x0 = e.clientX, y0 = e.clientY, sc0 = scroll.scrollTop;
  let ativo = false, novoIni = it.ini, novoFim = fv;

  capturar(el, e, ev => {
    if (!ativo && Math.hypot(ev.clientX - x0, ev.clientY - y0) < 4) return;
    if (!ativo) { ativo = true; el.classList.add('arrastando'); fecharPopover(); }
    autoRolar(scroll, ev);
    const dy = ev.clientY - y0 + (scroll.scrollTop - sc0);
    const passo = Math.round(dy / HORA_PX * (60 / PASSO_MIN)) * PASSO_MIN;
    if (modo === 'mover') {
      const rects = cols.map(c => c.getBoundingClientRect());
      let ci = rects.findIndex(r => ev.clientX >= r.left && ev.clientX < r.right);
      if (ci < 0) ci = ev.clientX < rects[0].left ? 0 : rects.length - 1;
      const m = clamp(Math.round((iniMin + passo) / PASSO_MIN) * PASSO_MIN, 0, 24 * 60 - PASSO_MIN);
      novoIni = addMin(deYmd(cols[ci].dataset.dia), m);
      novoFim = addMin(novoIni, durMin);
      // Desloca na tela sem trocar de coluna no DOM (isso soltaria a captura do ponteiro).
      const origem = el.parentElement.getBoundingClientRect();
      el.style.left = `${rects[ci].left - origem.left + 2}px`;
      el.style.width = `${rects[ci].width - 4}px`;
      el.style.top = `${m / 60 * HORA_PX}px`;
    } else {
      const dur = clamp(durMin + passo, PASSO_MIN, 24 * 60 - iniMin);
      novoFim = addMin(it.ini, dur);
      el.style.height = `${dur / 60 * HORA_PX - 2}px`;
    }
    const h = el.querySelector('.h');
    if (h) h.textContent = `${hm(novoIni)} – ${hm(novoFim)}`;
  }, ev => {
    el.classList.remove('arrastando');
    if (!ativo) return;
    suprimirClique();
    const igual = novoIni.getTime() === it.ini.getTime() && novoFim.getTime() === fv.getTime();
    if (ev.type === 'pointercancel' || igual) { render(); return; }
    // Evento sem fim continua sem fim ao ser movido; esticar cria o fim.
    reagendar(it, novoIni, modo === 'esticar' || it.fim ? novoFim : null);
  });
}

function desenharNaGrade(e, col) {
  e.preventDefault();
  const minDe = y => { const r = col.getBoundingClientRect(); return clamp(Math.floor((y - r.top) / HORA_PX * (60 / PASSO_MIN)) * PASSO_MIN, 0, 24 * 60 - PASSO_MIN); };
  const scroll = document.getElementById('a2Scroll');
  const m0 = minDe(e.clientY);
  const y0 = e.clientY;
  let ativo = false, fant = null, a = m0, b = m0 + PASSO_MIN;

  capturar(col, e, ev => {
    if (!ativo && Math.abs(ev.clientY - y0) < 6) return;
    if (!ativo) {
      ativo = true;
      fecharPopover();
      fant = document.createElement('div');
      fant.className = 'a2-fantasma';
      col.appendChild(fant);
    }
    autoRolar(scroll, ev);
    const m = minDe(ev.clientY);
    a = Math.min(m0, m); b = Math.max(m0, m) + PASSO_MIN;
    fant.style.top = `${a / 60 * HORA_PX}px`;
    fant.style.height = `${(b - a) / 60 * HORA_PX - 2}px`;
    fant.textContent = `${hmMin(a)} – ${b >= 1440 ? '24:00' : hmMin(b)}`;
  }, ev => {
    if (!ativo) return;
    suprimirClique();
    if (ev.type === 'pointercancel') { fant.remove(); return; }
    fantasmaAtual = fant;
    const dia = deYmd(col.dataset.dia);
    abrirCriar(addMin(dia, a), addMin(dia, Math.min(b, 24 * 60 - 1)));
  });
}

// Mês: o compromisso "voa" com o ponteiro e cai no dia sob ele (mantém hora e duração).
function arrastarNoMes(e, el) {
  const it = itemDe(el);
  if (!it?.editavel) return;
  e.preventDefault();
  const x0 = e.clientX, y0 = e.clientY;
  let ativo = false, voando = null, alvo = null;
  const limparAlvo = () => document.querySelectorAll('.a2-dia.alvo').forEach(d => d.classList.remove('alvo'));

  capturar(el, e, ev => {
    if (!ativo && Math.hypot(ev.clientX - x0, ev.clientY - y0) < 5) return;
    if (!ativo) {
      ativo = true;
      fecharPopover();
      voando = el.cloneNode(true);
      voando.classList.add('a2-voando');
      voando.style.width = `${el.offsetWidth}px`;
      document.body.appendChild(voando);
      el.style.opacity = '.35';
    }
    voando.style.left = `${ev.clientX - 20}px`;
    voando.style.top = `${ev.clientY - 12}px`;
    const sob = document.elementFromPoint(ev.clientX, ev.clientY)?.closest('.a2-dia');
    if (sob !== alvo) { limparAlvo(); alvo = sob; alvo?.classList.add('alvo'); }
  }, ev => {
    limparAlvo();
    voando?.remove();
    el.style.opacity = '';
    if (!ativo) return;
    suprimirClique();
    if (ev.type === 'pointercancel' || !alvo) return;
    moverParaDia(it, deYmd(alvo.dataset.dia));
  });
}

// Itinerário: a barra desliza na horizontal, com ímã de 1 dia.
function arrastarNoItinerario(e, el) {
  const it = itemDe(el);
  if (!it?.editavel) return;
  e.preventDefault();
  const p = periodo(view, data);
  const diaPx = diaPxIt();
  const s0 = diasEntre(p.ini, it.ini);
  const x0 = e.clientX, y0 = e.clientY;
  let ativo = false, delta = 0;

  capturar(el, e, ev => {
    if (!ativo && Math.hypot(ev.clientX - x0, ev.clientY - y0) < 5) return;
    if (!ativo) { ativo = true; el.classList.add('arrastando'); fecharPopover(); }
    delta = clamp(Math.round((ev.clientX - x0) / diaPx), -s0, DIAS_ITINERARIO - 1 - s0);
    el.style.transform = `translateX(${delta * diaPx}px)`;
    const h = el.querySelector('.h');
    if (h) h.textContent = addDias(it.ini, delta).toLocaleDateString('pt-BR', { weekday: 'short', day: 'numeric' }).replace('.', '');
  }, ev => {
    el.classList.remove('arrastando');
    if (!ativo) return;
    suprimirClique();
    if (ev.type === 'pointercancel' || !delta) { render(); return; }
    moverParaDia(it, addDias(diaZero(it.ini), delta));
  });
}

function moverParaDia(it, dia) {
  const delta = diasEntre(it.ini, dia);
  if (!delta) return;
  reagendar(it, addDias(it.ini, delta), it.fim ? addDias(it.fim, delta) : null);
}

// Evento: PUT com os demais campos como estão. Tarefa/prazo: PATCH só de prazo e duração
// (o bloco vai do prazo até prazo + duração). Já mostra o resultado antes da resposta.
async function salvarHorario(it, ini, fim) {
  if (it.tarefa) {
    const duracaoMinutos = fim ? Math.round((fim - ini) / 60000) : null;
    return apiFetch(`/tarefas/${it.id}/agenda`, { method: 'PATCH', body: JSON.stringify({ prazo: isoLocal(ini), duracaoMinutos }) });
  }
  const ev = await apiFetch(`/eventos/${it.id}`);
  return apiFetch(`/eventos/${it.id}`, {
    method: 'PUT',
    body: JSON.stringify({
      titulo: ev.titulo, tipo: ev.tipo,
      dataHora: isoLocal(ini), dataHoraFim: fim ? isoLocal(fim) : null,
      local: ev.local ?? null, responsavelId: ev.responsavelId ?? null,
      processoId: ev.processoId ?? null, observacoes: ev.observacoes ?? null,
    }),
  });
}

async function reagendar(it, ini, fim, { desfazivel = true } = {}) {
  const antes = { ini: it.ini, fim: it.fim, dataHora: it.dataHora, dataHoraFim: it.dataHoraFim, atrasada: it.atrasada };
  Object.assign(it, { ini, fim, dataHora: isoLocal(ini), dataHoraFim: fim ? isoLocal(fim) : null });
  if (it.tarefa) it.atrasada = ini < new Date();
  render();
  try {
    const res = await salvarHorario(it, ini, fim);
    const quando = ini.toLocaleDateString('pt-BR', { weekday: 'short', day: 'numeric', month: 'short' }).replace(/\./g, '');
    const faixa = fim ? `${hm(ini)} – ${hm(fim)}` : hm(ini);
    // A contagem do prazo (início + dias) foi desligada no servidor: avisar, porque o desfazer não a volta.
    const contagem = res?.contagemDesligada ? ' · contagem automática do prazo desligada' : '';
    // Tarefa sem duração: desfazer grava a duração padrão do bloco (o PATCH não apaga a duração).
    const fimAntes = antes.fim ?? (it.tarefa && fim ? addMin(antes.ini, 30) : null);
    if (desfazivel) aviso(`“${it.titulo}” → ${quando}, ${faixa}${contagem}`, { rotulo: 'Desfazer', acao: () => reagendar(it, antes.ini, fimAntes, { desfazivel: false }) });
    else aviso('Desfeito.');
  } catch (err) {
    Object.assign(it, antes);
    render();
    aviso(err?.message || 'Não foi possível reagendar.', { erro: true });
  }
}

// ── Popover de detalhe ──────────────────────────────────────────────────
const pop = document.getElementById('a2Pop');

function abrirPopover(it, ancora) {
  if (!it) return;
  pop.style.setProperty('--c', it.atrasada ? 'var(--color-danger)' : corItem(it));
  document.getElementById('a2PopTipo').textContent = it.prevista ? 'Recorrente · prevista' : it.tarefa ? 'Prazo / tarefa' : (TIPO_LABEL[it.tipo] ?? it.tipo);
  document.getElementById('a2PopTitulo').textContent = it.titulo;

  const dataTxt = cap(it.ini.toLocaleDateString('pt-BR', { weekday: 'long', day: 'numeric', month: 'long' }));
  const linhas = [
    ['🗓', `${esc(dataTxt)} · ${esc(faixaHora(it))}`],
    it.atrasada && ['⚠️', '<span class="alerta">Prazo vencido</span>'],
    it.tarefa && it.fim && ['⏱', `Prazo às ${hm(it.ini)} · ${duracaoTxt((it.fim - it.ini) / 60000)} reservados na agenda`],
    it.prevista && ['🔁', 'Será criada quando a anterior vencer'],
    it.local && ['📍', esc(it.local)],
    it.nomeResponsavel && ['👤', esc(it.nomeResponsavel)],
    it.numeroCNJProcesso && ['⚖️', esc(it.numeroCNJProcesso)],
  ].filter(Boolean);
  document.getElementById('a2PopInfo').innerHTML = linhas.map(([i, t]) => `<dt>${i}</dt><dd>${t}</dd>`).join('');

  const acoes = document.getElementById('a2PopAcoes');
  if (!it.tarefa) {
    acoes.innerHTML = `<button class="btn btn-primary btn-sm" data-a="editar">Editar</button>
      <button class="btn btn-secondary btn-sm" data-a="duplicar">Duplicar</button>
      <button class="btn btn-danger btn-sm" data-a="excluir" style="margin-left:auto">Excluir</button>`;
  } else {
    const tarefaId = it.prevista ? it.tarefaId : it.id;
    acoes.innerHTML = `${tarefaId ? `<a class="btn btn-primary btn-sm" href="/pages/tarefas.html?abrirId=${encodeURIComponent(tarefaId)}">${it.prevista ? 'Abrir série →' : 'Abrir tarefa →'}</a>` : ''}
      ${it.prevista ? '' : '<button class="btn btn-secondary btn-sm" data-a="concluir">✓ Concluir</button>'}`;
  }
  acoes.onclick = async ev => {
    const a = ev.target.closest('[data-a]')?.dataset.a;
    if (!a) return;
    fecharPopover();
    if (a === 'editar') openEditModal(it.id);
    else if (a === 'duplicar') duplicar(it);
    else if (a === 'excluir') deleteEvento(it.id);
    else if (a === 'concluir') concluirTarefa(it);
  };

  pop.hidden = false;
  const r = ancora.getBoundingClientRect();
  const w = pop.offsetWidth, h = pop.offsetHeight;
  // À direita do item; sem espaço, à esquerda; sem espaço dos dois lados (celular), embaixo.
  let left = r.right + 10, top = r.top;
  if (left + w > innerWidth - 12) left = r.left - w - 10;
  if (left < 12) { left = clamp(r.left, 12, innerWidth - w - 12); top = r.bottom + 8; }
  top = clamp(top, 12, innerHeight - h - 12);
  pop.style.left = `${left}px`;
  pop.style.top = `${top}px`;
  pop.querySelector('.a2-pop-acoes .btn, .a2-pop-x')?.focus({ preventScroll: true });
}

function fecharPopover() { pop.hidden = true; }
document.getElementById('a2PopX').addEventListener('click', fecharPopover);
document.addEventListener('click', e => { if (!pop.hidden && !pop.contains(e.target)) fecharPopover(); });

function abrirPendente(c) {
  if (!pendingAbrirId) return;
  const id = pendingAbrirId;
  pendingAbrirId = null;
  const it = itens.find(i => i.id === id);
  if (!it) return;
  requestAnimationFrame(() => {
    const el = c.querySelector(`[data-k="${it.k}"]`);
    if (!el) return;
    el.scrollIntoView({ block: 'center', behavior: 'smooth' });
    setTimeout(() => abrirPopover(it, el), 250);
  });
}

// ── Ações ───────────────────────────────────────────────────────────────
async function concluirTarefa(it) {
  try {
    await apiFetch(`/tarefas/${it.id}/concluir`, { method: 'POST' });
    aviso(`“${it.titulo}” concluída.`);
    await carregar();
  } catch (err) { aviso(err?.message || 'Não foi possível concluir.', { erro: true }); }
}

async function duplicar(it) {
  // Abre o modal já preenchido com os dados do evento, uma semana depois.
  await openEditModal(it.id);
  editingId = null;
  document.getElementById('modalEventoTitulo').textContent = 'Duplicar evento';
  document.querySelector('#eTipo option[value="Prazo"]').hidden = false;
  const ini = addDias(it.ini, 7);
  document.getElementById('eDataHora').value = isoLocal(ini).slice(0, 16);
  document.getElementById('eDataHoraFim').value = it.fim ? isoLocal(addDias(it.fim, 7)).slice(0, 16) : '';
}

async function deleteEvento(id) {
  if (!confirm('Excluir este evento?')) return;
  try {
    await apiFetch(`/eventos/${id}`, { method: 'DELETE' });
    aviso('Evento excluído.');
    await carregar();
  } catch (err) { aviso(err?.message || 'Não foi possível excluir.', { erro: true }); }
}

// ── Aviso (toast) ───────────────────────────────────────────────────────
let avisoTimer = null;
function aviso(msg, { rotulo, acao, erro } = {}) {
  const el = document.getElementById('a2Aviso');
  el.className = `a2-aviso${erro ? ' erro' : ''}`;
  el.innerHTML = `<span>${esc(msg)}</span>${rotulo ? `<button type="button">${esc(rotulo)}</button>` : ''}`;
  el.hidden = false;
  if (rotulo) el.querySelector('button').onclick = () => { el.hidden = true; acao?.(); };
  clearTimeout(avisoTimer);
  avisoTimer = setTimeout(() => { el.hidden = true; }, rotulo ? 7000 : 3500);
}

// ── Modal de evento ──────────────────────────────────────────────────────
// Sem responsável o evento não gera alerta por e-mail, então o campo sempre vem preenchido
// (padrão: quem está criando). Só admin pode listar /usuarios; os demais veem só a si.
let usuariosCache = null;
async function carregarResponsaveis(selecionadoId, selecionadoNome) {
  const eu = getUser();
  if (!usuariosCache) {
    if (eu?.perfil === 'Admin') {
      try { usuariosCache = (await apiFetch('/usuarios') || []).filter(u => u.ativo); } catch { usuariosCache = null; }
    }
    if (!usuariosCache?.length) usuariosCache = eu ? [{ id: eu.id, nome: eu.nome }] : [];
  }
  const lista = [...usuariosCache];
  if (selecionadoId && !lista.some(u => u.id === selecionadoId))
    lista.push({ id: selecionadoId, nome: selecionadoNome || 'Usuário' });
  const sel = document.getElementById('eResponsavel');
  sel.innerHTML = lista.map(u => `<option value="${esc(u.id)}">${esc(u.nome)}</option>`).join('');
  sel.value = selecionadoId || eu?.id || lista[0]?.id || '';
}

async function abrirCriar(ini, fim) {
  editingId = null;
  editingProcessoId = null;
  document.getElementById('modalEventoTitulo').textContent = 'Novo Evento';
  document.getElementById('formEvento').reset();
  document.querySelector('#eTipo option[value="Prazo"]').hidden = false;
  atualizarTipoPrazo();
  if (ini) document.getElementById('eDataHora').value = isoLocal(ini).slice(0, 16);
  if (fim) document.getElementById('eDataHoraFim').value = isoLocal(fim).slice(0, 16);
  document.getElementById('eMsgErro').style.display = 'none';
  document.getElementById('modalEvento').style.display = 'flex';
  await carregarResponsaveis(null);
  document.getElementById('eTitulo').focus();
}

async function openEditModal(id) {
  editingId = id;
  document.getElementById('modalEventoTitulo').textContent = 'Editar Evento';
  document.getElementById('eMsgErro').style.display = 'none';
  const ev = await apiFetch(`/eventos/${id}`);
  document.getElementById('eTitulo').value = ev.titulo;
  // Prazo é Tarefa: um evento existente não vira prazo pela edição.
  document.querySelector('#eTipo option[value="Prazo"]').hidden = true;
  document.getElementById('eTipo').value = ev.tipo;
  atualizarTipoPrazo();
  document.getElementById('eDataHora').value = ev.dataHora.substring(0, 16);
  document.getElementById('eDataHoraFim').value = ev.dataHoraFim ? ev.dataHoraFim.substring(0, 16) : '';
  document.getElementById('eLocal').value = ev.local ?? '';
  document.getElementById('eObservacoes').value = ev.observacoes ?? '';
  editingProcessoId = ev.processoId ?? null;
  await carregarResponsaveis(ev.responsavelId, ev.nomeResponsavel);
  document.getElementById('modalEvento').style.display = 'flex';
}

// Prazo é salvo como Tarefa (Tipo=Prazo), que não tem fim nem local.
function atualizarTipoPrazo() {
  const prazo = document.getElementById('eTipo').value === 'Prazo';
  document.getElementById('eTipoPrazoHint').style.display = prazo ? 'block' : 'none';
  document.getElementById('eFimGroup').style.display = prazo ? 'none' : '';
  document.getElementById('eLocalGroup').style.display = prazo ? 'none' : '';
}
document.getElementById('eTipo').addEventListener('change', atualizarTipoPrazo);

const modalAberto = () => document.getElementById('modalEvento').style.display !== 'none';
function closeModal() {
  document.getElementById('modalEvento').style.display = 'none';
  fantasmaAtual?.remove();
  fantasmaAtual = null;
}

document.getElementById('btnNovoEvento').addEventListener('click', () => {
  // Sugere o próximo horário cheio do dia em exibição (ou de hoje).
  const base = mesmoDia(data, new Date()) || view !== 'dia' ? new Date() : new Date(data);
  if (mesmoDia(base, new Date())) base.setHours(base.getHours() + 1, 0, 0, 0); else base.setHours(9, 0, 0, 0);
  abrirCriar(base, addMin(base, 60));
});
document.getElementById('modalEventoClose').addEventListener('click', closeModal);
document.getElementById('btnCancelarEvento').addEventListener('click', closeModal);
document.getElementById('modalEvento').addEventListener('click', e => { if (e.target === e.currentTarget) closeModal(); });

document.getElementById('formEvento').addEventListener('submit', async e => {
  e.preventDefault();
  const errEl = document.getElementById('eMsgErro');
  errEl.style.display = 'none';
  const titulo = document.getElementById('eTitulo').value.trim();
  const dataHora = document.getElementById('eDataHora').value;
  if (!titulo || !dataHora) { showErr(errEl, 'Título e data/hora são obrigatórios.'); return; }

  const dto = {
    titulo,
    tipo: document.getElementById('eTipo').value,
    dataHora,
    dataHoraFim: document.getElementById('eDataHoraFim').value || null,
    local: document.getElementById('eLocal').value.trim() || null,
    observacoes: document.getElementById('eObservacoes').value.trim() || null,
    responsavelId: document.getElementById('eResponsavel').value || null,
    processoId: editingId ? editingProcessoId : null,
  };
  if (dto.dataHoraFim && dto.dataHoraFim < dto.dataHora) { showErr(errEl, 'O fim precisa ser depois do início.'); return; }

  try {
    if (!editingId && dto.tipo === 'Prazo') {
      await apiFetch('/tarefas', { method: 'POST', body: JSON.stringify({
        titulo, tipo: 'Prazo', prioridade: 'Media', prazo: dataHora,
        descricao: dto.observacoes, responsavelId: dto.responsavelId,
      })});
    } else if (editingId) {
      await apiFetch(`/eventos/${editingId}`, { method: 'PUT', body: JSON.stringify(dto) });
    } else {
      await apiFetch('/eventos', { method: 'POST', body: JSON.stringify(dto) });
    }
    closeModal();
    await carregar();
  } catch (err) {
    showErr(errEl, err.message ?? 'Erro ao salvar evento.');
  }
});

function showErr(el, msg) { el.textContent = msg; el.style.display = ''; }

// ── Navegação, filtros e atalhos ────────────────────────────────────────
function mover(dir) {
  if (view === 'dia') data = addDias(data, dir);
  else if (view === 'semana' || view === 'itinerario') data = addDias(data, 7 * dir);
  else data = new Date(data.getFullYear(), data.getMonth() + dir, 1);
  carregar();
}
function irHoje() { data = hojeZero(); carregar(); }
function trocarView(v) { if (v === view) return; view = v; carregar(); }

document.getElementById('a2Ant').addEventListener('click', () => mover(-1));
document.getElementById('a2Prox').addEventListener('click', () => mover(1));
document.getElementById('a2Hoje').addEventListener('click', irHoje);
document.querySelector('.a2-top .a2-seg').addEventListener('click', e => {
  const b = e.target.closest('[data-view]');
  if (b) trocarView(b.dataset.view);
});

document.getElementById('a2Filtros').addEventListener('click', e => {
  const tipo = e.target.closest('[data-tipo]')?.dataset.tipo;
  if (tipo) {
    if (e.altKey) { TIPOS.forEach(t => (t === tipo ? ocultos.delete(t) : ocultos.add(t))); }
    else if (ocultos.has(tipo)) ocultos.delete(tipo);
    else ocultos.add(tipo);
    gravarPref('ocultos', [...ocultos]);
    render();
    return;
  }
  const agr = e.target.closest('[data-agrupar]')?.dataset.agrupar;
  if (agr) { agruparPor = agr; gravarPref('agrupar', agr); render(); return; }
  if (e.target.closest('#a2SoMeus')) { soMeus = !soMeus; gravarPref('soMeus', soMeus); carregar(); }
});

document.addEventListener('keydown', e => {
  if (e.key === 'Escape') { if (!pop.hidden) fecharPopover(); else if (modalAberto()) closeModal(); return; }
  if (e.ctrlKey || e.metaKey || e.altKey || modalAberto()) return;
  if (e.target.closest('input, textarea, select, [contenteditable="true"]')) return;
  const k = e.key.toLowerCase();
  const acoes = {
    arrowleft: () => mover(-1), arrowright: () => mover(1), t: irHoje,
    d: () => trocarView('dia'), s: () => trocarView('semana'), m: () => trocarView('mes'),
    i: () => trocarView('itinerario'), l: () => trocarView('lista'),
    n: () => document.getElementById('btnNovoEvento').click(),
  };
  if (acoes[k]) { e.preventDefault(); acoes[k](); }
});

// Compromisso criado/remarcado pelo chat do assistente de IA: recarrega o período exibido.
window.addEventListener('assistente:alteracao', e => { if (e.detail?.entidade === 'evento') carregar(); });

carregar();
abrirNovoPelaUrl('btnNovoEvento');
