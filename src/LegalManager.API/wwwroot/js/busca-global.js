// Busca global: paleta Ctrl/⌘ + K (ou "/") em todas as telas do escritório e caixa de busca no
// dashboard (montarCaixaBusca). As duas usam o mesmo motor: páginas do menu e atalhos "Criar"
// filtrados aqui mesmo, e processos, contatos, tarefas, eventos, documentos e contratos pela
// API (GET /api/busca?q=). Enter abre; Ctrl/⌘ + Enter (ou clique do meio) abre em nova aba.
import { apiFetch } from './api.js';
import { esc, fmtDataParede, fmtDataHoraParede } from './utils.js';

const TAMANHO_MINIMO = 2;
const ESPERA_MS = 180;

const GRUPOS = {
  acao:      { rotulo: 'Criar',                   icone: '➕' },
  pagina:    { rotulo: 'Páginas',                 icone: '🧭' },
  processo:  { rotulo: 'Processos',               icone: '⚖️' },
  contato:   { rotulo: 'Contatos',                icone: '👤' },
  tarefa:    { rotulo: 'Tarefas e prazos',        icone: '✅' },
  evento:    { rotulo: 'Agenda',                  icone: '📅' },
  documento: { rotulo: 'Documentos',              icone: '📄' },
  contrato:  { rotulo: 'Contratos de honorários', icone: '💼' },
};

// Mesmos atalhos do bloco "Atalhos" do dashboard.
const ACOES = [
  { titulo: 'Novo processo',  url: '/pages/processos.html?novo=1', icone: '⚖️' },
  { titulo: 'Nova tarefa',    url: '/pages/tarefas.html?novo=1',   icone: '✅' },
  { titulo: 'Novo evento',    url: '/pages/agenda.html?novo=1',    icone: '📅' },
  { titulo: 'Novo contato',   url: '/pages/contatos.html?novo=1',  icone: '👤' },
  { titulo: 'Novo contrato de honorários', url: '/pages/honorarios-contrato-novo.html', icone: '🤝', plus: true },
  { titulo: 'Novo lançamento financeiro',  url: '/pages/financeiro.html?novo=1',        icone: '💵', plus: true },
];

const ehMac = /Mac|iPhone|iPad/.test(navigator.userAgentData?.platform ?? navigator.platform ?? '');
export const ATALHO_BUSCA = ehMac ? '⌘K' : 'Ctrl K';

const LUPA = `<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" aria-hidden="true"><circle cx="11" cy="11" r="7"/><path d="m20 20-3.5-3.5"/></svg>`;

let config = { paginas: [], plano: 'Free', aoBloqueado: null };
let paleta = null;

/** Sem acentos e em minúsculas, para comparar "acao" com "Ação". */
function normalizar(texto) {
  return String(texto ?? '').normalize('NFD').replace(/\p{M}/gu, '').toLowerCase();
}

function palavrasDe(termo) {
  return normalizar(termo).split(/\s+/).filter(Boolean);
}

/** Texto escapado com <mark> nos trechos que batem com as palavras buscadas (ignorando acentos). */
function destacar(texto, palavras) {
  if (!texto) return '';
  const chars = [...String(texto)];
  let norm = '';
  const origem = [];
  chars.forEach((c, i) => {
    const n = normalizar(c);
    for (let j = 0; j < n.length; j++) origem.push(i);
    norm += n;
  });
  const marca = new Array(chars.length).fill(false);
  for (const p of palavras) {
    for (let k = norm.indexOf(p); k !== -1; k = norm.indexOf(p, k + p.length)) {
      for (let j = k; j < k + p.length; j++) marca[origem[j]] = true;
    }
  }
  let html = '';
  let aberto = false;
  chars.forEach((c, i) => {
    if (marca[i] !== aberto) { html += marca[i] ? '<mark>' : '</mark>'; aberto = marca[i]; }
    html += esc(c);
  });
  return aberto ? html + '</mark>' : html;
}

function filtrarLocais(palavras) {
  if (!palavras.length) return [];
  const bate = titulo => { const n = normalizar(titulo); return palavras.every(p => n.includes(p)); };
  const acoes = ACOES.filter(a => bate(a.titulo)).slice(0, 4).map(a => ({
    tipo: 'acao', titulo: a.titulo, url: a.url, icone: a.icone,
    bloqueado: !!a.plus && config.plano === 'Free', upgrade: 'Plus',
  }));
  const paginas = config.paginas.filter(p => bate(p.titulo)).slice(0, 5).map(p => ({ tipo: 'pagina', ...p }));
  return [...acoes, ...paginas];
}

