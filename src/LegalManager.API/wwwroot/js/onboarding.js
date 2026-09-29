import { apiFetch } from '/js/api.js';
import { acompanharImportacao } from './importacao-bar.js';

import { esc } from './utils.js';
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

// onDone (opcional): chamado quando o fluxo de onboarding termina — seja por
// já estar completo (modal nem chega a abrir), seja por Pular/Concluir no
// modal. O tour guiado não inicia mais automaticamente em seguida (ver
// tour.js) — fica disponível sob demanda pelo widget "Tutorial" — mas o
// parâmetro continua útil para outros encadeamentos pós-onboarding.
export async function initOnboarding(onDone) {
  try {
    const status = await apiFetch('/onboarding/status');
    if (status.completo) { onDone?.(); return; }
    _afterClose = onDone ?? null;
    showModal();
  } catch {
    // silently fail — onboarding is non-critical
    onDone?.();
  }
}

export function openOnboardingModal() {
  showStep('oab');
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

// Passo → [conteúdo, rodapé]. O passo de leitura só existe na tela de processos.
const PASSOS = {
  oab: ['obStep1', 'obFooterStep1'],
  iniciada: ['obStep3', 'obFooterStep3'],
  leitura: ['obStepLeitura', 'obFooterLeitura'],
};

function showStep(passo) {
  Object.entries(PASSOS).forEach(([nome, [stepId, footerId]]) => {
    const step = document.getElementById(stepId);
    const footer = document.getElementById(footerId);
    if (step) step.style.display = nome === passo ? 'block' : 'none';
    if (footer) footer.style.display = nome === passo ? 'flex' : 'none';
  });
}

export async function openReadOnlyModal(numero, uf) {
  const oabInfoEl = document.getElementById('obOabLeituraInfo');
  const listaEl = document.getElementById('obListaLeitura');

  if (oabInfoEl) oabInfoEl.textContent = `${numero}/${uf}`;
  if (listaEl) listaEl.innerHTML = '<div style="color:var(--color-text-muted);font-size:13px">Carregando...</div>';

  document.getElementById('obFecharLeituraBtn')?.addEventListener('click', hideModal, { once: true });

  showStep('leitura');
  showModal();
  mostrarImportacaoEmAndamento(listaEl);

  try {
    const oabs = await apiFetch('/onboarding/oabs-importadas');
    if (!listaEl) return;
    if (!oabs || oabs.length === 0) {
      listaEl.innerHTML = '<div style="color:var(--color-text-muted);font-size:13px">Nenhuma OAB registrada.</div>';
      return;
    }
    listaEl.innerHTML = oabs.map(o => {
      const minha = o.numero === numero && o.uf === uf;
      return `<div style="display:flex;align-items:center;gap:8px;padding:8px 10px;border:1px solid var(--color-border);border-radius:6px;margin-bottom:6px${minha ? ';background:#f0fdf4' : ''}">
        <span style="font-family:monospace;font-weight:600;min-width:80px">${esc(o.numero)}/${esc(o.uf)}</span>
        <span style="font-size:12px;color:var(--color-text-muted);flex:1">${esc(o.nomeUsuario)}</span>
        <span style="font-size:12px;color:var(--color-text-muted)">${o.totalProcessos} processo(s)</span>
        ${minha ? '<span style="font-size:10px;background:#d1fae5;color:#065f46;padding:1px 6px;border-radius:100px;font-weight:600">Você</span>' : ''}
      </div>`;
    }).join('');
  } catch {
    if (listaEl) listaEl.innerHTML = '<div style="color:#dc2626;font-size:13px">Erro ao carregar lista.</div>';
  }
}

async function mostrarImportacaoEmAndamento(listaEl) {
  document.getElementById('obImportacaoAndamento')?.remove();
  if (!listaEl) return;
  try {
    const ativa = await apiFetch('/importacoes/ativa');
    if (!ativa) return;
    const aviso = document.createElement('div');
    aviso.id = 'obImportacaoAndamento';
    aviso.style.cssText = 'background:var(--color-primary-light);border-radius:6px;padding:10px 12px;font-size:13px;margin-bottom:12px';
    aviso.textContent = ativa.status === 'Importando'
      ? `Importação em andamento: ${ativa.processados} de ${ativa.total} processos. Acompanhe pela barra na parte inferior da tela.`
      : 'Importação em andamento: pesquisando seus processos. Acompanhe pela barra na parte inferior da tela.';
    listaEl.parentElement.insertBefore(aviso, listaEl.previousElementSibling ?? listaEl);
  } catch { /* não crítico */ }
}

let _marcarCompleto = true;
let _onImportStart = null;
let _afterClose = null;

async function completar() {
  if (_marcarCompleto) {
    try { await apiFetch('/onboarding/completar', { method: 'POST' }); } catch {}
  }
  hideModal();
  const afterClose = _afterClose;
  _afterClose = null;
  afterClose?.();
}

export function initOnboardingModal(opts = {}) {
  _marcarCompleto = opts.marcarCompleto !== false;
  _onImportStart = opts.onImportStart ?? null;

  const select = document.getElementById('obUf');
  UFS.forEach(uf => {
    const opt = document.createElement('option');
    opt.value = uf;
    opt.textContent = uf;
    select.appendChild(opt);
  });

  document.getElementById('obBuscarBtn').addEventListener('click', importarOab);
  document.getElementById('obOab').addEventListener('keydown', e => {
    if (e.key === 'Enter') importarOab();
  });
  document.getElementById('obPularBtn').addEventListener('click', completar);
  document.getElementById('obConcluirBtn').addEventListener('click', completar);

  showStep('oab');
}

// Importa sempre todos os processos da OAB: a pesquisa e a importação rodam em background.
async function importarOab() {
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
  btn.disabled = true;
  btn.textContent = 'Iniciando...';
  try {
    await iniciarImportacao({ numeroOAB, uf });
  } catch (e) {
    erroEl.textContent = e?.message || 'Erro ao iniciar a importação. Tente novamente.';
  } finally {
    btn.disabled = false;
    btn.textContent = 'Importar processos';
  }
}

// Cria a importação em background e passa o acompanhamento para a barra inferior
// (importacao-bar.js). O modal só confirma que começou; o resultado chega por aviso
// e notificação quando o job terminar.
async function iniciarImportacao(body) {
  const importacao = await apiFetch('/importacoes', { method: 'POST', body: JSON.stringify(body) });

  document.getElementById('obResultadoTexto').innerHTML = `
    Importação iniciada!
    <div style="font-size:13px;font-weight:400;color:var(--color-text-muted);margin-top:8px;line-height:1.5">
      Estamos pesquisando e importando seus processos em segundo plano — você pode continuar
      usando o sistema. Acompanhe o progresso na barra na parte inferior da tela; avisaremos
      quando terminar.
    </div>`;
  document.getElementById('obResultadoDetalhe').style.display = 'none';
  showStep('iniciada');

  acompanharImportacao(importacao);
  if (_marcarCompleto) {
    try { await apiFetch('/onboarding/completar', { method: 'POST' }); } catch {}
  }
  _onImportStart?.(importacao);
}

