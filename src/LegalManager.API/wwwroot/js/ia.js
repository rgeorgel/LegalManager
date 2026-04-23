import { apiFetch } from './api.js';

export async function traduzirAndamento(dto) {
  return apiFetch('/ia/traduzir-andamento', {
    method: 'POST',
    body: JSON.stringify(dto)
  });
}

export async function obterTraducao(andamentoId) {
  return apiFetch(`/ia/traduzir-andamento/${andamentoId}`);
}

export async function gerarPeca(dto) {
  return apiFetch('/ia/gerar-peca', {
    method: 'POST',
    body: JSON.stringify(dto)
  });
}

export async function listarPecasGeradas(filtro = {}) {
  const p = new URLSearchParams();
  p.set('page', filtro.page || 1);
  p.set('pageSize', filtro.pageSize || 20);
  if (filtro.processoId) p.set('processoId', filtro.processoId);
  if (filtro.tipo) p.set('tipo', filtro.tipo);
  return apiFetch(`/ia/pecas-geradas?${p}`);
}

export async function obterPeca(id) {
  return apiFetch(`/ia/pecas-geradas/${id}`);
}

export async function getCreditos() {
  return apiFetch('/creditos');
}