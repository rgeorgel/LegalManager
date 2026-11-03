// Publicidade (Adsterra) para escritórios no plano Free. Liga/desliga no .env (ADSTERRA_ENABLED);
// quem vê é decidido pelo servidor em GET /api/publicidade (plano lido do banco). Planos pagos
// nem chegam a perguntar.
//
// Cada anúncio é um iframe sandbox de /api/publicidade/anuncio/{formato}, servido pelo host
// próprio dos anúncios (cfg.origem, ADSTERRA_FRAME_ORIGIN) — outra origem, então o script do
// anunciante não alcança o token da sessão (sessionStorage) nem o DOM do app. Sem host próprio, o
// iframe vem do app sem allow-same-origin (origem opaca). O formato é escolhido pelo espaço livre do bloco
// (e trocado se ele mudar, ex.: alternar lista/grade), entre os configurados no servidor.
//
// Anúncios próprios do Causify (cfg.proprios, cadastrados no super admin) entram de dois jeitos:
// - sorteio: cfg.percentualProprios % dos blocos já nascem com um próprio, sem iframe do Adsterra
//   (mistura ajustada no super admin);
// - vazio: o Adsterra não desenhou nada (bloqueador de anúncios, sem campanha) — a página do iframe
//   avisa { pubEstado: 'vazio' }, ou o iframe nem responde (bloqueado) até ESPERA_MS depois de visível.
// Impressões (anúncio próprio na tela) e cliques vão para o servidor, que soma por anúncio e dia
// para o super admin acompanhar o desempenho de cada um.
import { apiFetch } from './api.js';
import { isPlanoFree } from './layout.js';
import { trackEvent } from './analytics.js';
import { criarAnuncioProprio, pintarMoldura } from './anuncio-proprio.js';

// Mesmas permissões do CSP da resposta (Adsterra.Sandbox). allow-same-origin só no host próprio:
// na origem do app ele desfaria o isolamento.
const SANDBOX = 'allow-scripts allow-popups allow-popups-to-escape-sandbox allow-top-navigation-by-user-activation';

// A página do iframe espera até 5 s depois do load antes de dizer 'vazio'; folga para carregar.
const ESPERA_MS = 10000;

// Impressões juntas num envio só (vários blocos aparecem quase ao mesmo tempo).
const ENVIO_MS = 2000;

const TAMANHOS = {
  '300x250': [300, 250],
  '728x90': [728, 90],
  '468x60': [468, 60],
  '320x50': [320, 50],
  nativo: [null, 250], // largura do bloco; a altura vem do próprio anúncio (postMessage)
};

// Ordem de preferência por tipo de espaço; vale o primeiro configurado que cabe na largura.
const PREFERENCIA = {
  bloco: ['300x250', 'nativo', '320x50'],
  nativo: ['nativo', '300x250', '320x50'],
  faixa: ['728x90', '468x60', '320x50', '300x250'],
  auto: ['728x90', '300x250', '320x50'],
};

let config = null;
// Ações de cada bloco ao receber o estado da sua página de anúncio (ver ouvinte de 'message').
const estados = new WeakMap();
// Anúncios próprios ainda não usados nesta página: blocos vizinhos mostram anúncios diferentes.
let fila = [];
// Eventos de anúncio próprio ainda não enviados ao servidor.
const pendentes = { impressoes: [], cliques: [] };
let envio = null;

function carregarConfig() {
  config ??= !isPlanoFree()
    ? Promise.resolve(null)
    : apiFetch('/publicidade')
      .then(c => (c?.ativo && c.formatos?.length ? c : null))
      .catch(() => null);
  return config;
}

/**
 * Insere um anúncio depois de cada `cada` itens (`seletor`, filhos diretos de `container`).
 * Chame após cada renderização da lista: os anúncios anteriores são removidos antes.
 * @param {HTMLElement} container
 * @param {{ seletor: string, cada: number, tipo?: keyof PREFERENCIA }} opts
 */
export async function intercalarAnuncios(container, { seletor, cada, tipo = 'auto' }) {
  const cfg = await carregarConfig();
  container.querySelectorAll(':scope > .pub-slot').forEach(s => s.remove());
  if (!cfg) return;
  container.querySelectorAll(`:scope > ${seletor}`).forEach((el, i) => {
    if ((i + 1) % cada === 0) el.after(criarSlot(cfg, tipo));
  });
}

/**
 * Anúncios no grid do dashboard, nas `posicoes` (1-based) entre os blocos visíveis — a
 * personalização (ordem, blocos ocultos) não os tira do lugar. Somem no modo de edição.
 * @param {HTMLElement} grid
 * @param {{ posicao: number, tipo: keyof PREFERENCIA }[]} anuncios
 */
