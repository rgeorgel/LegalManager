// Miniaturas dos documentos (capa dos cards e "carregando" do painel).
// PDF: 1ª página renderizada com o pdf.js (js/vendor/pdfjs); imagem: reduzida num canvas.
// O arquivo só é baixado quando o card aparece na tela (IntersectionObserver), no máximo 2 por
// vez, e a miniatura (JPEG pequeno) fica no Cache Storage do navegador — a próxima visita não
// baixa o documento de novo. Demais tipos (Word, planilha...) ficam com o ícone do tipo.

import { esc } from '/js/utils.js';

const LARGURA = 480;
const LIMITE_BYTES = 25 * 1024 * 1024; // acima disso não vale baixar só para a capa
const SIMULTANEOS = 2;
const CACHE = 'causify-miniaturas-v1';

const PREVIA_PDF = new Set(['application/pdf']);
const PREVIA_IMAGEM = new Set(['image/png', 'image/jpeg', 'image/webp']);

const memoria = new Map(); // id → Promise<string|null> (object URL)
const fila = [];
let ativos = 0;

/** O documento tem miniatura (PDF ou imagem dentro do limite)? */
export function temMiniatura(doc) {
  return (PREVIA_PDF.has(doc.contentType) || PREVIA_IMAGEM.has(doc.contentType))
    && doc.tamanhoBytes > 0 && doc.tamanhoBytes <= LIMITE_BYTES;
}

/** Já está pronta em memória? (evita piscar o ícone ao re-renderizar a lista) */
export function miniaturaPronta(id) {
  return prontas.get(id) ?? null;
}
const prontas = new Map(); // id → object URL já resolvida

/**
 * Object URL da miniatura (ou null quando não dá). `obterBlob()` baixa o arquivo — só é
 * chamado se a miniatura não estiver em cache.
 */
export function miniatura(doc, obterBlob) {
  if (!temMiniatura(doc)) return Promise.resolve(null);
  if (!memoria.has(doc.id)) {
    const p = new Promise(resolve => fila.push({ doc, obterBlob, resolve }))
      .then(url => { if (url) prontas.set(doc.id, url); return url; });
    memoria.set(doc.id, p);
    proximo();
  }
  return memoria.get(doc.id);
}

function proximo() {
  while (ativos < SIMULTANEOS && fila.length) {
    const { doc, obterBlob, resolve } = fila.shift();
    ativos++;
    gerar(doc, obterBlob)
      .catch(() => null)
      .then(url => { ativos--; resolve(url); proximo(); });
  }
}

async function gerar(doc, obterBlob) {
  // A chave inclui tamanho: um arquivo trocado com o mesmo id não reaproveita a capa velha.
  const chave = `/__miniaturas/${doc.id}-${doc.tamanhoBytes}`;
  const cache = await abrirCache();
  const salva = await cache?.match(chave).catch(() => null);
  if (salva) return URL.createObjectURL(await salva.blob());

  const blob = await obterBlob();
  const jpeg = PREVIA_PDF.has(doc.contentType) ? await capaPdf(blob) : await capaImagem(blob);
  if (!jpeg) return null;
  cache?.put(chave, new Response(jpeg, { headers: { 'Content-Type': 'image/jpeg' } })).catch(() => {});
  return URL.createObjectURL(jpeg);
}

async function abrirCache() {
  try { return 'caches' in window ? await caches.open(CACHE) : null; } catch { return null; }
}

function paraJpeg(canvas) {
  return new Promise(r => canvas.toBlob(r, 'image/jpeg', 0.78));
}

async function capaImagem(blob) {
  const bmp = await createImageBitmap(blob);
  const escala = Math.min(1, LARGURA / bmp.width);
  const canvas = document.createElement('canvas');
  canvas.width = Math.max(1, Math.round(bmp.width * escala));
  canvas.height = Math.max(1, Math.round(bmp.height * escala));
  canvas.getContext('2d').drawImage(bmp, 0, 0, canvas.width, canvas.height);
  bmp.close?.();
  return paraJpeg(canvas);
}

let pdfjs = null;
async function carregarPdfjs() {
  if (!pdfjs) {
    pdfjs = import('/js/vendor/pdfjs/pdf.min.js').then(m => {
      m.GlobalWorkerOptions.workerSrc = '/js/vendor/pdfjs/pdf.worker.min.js';
      return m;
    });
  }
  return pdfjs;
}

