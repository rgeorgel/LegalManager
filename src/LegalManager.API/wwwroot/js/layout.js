import { isLoggedIn, logout, getUser } from './auth.js';
import { apiFetch } from './api.js';

const NAV_GROUPS = [
  {
    label: 'Visão Geral',
    items: [
      { href: '/pages/dashboard.html',   label: '📊 Dashboard' },
    ],
  },
  {
    label: 'Gestão',
    items: [
      { href: '/pages/processos.html',    label: '⚖️ Processos' },
      { href: '/pages/contatos.html',     label: '👥 Contatos' },
      { href: '/pages/documentos.html',   label: '📁 Documentos' },
      { href: '/pages/publicacoes.html',   label: '📰 Publicações', pro: true },  // Pro-only: Plus não tem acesso
      { href: '/pages/oabs.html',          label: '👨‍⚖️ OABs Monitoradas', pro: true },
    ],
  },
  {
    label: 'Produtividade',
    items: [
      { href: '/pages/tarefas.html',      label: '✅ Tarefas/Prazos' },
      { href: '/pages/agenda.html',        label: '📅 Agenda' },
      { href: '/pages/timesheet.html',     label: '⏱️ Timesheet' },
    ],
  },
  {
    label: 'Financeiro',
    items: [
      { href: '/pages/financeiro.html',    label: '💰 Financeiro', plus: true },
    ],
  },
  {
    label: 'Calculadoras',
    items: [
      { href: '/pages/honorarios.html',          label: '⚖️ Honorários' },
      { href: '/pages/calculadora-prazos.html',  label: '📅 Calculadora de Prazos', plus: true },
    ],
  },
  {
    label: 'Sistema',
    items: [
      { href: '/pages/alertas.html',          label: '🔔 Alertas' },
      { href: '/pages/configuracoes.html',  label: '⚙️ Configurações' },
      { href: '/pages/assinatura.html',     label: '💳 Assinatura' },
    ],
  },
];

const BOTTOM_NAV_ITEMS = [
  { href: '/pages/dashboard.html', icon: '📊', label: 'Dashboard' },
  { href: '/pages/processos.html', icon: '⚖️', label: 'Processos' },
  { href: '/pages/tarefas.html', icon: '✅', label: 'Tarefas/Prazos' },
  { href: '/pages/agenda.html', icon: '📅', label: 'Agenda' },
  { action: 'menu', icon: '☰', label: 'Menu' },
];

export function getPlano() {
  return getUser()?.plano ?? 'Free';
}

export function isPlanoFree() {
  return getPlano() === 'Free';
}

export function isPlanoPlus() {
  return getPlano() === 'Plus';
}

export function initLayout() {
  if (!isLoggedIn()) {
    window.location.href = '/login.html';
    return;
  }

  const user = getUser();

  document.querySelector('.header-user-name').textContent = user?.nome ?? '';
  document.querySelector('.header-tenant').textContent = user?.nomeEscritorio ?? '';

  document.getElementById('logoutBtn')?.addEventListener('click', () => logout());

  injectSidebarNav();
  injectBottomNav();
  setupMobileMenu();
  injectNotificationBell();
}

function injectSidebarNav() {
  const navEl = document.querySelector('.sidebar-nav');
  if (!navEl) return;

  const current = window.location.pathname;
  const plano = getPlano();
  const free = plano === 'Free';
  const plus = plano === 'Plus';

  let collapsedGroups = {};
  try {
    collapsedGroups = JSON.parse(localStorage.getItem('sidebarCollapsed') || '{}');
  } catch {}

  navEl.innerHTML = NAV_GROUPS.map(group => {
    const isCollapsed = collapsedGroups[group.label] === true;
    const groupId = 'grp-' + group.label.toLowerCase().replace(/\s+/g, '-');

    const itemsHtml = group.items.map(item => {
      // pro:true = requer Pro (bloqueia Free e Plus)
      // plus:true = requer Plus (bloqueia apenas Free)
      const lockedPro = item.pro && (free || plus);
      const lockedPlus = item.plus && free;
      const locked = lockedPro || lockedPlus;
      const badgeText = lockedPro ? 'PRO' : 'PLUS';
      const upgradeTarget = lockedPro ? 'Pro' : 'Plus';
      const activeClass = item.href === current ? ' class="active"' : '';
      const badge = locked ? ` <span style="background:#f59e0b;color:#0f172a;font-size:10px;font-weight:600;padding:1px 6px;border-radius:100px;letter-spacing:.04em;vertical-align:middle">${badgeText}</span>` : '';
      if (locked) {
        return `<li><a href="#" data-locked="true" data-upgrade="${upgradeTarget}"${activeClass} title="Disponível no plano ${upgradeTarget}">${item.label}${badge}</a></li>`;
      }
      return `<li><a href="${item.href}"${activeClass}>${item.label}</a></li>`;
    }).join('');

    return `
      <li class="nav-group" data-group="${groupId}">
        <div class="nav-group-header" title="Clique para ${isCollapsed ? 'expandir' : 'recolher'}">
          <span class="nav-group-toggle">${isCollapsed ? '▶' : '▼'}</span>
          <span class="nav-group-label">${group.label}</span>
        </div>
        <ul class="nav-group-items" style="${isCollapsed ? 'display:none' : ''}">
          ${itemsHtml}
        </ul>
      </li>
    `;
  }).join('');

  navEl.querySelectorAll('a').forEach(link => {
    if (link.dataset.locked) {
      link.addEventListener('click', e => {
        e.preventDefault();
        showUpgradeToast(link.dataset.upgrade ?? 'Pro');
      });
    } else {
      link.addEventListener('click', () => closeMobileMenu());
    }
  });

  navEl.querySelectorAll('.nav-group-header').forEach(header => {
    header.addEventListener('click', () => {
      const groupEl = header.parentElement;
      const groupLabel = groupEl.dataset.group;
      const itemsEl = groupEl.querySelector('.nav-group-items');
      const toggleEl = header.querySelector('.nav-group-toggle');
      const isCollapsed = itemsEl.style.display === 'none';

      if (isCollapsed) {
        itemsEl.style.display = '';
        toggleEl.textContent = '▼';
        delete collapsedGroups[groupLabel];
      } else {
        itemsEl.style.display = 'none';
        toggleEl.textContent = '▶';
        collapsedGroups[groupLabel] = true;
      }
      localStorage.setItem('sidebarCollapsed', JSON.stringify(collapsedGroups));
    });
  });
}

