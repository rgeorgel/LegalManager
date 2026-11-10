// Variação de layout "menu superior": no desktop a barra lateral some e a navegação entra no
// próprio cabeçalho, que fica preso no topo ao rolar (position: sticky), com um painel suspenso
// por grupo (ícone + descrição de cada página). Se logo + menu + ações não couberem numa linha,
// o menu desce para uma segunda linha do cabeçalho (classe .topnav-2linhas, medida no resize).
// É opcional, por usuário: escolhida em Configurações → Aparência, salva no banco
// (PUT /api/usuarios/me/menu-layout) e devolvida no login em usuario.menuLayout.
// Sem escolha (null) ou "lateral", nada aqui roda — o layout padrão fica intacto.
// No celular (<= 768px) o menu fica escondido e valem a barra inferior + gaveta de sempre.
//
// Também daqui: o menu da conta (iniciais no cabeçalho, nos DOIS layouts, só no desktop) e o
// convite único para experimentar o menu superior.
import { apiFetch } from './api.js';
import { esc } from './utils.js';

const KEY = 'menu_layout';

/** "superior" ou "lateral" (padrão). Banco (via sessão) primeiro; localStorage é só reserva. */
export function getMenuLayout() {
  let valor = null;
  try { valor = JSON.parse(sessionStorage.getItem('user') || 'null')?.menuLayout ?? null; } catch { /* sessão ilegível */ }
  if (!valor) {
    try { valor = localStorage.getItem(KEY); } catch { /* storage bloqueado */ }
  }
  return valor === 'superior' ? 'superior' : 'lateral';
}

/** Salva a escolha no servidor e, se der certo, na sessão e no localStorage. Rejeita se a API falhar. */
export async function setMenuLayout(layout) {
  const valor = layout === 'superior' ? 'superior' : 'lateral';
  await apiFetch('/usuarios/me/menu-layout', { method: 'PUT', body: { layout: valor } });
  try { localStorage.setItem(KEY, valor); } catch { /* storage bloqueado */ }
  try {
    const user = JSON.parse(sessionStorage.getItem('user') || 'null');
    if (user) sessionStorage.setItem('user', JSON.stringify({ ...user, menuLayout: valor }));
  } catch { /* sessão ilegível: o servidor já tem a escolha */ }
}

