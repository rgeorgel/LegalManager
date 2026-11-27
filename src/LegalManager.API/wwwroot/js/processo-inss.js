// Cartão "Requerimento no INSS" do detalhe de um processo administrativo: dados do requerimento
// (espécie, situação, NB, DIB/DCB, recurso, perícia), edição num modal e os prazos que eles pedem.
// Os prazos são sugestões com memória de cálculo: só viram tarefa/evento quando o advogado
// confirma (POST /api/processos/{id}/inss/sugestoes/{chave}), como na triagem de publicações.
import { apiFetch } from './api.js';
import { brl, dataParede, esc, inputDataHoraParede } from './utils.js';

export const SITUACAO_INSS = {
  EmAnalise: 'Em análise', EmExigencia: 'Em exigência', Concedido: 'Concedido',
  Indeferido: 'Indeferido', EmRecurso: 'Em recurso (CRPS)', Cancelado: 'Cancelado',
};
const COR_SITUACAO = { EmExigencia: 'badge-red', Indeferido: 'badge-red', Concedido: 'badge-green' };
const INSTANCIA = { JuntaRecursos: 'Junta de Recursos', CamaraJulgamento: 'Câmara de Julgamento' };

export const ehOrgaoInss = orgao => /\b(inss|crps)\b/i.test(orgao || '');

// Datas puras (DER, ciência, DIB, DCB) voltam da API como meia-noite UTC: o dia é o do UTC.
function isoDia(s) { const d = s ? new Date(s) : null; return d && !isNaN(d) ? d.toISOString().slice(0, 10) : ''; }
function fmtDia(s) { const iso = isoDia(s); return iso ? iso.split('-').reverse().join('/') : '—'; }
// Perícia: hora "de parede", como os eventos da agenda.
function fmtPericia(s) {
  const d = dataParede(s);
  return d ? `${d.toLocaleDateString('pt-BR')} às ${d.toLocaleTimeString('pt-BR', { hour: '2-digit', minute: '2-digit' })}` : '—';
}

let especies = null;
async function carregarEspecies() {
  if (!especies) especies = await apiFetch('/inss/especies');
  return especies;
}

/**
 * Monta o cartão em `container` (some quando o processo não é do INSS).
 * @param {{ processo: object, avisar: (msg: string, tipo?: string) => void, onCriado?: () => void }} opcoes
 */