export async function anunciosNoDashboard(grid, anuncios) {
  const cfg = await carregarConfig();
  if (!cfg || !grid) return;
  const itens = [...anuncios]
    .sort((a, b) => a.posicao - b.posicao)
    .map(a => ({ ...a, slot: criarSlot(cfg, a.tipo, 'd2-card') }));

  const posicionar = () => {
    if (grid.classList.contains('d2-editando')) return;
    const seq = [...grid.children].filter(el => el.matches('[data-widget]:not(.d2-oculto)'));
    for (const { posicao, slot } of itens) {
      const anterior = seq[Math.min(posicao - 1, seq.length) - 1];
      if (!anterior) { if (grid.firstElementChild !== slot) grid.prepend(slot); }
      else if (anterior.nextElementSibling !== slot) anterior.after(slot);
      seq.splice(Math.min(posicao - 1, seq.length), 0, slot);
    }
  };

  posicionar();
  // O layout salvo é aplicado depois (cache local e servidor) reordenando os filhos do grid;
  // ocultar/exibir blocos troca a classe deles. As mudanças que este código faz são idempotentes.
  new MutationObserver(registros => {
    if (registros.some(r => r.type === 'childList' || r.target === grid || r.target.parentElement === grid)) posicionar();
  }).observe(grid, { childList: true, subtree: true, attributes: true, attributeFilter: ['class'] });
}

function criarSlot(cfg, tipo, classeExtra = '') {
  injectStyles();
  const slot = document.createElement('aside');
  slot.className = `pub-slot ${classeExtra}`.trim();
  slot.setAttribute('aria-label', 'Publicidade');
  slot.innerHTML = `<div class="pub-topo"><span>Publicidade</span>
      <a href="/pages/assinatura.html" class="pub-remover">Remover anúncios</a></div>
    <div class="pub-area"></div>`;
  slot.querySelector('.pub-remover').addEventListener('click', () => trackEvent('publicidade_remover_click', { tipo }));

  const area = slot.querySelector('.pub-area');
  if (sortearProprio(cfg)) {
    mostrarProprio(slot, area, cfg, tipo, 'sorteio');
    return slot;
  }

  let atual = null;
  let estado = 'carregando'; // → 'ok' (Adsterra desenhou) ou 'proprio' (anúncio do Causify no lugar)
  let espera = null;

  const substituir = () => {
    clearTimeout(espera);
    if (estado !== 'carregando') return;
    estado = 'proprio';
    observador.disconnect();
    visibilidade.disconnect();
    mostrarProprio(slot, area, cfg, tipo, 'vazio');
  };
  estados.set(slot, {
    ok: () => { if (estado === 'carregando') { estado = 'ok'; clearTimeout(espera); } },
    vazio: substituir,
  });

  const observador = new ResizeObserver(() => {
    const largura = area.clientWidth;
    if (!largura) return; // oculto (ex.: modo de edição do dashboard): mantém o anúncio
    const formato = escolherFormato(cfg.formatos, PREFERENCIA[tipo] ?? PREFERENCIA.auto, largura);
    if (formato === atual) return;
    atual = formato;
    area.replaceChildren(criarIframe(formato, cfg.origem));
  });
  observador.observe(area);

  // O iframe é lazy: o prazo para o anúncio aparecer só começa quando o bloco fica visível.
  const visibilidade = new IntersectionObserver(entradas => {
    if (!entradas.some(e => e.isIntersecting)) return;
    visibilidade.disconnect();
    espera = setTimeout(substituir, ESPERA_MS);
  });
  visibilidade.observe(slot);
  return slot;
}

// O bloco começa com um anúncio próprio (sem tentar o Adsterra)? Sem nenhum ativo, nunca.
function sortearProprio(cfg) {
  const percentual = Math.min(Math.max(Number(cfg.percentualProprios) || 0, 0), 100);
  return cfg.proprios?.length > 0 && Math.random() * 100 < percentual;
}

// `motivo`: 'sorteio' (percentual do super admin) ou 'vazio' (o Adsterra não entregou).
function mostrarProprio(slot, area, cfg, tipo, motivo) {
  const anuncio = proximoProprio(cfg.proprios);
  if (!anuncio) { slot.hidden = true; return; } // nenhum cadastrado: some em vez de ficar vazio
  area.replaceChildren(criarProprio(anuncio, tipo, motivo));
  pintarMoldura(slot, anuncio.cor); // o bloco todo na cor do anúncio
  const exibido = () => {
    trackEvent('publicidade_propria_exibida', { anuncio: anuncio.id, tipo, motivo });
    registrar('impressoes', anuncio.id);
  };
  if (motivo === 'vazio') { exibido(); return; } // o vazio só é decidido com o bloco já à vista
  // Sorteado ao montar a lista: conta a exibição quando o bloco aparece na tela.
  const visivel = new IntersectionObserver(entradas => {
    if (!entradas.some(e => e.isIntersecting)) return;
    visivel.disconnect();
    exibido();
  });
  visivel.observe(slot);
}

