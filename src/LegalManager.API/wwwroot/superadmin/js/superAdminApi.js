const API_BASE = '/api';
const SA_ACCESS_TOKEN = 'sa_access_token';
const SA_REFRESH_TOKEN = 'sa_refresh_token';
const SA_USER = 'sa_user';

export function getToken() {
  return sessionStorage.getItem(SA_ACCESS_TOKEN);
}

export function setSession(data) {
  sessionStorage.setItem(SA_ACCESS_TOKEN, data.accessToken);
  sessionStorage.setItem(SA_REFRESH_TOKEN, data.refreshToken);
  sessionStorage.setItem(SA_USER, JSON.stringify(data.usuario));
}

export function clearSession() {
  sessionStorage.removeItem(SA_ACCESS_TOKEN);
  sessionStorage.removeItem(SA_REFRESH_TOKEN);
  sessionStorage.removeItem(SA_USER);
}

export function getAdminUser() {
  const u = sessionStorage.getItem(SA_USER);
  return u ? JSON.parse(u) : null;
}

export function isAuthenticated() {
  const token = getToken();
  const user = getAdminUser();
  return !!token && user?.perfil === 'SuperAdmin';
}

async function refreshTokenIfNeeded() {
  const rt = sessionStorage.getItem(SA_REFRESH_TOKEN);
  if (!rt) return false;
  try {
    const res = await fetch(`${API_BASE}/auth/refresh`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ refreshToken: rt })
    });
    if (res.ok) {
      const data = await res.json();
      if (data.usuario?.perfil === 'SuperAdmin') {
        setSession(data);
        return true;
      }
    }
  } catch {}
  return false;
}

export async function saFetch(path, options = {}) {
  const token = getToken();
  const headers = {
    'Content-Type': 'application/json',
    ...(token ? { 'Authorization': `Bearer ${token}` } : {}),
    ...(options.headers || {})
  };

  let res = await fetch(`${API_BASE}${path}`, { ...options, headers });

  if (res.status === 401) {
    const refreshed = await refreshTokenIfNeeded();
    if (refreshed) {
      headers['Authorization'] = `Bearer ${getToken()}`;
      res = await fetch(`${API_BASE}${path}`, { ...options, headers });
    } else {
      clearSession();
      window.location.href = '/superadmin/login.html';
      throw new Error('Unauthorized');
    }
  }

  if (!res.ok) {
    let errorMsg = `HTTP ${res.status}`;
    const contentType = res.headers.get('content-type') || '';
    if (contentType.includes('application/json')) {
      try {
        const body = await res.json();
        errorMsg = body.message || body.title || errorMsg;
      } catch {}
    } else {
      const text = await res.text();
      errorMsg = text.substring(0, 200);
    }
    throw new Error(errorMsg);
  }

  if (res.status === 204) return null;
  return res.json();
}
