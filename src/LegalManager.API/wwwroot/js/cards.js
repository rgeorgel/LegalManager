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
 * @param {string} [o.href]        Detalhe da entidade: o card inteiro e o título abrem este link.
 *                                 Sem href (entidade sem detalhe, ex.: publicação), o card não é clicável.
 * @param {boolean} [o.abrivel]    Sem href mas clicável (abre um modal via ativarCardsClicaveis({ abrir })),
 *                                 ex.: modelo de documento. Também abre com Enter (o card recebe foco).
 * @param {string}  o.titulo       Texto do título (uma linha; completo na dica).
 * @param {boolean} [o.tituloMono] Título em fonte monoespaçada (identificadores como o CNJ).
 * @param {boolean} [o.tituloCompleto] Título quebra linha e aparece inteiro (quando o título é o
 *                                 conteúdo, como o texto de uma tarefa). Abre mão do alinhamento.
 * @param {string} [o.estado]      Cor da borda esquerda: ok | danger | warn | info | muted.
 * @param {string} [o.classe]      Classes extras no card (ex.: "favorito").
 * @param {Object<string,string>} [o.dados]  Atributos data-* do card (ex.: { id } → data-id).
 * @param {string} [o.capaHtml]    Faixa de imagem no topo do card, de borda a borda (ex.: miniatura
 *                                 do documento). Altura fixa (.ent-card-capa) para alinhar vizinhos.
 * @param {string} [o.prefixoHtml] Antes do título (ex.: caixa de seleção para ações em massa).
 * @param {string} [o.statusHtml]  Selo à direita do título (ex.: badge de status). Um menu de
 *                                 ações (⋮, classe "ent-card-menu") vai aqui, não no rodapé.
 * @param {string[]} [o.tagsHtml]  Selos da 2ª linha (nº, processo...). Classe "ent-encolhe" no
 *                                 selo que pode ser cortado com reticências quando faltar espaço.
 * @param {string} [o.descricao]   Linha de descrição (a linha é reservada mesmo vazia).
 * @param {{rotulo:string, valor?:string, valorHtml?:string, dica?:string, perigo?:boolean}[]} [o.campos]  Linhas "rótulo  valor" na
 *                                 largura do card, para textos (parte, vara, responsável...). Uma
 *                                 linha cada, com reticências; vazio mostra "—" (a linha não some,
 *                                 para manter o alinhamento entre cards). `valorHtml` (ex.: link
 *                                 para o processo) entra como está, no lugar de `valor`.
 * @param {{rotulo:string, valor:string, perigo?:boolean, dica?:string, peso?:number}[]} [o.numeros]
 *                                 Grade de valores curtos (2–4: dinheiro, contagens), uma linha
 *                                 cada; a dica padrão é o próprio valor; "peso" alarga a coluna.
 * @param {string} [o.corpoHtml]   Conteúdo livre depois dos campos/valores (ex.: texto de uma
 *                                 publicação). Use com parcimônia: o padrão são faixas fixas.
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
    <div class="ent-card${estado}${classe}"${o.href ? ` data-href="${esc(o.href)}"` : o.abrivel ? ' data-abrir="1" tabindex="0"' : ''}${dados}>
      ${o.capaHtml ? `<div class="ent-card-capa">${o.capaHtml}</div>` : ''}
      <div class="ent-card-top">
        ${o.prefixoHtml ? `<div class="ent-card-prefixo">${o.prefixoHtml}</div>` : ''}
        <div class="ent-card-titulo${o.tituloMono ? ' is-mono' : ''}${o.tituloCompleto ? ' is-completo' : ''}">${o.href ? `<a href="${esc(o.href)}" title="${esc(o.titulo)}">${esc(o.titulo)}</a>` : `<span title="${esc(o.titulo)}">${esc(o.titulo)}</span>`}</div>
        ${o.statusHtml ? `<div class="ent-card-status">${o.statusHtml}</div>` : ''}
      </div>
      ${o.tagsHtml ? `<div class="ent-card-tags">${o.tagsHtml.filter(Boolean).join('')}</div>` : ''}
      ${'descricao' in o ? `<div class="ent-card-desc${desc ? '' : ' is-vazia'}"${desc ? ` title="${esc(desc)}"` : ''}>${desc ? esc(desc) : '&nbsp;'}</div>` : ''}
      ${campos.length ? `
        <div class="ent-card-campos">
          ${campos.map(c => {
            const vazio = !c.valorHtml && (c.valor == null || c.valor === '');
            const dica = c.dica ?? c.valor;
            return `<div class="ent-card-campo-lbl">${esc(c.rotulo)}</div>
            <div class="ent-card-campo-val${vazio ? ' is-vazio' : c.perigo ? ' is-danger' : ''}"${!vazio && dica ? ` title="${esc(dica)}"` : ''}>${vazio ? '—' : c.valorHtml || esc(c.valor)}</div>`;
          }).join('')}
        </div>` : ''}
      ${numeros.length ? `
        <div class="ent-card-nums" style="grid-template-columns:${numeros.map(n => `minmax(0,${Number(n.peso) > 0 ? Number(n.peso) : 1}fr)`).join(' ')}">
          ${numeros.map(n => `
            <div><div class="ent-card-num-lbl">${esc(n.rotulo)}</div>
            <div class="ent-card-num-val${n.perigo ? ' is-danger' : ''}" title="${esc(n.dica ?? n.valor)}">${esc(n.valor)}</div></div>`).join('')}
        </div>` : ''}
      ${o.corpoHtml ? `<div class="ent-card-corpo">${o.corpoHtml}</div>` : ''}
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
 * navega; Ctrl/⌘/Shift ou botão do meio abrem o href em nova aba. Chame uma vez por contêiner —
 * os cards podem ser re-renderizados à vontade (delegação de eventos).
 *
 * Entidade sem página própria (abre num modal, como a tarefa): passe `abrir(card)`. O clique
 * simples no card ou no título chama `abrir` em vez de navegar; o href continua valendo para
 * nova aba (use uma URL que abra o modal, ex.: `?abrirId=`).
 * @param {HTMLElement} container
 * @param {{abrir?: (card: HTMLElement, e: MouseEvent) => void}} [opcoes]
 */
