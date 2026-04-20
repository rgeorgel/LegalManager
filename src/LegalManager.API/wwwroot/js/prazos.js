import { initLayout } from './layout.js';
import { apiFetch } from './api.js';

initLayout();

const STATUS_LABEL = { Pendente: 'Pendente', Cumprido: 'Cumprido', Perdido: 'Perdido', Suspenso: 'Suspenso' };
let editingId = null;

// ─── Calculadora ──────────────────────────────────────────────
async function calcular() {
  const inicio = document.getElementById('calcInicio').value;
  const dias = document.getElementById('calcDias').value;
  const tipo = document.getElementById('calcTipo').value;
  if (!inicio || !dias) return;

  try {
    const res = await apiFetch('/prazos/calcular', {
      method: 'POST',
      body: JSON.stringify({ dataInicio: inicio, quantidadeDias: parseInt(dias), tipoCalculo: tipo })
    });
    document.getElementById('calcDataFinal').textContent =
      new Date(res.dataFinal).toLocaleDateString('pt-BR', { weekday: 'long', day: 'numeric', month: 'long', year: 'numeric' });
    const ferEl = document.getElementById('calcFeriados');
    ferEl.textContent = res.feriadosNoIntervalo?.length
      ? `Feriados no intervalo: ${res.feriadosNoIntervalo.join(', ')}`
      : 'Nenhum feriado nacional no intervalo.';
    document.getElementById('calcResultado').style.display = '';
  } catch (e) {
    console.error(e);
  }
}

document.getElementById('btnCalcular').addEventListener('click', calcular);

// ─── Lista de prazos ──────────────────────────────────────────
async function load() {
  const dias = document.getElementById('filtroStatus').value || '365';
  try {
    const prazos = await apiFetch(`/prazos?diasAteVencer=${dias}`);
    render(prazos);
  } catch (e) {
    console.error(e);
  }
}

function render(prazos) {
  const list = document.getElementById('listaPrazos');
  const sem  = document.getElementById('semPrazos');

  if (!prazos.length) {
    list.innerHTML = '';
    sem.style.display = '';
    return;
  }
  sem.style.display = 'none';

  list.innerHTML = prazos.map(p => {
    const dr = p.diasRestantes;
    const vencido = dr < 0 || p.status === 'Perdido';
    const urgClass = p.status !== 'Pendente' ? 'prazo-vencido'
                   : vencido                  ? 'prazo-vencido'
                   : dr <= 3                  ? 'prazo-urgente'
                   : dr <= 7                  ? 'prazo-proximo'
                                              : 'prazo-normal';
    const counterColor = vencido ? '#dc2626' : dr <= 3 ? '#f59e0b' : '#10b981';
    const drLabel = dr < 0 ? `${Math.abs(dr)} dias atrás` : dr === 0 ? 'hoje' : `${dr} dia(s)`;
    const badgeClass = { Pendente: 'badge-pendente', Cumprido: 'badge-cumprido', Perdido: 'badge-perdido', Suspenso: 'badge-suspenso' }[p.status] ?? '';

    return `
    <div class="prazo-card ${urgClass}">
      <div class="prazo-counter">
        <div class="prazo-counter-num" style="color:${counterColor}">${dr < 0 ? '!' : dr}</div>
        <div class="prazo-counter-label">${dr < 0 ? 'vencido' : dr === 0 ? 'hoje' : 'dias'}</div>
      </div>
      <div class="prazo-info">
        <div class="prazo-titulo">
          ${esc(p.descricao)}
          <span class="badge ${badgeClass}" style="margin-left:6px">${STATUS_LABEL[p.status] ?? p.status}</span>
        </div>
        <div class="prazo-meta">
          Vence em <strong>${new Date(p.dataFinal).toLocaleDateString('pt-BR')}</strong> &mdash; ${drLabel}
          ${p.numeroCNJ     ? ` &nbsp;·&nbsp; Processo: <strong>${esc(p.numeroCNJ)}</strong>` : ''}
          ${p.nomeResponsavel ? ` &nbsp;·&nbsp; 👤 ${esc(p.nomeResponsavel)}` : ''}
          &nbsp;·&nbsp; ${p.tipoCalculo === 'DiasUteis' ? 'Dias úteis' : 'Dias corridos'}
        </div>
      </div>
      <div class="prazo-actions">
        ${p.status === 'Pendente' ? `<button class="btn btn-secondary btn-sm" data-action="cumprir" data-id="${p.id}">✔ Cumprir</button>` : ''}
        <button class="btn btn-secondary btn-sm" data-action="editar"  data-id="${p.id}">✏️</button>
        <button class="btn btn-danger    btn-sm" data-action="excluir" data-id="${p.id}">🗑</button>
      </div>
    </div>`;
  }).join('');

  document.querySelectorAll('[data-action="cumprir"]').forEach(btn =>
    btn.addEventListener('click', () => marcarStatus(btn.dataset.id, 'Cumprido')));
  document.querySelectorAll('[data-action="excluir"]').forEach(btn =>
    btn.addEventListener('click', () => excluir(btn.dataset.id)));
  document.querySelectorAll('[data-action="editar"]').forEach(btn =>
    btn.addEventListener('click', () => openEditModal(btn.dataset.id, prazos)));
}

