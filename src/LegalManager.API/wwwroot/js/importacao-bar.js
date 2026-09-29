// Barra fixa na parte inferior da tela que acompanha a importação de processos por OAB
// rodando em background (ImportacoesController + ImportacaoProcessosJob). O estado vem
// do servidor, então a barra sobrevive a troca de página/F5: initImportacaoBar() (chamado
// pelo layout em toda página) retoma o acompanhamento se houver importação em andamento.
// Ao terminar, mostra um aviso com link para o resultado e dispara o evento
// `importacao:concluida` (detail = resumo) — a tela de processos o usa para recarregar a lista.
import { apiFetch } from './api.js';
import { esc } from './utils.js';

const POLL_MS = 3000;
const ATIVOS = ['Pendente', 'Buscando', 'Importando'];

let _timer = null;
let _id = null;

export async function initImportacaoBar() {
  try {
    const ativa = await apiFetch('/importacoes/ativa');
    if (ativa) acompanharImportacao(ativa);
  } catch { /* não crítico */ }
}

export function acompanharImportacao(importacao) {
  clearTimeout(_timer);
  _id = importacao.id;
  document.getElementById('importacaoToast')?.remove();
  if (ATIVOS.includes(importacao.status)) {
    renderBarra(importacao);
    agendar();
  } else {
    concluir(importacao);
  }
}

function agendar() {
  _timer = setTimeout(atualizar, POLL_MS);
}

async function atualizar() {
  if (!_id) return;
  let importacao;
  try {
    importacao = await apiFetch(`/importacoes/${_id}`);
  } catch {
    agendar(); // falha transitória de rede: tenta de novo
    return;
  }
  if (ATIVOS.includes(importacao.status)) {
    renderBarra(importacao);
    agendar();
  } else {
    concluir(importacao);
  }
}

function textoStatus(i) {
  const oab = `${esc(i.numeroOab)}/${esc(i.uf)}`;
  if (i.status === 'Pendente') return `Preparando a importação da OAB ${oab}…`;
  if (i.status === 'Buscando') {
    return i.total > 0
      ? `Pesquisando processos da OAB ${oab}… <strong>${i.total.toLocaleString('pt-BR')} encontrados</strong>`
      : `Pesquisando processos da OAB ${oab} nos tribunais…`;
  }
  return `Importando processos da OAB ${oab}… <strong>${i.processados.toLocaleString('pt-BR')} de ${i.total.toLocaleString('pt-BR')}</strong>`;
}

function renderBarra(i) {
  injetarEstilos();
  let barra = document.getElementById('importacaoBar');
  if (!barra) {
    barra = document.createElement('div');
    barra.id = 'importacaoBar';
    barra.className = 'importacao-bar';
    barra.setAttribute('role', 'status');
    barra.setAttribute('aria-live', 'polite');
    barra.innerHTML = `
      <span class="importacao-bar-spinner" aria-hidden="true"></span>
      <div class="importacao-bar-corpo">
        <div class="importacao-bar-texto"></div>
        <div class="importacao-bar-trilho"><div class="importacao-bar-progresso"></div></div>
      </div>`;
    document.body.appendChild(barra);
    document.body.classList.add('com-importacao-bar');
  }

  const determinado = i.status === 'Importando' && i.total > 0;
  const pct = determinado ? Math.round((i.processados / i.total) * 100) : 0;
  barra.querySelector('.importacao-bar-texto').innerHTML = textoStatus(i);
  const trilho = barra.querySelector('.importacao-bar-trilho');
  trilho.classList.toggle('indeterminado', !determinado);
  barra.querySelector('.importacao-bar-progresso').style.width = determinado ? `${pct}%` : '';
}

function removerBarra() {
  document.getElementById('importacaoBar')?.remove();
  document.body.classList.remove('com-importacao-bar');
}

function concluir(i) {
  clearTimeout(_timer);
  _id = null;
  removerBarra();
  mostrarAviso(i);
  window.dispatchEvent(new CustomEvent('importacao:concluida', { detail: i }));
}