export function ativarCardsClicaveis(container, { abrir } = {}) {
  if (!container || container.dataset.cardsClicaveis) return;
  container.dataset.cardsClicaveis = '1';
  const SELETOR = '.ent-card[data-href], .ent-card[data-abrir]';
  const aoClicar = (e) => {
    const card = e.target.closest(SELETOR);
    if (!card || !container.contains(card)) return;
    const noTitulo = e.target.closest('.ent-card-titulo a');
    if (!noTitulo && e.target.closest('a, button, input, select, textarea, label, summary')) return;
    if (e.type === 'click' && String(window.getSelection?.() ?? '').length) return;
    const href = card.dataset.href;
    const novaAba = e.ctrlKey || e.metaKey || e.shiftKey || e.button === 1;
    if (noTitulo && (novaAba || !abrir)) return; // o próprio link resolve
    if (novaAba && href) { window.open(href, '_blank', 'noopener'); return; }
    if (e.button === 1) return; // sem href não há nova aba
    if (abrir) { e.preventDefault(); abrir(card, e); return; }
    if (href) window.location.href = href;
  };
  container.addEventListener('click', aoClicar);
  container.addEventListener('auxclick', e => { if (e.button === 1) aoClicar(e); });
  // Card sem link (data-abrir) recebe foco: Enter abre, como num botão.
  container.addEventListener('keydown', e => {
    if (e.key !== 'Enter' || !abrir || !e.target.matches?.('.ent-card[data-abrir]')) return;
    e.preventDefault();
    abrir(e.target, e);
  });
}

// ── Menu de ações do card (⋮) ─────────────────────────────────────────────
// Botão no statusHtml: `<button type="button" class="ent-card-menu" aria-haspopup="menu"
// aria-expanded="false" data-...>⋮</button>`; no clique, chame abrirMenuCard(botao, itens).
let menuAberto = null;

/** Fecha o menu ⋮ aberto, se houver. */
export function fecharMenuCard() {
  if (!menuAberto) return;
  const { menu, botao, limpar } = menuAberto;
  menuAberto = null;
  limpar();
  menu.remove();
  botao.setAttribute('aria-expanded', 'false');
}

/**
 * Abre o menu de ações junto ao botão ⋮ (abaixo e alinhado à direita; acima, se faltar espaço).
 * Fecha com clique fora, Esc, rolagem ou ao escolher um item. Clicar de novo no mesmo botão fecha.
 * @param {HTMLElement} botao
 * @param {({rotulo:string, icone?:string, perigo?:boolean, acao:() => void}|false|null)[]} itens
 *        Itens falsy são ignorados (facilita itens condicionais).
 */
export function abrirMenuCard(botao, itens) {
  const mesmo = menuAberto?.botao === botao;
  fecharMenuCard();
  const validos = itens.filter(Boolean);
  if (mesmo || !validos.length) return;
  const menu = document.createElement('div');
  menu.className = 'ent-menu';
  menu.setAttribute('role', 'menu');
  for (const it of validos) {
    const b = document.createElement('button');
    b.type = 'button';
    b.className = 'ent-menu-item' + (it.perigo ? ' is-perigo' : '');
    b.setAttribute('role', 'menuitem');
    b.innerHTML = `<span class="ent-menu-icone" aria-hidden="true">${it.icone ?? ''}</span><span>${esc(it.rotulo)}</span>`;
    b.addEventListener('click', e => { e.stopPropagation(); fecharMenuCard(); it.acao(); });
    menu.appendChild(b);
  }
  document.body.appendChild(menu);

  const r = botao.getBoundingClientRect();
  const w = menu.offsetWidth, h = menu.offsetHeight;
  const left = Math.max(8, Math.min(r.right - w, window.innerWidth - w - 8));
  const top = r.bottom + 4 + h > window.innerHeight - 8 ? Math.max(8, r.top - 4 - h) : r.bottom + 4;
  menu.style.left = `${left}px`;
  menu.style.top = `${top}px`;
  botao.setAttribute('aria-expanded', 'true');

  const fora = e => { if (!menu.contains(e.target) && !botao.contains(e.target)) fecharMenuCard(); };
  const tecla = e => { if (e.key === 'Escape') { fecharMenuCard(); botao.focus(); } };
  const fechar = () => fecharMenuCard();
  setTimeout(() => {
    if (menuAberto?.menu !== menu) return;
    document.addEventListener('click', fora, true);
    document.addEventListener('keydown', tecla);
    window.addEventListener('scroll', fechar, true);
    window.addEventListener('resize', fechar);
  }, 0);
  menuAberto = {
    menu, botao,
    limpar: () => {
      document.removeEventListener('click', fora, true);
      document.removeEventListener('keydown', tecla);
      window.removeEventListener('scroll', fechar, true);
      window.removeEventListener('resize', fechar);
    },
  };
  menu.querySelector('button')?.focus({ preventScroll: true });
}
