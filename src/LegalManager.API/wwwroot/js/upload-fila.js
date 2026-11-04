// Fila de upload de documentos: zona de arrastar e soltar, vários arquivos por vez,
// validação no navegador (tipo e tamanho), renomear antes de enviar e progresso real de
// cada arquivo (XMLHttpRequest — o fetch não informa o progresso do envio).
// A página decide os metadados (tipo, processo, pasta...) e o que fazer ao terminar.
import { esc } from '/js/utils.js';
import { apiFetch } from '/js/api.js';

// Mesma lista do servidor (ArquivoPermitido.cs) — lá a assinatura do arquivo é conferida.
export const EXTENSOES_DOCUMENTO = ['pdf', 'doc', 'docx', 'xls', 'xlsx', 'odt', 'ods', 'rtf', 'txt', 'csv', 'png', 'jpg', 'jpeg', 'webp'];
export const TAMANHO_MAXIMO = 100_000_000; // [RequestSizeLimit] do DocumentosController

const SIMULTANEOS = 3;

const FAMILIAS = {
  pdf: ['pdf'],
  word: ['doc', 'docx', 'odt', 'rtf'],
  planilha: ['xls', 'xlsx', 'ods', 'csv'],
  imagem: ['png', 'jpg', 'jpeg', 'webp'],
  texto: ['txt'],
};

export function extensao(nome) {
  const i = nome.lastIndexOf('.');
  return i > 0 ? nome.slice(i + 1).toLowerCase() : '';
}

function familia(ext) {
  return Object.keys(FAMILIAS).find(f => FAMILIAS[f].includes(ext)) || 'outro';
}

