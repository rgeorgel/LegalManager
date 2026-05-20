import { apiFetch } from '/js/api.js';

const UFS = [
  'AC','AL','AM','AP','BA','CE','DF','ES','GO','MA',
  'MG','MS','MT','PA','PB','PE','PI','PR','RJ','RN',
  'RO','RR','RS','SC','SE','SP','TO',
];

const TRIBUNAIS_POR_UF = {
  AC:3, AL:3, AM:3, AP:3, BA:3, CE:3, DF:3, ES:3, GO:3,
  MA:3, MG:3, MS:3, MT:3, PA:3, PB:3, PE:3, PI:3, PR:3,
  RJ:3, RN:3, RO:3, RR:3, RS:3, SC:3, SE:3,
  SP:4, // tjsp + trf3 + trt2 + trt15
  TO:3,
};

const DOTS_TXT = { search: null, import: null };

export function startDots(key, baseText, txtEl) {
  let count = 0;
  const txt = txtEl || document.querySelector(`#${key === 'search' ? 'obLoadingSearch' : key === 'import' ? 'obLoadingImport' : 'processosLoading'} div > div:last-child`);
  if (!txt) return;
  txt.textContent = baseText;
  DOTS_TXT[key] = setInterval(() => {
    count = (count + 1) % 4;
    txt.textContent = baseText + '.'.repeat(count);
  }, 400);
}

export function stopDots(key) {
  if (DOTS_TXT[key]) { clearInterval(DOTS_TXT[key]); DOTS_TXT[key] = null; }
}

export async function initOnboarding() {
  try {
    const status = await apiFetch('/onboarding/status');
    if (status.completo) return;
    showModal();
  } catch {
    // silently fail — onboarding is non-critical
  }
}

export function openOnboardingModal() {
  showStep(1);
  showModal();
}

function showModal() {
  const overlay = document.getElementById('onboardingOverlay');
  if (overlay) overlay.classList.add('open');
}

function hideModal() {
  const overlay = document.getElementById('onboardingOverlay');
  if (overlay) overlay.classList.remove('open');
}

function showStep(n) {
  document.querySelectorAll('.ob-step').forEach((el, i) => {
    el.style.display = i + 1 === n ? 'block' : 'none';
  });
  const footers = ['obFooterStep1', 'obFooterStep2', 'obFooterStep3'];
  footers.forEach((id, i) => {
    const el = document.getElementById(id);
    if (el) el.style.display = i + 1 === n ? 'flex' : 'none';
  });
}

let _marcarCompleto = true;
let _onImportSuccess = null;

async function completar() {
  if (_marcarCompleto) {
    try { await apiFetch('/onboarding/completar', { method: 'POST' }); } catch {}
  }
  hideModal();
}

export function initOnboardingModal(opts = {}) {
  _marcarCompleto = opts.marcarCompleto !== false;
  _onImportSuccess = opts.onImportSuccess ?? null;

  const select = document.getElementById('obUf');
  UFS.forEach(uf => {
    const opt = document.createElement('option');
    opt.value = uf;
    opt.textContent = uf;
    select.appendChild(opt);
  });

  document.getElementById('obBuscarBtn').addEventListener('click', buscarPorOab);
  document.getElementById('obOab').addEventListener('keydown', e => {
    if (e.key === 'Enter') buscarPorOab();
  });
  document.getElementById('obImportarBtn').addEventListener('click', importar);
  document.getElementById('obPularBtn').addEventListener('click', completar);
  document.getElementById('obPularStep2Btn').addEventListener('click', completar);
  document.getElementById('obConcluirBtn').addEventListener('click', completar);

  showStep(1);
}

async function buscarPorOab() {
  const numeroOAB = document.getElementById('obOab').value.trim().replace(/\D/g, '');
  const uf = document.getElementById('obUf').value;
  const erroEl = document.getElementById('obErroBusca');
  erroEl.textContent = '';

  if (!numeroOAB) {
    erroEl.textContent = 'Informe o número da OAB (apenas dígitos).';
    return;
  }
  if (!uf) {
    erroEl.textContent = 'Selecione a UF.';
    return;
  }

  const btn = document.getElementById('obBuscarBtn');
  const loadingEl = document.getElementById('obLoadingSearch');
  btn.disabled = true;
  btn.textContent = 'Buscando...';

  loadingEl.style.display = 'flex';
  startDots('search', 'Procurando processos');

  try {
    const processos = await apiFetch('/onboarding/buscar-por-oab', {
      method: 'POST',
      body: JSON.stringify({ numeroOAB, uf })
    });

    loadingEl.style.display = 'none';
    stopDots('search');
    renderResultados(processos);
    showStep(2);
  } catch {
    loadingEl.style.display = 'none';
    stopDots('search');
    erroEl.textContent = 'Erro ao buscar processos. Verifique o número OAB e tente novamente.';
  } finally {
    btn.disabled = false;
    btn.textContent = 'Buscar Processos';
  }
}