// Ícones em traço (24×24, stroke = currentColor) no lugar dos emojis do menu lateral.
const SVG = {
  painel: '<rect x="3" y="3" width="7" height="9" rx="1.5"/><rect x="14" y="3" width="7" height="5" rx="1.5"/><rect x="14" y="12" width="7" height="9" rx="1.5"/><rect x="3" y="16" width="7" height="5" rx="1.5"/>',
  balanca: '<path d="m16 16 3-8 3 8c-.87.65-1.92 1-3 1s-2.13-.35-3-1Z"/><path d="m2 16 3-8 3 8c-.87.65-1.92 1-3 1s-2.13-.35-3-1Z"/><path d="M7 21h10M12 3v18M3 7h2c2 0 5-1 7-2 2 1 5 2 7 2h2"/>',
  pessoas: '<path d="M16 21v-2a4 4 0 0 0-4-4H6a4 4 0 0 0-4 4v2"/><circle cx="9" cy="7" r="4"/><path d="M22 21v-2a4 4 0 0 0-3-3.87M16 3.13a4 4 0 0 1 0 7.75"/>',
  pasta: '<path d="M20 20a2 2 0 0 0 2-2V8a2 2 0 0 0-2-2h-7.9a2 2 0 0 1-1.69-.9L9.6 3.9A2 2 0 0 0 7.93 3H4a2 2 0 0 0-2 2v13a2 2 0 0 0 2 2Z"/>',
  jornal: '<path d="M4 22h16a2 2 0 0 0 2-2V4a2 2 0 0 0-2-2H8a2 2 0 0 0-2 2v16a2 2 0 0 1-2 2Zm0 0a2 2 0 0 1-2-2v-9c0-1.1.9-2 2-2h2"/><path d="M18 14h-8M15 18h-5M10 6h8v4h-8z"/>',
  carteirinha: '<rect x="3" y="5" width="18" height="14" rx="2"/><circle cx="9" cy="11" r="2"/><path d="M15 10h3M15 14h3M6 16c.5-1 1.6-1.5 3-1.5s2.5.5 3 1.5"/>',
  check: '<path d="m9 11 3 3L22 4"/><path d="M21 12v7a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h11"/>',
  calendario: '<rect x="3" y="4" width="18" height="18" rx="2"/><path d="M16 2v4M8 2v4M3 10h18"/>',
  relogio: '<circle cx="12" cy="12" r="9"/><path d="M12 7v5l3 2"/>',
  carteira: '<path d="M19 7V4a1 1 0 0 0-1-1H5a2 2 0 0 0 0 4h15a1 1 0 0 1 1 1v4h-3a2 2 0 0 0 0 4h3a1 1 0 0 0 1-1v-2a1 1 0 0 0-1-1"/><path d="M3 5v14a2 2 0 0 0 2 2h15a1 1 0 0 0 1-1v-4"/>',
  maleta: '<rect x="2" y="7" width="20" height="14" rx="2"/><path d="M16 21V5a2 2 0 0 0-2-2h-4a2 2 0 0 0-2 2v16"/>',
  calculadora: '<rect x="4" y="2" width="16" height="20" rx="2"/><path d="M8 6h8M16 14v4M16 10h.01M12 10h.01M8 10h.01M12 14h.01M8 14h.01M12 18h.01M8 18h.01"/>',
  capacete: '<path d="M2 18h20"/><path d="M4 18v-2a8 8 0 0 1 16 0v2"/><path d="M10 8V5h4v3"/>',
  cofre: '<rect x="3" y="5" width="18" height="15" rx="2"/><circle cx="12" cy="12.5" r="3.5"/><path d="M12 9v1M12 15v1M7 20v1M17 20v1"/>',
  casa: '<path d="m3 10 9-7 9 7"/><path d="M5 9v11h14V9"/><path d="M10 20v-6h4v6"/>',
  banco: '<path d="M3 21h18M4 10h16M12 3l9 5H3z"/><path d="M6 10v8M10 10v8M14 10v8M18 10v8"/>',
  grafico: '<path d="M3 3v18h18"/><path d="m7 8 4 4 3-3 5 6"/>',
  familia: '<circle cx="8" cy="6" r="3"/><circle cx="17" cy="9" r="2.2"/><path d="M3 21v-3a5 5 0 0 1 10 0v3"/><path d="M13.5 21v-2a3.5 3.5 0 0 1 7 0v2"/>',
  prazo: '<path d="M21 7.5V6a2 2 0 0 0-2-2H5a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h3.5"/><path d="M16 2v4M8 2v4M3 10h5"/><circle cx="16" cy="16" r="6"/><path d="M16 14v2l1 1"/>',
  sino: '<path d="M6 8a6 6 0 0 1 12 0c0 7 3 9 3 9H3s3-2 3-9"/><path d="M10.3 21a1.94 1.94 0 0 0 3.4 0"/>',
  ajustes: '<path d="M4 21v-7M4 10V3M12 21v-9M12 8V3M20 21v-5M20 12V3M1 14h6M9 8h6M17 16h6"/>',
  paleta: '<circle cx="13.5" cy="6.5" r="1"/><circle cx="17.5" cy="10.5" r="1"/><circle cx="8.5" cy="7.5" r="1"/><circle cx="6.5" cy="12.5" r="1"/><path d="M12 2C6.5 2 2 6.5 2 12s4.5 10 10 10c.93 0 1.65-.75 1.65-1.69 0-.44-.18-.84-.44-1.13-.29-.29-.44-.65-.44-1.13a1.64 1.64 0 0 1 1.67-1.67h2c3.05 0 5.55-2.5 5.55-5.55C21.97 6.01 17.46 2 12 2z"/>',
  brilho: '<path d="M12 3l1.9 5.8 5.8 1.9-5.8 1.9L12 18.4l-1.9-5.8-5.8-1.9 5.8-1.9z"/><path d="M5 3v4M3 5h4M19 17v4M17 19h4"/>',
  cartao: '<rect x="2" y="5" width="20" height="14" rx="2"/><path d="M2 10h20"/>',
  layoutLateral: '<rect x="3" y="3" width="18" height="18" rx="2"/><path d="M9 3v18"/>',
  layoutTopo: '<rect x="3" y="3" width="18" height="18" rx="2"/><path d="M3 9h18"/>',
  sair: '<path d="M9 21H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h4"/><path d="m16 17 5-5-5-5M21 12H9"/>',
  fechar: '<path d="M18 6 6 18M6 6l12 12"/>',
  seta: '<path d="m6 9 6 6 6-6"/>',
};
const icone = (nome, classe = 'topnav-ico') =>
  `<svg class="${classe}" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">${SVG[nome] ?? ''}</svg>`;