function mostrarAviso(i) {
  injetarEstilos();
  document.getElementById('importacaoToast')?.remove();

  const ok = i.status === 'Concluida';
  let resumo;
  if (!ok) {
    resumo = esc(i.mensagemErro || 'A importação falhou.');
  } else if (i.total === 0) {
    resumo = 'Nenhum processo novo encontrado para esta OAB.';
  } else {
    const partes = [`${i.importados} processo(s) importado(s)`];
    if (i.jaCadastrados > 0) partes.push(`${i.jaCadastrados} já cadastrado(s)`);
    if (i.erros > 0) partes.push(`${i.erros} com erro`);
    resumo = partes.join(', ') + '.';
  }

  const toast = document.createElement('div');
  toast.id = 'importacaoToast';
  toast.className = `importacao-toast${ok ? '' : ' erro'}`;
  toast.setAttribute('role', 'alert');
  toast.innerHTML = `
    <span class="importacao-toast-icone" aria-hidden="true">${ok ? '✅' : '⚠️'}</span>
    <div class="importacao-toast-corpo">
      <div class="importacao-toast-titulo">${ok ? 'Importação concluída' : 'Importação interrompida'}</div>
      <div class="importacao-toast-texto">${resumo}</div>
      <a class="importacao-toast-link" href="/pages/importacao.html?id=${encodeURIComponent(i.id)}">Ver resultado</a>
    </div>
    <button type="button" class="importacao-toast-fechar" aria-label="Fechar">×</button>`;
  toast.querySelector('.importacao-toast-fechar').addEventListener('click', () => toast.remove());
  document.body.appendChild(toast);
}

function injetarEstilos() {
  if (document.getElementById('importacaoBarStyles')) return;
  const s = document.createElement('style');
  s.id = 'importacaoBarStyles';
  s.textContent = `
    .importacao-bar {
      position: fixed; left: var(--sidebar-width); right: 0; bottom: 0; z-index: 150;
      display: flex; align-items: center; gap: 12px;
      padding: 10px 20px; background: var(--color-surface);
      border-top: 1px solid var(--color-border); box-shadow: 0 -2px 8px rgba(0,0,0,.08);
      font-size: 13px; color: var(--color-text);
    }
    .importacao-bar-corpo { flex: 1; min-width: 0; }
    .importacao-bar-texto { white-space: nowrap; overflow: hidden; text-overflow: ellipsis; margin-bottom: 6px; }
    .importacao-bar-trilho { position: relative; height: 6px; border-radius: 999px; background: var(--color-primary-light); overflow: hidden; }
    .importacao-bar-progresso { height: 100%; width: 0; background: var(--color-primary); border-radius: 999px; transition: width .4s ease; }
    .importacao-bar-trilho.indeterminado .importacao-bar-progresso {
      position: absolute; width: 30%; animation: importacaoIndeterminado 1.4s ease-in-out infinite;
    }
    @keyframes importacaoIndeterminado { from { left: -30%; } to { left: 100%; } }
    .importacao-bar-spinner {
      width: 16px; height: 16px; flex-shrink: 0; border-radius: 50%;
      border: 2px solid var(--color-primary-light); border-top-color: var(--color-primary);
      animation: importacaoGira .8s linear infinite;
    }
    @keyframes importacaoGira { to { transform: rotate(360deg); } }
    body.com-importacao-bar .main-content { padding-bottom: 80px; }

    .importacao-toast {
      position: fixed; right: 24px; bottom: 24px; z-index: 9999; max-width: 360px;
      display: flex; align-items: flex-start; gap: 12px; padding: 14px 16px;
      background: var(--color-surface); border: 1px solid var(--color-border);
      border-left: 4px solid var(--color-success); border-radius: 10px;
      box-shadow: 0 8px 24px rgba(0,0,0,.15); animation: importacaoEntra .25s ease;
    }
    .importacao-toast.erro { border-left-color: var(--color-danger); }
    @keyframes importacaoEntra { from { transform: translateY(16px); opacity: 0; } to { transform: none; opacity: 1; } }
    .importacao-toast-icone { font-size: 18px; line-height: 1.2; }
    .importacao-toast-corpo { flex: 1; min-width: 0; }
    .importacao-toast-titulo { font-weight: 700; font-size: 14px; }
    .importacao-toast-texto { font-size: 13px; color: var(--color-text-muted); margin: 2px 0 6px; }
    .importacao-toast-link { font-size: 13px; font-weight: 600; }
    .importacao-toast-fechar {
      background: none; border: none; font-size: 20px; line-height: 1; cursor: pointer;
      color: var(--color-text-muted); padding: 0 2px; min-width: 24px; min-height: 24px;
    }

    @media (max-width: 768px) {
      .importacao-bar { left: 0; bottom: var(--bottom-nav-height); padding: 8px 16px; }
      body.com-importacao-bar .main-content { padding-bottom: calc(var(--bottom-nav-height) + 80px); }
      .importacao-toast { left: 16px; right: 16px; bottom: calc(var(--bottom-nav-height) + 16px); max-width: none; }
    }
  `;
  document.head.appendChild(s);
}
