import { initLayout } from './layout.js';
import { apiFetch } from './api.js';

initLayout();

const TIPO_LABEL = {
  Audiencia: 'Audiência', Reuniao: 'Reunião', Pericia: 'Perícia',
  Prazo: 'Prazo', Despacho: 'Despacho', Outro: 'Outro', Tarefa: 'Tarefa'
};

let currentView = 'semana';
let currentDate = new Date();
currentDate.setHours(0, 0, 0, 0);

let agendaItems = [];
let editingId = null;

// --- Period helpers ---
function startOf(view, date) {
  const d = new Date(date);
  if (view === 'dia') { d.setHours(0, 0, 0, 0); return d; }
  if (view === 'semana') {
    const day = d.getDay();
    d.setDate(d.getDate() - day);
    d.setHours(0, 0, 0, 0);
    return d;
  }
  if (view === 'mes' || view === 'lista') {
    d.setDate(1); d.setHours(0, 0, 0, 0); return d;
  }
  return d;
}

function endOf(view, start) {
  const d = new Date(start);
  if (view === 'dia') { d.setHours(23, 59, 59, 999); return d; }
  if (view === 'semana') { d.setDate(d.getDate() + 6); d.setHours(23, 59, 59, 999); return d; }
  if (view === 'mes' || view === 'lista') {
    d.setMonth(d.getMonth() + 1);
    d.setDate(0);
    d.setHours(23, 59, 59, 999);
    return d;
  }
  return d;
}

function formatPeriod(view, start, end) {
  const opts = { month: 'long', year: 'numeric' };
  if (view === 'dia') return start.toLocaleDateString('pt-BR', { weekday: 'long', day: 'numeric', month: 'long', year: 'numeric' });
  if (view === 'semana') {
    return `${start.toLocaleDateString('pt-BR', { day: 'numeric', month: 'short' })} – ${end.toLocaleDateString('pt-BR', { day: 'numeric', month: 'short', year: 'numeric' })}`;
  }
  return start.toLocaleDateString('pt-BR', opts);
}

function advance(view, date, dir) {
  const d = new Date(date);
  if (view === 'dia') d.setDate(d.getDate() + dir);
  else if (view === 'semana') d.setDate(d.getDate() + dir * 7);
  else d.setMonth(d.getMonth() + dir);
  return d;
}

// --- Helpers ---
function toLocalISO(date) {
  const p = n => String(n).padStart(2, '0');
  return `${date.getFullYear()}-${p(date.getMonth()+1)}-${p(date.getDate())}T${p(date.getHours())}:${p(date.getMinutes())}:${p(date.getSeconds())}`;
}

// --- Load ---
async function load() {
  const start = startOf(currentView, currentDate);
  const end = endOf(currentView, start);

  document.getElementById('periodoLabel').textContent = formatPeriod(currentView, start, end);

  try {
    const params = new URLSearchParams({
      de: toLocalISO(start),
      ate: toLocalISO(end)
    });
    agendaItems = await apiFetch(`/agenda?${params}`);
    render(start, end);
  } catch (e) {
    console.error(e);
  }
}

function render(start, end) {
  const container = document.getElementById('calendarContainer');
  if (currentView === 'lista') container.innerHTML = renderListaView();
  else if (currentView === 'dia') container.innerHTML = renderDiaView(start);
  else if (currentView === 'semana') container.innerHTML = renderSemanaView(start);
  else if (currentView === 'mes') container.innerHTML = renderMesView(start);
  bindChipClicks();
}

// --- List View ---
function renderListaView() {
  if (!agendaItems.length) return '<p style="color:var(--color-text-muted);padding:16px">Nenhum evento no período.</p>';
  const byDay = {};
  agendaItems.forEach(item => {
    const day = new Date(item.dataHora).toDateString();
    if (!byDay[day]) byDay[day] = [];
    byDay[day].push(item);
  });
  return Object.entries(byDay).map(([day, items]) => {
    const date = new Date(day);
    const title = date.toLocaleDateString('pt-BR', { weekday: 'long', day: 'numeric', month: 'long' });
    return `<div class="agenda-group-title">${title}</div>` +
      items.map(item => `
      <div class="agenda-item" data-id="${item.id}" data-tipo="${item.tipo}">
        <div class="agenda-item-dot" style="background:${item.cor}"></div>
        <div class="agenda-item-body">
          <div class="agenda-item-titulo">${esc(item.titulo)}</div>
          <div class="agenda-item-meta">
            ${TIPO_LABEL[item.tipo] ?? item.tipo} ·
            ${new Date(item.dataHora).toLocaleTimeString('pt-BR', { hour: '2-digit', minute: '2-digit' })}
            ${item.local ? ` · 📍 ${esc(item.local)}` : ''}
            ${item.nomeResponsavel ? ` · 👤 ${esc(item.nomeResponsavel)}` : ''}
          </div>
        </div>
        <div style="display:flex;gap:6px">
          ${item.tipo !== 'Tarefa' ? `<button class="btn btn-secondary btn-sm" data-action="edit" data-id="${item.id}">✏️</button>
          <button class="btn btn-danger btn-sm" data-action="delete" data-id="${item.id}">🗑</button>` : ''}
        </div>
      </div>`).join('');
  }).join('');
}

