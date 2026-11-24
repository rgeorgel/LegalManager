// Jurisprudência verificada: desenho das citações conferidas (peça gerada e verificador avulso) e o
// modal "Verificar citações". Ver docs/features/jurisprudencia-verificada.md.
// Status (backend StatusCitacao): Verificada (verde), Inexistente (vermelho), o resto âmbar —
// âmbar quer dizer "não verificada/confira na fonte", nunca "falsa".
import { apiFetch } from './api.js';
import { esc, fmtDataParede } from './utils.js';
import { recursoLiberado, RECURSOS } from './recursos.js';

const CLASSE = { Verificada: 'cit-ok', Inexistente: 'cit-erro' };
const ROTULO = {
  Verificada: 'Verificada',
  Divergente: 'Divergente',
  Cancelada: 'Cancelada',
  Inexistente: 'Não existe',
  NaoEncontrada: 'Não encontrada na base',
  NaoVerificada: 'Não verificada',
};
const ICONE = { Verificada: '✓', Inexistente: '✕' };

const classeDe = status => CLASSE[status] ?? 'cit-alerta';

/** Texto escapado com cada citação destacada pela cor do status (dica = motivo). */
export function textoComCitacoesHtml(texto, verificacao) {
  const citacoes = [...(verificacao?.citacoes ?? [])]
    .filter(c => c.inicio >= 0 && c.fim <= (texto ?? '').length && c.fim > c.inicio)
    .sort((a, b) => a.inicio - b.inicio);
  let html = '';
  let pos = 0;
  for (const c of citacoes) {
    if (c.inicio < pos) continue; // sobreposta (não deveria acontecer)
    html += esc(texto.slice(pos, c.inicio));
    const dica = `${ROTULO[c.status] ?? c.status}: ${c.mensagem}`;
    html += `<mark class="cit ${classeDe(c.status)}" title="${esc(dica)}">${esc(texto.slice(c.inicio, c.fim))}</mark>`;
    pos = c.fim;
  }
  return html + esc((texto ?? '').slice(pos));
}

/** Selos com a contagem por cor. Vazio se não há citações. */
export function resumoCitacoesHtml(verificacao) {
  const lista = verificacao?.citacoes ?? [];
  if (!lista.length) return '';
  const ok = lista.filter(c => c.status === 'Verificada').length;
  const erro = lista.filter(c => c.status === 'Inexistente').length;
  const alerta = lista.length - ok - erro;
  const selo = (n, classe, singular, plural) => n
    ? `<span class="cit-selo ${classe}">${n} ${n === 1 ? singular : plural}</span>` : '';
  return `<div class="cit-resumo">
    ${selo(ok, 'cit-ok', 'verificada', 'verificadas')}
    ${selo(alerta, 'cit-alerta', 'a conferir', 'a conferir')}
    ${selo(erro, 'cit-erro', 'inexistente', 'inexistentes')}
  </div>
  ${erro ? '<div class="cit-aviso-erro">Há citação que não existe na base oficial. Remova ou corrija antes de protocolar.</div>' : ''}
  <div class="cit-cobertura">${esc(verificacao.cobertura ?? '')}</div>`;
}

/** Cartões dos precedentes da base usados na peça (com link para a fonte). */
export function precedentesHtml(precedentes) {
  if (!precedentes?.length) return '';
  return precedentes.map(p => {
    const dados = [p.orgaoJulgador, p.relator ? `Rel. Min. ${p.relator}` : null,
      p.dataJulgamento ? `julgado em ${fmtDataParede(p.dataJulgamento, '')}` : null].filter(Boolean).join(' · ');
    const trecho = p.texto.length > 320 ? p.texto.slice(0, 320) + '…' : p.texto;
    return `<div class="cit-prec">
      <div class="cit-prec-topo">
        <strong>${esc(p.identificacao)}</strong>
        <a href="${esc(p.urlFonte)}" target="_blank" rel="noopener">Ver na fonte ↗</a>
      </div>
      ${dados ? `<div class="cit-prec-dados">${esc(dados)}</div>` : ''}
      <div class="cit-prec-texto">${esc(trecho)}</div>
    </div>`;
  }).join('');
}

/** Uma linha por citação, com status e motivo (verificador avulso). */
export function listaCitacoesHtml(verificacao) {
  const lista = verificacao?.citacoes ?? [];
  if (!lista.length) return '<div class="cit-vazio">Nenhuma citação de jurisprudência encontrada no texto.</div>';
  return lista.map(c => `<div class="cit-linha">
      <span class="cit-icone ${classeDe(c.status)}" aria-hidden="true">${ICONE[c.status] ?? '!'}</span>
      <div class="cit-linha-texto">
        <div><strong>${esc(c.trecho)}</strong> <span class="cit-status ${classeDe(c.status)}">${esc(ROTULO[c.status] ?? c.status)}</span></div>
        <div class="cit-linha-motivo">${esc(c.mensagem)}${c.precedente?.urlFonte ? ` <a href="${esc(c.precedente.urlFonte)}" target="_blank" rel="noopener">Ver na fonte ↗</a>` : ''}</div>
      </div>
    </div>`).join('');
}