// Ícone e uma linha de descrição por página (chave = href do NAV_GROUPS de layout.js).
const DETALHES = {
  '/pages/dashboard.html':             { icone: 'painel',      desc: 'Visão geral do escritório' },
  '/pages/processos.html':             { icone: 'balanca',     desc: 'Andamentos e movimentações' },
  '/pages/contatos.html':              { icone: 'pessoas',     desc: 'Clientes, partes e parceiros' },
  '/pages/documentos.html':            { icone: 'pasta',       desc: 'Arquivos e modelos de peças' },
  '/pages/publicacoes.html':           { icone: 'jornal',      desc: 'Intimações dos diários oficiais' },
  '/pages/oabs.html':                  { icone: 'carteirinha', desc: 'Captura automática por OAB' },
  '/pages/tarefas.html':               { icone: 'check',       desc: 'Prazos e pendências da equipe' },
  '/pages/agenda.html':                { icone: 'calendario',  desc: 'Audiências, reuniões e eventos' },
  '/pages/timesheet.html':             { icone: 'relogio',     desc: 'Horas trabalhadas por caso' },
  '/pages/financeiro.html':            { icone: 'carteira',    desc: 'Receitas, despesas e caixa' },
  '/pages/honorarios-contratos.html':  { icone: 'maleta',      desc: 'Contratos e parcelas' },
  '/pages/honorarios.html':            { icone: 'calculadora', desc: 'Calcule valores de honorários' },
  '/pages/calculadora-prazos.html':    { icone: 'prazo',       desc: 'Contagem de prazos processuais' },
  '/pages/calculadora-pensao.html':    { icone: 'familia',     desc: 'Débito de alimentos e estimativa' },
  '/pages/calculadora-trabalhista.html': { icone: 'capacete', desc: 'Rescisão e pedidos da reclamação' },
  '/pages/calculadora-fgts.html':      { icone: 'cofre',       desc: 'FGTS devido, multa e revisão' },
  '/pages/calculadora-aluguel.html':   { icone: 'casa',        desc: 'Reajuste, débito e multa rescisória' },
  '/pages/calculadora-revisional.html': { icone: 'banco',     desc: 'Juros abusivos × taxa média do BCB' },
  '/pages/calculadora-rmc.html':       { icone: 'cartao',      desc: 'Cartão consignado do INSS' },
  '/pages/calculadora-superendividamento.html': { icone: 'grafico', desc: 'Plano de pagamento (Lei 14.181)' },
  '/pages/alertas.html':               { icone: 'sino',        desc: 'Notificações do sistema' },
  '/pages/configuracoes.html':         { icone: 'ajustes',     desc: 'Escritório, usuários e preferências' },
  '/pages/tema.html':                  { icone: 'paleta',      desc: 'Cores e logo do escritório' },
  '/pages/assistente.html':            { icone: 'brilho',      desc: 'Mascote do assistente de IA' },
  '/pages/assinatura.html':            { icone: 'cartao',      desc: 'Plano e pagamentos' },
};

