const API_BASE = '/api';

function getToken() {
  return sessionStorage.getItem('access_token');
}

export function setSession(data) {
  sessionStorage.setItem('access_token', data.accessToken);
  sessionStorage.setItem('refresh_token', data.refreshToken);
  sessionStorage.setItem('user', JSON.stringify(data.usuario));
}

export function clearSession() {
  sessionStorage.removeItem('access_token');
  sessionStorage.removeItem('refresh_token');
  sessionStorage.removeItem('user');
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

async function refreshTokenIfNeeded() {
  const rt = sessionStorage.getItem('refresh_token');
  if (!rt) return false;
  try {
    const res = await fetch(`${API_BASE}/auth/refresh`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: { refreshToken: rt }
    });
    if (res.ok) {
      const data = await res.json();
      setSession(data);
      return true;
    }
  } catch {}
  return false;
}

export async function apiFetch(path, options = {}) {
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
      window.location.href = '/login.html';
      throw new Error('Unauthorized');
    }
  }

  if (!res.ok) {
    let errorMsg = `HTTP ${res.status}`;
    const contentType = res.headers.get('content-type') || '';
    if (contentType.includes('application/json')) {
      try {
        const body = await res.json();
        errorMsg = body.message || body.title
          || (body.errors ? Object.values(body.errors).flat().join('; ') : errorMsg);
      } catch {}
    } else {
      const text = await res.text();
      errorMsg = text.substring(0, 200);
    }
    throw new Error(errorMsg);
  }

  if (res.status === 204) return null;

  const contentType = res.headers.get('content-type') || '';
  if (!contentType.includes('application/json')) {
    const text = await res.text();
    throw new Error(
      `Resposta não-JSON do endpoint ${path} (Content-Type: ${contentType || 'vazio'}). ` +
      `Provavelmente o endpoint não existe e o servidor retornou a página padrão. ` +
      `Conteúdo recebido: ${text.substring(0, 120)}`
    );
  }

  try {
    return await res.json();
  } catch (e) {
    const text = await res.text();
    throw new Error(
      `Falha ao parsear JSON do endpoint ${path}: ${e.message}. ` +
      `Conteúdo recebido: ${text.substring(0, 120)}`
    );
  }
}
