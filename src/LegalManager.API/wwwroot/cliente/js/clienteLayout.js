import { getUser, logout, isLoggedIn, urlLogin, atualizarPerfil, getTemaPortal } from './clienteApi.js';
import { applyTenantTheme, sanitizeHex } from '/js/theme.js';
import { injectPerguntasWidget } from './clientePerguntas.js';

import { esc as escapeHtml } from '/js/utils.js';
export function requireAuth() {
  if (!isLoggedIn()) {
    window.location.href = urlLogin();
    return null;
  }
  return getUser();
}

export function initLayout(activePage) {
  const user = requireAuth();
  if (!user) return null;

  aplicarTemaPortal(getTemaPortal() || user?.tema);

  const navEl = document.getElementById('portalNav');
  if (!navEl) return user;

  const itens = [
    { id: 'dashboard', href: '/cliente/dashboard.html', rotulo: 'Início', icone: 'inicio' },
    { id: 'processos', href: '/cliente/processos.html', rotulo: 'Meus Processos', curto: 'Processos', icone: 'processos' },
    { id: 'honorarios', href: '/cliente/honorarios.html', rotulo: 'Honorários', icone: 'honorarios' },
  ];
  const ativo = (i) => i.id === activePage ? ' class="active" aria-current="page"' : '';

  navEl.innerHTML = `
    <a class="portal-nav-brand" href="/cliente/dashboard.html" id="navBrand">${marcaHtml(user)}</a>
    <nav class="portal-nav-links" aria-label="Navegação principal">
      ${itens.map(i => `<a href="${i.href}"${ativo(i)}>${icone(i.icone)}${i.rotulo}</a>`).join('')}
    </nav>
    <div class="portal-nav-user">
      <span class="nav-user-nome">${escapeHtml(user.nome)}</span>
      <span class="avatar" title="${escapeHtml(user.nome)}" aria-hidden="true">${escapeHtml(iniciais(user.nome))}</span>
      <button class="btn-logout" id="logoutBtn">${icone('sair')}Sair</button>
    </div>
  `;

  document.getElementById('logoutBtn').addEventListener('click', logout);

  // Mobile: barra de navegação inferior (como no painel do escritório).
  document.getElementById('bottomNav')?.remove();
  const bottomNav = document.createElement('nav');
  bottomNav.id = 'bottomNav';
  bottomNav.className = 'bottom-nav';
  bottomNav.setAttribute('aria-label', 'Navegação');
  bottomNav.innerHTML = `<div class="bottom-nav-inner">
    ${itens.map(i => `<a href="${i.href}" class="bottom-nav-item${i.id === activePage ? ' active' : ''}"${i.id === activePage ? ' aria-current="page"' : ''}>
      <span class="bn-icone">${icone(i.icone)}</span><span>${i.curto || i.rotulo}</span></a>`).join('')}
    <button type="button" class="bottom-nav-item" id="bottomNavSair">
      <span class="bn-icone">${icone('sair')}</span><span>Sair</span></button>
  </div>`;
  document.body.appendChild(bottomNav);
  document.getElementById('bottomNavSair').addEventListener('click', () => {
    if (confirm('Sair do Portal do Cliente?')) logout();
  });

  // Pesquisa de interesse/satisfação — assíncrono; se não houver pergunta
  // pendente para o cliente, não injeta nada (nem o botão flutuante aparece).
  injectPerguntasWidget();

  // O tema guardado no login pode estar velho: relê o perfil e reaplica se o escritório mudou algo.
  atualizarPerfil().then(perfil => {
    if (!perfil) return;
    aplicarTemaPortal(perfil.tema);
    const brand = document.getElementById('navBrand');
    if (brand) brand.innerHTML = marcaHtml(perfil);
  }).catch(() => {});

  return user;
}

const ICONES = {
  inicio: '<path d="M3 10.5 12 3l9 7.5V20a1 1 0 0 1-1 1h-5v-6H9v6H4a1 1 0 0 1-1-1z"/>',
  processos: '<path d="M3 7a2 2 0 0 1 2-2h4l2 2h8a2 2 0 0 1 2 2v9a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2z"/>',
  honorarios: '<rect x="3" y="6" width="18" height="13" rx="2"/><path d="M3 10h18"/><path d="M7 15h3"/>',
  sair: '<path d="M9 21H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h4"/><path d="m16 17 5-5-5-5"/><path d="M21 12H9"/>',
  documento: '<path d="M14 3H7a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h10a2 2 0 0 0 2-2V8z"/><path d="M14 3v5h5"/><path d="M9 13h6"/><path d="M9 17h4"/>',
  atividade: '<path d="M22 12h-4l-3 9L9 3l-3 9H2"/>',
  seta: '<path d="m9 18 6-6-6-6"/>',
  voltar: '<path d="m15 18-6-6 6-6"/>',
};

/** Ícone SVG (traço, herda a cor do texto). */
export function icone(nome) {
  return `<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">${ICONES[nome] || ''}</svg>`;
}

function iniciais(nome) {
  const partes = (nome || '').trim().split(/\s+/).filter(Boolean);
  if (!partes.length) return '?';
  return ((partes[0][0] || '') + (partes.length > 1 ? partes[partes.length - 1][0] : '')).toUpperCase();
}

// Logo do escritório (Configurações → Tema); sem logo, o nome do escritório.
function marcaHtml(perfil) {
  const tema = perfil?.tema;
  const nome = perfil?.nomeEscritorio || 'Portal do Cliente';
  if (tema?.logoUrl) {
    return `<img src="${escapeHtml(tema.logoUrl)}" alt="${escapeHtml(nome)}" class="portal-nav-logo"
      onerror="this.replaceWith(Object.assign(document.createElement('span'), { textContent: this.alt }))">`;
  }
  return `<span>⚖️ ${escapeHtml(nome)}</span>`;
}