// "⚖️ Processos" → "Processos" quando o item ganha ícone próprio (emoji é a 1ª "palavra").
const semEmoji = (label) => label.replace(/^\S+\s+/, '');

let _desligar = null;

/**
 * Monta (ou remonta) o menu dentro do `.header`, entre o logo e as ações do usuário.
 * @param {{ grupos: Array<{label: string, items: Array<{href: string, label: string,
 *   bloqueado?: boolean, upgrade?: string, selo?: string}>}>, atual: string,
 *   aoBloqueado: (upgrade: string) => void }} opts
 */
export function montarMenuSuperior({ grupos, atual, aoBloqueado }) {
  removerMenuSuperior();
  const header = document.querySelector('.app-layout > .header');
  if (!header) return;

  const nav = document.createElement('nav');
  nav.id = 'menuSuperior';
  nav.className = 'topnav';
  nav.setAttribute('aria-label', 'Menu principal');

  const itemPainel = (item) => {
    const det = DETALHES[item.href];
    const ativo = item.href === atual;
    const rotulo = det ? semEmoji(item.label) : item.label;
    const selo = item.bloqueado ? `<span class="topnav-selo">${esc(item.selo)}</span>` : '';
    const attrs = item.bloqueado
      ? `href="#" data-locked="true" data-upgrade="${esc(item.upgrade)}" title="Disponível no plano ${esc(item.upgrade)}"`
      : `href="${esc(item.href)}"${ativo ? ' aria-current="page"' : ''}`;
    return `<a ${attrs} class="topnav-item${ativo ? ' active' : ''}${item.bloqueado ? ' bloqueado' : ''}">
      ${det ? `<span class="topnav-item-ico">${icone(det.icone)}</span>` : ''}
      <span class="topnav-item-txt">
        <span class="topnav-item-titulo">${esc(rotulo)}${selo}</span>
        ${det ? `<span class="topnav-item-desc">${esc(det.desc)}</span>` : ''}
      </span>
    </a>`;
  };

  nav.innerHTML = `<ul class="topnav-lista">${grupos.map((g, i) => {
    // Grupo de um item só (Visão Geral → Dashboard) vira link direto, sem painel.
    if (g.items.length === 1) {
      const it = g.items[0];
      const ativo = it.href === atual;
      return `<li class="topnav-grupo"><a href="${esc(it.href)}" class="topnav-botao${ativo ? ' ativo' : ''}"${ativo ? ' aria-current="page"' : ''}>${esc(DETALHES[it.href] ? semEmoji(it.label) : it.label)}</a></li>`;
    }
    const ativo = g.items.some(it => it.href === atual);
    const painelId = `topnavPainel${i}`;
    return `<li class="topnav-grupo">
      <button type="button" class="topnav-botao${ativo ? ' ativo' : ''}" aria-expanded="false" aria-controls="${painelId}">
        ${esc(g.label)}${icone('seta', 'topnav-seta')}
      </button>
      <div class="topnav-painel${g.items.length > 3 ? ' duas-colunas' : ''}" id="${painelId}">
        ${g.items.map(itemPainel).join('')}
      </div>
    </li>`;
  }).join('')}</ul>`;

  header.insertBefore(nav, header.querySelector('.header-spacer'));
  _desligar = ligarComportamento(header, nav, aoBloqueado);
}

export function removerMenuSuperior() {
  _desligar?.();
  _desligar = null;
  document.getElementById('menuSuperior')?.remove();
  document.body.classList.remove('topnav-2linhas', 'topnav-rolado');
}