// --- Week View ---
const HOURS = Array.from({ length: 14 }, (_, i) => i + 7); // 07..20
const SLOT_PX = 48;

function renderSemanaView(start) {
  const days = Array.from({ length: 7 }, (_, i) => {
    const d = new Date(start); d.setDate(d.getDate() + i); return d;
  });
  const today = new Date(); today.setHours(0, 0, 0, 0);
  const totalH = HOURS.length * SLOT_PX;

  const headerCells = days.map(d => {
    const isHoje = d.getTime() === today.getTime();
    const label = d.toLocaleDateString('pt-BR', { weekday: 'short', day: 'numeric' });
    return `<div class="semana-header${isHoje ? ' hoje' : ''}">${label}</div>`;
  }).join('');

  const timeLabels = HOURS.map((h, i) =>
    `<div style="position:absolute;top:${i*SLOT_PX+2}px;right:4px;font-size:11px;color:var(--color-text-muted)">${String(h).padStart(2,'0')}h</div>`
  ).join('');

  const dayCols = days.map(d => {
    const dayItems = agendaItems.filter(it => {
      const dt = new Date(it.dataHora);
      return dt.getFullYear() === d.getFullYear() &&
             dt.getMonth() === d.getMonth() &&
             dt.getDate() === d.getDate();
    });

    const hLines = HOURS.map((_, i) =>
      `<div style="position:absolute;top:${i*SLOT_PX}px;left:0;right:0;border-top:1px solid var(--color-border-light,#f3f4f6);pointer-events:none"></div>`
    ).join('');

    const chips = dayItems.map(it => {
      const dtStart = new Date(it.dataHora);
      const startH = dtStart.getHours() + dtStart.getMinutes() / 60;
      const top = (startH - HOURS[0]) * SLOT_PX;
      if (top < 0 || top >= totalH) return '';
      let height = SLOT_PX - 4;
      if (it.dataHoraFim) {
        const dur = (new Date(it.dataHoraFim) - dtStart) / 3600000;
        height = Math.max(20, dur * SLOT_PX - 4);
      }
      return `<div class="evento-chip" style="position:absolute;top:${top}px;left:2px;right:2px;height:${height}px;background:${it.cor};white-space:normal;line-height:1.3;overflow:hidden" data-id="${it.id}" data-tipo="${it.tipo}" title="${esc(it.titulo)}">${esc(it.titulo)}</div>`;
    }).join('');

    return `<div style="position:relative;height:${totalH}px;border-right:1px solid var(--color-border-light,#f3f4f6)">${hLines}${chips}</div>`;
  }).join('');

  return `
  <div style="border:1px solid var(--color-border);border-radius:var(--radius);overflow:hidden;background:var(--color-surface)">
    <div style="display:grid;grid-template-columns:60px repeat(7,1fr)">
      <div class="semana-header"></div>
      ${headerCells}
    </div>
    <div style="display:grid;grid-template-columns:60px repeat(7,1fr);overflow-y:auto;max-height:620px">
      <div style="position:relative;height:${totalH}px;border-right:1px solid var(--color-border)">${timeLabels}</div>
      ${dayCols}
    </div>
  </div>`;
}

// --- Day View ---
function renderDiaView(day) {
  const dayItems = agendaItems.filter(it => {
    const dt = new Date(it.dataHora);
    return dt.getFullYear() === day.getFullYear() &&
           dt.getMonth() === day.getMonth() &&
           dt.getDate() === day.getDate();
  });

  if (!dayItems.length) return '<p style="color:var(--color-text-muted);padding:16px">Nenhum evento neste dia.</p>';

  return dayItems.map(it => `
  <div class="agenda-item" style="border-left:4px solid ${it.cor};cursor:pointer" data-id="${it.id}" data-tipo="${it.tipo}">
    <div class="agenda-item-body">
      <div class="agenda-item-titulo">${esc(it.titulo)}</div>
      <div class="agenda-item-meta">
        ${new Date(it.dataHora).toLocaleTimeString('pt-BR', { hour: '2-digit', minute: '2-digit' })}
        ${it.dataHoraFim ? ` – ${new Date(it.dataHoraFim).toLocaleTimeString('pt-BR', { hour: '2-digit', minute: '2-digit' })}` : ''}
        ${it.local ? ` · 📍 ${esc(it.local)}` : ''}
      </div>
    </div>
  </div>`).join('');
}

