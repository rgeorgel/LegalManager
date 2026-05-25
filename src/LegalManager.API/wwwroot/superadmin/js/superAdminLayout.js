import { isAuthenticated, getAdminUser, clearSession, saFetch } from './superAdminApi.js';

const NAV_GROUPS = [
  {
    label: 'Visão Geral',
    items: [{ href: '/superadmin/dashboard.html', label: '📊 Dashboard' }]
  },
  {
    label: 'Clientes',
    items: [
      { href: '/superadmin/tenants.html', label: '🏢 Tenants' },
      { href: '/superadmin/users.html', label: '👥 Usuários' }
    ]
  },
  {
    label: 'Growth',
    items: [{ href: '/superadmin/waitlist.html', label: '📋 Waitlist' }]
  }
];

export function initAdminLayout() {
  if (!isAuthenticated()) {
    window.location.href = '/superadmin/login.html';
    return;
  }

  const user = getAdminUser();
  const nameEl = document.querySelector('.header-user-name');
  const tenantEl = document.querySelector('.header-tenant');
  if (nameEl) nameEl.textContent = user?.nome ?? 'Super Admin';
  if (tenantEl) tenantEl.textContent = 'Super Admin';

  document.getElementById('logoutBtn')?.addEventListener('click', () => {
    clearSession();
    window.location.href = '/superadmin/login.html';
  });

  injectSidebarNav();
  setupMobileMenu();
}

function injectSidebarNav() {
  const navEl = document.querySelector('.sidebar-nav');
  if (!navEl) return;

  const current = window.location.pathname;

  let collapsedGroups = {};
  try { collapsedGroups = JSON.parse(localStorage.getItem('saCollapsed') || '{}'); } catch {}

  navEl.innerHTML = NAV_GROUPS.map(group => {
    const isCollapsed = collapsedGroups[group.label] === true;
    const groupId = 'grp-' + group.label.toLowerCase().replace(/\s+/g, '-');

    const itemsHtml = group.items.map(item => {
      const activeClass = item.href === current ? ' class="active"' : '';
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
      </li>`;
  }).join('');

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
      localStorage.setItem('saCollapsed', JSON.stringify(collapsedGroups));
    });
  });
}

let _overlay = null;

function setupMobileMenu() {
  document.getElementById('hamburger')?.addEventListener('click', () => {
    const sidebar = document.querySelector('.sidebar');
    if (!sidebar) return;
    const isOpen = sidebar.classList.contains('open');
    if (!isOpen) {
      if (!_overlay) {
        _overlay = document.createElement('div');
        _overlay.id = 'sidebarOverlay';
        _overlay.className = 'sidebar-overlay';
        _overlay.addEventListener('click', () => {
          sidebar.classList.remove('open');
          _overlay.classList.remove('visible');
        });
        document.body.appendChild(_overlay);
      }
      requestAnimationFrame(() => _overlay.classList.add('visible'));
      sidebar.classList.add('open');
    } else {
      sidebar.classList.remove('open');
      _overlay?.classList.remove('visible');
    }
  });
}
