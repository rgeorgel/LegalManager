import { initLayout } from './layout.js';
import { apiFetch } from './api.js';

initLayout();

const PRIORIDADE_LABEL = { Baixa: 'Baixa', Media: 'Média', Alta: 'Alta', Urgente: 'Urgente' };
const STATUS_LABEL = { Pendente: 'Pendente', EmAndamento: 'Em Andamento', Concluida: 'Concluída', Cancelada: 'Cancelada' };

let currentPage = 1;
let currentView = 'lista';
let editingId = null;
let allItems = [];

// --- State ---
function getFilters() {
  return {
    busca: document.getElementById('filtBusca').value.trim(),
    status: document.getElementById('filtStatus').value,
    prioridade: document.getElementById('filtPrioridade').value,
    atrasada: document.getElementById('filtAtrasada').checked ? 'true' : '',
  };
}

// --- Load ---
async function loadTarefas(page = 1) {
  currentPage = page;
  const f = getFilters();
  const params = new URLSearchParams({ page, pageSize: 50 });
  if (f.busca) params.set('busca', f.busca);
  if (f.status) params.set('status', f.status);
  if (f.prioridade) params.set('prioridade', f.prioridade);
  if (f.atrasada) params.set('atrasada', 'true');

  try {
    const data = await apiFetch(`/api/tarefas?${params}`);
    allItems = data.items ?? [];
    if (currentView === 'lista') renderLista(allItems, data);
    else renderKanban(allItems);
  } catch (e) {
    console.error(e);
  }
}

// --- Render Lista ---
function renderLista(items, data) {
  const container = document.getElementById('tarefasList');
  if (!items.length) {
    container.innerHTML = '<p style="color:var(--color-text-muted);padding:16px">Nenhuma tarefa encontrada.</p>';
    document.getElementById('pagination').innerHTML = '';
    return;
  }
  container.innerHTML = items.map(t => tarefaCard(t)).join('');
  renderPagination(data);
  bindCardActions();
}

function tarefaCard(t) {
  const prioClass = `prioridade-${t.prioridade.toLowerCase()}`;
  const statusClass = `status-${t.status.toLowerCase()}`;
  const prazoStr = t.prazo ? new Date(t.prazo).toLocaleString('pt-BR') : '–';
  const atrasadaBadge = t.atrasada ? '<span class="badge-atrasada">Atrasada</span>' : '';
  const tags = t.tags?.map(tag => `<span class="tag">${tag}</span>`).join('') ?? '';

  return `
  <div class="tarefa-card" data-id="${t.id}">
    <div class="tarefa-card-header">
      <div>
        <span class="tarefa-card-titulo">${esc(t.titulo)}</span>
        ${atrasadaBadge}
      </div>
      <div style="display:flex;gap:6px;flex-wrap:wrap">
        <span class="badge ${prioClass}">${PRIORIDADE_LABEL[t.prioridade]}</span>
        <span class="badge ${statusClass}">${STATUS_LABEL[t.status]}</span>
      </div>
    </div>
    <div class="tarefa-card-meta">
      <span>📅 Prazo: ${prazoStr}</span>
      ${t.nomeResponsavel ? `<span>👤 ${esc(t.nomeResponsavel)}</span>` : ''}
      ${t.numeroCNJProcesso ? `<span>⚖️ ${esc(t.numeroCNJProcesso)}</span>` : ''}
    </div>
    ${tags ? `<div class="tags-list">${tags}</div>` : ''}
    <div class="tarefa-card-actions">
      ${t.status !== 'Concluida' && t.status !== 'Cancelada' ?
        `<button class="btn btn-secondary btn-sm" data-action="concluir" data-id="${t.id}">✓ Concluir</button>` : ''}
      <button class="btn btn-secondary btn-sm" data-action="edit" data-id="${t.id}">Editar</button>
      <button class="btn btn-danger btn-sm" data-action="delete" data-id="${t.id}">Excluir</button>
    </div>
  </div>`;
}

