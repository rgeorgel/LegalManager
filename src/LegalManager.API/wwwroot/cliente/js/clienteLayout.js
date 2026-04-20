import { getUser, logout, isLoggedIn } from './clienteApi.js';

export function requireAuth() {
  if (!isLoggedIn()) {
    window.location.href = '/cliente/';
    return null;
  }
  return getUser();
}

export function initLayout(activePage) {
  const user = requireAuth();
  if (!user) return null;

  const navEl = document.getElementById('portalNav');
  if (!navEl) return user;

  navEl.innerHTML = `
    <span class="portal-nav-brand">⚖️ Portal do Cliente</span>
    <button class="nav-toggle" id="navToggle">☰</button>
    <nav class="portal-nav-links" id="navLinks">
      <a href="/cliente/dashboard.html" ${activePage === 'dashboard' ? 'class="active"' : ''}>Início</a>
      <a href="/cliente/processos.html" ${activePage === 'processos' ? 'class="active"' : ''}>Meus Processos</a>
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

  return user;
}

function escapeHtml(str) {
  return str.replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
}

export function formatDate(dateStr) {
  if (!dateStr) return '—';
  return new Date(dateStr).toLocaleDateString('pt-BR', { day: '2-digit', month: '2-digit', year: 'numeric' });
}

export function formatDateTime(dateStr) {
  if (!dateStr) return '—';
  return new Date(dateStr).toLocaleDateString('pt-BR', {
    day: '2-digit', month: '2-digit', year: 'numeric',
    hour: '2-digit', minute: '2-digit'
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
