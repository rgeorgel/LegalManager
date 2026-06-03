import { initLayout } from './layout.js';
import { apiFetch } from './api.js';

initLayout();

let oabs = [];
let usuarios = [];

async function load() {
  try {
    [oabs, usuarios] = await Promise.all([
      apiFetch('/tenant-oabs'),
      carregarUsuarios()
    ]);
    render();
  } catch (e) {
    if (e.status === 402) {
      document.getElementById('oabsList').innerHTML =
        `<div class="empty-state">${esc(e.message || 'Plano não permite cadastro de OABs. Faça upgrade para o plano Pro.')}</div>`;
      document.getElementById('btnNovaOab').style.display = 'none';
      document.getElementById('btnSincronizarTodas').style.display = 'none';
    } else {
      console.error(e);
    }
  }
}

async function carregarUsuarios() {
  try {
    // Tenta o endpoint de usuários do tenant; pode não existir em todos os planos
    return await apiFetch('/usuarios') || [];
  } catch {
    return [];
  }
}

function render() {
  const el = document.getElementById('oabsList');
  if (!oabs.length) {
    el.innerHTML = `<div class="empty-state">
      Nenhuma OAB cadastrada. Clique em <strong>+ Adicionar OAB</strong> para começar.
    </div>`;
    return;
  }
  el.innerHTML = `
    <table class="oab-table">
      <thead>
        <tr>
          <th>Nome</th>
          <th>UF</th>
          <th>Número</th>
          <th>Status</th>
          <th>Escavador</th>
          <th style="text-align:right">Ações</th>
        </tr>
      </thead>
      <tbody>
        ${oabs.map(o => `
          <tr class="${o.ativo ? '' : 'oab-row-inativa'}">
            <td>
              <div>${esc(o.nome)}</div>
              ${o.userNome ? `<div style="font-size:12px;color:var(--color-text-muted)">→ ${esc(o.userNome)}</div>` : ''}
            </td>
            <td>${esc(o.uf)}</td>
            <td class="oab-numero">${esc(o.numero)}</td>
            <td>
              <span class="oab-status ${o.ativo ? 'ativo' : 'inativo'}">
                <span class="dot"></span>${o.ativo ? 'Ativa' : 'Inativa'}
              </span>
            </td>
            <td>
              ${renderSyncBadge(o)}
            </td>
            <td>
              <div class="oab-actions">
                <button class="btn btn-secondary btn-sm" data-action="sincronizar" data-id="${o.id}" title="Recriar monitoramento no Escavador">⟳</button>
                <button class="btn btn-secondary btn-sm" data-action="editar" data-id="${o.id}">Editar</button>
                <button class="btn btn-secondary btn-sm" data-action="remover" data-id="${o.id}">Remover</button>
              </div>
            </td>
          </tr>`).join('')}
      </tbody>
    </table>`;

  document.querySelectorAll('[data-action="sincronizar"]').forEach(btn =>
    btn.addEventListener('click', () => sincronizarOab(btn.dataset.id)));
  document.querySelectorAll('[data-action="editar"]').forEach(btn =>
    btn.addEventListener('click', () => abrirModal(oabs.find(o => o.id === btn.dataset.id))));
  document.querySelectorAll('[data-action="remover"]').forEach(btn =>
    btn.addEventListener('click', async () => {
      if (!confirm('Remover esta OAB? O monitoramento remoto no Escavador também será excluído.')) return;
      try {
        await apiFetch(`/tenant-oabs/${btn.dataset.id}`, { method: 'DELETE' });
        load();
      } catch (e) {
        alert(e.message || 'Erro ao remover.');
      }
    }));
}

function renderSyncBadge(o) {
  if (o.syncError) {
    return `
      <span class="sync-badge error" title="${esc(o.syncError)}">
        <span class="dot"></span>Erro de sync
      </span>
      <div class="sync-error-msg" title="${esc(o.syncError)}">${esc(o.syncError)}</div>`;
  }
  if (o.sincronizada) {
    const when = o.ultimoSyncEm ? new Date(o.ultimoSyncEm).toLocaleString('pt-BR') : '—';
    return `
      <span class="sync-badge synced">
        <span class="dot"></span>Sincronizada
      </span>
      <div style="font-size:11px;color:var(--color-text-muted);margin-top:2px">ID ${o.escavadorMonitoramentoId} · ${when}</div>`;
  }
  return `
    <span class="sync-badge pending">
      <span class="dot"></span>Pendente
    </span>
    <div style="font-size:11px;color:var(--color-text-muted);margin-top:2px">Aguardando sync</div>`;
}

