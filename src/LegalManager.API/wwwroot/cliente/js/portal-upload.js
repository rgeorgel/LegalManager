// Envio de documentos no Portal do Cliente: painel que se abre dentro do card de Documentos
// com a fila compartilhada do escritório (js/upload-fila.js — arrastar e soltar, vários
// arquivos, renomear, progresso por arquivo) e a tela "Solte para enviar" ao arrastar
// arquivos sobre a página. Estilos: /css/upload-fila.css (fila) + portal.css (.pu-*).
import { trackEvent } from '/js/analytics.js';
import { createUploadFila, extensao, EXTENSOES_DOCUMENTO } from '/js/upload-fila.js';
import { getToken } from '/cliente/js/clienteApi.js';

const TIPOS = [['Prova', 'Prova'], ['Contrato', 'Contrato'], ['Peticao', 'Petição'], ['Decisao', 'Decisão'], ['Outro', 'Outro']];
const ACCEPT = EXTENSOES_DOCUMENTO.map(e => '.' + e).join(',');
const NUVEM = '<svg viewBox="0 0 48 48" aria-hidden="true"><path d="M14 34h-1.5a8.5 8.5 0 0 1-1.6-16.85A12 12 0 0 1 34.3 14.6 9.75 9.75 0 0 1 35.5 34H34"/><g class="up-seta"><path d="M24 40V23"/><path d="M17.5 29.5 24 23l6.5 6.5"/></g></svg>';
const FECHA_SOZINHO_MS = 1100;

const plural = (n, um, varios) => `${n} ${n === 1 ? um : varios}`;

// Cada render da aba Documentos monta um painel novo: o anterior solta os listeners globais.
let painelAnterior = null;

/**
 * Monta o painel de envio em `raiz` (começa fechado).
 * @param {HTMLElement} raiz
 * @param {object} o
 * @param {string} o.processoId
 * @param {(enviados: number) => void} [o.aoEnviar]  algum arquivo chegou ao servidor (recarregar a lista)
 * @param {(aberto: boolean) => void} [o.aoAbrirFechar]
 */
