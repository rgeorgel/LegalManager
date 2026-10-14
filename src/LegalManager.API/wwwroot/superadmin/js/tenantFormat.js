// Formatação de dados de tenant no superadmin, compartilhada entre a listagem
// (tenants.html) e a página de um tenant (tenant-detalhe.html).
import { esc } from '/js/utils.js';
import { getMascote, MASCOTE_PADRAO } from '/js/mascotes.js';

export const PLAN_COLORS = { Free: 'gray', Plus: 'blue', Pro: 'amber', Max: 'green', Enterprise: 'blue' };
export const STATUS_COLORS = { Ativo: 'green', Trial: 'amber', Suspenso: 'red', Cancelado: 'gray' };

export function fmtDate(iso) { return iso ? new Date(iso).toLocaleDateString('pt-BR') : '—'; }
export function fmtDateTime(iso) {
  if (!iso) return '—';
  const d = new Date(iso);
  return d.toLocaleDateString('pt-BR') + ' ' + d.toLocaleTimeString('pt-BR', { hour: '2-digit', minute: '2-digit' });
}
export function fmtBytes(b) {
  if (!b) return '0 B';
  if (b < 1024) return b + ' B';
  if (b < 1024 * 1024) return (b / 1024).toFixed(1) + ' KB';
  if (b < 1024 * 1024 * 1024) return (b / (1024 * 1024)).toFixed(1) + ' MB';
  return (b / (1024 * 1024 * 1024)).toFixed(2) + ' GB';
}
export function pctColor(pct) {
  if (pct >= 90) return 'red';
  if (pct >= 70) return 'amber';
  return '';
}
export function pctBar(pct, label) {
  const w = Math.min(100, Math.max(0, pct)).toFixed(0);
  const cls = pctColor(pct);
  return `<div class="usage-bar" title="${label} — ${w}%">
    <div class="usage-bar-fill ${cls}" style="width:${w}%"></div>
  </div>`;
}
export function fmtMb(mb) {
  if (mb >= 1024) return (mb / 1024).toFixed(1) + ' GB';
  return mb + ' MB';
}
export function daysUntil(iso) {
  if (!iso) return null;
  const ms = new Date(iso).getTime() - Date.now();
  return Math.ceil(ms / 86400000);
}
// Código do cadastro (período grátis) e/ou de desconto — podem ser diferentes quando o
// desconto foi aplicado depois na tela de assinatura.
export function codigoPromoDetalhe(t) {
  const codigo = c => `<span style="font-family:monospace;font-weight:600">${esc(c.toUpperCase())}</span>`;
  const partes = [];
  if (t.voucherUtilizado) partes.push(`${codigo(t.voucherUtilizado)} <span style="color:var(--color-text-muted);font-size:12px">(cadastro)</span>`);
  if (t.codigoDesconto && t.codigoDesconto !== t.voucherUtilizado) partes.push(`${codigo(t.codigoDesconto)} <span style="color:var(--color-text-muted);font-size:12px">(desconto)</span>`);
  if (!partes.length) return '—';
  if (t.codigoDesconto) {
    partes.push(t.descontoPromocionalUsadoEm
      ? `<span class="badge badge-green" title="Desconto aplicado numa assinatura">desconto usado em ${fmtDate(t.descontoPromocionalUsadoEm)}</span>`
      : `<span class="badge badge-yellow" title="Desconto ainda não aproveitado numa assinatura">desconto pendente</span>`);
  }
  return partes.join(' ');
}