function subtituloRemoto(item) {
  const partes = [];
  if (item.data) partes.push(item.tipo === 'evento' ? fmtDataHoraParede(item.data, '') : fmtDataParede(item.data, ''));
  if (item.subtitulo) partes.push(item.subtitulo);
  return partes.filter(Boolean).join(' · ');
}

/**
 * Liga um campo de busca a uma lista de resultados. A lista é desenhada por grupo e navegada pelo
 * teclado no próprio campo (o foco não sai dele: aria-activedescendant).
 */
function criarMotor({ input, lista, idBase, sugestoes = () => [], dica = '', aoMudar = () => {}, aoEscapar = () => {} }) {
  let itens = [];
  let ativo = -1;
  let seq = 0;
  let temporizador = null;
  let controle = null;
  let remotos = [];
  let carregando = false;
  let erro = false;

  function abrirItem(item, novaAba) {
    if (!item) return;
    if (item.bloqueado) { config.aoBloqueado?.(item.upgrade ?? 'Plus'); return; }
    if (novaAba) window.open(item.url, '_blank', 'noopener');
    else window.location.href = item.url;
  }

  function marcarAtivo(i, rolar = true) {
    ativo = itens.length ? Math.max(0, Math.min(i, itens.length - 1)) : -1;
    lista.querySelectorAll('.busca-item').forEach((el, k) => el.classList.toggle('ativo', k === ativo));
    const el = ativo >= 0 ? lista.querySelector(`#${idBase}-${ativo}`) : null;
    if (el) {
      input.setAttribute('aria-activedescendant', el.id);
      if (rolar) el.scrollIntoView({ block: 'nearest' });
    } else input.removeAttribute('aria-activedescendant');
  }

  function desenhar() {
    const termo = input.value.trim();
    const palavras = palavrasDe(termo);
    itens = termo
      ? [...filtrarLocais(palavras), ...remotos.map(r => ({ ...r, icone: GRUPOS[r.tipo]?.icone ?? '•', detalhe: subtituloRemoto(r) }))]
      : sugestoes();

    let html = '';
    let grupoAtual = null;
    itens.forEach((it, i) => {
      if (it.tipo !== grupoAtual) {
        if (grupoAtual !== null) html += '</div>';
        grupoAtual = it.tipo;
        html += `<div class="busca-grupo" role="group" aria-label="${esc(GRUPOS[it.tipo]?.rotulo ?? '')}">
          <div class="busca-grupo-titulo">${esc(GRUPOS[it.tipo]?.rotulo ?? '')}</div>`;
      }
      const href = it.bloqueado ? '#' : it.url;
      html += `<a class="busca-item${it.inativo ? ' inativo' : ''}" id="${idBase}-${i}" role="option" href="${esc(href)}" data-i="${i}">
        <span class="busca-item-icone" aria-hidden="true">${esc(it.icone ?? '')}</span>
        <span class="busca-item-texto">
          <span class="busca-item-titulo">${destacar(it.titulo, palavras)}</span>
          ${it.detalhe ? `<span class="busca-item-sub">${destacar(it.detalhe, palavras)}</span>` : ''}
        </span>
        ${it.bloqueado ? `<span class="busca-item-selo">${esc((it.upgrade ?? 'Plus').toUpperCase())}</span>` : ''}
      </a>`;
    });
    if (grupoAtual !== null) html += '</div>';

    if (!termo) {
      lista.innerHTML = html + (dica ? `<div class="busca-status">${esc(dica)}</div>` : '');
      marcarAtivo(-1);
      aoMudar(false);
      return;
    }

    if (carregando) html += '<div class="busca-status">Buscando…</div>';
    else if (erro) html += '<div class="busca-status erro">Não foi possível buscar agora. Tente de novo.</div>';
    else if (!itens.length) {
      html += termo.length < TAMANHO_MINIMO
        ? '<div class="busca-status">Digite ao menos 2 letras.</div>'
        : `<div class="busca-status">Nada encontrado para “${esc(termo)}”.</div>`;
    }

    lista.innerHTML = html;
    marcarAtivo(itens.length ? 0 : -1, false);
    aoMudar(true);
  }

  async function buscarRemoto(termo) {
    const minha = ++seq;
    controle?.abort();
    controle = new AbortController();
    try {
      const r = await apiFetch(`/busca?q=${encodeURIComponent(termo)}`, { signal: controle.signal });
      if (minha !== seq) return;
      remotos = (r?.itens ?? []).map(i => ({
        tipo: i.tipo, titulo: i.titulo, subtitulo: i.subtitulo, url: i.url, data: i.data, inativo: i.inativo,
      }));
      erro = false;
    } catch (e) {
      if (e?.name === 'AbortError' || minha !== seq) return;
      remotos = [];
      erro = true;
    }
    carregando = false;
    desenhar();
  }

  function aoDigitar() {
    const termo = input.value.trim();
    clearTimeout(temporizador);
    remotos = [];
    erro = false;
    carregando = termo.length >= TAMANHO_MINIMO;
    if (!carregando) { seq++; controle?.abort(); }
    desenhar();
    if (carregando) temporizador = setTimeout(() => buscarRemoto(termo), ESPERA_MS);
  }

  input.addEventListener('input', aoDigitar);
  input.addEventListener('keydown', e => {
    if (e.key === 'ArrowDown') { e.preventDefault(); marcarAtivo(ativo + 1); }
    else if (e.key === 'ArrowUp') { e.preventDefault(); marcarAtivo(ativo - 1); }
    else if (e.key === 'Enter') {
      if (ativo < 0) return;
      e.preventDefault();
      abrirItem(itens[ativo], e.ctrlKey || e.metaKey);
    } else if (e.key === 'Escape') {
      e.preventDefault();
      e.stopPropagation();
      aoEscapar();
    }
  });

  // Mantém o foco no campo ao clicar na lista (o blur fecharia a suspensa antes do clique).
  lista.addEventListener('mousedown', e => { if (e.target.closest('.busca-item')) e.preventDefault(); });
  lista.addEventListener('mousemove', e => {
    const el = e.target.closest('.busca-item');
    if (el && Number(el.dataset.i) !== ativo) marcarAtivo(Number(el.dataset.i), false);
  });
  lista.addEventListener('click', e => {
    const el = e.target.closest('.busca-item');
    if (!el || el.dataset.i === undefined) return;
    const item = itens[Number(el.dataset.i)];
    // Ctrl/⌘/Shift + clique e clique do meio ficam com o navegador (nova aba/janela).
    if (item?.bloqueado) { e.preventDefault(); abrirItem(item); }
  });

  return { atualizar: aoDigitar, desenhar };
}

