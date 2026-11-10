// Helpers das calculadoras (pensão, FGTS, trabalhista): formatação, fonte dos índices, PDF e cópia da tabela.
import { esc, brl, isoLocal } from './utils.js';
import { apiDownload } from './api.js';

const formatador = (casas) => new Intl.NumberFormat('pt-BR', { minimumFractionDigits: casas, maximumFractionDigits: casas });
export const fmt2 = formatador(2);
export const fmt4 = formatador(4);
export const fmt6 = formatador(6);

export const pad = n => String(n).padStart(2, '0');
export const arred = v => Math.round((v + Number.EPSILON) * 100) / 100;

/** "YYYY-MM-DD" (DateOnly da API) → "dd/mm/aaaa". */
export function fmtData(iso) {
  if (!iso) return '—';
  const [a, m, d] = String(iso).slice(0, 10).split('-');
  return `${d}/${m}/${a}`;
}

/** Hoje no formato de <input type="date">. */
export const hojeIso = () => isoLocal(new Date()).slice(0, 10);

/** Valor numérico de um <input>, ou null se vazio/inválido. */
export function numero(el) {
  const v = parseFloat(el.value);
  return Number.isFinite(v) ? v : null;
}

/** Meses de "YYYY-MM" a "YYYY-MM" (inclusive), no máximo `limite`. */
export function mesesEntre(inicio, fim, limite = 800) {
  if (!inicio || !fim || fim < inicio) return [];
  let [a, m] = inicio.split('-').map(Number);
  const [af, mf] = fim.split('-').map(Number);
  const lista = [];
  while ((a < af || (a === af && m <= mf)) && lista.length < limite) {
    lista.push({ ano: a, mes: m });
    if (++m > 12) { m = 1; a++; }
  }
  return lista;
}

/** Cartão de número (rótulo + valor) do resultado. */
export function stat(rotulo, valor) {
  return `<div class="pc-stat"><div class="pc-stat-label">${esc(rotulo)}</div><div class="pc-stat-value">${esc(valor)}</div></div>`;
}

/** Salário mínimo vigente em (ano, mês) a partir das vigências da API. */
export function smEm(salarios, ano, mes) {
  let valor = null;
  for (const s of salarios || []) {
    if (s.ano > ano || (s.ano === ano && s.mes > mes)) break;
    valor = s.valor;
  }
  return valor;
}

/** Linha "Atualizados automaticamente pelo Banco Central: IPCA-E até 09/2026 · …". */
export function htmlFonteIndices(indices, salarios) {
  const partes = (indices || []).map(i => `${i.nome} até ${i.ano ? `${pad(i.mes)}/${i.ano}` : 'sem dados'}`);
  const hoje = new Date();
  const sm = smEm(salarios, hoje.getFullYear(), hoje.getMonth() + 1);
  return `Índices atualizados automaticamente pelo Banco Central: ${esc(partes.join(' · '))}` +
    (sm ? ` · salário mínimo vigente <strong>${esc(brl(sm))}</strong>` : '');
}

/** Gera o PDF por POST e baixa o arquivo. Lança Error com a mensagem do servidor. */
export async function baixarPdf(path, payload, botao) {
  const rotulo = botao.textContent;
  botao.disabled = true;
  botao.textContent = 'Gerando…';
  try {
    const { blob, nomeArquivo } = await apiDownload(path, { method: 'POST', body: payload });
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = nomeArquivo;
    document.body.appendChild(a);
    a.click();
    a.remove();
    setTimeout(() => URL.revokeObjectURL(url), 60_000);
  } finally {
    botao.disabled = false;
    botao.textContent = rotulo;
  }
}

/** Copia linhas (arrays de texto) como TSV — cola direto no Excel/Planilhas. */
export async function copiarTsv(linhas, botao) {
  await navigator.clipboard.writeText(linhas.map(l => l.join('\t')).join('\n'));
  const rotulo = botao.textContent;
  botao.textContent = 'Copiado!';
  setTimeout(() => { botao.textContent = rotulo; }, 1800);
}
