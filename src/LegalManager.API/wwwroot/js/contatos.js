import { apiFetch } from './api.js';

export async function getContatos(filtro = {}) {
  const params = new URLSearchParams();
  if (filtro.busca) params.set('busca', filtro.busca);
  if (filtro.tipoContato) params.set('tipoContato', filtro.tipoContato);
  if (filtro.tipo) params.set('tipo', filtro.tipo);
  if (filtro.tag) params.set('tag', filtro.tag);
  if (filtro.ativo !== undefined) params.set('ativo', filtro.ativo);
  params.set('page', filtro.page || 1);
  params.set('pageSize', filtro.pageSize || 20);
  return apiFetch(`/contatos?${params}`);
}

export async function getContato(id) {
  return apiFetch(`/contatos/${id}`);
}

export async function createContato(data) {
  return apiFetch('/contatos', { method: 'POST', body: JSON.stringify(data) });
}

export async function updateContato(id, data) {
  return apiFetch(`/contatos/${id}`, { method: 'PUT', body: JSON.stringify(data) });
}

export async function deleteContato(id) {
  return apiFetch(`/contatos/${id}`, { method: 'DELETE' });
}

export async function getAtendimentos(contatoId) {
  return apiFetch(`/contatos/${contatoId}/atendimentos`);
}

export async function addAtendimento(contatoId, data) {
  return apiFetch(`/contatos/${contatoId}/atendimentos`, {
    method: 'POST',
    body: JSON.stringify(data)
  });
}

export async function getPortalAcesso(contatoId) {
  try {
    return await apiFetch(`/contatos/${contatoId}/portal-acesso`);
  } catch (err) {
    if (err.message.startsWith('HTTP 404')) return null;
    throw err;
  }
}

export async function criarPortalAcesso(contatoId, data) {
  return apiFetch(`/contatos/${contatoId}/portal-acesso`, {
    method: 'POST',
    body: JSON.stringify(data)
  });
}

export async function revogarPortalAcesso(contatoId) {
  return apiFetch(`/contatos/${contatoId}/portal-acesso`, { method: 'DELETE' });
}
