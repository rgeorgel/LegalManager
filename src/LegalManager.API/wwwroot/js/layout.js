import { isLoggedIn, logout, getUser } from './auth.js';
import { apiFetch } from './api.js';

const NAV_ITEMS = [
  { href: '/pages/dashboard.html',   label: '📊 Dashboard' },
  { href: '/pages/processos.html',   label: '⚖️ Processos' },
  { href: '/pages/contatos.html',    label: '👥 Contatos' },
  { href: '/pages/tarefas.html',     label: '✅ Tarefas' },
  { href: '/pages/kanban.html',      label: '🗂️ Kanban' },
  { href: '/pages/agenda.html',      label: '📅 Agenda' },
  { href: '/pages/financeiro.html',  label: '💰 Financeiro' },
  { href: '/pages/timesheet.html',   label: '⏱️ Timesheet' },
  { href: '/pages/publicacoes.html', label: '📰 Publicações' },
  { href: '/pages/prazos.html',      label: '⏰ Prazos' },
  { href: '/pages/usuarios.html',    label: '🔑 Usuários' },
  { href: '/pages/configuracoes.html', label: '⚙️ Configurações' },
];

export function initLayout() {
  if (!isLoggedIn()) {
    window.location.href = '/index.html';
    return;
  }

  const user = getUser();

  document.querySelector('.header-user-name').textContent = user?.nome ?? '';
  document.querySelector('.header-tenant').textContent = user?.nomeEscritorio ?? '';

  document.getElementById('logoutBtn')?.addEventListener('click', () => logout());

  // Inject full nav
  const navEl = document.querySelector('.sidebar-nav');
  if (navEl) {
    const current = window.location.pathname;
    navEl.innerHTML = NAV_ITEMS.map(item =>
      `<li><a href="${item.href}"${item.href === current ? ' class="active"' : ''}>${item.label}</a></li>`
    ).join('');
    navEl.querySelectorAll('a').forEach(link => {
      link.addEventListener('click', () => document.querySelector('.sidebar')?.classList.remove('open'));
    });
  }

  const hamburger = document.getElementById('hamburger');
  const sidebar = document.querySelector('.sidebar');
  hamburger?.addEventListener('click', () => sidebar?.classList.toggle('open'));

  injectNotificationBell();
}

function injectNotificationBell() {
  const headerUser = document.querySelector('.header-user');
  if (!headerUser || document.getElementById('notifBell')) return;

  const bell = document.createElement('div');
  bell.id = 'notifBell';
  bell.style.cssText = 'position:relative;cursor:pointer;padding:6px;display:flex;align-items:center;user-select:none';
  bell.innerHTML = `
    <span style="font-size:18px" title="Notificações">🔔</span>
    <span id="notifBadge" style="display:none;position:absolute;top:2px;right:2px;background:#dc2626;color:#fff;border-radius:999px;font-size:10px;font-weight:700;min-width:16px;height:16px;line-height:16px;text-align:center;padding:0 4px"></span>
    <div id="notifDropdown" style="display:none;position:absolute;top:100%;right:0;z-index:9999;width:320px;background:#fff;border:1px solid var(--color-border);border-radius:8px;box-shadow:0 8px 24px rgba(0,0,0,.12);max-height:400px;overflow-y:auto"></div>
  `;
  headerUser.insertBefore(bell, headerUser.firstChild);

  bell.addEventListener('click', e => {
    e.stopPropagation();
    const dd = document.getElementById('notifDropdown');
    if (dd.style.display === 'none') {
      loadNotificacoes();
      dd.style.display = '';
    } else {
      dd.style.display = 'none';
    }
  });

  document.addEventListener('click', () => {
    const dd = document.getElementById('notifDropdown');
    if (dd) dd.style.display = 'none';
  });

  loadNotifCount();
  // refresh count every 60s
  setInterval(loadNotifCount, 60000);
}

async function loadNotifCount() {
  try {
    const count = await apiFetch('/notificacoes/count');
    const badge = document.getElementById('notifBadge');
    if (!badge) return;
    if (count > 0) {
      badge.textContent = count > 99 ? '99+' : String(count);
      badge.style.display = '';
    } else {
      badge.style.display = 'none';
    }
  } catch { /* silent */ }
}

async function loadNotificacoes() {
  const dd = document.getElementById('notifDropdown');
  if (!dd) return;
  dd.innerHTML = '<div style="padding:12px;text-align:center;color:#6b7280;font-size:13px">Carregando...</div>';

  try {
    const items = await apiFetch('/notificacoes');
    if (!items.length) {
      dd.innerHTML = '<div style="padding:16px;text-align:center;color:#6b7280;font-size:13px">Nenhuma notificação</div>';
      return;
    }

    const header = `
      <div style="display:flex;justify-content:space-between;align-items:center;padding:10px 14px;border-bottom:1px solid #f3f4f6">
        <strong style="font-size:13px">Notificações</strong>
        <button id="notifMarcarTodas" style="background:none;border:none;color:#1a56db;font-size:12px;cursor:pointer">Marcar todas como lidas</button>
      </div>`;

    const rows = items.map(n => `
      <div class="notif-item" data-id="${n.id}" data-url="${n.url ?? ''}"
           style="padding:10px 14px;border-bottom:1px solid #f3f4f6;cursor:pointer;background:${n.lida ? '#fff' : '#eff6ff'};transition:.15s">
        <div style="font-weight:600;font-size:13px">${esc(n.titulo)}</div>
        <div style="font-size:12px;color:#6b7280;margin-top:2px">${esc(n.mensagem)}</div>
        <div style="font-size:11px;color:#9ca3af;margin-top:4px">${timeAgo(n.criadaEm)}</div>
      </div>`).join('');

    dd.innerHTML = header + rows;

    dd.querySelector('#notifMarcarTodas')?.addEventListener('click', async e => {
      e.stopPropagation();
      await apiFetch('/notificacoes/marcar-todas-lidas', { method: 'POST' });
      await loadNotifCount();
      dd.style.display = 'none';
    });

    dd.querySelectorAll('.notif-item').forEach(item => {
      item.addEventListener('click', async e => {
        e.stopPropagation();
        const id = item.dataset.id;
        const url = item.dataset.url;
        await apiFetch(`/notificacoes/${id}/lida`, { method: 'POST' });
        await loadNotifCount();
        dd.style.display = 'none';
        if (url) window.location.href = url;
      });
    });
  } catch {
    dd.innerHTML = '<div style="padding:12px;text-align:center;color:#e02424;font-size:13px">Erro ao carregar notificações.</div>';
  }
}

function timeAgo(isoStr) {
  const diff = Math.floor((Date.now() - new Date(isoStr).getTime()) / 60000);
  if (diff < 1) return 'agora';
  if (diff < 60) return `${diff} min atrás`;
  if (diff < 1440) return `${Math.floor(diff / 60)}h atrás`;
  return `${Math.floor(diff / 1440)}d atrás`;
}

function esc(str) {
  return (str ?? '').replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');
}
