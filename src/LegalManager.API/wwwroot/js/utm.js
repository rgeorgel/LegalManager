const STORAGE_KEY = 'signup_attribution';

function readUrlParams() {
  try { return new URLSearchParams(location.search); }
  catch { return new URLSearchParams(); }
}

function clean(value) {
  if (value === null || value === undefined) return null;
  const trimmed = String(value).trim();
  return trimmed.length === 0 ? null : trimmed.slice(0, 500);
}

function readStored() {
  try {
    const raw = sessionStorage.getItem(STORAGE_KEY);
    return raw ? JSON.parse(raw) : null;
  } catch { return null; }
}

function writeStored(payload) {
  try { sessionStorage.setItem(STORAGE_KEY, JSON.stringify(payload)); }
  catch { /* sessionStorage indisponível — segue sem persistir */ }
}

export function captureAttribution() {
  const params = readUrlParams();
  const hasAnyUtm = ['utm_source', 'utm_medium', 'utm_campaign', 'utm_content', 'utm_term', 'fbclid', 'gclid', 'igclid']
    .some(k => params.has(k));

  const stored = readStored();
  const base = hasAnyUtm ? {} : (stored || {});

  const payload = {
    utmSource:   clean(params.get('utm_source'))   ?? base.utmSource   ?? null,
    utmMedium:   clean(params.get('utm_medium'))   ?? base.utmMedium   ?? null,
    utmCampaign: clean(params.get('utm_campaign')) ?? base.utmCampaign ?? null,
    utmContent:  clean(params.get('utm_content'))  ?? base.utmContent  ?? null,
    utmTerm:     clean(params.get('utm_term'))     ?? base.utmTerm     ?? null,
    fbclid:      clean(params.get('fbclid'))       ?? base.fbclid      ?? null,
    gclid:       clean(params.get('gclid'))        ?? base.gclid       ?? null,
    referrer:    clean(document.referrer)         ?? base.referrer    ?? null,
    landingPage: location.pathname || null
  };

  if (hasAnyUtm) writeStored(payload);
  return payload;
}

export function getAttributionPayload() {
  return captureAttribution();
}

export function clearAttribution() {
  try { sessionStorage.removeItem(STORAGE_KEY); }
  catch { /* ignore */ }
}