export function formatarTamanho(bytes) {
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(0)} KB`;
  if (bytes < 1024 * 1024 * 1024) return `${(bytes / (1024 * 1024)).toFixed(1).replace('.', ',')} MB`;
  return `${(bytes / (1024 * 1024 * 1024)).toFixed(2).replace('.', ',')} GB`;
}

const ICONE_OK = '<svg viewBox="0 0 24 24" aria-hidden="true"><path class="up-check" d="M5 12.5l4.5 4.5L19 7.5"/></svg>';
const ICONE_ERRO = '<svg viewBox="0 0 24 24" aria-hidden="true"><path d="M12 7v6M12 16.5v.5"/></svg>';
const ICONE_REMOVER = '<svg viewBox="0 0 24 24" aria-hidden="true"><path d="M6 6l12 12M18 6L6 18"/></svg>';

/**
 * @param {object} o
 * @param {HTMLElement} o.zona       área de arrastar/clicar
 * @param {HTMLInputElement} o.input input[type=file] (multiple)
 * @param {HTMLElement} o.lista      onde os arquivos aparecem
 * @param {(estado: object) => void} [o.aoMudar] chamado a cada mudança (contagens, fase)
 */
export function createUploadFila({ zona, input, lista, aoMudar = () => {} }) {
  let itens = [];
  let seq = 0;
  let fase = 'selecao'; // selecao | enviando | fim
  const xhrs = new Map();
  let rodada = 0; // limpar() no meio de um envio invalida a rodada em curso

  // ── Seleção ───────────────────────────────────────────
  function adicionar(arquivos) {
    if (fase === 'enviando') return;
    if (fase === 'fim') { itens = itens.filter(i => i.status !== 'ok'); fase = 'selecao'; }
    for (const file of arquivos) {
      const dup = itens.some(i => i.file.name === file.name && i.file.size === file.size && i.file.lastModified === file.lastModified);
      if (dup) continue;
      const ext = extensao(file.name);
      const item = {
        id: ++seq, file, ext,
        nomeBase: ext ? file.name.slice(0, -(ext.length + 1)) : file.name,
        status: 'pronto', progresso: 0, erro: '', novo: true,
      };
      if (!EXTENSOES_DOCUMENTO.includes(ext)) {
        item.status = 'invalido';
        item.erro = ext ? `Tipo .${ext} não é aceito.` : 'Arquivo sem extensão.';
      } else if (file.size === 0) {
        item.status = 'invalido'; item.erro = 'Arquivo vazio.';
      } else if (file.size > TAMANHO_MAXIMO) {
        item.status = 'invalido'; item.erro = `Maior que o limite de ${formatarTamanho(TAMANHO_MAXIMO)}.`;
      }
      if (familia(ext) === 'imagem' && item.status === 'pronto') item.miniatura = URL.createObjectURL(file);
      itens.push(item);
    }
    render();
  }

  function remover(id) {
    const item = itens.find(i => i.id === id);
    if (!item || item.status === 'enviando') return;
    const li = lista.querySelector(`[data-up-id="${id}"]`);
    const tirar = () => {
      if (item.miniatura) URL.revokeObjectURL(item.miniatura);
      itens = itens.filter(i => i !== item);
      render();
    };
    if (li && !matchMedia('(prefers-reduced-motion: reduce)').matches) {
      li.classList.add('saindo');
      li.addEventListener('animationend', tirar, { once: true });
    } else tirar();
  }

  function limpar() {
    rodada++;
    xhrs.forEach(x => x.abort());
    xhrs.clear();
    itens.forEach(i => i.miniatura && URL.revokeObjectURL(i.miniatura));
    itens = [];
    fase = 'selecao';
    input.value = '';
    render();
  }

  zona.addEventListener('click', () => { if (fase !== 'enviando') input.click(); });
  zona.addEventListener('keydown', e => {
    if ((e.key === 'Enter' || e.key === ' ') && fase !== 'enviando') { e.preventDefault(); input.click(); }
  });
  input.addEventListener('change', () => { adicionar([...input.files]); input.value = ''; });

  // dragenter/dragleave disparam também nos filhos: conta a profundidade.
  let profundidade = 0;
  const temArquivos = e => [...(e.dataTransfer?.types || [])].includes('Files');
  zona.addEventListener('dragenter', e => {
    if (!temArquivos(e)) return;
    e.preventDefault();
    if (++profundidade === 1) zona.classList.add('arrastando');
  });
  zona.addEventListener('dragover', e => {
    if (!temArquivos(e)) return;
    e.preventDefault();
    e.dataTransfer.dropEffect = fase === 'enviando' ? 'none' : 'copy';
  });
  zona.addEventListener('dragleave', () => {
    if (--profundidade <= 0) { profundidade = 0; zona.classList.remove('arrastando'); }
  });
  zona.addEventListener('drop', e => {
    if (!temArquivos(e)) return;
    e.preventDefault();
    profundidade = 0;
    zona.classList.remove('arrastando');
    adicionar([...e.dataTransfer.files]);
  });

  lista.addEventListener('click', e => {
    const btn = e.target.closest('[data-up-remover]');
    if (btn) remover(Number(btn.dataset.upRemover));
  });
  lista.addEventListener('input', e => {
    const campo = e.target.closest('[data-up-nome]');
    if (!campo) return;
    const item = itens.find(i => i.id === Number(campo.dataset.upNome));
    if (item) item.nomeBase = campo.value;
  });

  // ── Renderização ──────────────────────────────────────
  function htmlItem(item) {
    const fam = familia(item.ext);
    const icone = item.miniatura
      ? `<img src="${esc(item.miniatura)}" alt="">`
      : `<span>${esc((item.ext || '?').slice(0, 4).toUpperCase())}</span>`;
    const editavel = item.status === 'pronto' || item.status === 'erro';
    const nome = editavel
      ? `<span class="up-nome-edit"><input type="text" class="up-nome-input" data-up-nome="${item.id}" value="${esc(item.nomeBase)}" size="${Math.max(4, item.nomeBase.length)}" maxlength="200" aria-label="Nome do arquivo" title="Clique para renomear"><span class="up-ext">${item.ext ? '.' + esc(item.ext) : ''}</span></span>`
      : `<span class="up-nome-txt" title="${esc(nomeFinal(item))}">${esc(nomeFinal(item))}</span>`;
    return `
      <li class="up-item up-${item.status}${item.novo ? ' entrando' : ''}" data-up-id="${item.id}">
        <div class="up-icone up-fam-${fam}${item.miniatura ? ' com-miniatura' : ''}">${icone}</div>
        <div class="up-info">
          ${nome}
          <div class="up-meta">${htmlMeta(item)}</div>
          <div class="up-barra"><div class="up-barra-fill" style="width:${item.progresso}%"></div></div>
        </div>
        <div class="up-acao">${htmlAcao(item)}</div>
      </li>`;
  }

  function htmlMeta(item) {
    const tam = formatarTamanho(item.file.size);
    switch (item.status) {
      case 'enviando': return `<span>${item.progresso < 100 ? `Enviando… ${item.progresso}%` : 'Processando…'}</span><span>${tam}</span>`;
      case 'ok': return `<span class="up-meta-ok">Enviado</span><span>${tam}</span>`;
      case 'erro':
      case 'invalido': return `<span class="up-meta-erro" title="${esc(item.erro)}">${esc(item.erro)}</span><span>${tam}</span>`;
      default: return `<span>${tam}</span>`;
    }
  }

  function htmlAcao(item) {
    if (item.status === 'enviando') return `<span class="up-anel" style="--p:${item.progresso}" aria-label="${item.progresso}%"></span>`;
    if (item.status === 'ok') return `<span class="up-selo up-selo-ok">${ICONE_OK}</span>`;
    const erro = item.status === 'erro' || item.status === 'invalido'
      ? `<span class="up-selo up-selo-erro" title="${esc(item.erro)}">${ICONE_ERRO}</span>` : '';
    if (fase === 'enviando') return erro;
    return `${erro}<button type="button" class="up-remover" data-up-remover="${item.id}" title="Remover da lista" aria-label="Remover ${esc(item.file.name)}">${ICONE_REMOVER}</button>`;
  }

  function render() {
    zona.classList.toggle('compacta', itens.length > 0);
    zona.classList.toggle('bloqueada', fase === 'enviando');
    zona.setAttribute('aria-disabled', fase === 'enviando' ? 'true' : 'false');
    // Preserva o foco/cursor do campo de nome que está sendo editado.
    const ativo = document.activeElement?.closest?.('[data-up-nome]');
    const foco = ativo ? { id: ativo.dataset.upNome, ini: ativo.selectionStart, fim: ativo.selectionEnd } : null;
    lista.innerHTML = itens.map(htmlItem).join('');
    itens.forEach(i => { i.novo = false; });
    if (foco) {
      const campo = lista.querySelector(`[data-up-nome="${foco.id}"]`);
      if (campo) { campo.focus(); campo.setSelectionRange(foco.ini, foco.fim); }
    }
    aoMudar(estado());
  }

  // Durante o envio só a barra/anel de cada item muda — evita refazer a lista inteira.
  function atualizarItem(item) {
    const li = lista.querySelector(`[data-up-id="${item.id}"]`);
    if (!li) return render();
    const jaEraErro = li.classList.contains('up-erro');
    li.className = `up-item up-${item.status}`;
    if (item.status === 'erro' && !jaEraErro) li.classList.add('mudou');
    li.querySelector('.up-meta').innerHTML = htmlMeta(item);
    li.querySelector('.up-barra-fill').style.width = `${item.progresso}%`;
    const acao = li.querySelector('.up-acao');
    const anel = acao.querySelector('.up-anel');
    if (item.status === 'enviando' && anel) {
      anel.style.setProperty('--p', item.progresso);
      anel.setAttribute('aria-label', `${item.progresso}%`);
    } else acao.innerHTML = htmlAcao(item);
    aoMudar(estado());
  }

  function estado() {
    const conta = s => itens.filter(i => i.status === s).length;
    const enviaveis = itens.filter(i => i.status === 'pronto' || i.status === 'erro');
    const bytesTotal = itens.filter(i => i.status !== 'invalido').reduce((s, i) => s + i.file.size, 0);
    const bytesEnviados = itens.reduce((s, i) =>
      s + (i.status === 'ok' ? i.file.size : i.status === 'enviando' ? i.file.size * i.progresso / 100 : 0), 0);
    return {
      fase, total: itens.length,
      prontos: enviaveis.length,
      bytesProntos: enviaveis.reduce((s, i) => s + i.file.size, 0),
      enviando: conta('enviando'), ok: conta('ok'), erros: conta('erro'), invalidos: conta('invalido'),
      bytesTotal, progressoGeral: bytesTotal ? Math.round(bytesEnviados / bytesTotal * 100) : 0,
    };
  }

  function nomeFinal(item) {
    const base = item.nomeBase.trim() || (item.ext ? item.file.name.slice(0, -(item.ext.length + 1)) : item.file.name);
    return item.ext ? `${base}.${item.ext}` : base;
  }

  // ── Envio ─────────────────────────────────────────────
  function enviarUm(item, campos, tentouRenovar = false) {
    return new Promise(resolve => {
      const fd = new FormData();
      fd.append('file', item.file);
      if (nomeFinal(item) !== item.file.name) fd.append('nome', nomeFinal(item));
      Object.entries(campos).forEach(([k, v]) => { if (v) fd.append(k, v); });

      const xhr = new XMLHttpRequest();
      xhrs.set(item.id, xhr);
      xhr.open('POST', '/api/documentos/upload');
      xhr.setRequestHeader('Authorization', `Bearer ${sessionStorage.getItem('access_token')}`);
      xhr.upload.onprogress = e => {
        if (!e.lengthComputable) return;
        const p = Math.min(100, Math.round(e.loaded / e.total * 100));
        if (p !== item.progresso) { item.progresso = p; atualizarItem(item); }
      };
      const terminar = (status, erro = '') => {
        xhrs.delete(item.id);
        if (!itens.includes(item)) return resolve(); // lista limpa no meio do envio
        item.status = status; item.erro = erro;
        if (status === 'ok') item.progresso = 100;
        else item.progresso = 0;
        atualizarItem(item);
        resolve();
      };
      xhr.onload = async () => {
        if (xhr.status >= 200 && xhr.status < 300) return terminar('ok');
        if (xhr.status === 401 && !tentouRenovar) {
          // Token expirado: o apiFetch renova a sessão; tenta mais uma vez.
          xhrs.delete(item.id);
          try { await apiFetch('/documentos/cota'); } catch { /* sem sessão: cai no erro abaixo */ }
          item.progresso = 0;
          await enviarUm(item, campos, true);
          return resolve();
        }
        terminar('erro', mensagemErro(xhr));
      };
      xhr.onerror = () => terminar('erro', 'Falha de conexão. Tente novamente.');
      xhr.onabort = () => terminar('erro', 'Envio cancelado.');
      item.status = 'enviando'; item.progresso = 0; item.erro = '';
      atualizarItem(item);
      xhr.send(fd);
    });
  }

  /** Envia os itens prontos (e os que falharam antes). Resolve com o estado final. */
  async function enviar(campos) {
    const fila = itens.filter(i => i.status === 'pronto' || i.status === 'erro');
    if (!fila.length) return estado();
    const minha = ++rodada;
    fase = 'enviando';
    render();
    const trabalhador = async () => {
      while (fila.length && rodada === minha) await enviarUm(fila.shift(), campos);
    };
    await Promise.all(Array.from({ length: Math.min(SIMULTANEOS, fila.length) }, trabalhador));
    if (rodada !== minha) return estado();
    fase = 'fim';
    render();
    return estado();
  }

  return { adicionar, enviar, limpar, estado };
}

function mensagemErro(xhr) {
  if (xhr.status === 413) return `Maior que o limite de ${formatarTamanho(TAMANHO_MAXIMO)}.`;
  const texto = xhr.responseText || '';
  try {
    const b = JSON.parse(texto);
    if (typeof b === 'string') return b;
    return b.message || b.title || `Erro ${xhr.status}.`;
  } catch {
    return texto && texto.length < 200 ? texto : `Erro ${xhr.status}.`;
  }
}
