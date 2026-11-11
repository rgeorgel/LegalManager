import { identifyUser, resetIdentity, trackApiCall } from './analytics.js';

const API_BASE = '/api';

function getToken() {
  return sessionStorage.getItem('access_token');
}

// "Lembrar de mim": a sessão de trabalho continua no sessionStorage (é dali que todas as
// páginas leem), e uma cópia fica no localStorage para sobreviver ao fechar o navegador e
// valer em outras abas. Expira junto com o refresh token (12h do login, ver
// AuthService.DuracaoLembrar) — depois disso a cópia é descartada e o login pede a senha.
const SESSAO_LEMBRADA = 'sessao_lembrada';

function lerSessaoLembrada() {
  try {
    const s = JSON.parse(localStorage.getItem(SESSAO_LEMBRADA) || 'null');
    if (s?.refreshToken && Date.parse(s.expiraEm) > Date.now()) return s;
    if (s) localStorage.removeItem(SESSAO_LEMBRADA);
  } catch {}
  return null;
}

function gravarSessaoLembrada(data) {
  try {
    localStorage.setItem(SESSAO_LEMBRADA, JSON.stringify({
      accessToken: data.accessToken,
      refreshToken: data.refreshToken,
      usuario: data.usuario,
      expiraEm: data.expiresAt,
    }));
  } catch {}
}

function esquecerSessaoLembrada() {
  try { localStorage.removeItem(SESSAO_LEMBRADA); } catch {}
}

// Aba nova (ou navegador reaberto) sem sessão própria: retoma a sessão lembrada. Roda na
// avaliação do módulo, antes de qualquer isLoggedIn() da página. O access token pode já ter
// vencido — o 1º 401 renova com o refresh token, como em qualquer aba.
function restaurarSessaoLembrada() {
  try {
    if (sessionStorage.getItem('access_token')) return;
    const s = lerSessaoLembrada();
    if (!s) return;
    sessionStorage.setItem('access_token', s.accessToken);
    sessionStorage.setItem('refresh_token', s.refreshToken);
    sessionStorage.setItem('user', JSON.stringify(s.usuario));
    if (s.usuario?.tema !== undefined) sessionStorage.setItem('tenant_theme', JSON.stringify(s.usuario.tema));
  } catch {}
}

/** Refresh token mais recente: o da sessão lembrada, se houver (outra aba pode tê-lo renovado). */
export function getRefreshToken() {
  return lerSessaoLembrada()?.refreshToken || sessionStorage.getItem('refresh_token');
}

/**
 * Grava a sessão devolvida pelo login/refresh. `lembrar`: true no login com "Lembrar de mim"
 * marcado, false no login sem ele; omitido (refresh, impersonação...) mantém o que já estava.
 */
export function setSession(data, { lembrar } = {}) {
  if (lembrar === true) gravarSessaoLembrada(data);
  else if (lembrar === false) esquecerSessaoLembrada();
  // Renovação da própria sessão lembrada (o vencimento vem igual do servidor). Outro usuário
  // (convite, cadastro) não herda a cópia — e ela é descartada.
  else if (lerSessaoLembrada()) {
    if (lerSessaoLembrada().usuario?.id === data.usuario?.id) gravarSessaoLembrada(data);
    else esquecerSessaoLembrada();
  }

  sessionStorage.setItem('access_token', data.accessToken);
  sessionStorage.setItem('refresh_token', data.refreshToken);
  sessionStorage.setItem('user', JSON.stringify(data.usuario));
  if (data.usuario && data.usuario.tema !== undefined) {
    sessionStorage.setItem('tenant_theme', JSON.stringify(data.usuario.tema));
  } else if (data.perfil && data.perfil.tema !== undefined) {
    sessionStorage.setItem('tenant_theme', JSON.stringify(data.perfil.tema));
  }
  identifyUser(data.usuario?.id, {
    email: data.usuario?.email,
    nome: data.usuario?.nome,
    perfil: data.usuario?.perfil,
    tenantId: data.usuario?.tenantId,
    app_area: 'admin',
  });
}

export function clearSession() {
  sessionStorage.removeItem('access_token');
  sessionStorage.removeItem('refresh_token');
  sessionStorage.removeItem('user');
  sessionStorage.removeItem('tenant_theme');
  esquecerSessaoLembrada();
  resetIdentity();
}

export function getUser() {
  const u = sessionStorage.getItem('user');
  return u ? JSON.parse(u) : null;
}

