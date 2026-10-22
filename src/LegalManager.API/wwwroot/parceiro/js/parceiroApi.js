import { identifyUser, resetIdentity, trackApiCall, trackEvent } from '/js/analytics.js';

// Portal do parceiro (ver ParceiroPortalController). Sessão própria, separada da do escritório
// e da do portal do cliente.
const API_BASE = '/api/parceiro';
const TOKEN_KEY = 'parceiro_token';
const USER_KEY = 'parceiro_user';

export function getToken() {
  return sessionStorage.getItem(TOKEN_KEY);
}

export function setSession(data) {
  sessionStorage.setItem(TOKEN_KEY, data.accessToken);
  sessionStorage.setItem(USER_KEY, JSON.stringify(data.parceiro));
  identifyUser(data.parceiro?.id, {
    email: data.parceiro?.email,
    nome: data.parceiro?.nome,
    app_area: 'portal_parceiro',
  });
}

export function clearSession() {
  sessionStorage.removeItem(TOKEN_KEY);
  sessionStorage.removeItem(USER_KEY);
  resetIdentity();
}

export function getParceiro() {
  try { return JSON.parse(sessionStorage.getItem(USER_KEY) || 'null'); }
  catch { return null; }
}

export function isLoggedIn() {
  return !!getToken();
}

async function erroDe(res, padrao) {
  const body = await res.json().catch(() => ({}));
  if (res.status === 429) return new Error('Muitas tentativas. Aguarde um minuto e tente novamente.');
  return new Error(body.message || body.title || padrao);
}

export async function parceiroFetch(path, options = {}) {
  const method = (options.method || 'GET').toUpperCase();
  const token = getToken();
  const res = await fetch(`${API_BASE}${path}`, {
    ...options,
    headers: {
      'Content-Type': 'application/json',
      ...(token ? { 'Authorization': `Bearer ${token}` } : {}),
      ...(options.headers || {})
    }
  });

  if (res.status === 401) {
    clearSession();
    window.location.href = '/parceiro/index.html?msg=sessao';
    throw new Error('Sessão expirada.');
  }

  if (!res.ok) {
    const err = await erroDe(res, `HTTP ${res.status}`);
    trackApiCall('parceiro_api', method, path, { ok: false, error: err, status: res.status });
    throw err;
  }

  trackApiCall('parceiro_api', method, path, { ok: true });
  return res.status === 204 ? null : res.json();
}

async function postPublico(path, body, padrao) {
  const res = await fetch(`${API_BASE}${path}`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(body)
  });
  if (!res.ok) throw await erroDe(res, padrao);
  return res.json();
}

export async function login(email, senha) {
  try {
    const data = await postPublico('/login', { email, senha }, 'E-mail ou senha inválidos.');
    setSession(data);
    trackEvent('parceiro_login_succeeded');
    return data;
  } catch (err) {
    trackEvent('parceiro_login_failed');
    throw err;
  }
}

export async function definirSenha(token, senha) {
  const data = await postPublico('/definir-senha', { token, senha }, 'Link inválido ou expirado.');
  setSession(data);
  trackEvent('parceiro_senha_definida');
  return data;
}

export function esqueciSenha(email) {
  return postPublico('/esqueci-senha', { email }, 'Não foi possível enviar o link agora. Tente novamente.');
}

export function logout() {
  trackEvent('parceiro_logout');
  clearSession();
  window.location.href = '/parceiro/index.html';
}

export const parceiroApi = {
  me: () => parceiroFetch('/me'),
  escritorios: () => parceiroFetch('/escritorios'),
  comissoes: () => parceiroFetch('/comissoes'),
  solicitacoes: () => parceiroFetch('/solicitacoes'),
  solicitarPagamento: (chavePix, observacao) =>
    parceiroFetch('/solicitacoes', { method: 'POST', body: JSON.stringify({ chavePix, observacao }) }),
};
