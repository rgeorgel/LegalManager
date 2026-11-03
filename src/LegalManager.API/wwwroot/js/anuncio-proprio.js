// Anúncio próprio do Causify (AnuncioProprio): o mesmo desenho no portal (/js/publicidade.js, no
// lugar do Adsterra vazio) e na pré-visualização do super admin. Sem dependências do portal.

const COR_HEX = /^#[0-9a-f]{6}$/i;

/** A cor é válida (#RRGGBB)? */
export const corValida = cor => typeof cor === 'string' && COR_HEX.test(cor);

/** Texto escuro ou claro sobre `cor`, pelo contraste (luminância relativa do WCAG). */
export function corDoTexto(cor) {
  const canal = i => {
    const c = parseInt(cor.slice(i, i + 2), 16) / 255;
    return c <= 0.03928 ? c / 12.92 : ((c + 0.055) / 1.055) ** 2.4;
  };
  const luminancia = 0.2126 * canal(1) + 0.7152 * canal(3) + 0.0722 * canal(5);
  // Ponto em que o contraste com branco e com #111827 se iguala.
  return luminancia > 0.18 ? '#111827' : '#ffffff';
}

// Só caminhos do sistema (mesma aba) ou https (outra aba) — o servidor já valida ao salvar.
export function linkSeguro(url) {
  if (typeof url !== 'string') return null;
  if (url.startsWith('/') && !url.startsWith('//') && !url.startsWith('/\\')) return { href: url, externo: false };
  return /^https:\/\//i.test(url) ? { href: url, externo: true } : null;
}

/**
 * Elemento do anúncio. Com `link: false` (pré-visualização) vira um bloco sem navegação.
 * @param {{ icone?: string, titulo: string, texto: string, textoBotao: string, linkUrl: string, cor?: string }} anuncio
 */
export function criarAnuncioProprio(anuncio, { link = true } = {}) {
  injectStyles();
  const destino = linkSeguro(anuncio.linkUrl) ?? { href: '/pages/assinatura.html', externo: false };
  const el = document.createElement(link ? 'a' : 'div');
  el.className = 'pub-proprio';
  if (link) {
    el.href = destino.href;
    if (destino.externo) { el.target = '_blank'; el.rel = 'noopener noreferrer'; }
  }
  if (corValida(anuncio.cor)) {
    el.classList.add('pub-proprio-colorido');
    el.style.setProperty('--pub-cor', anuncio.cor);
    el.style.setProperty('--pub-tinta', corDoTexto(anuncio.cor));
  }

  const parte = (classe, texto) => {
    const span = document.createElement('span');
    span.className = classe;
    span.textContent = texto;
    return span;
  };
  const corpo = document.createElement('span');
  corpo.className = 'pub-proprio-corpo';
  corpo.append(parte('pub-proprio-titulo', anuncio.titulo), parte('pub-proprio-texto', anuncio.texto));
  // O conteúdo fica num filho: o layout responde à largura do anúncio (container query).
  const conteudo = document.createElement('span');
  conteudo.className = 'pub-proprio-conteudo';
  if (anuncio.icone) conteudo.append(parte('pub-proprio-icone', anuncio.icone));
  conteudo.append(corpo, parte('pub-proprio-botao', `${anuncio.textoBotao} →`));
  el.append(conteudo);
  return el;
}

/**
 * Pinta a moldura em volta do anúncio (o bloco "Publicidade") com a cor dele, para o anúncio
 * ocupar o bloco inteiro. Sem cor válida, volta ao visual neutro.
 * Fundo e borda vão inline: vencem o CSS da página (ex.: .d2-card do dashboard).
 */
export function pintarMoldura(moldura, cor) {
  injectStyles();
  const colorida = corValida(cor);
  moldura.classList.toggle('pub-moldura-colorida', colorida);
  moldura.style.background = colorida ? cor : '';
  moldura.style.borderColor = colorida ? cor : '';
  if (colorida) moldura.style.setProperty('--pub-tinta', corDoTexto(cor));
  else moldura.style.removeProperty('--pub-tinta');
}

function injectStyles() {
  if (document.getElementById('pubProprioStyles')) return;
  const s = document.createElement('style');
  s.id = 'pubProprioStyles';
  s.textContent = `
    .pub-proprio { display: block; width: 100%; box-sizing: border-box; padding: 14px 16px; border-radius: 10px;
      background: var(--color-bg); color: var(--color-text); text-decoration: none; container-type: inline-size; }
    .pub-proprio-conteudo { display: flex; flex-wrap: wrap; align-items: center; gap: 10px 14px; }
    a.pub-proprio:hover { background: var(--color-border); }
    .pub-proprio-icone { font-size: 26px; line-height: 1; }
    .pub-proprio-corpo { display: flex; flex-direction: column; gap: 3px; flex: 1 1 220px; min-width: 0; }
    .pub-proprio-titulo { font-weight: 700; font-size: 14px; }
    .pub-proprio-texto { font-size: 13px; line-height: 1.45; color: var(--color-text-muted); }
    .pub-proprio-botao { flex: 0 0 auto; font-size: 13px; font-weight: 600; color: var(--color-primary); }
    .pub-proprio-colorido, a.pub-proprio-colorido:hover { background: var(--pub-cor); color: var(--pub-tinta); }
    a.pub-proprio-colorido:hover { filter: brightness(1.08); }
    .pub-proprio-colorido .pub-proprio-texto { color: inherit; opacity: .88; }
    .pub-proprio-colorido .pub-proprio-botao { padding: 6px 14px; border-radius: 999px;
      background: var(--pub-tinta); color: var(--pub-cor); }
    /* Moldura já pintada (pintarMoldura): o anúncio se funde a ela, alinhado ao rótulo "Publicidade". */
    .pub-moldura-colorida .pub-proprio { padding: 4px 0; border-radius: 0; }
    .pub-moldura-colorida a.pub-proprio-colorido:hover { filter: none; }
    .pub-moldura-colorida a.pub-proprio:hover .pub-proprio-botao { filter: brightness(.92); }
    .pub-moldura-colorida .pub-topo, .pub-moldura-colorida .pub-remover { color: var(--pub-tinta); }
    .pub-moldura-colorida .pub-topo > span { opacity: .75; }
    /* Faixa (listas) fica em linha. Bloco estreito (cards do dashboard, grade de processos):
       empilhado e centralizado, com título maior e o botão no meio. */
    @container (max-width: 520px) {
      .pub-proprio .pub-proprio-conteudo { flex-direction: column; flex-wrap: nowrap; gap: 12px; text-align: center; }
      .pub-proprio .pub-proprio-icone { font-size: 40px; }
      .pub-proprio .pub-proprio-corpo { flex: 0 1 auto; align-items: center; gap: 6px; }
      .pub-proprio .pub-proprio-titulo { font-size: 18px; line-height: 1.3; }
      .pub-proprio .pub-proprio-texto { font-size: 13.5px; line-height: 1.5; }
      .pub-proprio .pub-proprio-botao { margin-top: 4px; font-size: 14px; }
      .pub-proprio-colorido .pub-proprio-botao { padding: 9px 20px; }
    }
  `;
  document.head.appendChild(s);
}