function luminancia(hex) {
  const h = hex.replace('#', '');
  const full = h.length === 3 ? h.split('').map(c => c + c).join('') : h;
  const [r, g, b] = [0, 2, 4].map(i => parseInt(full.slice(i, i + 2), 16));
  return (r * 0.299 + g * 0.587 + b * 0.114) / 255;
}

// Aplica as cores do escritório; a barra usa a cor do menu lateral do painel e o texto
// escurece quando ela é clara (ex.: menu branco).
function aplicarTemaPortal(tema) {
  if (!tema) return;
  applyTenantTheme(tema);
  const nav = sanitizeHex(tema.sidebarColor);
  document.documentElement.classList.toggle('portal-nav-clara', !!nav && luminancia(nav) > 0.6);
}


export function formatDate(dateStr) {
  if (!dateStr) return '—';
  return new Date(dateStr).toLocaleDateString('pt-BR', { day: '2-digit', month: '2-digit', year: 'numeric' });
}

export function formatDateTime(dateStr) {
  if (!dateStr) return '—';
  const d = new Date(dateStr);
  // Andamentos do tribunal costumam vir só com a data (meia-noite UTC): sem hora, mostra só o dia.
  if (d.getUTCHours() === 0 && d.getUTCMinutes() === 0) {
    return d.toLocaleDateString('pt-BR', { timeZone: 'UTC', day: '2-digit', month: '2-digit', year: 'numeric' });
  }
  return d.toLocaleDateString('pt-BR', {
    timeZone: 'UTC',
    day: '2-digit', month: '2-digit', year: 'numeric',
    hour: '2-digit', minute: '2-digit', timeZoneName: 'short'
  });
}

const STATUS_LABELS = {
  Ativo: ['badge-green', 'Ativo'],
  Suspenso: ['badge-yellow', 'Suspenso'],
  Arquivado: ['badge-gray', 'Arquivado'],
  Encerrado: ['badge-gray', 'Encerrado'],
};

const FASE_LABELS = {
  Conhecimento: ['badge-blue', 'Conhecimento'],
  Recursal: ['badge-purple', 'Recursal'],
  Execucao: ['badge-yellow', 'Execução'],
  Cumprimento: ['badge-yellow', 'Cumprimento de sentença'],
  InqueritoPolicial: ['badge-gray', 'Inquérito policial'],
  InvestigacaoDefensiva: ['badge-gray', 'Investigação defensiva'],
  Outro: ['badge-gray', 'Outra fase'],
};

const TIPO_PARTE_LABELS = {
  Autor: 'Autor',
  Reu: 'Réu',
  Interessado: 'Interessado',
  Terceiro: 'Terceiro',
  Litisconsorte: 'Litisconsorte',
};

export function statusBadge(status) {
  const [cls, label] = STATUS_LABELS[status] || ['badge-gray', status];
  return `<span class="badge ${cls}">${label}</span>`;
}

export function faseBadge(fase) {
  const [cls, label] = FASE_LABELS[fase] || ['badge-gray', fase];
  return `<span class="badge ${cls}">${label}</span>`;
}

export function tipoParteLabel(tipo) {
  return TIPO_PARTE_LABELS[tipo] || tipo;
}

const TIPO_ANDAMENTO_LABELS = {
  Despacho: 'Despacho', Decisao: 'Decisão', Sentenca: 'Sentença', Acordao: 'Acórdão',
  Audiencia: 'Audiência', Peticao: 'Petição', Intimacao: 'Intimação', Publicacao: 'Publicação', Outro: 'Outro',
};

export function tipoAndamentoLabel(tipo) {
  return TIPO_ANDAMENTO_LABELS[tipo] || tipo;
}

const AREA_LABELS = {
  Civil: 'Cível', Trabalhista: 'Trabalhista', Criminal: 'Criminal', Tributario: 'Tributário',
  Familia: 'Família', Previdenciario: 'Previdenciário', Empresarial: 'Empresarial',
  Consumidor: 'Consumidor', Administrativo: 'Administrativo', Ambiental: 'Ambiental',
  Imobiliario: 'Imobiliário', Outro: 'Outro',
};

export function areaLabel(area) {
  return AREA_LABELS[area] || area;
}

/** Cartão de processo das listas do portal (linha: sem moldura, dentro de um card). */
export function processoCardHtml(p, { linha = false, area = false } = {}) {
  return `
    <a class="processo-card${linha ? ' linha' : ''}" href="/cliente/processo-detalhe.html?id=${encodeURIComponent(p.id)}">
      <span class="processo-icone">${icone('processos')}</span>
      <div class="processo-corpo">
        <div class="processo-cnj">${escapeHtml(p.numeroCNJ)}</div>
        <div class="processo-meta">
          ${p.tribunal ? `<span>${escapeHtml(p.tribunal)}</span>` : ''}
          ${p.comarca ? `<span>${escapeHtml(p.comarca)}</span>` : ''}
          ${area && p.areaDireito ? `<span>${escapeHtml(areaLabel(p.areaDireito))}</span>` : ''}
          <span>${escapeHtml(tipoParteLabel(p.tipoParte))}</span>
        </div>
        <div class="processo-tags">
          ${statusBadge(p.status)}
          ${faseBadge(p.fase)}
          ${p.totalAndamentos > 0 ? `<span class="badge badge-gray">${p.totalAndamentos} andamento${p.totalAndamentos !== 1 ? 's' : ''}</span>` : ''}
        </div>
      </div>
      <span class="processo-seta">${icone('seta')}</span>
    </a>`;
}
