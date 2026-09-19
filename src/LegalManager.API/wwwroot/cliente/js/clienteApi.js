import { identifyUser, resetIdentity, trackApiCall, trackEvent } from '/js/analytics.js';

const API_BASE = '/api/portal';
const TOKEN_KEY = 'cliente_token';
const USER_KEY = 'cliente_user';
const THEME_KEY = 'cliente_tenant_theme';

export function getToken() {
  return sessionStorage.getItem(TOKEN_KEY);
}

export function setSession(data) {
  sessionStorage.setItem(TOKEN_KEY, data.accessToken);
  sessionStorage.setItem(USER_KEY, JSON.stringify(data.perfil));
  if (data.perfil && data.perfil.tema !== undefined) {
    sessionStorage.setItem(THEME_KEY, JSON.stringify(data.perfil.tema));
  }
  identifyUser(data.perfil?.acessoId, {
    email: data.perfil?.email,
    nome: data.perfil?.nome,
    app_area: 'portal_cliente',
  });
}

export function clearSession() {
  sessionStorage.removeItem(TOKEN_KEY);
  sessionStorage.removeItem(USER_KEY);
  sessionStorage.removeItem(THEME_KEY);
  resetIdentity();
}

export function getUser() {
  const u = sessionStorage.getItem(USER_KEY);
  return u ? JSON.parse(u) : null;
}

export function isLoggedIn() {
  return !!getToken();
}

export async function clienteApiFetch(path, options = {}) {
  const method = (options.method || 'GET').toUpperCase();
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
    const err = new Error(errorMsg);
    trackApiCall('portal_api', method, path, { ok: false, error: err, status: res.status });
    throw err;
  }

  if (res.status === 204) {
    trackApiCall('portal_api', method, path, { ok: true });
    return null;
  }
  const json = await res.json();
  trackApiCall('portal_api', method, path, { ok: true });
  return json;
}

export async function login(email, senha) {
  const data = await fetch(`${API_BASE}/login`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ email, senha })
  });
  if (!data.ok) {
    const body = await data.json().catch(() => ({}));
    trackEvent('portal_login_failed', { status: data.status });
    throw new Error(body.message || body.title || 'E-mail ou senha inválidos.');
  }
  const json = await data.json();
  setSession(json);
  trackEvent('portal_login_succeeded');
  return json;
}

export function logout() {
  trackEvent('portal_logout');
  clearSession();
  window.location.href = '/cliente/';
}

export async function solicitarRedefinicao(email) {
  const res = await fetch(`${API_BASE}/solicitar-redefinicao`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ email }),
  });
  return res;
}

export async function redefinirSenha(token, novaSenha) {
  const res = await fetch(`${API_BASE}/redefinir-senha`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ token, novaSenha }),
  });
  if (!res.ok) {
    const body = await res.json().catch(() => ({}));
    throw new Error(body.message || body.title || 'Erro ao redefinir senha.');
  }
  return res.json();
}

export async function aceitarConvitePortal(token, senha) {
  const res = await fetch(`${API_BASE}/aceitar-convite`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ token, senha }),
  });
  if (!res.ok) {
    const body = await res.json().catch(() => ({}));
    throw new Error(body.message || body.title || 'Convite inválido ou expirado.');
  }
  const json = await res.json();
  setSession(json);
  trackEvent('portal_convite_aceito');
  return json;
}

export const portalApi = {
  getMe: () => clienteApiFetch('/me'),
  getMeusProcessos: () => clienteApiFetch('/meus-processos'),
  getProcesso: (id) => clienteApiFetch(`/meus-processos/${id}`),
  getAndamentos: (id) => clienteApiFetch(`/meus-processos/${id}/andamentos`),
  getResumos: (id) => clienteApiFetch(`/meus-processos/${id}/resumos`),
  getDocumentos: (id) => clienteApiFetch(`/meus-processos/${id}/documentos`),
  uploadDocumento: (processoId, file, tipo = 'Prova', nome = null) => {
    const token = getToken();
    const formData = new FormData();
    formData.append('file', file);
    formData.append('tipo', tipo);
    if (nome) formData.append('nome', nome);
    return fetch(`${API_BASE}/meus-processos/${processoId}/documentos`, {
      method: 'POST',
      headers: { 'Authorization': `Bearer ${token}` },
      body: formData
    }).then(res => {
      trackApiCall('portal_api', 'POST', '/meus-processos/:id/documentos', { ok: res.ok, status: res.status });
      return res;
    });
  }
};