function proximoProprio(proprios) {
  if (!proprios?.length) return null;
  if (fila.length === 0) fila = [...proprios].sort(() => Math.random() - 0.5);
  return fila.pop();
}

function criarProprio(anuncio, tipo, motivo) {
  const el = criarAnuncioProprio(anuncio);
  el.addEventListener('click', () => {
    trackEvent('publicidade_propria_click', { anuncio: anuncio.id, tipo, motivo });
    registrar('cliques', anuncio.id);
  });
  return el;
}

function registrar(evento, id) {
  pendentes[evento].push(id);
  if (evento === 'cliques') enviarEventos(); // o clique costuma sair da página: vai na hora
  else envio ??= setTimeout(enviarEventos, ENVIO_MS);
}

// keepalive: o envio sobrevive à navegação (clique no anúncio, troca de página).
function enviarEventos() {
  clearTimeout(envio);
  envio = null;
  if (!pendentes.impressoes.length && !pendentes.cliques.length) return;
  const body = { impressoes: pendentes.impressoes.splice(0), cliques: pendentes.cliques.splice(0) };
  apiFetch('/publicidade/proprios/eventos', { method: 'POST', body, keepalive: true }).catch(() => {});
}
window.addEventListener('pagehide', enviarEventos);
document.addEventListener('visibilitychange', () => { if (document.visibilityState === 'hidden') enviarEventos(); });

function escolherFormato(disponiveis, preferencia, largura) {
  const candidatos = preferencia.filter(f => disponiveis.includes(f));
  return candidatos.find(f => (TAMANHOS[f][0] ?? 0) <= largura)
    ?? candidatos.at(-1)
    ?? disponiveis[0];
}

function criarIframe(formato, origem) {
  const [largura, altura] = TAMANHOS[formato] ?? TAMANHOS['300x250'];
  const f = document.createElement('iframe');
  f.className = 'pub-frame';
  f.title = 'Publicidade';
  f.loading = 'lazy';
  f.setAttribute('sandbox', origem ? `${SANDBOX} allow-same-origin` : SANDBOX);
  f.setAttribute('scrolling', 'no');
  f.src = `${origem ?? ''}/api/publicidade/anuncio/${encodeURIComponent(formato)}`;
  f.style.width = largura ? `${largura}px` : '100%';
  f.style.height = `${altura}px`;
  return f;
}

// Altura do Native Banner, informada pela página do iframe. A mensagem é aceita só se vier de
// um iframe de anúncio desta página (a origem não ajuda: é "null" sem o host próprio).
// O mesmo vale para o estado ({ pubEstado: 'ok' | 'vazio' }).
window.addEventListener('message', e => {
  const altura = e.data?.pubAltura;
  const estado = e.data?.pubEstado;
  if (typeof altura !== 'number' && estado !== 'ok' && estado !== 'vazio') return;
  const frame = [...document.querySelectorAll('iframe.pub-frame')].find(f => f.contentWindow === e.source);
  if (!frame) return;
  if (typeof altura === 'number') frame.style.height = `${Math.min(Math.max(Math.round(altura), 50), 600)}px`;
  else estados.get(frame.closest('.pub-slot'))?.[estado]();
});

function injectStyles() {
  if (document.getElementById('pubStyles')) return;
  const s = document.createElement('style');
  s.id = 'pubStyles';
  s.textContent = `
    .pub-slot { display: flex; flex-direction: column; gap: 8px; min-width: 0; overflow: hidden;
      padding: 10px 14px 14px; border: 1px dashed var(--color-border); border-radius: var(--radius, 12px);
      background: var(--color-surface); }
    .pub-topo { display: flex; justify-content: space-between; align-items: baseline; gap: 8px;
      font-size: 11px; font-weight: 600; text-transform: uppercase; letter-spacing: .05em; color: var(--color-text-muted); }
    .pub-remover { text-transform: none; letter-spacing: 0; font-weight: 600; color: var(--color-primary); text-decoration: none; }
    .pub-remover:hover { text-decoration: underline; }
    .pub-area { display: flex; justify-content: center; align-items: center; flex: 1; min-width: 0; }
    .pub-frame { display: block; max-width: 100%; border: 0; background: transparent; }
    .d2-editando > .pub-slot { display: none; }
    .pub-slot[hidden] { display: none; }
  `;
  document.head.appendChild(s);
}