// --- Month View ---
function renderMesView(start) {
  const year = start.getFullYear();
  const month = start.getMonth();
  const daysInMonth = new Date(year, month + 1, 0).getDate();
  const firstDay = new Date(year, month, 1).getDay();

  const dayNames = ['Dom', 'Seg', 'Ter', 'Qua', 'Qui', 'Sex', 'Sáb'];
  const headers = dayNames.map(d => `<div style="text-align:center;font-weight:600;font-size:12px;padding:4px">${d}</div>`).join('');

  const cells = [];
  for (let i = 0; i < firstDay; i++) cells.push('<div></div>');
  for (let d = 1; d <= daysInMonth; d++) {
    const date = new Date(year, month, d);
    const items = agendaItems.filter(it => {
      const dt = new Date(it.dataHora);
      return dt.getFullYear() === year && dt.getMonth() === month && dt.getDate() === d;
    });
    const today = new Date(); today.setHours(0,0,0,0);
    const isToday = date.getTime() === today.getTime();
    const dots = items.slice(0, 3).map(it =>
      `<div class="evento-chip" style="background:${it.cor};font-size:10px" data-id="${it.id}" data-tipo="${it.tipo}">${esc(it.titulo)}</div>`
    ).join('');
    cells.push(`
    <div style="border:1px solid var(--color-border);border-radius:4px;padding:4px;min-height:70px;background:${isToday ? '#eff6ff' : 'var(--color-surface)'}">
      <div style="font-weight:${isToday ? '700' : '400'};font-size:13px;margin-bottom:2px">${d}</div>
      ${dots}
      ${items.length > 3 ? `<div style="font-size:10px;color:var(--color-text-muted)">+${items.length - 3} mais</div>` : ''}
    </div>`);
  }

  return `
  <div style="display:grid;grid-template-columns:repeat(7,1fr);gap:4px">
    ${headers}
    ${cells.join('')}
  </div>`;
}

// --- Chip click → tooltip ---
function bindChipClicks() {
  document.querySelectorAll('.evento-chip,[data-id][data-tipo]').forEach(el => {
    el.addEventListener('click', e => {
      e.stopPropagation();
      const id = el.dataset.id;
      const tipo = el.dataset.tipo;
      const item = agendaItems.find(it => it.id === id);
      if (!item) return;
      showTooltip(item, tipo, e.clientX, e.clientY);
    });
  });

  document.querySelectorAll('[data-action="edit"]').forEach(btn =>
    btn.addEventListener('click', e => {
      e.stopPropagation();
      openEditModal(btn.dataset.id);
    }));

  document.querySelectorAll('[data-action="delete"]').forEach(btn =>
    btn.addEventListener('click', async e => {
      e.stopPropagation();
      await deleteEvento(btn.dataset.id);
    }));
}

// --- Tooltip ---
function showTooltip(item, tipo, x, y) {
  const tt = document.getElementById('eventoTooltip');
  document.getElementById('ttTitulo').textContent = item.titulo;
  const dt = new Date(item.dataHora);
  const meta = [
    TIPO_LABEL[tipo] ?? tipo,
    dt.toLocaleDateString('pt-BR', { weekday: 'short', day: 'numeric', month: 'short' }),
    dt.toLocaleTimeString('pt-BR', { hour: '2-digit', minute: '2-digit' }),
    item.local ? `📍 ${item.local}` : null,
    item.nomeResponsavel ? `👤 ${item.nomeResponsavel}` : null,
    item.numeroCNJProcesso ? `⚖️ ${item.numeroCNJProcesso}` : null,
  ].filter(Boolean).join(' · ');
  document.getElementById('ttMeta').textContent = meta;

  const isEvento = tipo !== 'Tarefa';
  document.getElementById('ttEdit').style.display = isEvento ? '' : 'none';
  document.getElementById('ttDelete').style.display = isEvento ? '' : 'none';

  document.getElementById('ttEdit').onclick = () => { hideTooltip(); openEditModal(item.id); };
  document.getElementById('ttDelete').onclick = async () => { hideTooltip(); await deleteEvento(item.id); };

  tt.style.display = '';
  tt.style.left = Math.min(x, window.innerWidth - 300) + 'px';
  tt.style.top = Math.min(y + 8, window.innerHeight - 160) + 'px';
}

