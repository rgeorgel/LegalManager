import { initLayout } from './layout.js';
import { apiFetch } from './api.js';

import { esc, dataParede, fmtDataParede, inputDataHoraParede } from './utils.js';
import { cardEntidade, abrirMenuCard } from './cards.js';
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

// Card de entidade do sistema (js/cards.js), numa coluna (são textos para ler). Publicação não tem
// página de detalhe: o card não é clicável; o texto, a sugestão da IA e o "Ver mais" ficam no corpo
// e as ações (lida, IA, PDF, Escavador, arquivar) no menu ⋮ ao lado do status.
const STATUS_BADGE = { Nova: 'pub-badge-nova', Lida: 'pub-badge-lida', Arquivada: 'pub-badge-arquivada' };
const FONTE_BADGE = {
  Escavador: '<span class="pub-badge" style="background:#ecfdf5;color:#065f46">📡 Escavador</span>',
  DJe: '<span class="pub-badge" style="background:#fef3c7;color:#92400e">📰 DJe</span>',
  Manual: '<span class="pub-badge" style="background:#f3f4f6;color:#6b7280">✍️ Manual</span>'
};

// Sem nenhuma ação disponível (arquivada, já analisada, sem links), o ⋮ não aparece.
const temAcoes = p => p.status !== 'Arquivada' || !p.triagem || !!p.linkPdf || !!p.linkEscavador;

function cardPublicacao(p) {
  const processo = p.numeroCNJ
    ? (p.processoId
      ? `<a href="/pages/processo-detalhe.html?id=${encodeURIComponent(p.processoId)}">${esc(p.numeroCNJ)}</a>`
      : esc(p.numeroCNJ))
    : '';
  const longo = (p.conteudo?.length ?? 0) > 300;
  return cardEntidade({
    titulo: TIPO_LABEL[p.tipo] ?? p.tipo,
    estado: p.urgente ? 'danger' : p.status === 'Nova' ? 'warn' : p.status === 'Arquivada' ? 'muted' : '',
    dados: { id: p.id },
    statusHtml: `<span class="pub-badge ${STATUS_BADGE[p.status] ?? ''}">${esc(STATUS_LABEL[p.status] ?? p.status)}</span>`
      + (temAcoes(p) ? `<button type="button" class="ent-card-menu" data-menu-pub="${esc(p.id)}" aria-haspopup="menu" aria-expanded="false" title="Mais ações" aria-label="Mais ações da publicação">⋮</button>` : ''),
    tagsHtml: [
      p.urgente ? '<span class="pub-badge-urgente">🔴 URGENTE</span>' : '',
      FONTE_BADGE[p.fonteCaptura] ?? '',
    ],
    campos: [
      { rotulo: 'Processo', valorHtml: processo, dica: p.numeroCNJ },
      { rotulo: 'Publicada em', valor: fmtDataParede(p.dataPublicacao) },
      { rotulo: 'Diário', valor: [p.diario, p.tribunal].filter(Boolean).join(' — ') },
    ],
    corpoHtml: `
      ${p.classificacaoIA ? `<div class="pub-ia-resumo">🤖 ${esc(p.classificacaoIA)}</div>` : ''}
      ${renderTriagem(p)}
      ${p.snippet ? `<div class="pub-snippet">${esc(p.snippet)}</div>` : ''}
      <div class="pub-conteudo" id="conteudo-${esc(p.id)}">${esc(p.conteudo)}</div>
      ${longo ? `<button type="button" class="pub-ver-mais" data-action="expandir" data-id="${esc(p.id)}" aria-expanded="false">Ver mais</button>` : ''}`,
  });
}

async function acaoPublicacao(id, caminho, metodo = 'PATCH') {
  try {
    await apiFetch(`/publicacoes/${id}/${caminho}`, { method: metodo });
    load();
  } catch (e) {
    alert(e.message || 'Não foi possível concluir. Tente de novo.');
  }
}

document.getElementById('listaPubs').addEventListener('click', e => {
  const expandir = e.target.closest('[data-action="expandir"]');
  if (expandir) {
    const el = document.getElementById(`conteudo-${expandir.dataset.id}`);
    const aberto = el.classList.toggle('expanded');
    expandir.textContent = aberto ? 'Ver menos' : 'Ver mais';
    expandir.setAttribute('aria-expanded', String(aberto));
    return;
  }
  const botao = e.target.closest('[data-menu-pub]');
  if (!botao) return;
  const p = pubs.find(x => x.id === botao.dataset.menuPub);
  if (!p) return;
  const abrir = url => window.open(url, '_blank', 'noopener');
  abrirMenuCard(botao, [
    p.status === 'Nova' && { icone: '✓', rotulo: 'Marcar como lida', acao: () => acaoPublicacao(p.id, 'lida') },
    !p.triagem && { icone: '🤖', rotulo: 'Analisar com IA', acao: () => acaoPublicacao(p.id, 'triagem/refazer', 'POST') },
    p.linkPdf && { icone: '📄', rotulo: 'Ver PDF', acao: () => abrir(p.linkPdf) },
    p.linkEscavador && { icone: '🔗', rotulo: 'Ver no Escavador', acao: () => abrir(p.linkEscavador) },
    p.status !== 'Arquivada' && { icone: '🗄️', rotulo: 'Arquivar', acao: () => acaoPublicacao(p.id, 'arquivar') },
  ]);
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
  list.innerHTML = pubs.map(cardPublicacao).join('');
}

document.getElementById('btnFiltrar').addEventListener('click', load);
document.getElementById('btnMarcarTodasLidas').addEventListener('click', async () => {
  const novas = pubs.filter(p => p.status === 'Nova');
  await Promise.all(novas.map(p => apiFetch(`/publicacoes/${p.id}/lida`, { method: 'PATCH' })));
  load();
});


load();
