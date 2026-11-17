// Configurações → Integrações: conexão do escritório com o Contratos (assinatura eletrônica).
// O Admin cola a chave de API (ctr_…), o Causify confere no Contratos e guarda cifrada; depois
// cadastra no Contratos o endereço de webhook mostrado aqui e cola de volta o segredo (whsec_…).
import { apiFetch } from './api.js';
import { esc } from './utils.js';
import { notify } from './honorarios.js';
import { abrirEditorTemplates } from './contratos-embed.js';

const API = '/assinatura-digital/integracao';
let raiz = null;
let estado = null;
let trocandoChave = false;
let ocupado = false;

export async function montarIntegracoes(container) {
  raiz = container;
  raiz.addEventListener('click', aoClicar);
  raiz.addEventListener('submit', aoEnviar);
  await carregar();
}

async function carregar() {
  raiz.innerHTML = '<p class="asg-aviso">Carregando…</p>';
  try {
    estado = await apiFetch(API);
  } catch (err) {
    raiz.innerHTML = `<div class="alert alert-error">${esc(err?.message || 'Não foi possível carregar.')}</div>`;
    return;
  }
  render();
}

function fmt(iso) {
  if (!iso) return '—';
  const d = new Date(iso);
  return Number.isNaN(d.getTime()) ? '—' : d.toLocaleString('pt-BR', { dateStyle: 'short', timeStyle: 'short' });
}

function render() {
  const e = estado;
  let html = `
    <div class="config-section asg-int">
      <div class="config-section-title">Contratos · assinatura eletrônica</div>
      <p class="asg-aviso">Envie contratos de honorários para assinatura por e-mail ou WhatsApp a partir de um template
        do Contratos, já preenchido com os dados do cliente e do contrato. O PDF assinado volta sozinho para Documentos.</p>`;

  if (!e.disponivel) {
    html += '<p class="asg-aviso" style="margin-top:12px">A integração ainda não está disponível neste servidor.</p></div>';
    raiz.innerHTML = html;
    return;
  }

  if (!e.podeConfigurar) {
    html += `
      <div class="asg-int-status" style="margin-top:12px">
        <span class="asg-ponto ${e.conectado ? 'ok' : ''}" aria-hidden="true"></span>
        ${e.conectado ? 'Conectado. Use “Enviar para assinatura” no contrato de honorários, no contato ou no processo.' : 'Não conectado. Peça a um administrador do escritório para conectar.'}
      </div>
      ${e.conectado ? `
        <div class="asg-int-bloco">
          <div class="config-section-title" style="font-size:14px">Templates de contrato</div>
          <p class="asg-aviso">Monte e ajuste os modelos de contrato usados na assinatura, sem sair do Causify.</p>
          <button type="button" class="btn btn-secondary" data-acao="templates">Abrir editor de templates</button>
        </div>` : ''}
    </div>`;
    raiz.innerHTML = html;
    return;
  }

  const formChave = `
    <form class="asg-int-linha" data-form="chave" autocomplete="off">
      <input type="password" name="apiKey" class="form-control" placeholder="ctr_…" aria-label="Chave de API do Contratos"
        autocomplete="off" spellcheck="false" required>
      <button type="submit" class="btn btn-primary">${e.conectado ? 'Trocar chave' : 'Conectar'}</button>
      ${e.conectado ? '<button type="button" class="btn btn-secondary" data-acao="cancelar-troca">Cancelar</button>' : ''}
    </form>`;

  if (!e.conectado) {
    html += `
      <ol class="asg-int-passos">
        <li>No Contratos (plano Empresa), abra <strong>Integrações → Chaves de API</strong> e clique em <strong>Nova chave</strong>
          (ex.: nome “Causify”).</li>
        <li>Copie a chave <code>ctr_…</code> — ela aparece uma única vez — e cole abaixo.</li>
      </ol>
      ${formChave}
      ${e.contratosUrl ? `<p class="asg-aviso" style="margin-top:10px"><a href="${esc(e.contratosUrl)}/integrations.html" target="_blank" rel="noopener">Abrir as Integrações do Contratos ↗</a></p>` : ''}
    </div>`;
    raiz.innerHTML = html;
    return;
  }

  const uso = e.uso;
  html += `
    <div class="asg-int-status" style="margin-top:12px">
      <span class="asg-ponto ${e.avisoUso ? 'warn' : 'ok'}" aria-hidden="true"></span>
      <span><strong>Conectado</strong> à conta ${esc(e.organizacao || '')}${e.plano ? ` (plano ${esc(e.plano)})` : ''}</span>
    </div>
    ${e.avisoUso ? `<div class="alert alert-error" style="margin-top:10px">${esc(e.avisoUso)}</div>` : ''}
    <div class="asg-int-grade">
      <div><span>Chave</span>ctr_••••${esc(e.chaveFinal || '')}</div>
      <div><span>Conectado em</span>${esc(fmt(e.conectadoEm))}</div>
      ${uso ? `<div><span>Envios disponíveis</span>${uso.enviosDisponiveis}${uso.limite != null ? ` <small class="asg-aviso">(${uso.usados} de ${uso.limite} no ciclo${uso.creditos ? ` + ${uso.creditos} créditos` : ''})</small>` : ''}</div>` : ''}
    </div>
    ${uso?.pagamentoPendente ? '<div class="alert alert-error">A conta do Contratos está com pagamento pendente: os envios ficam bloqueados até regularizar.</div>' : ''}
    ${trocandoChave ? formChave : `
      <div class="asg-int-linha">
        <button type="button" class="btn btn-secondary" data-acao="trocar">Trocar chave</button>
        <button type="button" class="btn btn-secondary" data-acao="desconectar" style="color:var(--color-danger)">Desconectar</button>
      </div>`}

    <div class="asg-int-bloco">
      <div class="config-section-title" style="font-size:14px">Aviso na hora (webhook)</div>
      <p class="asg-aviso">Sem o webhook, o Causify confere os contratos a cada 2 horas. Com ele, o status e o PDF assinado chegam na hora.</p>
      <ol class="asg-int-passos">
        <li>No Contratos, em <strong>Integrações → Webhooks</strong>, clique em <strong>Novo webhook</strong> e cole este endereço
          (todos os eventos):</li>
      </ol>
      <div class="asg-int-linha">
        <input type="text" class="form-control" id="asgWebhookUrl" readonly value="${esc(e.webhookUrl || '')}" aria-label="Endereço do webhook">
        <button type="button" class="btn btn-secondary" data-acao="copiar">Copiar</button>
      </div>
      <ol class="asg-int-passos" start="2">
        <li>Copie o <strong>segredo</strong> (<code>whsec_…</code>) que o Contratos mostra e cole aqui:</li>
      </ol>
      <form class="asg-int-linha" data-form="segredo" autocomplete="off">
        <input type="password" name="segredo" class="form-control" placeholder="${e.temSegredoWebhook ? 'Segredo cadastrado — cole outro para trocar' : 'whsec_…'}"
          aria-label="Segredo do webhook" autocomplete="off" spellcheck="false" required>
        <button type="submit" class="btn btn-primary">Salvar segredo</button>
      </form>
      <div class="asg-int-status" style="margin-top:12px">
        <span class="asg-ponto ${e.ultimoWebhookEm ? 'ok' : e.temSegredoWebhook ? 'warn' : ''}" aria-hidden="true"></span>
        ${e.ultimoWebhookEm
          ? `Último aviso recebido: ${esc(fmt(e.ultimoWebhookEm))}`
          : e.temSegredoWebhook
            ? 'Segredo salvo. Use “Enviar teste” no Contratos para conferir a conexão.'
            : 'Webhook ainda não configurado.'}
      </div>
    </div>

    <div class="asg-int-bloco">
      <div class="config-section-title" style="font-size:14px">Templates de contrato</div>
      <p class="asg-aviso">O editor de templates do Contratos abre aqui dentro: variáveis, pré-visualização e versões.
        O que for publicado já aparece em “Enviar para assinatura”. Administradores editam, publicam e arquivam;
        advogados editam e publicam; colaboradores só editam rascunhos.</p>
      ${e.origemEmbed ? `
        <ol class="asg-int-passos">
          <li>No Contratos, em <strong>Integrações → Embed de templates</strong>, cadastre este site (uma vez só):</li>
        </ol>
        <div class="asg-int-linha">
          <input type="text" class="form-control" id="asgOrigemEmbed" readonly value="${esc(e.origemEmbed)}" aria-label="Site do Causify para o embed">
          <button type="button" class="btn btn-secondary" data-acao="copiar-origem">Copiar</button>
        </div>` : ''}
      <div class="asg-int-linha" style="margin-top:12px">
        <button type="button" class="btn btn-primary" data-acao="templates">Abrir editor de templates</button>
      </div>
    </div>
  </div>`;
  raiz.innerHTML = html;
}