// ── Menu da conta ───────────────────────────────────────────────────────
// As iniciais (com nome/escritório ao lado) abrem um painel com a posição do menu, Configurações e
// Sair. Vale nos dois layouts (barra lateral e menu superior), só no desktop: no celular o CSS
// esconde o menu e o cabeçalho segue com os ícones + "Sair" de sempre. Substitui o texto
// "nome | escritório" e o botão Sair (que continua no DOM — é ele que o item "Sair" aciona).
// Montado uma vez por página (initLayout); a troca de layout só atualiza a opção marcada.

/**
 * @param {{ usuario?: {nome?: string, nomeEscritorio?: string},
 *   aoTrocarLayout: (layout: 'lateral'|'superior', origem: string) => Promise<void> }} opts
 */
export function montarMenuConta({ usuario, aoTrocarLayout }) {
  if (document.getElementById('menuConta')) return;
  const header = document.querySelector('.app-layout > .header');
  const alvo = header?.querySelector('.header-user-name');
  if (!alvo) return;
  const nome = usuario?.nome ?? '';
  const escritorio = usuario?.nomeEscritorio ?? '';
  const partes = nome.split(/\s+/).filter(Boolean);
  const iniciais = ((partes[0]?.[0] ?? '') + (partes.length > 1 ? partes[partes.length - 1][0] : '')).toUpperCase() || '?';
  const layoutAtual = getMenuLayout();
  const opcao = (layout, rotulo, ico) => {
    const ativo = layout === layoutAtual;
    return `<button type="button" role="radio" aria-checked="${ativo}" data-layout="${layout}"
      class="topnav-seg-opcao${ativo ? ' ativo' : ''}">${icone(ico)}${rotulo}</button>`;
  };

  const el = document.createElement('div');
  el.id = 'menuConta';
  el.className = 'topnav-conta';
  el.innerHTML = `
    <button type="button" class="topnav-perfil" aria-haspopup="true" aria-expanded="false"
      aria-controls="menuContaPainel" title="Sua conta">
      <span class="topnav-avatar" aria-hidden="true">${esc(iniciais)}</span>
      <span class="topnav-perfil-txt"><span class="topnav-perfil-nome">${esc(nome)}</span>
        <span class="topnav-perfil-escritorio">${esc(escritorio)}</span></span>
      ${icone('seta', 'topnav-seta')}
    </button>
    <div class="topnav-conta-painel" id="menuContaPainel">
      <div class="topnav-conta-topo">
        <span class="topnav-avatar grande" aria-hidden="true">${esc(iniciais)}</span>
        <span class="topnav-perfil-txt"><span class="topnav-perfil-nome">${esc(nome)}</span>
          <span class="topnav-perfil-escritorio">${esc(escritorio)}</span></span>
      </div>
      <div class="topnav-conta-secao">
        <div class="topnav-conta-rotulo">Posição do menu</div>
        <div class="topnav-seg" role="radiogroup" aria-label="Posição do menu">
          ${opcao('lateral', 'Barra lateral', 'layoutLateral')}${opcao('superior', 'Menu superior', 'layoutTopo')}
        </div>
        <div class="topnav-conta-erro" role="alert" hidden></div>
      </div>
      <a class="topnav-conta-link" href="/pages/configuracoes.html">${icone('ajustes')}Configurações</a>
      <button type="button" class="topnav-conta-link" data-acao="sair">${icone('sair')}Sair</button>
    </div>`;
  alvo.before(el);
  document.body.classList.add('com-menu-conta');

  const botao = el.querySelector('.topnav-perfil');
  const erro = el.querySelector('.topnav-conta-erro');
  const abrir = () => {
    // Fecha os painéis do menu (eles fecham sozinhos no clique fora, mas o foco pode vir do teclado).
    document.querySelectorAll('#menuSuperior .topnav-grupo.aberto').forEach(g => g.classList.remove('aberto'));
    removerDica();
    el.classList.add('aberto');
    botao.setAttribute('aria-expanded', 'true');
  };
  const fechar = () => {
    el.classList.remove('aberto');
    botao.setAttribute('aria-expanded', 'false');
  };

  botao.addEventListener('click', () => (el.classList.contains('aberto') ? fechar() : abrir()));
  el.addEventListener('keydown', e => {
    if (e.key === 'Escape' && el.classList.contains('aberto')) { fechar(); botao.focus(); }
  });
  el.addEventListener('focusout', e => { if (!el.contains(e.relatedTarget)) fechar(); });
  el.querySelector('[data-acao="sair"]').addEventListener('click', () => document.getElementById('logoutBtn')?.click());

  el.querySelectorAll('.topnav-seg-opcao').forEach(op => op.addEventListener('click', async () => {
    if (op.classList.contains('ativo') || !aoTrocarLayout) return;
    erro.hidden = true;
    el.querySelectorAll('.topnav-seg-opcao').forEach(b => { b.disabled = true; });
    try {
      await aoTrocarLayout(op.dataset.layout, 'menu_conta');
      fechar();
    } catch {
      erro.textContent = 'Não foi possível salvar. Tente novamente.';
      erro.hidden = false;
    } finally {
      el.querySelectorAll('.topnav-seg-opcao').forEach(b => { b.disabled = false; });
    }
  }));

  document.addEventListener('click', (e) => { if (!el.contains(e.target)) fechar(); });
}

