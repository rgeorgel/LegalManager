const API_BASE = '/api/portal';
const TOKEN_KEY = 'cliente_token';
const USER_KEY = 'cliente_user';

export function getToken() {
  return sessionStorage.getItem(TOKEN_KEY);
}

export function setSession(data) {
  sessionStorage.setItem(TOKEN_KEY, data.accessToken);
  sessionStorage.setItem(USER_KEY, JSON.stringify(data.perfil));
}

export function clearSession() {
  sessionStorage.removeItem(TOKEN_KEY);
  sessionStorage.removeItem(USER_KEY);
}

export function getUser() {
  const u = sessionStorage.getItem(USER_KEY);
  return u ? JSON.parse(u) : null;
}

export function isLoggedIn() {
  return !!getToken();
}

export async function clienteApiFetch(path, options = {}) {
  const token = getToken();
  const headers = {
    'Content-Type': 'application/json',
    ...(token ? { 'Authorization': `Bearer ${token}` } : {}),
    ...(options.headers || {})
  };

  const res = await fetch(`${API_BASE}${path}`, { ...options, headers });

  if (res.status === 401) {
    clearSession();
    window.location.href = '/cliente/index.html';
    return;
  }

  if (!res.ok) {
    let errorMsg = `HTTP ${res.status}`;
    try {
      const body = await res.json();
      errorMsg = body.message || body.title || errorMsg;
    } catch {}
    throw new Error(errorMsg);
  }

  if (res.status === 204) return null;
  return res.json();
}

export async function login(email, senha) {
  const data = await fetch(`${API_BASE}/login`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ email, senha })
  });
  if (!data.ok) {
    const body = await data.json().catch(() => ({}));
    throw new Error(body.message || body.title || 'E-mail ou senha inválidos.');
  }
  const json = await data.json();
  setSession(json);
  return json;
}

export function logout() {
  clearSession();
  window.location.href = '/cliente/';
}

export const portalApi = {
  getMe: () => clienteApiFetch('/me'),
  getMeusProcessos: () => clienteApiFetch('/meus-processos'),
  getProcesso: (id) => clienteApiFetch(`/meus-processos/${id}`),
  getAndamentos: (id) => clienteApiFetch(`/meus-processos/${id}/andamentos`),
};