export function countdownBadge(iso, type) {
  const d = daysUntil(iso);
  if (d === null) return '';
  if (d < 0) {
    return `<span class="badge badge-red" title="${type} venceu há ${-d} dia(s)">vencido há ${-d}d</span>`;
  }
  if (d <= 7) {
    return `<span class="badge badge-red" title="${type} expira em ${d} dia(s)">em ${d}d</span>`;
  }
  if (d <= 30) {
    return `<span class="badge badge-amber" title="${type} expira em ${d} dia(s)">em ${d}d</span>`;
  }
  return `<span class="badge badge-gray" title="${type} expira em ${d} dia(s)">em ${d}d</span>`;
}
export function daysAgo(iso) {
  if (!iso) return 'nunca';
  const ms = Date.now() - new Date(iso).getTime();
  const d = Math.floor(ms / 86400000);
  if (d === 0) {
    const h = Math.floor(ms / 3600000);
    if (h === 0) return 'agora';
    return `há ${h}h`;
  }
  if (d === 1) return 'há 1 dia';
  if (d < 30) return `há ${d} dias`;
  if (d < 365) return `há ${Math.floor(d / 30)}m`;
  return `há ${Math.floor(d / 365)}a`;
}
export function paymentBadge(t) {
  // Na listagem vem `temBilling` (já calculado no backend); no modal de detalhes
  // vêm os IDs crus (stripeSubscriptionId/abacatePayBillingId) — cobrimos os dois formatos.
  const hasBilling = t.temBilling !== undefined
    ? t.temBilling
    : !!(t.stripeSubscriptionId || t.abacatePayBillingId);
  // Plano: Free não tem billing
  if (t.plano === 'Free') {
    return '<span class="badge badge-gray" title="Plano Free, sem cobrança">—</span>';
  }
  if (!hasBilling) {
    // Pago ou Plus trial sem billing ainda
    const isTrial = t.status === 'Trial';
    if (isTrial) return '<span class="badge badge-blue" title="Em trial, ainda sem billing">trial</span>';
    return '<span class="badge badge-amber" title="Plano pago sem billing — verifique">⚠ sem billing</span>';
  }
  // Tem billing
  const planoDias = daysUntil(t.planoExpiraEm);
  if (planoDias !== null && planoDias < 0) {
    return '<span class="badge badge-red" title="Plano vencido, billing pode estar inadimplente">⚠ vencido</span>';
  }
  return '<span class="badge badge-green" title="Billing ativo no AbacatePay">✓ pago</span>';
}
// Mascote do assistente: id salvo pelo usuário, ou null = nunca escolheu (vale o padrão).
export function mascoteMini(id) {
  const m = getMascote(id);
  const titulo = id ? m.nome : `${m.nome} (padrão — nunca escolheu)`;
  return `<span class="mascote-mini" title="${esc(titulo)}" aria-label="${esc(titulo)}">${m.svg}</span>`;
}
export function mascotesCell(t) {
  const usos = t.mascotes ?? [];
  if (!usos.length) return '';
  return `<div style="display:flex;gap:6px;flex-wrap:wrap;margin-top:4px" title="Mascote do assistente escolhido pelos usuários">
    ${usos.map(u => `<span class="mascote-uso">${mascoteMini(u.mascote)}${u.usuarios > 1 ? `×${u.usuarios}` : ''}</span>`).join('')}
  </div>`;
}
export function mascotesDetalhado(t) {
  const usuarios = (t.usuarios ?? []).filter(u => u.perfil !== 'Cliente' && u.perfil !== 'SuperAdmin');
  if (!usuarios.length) return '<span class="badge badge-gray">—</span>';
  const contagem = new Map();
  usuarios.forEach(u => contagem.set(u.assistenteMascote ?? null, (contagem.get(u.assistenteMascote ?? null) ?? 0) + 1));
  return `<div style="display:flex;gap:8px;flex-wrap:wrap;justify-content:flex-end">${[...contagem]
    .sort((a, b) => b[1] - a[1])
    .map(([id, n]) => `<span class="mascote-uso">${mascoteMini(id)}${n > 1 ? `×${n}` : ''}</span>`).join('')}</div>`;
}
// Mascotes lado a lado, sem margem — para ficar ao lado do nome do escritório.
export function mascotesInline(t) {
  const usos = t.mascotes ?? [];
  if (!usos.length) return '';
  return `<span class="mascotes-inline" title="Mascote do assistente escolhido pelos usuários">
    ${usos.map(u => `<span class="mascote-uso">${mascoteMini(u.mascote)}${u.usuarios > 1 ? `×${u.usuarios}` : ''}</span>`).join('')}
  </span>`;
}
/** @param {{ comMascotes?: boolean }} opcoes comMascotes=false quando o mascote já aparece ao lado do nome. */
export function recursosCell(t, { comMascotes = true } = {}) {
  const tema = t.temaCustomizado
    ? '<span class="badge badge-blue" title="Tema personalizado (cores/layout/CSS) configurado por este tenant">🎨 tema custom</span>'
    : '<span class="badge badge-gray" title="Usando o tema padrão do Causify">🎨 tema padrão</span>';
  const usoBadge = (count, icon, label) =>
    `<span class="badge badge-${count > 0 ? 'green' : 'gray'}" title="${label}: ${count}">${icon} ${count}</span>`;
  return `<div>${tema}</div>
    <div class="badges-row" style="margin-top:3px">
      ${usoBadge(t.honorarioContratosCount, '📄', 'Contratos de honorário cadastrados')}
      ${usoBadge(t.calculadoraPrazosCount, '⏱', 'Cálculos na calculadora de prazos')}
      ${usoBadge(t.calculadoraHonorariosCount, '🧮', 'Cálculos salvos na calculadora de honorários')}
    </div>
    ${comMascotes ? mascotesCell(t) : ''}`;
}
// Versão explicada da mesma informação de recursosCell() — usada só no modal de detalhes,
// que tem espaço de sobra pra descrever o que cada indicador significa (a tabela precisa
// ficar compacta com ícone + número, sem contexto).
export function recursoRow(icon, titulo, descricao, valorHtml) {
  return `<div class="recurso-row">
    <div class="recurso-row-icon">${icon}</div>
    <div style="flex:1;min-width:0">
      <div class="recurso-row-titulo">${titulo}</div>
      <div class="recurso-row-desc">${descricao}</div>
    </div>
    <div class="recurso-row-valor">${valorHtml}</div>
  </div>`;
}
export function recursosDetalhado(t) {
  const temaValor = t.temaCustomizado
    ? '<span class="badge badge-blue">Personalizado</span>'
    : '<span class="badge badge-gray">Padrão</span>';
  const temaDesc = t.temaCustomizado
    ? 'O tenant salvou cores, layout ou CSS próprios em Configurações → Tema, diferentes do padrão do Causify.'
    : 'Nenhuma personalização visual salva — o tenant usa as cores e o layout padrão do Causify.';
  const usoValor = count => `<span class="badge badge-${count > 0 ? 'green' : 'gray'}">${count}</span>`;

  return `
    ${recursoRow('🎨', 'Tema personalizado', temaDesc, temaValor)}
    ${recursoRow('📄', 'Honorários · Contratos', 'Contratos de honorário cadastrados no módulo de Honorários (cobrança por parcelas, multa e juros).', usoValor(t.honorarioContratosCount))}
    ${recursoRow('⏱', 'Calculadora de prazos', 'Quantidade de vezes que usuários do tenant calcularam um prazo processual (dias úteis/corridos) na calculadora de prazos.', usoValor(t.calculadoraPrazosCount))}
    ${recursoRow('🧮', 'Calculadora de honorários', 'Cálculos de honorários (base na tabela OAB) salvos no histórico da calculadora de honorários.', usoValor(t.calculadoraHonorariosCount))}
    ${recursoRow(mascoteMini(MASCOTE_PADRAO), 'Mascote do assistente', 'Mascote escolhido por cada usuário em Sistema → Assistente (passe o mouse para ver o nome). Quem nunca escolheu usa o padrão.', mascotesDetalhado(t))}
  `;
}

export function planoHtml(t) {
  const trialPlus = t.trialConcedidoDias && t.trialExpiraEm && new Date(t.trialExpiraEm) > new Date()
    ? `<span class="badge badge-blue" style="font-size:10px" title="Trial Plus concedido por superadmin">🎁 trial plus ${t.trialConcedidoDias}d</span>`
    : '';
  return `<div class="badges-col">
    <div class="badges-row">
      <span class="badge badge-${PLAN_COLORS[t.plano] ?? 'gray'}">${t.plano}</span>
      <span class="badge badge-${STATUS_COLORS[t.status] ?? 'gray'}">${t.status}</span>
    </div>
    ${paymentBadge(t)}
    ${trialPlus}
  </div>`;
}
