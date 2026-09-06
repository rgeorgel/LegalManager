import { apiFetch } from './api.js';

const FORMATTERS = {
  brl: new Intl.NumberFormat('pt-BR', { style: 'currency', currency: 'BRL' }),
  date: new Intl.DateTimeFormat('pt-BR'),
  dateTime: new Intl.DateTimeFormat('pt-BR', { dateStyle: 'short', timeStyle: 'short' }),
};

export function brl(v) {
  if (v === null || v === undefined || Number.isNaN(v)) return '—';
  return FORMATTERS.brl.format(v);
}

export function fmtDate(iso) {
  if (!iso) return '—';
  try {
    const d = new Date(iso);
    if (Number.isNaN(d.getTime())) return '—';
    return FORMATTERS.date.format(d);
  } catch { return iso; }
}

export function fmtDateInput(iso) {
  if (!iso) return '';
  try {
    const d = new Date(iso);
    return d.toISOString().slice(0, 10);
  } catch { return ''; }
}

export const STATUS_CONTRATO_LABELS = {
  Ativo: { cls: 'badge-ok', label: '🟢 Ativo' },
  Inadimplente: { cls: 'badge-error', label: '🔴 Em Atraso' },
  Quitado: { cls: 'badge-muted', label: '✔ Quitado' },
  Suspenso: { cls: 'badge-warn', label: '⏸ Suspenso' },
  Distratado: { cls: 'badge-muted', label: '✖ Distratado' },
  Encerrado: { cls: 'badge-muted', label: '◆ Encerrado' },
};

export const FORMA_PAGAMENTO_LABELS = {
  AVista: 'À Vista',
  Parcelado: 'Parcelado',
  EntradaParcelado: 'Entrada + Parcelamento',
};

export const PERIODICIDADE_LABELS = {
  Mensal: 'Mensal',
  Quinzenal: 'Quinzenal',
  Semanal: 'Semanal',
  Semestral: 'Semestral',
};

export const TIPO_COBRANCA_OPCOES = ['Boleto/PIX', 'Cartão de Crédito', 'Dinheiro', 'Cessão'];

export function mapForma(raw) {
  if (typeof raw === 'string') return FORMA_PAGAMENTO_LABELS[raw] ?? raw;
  return Object.keys(FORMA_PAGAMENTO_LABELS)[raw] ?? '—';
}

export function statusContrato(c) {
  const parcelasVencidas = pick(c, 'parcelasVencidas', 0);
  const status = pick(c, 'status', null) ?? pick(c, 'Status', 'Ativo');
  if (parcelasVencidas > 0 && status !== 'Quitado' && status !== 'Distratado' && status !== 'Encerrado')
    return 'Inadimplente';
  return status;
}

export const STATUS_PARCELA_LABELS = {
  Pendente: { cls: 'status-pendente', label: '⏳ Pendente' },
  Pago: { cls: 'status-pago', label: '✅ Pago' },
  Vencido: { cls: 'status-vencido', label: '🔴 Em Atraso' },
  Cancelado: { cls: 'status-cancelado', label: '◆ Cancelado' },
};

export async function getDashboard() {
  const res = await apiFetch('/honorarios/contratos/dashboard');
  if (!res || res.status === 'error') return null;
  return res;
}

export async function listarContratos(params = {}) {
  const qs = new URLSearchParams();
  if (params.status) qs.set('status', params.status);
  if (params.contatoId) qs.set('contatoId', params.contatoId);
  if (params.processoId) qs.set('processoId', params.processoId);
  if (params.busca) qs.set('busca', params.busca);
  qs.set('page', params.page ?? 1);
  qs.set('pageSize', params.pageSize ?? 50);
  return apiFetch(`/honorarios/contratos?${qs.toString()}`);
}

export async function getContrato(id) {
  return apiFetch(`/honorarios/contratos/${id}`);
}

export async function criarContrato(body) {
  return apiFetch('/honorarios/contratos', { method: 'POST', body: JSON.stringify(body) });
}

export async function atualizarContrato(id, body) {
  return apiFetch(`/honorarios/contratos/${id}`, { method: 'PUT', body: JSON.stringify(body) });
}

export async function excluirContrato(id) {
  return apiFetch(`/honorarios/contratos/${id}`, { method: 'DELETE' });
}

export async function listarParcelas(id) {
  return apiFetch(`/honorarios/contratos/${id}/parcelas`);
}

export async function pagarParcela(contratoId, parcelaId, body) {
  return apiFetch(`/honorarios/contratos/${contratoId}/parcelas/${parcelaId}/pagar`, { method: 'POST', body: JSON.stringify(body) });
}