/** Marca no menu da conta a posição do menu em uso (chamado a cada troca de layout). */
export function atualizarMenuConta(layout) {
  document.querySelectorAll('#menuConta .topnav-seg-opcao').forEach(b => {
    const ativo = b.dataset.layout === layout;
    b.classList.toggle('ativo', ativo);
    b.setAttribute('aria-checked', String(ativo));
  });
}

/** Balão sob as iniciais (ex.: logo depois de experimentar o menu, "para voltar, clique aqui"). */
export function destacarConta(texto, ms = 8000) {
  const conta = document.getElementById('menuConta');
  if (!conta || getComputedStyle(conta).display === 'none') return;
  removerDica();
  const dica = document.createElement('div');
  dica.id = 'menuSuperiorDica';
  dica.className = 'topnav-dica';
  dica.setAttribute('role', 'status');
  dica.textContent = texto;
  conta.appendChild(dica);
  conta.querySelector('.topnav-perfil')?.classList.add('pulsando');
  dica.addEventListener('click', removerDica);
  dica._timer = setTimeout(removerDica, ms);
}

function removerDica() {
  const dica = document.getElementById('menuSuperiorDica');
  if (dica) { clearTimeout(dica._timer); dica.remove(); }
  document.querySelector('#menuConta .topnav-perfil')?.classList.remove('pulsando');
}