export function montarPainelUpload(raiz, { processoId, aoEnviar = () => {}, aoAbrirFechar = () => {} }) {
  painelAnterior?.abort();
  const sinal = (painelAnterior = new AbortController()).signal;

  raiz.innerHTML = `
    <div class="pu-painel" inert>
      <div class="pu-dentro">
        <div class="pu-corpo">
          <div class="up-zona" tabindex="0" role="button" aria-label="Escolher arquivos para enviar">
            <input type="file" multiple hidden accept="${ACCEPT}">
            <div class="up-zona-icone">${NUVEM}</div>
            <div class="up-zona-texto">
              <div class="up-zona-titulo"><strong>Arraste os arquivos aqui</strong> <span class="up-zona-ou">ou</span> <span class="up-link">escolha no computador</span></div>
              <div class="up-zona-titulo up-zona-toque"><span class="up-link">Toque para escolher arquivos ou fotos</span></div>
              <div class="up-zona-dica">PDF, Word, Excel, fotos e texto · até 100 MB cada · vários de uma vez</div>
              <div class="up-zona-solte">Solte para adicionar</div>
            </div>
          </div>
          <ul class="up-lista" aria-live="polite"></ul>
          <div class="pu-tipo">
            <span class="pu-tipo-rotulo" id="puTipoRotulo">Tipo de documento</span>
            <div class="pu-chips" role="radiogroup" aria-labelledby="puTipoRotulo">
              ${TIPOS.map(([valor, rotulo], i) => `
                <label class="pu-chip"><input type="radio" name="puTipo" value="${valor}"${i === 0 ? ' checked' : ''}><span>${rotulo}</span></label>`).join('')}
            </div>
          </div>
          <div class="alert pu-alerta" hidden></div>
        </div>
        <div class="pu-rodape">
          <div class="up-rodape-info"></div>
          <button type="button" class="btn btn-secondary btn-sm" data-pu-cancelar>Cancelar</button>
          <button type="button" class="btn btn-primary btn-sm up-btn-enviar" data-pu-enviar disabled>Enviar</button>
        </div>
      </div>
    </div>`;

  const painel = raiz.querySelector('.pu-painel');
  const zona = raiz.querySelector('.up-zona');
  const btnEnviar = raiz.querySelector('[data-pu-enviar]');
  const btnCancelar = raiz.querySelector('[data-pu-cancelar]');
  const info = raiz.querySelector('.up-rodape-info');
  const alerta = raiz.querySelector('.pu-alerta');
  let rodada = { total: 0, okAntes: 0 };
  let fechamento = null;

  const fila = createUploadFila({
    zona,
    input: raiz.querySelector('input[type=file]'),
    lista: raiz.querySelector('.up-lista'),
    aoMudar: atualizar,
    url: `/api/portal/meus-processos/${encodeURIComponent(processoId)}/documentos`,
    token: getToken,
    renovarSessao: async () => false, // o portal não renova token: o item mostra "sessão expirou"
  });

  function atualizar(e = fila.estado()) {
    painel.querySelectorAll('.pu-chip input').forEach(r => { r.disabled = e.fase === 'enviando'; });
    btnCancelar.textContent = e.fase === 'fim' ? 'Fechar' : 'Cancelar';
    btnEnviar.classList.remove('concluido');

    if (e.fase === 'enviando') {
      const feitos = e.ok - rodada.okAntes;
      info.innerHTML = `<div class="up-geral"><span>${feitos} de ${rodada.total}</span>
        <div class="up-geral-barra"><div class="up-geral-fill" style="width:${e.progressoGeral}%"></div></div>
        <span>${e.progressoGeral}%</span></div>`;
      btnEnviar.disabled = true;
      btnEnviar.innerHTML = '<span class="spinner" style="width:14px;height:14px;vertical-align:-2px"></span> Enviando…';
      return;
    }
    info.textContent = e.fase === 'selecao' && e.total
      ? `${plural(e.prontos, 'arquivo pronto', 'arquivos prontos')}`
        + (e.invalidos ? ` · ${plural(e.invalidos, 'não será enviado', 'não serão enviados')}` : '')
      : '';
    if (e.fase === 'fim' && e.erros === 0 && e.ok > 0) {
      btnEnviar.disabled = true;
      btnEnviar.classList.add('concluido');
      btnEnviar.textContent = '✓ Enviado';
      return;
    }
    btnEnviar.disabled = e.prontos === 0;
    btnEnviar.textContent = e.fase === 'fim' && e.erros
      ? `Tentar de novo (${e.prontos})`
      : e.prontos > 1 ? `Enviar ${e.prontos} arquivos` : 'Enviar';
  }

  function mostrarAlerta(texto) {
    alerta.textContent = texto;
    alerta.className = 'alert alert-error pu-alerta';
    alerta.hidden = false;
  }

  const aberto = () => painel.classList.contains('aberto');

  function abrir() {
    clearTimeout(fechamento);
    if (aberto()) return;
    alerta.hidden = true;
    painel.inert = false;
    painel.classList.add('aberto');
    aoAbrirFechar(true);
    atualizar();
  }

  function fechar({ perguntar = true } = {}) {
    const e = fila.estado();
    if (perguntar && e.fase === 'enviando' && !confirm('Há arquivos sendo enviados. Cancelar o envio dos que faltam?')) return;
    clearTimeout(fechamento);
    painel.classList.remove('aberto');
    painel.inert = true;
    aoAbrirFechar(false);
    // Limpa depois da animação de fechar, para a lista não sumir de repente.
    setTimeout(() => { if (!aberto()) fila.limpar(); }, 320);
    if (e.fase === 'enviando' && e.ok > rodada.okAntes) aoEnviar(e.ok - rodada.okAntes);
  }

  btnCancelar.addEventListener('click', () => fechar());

  btnEnviar.addEventListener('click', async () => {
    const antes = fila.estado();
    if (antes.fase === 'enviando' || !antes.prontos) return;
    alerta.hidden = true;
    rodada = { total: antes.prontos, okAntes: antes.ok };
    const tipo = painel.querySelector('.pu-chip input:checked')?.value || 'Prova';
    const fim = await fila.enviar({ tipo });
    if (fim.fase !== 'fim') return; // fechado no meio — fechar() já avisou a página
    const enviados = fim.ok - rodada.okAntes;
    if (enviados) {
      trackEvent('portal_documentos_enviados', { quantidade: enviados, erros: fim.erros });
      aoEnviar(enviados);
    }
    if (fim.erros === 0) {
      fechamento = setTimeout(() => fechar({ perguntar: false }), FECHA_SOZINHO_MS);
    } else {
      mostrarAlerta(`${plural(fim.erros, 'arquivo não foi enviado', 'arquivos não foram enviados')}`
        + (enviados ? ` (${plural(enviados, 'enviado', 'enviados')} com sucesso)` : '')
        + '. Veja o motivo em cada um e tente de novo.');
    }
  });

  // Colar (Ctrl/⌘+V) arquivos ou prints com o painel aberto.
  document.addEventListener('paste', e => {
    if (!aberto() || !raiz.isConnected) return;
    const arquivos = [...(e.clipboardData?.files || [])];
    if (!arquivos.length) return;
    e.preventDefault();
    const hora = new Date().toLocaleTimeString('pt-BR').replace(/:/g, '-');
    fila.adicionar(arquivos.map((f, i) =>
      // Print colado chega como "image.png" — dá um nome que não repete.
      /^image\.\w+$/i.test(f.name)
        ? new File([f], `Imagem colada ${hora}${arquivos.length > 1 ? `-${i + 1}` : ''}.${extensao(f.name)}`, { type: f.type })
        : f));
  }, { signal: sinal });

  return {
    abrir,
    fechar,
    aberto,
    /** Abre o painel já com estes arquivos (soltos na página). */
    adicionar(arquivos) {
      abrir();
      fila.adicionar(arquivos);
      zona.scrollIntoView({ block: 'nearest', behavior: matchMedia('(prefers-reduced-motion: reduce)').matches ? 'auto' : 'smooth' });
    },
    focar() { zona.focus({ preventScroll: true }); },
  };
}

