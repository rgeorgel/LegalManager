import { apiFetch } from './api.js';

export async function getProcessos(filtro = {}) {
  const p = new URLSearchParams();
  if (filtro.busca) p.set('busca', filtro.busca);
  if (filtro.status) p.set('status', filtro.status);
  if (filtro.areaDireito) p.set('areaDireito', filtro.areaDireito);
  if (filtro.advogadoResponsavelId) p.set('advogadoResponsavelId', filtro.advogadoResponsavelId);
  if (filtro.contatoId) p.set('contatoId', filtro.contatoId);
  p.set('page', filtro.page || 1);
  p.set('pageSize', filtro.pageSize || 20);
  return apiFetch(`/processos?${p}`);
}

export async function getProcesso(id) {
  return apiFetch(`/processos/${id}`);
}

export async function createProcesso(data) {
  return apiFetch('/processos', { method: 'POST', body: JSON.stringify(data) });
}

export async function updateProcesso(id, data) {
  return apiFetch(`/processos/${id}`, { method: 'PUT', body: JSON.stringify(data) });
}

export async function encerrarProcesso(id, data) {
  return apiFetch(`/processos/${id}/encerrar`, { method: 'POST', body: JSON.stringify(data) });
}

export async function deleteProcesso(id) {
  return apiFetch(`/processos/${id}`, { method: 'DELETE' });
}

export async function getAndamentos(processoId) {
  return apiFetch(`/processos/${processoId}/andamentos`);
}

export async function addAndamento(processoId, data) {
  return apiFetch(`/processos/${processoId}/andamentos`, {
    method: 'POST',
    body: JSON.stringify(data)
  });
}

export async function deleteAndamento(processoId, andamentoId) {
  return apiFetch(`/processos/${processoId}/andamentos/${andamentoId}`, { method: 'DELETE' });
}