async function salvar(corpo, sucesso) {
  if (ocupado) return;
  ocupado = true;
  raiz.querySelectorAll('button').forEach(b => { b.disabled = true; });
  try {
    estado = await apiFetch(API, { method: 'PUT', body: corpo });
    trocandoChave = false;
    render();
    notify(sucesso);
  } catch (err) {
    raiz.querySelectorAll('button').forEach(b => { b.disabled = false; });
    notify(err?.message || 'Não foi possível salvar.', 'error');
  } finally {
    ocupado = false;
  }
}

function aoEnviar(ev) {
  const form = ev.target.closest('form[data-form]');
  if (!form) return;
  ev.preventDefault();
  if (form.dataset.form === 'chave') {
    salvar({ apiKey: form.apiKey.value.trim() }, 'Contratos conectado.');
  } else {
    salvar({ webhookSecret: form.segredo.value.trim() }, 'Segredo do webhook salvo.');
  }
}

async function aoClicar(ev) {
  const acao = ev.target.closest('[data-acao]')?.dataset.acao;
  if (!acao) return;
  if (acao === 'trocar') { trocandoChave = true; render(); raiz.querySelector('input[name="apiKey"]')?.focus(); }
  else if (acao === 'cancelar-troca') { trocandoChave = false; render(); }
  else if (acao === 'templates') abrirEditorTemplates();
  else if (acao === 'copiar' || acao === 'copiar-origem') {
    const input = raiz.querySelector(acao === 'copiar' ? '#asgWebhookUrl' : '#asgOrigemEmbed');
    try { await navigator.clipboard.writeText(input.value); } catch { input.select(); document.execCommand('copy'); }
    notify('Endereço copiado.');
  } else if (acao === 'desconectar') {
    if (!confirm('Desconectar o Contratos? Os contratos já enviados continuam no Contratos, mas o Causify deixa de acompanhá-los.')) return;
    try {
      await apiFetch(API, { method: 'DELETE' });
      notify('Contratos desconectado.');
      await carregar();
    } catch (err) {
      notify(err?.message || 'Não foi possível desconectar.', 'error');
    }
  }
}