function renderResultados(processos) {
  const lista = document.getElementById('obListaProcessos');
  const semResultado = document.getElementById('obSemResultado');
  const contagem = document.getElementById('obContagem');
  lista.innerHTML = '';

  if (!processos || processos.length === 0) {
    semResultado.style.display = 'block';
    contagem.textContent = '';
    document.getElementById('obImportarBtn').style.display = 'none';
    return;
  }

  semResultado.style.display = 'none';
  document.getElementById('obImportarBtn').style.display = '';
  contagem.textContent = `${processos.length} processo(s) encontrado(s)`;

  processos.forEach(p => {
    const item = document.createElement('label');
    item.style.cssText = 'display:flex;align-items:flex-start;gap:10px;padding:10px 12px;border:1px solid var(--color-border);border-radius:6px;cursor:pointer;margin-bottom:6px;';

    const dataStr = p.dataAjuizamento
      ? new Date(p.dataAjuizamento).toLocaleDateString('pt-BR')
      : '—';

    const badge = p.jaCadastrado
      ? '<span style="font-size:10px;background:#d1fae5;color:#065f46;padding:1px 6px;border-radius:100px;font-weight:600;margin-left:6px">Já importado</span>'
      : '';

    item.innerHTML = `
      <input type="checkbox" value="${esc(p.numeroCNJ)}" data-codigo="${esc(p.codigo ?? '')}" data-foro="${esc(p.foro ?? '')}" ${p.jaCadastrado ? 'disabled checked' : 'checked'} style="margin-top:3px;flex-shrink:0">
      <div>
        <div style="font-size:13px;font-weight:600;font-family:monospace">${esc(p.numeroCNJ)}${badge}</div>
        <div style="font-size:12px;color:var(--color-text-muted);margin-top:2px">
          ${esc(p.tribunal)}${p.vara ? ' · ' + esc(p.vara) : ''}${p.classe ? ' · ' + esc(p.classe) : ''} · Ajuizado: ${dataStr}
        </div>
      </div>`;

    lista.appendChild(item);
  });

  document.getElementById('obSelecionarTodosBtn').addEventListener('click', () => {
    lista.querySelectorAll('input[type=checkbox]:not(:disabled)').forEach(cb => cb.checked = true);
  });
}

async function importar() {
  const checkboxes = document.querySelectorAll('#obListaProcessos input[type=checkbox]:checked:not(:disabled)');
  const processos = Array.from(checkboxes).map(cb => ({
    numeroCNJ: cb.value,
    codigo: cb.dataset.codigo || null,
    foro: cb.dataset.foro || null
  }));

  if (processos.length === 0) {
    document.getElementById('obErroImportar').textContent = 'Selecione ao menos um processo para importar.';
    return;
  }

  const btn = document.getElementById('obImportarBtn');
  const loadingEl = document.getElementById('obLoadingImport');
  btn.disabled = true;
  btn.textContent = `Importando ${processos.length} processo(s)...`;
  loadingEl.style.display = 'flex';
  startDots('import', 'Importando processos');
  document.getElementById('obErroImportar').textContent = '';

  try {
    const resultado = await apiFetch('/onboarding/importar', {
      method: 'POST',
      body: JSON.stringify({ processos })
    });

    document.getElementById('obResultadoTexto').textContent =
      `${resultado.importados} processo(s) importado(s) com sucesso.`;

    const detalheEl = document.getElementById('obResultadoDetalhe');
    if (resultado.mensagens && resultado.mensagens.length > 0) {
      detalheEl.textContent = resultado.mensagens.join('\n');
      detalheEl.style.display = 'block';
    } else {
      detalheEl.style.display = 'none';
    }

    showStep(3);
    if (_marcarCompleto) {
      try { await apiFetch('/onboarding/completar', { method: 'POST' }); } catch {}
    }
    if (_onImportSuccess) _onImportSuccess(resultado.importados);
  } catch {
    document.getElementById('obErroImportar').textContent = 'Erro ao importar processos. Tente novamente.';
  } finally {
    loadingEl.style.display = 'none';
    stopDots('import');
    btn.disabled = false;
    btn.textContent = 'Importar Selecionados';
  }
}

function esc(str) {
  return (str ?? '').replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');
}
