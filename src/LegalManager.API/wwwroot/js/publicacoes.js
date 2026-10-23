import { initLayout } from './layout.js';
import { apiFetch } from './api.js';

import { esc, dataParede, fmtDataParede, inputDataHoraParede } from './utils.js';
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
  const fonte = document.getElementById('filtroFonte').value;
  const de = document.getElementById('filtroDe').value;
  const ate = document.getElementById('filtroAte').value;
  const sugestao = document.getElementById('filtroSugestao').value;

  const params = new URLSearchParams({ pageSize: 50 });
  if (status) params.set('status', status);
  if (tipo) params.set('tipo', tipo);
  if (fonte) params.set('fonteCaptura', fonte);
  if (de) params.set('de', de);
  if (ate) params.set('ate', ate);
  if (sugestao) params.set('comSugestao', 'true');

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

// ── Sugestão da IA (triagem) ───────────────────────────────────────────────
// A IA só extrai o prazo do texto; o vencimento vem calculado pelo servidor (feriados, recesso).

const fmtDiaSemana = v => dataParede(v)?.toLocaleDateString('pt-BR', { weekday: 'long' }) ?? '';
const fmtDataHora = v => {
  const d = dataParede(v);
  return d ? `${d.toLocaleDateString('pt-BR')} às ${d.toLocaleTimeString('pt-BR', { hour: '2-digit', minute: '2-digit' })}` : '—';
};
const inputData = v => (v ? String(v).substring(0, 10) : '');

function renderTriagem(p) {
  const t = p.triagem;
  if (!t) return '';
  switch (t.status) {
    case 'Pendente':
      return '<div class="pub-sugestao-status">🤖 Analisando com IA…</div>';
    case 'Falhou':
      return `<div class="pub-sugestao-status">🤖 Não foi possível analisar esta publicação com IA.
        <button class="btn btn-secondary btn-sm" data-action="triagem-refazer" data-id="${p.id}">Tentar de novo</button></div>`;
    case 'Ignorada':
      return `<div class="pub-sugestao-status">🤖 Sugestão ignorada.
        <button class="btn btn-secondary btn-sm" data-action="triagem-refazer" data-id="${p.id}">Analisar de novo</button></div>`;
    case 'Aceita':
      return t.eventoId
        ? '<div class="pub-sugestao-status">✓ Audiência criada na <a href="/pages/agenda.html">agenda</a>.</div>'
        : '<div class="pub-sugestao-status">✓ Prazo criado em <a href="/pages/tarefas.html">Tarefas</a>.</div>';
    case 'Sugerida':
      break;
    default:
      return '';
  }

  // Sem prazo e com audiência, a sugestão é a audiência; senão, o prazo (com data a informar se faltar).
  const evento = !t.vencimento && !!t.audienciaDataHora;
  const tipoDias = t.tipoCalculo === 'DiasCorridos' ? 'corridos' : 'úteis';
  let corpo = '';
  if (t.vencimento) {
    corpo += `<div class="pub-sugestao-prazo">⏰ ${t.prazoDias} dias ${tipoDias} — vence <strong>${fmtDataParede(t.vencimento)}</strong> (${esc(fmtDiaSemana(t.vencimento))})</div>`;
  } else if (!evento) {
    corpo += '<div class="pub-sugestao-prazo">⏰ Prazo não identificado com segurança: informe o vencimento.</div>';
  }
  if (t.audienciaDataHora) {
    corpo += `<div class="pub-sugestao-prazo">📅 Audiência em <strong>${esc(fmtDataHora(t.audienciaDataHora))}</strong></div>`;
  }
  if (t.memoriaCalculo) corpo += `<div class="pub-sugestao-memoria">${esc(t.memoriaCalculo)}</div>`;
  const trecho = t.prazoTrecho || t.audienciaTrecho;
  if (trecho) corpo += `<div class="pub-sugestao-trecho">“${esc(trecho)}”</div>`;

  const titulo = evento ? 'Audiência' : (t.ato || 'Prazo da publicação');
  const campoData = evento
    ? `<input type="datetime-local" class="form-control" data-campo="data" value="${inputDataHoraParede(t.audienciaDataHora)}" aria-label="Data e hora da audiência">`
    : `<input type="date" class="form-control" data-campo="data" value="${inputData(t.vencimento)}" aria-label="Vencimento">`;
  const extraAudiencia = !evento && t.audienciaDataHora
    ? `<button class="btn btn-secondary btn-sm" data-action="triagem-audiencia" data-id="${p.id}">Criar a audiência na agenda</button>`
    : '';

  return `
    <div class="pub-sugestao" data-triagem="${p.id}" data-evento="${evento ? '1' : ''}">
      <div class="pub-sugestao-titulo">🤖 Sugestão da IA${t.ato ? `: ${esc(t.ato)}` : ''}</div>
      ${corpo}
      <div class="pub-sugestao-form">
        <input type="text" class="form-control" data-campo="titulo" maxlength="300" value="${esc(titulo)}" aria-label="Título">
        ${campoData}
        <button class="btn btn-primary btn-sm" data-action="triagem-aceitar" data-id="${p.id}">${evento ? 'Criar audiência' : 'Criar prazo'}</button>
        ${extraAudiencia}
        <button class="btn btn-secondary btn-sm" data-action="triagem-ignorar" data-id="${p.id}">Ignorar</button>
      </div>
    </div>`;
}

