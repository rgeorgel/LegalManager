// Helpers compartilhados do frontend (escape de HTML, moeda e datas).
// Antes havia uma cópia de `esc` em quase toda página — algumas sem escapar aspas.

/** Escapa texto para interpolar em HTML (conteúdo e atributos entre aspas). */
export function esc(valor) {
  return String(valor ?? '')
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;')
    .replace(/'/g, '&#39;');
}

const BRL = new Intl.NumberFormat('pt-BR', { style: 'currency', currency: 'BRL' });

/** Formata um número como moeda (R$ 1.234,56). Nulo/NaN → "—". */
export function brl(valor) {
  if (valor === null || valor === undefined || Number.isNaN(Number(valor))) return '—';
  return BRL.format(Number(valor));
}

// ── Datas ────────────────────────────────────────────────────────────────
// Prazos (Tarefa.prazo) e eventos (Evento.dataHora/dataHoraFim) são gravados na hora
// "de parede" de Brasília que o usuário digitou, mas a API os devolve com sufixo de fuso
// do servidor (ex.: "2026-09-28T17:00:00+00:00"). `new Date()` aplicaria esse fuso e
// mostraria 3h a menos — use `dataParede()` para esses campos. Para carimbos gerados no
// servidor (criadoEm, criadaEm, concluidaEm…), que são UTC de verdade, use `new Date()`.

/**
 * Lê um prazo/data de evento como hora local, ignorando o sufixo de fuso.
 * Aceita "YYYY-MM-DD", "YYYY-MM-DDTHH:mm[:ss][.fff][Z|±hh:mm]" ou Date. Inválido → null.
 */
export function dataParede(valor) {
  if (!valor) return null;
  if (valor instanceof Date) return Number.isNaN(valor.getTime()) ? null : valor;
  const m = String(valor).match(/^(\d{4}-\d{2}-\d{2})(?:[T ](\d{2}:\d{2}(?::\d{2})?))?/);
  if (!m) return null;
  // Sem sufixo de fuso, o JS interpreta data+hora como hora local (data pura seria UTC).
  const d = new Date(`${m[1]}T${m[2] ?? '00:00:00'}`);
  return Number.isNaN(d.getTime()) ? null : d;
}

/** "dd/mm/aaaa" de um prazo/data de evento (ver `dataParede`). Vazio → fallback. */
export function fmtDataParede(valor, fallback = '—') {
  const d = dataParede(valor);
  return d ? d.toLocaleDateString('pt-BR') : fallback;
}

/** "dd/mm/aaaa, hh:mm:ss" de um prazo/data de evento (ver `dataParede`). Vazio → fallback. */
export function fmtDataHoraParede(valor, fallback = '—') {
  const d = dataParede(valor);
  return d ? d.toLocaleString('pt-BR') : fallback;
}

/** "YYYY-MM-DDTHH:mm" para preencher <input type="datetime-local"> com um prazo/evento. */
export function inputDataHoraParede(valor) {
  const m = valor ? String(valor).match(/^(\d{4}-\d{2}-\d{2})[T ](\d{2}:\d{2})/) : null;
  return m ? `${m[1]}T${m[2]}` : '';
}

/**
 * "YYYY-MM-DDTHH:mm:ss" na hora local, sem fuso — para limites de consulta de prazos/eventos
 * (ex.: GET /agenda?de=…&ate=…), que estão no mesmo referencial (ver `dataParede`).
 */
export function isoLocal(data) {
  const p = n => String(n).padStart(2, '0');
  return `${data.getFullYear()}-${p(data.getMonth() + 1)}-${p(data.getDate())}T${p(data.getHours())}:${p(data.getMinutes())}:${p(data.getSeconds())}`;
}

// ── Navegação ────────────────────────────────────────────────────────────

/**
 * Atalhos "criar novo" (ex.: dashboard) abrem a página com ?novo=1. Clica no botão de
 * criação — mesmo fluxo do usuário, inclusive regras de plano — e remove o parâmetro da
 * URL, para que recarregar a página não reabra o formulário. Chame depois de ligar o clique.
 */
// Capturado quando este módulo é carregado — antes do código da página, que pode
// reescrever a URL (ex.: financeiro troca a query por ?mes=&ano=) e perder o ?novo=1.
let novoPendente = new URLSearchParams(location.search).get('novo') === '1';

export function abrirNovoPelaUrl(idBotao) {
  if (!novoPendente) return;
  novoPendente = false;
  const url = new URL(location.href);
  if (url.searchParams.has('novo')) {
    url.searchParams.delete('novo');
    history.replaceState(history.state, '', url.pathname + url.search + url.hash);
  }
  document.getElementById(idBotao)?.click();
}
