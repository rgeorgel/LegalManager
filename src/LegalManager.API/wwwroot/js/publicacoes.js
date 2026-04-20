import { initLayout } from './layout.js';
import { apiFetch } from './api.js';

initLayout();

const TIPO_LABEL = {
  Prazo: 'Prazo', Audiencia: 'Audiência', Decisao: 'Decisão',
  Despacho: 'Despacho', Intimacao: 'Intimação', Outro: 'Outro'
};

const STATUS_LABEL = { Nova: 'Nova', Lida: 'Lida', Arquivada: 'Arquivada' };

let pubs = [];

async function load() {
  const status = document.getElementById('filtroStatus').value;
  const tipo = document.getElementById('filtroTipo').value;
  const de = document.getElementById('filtroDe').value;
  const ate = document.getElementById('filtroAte').value;

  const params = new URLSearchParams({ pageSize: 50 });
  if (status) params.set('status', status);
  if (tipo) params.set('tipo', tipo);
  if (de) params.set('de', de);
  if (ate) params.set('ate', ate);

  try {
    pubs = await apiFetch(`/publicacoes?${params}`);
    render();
    await loadCount();
  } catch (e) {
    console.error(e);
  }
}

async function loadCount() {
  try {
    const res = await apiFetch('/publicacoes/nao-lidas/count');
    const el = document.getElementById('countNaoLidas');
    if (res.count > 0) {
      el.textContent = `${res.count} não lida(s)`;
      el.style.display = '';
    } else {
      el.style.display = 'none';
    }
  } catch {}
}

function render() {
  const list = document.getElementById('listaPubs');
  const sem = document.getElementById('semPubs');

  if (!pubs.length) {
    list.innerHTML = '';
    sem.style.display = '';
    return;
  }
  sem.style.display = 'none';
  list.innerHTML = pubs.map(p => {
    const statusClass = { Nova: 'pub-badge-nova', Lida: 'pub-badge-lida', Arquivada: 'pub-badge-arquivada' }[p.status];
    return `
    <div class="pub-card" data-id="${p.id}">
      <div class="pub-card-header">
        <div class="pub-card-tipo">
          <span class="pub-tipo">${TIPO_LABEL[p.tipo] ?? p.tipo}</span>
          <span class="pub-badge ${statusClass}">${STATUS_LABEL[p.status]}</span>
          ${p.numeroCNJ ? `<span style="font-size:12px;color:var(--color-text-muted)">${esc(p.numeroCNJ)}</span>` : ''}
        </div>
        <span class="pub-card-meta">${new Date(p.dataPublicacao).toLocaleDateString('pt-BR')} — ${esc(p.diario)}</span>
      </div>
      <div class="pub-card-conteudo" id="conteudo-${p.id}">${esc(p.conteudo)}</div>
      <div class="pub-card-actions">
        ${p.conteudo.length > 300 ? `<button class="btn btn-secondary btn-sm" data-action="expandir" data-id="${p.id}">Ver mais</button>` : ''}
        ${p.status === 'Nova' ? `<button class="btn btn-primary btn-sm" data-action="lida" data-id="${p.id}">Marcar como lida</button>` : ''}
        ${p.status !== 'Arquivada' ? `<button class="btn btn-secondary btn-sm" data-action="arquivar" data-id="${p.id}">Arquivar</button>` : ''}
      </div>
    </div>`;
  }).join('');

  document.querySelectorAll('[data-action="lida"]').forEach(btn =>
    btn.addEventListener('click', async () => {
      await apiFetch(`/publicacoes/${btn.dataset.id}/lida`, { method: 'PATCH' });
      load();
    }));

  document.querySelectorAll('[data-action="arquivar"]').forEach(btn =>
    btn.addEventListener('click', async () => {
      await apiFetch(`/publicacoes/${btn.dataset.id}/arquivar`, { method: 'PATCH' });
      load();
    }));

  document.querySelectorAll('[data-action="expandir"]').forEach(btn =>
    btn.addEventListener('click', () => {
      const el = document.getElementById(`conteudo-${btn.dataset.id}`);
      el.classList.toggle('expanded');
      btn.textContent = el.classList.contains('expanded') ? 'Ver menos' : 'Ver mais';
    }));
}

document.getElementById('btnFiltrar').addEventListener('click', load);
document.getElementById('btnMarcarTodasLidas').addEventListener('click', async () => {
  const novas = pubs.filter(p => p.status === 'Nova');
  await Promise.all(novas.map(p => apiFetch(`/publicacoes/${p.id}/lida`, { method: 'PATCH' })));
  load();
});

function esc(str) {
  return (str ?? '').replace(/&/g,'&amp;').replace(/</g,'&lt;').replace(/>/g,'&gt;');
}

load();