export function montarCartaoInss(container, { processo, avisar, onCriado }) {
  const aplica = processo.tipo === 'Administrativo' && (ehOrgaoInss(processo.orgao) || processo.situacaoInss);
  container.hidden = !aplica;
  if (!aplica) return;

  let dados = null;
  const base = `/processos/${encodeURIComponent(processo.id)}/inss`;

  async function carregar() {
    try { dados = await apiFetch(base); } catch { dados = null; }
    render();
  }

  function linha(rotulo, valor) {
    return `<div class="info-item"><label>${rotulo}</label><span>${valor ?? '—'}</span></div>`;
  }
  // Campos que só existem em parte dos casos (NB depois da concessão, DCB no auxílio...): só aparecem preenchidos.
  const opcional = (rotulo, valor) => valor ? linha(rotulo, valor) : '';

  function render() {
    const d = dados;
    const corpo = !d
      ? `<p class="inss-vazio">Preencha espécie, situação e datas: o Causify sugere os prazos de exigência, recurso ao CRPS e prorrogação.</p>`
      : `<div class="info-grid inss-grid">
          ${linha('Espécie', d.especieNome ? esc(d.especieNome) : '—')}
          ${linha('Situação', `<span class="badge ${COR_SITUACAO[d.situacao] || 'badge-gray'}">${esc(SITUACAO_INSS[d.situacao] || d.situacao)}</span>`)}
          ${linha('DER', fmtDia(d.der))}
          ${linha('Ciência', fmtDia(d.dataCiencia))}
          ${opcional('NB', d.numeroBeneficio && esc(d.numeroBeneficio))}
          ${opcional('Unidade (APS)', d.unidade && esc(d.unidade))}
          ${opcional('DIB', d.dib && fmtDia(d.dib))}
          ${opcional('DCB', d.dcb && fmtDia(d.dcb))}
          ${opcional('RMI', d.rmi != null && esc(brl(d.rmi)))}
          ${opcional('Recurso', (d.instanciaRecurso || d.numeroRecurso)
            && esc([INSTANCIA[d.instanciaRecurso], d.numeroRecurso].filter(Boolean).join(' · ')))}
          ${opcional('Perícia', d.periciaEm && esc(fmtPericia(d.periciaEm) + (d.periciaLocal ? ` · ${d.periciaLocal}` : '')))}
        </div>
        ${sugestoesHtml(d.sugestoes || [])}`;
    container.innerHTML = `<div class="card"><div class="card-body">
      <div style="display:flex;align-items:center;justify-content:space-between;margin-bottom:12px">
        <div class="card-title" style="margin-bottom:0">Requerimento no INSS</div>
        <button type="button" class="btn btn-secondary btn-sm" data-inss="editar">${d ? 'Editar' : 'Preencher'}</button>
      </div>
      ${corpo}
    </div></div>`;
  }

  function sugestoesHtml(lista) {
    if (!lista.length) return '';
    return `<div class="inss-sugestoes">
      <div class="inss-sugestoes-titulo">Prazos sugeridos</div>
      ${lista.map(s => `<div class="inss-sugestao">
        <div class="inss-sugestao-texto">
          <strong>${esc(s.titulo)}</strong> · ${s.tipo === 'evento' ? esc(fmtPericia(s.data)) : fmtDia(s.data)}
          <div class="inss-memoria">${esc(s.memoria)}</div>
        </div>
        <button type="button" class="btn btn-primary btn-sm" data-inss="criar" data-chave="${esc(s.chave)}">
          ${s.tipo === 'evento' ? 'Pôr na agenda' : 'Criar prazo'}</button>
      </div>`).join('')}
    </div>`;
  }

  container.addEventListener('click', async e => {
    const btn = e.target.closest('[data-inss]');
    if (!btn) return;
    if (btn.dataset.inss === 'editar') { abrirEdicao(); return; }
    btn.disabled = true;
    try {
      const criado = await apiFetch(`${base}/sugestoes/${encodeURIComponent(btn.dataset.chave)}`, { method: 'POST' });
      avisar(criado.tipo === 'evento' ? 'Perícia incluída na agenda.' : 'Prazo criado nas tarefas do processo.', 'success');
      onCriado?.();
      await carregar();
    } catch (err) {
      avisar(err.message);
      btn.disabled = false;
    }
  });

  // ── Edição ──
  const modal = document.createElement('div');
  modal.className = 'modal-overlay';
  modal.innerHTML = `<div class="modal" style="max-width:640px" role="dialog" aria-labelledby="inssTitulo">
    <div class="modal-header">
      <span class="modal-title" id="inssTitulo">Requerimento no INSS</span>
      <button class="modal-close" type="button" data-fechar aria-label="Fechar">✕</button>
    </div>
    <div class="modal-body">
      <div class="alert alert-error" data-erro style="display:none"></div>
      <div class="form-row">
        <div class="form-group"><label for="iEspecie">Espécie do benefício</label>
          <select id="iEspecie" class="form-control"><option value="">Selecionar…</option></select></div>
        <div class="form-group"><label for="iSituacao">Situação</label>
          <select id="iSituacao" class="form-control">${Object.entries(SITUACAO_INSS).map(([v, r]) => `<option value="${v}">${r}</option>`).join('')}</select></div>
      </div>
      <div class="form-row">
        <div class="form-group"><label for="iCiencia">Data da ciência</label>
          <input type="date" id="iCiencia" class="form-control">
          <div class="inss-hint">Da exigência ou da decisão atual: os 30 dias contam dela.</div></div>
        <div class="form-group"><label for="iUnidade">Unidade (APS)</label>
          <input type="text" id="iUnidade" class="form-control" maxlength="150" placeholder="APS Centro, CEAB…"></div>
      </div>
      <div class="form-row">
        <div class="form-group"><label for="iNb">NB (número do benefício)</label>
          <input type="text" id="iNb" class="form-control" maxlength="20" inputmode="numeric"></div>
        <div class="form-group"><label for="iRmi">RMI (R$)</label>
          <input type="number" id="iRmi" class="form-control" min="0" step="0.01"></div>
      </div>
      <div class="form-row">
        <div class="form-group"><label for="iDib">DIB (início do benefício)</label>
          <input type="date" id="iDib" class="form-control"></div>
        <div class="form-group"><label for="iDcb">DCB (cessação)</label>
          <input type="date" id="iDcb" class="form-control"></div>
      </div>
      <div class="form-row">
        <div class="form-group"><label for="iInstancia">Recurso no CRPS</label>
          <select id="iInstancia" class="form-control"><option value="">—</option>
            ${Object.entries(INSTANCIA).map(([v, r]) => `<option value="${v}">${r}</option>`).join('')}</select></div>
        <div class="form-group"><label for="iNumRecurso">Número do recurso</label>
          <input type="text" id="iNumRecurso" class="form-control" maxlength="50"></div>
      </div>
      <div class="form-row">
        <div class="form-group"><label for="iPericia">Perícia médica</label>
          <input type="datetime-local" id="iPericia" class="form-control"></div>
        <div class="form-group"><label for="iPericiaLocal">Local da perícia</label>
          <input type="text" id="iPericiaLocal" class="form-control" maxlength="200"></div>
      </div>
    </div>
    <div class="modal-footer">
      <button class="btn btn-secondary" type="button" data-fechar>Cancelar</button>
      <button class="btn btn-primary" type="button" data-salvar>Salvar</button>
    </div>
  </div>`;
  document.body.appendChild(modal);
  const campo = id => modal.querySelector(`#${id}`);
  const fechar = () => modal.classList.remove('open');
  modal.querySelectorAll('[data-fechar]').forEach(b => b.addEventListener('click', fechar));

  async function abrirEdicao() {
    const erro = modal.querySelector('[data-erro]');
    erro.style.display = 'none';
    try {
      const sel = campo('iEspecie');
      if (sel.options.length === 1) {
        sel.insertAdjacentHTML('beforeend', (await carregarEspecies())
          .map(e => `<option value="${e.codigo}">B${e.codigo} — ${esc(e.nome)}</option>`).join(''));
      }
    } catch { /* sem catálogo: o select fica só com "Selecionar…" */ }
    const d = dados || {};
    campo('iEspecie').value = d.especie ?? '';
    campo('iSituacao').value = d.situacao || 'EmAnalise';
    campo('iCiencia').value = isoDia(d.dataCiencia);
    campo('iUnidade').value = d.unidade || '';
    campo('iNb').value = d.numeroBeneficio || '';
    campo('iRmi').value = d.rmi ?? '';
    campo('iDib').value = isoDia(d.dib);
    campo('iDcb').value = isoDia(d.dcb);
    campo('iInstancia').value = d.instanciaRecurso || '';
    campo('iNumRecurso').value = d.numeroRecurso || '';
    campo('iPericia').value = d.periciaEm ? inputDataHoraParede(d.periciaEm) : '';
    campo('iPericiaLocal').value = d.periciaLocal || '';
    modal.classList.add('open');
  }

  modal.querySelector('[data-salvar]').addEventListener('click', async e => {
    const btn = e.currentTarget;
    const erro = modal.querySelector('[data-erro]');
    const v = id => campo(id).value.trim() || null;
    btn.disabled = true;
    erro.style.display = 'none';
    try {
      dados = await apiFetch(base, {
        method: 'PUT',
        body: JSON.stringify({
          especie: v('iEspecie') ? Number(v('iEspecie')) : null,
          situacao: campo('iSituacao').value,
          dataCiencia: v('iCiencia'),
          unidade: v('iUnidade'),
          numeroBeneficio: v('iNb'),
          rmi: v('iRmi') != null ? Number(v('iRmi')) : null,
          dib: v('iDib'),
          dcb: v('iDcb'),
          instanciaRecurso: v('iInstancia'),
          numeroRecurso: v('iNumRecurso'),
          periciaEm: v('iPericia'),
          periciaLocal: v('iPericiaLocal'),
        }),
      });
      fechar();
      render();
      avisar(dados.sugestoes?.length ? 'Dados do INSS salvos. Confira os prazos sugeridos.' : 'Dados do INSS salvos.', 'success');
    } catch (err) {
      erro.textContent = err.message;
      erro.style.display = 'block';
    } finally {
      btn.disabled = false;
    }
  });

  carregar();
}
