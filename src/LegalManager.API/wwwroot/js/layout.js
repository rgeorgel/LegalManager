import { isLoggedIn, logout, getUser } from './auth.js';

export function initLayout() {
  if (!isLoggedIn()) {
    window.location.href = '/index.html';
    return;
  }

  const user = getUser();

  document.querySelector('.header-user-name').textContent = user?.nome ?? '';
  document.querySelector('.header-tenant').textContent = user?.nomeEscritorio ?? '';

  document.getElementById('logoutBtn')?.addEventListener('click', () => logout());

  const hamburger = document.getElementById('hamburger');
  const sidebar = document.querySelector('.sidebar');

  hamburger?.addEventListener('click', () => {
    sidebar?.classList.toggle('open');
  });

  // Close sidebar on nav link click (mobile)
  document.querySelectorAll('.sidebar-nav a').forEach(link => {
    link.addEventListener('click', () => sidebar?.classList.remove('open'));
  });

  // Mark active nav
  const current = window.location.pathname;
  document.querySelectorAll('.sidebar-nav a').forEach(link => {
    if (link.getAttribute('href') === current) link.classList.add('active');
  });
}