// ── Verificador avulso ──────────────────────────────────────────────────

let modal = null;

function montarModal() {
  if (modal) return modal;
  modal = document.createElement('div');
  modal.className = 'modal-overlay';
  modal.id = 'modalVerificarCitacoes';
  modal.innerHTML = `
    <div class="modal" style="max-width:720px;max-height:90vh;overflow-y:auto">
      <div class="modal-header">
        <span class="modal-title">Verificar citações</span>
        <button class="modal-close" type="button" data-fechar aria-label="Fechar">✕</button>
      </div>
      <div class="modal-body">
        <p class="cit-ajuda">Confira se os precedentes citados (REsp, AgInt, Súmula, Tema...) existem na base oficial e batem com relator e tribunal.</p>
        <div class="form-group">
          <label for="vcDocumento">Documento do processo (PDF ou texto)</label>
          <select id="vcDocumento" class="form-control"><option value="">— Colar o texto abaixo —</option></select>
        </div>
        <div class="form-group" id="vcTextoGrupo">
          <label for="vcTexto">Texto</label>
          <textarea id="vcTexto" class="form-control" rows="7" placeholder="Cole aqui o trecho da peça com as citações"></textarea>
        </div>
        <div id="vcErro" class="alert alert-error" hidden></div>
        <div id="vcResultado"></div>
      </div>
      <div class="modal-footer">
        <button class="btn btn-secondary" type="button" data-fechar>Fechar</button>
        <button class="btn btn-primary" type="button" id="vcVerificar">Verificar</button>
      </div>
    </div>`;
  document.body.appendChild(modal);

  const fechar = () => modal.classList.remove('open');
  modal.querySelectorAll('[data-fechar]').forEach(b => b.addEventListener('click', fechar));
  modal.addEventListener('mousedown', e => { if (e.target === modal) fechar(); });
  modal.querySelector('#vcDocumento').addEventListener('change', e => {
    modal.querySelector('#vcTextoGrupo').hidden = !!e.target.value;
  });
  modal.querySelector('#vcVerificar').addEventListener('click', verificar);
  return modal;
}

async function verificar() {
  const erro = modal.querySelector('#vcErro');
  const resultado = modal.querySelector('#vcResultado');
  const botao = modal.querySelector('#vcVerificar');
  const documentoId = modal.querySelector('#vcDocumento').value || null;
  const texto = modal.querySelector('#vcTexto').value.trim();
  erro.hidden = true;
  if (!documentoId && !texto) {
    erro.textContent = 'Cole um texto ou escolha um documento.';
    erro.hidden = false;
    return;
  }
  botao.disabled = true;
  botao.innerHTML = '<span class="spinner"></span> Verificando...';
  resultado.innerHTML = '';
  try {
    const r = await apiFetch('/jurisprudencia/verificar', { method: 'POST', body: documentoId ? { documentoId } : { texto } });
    resultado.innerHTML = `${resumoCitacoesHtml(r)}<div class="cit-lista">${listaCitacoesHtml(r)}</div>`;
  } catch (e) {
    erro.textContent = e.message || 'Não foi possível verificar agora.';
    erro.hidden = false;
  } finally {
    botao.disabled = false;
    botao.textContent = 'Verificar';
  }
}

/** Abre o verificador; com <processoId>, oferece os documentos PDF/texto do processo. */
export async function abrirVerificadorCitacoes({ processoId } = {}) {
  const m = montarModal();
  m.querySelector('#vcTexto').value = '';
  m.querySelector('#vcResultado').innerHTML = '';
  m.querySelector('#vcErro').hidden = true;
  m.querySelector('#vcTextoGrupo').hidden = false;
  const select = m.querySelector('#vcDocumento');
  select.innerHTML = '<option value="">— Colar o texto abaixo —</option>';
  m.classList.add('open');
  if (!processoId) return;
  try {
    const docs = await apiFetch(`/documentos/processo/${processoId}`);
    const legiveis = (docs ?? []).filter(d => /pdf|^text\//i.test(d.contentType ?? '') || /\.(pdf|txt)$/i.test(d.nome ?? ''));
    select.insertAdjacentHTML('beforeend', legiveis.map(d => `<option value="${esc(d.id)}">${esc(d.nome)}</option>`).join(''));
  } catch { /* só o campo de texto */ }
}

/** Mostra o botão se o escritório tem o recurso liberado pelo super admin. */
export async function iniciarVerificadorCitacoes({ botao, processoId }) {
  if (!botao || !await recursoLiberado(RECURSOS.jurisprudenciaVerificada)) return;
  botao.hidden = false;
  botao.addEventListener('click', () => abrirVerificadorCitacoes({ processoId }));
}