function ligarComportamento(header, nav, aoBloqueado) {
  const grupos = [...nav.querySelectorAll('.topnav-grupo')].filter(g => g.querySelector('.topnav-painel'));
  // Com mouse, o painel abre ao passar por cima; o clique "fixa" o painel aberto (e um segundo
  // clique fecha). No toque não há hover: o clique abre e fecha.
  const comHover = window.matchMedia?.('(hover: hover) and (pointer: fine)').matches;
  let fixado = null;
  let timer = null;

  const abrir = (g) => {
    clearTimeout(timer);
    grupos.forEach(o => { if (o !== g) fechar(o); });
    const painel = g.querySelector('.topnav-painel');
    painel.classList.remove('a-direita');
    g.classList.add('aberto');
    g.querySelector('.topnav-botao').setAttribute('aria-expanded', 'true');
    // Painel largo perto da borda direita: alinha pela direita do botão para não sair da tela.
    if (painel.getBoundingClientRect().right > window.innerWidth - 12) painel.classList.add('a-direita');
  };
  const fechar = (g) => {
    g.classList.remove('aberto');
    g.querySelector('.topnav-botao').setAttribute('aria-expanded', 'false');
    if (fixado === g) fixado = null;
  };
  const fecharTodos = () => grupos.forEach(fechar);

  grupos.forEach(g => {
    const botao = g.querySelector('.topnav-botao');
    botao.addEventListener('click', () => {
      if (g.classList.contains('aberto') && (fixado === g || !comHover)) { fechar(g); return; }
      abrir(g);
      fixado = g;
    });
    botao.addEventListener('keydown', e => {
      if (e.key === 'ArrowDown') {
        e.preventDefault();
        abrir(g);
        fixado = g;
        g.querySelector('.topnav-painel a')?.focus();
      }
    });
    if (comHover) {
      g.addEventListener('mouseenter', () => { if (!fixado || fixado === g) abrir(g); });
      g.addEventListener('mouseleave', () => {
        if (fixado === g) return;
        clearTimeout(timer);
        timer = setTimeout(() => fechar(g), 150);
      });
    }
    // Foco saiu do grupo (Tab) → fecha.
    g.addEventListener('focusout', e => { if (!g.contains(e.relatedTarget)) fechar(g); });
  });

  nav.addEventListener('keydown', e => {
    if (e.key !== 'Escape') return;
    const aberto = grupos.find(g => g.classList.contains('aberto'));
    if (!aberto) return;
    fechar(aberto);
    aberto.querySelector('.topnav-botao').focus();
  });

  nav.querySelectorAll('a[data-locked]').forEach(a => a.addEventListener('click', e => {
    e.preventDefault();
    fecharTodos();
    aoBloqueado(a.dataset.upgrade || 'Pro');
  }));

  const foraDoMenu = (e) => { if (!nav.contains(e.target)) fecharTodos(); };
  document.addEventListener('click', foraDoMenu);

  // Sombra do cabeçalho só depois que a página rola.
  const aoRolar = () => document.body.classList.toggle('topnav-rolado', window.scrollY > 4);
  window.addEventListener('scroll', aoRolar, { passive: true });
  aoRolar();

  // Uma linha (logo · menu · ações) quando cabe; senão o menu desce para a 2ª linha. Mede sem
  // a classe (layout síncrono, sem pintar no meio) para poder voltar a uma linha ao alargar.
  // Observa o logo e as ações (o sino, o suporte etc. entram depois deste menu; o logo do
  // escritório carrega depois) — a largura delas não muda com a troca de linha, então não há laço.
  let quadro = 0;
  const medir = () => {
    cancelAnimationFrame(quadro);
    quadro = requestAnimationFrame(() => {
      const body = document.body;
      body.classList.remove('topnav-2linhas');
      if (header.scrollWidth > header.clientWidth + 1) body.classList.add('topnav-2linhas');
    });
  };
  const ro = typeof ResizeObserver === 'function' ? new ResizeObserver(medir) : null;
  header.querySelectorAll('.header-logo, .header-user').forEach(el => ro?.observe(el));
  window.addEventListener('resize', medir);
  medir();

  return () => {
    document.removeEventListener('click', foraDoMenu);
    window.removeEventListener('scroll', aoRolar);
    window.removeEventListener('resize', medir);
    ro?.disconnect();
    cancelAnimationFrame(quadro);
    clearTimeout(timer);
  };
}

// ── Convite único para experimentar o menu superior ─────────────────────
// Cartão discreto no canto inferior, ao lado da barra lateral, para quem nunca escolheu a posição
// do menu (usuario.menuLayout vazio). "Agora não" grava "lateral" na conta, então o convite não
// volta em nenhum dispositivo; o localStorage cobre o caso de a API falhar.
const CONVITE_KEY = 'menu_convite_visto';

function jaEscolheuMenu() {
  try {
    if (JSON.parse(sessionStorage.getItem('user') || 'null')?.menuLayout) return true;
  } catch { /* sessão ilegível: segue para o localStorage */ }
  try { return !!(localStorage.getItem(KEY) || localStorage.getItem(CONVITE_KEY)); } catch { return true; }
}