// ── Paleta (Ctrl/⌘ + K) ─────────────────────────────────

// Com o campo vazio a paleta oferece os atalhos de criação mais usados.
const sugestoesPaleta = () => ACOES.slice(0, 4).map(a => ({ tipo: 'acao', titulo: a.titulo, url: a.url, icone: a.icone }));
const DICA_PALETA = 'Busque pelo número do processo (com ou sem pontuação), nome da parte, CPF/CNPJ, tarefa, evento ou documento.';

function montarPaleta() {
  if (paleta) return paleta;
  const overlay = document.createElement('div');
  overlay.className = 'busca-overlay';
  overlay.hidden = true;
  overlay.innerHTML = `
    <div class="busca-paleta" role="dialog" aria-modal="true" aria-label="Busca global">
      <label class="busca-campo">
        ${LUPA}
        <input type="search" placeholder="Buscar processos, contatos, tarefas, documentos…" autocomplete="off"
               spellcheck="false" role="combobox" aria-expanded="true" aria-controls="buscaPaletaLista" aria-autocomplete="list">
        <kbd>Esc</kbd>
      </label>
      <div class="busca-lista" id="buscaPaletaLista" role="listbox"></div>
      <div class="busca-rodape" aria-hidden="true">
        <span><kbd>↑</kbd><kbd>↓</kbd> navegar</span>
        <span><kbd>Enter</kbd> abrir</span>
        <span><kbd>${ehMac ? '⌘' : 'Ctrl'}</kbd>+<kbd>Enter</kbd> nova aba</span>
      </div>
    </div>`;
  document.body.appendChild(overlay);

  const input = overlay.querySelector('input');
  const lista = overlay.querySelector('.busca-lista');
  let focoAnterior = null;

  const fechar = () => {
    overlay.hidden = true;
    document.body.classList.remove('busca-aberta');
    focoAnterior?.focus?.();
  };
  const motor = criarMotor({
    input, lista, idBase: 'buscaPaletaItem',
    sugestoes: sugestoesPaleta,
    dica: DICA_PALETA,
    aoEscapar: () => (input.value ? (input.value = '', motor.atualizar()) : fechar()),
  });

  overlay.addEventListener('mousedown', e => { if (e.target === overlay) fechar(); });
  // Sair da paleta com Tab volta ao campo (é um diálogo modal).
  overlay.addEventListener('keydown', e => { if (e.key === 'Tab') { e.preventDefault(); input.focus(); } });

  paleta = {
    abrir(termo = '') {
      focoAnterior = document.activeElement;
      overlay.hidden = false;
      document.body.classList.add('busca-aberta');
      input.value = termo;
      motor.atualizar();
      input.focus();
      input.select();
    },
    fechar,
    aberta: () => !overlay.hidden,
  };
  return paleta;
}

