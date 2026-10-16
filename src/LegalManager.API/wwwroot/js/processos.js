import { apiFetch, apiDownload } from './api.js';

export async function getProcessos(filtro = {}) {
  const p = new URLSearchParams();
  if (filtro.busca) p.set('busca', filtro.busca);
  // status e areaDireito aceitam um valor ou uma lista (processos com qualquer um deles).
  for (const v of [].concat(filtro.status || [])) p.append('status', v);
  for (const v of [].concat(filtro.areaDireito || [])) p.append('areaDireito', v);
  if (filtro.advogadoResponsavelId) p.set('advogadoResponsavelId', filtro.advogadoResponsavelId);
  if (filtro.contatoId) p.set('contatoId', filtro.contatoId);
  if (filtro.favoritos) p.set('favoritos', 'true');
  p.set('page', filtro.page || 1);
  p.set('pageSize', filtro.pageSize || 20);
  return apiFetch(`/processos?${p}`);
}

// Relatório com os mesmos filtros da listagem (sem paginação). formato: 'csv' (Excel) | 'pdf'.
export async function baixarRelatorioProcessos(filtro = {}, formato = 'csv') {
  const p = new URLSearchParams({ formato });
  if (filtro.busca) p.set('busca', filtro.busca);
  for (const v of [].concat(filtro.status || [])) p.append('status', v);
  for (const v of [].concat(filtro.areaDireito || [])) p.append('areaDireito', v);
  if (filtro.advogadoResponsavelId) p.set('advogadoResponsavelId', filtro.advogadoResponsavelId);
  if (filtro.contatoId) p.set('contatoId', filtro.contatoId);
  if (filtro.favoritos) p.set('favoritos', 'true');
  const { blob, nomeArquivo } = await apiDownload(`/processos/relatorio?${p}`);
  const url = URL.createObjectURL(blob);
  const a = document.createElement('a');
  a.href = url;
  a.download = nomeArquivo;
  document.body.appendChild(a);
  a.click();
  a.remove();
  setTimeout(() => URL.revokeObjectURL(url), 60_000);
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

// Favorito é do usuário logado (não do escritório).
export async function setFavorito(id, favorito) {
  return apiFetch(`/processos/${id}/favorito`, { method: 'PUT', body: JSON.stringify({ favorito }) });
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

export async function updateAndamento(processoId, andamentoId, data) {
  return apiFetch(`/processos/${processoId}/andamentos/${andamentoId}`, {
    method: 'PUT',
    body: JSON.stringify(data)
  });
}

export async function deleteAndamento(processoId, andamentoId) {
  return apiFetch(`/processos/${processoId}/andamentos/${andamentoId}`, { method: 'DELETE' });
}
