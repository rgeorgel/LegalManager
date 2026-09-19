// Wrapper fino sobre window.posthog — mantém o resto do app livre da API do PostHog
// e faz da falta do SDK (bloqueador de anúncios, localhost, script.onerror) um no-op
// silencioso em vez de um erro que quebra o fluxo de negócio.

function ph() {
  return window.posthog || null;
}

// Normaliza IDs (uuid, numéricos) num path para evitar explosão de cardinalidade
// de eventos no PostHog: "/processos/8f0..-...--/andamentos" vira "/processos/:id/andamentos".
export function normalizePath(path) {
  return String(path)
    .replace(/\/[0-9a-fA-F]{8}-[0-9a-fA-F-]{27}(?=\/|$)/g, '/:id')
    .replace(/\/\d+(?=\/|$)/g, '/:id');
}

export function trackEvent(name, props = {}) {
  ph()?.capture(name, props);
}

export function trackError(error, context = {}) {
  const p = ph();
  if (!p) return;
  const err = error instanceof Error ? error : new Error(String(error?.message || error));
  if (typeof p.captureException === 'function') {
    p.captureException(err, context);
  } else {
    p.capture('$exception', { message: err.message, ...context });
  }
}

// `id` varia de formato entre os 3 perfis do app (usuario.id no admin/superadmin,
// perfil.acessoId no portal do cliente) — por isso é resolvido pelo chamador.
export function identifyUser(id, properties = {}) {
  if (!id) return;
  ph()?.identify(String(id), properties);
}

export function resetIdentity() {
  ph()?.reset();
}

const MUTATING_METHODS = new Set(['POST', 'PUT', 'PATCH', 'DELETE']);

// Instrumenta um fetch wrapper de API (apiFetch, clienteApiFetch, saFetch): dado o
// método/path da chamada, decide se emite um evento de engajamento (mutações bem-
// sucedidas) e sempre reporta falhas como exceção — assim cada módulo do app ganha
// visibilidade automática sem precisar de captura manual em cada tela.
export function trackApiCall(prefix, method, path, { ok, error, status } = {}) {
  const normalized = normalizePath(path);
  if (!ok) {
    trackError(error || new Error(`HTTP ${status}`), { path: normalized, method, status, api: prefix });
    return;
  }
  if (MUTATING_METHODS.has(method)) {
    trackEvent(`${prefix}_${method.toLowerCase()}`, { path: normalized });
  }
}
