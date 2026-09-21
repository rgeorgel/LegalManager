import { apiFetch } from './api.js';

export async function getContatos(filtro = {}) {
  const params = new URLSearchParams();
  if (filtro.busca) params.set('busca', filtro.busca);
  if (filtro.tipoContato) params.set('tipoContato', filtro.tipoContato);
  if (filtro.tipo) params.set('tipo', filtro.tipo);
  if (filtro.tag) params.set('tag', filtro.tag);
  if (filtro.ativo !== undefined) params.set('ativo', filtro.ativo);
  if (filtro.sortBy) params.set('sortBy', filtro.sortBy);
  if (filtro.sortDir) params.set('sortDir', filtro.sortDir);
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
  const token = sessionStorage.getItem('access_token');
  const res = await fetch(`/api/contatos/${contatoId}/portal-acesso`, {
    headers: token ? { Authorization: `Bearer ${token}` } : {}
  });
  if (res.status === 404) return null;
  if (!res.ok) throw new Error(`HTTP ${res.status}`);
  return res.json();
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

// ── Perfil (resumo 360° + timeline) ─────────────────────────────────────

export async function getContatoPerfil(contatoId) {
  return apiFetch(`/contatos/${contatoId}/perfil`);
}

// ── Vínculos entre contatos ──────────────────────────────────────────────

export async function getVinculos(contatoId) {
  return apiFetch(`/contatos/${contatoId}/vinculos`);
}

export async function addVinculo(contatoId, data) {
  return apiFetch(`/contatos/${contatoId}/vinculos`, { method: 'POST', body: JSON.stringify(data) });
}

export async function removeVinculo(contatoId, vinculoId) {
  return apiFetch(`/contatos/${contatoId}/vinculos/${vinculoId}`, { method: 'DELETE' });
}

// ── Duplicados ────────────────────────────────────────────────────────────

export async function getDuplicados() {
  return apiFetch('/contatos/duplicados');
}

// ── Aniversariantes ───────────────────────────────────────────────────────

export async function getAniversariantes(mes) {
  const params = new URLSearchParams();
  if (mes) params.set('mes', mes);
  return apiFetch(`/contatos/aniversariantes?${params}`);
}

// ── Filtros salvos ────────────────────────────────────────────────────────

export async function getFiltrosSalvos() {
  return apiFetch('/contatos/filtros-salvos');
}

export async function addFiltroSalvo(data) {
  return apiFetch('/contatos/filtros-salvos', { method: 'POST', body: JSON.stringify(data) });
}

export async function removeFiltroSalvo(filtroId) {
  return apiFetch(`/contatos/filtros-salvos/${filtroId}`, { method: 'DELETE' });
}

// ── Lembretes / follow-up (reaproveita o módulo de Tarefas) ──────────────

export async function agendarRetorno(contatoId, { titulo, prazo }) {
  // "prazo" vem de um <input type="date"> (só "YYYY-MM-DD", sem hora). Sem horário,
  // o valor é interpretado como meia-noite UTC e o fuso do Brasil (UTC-3) faz a data
  // "voltar" um dia quando exibida. Fixando 12:00 evita o problema em qualquer fuso.
  const prazoComHora = prazo && /^\d{4}-\d{2}-\d{2}$/.test(prazo) ? `${prazo}T12:00:00` : prazo;
  return apiFetch('/tarefas', {
    method: 'POST',
    body: JSON.stringify({
      titulo,
      contatoId,
      prazo: prazoComHora,
      prioridade: 'Media',
      tipo: 'Tarefa'
    })
  });
}