// Não disputa a tela com tour guiado, modais (boas-vindas, pesquisas) ou o painel de novidades.
function telaOcupada() {
  try { if (sessionStorage.getItem('tour_active')) return true; } catch { /* sem storage */ }
  return !!document.querySelector('.modal-overlay.open, #novidadesOverlay, .tour-overlay')
    || document.body.classList.contains('impersonating');
}

/**
 * Agenda o convite (desktop largo, só para quem nunca escolheu). Tenta algumas vezes se a tela
 * estiver ocupada e desiste em silêncio.
 * @param {{ aoExperimentar: () => Promise<void>, aoRecusar: () => Promise<void> }} acoes
 */
export function agendarConviteMenuSuperior({ aoExperimentar, aoRecusar }, { espera = 3000, tentativas = 5 } = {}) {
  if (jaEscolheuMenu() || !window.matchMedia?.('(min-width: 1024px)').matches) return;
  let restantes = tentativas;
  const tentar = () => {
    if (jaEscolheuMenu() || document.getElementById('menuConvite')) return;
    if (telaOcupada()) { if (--restantes > 0) setTimeout(tentar, 5000); return; }
    mostrarConvite({ aoExperimentar, aoRecusar });
  };
  setTimeout(tentar, espera);
}

function mostrarConvite({ aoExperimentar, aoRecusar }) {
  const el = document.createElement('div');
  el.id = 'menuConvite';
  el.className = 'menu-convite';
  el.setAttribute('role', 'dialog');
  el.setAttribute('aria-labelledby', 'menuConviteTitulo');
  el.innerHTML = `
    <button type="button" class="menu-convite-fechar" aria-label="Fechar">${icone('fechar')}</button>
    <div class="menu-convite-previas" aria-hidden="true">
      <span class="menu-convite-previa lateral"><i></i><i></i></span>
      <svg class="menu-convite-seta" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M5 12h14M13 6l6 6-6 6"/></svg>
      <span class="menu-convite-previa topo"><i></i><i></i></span>
    </div>
    <div class="menu-convite-selo">Novidade</div>
    <div class="menu-convite-titulo" id="menuConviteTitulo">Experimente o menu no topo</div>
    <p class="menu-convite-texto">A tela fica mais limpa e com mais espaço: o menu vai para o topo e continua
      visível ao rolar. Dá para voltar quando quiser, pelas suas iniciais.</p>
    <div class="menu-convite-acoes">
      <button type="button" class="btn btn-primary btn-sm" data-acao="experimentar">Experimentar</button>
      <button type="button" class="btn btn-secondary btn-sm" data-acao="recusar">Agora não</button>
    </div>
    <div class="menu-convite-erro" role="alert" hidden></div>`;
  document.body.appendChild(el);
  requestAnimationFrame(() => el.classList.add('visivel'));

  const fechar = () => {
    el.classList.remove('visivel');
    setTimeout(() => el.remove(), 200);
  };
  const recusar = () => {
    try { localStorage.setItem(CONVITE_KEY, '1'); } catch { /* storage bloqueado */ }
    fechar();
    aoRecusar().catch(() => { /* o localStorage já evita repetir neste navegador */ });
  };
  el.querySelector('.menu-convite-fechar').addEventListener('click', recusar);
  el.querySelector('[data-acao="recusar"]').addEventListener('click', recusar);
  el.querySelector('[data-acao="experimentar"]').addEventListener('click', async (e) => {
    const botao = e.currentTarget;
    botao.disabled = true;
    try {
      await aoExperimentar();
      fechar();
      destacarConta('Pronto! Para voltar à barra lateral, clique aqui.');
    } catch {
      botao.disabled = false;
      const erro = el.querySelector('.menu-convite-erro');
      erro.textContent = 'Não foi possível ativar agora. Tente novamente.';
      erro.hidden = false;
    }
  });
}