export function isLoggedIn() {
  return !!getToken();
}

export function consumeImpersonationHandoff() {
  const raw = localStorage.getItem('impersonation_handoff');
  if (!raw) return;
  localStorage.removeItem('impersonation_handoff');

  try {
    const data = JSON.parse(raw);
    if (Date.now() - data.ts > 30000) return;
    setSession(data);
  } catch {}
}

// Executado na avaliação do módulo (não dentro de uma função) para garantir que a sessão
// já esteja em sessionStorage antes de qualquer verificação isLoggedIn() feita pela página
// que importa este módulo — inclusive checagens inline que rodam antes de initLayout().
consumeImpersonationHandoff();
restaurarSessaoLembrada();

export function getImpersonationInfo() {
  const token = getToken();
  if (!token) return null;
  try {
    const b64 = token.split('.')[1].replace(/-/g, '+').replace(/_/g, '/');
    const payload = JSON.parse(decodeURIComponent(escape(atob(b64))));
    return payload.impersonadoPorId ? { adminNome: payload.impersonadoPorNome || 'SuperAdmin' } : null;
  } catch {
    return null;
  }
}

export async function refreshTokenIfNeeded() {
  const rt = getRefreshToken();
  if (!rt) return false;
  try {
    const res = await fetch(`${API_BASE}/auth/refresh`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      // fetch() não serializa objetos automaticamente — sem o JSON.stringify
      // o servidor recebia "[object Object]" como body, refresh falhava
      // silenciosamente, clearSession era chamado e o usuário perdia
      // sessionStorage (incluindo tenant_theme) sem entender por quê.
      body: JSON.stringify({ refreshToken: rt })
    });
    if (res.ok) {
      const data = await res.json();
      setSession(data);
      return true;
    }
    // Sessão lembrada: outra aba pode ter renovado (e revogado) o mesmo refresh token ao mesmo
    // tempo — se ele mudou no localStorage, tenta de novo com o novo.
    const novo = lerSessaoLembrada()?.refreshToken;
    if (novo && novo !== rt) return refreshTokenIfNeeded();
  } catch {}
  return false;
}

/**
 * Baixa um arquivo da API (CSV, PDF...) com o token da sessão, renovando-o se preciso.
 * `options.method`/`options.body` para gerar o arquivo por POST (o body é serializado em JSON).
 * Retorna { blob, nomeArquivo } — o nome vem do Content-Disposition do servidor.
 * Em erro, lança Error com a mensagem do backend e a propriedade `status` (ex.: 402 = plano).
 */
export async function apiDownload(path, options = {}) {
  const method = (options.method || 'GET').toUpperCase();
  const body = options.body == null || typeof options.body === 'string' ? options.body : JSON.stringify(options.body);
  const doFetch = () => fetch(`${API_BASE}${path}`, {
    method,
    body,
    headers: {
      ...(body != null ? { 'Content-Type': 'application/json' } : {}),
      ...(getToken() ? { 'Authorization': `Bearer ${getToken()}` } : {}),
    },
  });

  let res = await doFetch();
  if (res.status === 401) {
    if (await refreshTokenIfNeeded()) {
      res = await doFetch();
    } else {
      clearSession();
      const back = encodeURIComponent(window.location.pathname + window.location.search);
      window.location.href = `/login.html?redirect=${back}`;
      throw new Error('Unauthorized');
    }
  }

  if (!res.ok) {
    let msg = '';
    try { msg = (await res.json()).message || ''; } catch {}
    const err = new Error(msg || `HTTP ${res.status}`);
    err.status = res.status;
    trackApiCall('api', method, path, { ok: false, error: err, status: res.status });
    throw err;
  }

  const disposition = res.headers.get('content-disposition') || '';
  const match = /filename\*=UTF-8''([^;]+)/i.exec(disposition) || /filename="?([^";]+)"?/i.exec(disposition);
  const nomeArquivo = match ? decodeURIComponent(match[1]) : 'download';
  trackApiCall('api', method, path, { ok: true });
  return { blob: await res.blob(), nomeArquivo };
}

/**
 * Traduz o `errors` de um ValidationProblem do ASP.NET ({ "Descricao": ["The Descricao field is required."] })
 * para uma frase em português. Mensagens já escritas pelo backend em [ErrorMessage] passam como estão.
 */
