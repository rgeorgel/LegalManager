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

/** Relê o perfil (com tema e logo atuais do escritório) e atualiza a sessão. */
export async function atualizarPerfil() {
  const perfil = await clienteApiFetch('/me');
  if (!perfil) return null;
  sessionStorage.setItem(USER_KEY, JSON.stringify(perfil));
  sessionStorage.setItem(THEME_KEY, JSON.stringify(perfil.tema ?? null));
  return perfil;
}

/** Tema do escritório guardado na sessão do portal (não o do painel do escritório). */
export function getTemaPortal() {
  try { return JSON.parse(sessionStorage.getItem(THEME_KEY) || 'null'); } catch { return null; }
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

/** Tela de login guardando a página atual para voltar a ela depois (ex.: link de processo compartilhado). */
export function urlLogin() {
  const atual = location.pathname + location.search;
  return atual.startsWith('/cliente/') && !/^\/cliente\/(index\.html)?$/.test(location.pathname)
    ? `/cliente/index.html?next=${encodeURIComponent(atual)}`
    : '/cliente/index.html';
}

/** Destino seguro do ?next= da tela de login: só páginas do próprio portal. */
export function destinoAposLogin() {
  const next = new URLSearchParams(location.search).get('next');
  return next && next.startsWith('/cliente/') && !next.startsWith('//') ? next : '/cliente/dashboard.html';
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
    window.location.href = urlLogin();
    return;
  }

  if (!res.ok) {
    let errorMsg = `HTTP ${res.status}`;
    try {
      const body = await res.json();
      errorMsg = body.message || body.title || errorMsg;
    } catch {}
    const err = new Error(errorMsg);
    err.status = res.status;
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

/**
 * Processo do link que trouxe o cliente ao login (?next=/cliente/processo-detalhe.html?id=…).
 * Com o mesmo e-mail em mais de um escritório, o login entra no acesso que é parte dele.
 */
function processoDoDestino() {
  const destino = new URL(destinoAposLogin(), location.origin);
  if (destino.pathname !== '/cliente/processo-detalhe.html') return null;
  const id = destino.searchParams.get('id');
  return id && /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(id) ? id : null;
}

export async function login(email, senha) {
  const data = await fetch(`${API_BASE}/login`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ email, senha, processoId: processoDoDestino() })
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
  if (!res.ok) {
    const body = await res.json().catch(() => ({}));
    throw new Error(res.status === 429
      ? 'Muitas tentativas. Aguarde um minuto e tente de novo.'
      : body.message || body.title || 'Não foi possível enviar o link. Tente novamente.');
  }
  return res.json();
}

export async function redefinirSenha(token, novaSenha) {
  const res = await fetch(`${API_BASE}/redefinir-senha`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ token, novaSenha }),
  });
  if (!res.ok) {
    const body = await res.json().catch(() => ({}));
    throw new Error(res.status === 429
      ? 'Muitas tentativas. Aguarde um minuto e tente de novo.'
      : body.message || body.title || 'Erro ao redefinir senha.');
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
  // Conteúdo do documento como Blob (PDF/imagem com o tipo real; demais como binário).
  getDocumentoArquivo: async (processoId, documentoId) => {
    const path = `/meus-processos/${processoId}/documentos/${documentoId}/arquivo`;
    const res = await fetch(`${API_BASE}${path}`, {
      headers: { 'Authorization': `Bearer ${getToken()}` }
    });
    trackApiCall('portal_api', 'GET', '/meus-processos/:id/documentos/:docId/arquivo', { ok: res.ok, status: res.status });
    if (res.status === 401) {
      clearSession();
      window.location.href = urlLogin();
      return null;
    }
    if (!res.ok) throw new Error(res.status === 404 ? 'Documento não encontrado.' : `Erro ao abrir o documento (HTTP ${res.status}).`);
    return res.blob();
  },
  // Envio de documentos: /cliente/js/portal-upload.js (fila com progresso via XMLHttpRequest).
};