function hideTooltip() { document.getElementById('eventoTooltip').style.display = 'none'; }
document.getElementById('ttClose').addEventListener('click', hideTooltip);
document.addEventListener('click', e => {
  if (!document.getElementById('eventoTooltip').contains(e.target)) hideTooltip();
});

// --- CRUD Evento ---
async function deleteEvento(id) {
  if (!confirm('Excluir este evento?')) return;
  await apiFetch(`/eventos/${id}`, { method: 'DELETE' });
  await load();
}

function openCreateModal() {
  editingId = null;
  document.getElementById('modalEventoTitulo').textContent = 'Novo Evento';
  document.getElementById('formEvento').reset();
  document.getElementById('eMsgErro').style.display = 'none';
  document.getElementById('modalEvento').style.display = 'flex';
}

async function openEditModal(id) {
  editingId = id;
  document.getElementById('modalEventoTitulo').textContent = 'Editar Evento';
  document.getElementById('eMsgErro').style.display = 'none';
  const ev = await apiFetch(`/eventos/${id}`);
  document.getElementById('eTitulo').value = ev.titulo;
  document.getElementById('eTipo').value = ev.tipo;
  document.getElementById('eDataHora').value = ev.dataHora.substring(0, 16);
  document.getElementById('eDataHoraFim').value = ev.dataHoraFim ? ev.dataHoraFim.substring(0, 16) : '';
  document.getElementById('eLocal').value = ev.local ?? '';
  document.getElementById('eObservacoes').value = ev.observacoes ?? '';
  document.getElementById('modalEvento').style.display = 'flex';
}

function closeModal() { document.getElementById('modalEvento').style.display = 'none'; }

document.getElementById('btnNovoEvento').addEventListener('click', openCreateModal);
document.getElementById('modalEventoClose').addEventListener('click', closeModal);
document.getElementById('btnCancelarEvento').addEventListener('click', closeModal);
document.getElementById('modalEvento').addEventListener('click', e => {
  if (e.target === e.currentTarget) closeModal();
});

document.getElementById('formEvento').addEventListener('submit', async e => {
  e.preventDefault();
  const errEl = document.getElementById('eMsgErro');
  errEl.style.display = 'none';

  const titulo = document.getElementById('eTitulo').value.trim();
  const dataHora = document.getElementById('eDataHora').value;
  if (!titulo || !dataHora) { showErr(errEl, 'Título e data/hora são obrigatórios.'); return; }

  const dto = {
    titulo,
    tipo: document.getElementById('eTipo').value,
    dataHora: dataHora,
    dataHoraFim: document.getElementById('eDataHoraFim').value || null,
    local: document.getElementById('eLocal').value.trim() || null,
    observacoes: document.getElementById('eObservacoes').value.trim() || null,
  };

  try {
    if (editingId) {
      await apiFetch(`/eventos/${editingId}`, { method: 'PUT', body: JSON.stringify(dto) });
    } else {
      await apiFetch('/eventos', { method: 'POST', body: JSON.stringify(dto) });
    }
    closeModal();
    await load();
  } catch (err) {
    showErr(errEl, err.message ?? 'Erro ao salvar evento.');
  }
});

// --- Navigation ---
document.getElementById('btnAnterior').addEventListener('click', () => {
  currentDate = advance(currentView, startOf(currentView, currentDate), -1);
  load();
});
document.getElementById('btnProximo').addEventListener('click', () => {
  currentDate = advance(currentView, startOf(currentView, currentDate), 1);
  load();
});
document.getElementById('btnHoje').addEventListener('click', () => {
  currentDate = new Date(); currentDate.setHours(0, 0, 0, 0);
  load();
});

['Dia', 'Semana', 'Mes', 'Lista'].forEach(v => {
  document.getElementById(`view${v}`).addEventListener('click', () => {
    currentView = v.toLowerCase();
    document.querySelectorAll('.view-btn').forEach(b => b.classList.remove('active'));
    document.getElementById(`view${v}`).classList.add('active');
    load();
  });
});

// --- Helpers ---
function esc(str) {
  return (str ?? '').replace(/&/g,'&amp;').replace(/</g,'&lt;').replace(/>/g,'&gt;').replace(/"/g,'&quot;');
}
function showErr(el, msg) { el.textContent = msg; el.style.display = ''; }

// --- Init ---
load();