function mensagemDeValidacao(errors) {
  let entradas = Object.entries(errors);
  // JSON inválido gera também "The dto field is required." (nome do parâmetro da action) — só ruído.
  if (entradas.length > 1) entradas = entradas.filter(([chave]) => !/^[a-z]\w*$/.test(chave));
  const mensagens = entradas.flatMap(([chave, lista]) => {
    const campo = chave.replace(/^\$\./, '').split('.').pop();
    return (Array.isArray(lista) ? lista : [lista]).map(m => {
      if (/^The \S+ field is required\.$/.test(m)) return `O campo ${campo} é obrigatório.`;
      if (/could not be converted|is not valid/i.test(m)) return `Valor inválido no campo ${campo}.`;
      return m;
    });
  });
  return [...new Set(mensagens)].join(' ');
}

export async function apiFetch(path, options = {}) {
  const method = (options.method || 'GET').toUpperCase();
  const token = getToken();
  const headers = {
    'Content-Type': 'application/json',
    ...(token ? { 'Authorization': `Bearer ${token}` } : {}),
    ...(options.headers || {})
  };

  // fetch() doesn't auto-serialize objects — without this, the server receives "[object Object]".
  // Stringify any non-string body (objects, arrays) before passing to fetch.
  let body = options.body;
  if (body != null && typeof body === 'object' && !(body instanceof FormData) && !(body instanceof Blob)) {
    body = JSON.stringify(body);
  }

  let res = await fetch(`${API_BASE}${path}`, { ...options, headers, body });

  if (res.status === 401) {
    const refreshed = await refreshTokenIfNeeded();
    if (refreshed) {
      headers['Authorization'] = `Bearer ${getToken()}`;
      res = await fetch(`${API_BASE}${path}`, { ...options, headers, body });
    } else {
      clearSession();
      const back = encodeURIComponent(window.location.pathname + window.location.search);
      window.location.href = `/login.html?redirect=${back}`;
      throw new Error('Unauthorized');
    }
  }

  if (!res.ok) {
    let errorMsg = '';
    let errorBody = null;
    const contentType = res.headers.get('content-type') || '';
    // Erros de validação do ASP.NET vêm como application/problem+json.
    if (/[/+]json\b/.test(contentType)) {
      try {
        const body = errorBody = await res.json();
        errorMsg = body.message
          || (body.errors ? mensagemDeValidacao(body.errors) : '')
          || body.title;
      } catch {}
    } else {
      const text = (await res.text() || '').trim();
      if (text) errorMsg = text.substring(0, 200);
    }
    // [Authorize(Roles = ...)] retorna 403/401 com corpo vazio, deixando
    // o usuário sem entender por que a ação falhou. Adiciona dica contextual.
    if (!errorMsg) {
      const hint = res.status === 401
        ? 'Sessão expirada. Faça login novamente.'
        : res.status === 403
          ? 'Seu usuário não tem permissão para esta ação.'
          : res.status === 404
            ? 'Recurso não encontrado.'
            : `HTTP ${res.status}`;
      errorMsg = hint;
    }
    // status e corpo JSON ficam no erro para a tela tratar casos específicos (ex.: 409 com o id do registro existente).
    const err = new Error(errorMsg);
    err.status = res.status;
    err.data = errorBody;
    trackApiCall('api', method, path, { ok: false, error: err, status: res.status });
    throw err;
  }

  if (res.status === 204) {
    trackApiCall('api', method, path, { ok: true });
    return null;
  }

  const contentType = res.headers.get('content-type') || '';
  if (!contentType.includes('application/json')) {
    const text = await res.text();
    // `return Ok();` responde 200 com corpo vazio e sem Content-Type: sucesso sem payload, como o 204.
    if (!text.trim()) {
      trackApiCall('api', method, path, { ok: true });
      return null;
    }
    const err = new Error(
      `Resposta não-JSON do endpoint ${path} (Content-Type: ${contentType || 'vazio'}). ` +
      `Provavelmente o endpoint não existe e o servidor retornou a página padrão. ` +
      `Conteúdo recebido: ${text.substring(0, 120)}`
    );
    trackApiCall('api', method, path, { ok: false, error: err, status: res.status });
    throw err;
  }

  try {
    const json = await res.json();
    trackApiCall('api', method, path, { ok: true });
    return json;
  } catch (e) {
    const text = await res.text();
    const err = new Error(
      `Falha ao parsear JSON do endpoint ${path}: ${e.message}. ` +
      `Conteúdo recebido: ${text.substring(0, 120)}`
    );
    trackApiCall('api', method, path, { ok: false, error: err, status: res.status });
    throw err;
  }
}
