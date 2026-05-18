import { apiFetch, setSession, clearSession, isLoggedIn, getUser } from './api.js';

export async function login(email, senha) {
  // Usa fetch direto (não apiFetch) para evitar que 401 cause redirect
  // antes do catch block do formulário exibir o erro
  const res = await fetch('/api/auth/login', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ email, senha }),
  });
  if (!res.ok) {
    const body = await res.json().catch(() => ({}));
    throw new Error(body.message || body.title || 'E-mail ou senha inválidos.');
  }
  const data = await res.json();
  setSession(data);
  return data;
}

export async function register(payload) {
  const data = await apiFetch('/auth/register', {
    method: 'POST',
    body: JSON.stringify(payload)
  });
  setSession(data);
  return data;
}

export async function logout() {
  const rt = sessionStorage.getItem('refresh_token');
  try {
    await apiFetch('/auth/logout', {
      method: 'POST',
      body: JSON.stringify({ refreshToken: rt })
    });
  } catch {}
  clearSession();
  window.location.href = '/login.html';
}

export async function forgotPassword(email) {
  return apiFetch('/auth/forgot-password', {
    method: 'POST',
    body: JSON.stringify({ email })
  });
}

export async function resetPassword(payload) {
  return apiFetch('/auth/reset-password', {
    method: 'POST',
    body: JSON.stringify(payload)
  });
}

export async function aceitarConvite(payload) {
  const data = await apiFetch('/auth/aceitar-convite', {
    method: 'POST',
    body: JSON.stringify(payload)
  });
  setSession(data);
  return data;
}

export { isLoggedIn, getUser };