/**
 * Arrastar arquivos sobre a página mostra "Solte para enviar"; ao soltar fora da zona do
 * painel, `aoSoltar` recebe os arquivos. Chame uma vez por página.
 * @param {object} o
 * @param {() => boolean} o.podeSoltar
 * @param {(arquivos: File[]) => void} o.aoSoltar
 * @param {() => string} [o.destino] texto abaixo do título (ex.: número do processo)
 * @param {() => boolean} [o.mostrarTela] false = só aceita o arquivo, sem cobrir a página
 *        (painel já aberto: a zona dele é que se destaca)
 */
export function ativarSoltarNaPagina({ podeSoltar, aoSoltar, destino = () => '', mostrarTela = () => true }) {
  const tela = document.createElement('div');
  tela.className = 'up-tela-drop';
  tela.setAttribute('aria-hidden', 'true');
  tela.innerHTML = `<div class="up-tela-drop-caixa">${NUVEM}<strong>Solte para enviar</strong><span></span></div>`;
  document.body.appendChild(tela);

  const temArquivos = e => [...(e.dataTransfer?.types || [])].includes('Files');
  let profundidade = 0;
  const esconder = () => { profundidade = 0; tela.classList.remove('ativa'); };

  window.addEventListener('dragenter', e => {
    if (!temArquivos(e) || !podeSoltar()) return;
    if (++profundidade === 1 && mostrarTela()) {
      tela.querySelector('span').textContent = destino();
      tela.classList.add('ativa');
    }
  });
  window.addEventListener('dragleave', e => {
    if (temArquivos(e) && --profundidade <= 0) esconder();
  });
  window.addEventListener('dragover', e => {
    if (!temArquivos(e)) return;
    e.preventDefault(); // sem isso o navegador abre o arquivo e sai do portal
    e.dataTransfer.dropEffect = podeSoltar() ? 'copy' : 'none';
  });
  window.addEventListener('drop', e => {
    if (!temArquivos(e)) return;
    const jaTratado = e.defaultPrevented; // soltou na zona do painel
    e.preventDefault();
    esconder();
    if (!jaTratado && podeSoltar()) aoSoltar([...e.dataTransfer.files]);
  });
}