async function marcarStatus(id, status) {
  const p = await apiFetch(`/prazos/${id}`);
  await apiFetch(`/prazos/${id}`, { method: 'PUT', body: JSON.stringify({ ...p, status }) });
  load();
}

async function excluir(id) {
  if (!confirm('Excluir este prazo?')) return;
  await apiFetch(`/prazos/${id}`, { method: 'DELETE' });
  load();
}

// ─── Modal ────────────────────────────────────────────────────
function openCreateModal() {
  editingId = null;
  document.getElementById('modalPrazoTitulo').textContent = 'Novo Prazo';
  document.getElementById('formPrazo').reset();
  document.getElementById('pDataFinalCalc').value = '';
  document.getElementById('pMsgErro').style.display = 'none';
  document.getElementById('modalPrazo').style.display = 'flex';
}

function openEditModal(id, prazos) {
  const p = prazos.find(x => x.id === id);
  if (!p) return;
  editingId = id;
  document.getElementById('modalPrazoTitulo').textContent = 'Editar Prazo';
  document.getElementById('pDescricao').value      = p.descricao;
  document.getElementById('pDataInicio').value     = p.dataInicio.substring(0, 10);
  document.getElementById('pQuantidadeDias').value = p.quantidadeDias;
  document.getElementById('pTipoCalculo').value    = p.tipoCalculo;
  document.getElementById('pObservacoes').value    = p.observacoes ?? '';
  document.getElementById('pDataFinalCalc').value  = new Date(p.dataFinal).toLocaleDateString('pt-BR');
  document.getElementById('pMsgErro').style.display = 'none';
  document.getElementById('modalPrazo').style.display = 'flex';
}

function closeModal() { document.getElementById('modalPrazo').style.display = 'none'; }

document.getElementById('btnNovoPrazo').addEventListener('click', openCreateModal);
document.getElementById('modalPrazoClose').addEventListener('click', closeModal);
document.getElementById('btnCancelarPrazo').addEventListener('click', closeModal);
document.getElementById('modalPrazo').addEventListener('click', e => {
  if (e.target === e.currentTarget) closeModal();
});

// Auto-calculate data final on input change
['pDataInicio', 'pQuantidadeDias', 'pTipoCalculo'].forEach(id => {
  document.getElementById(id).addEventListener('change', autoCalc);
});

async function autoCalc() {
  const inicio = document.getElementById('pDataInicio').value;
  const dias   = parseInt(document.getElementById('pQuantidadeDias').value);
  const tipo   = document.getElementById('pTipoCalculo').value;
  if (!inicio || !dias) return;
  try {
    const res = await apiFetch('/prazos/calcular', {
      method: 'POST',
      body: JSON.stringify({ dataInicio: inicio, quantidadeDias: dias, tipoCalculo: tipo })
    });
    document.getElementById('pDataFinalCalc').value = new Date(res.dataFinal).toLocaleDateString('pt-BR');
  } catch {}
}

document.getElementById('formPrazo').addEventListener('submit', async e => {
  e.preventDefault();
  const errEl = document.getElementById('pMsgErro');
  errEl.style.display = 'none';

  const dto = {
    descricao:       document.getElementById('pDescricao').value.trim(),
    dataInicio:      document.getElementById('pDataInicio').value,
    quantidadeDias:  parseInt(document.getElementById('pQuantidadeDias').value),
    tipoCalculo:     document.getElementById('pTipoCalculo').value,
    observacoes:     document.getElementById('pObservacoes').value.trim() || null,
  };

  try {
    if (editingId) {
      const current = await apiFetch(`/prazos/${editingId}`);
      await apiFetch(`/prazos/${editingId}`, { method: 'PUT', body: JSON.stringify({ ...dto, status: current.status }) });
    } else {
      await apiFetch('/prazos', { method: 'POST', body: JSON.stringify(dto) });
    }
    closeModal();
    load();
  } catch (err) {
    errEl.textContent = err.message ?? 'Erro ao salvar prazo.';
    errEl.style.display = '';
  }
});

document.getElementById('btnFiltrar').addEventListener('click', load);

// Init calculadora with today
document.getElementById('calcInicio').value = new Date().toISOString().substring(0, 10);

function esc(s) {
  return (s ?? '').replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');
}

load();
