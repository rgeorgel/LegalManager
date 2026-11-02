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

  navEl.innerHTML = `
    <a class="portal-nav-brand" href="/cliente/dashboard.html" id="navBrand">${marcaHtml(user)}</a>
    <button class="nav-toggle" id="navToggle">☰</button>
    <nav class="portal-nav-links" id="navLinks">
      <a href="/cliente/dashboard.html" ${activePage === 'dashboard' ? 'class="active"' : ''}>Início</a>
      <a href="/cliente/processos.html" ${activePage === 'processos' ? 'class="active"' : ''}>Meus Processos</a>
      <a href="/cliente/honorarios.html" ${activePage === 'honorarios' ? 'class="active"' : ''}>Honorários</a>
    </nav>
    <div class="portal-nav-user">
      <span>${escapeHtml(user.nome)}</span>
      <button class="btn-logout" id="logoutBtn">Sair</button>
    </div>
  `;

  document.getElementById('logoutBtn').addEventListener('click', logout);

  const toggle = document.getElementById('navToggle');
  const links = document.getElementById('navLinks');
  toggle.addEventListener('click', () => links.classList.toggle('open'));

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
  return new Date(dateStr).toLocaleDateString('pt-BR', {
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
  PreContencioso: ['badge-gray', 'Pré-Contencioso'],
  Encerrado: ['badge-gray', 'Encerrado'],
};

const TIPO_PARTE_LABELS = {
  Autor: 'Autor',
  Reu: 'Réu',
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