function injectBottomNav() {
  const existing = document.getElementById('bottomNav');
  if (existing) existing.remove();

  const bottomNav = document.createElement('nav');
  bottomNav.id = 'bottomNav';
  bottomNav.className = 'bottom-nav';

  const current = window.location.pathname;

  bottomNav.innerHTML = `
    <div class="bottom-nav-inner">
      ${BOTTOM_NAV_ITEMS.map(item => {
        const isActive = item.href && item.href === current;
        const activeClass = isActive ? ' active' : '';
        if (item.action === 'menu') {
          return `<div class="bottom-nav-item${activeClass}" data-action="menu" title="Menu">
            <span>${item.icon}</span>
            <span>${item.label}</span>
          </div>`;
        }
        return `<a href="${item.href}" class="bottom-nav-item${activeClass}">
          <span>${item.icon}</span>
          <span>${item.label}</span>
        </a>`;
      }).join('')}
    </div>
  `;

  document.body.appendChild(bottomNav);

  bottomNav.querySelectorAll('.bottom-nav-item').forEach(item => {
    item.addEventListener('click', e => {
      if (item.dataset.action === 'menu') {
        e.preventDefault();
        toggleMobileMenu();
      }
    });
  });
}

let _overlay = null;

function toggleMobileMenu() {
  const sidebar = document.querySelector('.sidebar');
  if (!sidebar) return;

  const isOpen = sidebar.classList.contains('open');

  if (!isOpen) {
    if (!_overlay) {
      _overlay = document.createElement('div');
      _overlay.id = 'sidebarOverlay';
      _overlay.className = 'sidebar-overlay';
      _overlay.addEventListener('click', closeMobileMenu);
      document.body.appendChild(_overlay);
    }
    requestAnimationFrame(() => _overlay.classList.add('visible'));
    sidebar.classList.add('open');
  } else {
    closeMobileMenu();
  }
}

function closeMobileMenu() {
  const sidebar = document.querySelector('.sidebar');
  sidebar?.classList.remove('open');
  _overlay?.classList.remove('visible');
}

function setupMobileMenu() {
  const hamburger = document.getElementById('hamburger');
  hamburger?.addEventListener('click', () => toggleMobileMenu());
}

function injectNotificationBell() {
  const headerUser = document.querySelector('.header-user');
  if (!headerUser || document.getElementById('notifBell')) return;

  const bell = document.createElement('div');
  bell.id = 'notifBell';
  bell.style.cssText = 'position:relative;cursor:pointer;padding:6px;display:flex;align-items:center;user-select:none';
  bell.innerHTML = `
    <span style="font-size:18px" title="Alertas">🔔</span>
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
        <strong style="font-size:13px">Alertas</strong>
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
    dd.innerHTML = '<div style="padding:12px;text-align:center;color:#e02424;font-size:13px">Erro ao carregar alertas.</div>';
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

export function showUpgradeToast(targetPlano = 'Pro', msg = null) {
  const defaultMsg = targetPlano === 'Plus'
    ? 'Esta funcionalidade está disponível a partir do plano Plus.'
    : 'Esta funcionalidade está disponível no plano Pro.';
  const label = targetPlano === 'Plus' ? 'Funcionalidade Plus' : 'Funcionalidade Pro';
  const ctaLabel = targetPlano === 'Plus' ? 'Ver Plus' : 'Ver Pro';

  let toast = document.getElementById('upgradeToast');
  if (!toast) {
    toast = document.createElement('div');
    toast.id = 'upgradeToast';
    toast.style.cssText = [
      'position:fixed;bottom:80px;right:24px;z-index:9999',
      'background:#0f172a;border:1px solid #f59e0b;color:#e2e8f0',
      'border-radius:12px;padding:14px 18px;box-shadow:0 8px 32px rgba(0,0,0,.4)',
      'display:flex;align-items:center;gap:12px;max-width:340px',
      'animation:slideInToast .25s ease',
    ].join(';');
    document.body.appendChild(toast);
    if (!document.getElementById('toastKeyframes')) {
      const s = document.createElement('style');
      s.id = 'toastKeyframes';
      s.textContent = '@keyframes slideInToast{from{transform:translateY(16px);opacity:0}to{transform:none;opacity:1}}';
      document.head.appendChild(s);
    }
  }
  toast.innerHTML = `
    <span style="font-size:20px">⭐</span>
    <div>
      <div style="font-weight:700;font-size:13px;color:#f59e0b">${label}</div>
      <div style="font-size:12px;margin-top:2px">${msg ?? defaultMsg}</div>
    </div>
    <a href="/pages/assinatura.html" style="margin-left:auto;background:#f59e0b;color:#0f172a;font-size:11px;font-weight:600;padding:4px 10px;border-radius:6px;text-decoration:none;white-space:nowrap">${ctaLabel}</a>
  `;
  toast.style.display = 'flex';
  clearTimeout(toast._timeout);
  toast._timeout = setTimeout(() => { toast.style.display = 'none'; }, 4000);
}