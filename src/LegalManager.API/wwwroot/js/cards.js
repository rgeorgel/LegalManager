// Card de entidade — padrão do sistema para a visão em blocos das listagens.
// CSS em css/styles.css (.ent-*); regras em CLAUDE.md → "Padrão de card".
//
// Uso:
//   import { cardEntidade, ativarCardsClicaveis, ICONES } from '/js/cards.js';
//   lista.innerHTML = `<div class="ent-grid">${itens.map(i => cardEntidade({...})).join('')}</div>`;
//   ativarCardsClicaveis(lista);   // uma vez, no contêiner que recebe os cards
//
// Campos com sufixo "Html" são inseridos como estão (quem chama monta e escapa); os demais
// são texto e são escapados aqui.
import { esc } from './utils.js';

// Ícones de traço (herdam a cor do texto). Use ícones bem distintos entre si no mesmo card.
export const ICONES = {
  download: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M12 4v11"/><path d="m7 10 5 5 5-5"/><path d="M5 20h14"/></svg>',
};

const ESTADOS = new Set(['ok', 'danger', 'warn', 'info', 'muted']);

/**
 * Monta o HTML de um card de entidade.
 * @param {object} o
 * @param {string}  o.href         Detalhe da entidade: o card inteiro e o título abrem este link.
 * @param {string}  o.titulo       Texto do título (uma linha; completo na dica).
 * @param {boolean} [o.tituloMono] Título em fonte monoespaçada (identificadores como o CNJ).
 * @param {string} [o.estado]      Cor da borda esquerda: ok | danger | warn | info | muted.
 * @param {string} [o.classe]      Classes extras no card (ex.: "favorito").
 * @param {Object<string,string>} [o.dados]  Atributos data-* do card (ex.: { id } → data-id).
 * @param {string} [o.statusHtml]  Selo à direita do título (ex.: badge de status).
 * @param {string[]} [o.tagsHtml]  Selos da 2ª linha (nº, processo...). Classe "ent-encolhe" no
 *                                 selo que pode ser cortado com reticências quando faltar espaço.
 * @param {string} [o.descricao]   Linha de descrição (a linha é reservada mesmo vazia).
 * @param {{rotulo:string, valor?:string, dica?:string}[]} [o.campos]  Linhas "rótulo  valor" na
 *                                 largura do card, para textos (parte, vara, responsável...). Uma
 *                                 linha cada, com reticências; vazio mostra "—" (a linha não some,
 *                                 para manter o alinhamento entre cards).
 * @param {{rotulo:string, valor:string, perigo?:boolean, dica?:string, peso?:number}[]} [o.numeros]
 *                                 Grade de valores curtos (2–4: dinheiro, contagens), uma linha
 *                                 cada; a dica padrão é o próprio valor; "peso" alarga a coluna.
 * @param {{principal?:string, secundarioHtml?:string, progresso?:number}} [o.meta]  Rodapé à esquerda.
 * @param {string} [o.acoesHtml]   Ações secundárias no rodapé à direita (nunca "Ver"/"Editar").
 */
export function cardEntidade(o) {
  const estado = ESTADOS.has(o.estado) ? ` is-${o.estado}` : '';
  const classe = o.classe ? ` ${esc(o.classe)}` : '';
  const dados = Object.entries(o.dados ?? {})
    .map(([k, v]) => ` data-${k.replace(/[A-Z]/g, m => '-' + m.toLowerCase())}="${esc(v)}"`).join('');
  const numeros = o.numeros ?? [];
  const campos = o.campos ?? [];
  const meta = o.meta;
  const desc = o.descricao ? String(o.descricao) : '';
  const prog = meta && typeof meta.progresso === 'number'
    ? Math.max(0, Math.min(100, meta.progresso)) : null;
  return `
    <div class="ent-card${estado}${classe}" data-href="${esc(o.href)}"${dados}>
      <div class="ent-card-top">
        <div class="ent-card-titulo${o.tituloMono ? ' is-mono' : ''}"><a href="${esc(o.href)}" title="${esc(o.titulo)}">${esc(o.titulo)}</a></div>
        ${o.statusHtml ? `<div class="ent-card-status">${o.statusHtml}</div>` : ''}
      </div>
      ${o.tagsHtml ? `<div class="ent-card-tags">${o.tagsHtml.filter(Boolean).join('')}</div>` : ''}
      ${'descricao' in o ? `<div class="ent-card-desc"${desc ? ` title="${esc(desc)}"` : ''}>${desc ? esc(desc) : '&nbsp;'}</div>` : ''}
      ${campos.length ? `
        <div class="ent-card-campos">
          ${campos.map(c => {
            const vazio = c.valor == null || c.valor === '';
            return `<div class="ent-card-campo-lbl">${esc(c.rotulo)}</div>
            <div class="ent-card-campo-val${vazio ? ' is-vazio' : ''}"${vazio ? '' : ` title="${esc(c.dica ?? c.valor)}"`}>${vazio ? '—' : esc(c.valor)}</div>`;
          }).join('')}
        </div>` : ''}
      ${numeros.length ? `
        <div class="ent-card-nums" style="grid-template-columns:${numeros.map(n => `minmax(0,${Number(n.peso) > 0 ? Number(n.peso) : 1}fr)`).join(' ')}">
          ${numeros.map(n => `
            <div><div class="ent-card-num-lbl">${esc(n.rotulo)}</div>
            <div class="ent-card-num-val${n.perigo ? ' is-danger' : ''}" title="${esc(n.dica ?? n.valor)}">${esc(n.valor)}</div></div>`).join('')}
        </div>` : ''}
      ${meta || o.acoesHtml ? `
        <div class="ent-card-rodape">
          ${meta ? `
            <div class="ent-card-meta">
              <div class="ent-card-meta-linha">
                ${meta.principal ? `<b class="ent-card-meta-principal" title="${esc(meta.principal)}">${esc(meta.principal)}</b>` : '<span></span>'}
                ${meta.secundarioHtml ? `<span>${meta.secundarioHtml}</span>` : ''}
              </div>
              ${prog !== null ? `<div class="ent-card-prog"><span style="width:${prog.toFixed(0)}%"></span></div>` : ''}
            </div>` : ''}
          ${o.acoesHtml ? `<div class="ent-card-acoes">${o.acoesHtml}</div>` : ''}
        </div>` : ''}
    </div>`;
}

/**
 * Faz o clique em qualquer ponto de um card (.ent-card[data-href]) abrir o detalhe.
 * Links, botões e campos dentro do card mantêm o próprio comportamento; texto selecionado não
 * navega; Ctrl/⌘/Shift ou botão do meio abrem em nova aba. Chame uma vez por contêiner — os
 * cards podem ser re-renderizados à vontade (delegação de eventos).
 */
export function ativarCardsClicaveis(container) {
  if (!container || container.dataset.cardsClicaveis) return;
  container.dataset.cardsClicaveis = '1';
  const abrir = (e) => {
    const card = e.target.closest('.ent-card[data-href]');
    if (!card || !container.contains(card)) return;
    if (e.target.closest('a, button, input, select, textarea, label')) return;
    if (e.type === 'click' && String(window.getSelection?.() ?? '').length) return;
    if (e.ctrlKey || e.metaKey || e.shiftKey || e.button === 1) window.open(card.dataset.href, '_blank', 'noopener');
    else window.location.href = card.dataset.href;
  };
  container.addEventListener('click', abrir);
  container.addEventListener('auxclick', e => { if (e.button === 1) abrir(e); });
}
