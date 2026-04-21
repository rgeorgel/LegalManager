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

async function refreshTokenIfNeeded() {
  const rt = sessionStorage.getItem('refresh_token');
  if (!rt) return false;
  try {
    const res = await fetch(`${API_BASE}/auth/refresh`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ refreshToken: rt })
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

  let res = await fetch(`${API_BASE}${path}`, { ...options, headers });

  if (res.status === 401) {
    const refreshed = await refreshTokenIfNeeded();
    if (refreshed) {
      headers['Authorization'] = `Bearer ${getToken()}`;
      res = await fetch(`${API_BASE}${path}`, { ...options, headers });
    } else {
      clearSession();
      window.location.href = '/index.html';
      return;
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