function showError(msg) {
  const el = document.getElementById('formError');
  el.textContent = msg;
  el.classList.add('visible');
}
function clearError() {
  const el = document.getElementById('formError');
  el.textContent = '';
  el.classList.remove('visible');
}

function popularUsuariosSelect(selectedId) {
  const sel = document.getElementById('oabUserId');
  sel.innerHTML = `<option value="">— Sem vínculo —</option>` +
    usuarios.map(u => `<option value="${u.id}" ${u.id === selectedId ? 'selected' : ''}>${esc(u.nome || u.email)}</option>`).join('');
}

function abrirModal(oab = null) {
  clearError();
  document.getElementById('modalTitulo').textContent = oab ? 'Editar OAB' : 'Nova OAB';
  document.getElementById('oabId').value = oab?.id ?? '';
  document.getElementById('oabNome').value = oab?.nome ?? '';
  document.getElementById('oabUf').value = oab?.uf ?? '';
  document.getElementById('oabNumero').value = oab?.numero ?? '';
  document.getElementById('oabAtivo').checked = oab?.ativo ?? true;
  popularUsuariosSelect(oab?.userId);
  const backdrop = document.getElementById('modalBackdrop');
  backdrop.style.display = 'flex';
  backdrop.setAttribute('aria-hidden', 'false');
  setTimeout(() => document.getElementById('oabNome').focus(), 50);
}

function fecharModal() {
  const backdrop = document.getElementById('modalBackdrop');
  backdrop.style.display = 'none';
  backdrop.setAttribute('aria-hidden', 'true');
  clearError();
}

document.getElementById('btnNovaOab').addEventListener('click', () => abrirModal());
document.getElementById('btnCancelar').addEventListener('click', fecharModal);
document.getElementById('btnFechar').addEventListener('click', fecharModal);
document.getElementById('modalBackdrop').addEventListener('click', (e) => {
  if (e.target.id === 'modalBackdrop') fecharModal();
});
document.addEventListener('keydown', (e) => {
  if (e.key === 'Escape' && document.getElementById('modalBackdrop').style.display === 'flex') {
    fecharModal();
  }
});

document.getElementById('btnSincronizarTodas').addEventListener('click', async () => {
  const btn = document.getElementById('btnSincronizarTodas');
  btn.disabled = true;
  btn.innerHTML = '<span class="spinner"></span> Sincronizando...';
  try {
    const res = await apiFetch('/tenant-oabs/sincronizar-todas', { method: 'POST' });
    alert(`${res.sincronizadas} OAB(s) sincronizada(s) com o Escavador.`);
    load();
  } catch (e) {
    alert(e.message || 'Erro ao sincronizar.');
  } finally {
    btn.disabled = false;
    btn.innerHTML = '⟳ Sincronizar todas';
  }
});

async function sincronizarOab(id) {
  const btn = document.querySelector(`[data-action="sincronizar"][data-id="${id}"]`);
  if (btn) { btn.disabled = true; btn.innerHTML = '<span class="spinner"></span>'; }
  try {
    await apiFetch(`/tenant-oabs/${id}/sincronizar`, { method: 'POST' });
    load();
  } catch (e) {
    alert(e.message || 'Erro ao sincronizar.');
  } finally {
    if (btn) { btn.disabled = false; btn.innerHTML = '⟳'; }
  }
}

document.getElementById('formOab').addEventListener('submit', async (e) => {
  e.preventDefault();
  clearError();

  const id = document.getElementById('oabId').value;
  const userId = document.getElementById('oabUserId').value;
  const body = {
    uf: document.getElementById('oabUf').value,
    numero: document.getElementById('oabNumero').value,
    nome: document.getElementById('oabNome').value,
    ativo: document.getElementById('oabAtivo').checked
  };
  if (userId) body.userId = userId;

  if (!body.uf || !body.numero || !body.nome.trim()) {
    showError('Preencha todos os campos obrigatórios.');
    return;
  }

  const btnSalvar = document.getElementById('btnSalvar');
  btnSalvar.disabled = true;
  btnSalvar.innerHTML = '<span class="spinner"></span> Salvando…';

  try {
    if (id) {
      await apiFetch(`/tenant-oabs/${id}`, { method: 'PUT', body });
    } else {
      await apiFetch('/tenant-oabs', { method: 'POST', body });
    }
    fecharModal();
    load();
  } catch (err) {
    showError(err.message || 'Erro ao salvar.');
  } finally {
    btnSalvar.disabled = false;
    btnSalvar.textContent = 'Salvar OAB';
  }
});

function esc(str) {
  return (str ?? '').replace(/&/g,'&amp;').replace(/</g,'&lt;').replace(/>/g,'&gt;');
}

load();
