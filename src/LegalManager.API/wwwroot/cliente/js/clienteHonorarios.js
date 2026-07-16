import { portalApi } from './clienteApi.js';

const brl = (v) => (v ?? 0).toLocaleString('pt-BR', { style: 'currency', currency: 'BRL' });
const fmtDate = (s) => s ? new Date(s).toLocaleDateString('pt-BR') : '—';

export async function listarContratosCliente() {
  return portalApi('/portal/meus-honorarios/contratos');
}

export async function listarParcelasCliente(contratoId) {
  return portalApi(`/portal/meus-honorarios/contratos/${contratoId}/parcelas`);
}

export async function abrirExtratoPdf(contratoId) {
  const userStr = sessionStorage.getItem('cliente_user') ?? localStorage.getItem('cliente_user');
  const token = sessionStorage.getItem('cliente_token');

  if (!token) {
    window.location.href = '/cliente/index.html';
    return;
  }

  let resp;
  try {
    resp = await fetch(`/api/portal/meus-honorarios/contratos/${contratoId}/extrato/pdf`, {
      method: 'POST',
      headers: {
        'Authorization': `Bearer ${token}`,
        'Content-Type': 'application/json',
      },
      body: JSON.stringify({}),
    });
  } catch (e) {
    alert('Erro de rede: ' + e.message);
    return;
  }
  if (!resp.ok) {
    if (resp.status === 401) {
      window.location.href = '/cliente/index.html';
      return;
    }
    const txt = await resp.text();
    alert('Erro ao gerar extrato: ' + txt);
    return;
  }
  const blob = await resp.blob();
  const url = URL.createObjectURL(blob);
  window.open(url, '_blank');
  setTimeout(() => URL.revokeObjectURL(url), 60_000);
}

export { brl, fmtDate };