async function capaPdf(blob) {
  const lib = await carregarPdfjs();
  // isEvalSupported: false — a CSP do app não libera eval.
  const pdf = await lib.getDocument({ data: new Uint8Array(await blob.arrayBuffer()), isEvalSupported: false }).promise;
  try {
    const pagina = await pdf.getPage(1);
    const base = pagina.getViewport({ scale: 1 });
    const viewport = pagina.getViewport({ scale: LARGURA / base.width });
    const canvas = document.createElement('canvas');
    canvas.width = Math.round(viewport.width);
    canvas.height = Math.round(viewport.height);
    const ctx = canvas.getContext('2d');
    ctx.fillStyle = '#fff';
    ctx.fillRect(0, 0, canvas.width, canvas.height);
    await pagina.render({ canvasContext: ctx, viewport }).promise;
    return paraJpeg(canvas);
  } finally {
    pdf.destroy();
  }
}

/**
 * Observa as capas (`[data-capa-id]`) dentro do contêiner e preenche cada uma quando entra na
 * tela. `buscar(id)` devolve o documento e `obterBlob(id)` baixa o arquivo.
 * Chame de novo após cada renderização (é barato: só capas ainda vazias são observadas).
 */
export function observarCapas(container, { buscar, obterBlob }) {
  if (!('IntersectionObserver' in window)) return;
  container._capasObs?.disconnect();
  const obs = new IntersectionObserver(entradas => {
    for (const e of entradas) {
      if (!e.isIntersecting) continue;
      obs.unobserve(e.target);
      const capa = e.target;
      const doc = buscar(capa.dataset.capaId);
      if (!doc) continue;
      capa.classList.add('carregando');
      miniatura(doc, () => obterBlob(doc.id)).then(url => {
        capa.classList.remove('carregando');
        if (url) preencherCapa(capa, url);
      });
    }
  }, { rootMargin: '200px 0px' });
  container._capasObs = obs;
  container.querySelectorAll('[data-capa-id]:not(.com-imagem)').forEach(c => obs.observe(c));
}

export function preencherCapa(capa, url) {
  if (capa.classList.contains('com-imagem')) return;
  const img = new Image();
  img.alt = '';
  img.decoding = 'async';
  img.onload = () => capa.classList.add('com-imagem');
  img.src = url;
  capa.prepend(img);
}

// ── Capa do card de documento (cardEntidade({ capaHtml })) ──────────────────────
// Ícone/selo da extensão; com observarCapas(), a miniatura entra por cima quando fica pronta.
// CSS em styles.css (.doc-capa, .fam-*).

const FAMILIAS_EXT = { pdf: ['pdf'], word: ['doc', 'docx', 'odt', 'rtf', 'txt'], planilha: ['xls', 'xlsx', 'ods', 'csv'], imagem: ['png', 'jpg', 'jpeg', 'webp', 'gif'] };

function extensaoArquivo(nome) {
  const i = (nome || '').lastIndexOf('.');
  return i > 0 ? nome.slice(i + 1).toLowerCase() : '';
}

/** Família do arquivo pela extensão: pdf | word | planilha | imagem | outro (classe fam-*). */
export function familiaDoc(nome) {
  const ext = extensaoArquivo(nome);
  return Object.keys(FAMILIAS_EXT).find(f => FAMILIAS_EXT[f].includes(ext)) || 'outro';
}

/** Rótulo curto da extensão (PDF, DOCX...) mostrado na capa sem miniatura. */
export function rotuloExt(nome) { return (extensaoArquivo(nome) || 'arq').slice(0, 4).toUpperCase(); }

/** HTML da capa do card de um documento. */
export function capaDocumento(d) {
  const pronta = miniaturaPronta(d.id);
  const img = pronta ? `<img src="${esc(pronta)}" alt="">` : '';
  const observar = temMiniatura(d) ? ` data-capa-id="${esc(d.id)}"` : '';
  return `<div class="doc-capa fam-${familiaDoc(d.nome)}${pronta ? ' com-imagem' : ''}"${observar}>${img}<span class="doc-capa-ext">${esc(rotuloExt(d.nome))}</span></div>`;
}