export function abrirBuscaGlobal(termo = '') {
  montarPaleta().abrir(termo);
}

function emCampoDeTexto(el) {
  return !!el?.closest?.('input, textarea, select, [contenteditable=""], [contenteditable="true"]');
}

function injetarBotaoHeader() {
  const header = document.querySelector('.header');
  if (!header || document.getElementById('buscaGlobalBtn')) return;
  const btn = document.createElement('button');
  btn.type = 'button';
  btn.id = 'buscaGlobalBtn';
  btn.className = 'busca-header-btn';
  btn.title = `Buscar (${ATALHO_BUSCA})`;
  btn.setAttribute('aria-label', 'Buscar');
  btn.innerHTML = `${LUPA}<span class="busca-header-rotulo">Buscar…</span><kbd>${ATALHO_BUSCA}</kbd>`;
  btn.addEventListener('click', () => abrirBuscaGlobal());
  const usuario = header.querySelector('.header-user');
  header.insertBefore(btn, usuario ?? null);
}

let teclasLigadas = false;
function ligarAtalhos() {
  if (teclasLigadas) return;
  teclasLigadas = true;
  document.addEventListener('keydown', e => {
    const k = e.key?.toLowerCase();
    const ctrlK = k === 'k' && (e.ctrlKey || e.metaKey) && !e.altKey && !e.shiftKey;
    const barra = e.key === '/' && !e.ctrlKey && !e.metaKey && !e.altKey && !emCampoDeTexto(e.target)
      && !document.querySelector('.modal-overlay.open');
    if (!ctrlK && !barra) return;
    e.preventDefault();
    if (paleta?.aberta()) { if (ctrlK) paleta.fechar(); return; }
    // No dashboard o atalho vai para a caixa de busca da própria página, se estiver à vista.
    const caixa = document.querySelector('.busca-caixa input');
    if (caixa && caixa.offsetParent !== null) {
      const r = caixa.getBoundingClientRect();
      if (r.bottom > 0 && r.top < window.innerHeight) { caixa.focus(); caixa.select(); return; }
    }
    abrirBuscaGlobal();
  });
}

/**
 * Liga a busca global na página (chamado por initLayout).
 * @param {{ paginas: {titulo:string,url:string,icone?:string,bloqueado?:boolean,upgrade?:string}[], plano: string, aoBloqueado?: (plano:string)=>void }} opcoes
 */
export function initBuscaGlobal(opcoes) {
  config = { ...config, ...opcoes };
  injetarBotaoHeader();
  ligarAtalhos();
}

/** Atualiza as páginas do menu (ex.: depois de carregar os recursos em teste liberados). */
export function atualizarPaginasBusca(paginas) {
  config.paginas = paginas;
}

/**
 * Caixa de busca embutida na página (dashboard): mesmo motor da paleta, resultados numa lista
 * suspensa logo abaixo do campo.
 */
export function montarCaixaBusca(container, { placeholder = 'Buscar processos, contatos, tarefas, documentos…' } = {}) {
  if (!container) return;
  container.classList.add('busca-caixa');
  container.innerHTML = `
    <label class="busca-campo">
      ${LUPA}
      <input type="search" placeholder="${esc(placeholder)}" autocomplete="off" spellcheck="false"
             role="combobox" aria-expanded="false" aria-controls="buscaCaixaLista" aria-autocomplete="list"
             aria-label="Buscar no escritório">
      <kbd>${ATALHO_BUSCA}</kbd>
    </label>
    <div class="busca-lista busca-suspensa" id="buscaCaixaLista" role="listbox" hidden></div>`;
  const input = container.querySelector('input');
  const lista = container.querySelector('.busca-lista');

  const mostrar = visivel => {
    lista.hidden = !visivel;
    input.setAttribute('aria-expanded', String(visivel));
    container.classList.toggle('aberta', visivel);
  };
  const motor = criarMotor({
    input, lista, idBase: 'buscaCaixaItem',
    aoMudar: temItens => mostrar(temItens && document.activeElement === input),
    aoEscapar: () => {
      if (input.value) { input.value = ''; motor.atualizar(); }
      else input.blur();
    },
  });

  input.addEventListener('focus', () => { if (input.value.trim()) { motor.desenhar(); mostrar(true); } });
  input.addEventListener('blur', () => mostrar(false));
}