function renderPagination(data) {
  const pag = document.getElementById('pagination');
  if (!data || data.totalPages <= 1) { pag.innerHTML = ''; return; }
  const pages = [];
  for (let i = 1; i <= data.totalPages; i++) {
    pages.push(`<button class="pagination-btn${i === currentPage ? ' active' : ''}" data-page="${i}">${i}</button>`);
  }
  pag.innerHTML = pages.join('');
  pag.querySelectorAll('[data-page]').forEach(btn =>
    btn.addEventListener('click', () => loadTarefas(+btn.dataset.page)));
}

// --- Render Kanban ---
function renderKanban(items) {
  ['Pendente', 'EmAndamento', 'Concluida', 'Cancelada'].forEach(s => {
    const col = document.getElementById(`kanban-${s}`);
    const filtered = items.filter(t => t.status === s);
    col.innerHTML = filtered.length
      ? filtered.map(t => kanbanCard(t)).join('')
      : '<span style="color:var(--color-text-muted);font-size:13px">Vazio</span>';
  });
  bindCardActions();
}

function kanbanCard(t) {
  const prioClass = `prioridade-${t.prioridade.toLowerCase()}`;
  const prazoStr = t.prazo ? new Date(t.prazo).toLocaleDateString('pt-BR') : null;
  const atrasadaBadge = t.atrasada ? '<span class="badge-atrasada">Atrasada</span>' : '';
  return `
  <div class="tarefa-card" data-id="${t.id}" style="padding:10px">
    <div style="display:flex;justify-content:space-between;gap:6px">
      <span style="font-weight:600;font-size:13px">${esc(t.titulo)}</span>
      <span class="badge ${prioClass}" style="font-size:11px">${PRIORIDADE_LABEL[t.prioridade]}</span>
    </div>
    ${prazoStr ? `<div style="font-size:12px;color:var(--color-text-muted);margin-top:4px">📅 ${prazoStr} ${atrasadaBadge}</div>` : ''}
    <div style="display:flex;gap:6px;margin-top:8px;flex-wrap:wrap">
      ${t.status !== 'Concluida' && t.status !== 'Cancelada' ?
        `<button class="btn btn-secondary btn-sm" data-action="concluir" data-id="${t.id}" style="font-size:11px;padding:2px 8px">✓</button>` : ''}
      <button class="btn btn-secondary btn-sm" data-action="edit" data-id="${t.id}" style="font-size:11px;padding:2px 8px">✏️</button>
      <button class="btn btn-danger btn-sm" data-action="delete" data-id="${t.id}" style="font-size:11px;padding:2px 8px">🗑</button>
    </div>
  </div>`;
}

function bindCardActions() {
  document.querySelectorAll('[data-action]').forEach(btn => {
    btn.addEventListener('click', async e => {
      const id = e.currentTarget.dataset.id;
      const action = e.currentTarget.dataset.action;
      if (action === 'edit') await openEditModal(id);
      else if (action === 'concluir') await concluir(id);
      else if (action === 'delete') await deleteTarefa(id);
    });
  });
}

// --- CRUD ---
async function concluir(id) {
  if (!confirm('Marcar tarefa como concluída?')) return;
  await apiFetch(`/api/tarefas/${id}/concluir`, { method: 'POST' });
  await loadTarefas(currentPage);
}

async function deleteTarefa(id) {
  if (!confirm('Excluir esta tarefa?')) return;
  await apiFetch(`/api/tarefas/${id}`, { method: 'DELETE' });
  await loadTarefas(currentPage);
}

// --- Modal ---
function openCreateModal() {
  editingId = null;
  document.getElementById('modalTarefaTitulo').textContent = 'Nova Tarefa';
  document.getElementById('formTarefa').reset();
  document.getElementById('fStatusGroup').style.display = 'none';
  document.getElementById('fmsgErro').style.display = 'none';
  document.getElementById('modalTarefa').style.display = 'flex';
}