export async function cancelarParcela(contratoId, parcelaId, motivo) {
  return apiFetch(`/honorarios/contratos/${contratoId}/parcelas/${parcelaId}/cancelar`, { method: 'POST', body: JSON.stringify({ motivo }) });
}

export async function estornarParcela(contratoId, parcelaId) {
  return apiFetch(`/honorarios/contratos/${contratoId}/parcelas/${parcelaId}/estornar`, { method: 'POST' });
}

export async function suspenderContrato(id) {
  return apiFetch(`/honorarios/contratos/${id}/suspender`, { method: 'POST' });
}

export async function reativarContrato(id) {
  return apiFetch(`/honorarios/contratos/${id}/reativar`, { method: 'POST' });
}

export async function distratoContrato(id, motivo) {
  return apiFetch(`/honorarios/contratos/${id}/distrato`, { method: 'POST', body: JSON.stringify({ motivo }) });
}

export async function listarHistorico(id) {
  return apiFetch(`/honorarios/contratos/${id}/historico`);
}

export async function getConfiguracao() {
  return apiFetch('/honorarios/contratos/configuracao');
}

export async function salvarConfiguracao(body) {
  return apiFetch('/honorarios/contratos/configuracao', { method: 'PUT', body: JSON.stringify(body) });
}

export async function getContatos() {
  return apiFetch('/contatos?page=1&pageSize=200');
}

export async function getProcessos() {
  return apiFetch('/processos?page=1&pageSize=200');
}

export async function abrirExtratoPdf(id) {
  const token = sessionStorage.getItem('access_token');
  const resp = await fetch(`/api/honorarios/contratos/${id}/extrato/pdf`, {
    method: 'POST',
    headers: {
      'Authorization': `Bearer ${token}`,
      'Content-Type': 'application/json',
    },
    body: JSON.stringify({}),
  });
  if (resp.status === 402) {
    alert('A Gestão de Honorários está disponível a partir do plano Plus.');
    return;
  }
  if (resp.status === 401) {
    sessionStorage.removeItem('access_token');
    sessionStorage.removeItem('refresh_token');
    const back = encodeURIComponent(window.location.pathname + window.location.search);
    window.location.href = `/login.html?redirect=${back}`;
    return;
  }
  if (!resp.ok) {
    const txt = await resp.text();
    alert('Erro ao gerar extrato: ' + txt);
    return;
  }
  const blob = await resp.blob();
  const url = URL.createObjectURL(blob);
  window.open(url, '_blank');
  setTimeout(() => URL.revokeObjectURL(url), 60_000);
}

export function notify(text, kind = 'info') {
  const el = document.createElement('div');
  el.textContent = text;
  el.style.cssText = `
    position: fixed; bottom: 24px; left: 50%; transform: translateX(-50%);
    background: var(--color-text, #1B2A4A); color: #fff;
    padding: 11px 22px; border-radius: 10px; font-size: 14px; font-weight: 600;
    z-index: 1000; box-shadow: 0 6px 20px rgba(0,0,0,.2);
    border-left: 3px solid var(--color-accent, #C9A84C);
    opacity: 0; transition: opacity .25s;
  `;
  document.body.appendChild(el);
  requestAnimationFrame(() => el.style.opacity = '1');
  setTimeout(() => {
    el.style.opacity = '0';
    setTimeout(() => el.remove(), 250);
  }, 2600);
}

export function escapeHtml(s) {
  return (s ?? '').replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;');
}

export function getQueryParam(name) {
  return new URLSearchParams(window.location.search).get(name);
}

/**
 * Lê valor tolerante a camelCase/PascalCase e campos ausentes.
 * Tenta primeiro a chave como informada, depois PascalCase, depois fallback.
 */
export function pick(obj, camelKey, fallback) {
  if (obj == null || typeof obj !== 'object') return fallback;
  if (obj[camelKey] !== undefined && obj[camelKey] !== null) return obj[camelKey];
  const pascal = camelKey.charAt(0).toUpperCase() + camelKey.slice(1);
  if (obj[pascal] !== undefined && obj[pascal] !== null) return obj[pascal];
  return fallback;
}

/**
 * Liga um handler a um evento de forma idempotente.
 * Garante que `fn` será registrado no máximo uma vez, mesmo que `once` seja
 * chamado várias vezes (proteção contra bug de listeners duplicados).
 *
 * @param {EventTarget} el  Elemento alvo (ou window/document).
 * @param {string}      event  Nome do evento (ex: 'click').
 * @param {function}    fn  Handler.
 */
export function once(el, event, fn) {
  if (!el) return;
  // Cada closure tem sua própria flag — evita dupla inscrição por closures diferentes.
  if (!fn.__onceFlags) fn.__onceFlags = {};
  if (fn.__onceFlags[event]) return;
  fn.__onceFlags[event] = true;
  el.addEventListener(event, fn);
}