async function acaoTriagem(btn, caminho, body) {
  btn.disabled = true;
  try {
    await apiFetch(`/publicacoes/${btn.dataset.id}/triagem/${caminho}`, { method: 'POST', ...(body ? { body } : {}) });
    load();
  } catch (e) {
    btn.disabled = false;
    alert(e.message || 'Não foi possível concluir. Tente de novo.');
  }
}

document.getElementById('listaPubs').addEventListener('click', e => {
  const btn = e.target.closest('button[data-action^="triagem-"]');
  if (!btn) return;
  const acao = btn.dataset.action;
  if (acao === 'triagem-ignorar') return acaoTriagem(btn, 'ignorar');
  if (acao === 'triagem-refazer') return acaoTriagem(btn, 'refazer');
  // Prazo e audiência na mesma publicação: este botão cria a audiência, com a data sugerida.
  if (acao === 'triagem-audiencia') return acaoTriagem(btn, 'aceitar', { titulo: 'Audiência', criarEvento: true });

  const box = btn.closest('[data-triagem]');
  const titulo = box.querySelector('[data-campo="titulo"]').value.trim();
  const valor = box.querySelector('[data-campo="data"]').value;
  const evento = box.dataset.evento === '1';
  if (!valor) {
    alert(evento ? 'Informe a data e a hora da audiência.' : 'Informe o vencimento do prazo.');
    return;
  }
  // Hora "de parede", sem fuso. Prazo sem hora: meio-dia, como na calculadora de prazos.
  const data = evento ? `${valor}:00` : `${valor}T12:00:00`;
  return acaoTriagem(btn, 'aceitar', { titulo: titulo || null, data, criarEvento: evento });
});

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
    const fonteBadge = {
      Escavador: '<span class="pub-badge" style="background:#ecfdf5;color:#065f46">📡 Escavador</span>',
      DJe: '<span class="pub-badge" style="background:#fef3c7;color:#92400e">📰 DJe</span>',
      Manual: '<span class="pub-badge" style="background:#f3f4f6;color:#6b7280">✍️ Manual</span>'
    }[p.fonteCaptura] ?? '';
    return `
    <div class="pub-card${p.urgente ? ' pub-card-urgente' : ''}" data-id="${p.id}">
      <div class="pub-card-header">
        <div class="pub-card-tipo">
          ${p.urgente ? '<span class="pub-badge-urgente">🔴 URGENTE</span>' : ''}
          <span class="pub-tipo">${TIPO_LABEL[p.tipo] ?? p.tipo}</span>
          <span class="pub-badge ${statusClass}">${STATUS_LABEL[p.status]}</span>
          ${fonteBadge}
          ${p.numeroCNJ ? `<span style="font-size:12px;color:var(--color-text-muted)">${esc(p.numeroCNJ)}</span>` : ''}
        </div>
        <span class="pub-card-meta">${fmtDataParede(p.dataPublicacao)} — ${esc(p.diario)}${p.tribunal ? ' — ' + esc(p.tribunal) : ''}</span>
      </div>
      ${p.classificacaoIA ? `<div class="pub-ia-resumo">🤖 ${esc(p.classificacaoIA)}</div>` : ''}
      ${renderTriagem(p)}
      ${p.snippet ? `<div class="pub-snippet" style="font-size:13px;color:var(--color-text-muted);margin-bottom:6px;font-style:italic">${esc(p.snippet)}</div>` : ''}
      <div class="pub-card-conteudo" id="conteudo-${p.id}">${esc(p.conteudo)}</div>
      <div class="pub-card-actions">
        ${p.conteudo.length > 300 ? `<button class="btn btn-secondary btn-sm" data-action="expandir" data-id="${p.id}">Ver mais</button>` : ''}
        ${p.linkPdf ? `<a class="btn btn-secondary btn-sm" href="${esc(p.linkPdf)}" target="_blank" rel="noopener">📄 Ver PDF</a>` : ''}
        ${p.linkEscavador ? `<a class="btn btn-secondary btn-sm" href="${esc(p.linkEscavador)}" target="_blank" rel="noopener">🔗 Ver no Escavador</a>` : ''}
        ${!p.triagem ? `<button class="btn btn-secondary btn-sm" data-action="triagem-refazer" data-id="${p.id}">🤖 Analisar com IA</button>` : ''}
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


load();