async function openEditModal(id) {
  editingId = id;
  document.getElementById('modalTarefaTitulo').textContent = 'Editar Tarefa';
  document.getElementById('fmsgErro').style.display = 'none';
  document.getElementById('fStatusGroup').style.display = '';

  const t = await apiFetch(`/api/tarefas/${id}`);
  document.getElementById('fTitulo').value = t.titulo;
  document.getElementById('fDescricao').value = t.descricao ?? '';
  document.getElementById('fPrioridade').value = t.prioridade;
  document.getElementById('fStatus').value = t.status;
  document.getElementById('fPrazo').value = t.prazo ? t.prazo.substring(0, 16) : '';
  document.getElementById('fTags').value = (t.tags ?? []).join(', ');
  document.getElementById('modalTarefa').style.display = 'flex';
}

function closeModal() {
  document.getElementById('modalTarefa').style.display = 'none';
}

document.getElementById('btnNova').addEventListener('click', openCreateModal);
document.getElementById('modalTarefaClose').addEventListener('click', closeModal);
document.getElementById('btnCancelarTarefa').addEventListener('click', closeModal);
document.getElementById('modalTarefa').addEventListener('click', e => {
  if (e.target === e.currentTarget) closeModal();
});

document.getElementById('formTarefa').addEventListener('submit', async e => {
  e.preventDefault();
  const errEl = document.getElementById('fmsgErro');
  errEl.style.display = 'none';

  const titulo = document.getElementById('fTitulo').value.trim();
  if (!titulo) { showErr(errEl, 'Título é obrigatório.'); return; }

  const tagsRaw = document.getElementById('fTags').value.trim();
  const tags = tagsRaw ? tagsRaw.split(',').map(s => s.trim()).filter(Boolean) : [];
  const prazoVal = document.getElementById('fPrazo').value;

  try {
    if (editingId) {
      const dto = {
        titulo,
        descricao: document.getElementById('fDescricao').value.trim() || null,
        prioridade: document.getElementById('fPrioridade').value,
        status: document.getElementById('fStatus').value,
        prazo: prazoVal || null,
        tags
      };
      await apiFetch(`/api/tarefas/${editingId}`, { method: 'PUT', body: JSON.stringify(dto) });
    } else {
      const dto = {
        titulo,
        descricao: document.getElementById('fDescricao').value.trim() || null,
        prioridade: document.getElementById('fPrioridade').value,
        prazo: prazoVal || null,
        tags
      };
      await apiFetch('/api/tarefas', { method: 'POST', body: JSON.stringify(dto) });
    }
    closeModal();
    await loadTarefas(currentPage);
  } catch (err) {
    showErr(errEl, err.message ?? 'Erro ao salvar tarefa.');
  }
});

// --- Views ---
document.getElementById('viewLista').addEventListener('click', () => {
  currentView = 'lista';
  document.getElementById('viewLista').classList.add('active');
  document.getElementById('viewKanban').classList.remove('active');
  document.getElementById('viewListaContainer').style.display = '';
  document.getElementById('viewKanbanContainer').style.display = 'none';
  renderLista(allItems, null);
});

document.getElementById('viewKanban').addEventListener('click', async () => {
  currentView = 'kanban';
  document.getElementById('viewKanban').classList.add('active');
  document.getElementById('viewLista').classList.remove('active');
  document.getElementById('viewListaContainer').style.display = 'none';
  document.getElementById('viewKanbanContainer').style.display = '';
  if (!allItems.length) await loadTarefas(1);
  else renderKanban(allItems);
});

// --- Filters ---
let debounceTimer;
document.getElementById('filtBusca').addEventListener('input', () => {
  clearTimeout(debounceTimer);
  debounceTimer = setTimeout(() => loadTarefas(1), 400);
});
['filtStatus', 'filtPrioridade', 'filtAtrasada'].forEach(id =>
  document.getElementById(id).addEventListener('change', () => loadTarefas(1)));

// --- Helpers ---
function esc(str) {
  return (str ?? '').replace(/&/g,'&amp;').replace(/</g,'&lt;').replace(/>/g,'&gt;').replace(/"/g,'&quot;');
}
function showErr(el, msg) { el.textContent = msg; el.style.display = ''; }

// --- Init ---
loadTarefas();
