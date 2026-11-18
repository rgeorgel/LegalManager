// Assinatura eletrônica pelo Contratos, no detalhe do contrato de honorários, do contato e do
// processo (a "origem" do envio): cartão com os envios
// (status de cada signatário, atualizar, enviar rascunho, cancelar, reenviar link, abrir o PDF
// assinado) e o modal de envio — escolhe o template do Contratos, as variáveis vêm preenchidas com
// os dados do contrato (fonte trocável por variável, lembrada por template) e os signatários sugeridos.
import { apiFetch } from './api.js';
import { recursoLiberado, RECURSOS } from './recursos.js';
import { esc } from './utils.js';
import { notify } from './honorarios.js';
import { abrirEditorTemplates } from './contratos-embed.js';

const API = '/assinatura-digital';

const STATUS = {
  Rascunho: { rotulo: 'Rascunho', cls: '' },
  Enviada: { rotulo: 'Aguardando assinaturas', cls: 'warn' },
  ParcialmenteAssinada: { rotulo: 'Assinado em parte', cls: 'warn' },
  Concluida: { rotulo: 'Assinado', cls: 'ok' },
  Recusada: { rotulo: 'Recusado', cls: 'danger' },
  Expirada: { rotulo: 'Expirado', cls: 'danger' },
  Cancelada: { rotulo: 'Cancelado', cls: '' },
};
const STATUS_SIG = {
  pending: { rotulo: 'Aguardando', cls: '' },
  viewed: { rotulo: 'Abriu', cls: 'warn' },
  signed: { rotulo: 'Assinou', cls: 'ok' },
  declined: { rotulo: 'Recusou', cls: 'danger' },
  delivery_failed: { rotulo: 'Link não chegou', cls: 'danger' },
};
const CANAIS = { email: 'E-mail', whatsapp: 'WhatsApp', sms: 'SMS' };
const ACOES = { sign: 'Assina', sign_as_witness: 'Testemunha', approve: 'Aprova', recognize: 'Reconhece' };
const EM_ABERTO = ['Rascunho', 'Enviada', 'ParcialmenteAssinada'];

let ctx = null; // { origem: { contratoHonorarioId | contatoId | processoId }, container, botao, aoMudar }
let integracao = null;
let solicitacoes = [];
let overlay = null;
let preparado = null;
let enviando = false;

/** Parâmetros da origem para a URL: contratoHonorarioId=…, contatoId=… ou processoId=…. */
function qsOrigem() {
  return Object.entries(ctx.origem).filter(([, v]) => v).map(([k, v]) => `${k}=${encodeURIComponent(v)}`).join('&');
}

/**
 * Liga o botão "Enviar para assinatura" e o cartão. Some tudo se o escritório não tem o recurso
 * liberado (em teste) ou se a integração não existe no servidor.
 * origem: { contratoHonorarioId } | { contatoId } | { processoId } — `contratoId` vale como contratoHonorarioId.
 */
export async function iniciarAssinaturas({ origem, contratoId, container, botao, aoMudar }) {
  ctx = { origem: origem || { contratoHonorarioId: contratoId }, container, botao, aoMudar };
  if (!await recursoLiberado(RECURSOS.assinaturaDigital)) return;
  try {
    integracao = await apiFetch(`${API}/integracao`);
  } catch {
    return;
  }
  if (!integracao?.disponivel) return;
  botao.hidden = false;
  botao.addEventListener('click', abrirEnvio);
  container.addEventListener('click', aoClicarCartao);
  await recarregar();
}

async function recarregar() {
  try {
    solicitacoes = await apiFetch(`${API}/solicitacoes?${qsOrigem()}`) || [];
  } catch {
    solicitacoes = [];
  }
  renderCartao();
}

// ── Cartão ─────────────────────────────────────────────────────────────────────────

function fmt(iso) {
  if (!iso) return '';
  const d = new Date(iso);
  return Number.isNaN(d.getTime()) ? '' : d.toLocaleString('pt-BR', { dateStyle: 'short', timeStyle: 'short' });
}

function renderCartao() {
  const el = ctx.container;
  if (!solicitacoes.length) {
    el.hidden = true;
    el.innerHTML = '';
    return;
  }
  el.hidden = false;
  el.innerHTML = `
    <div class="asg-card" aria-labelledby="asgTitulo">
      <div class="asg-card-topo"><div class="asg-card-titulo" id="asgTitulo">Assinaturas</div></div>
      ${solicitacoes.map(renderItem).join('')}
    </div>`;
}

function renderItem(s) {
  const st = STATUS[s.status] || { rotulo: s.status, cls: '' };
  const total = s.signatarios.length;
  const rotulo = s.status === 'ParcialmenteAssinada' || s.status === 'Enviada'
    ? `${st.rotulo} · ${s.assinados}/${total}` : st.rotulo;
  const aberto = EM_ABERTO.includes(s.status);
  const enviado = s.status !== 'Rascunho';
  const sigs = s.signatarios.map(g => {
    const sg = STATUS_SIG[g.status] || { rotulo: g.status, cls: '' };
    const quando = g.status === 'signed' ? fmt(g.assinadoEm) : g.status === 'declined' ? fmt(g.recusadoEm) : '';
    const podeReenviar = aberto && enviado && ['pending', 'viewed', 'delivery_failed'].includes(g.status);
    return `
      <li class="asg-signatario">
        <span class="asg-signatario-nome">${esc(g.nome)}</span>
        <span class="asg-signatario-papel">${esc([g.papel, CANAIS[g.canal] || g.canal].filter(Boolean).join(' · '))}</span>
        ${podeReenviar ? `<button type="button" class="asg-link" data-asg="reenviar" data-id="${esc(s.id)}" data-sig="${esc(g.id)}">Reenviar link</button>` : ''}
        <span class="asg-selo ${sg.cls}" title="${esc(g.motivoRecusa || '')}">${esc(sg.rotulo)}${quando ? ` ${esc(quando)}` : ''}</span>
      </li>`;
  }).join('');

  const meta = [
    s.numero ? `Nº ${s.numero} no Contratos` : '',
    s.criadoPor ? `por ${s.criadoPor}` : '',
    s.enviadaEm ? `enviado em ${fmt(s.enviadaEm)}` : `criado em ${fmt(s.criadoEm)}`,
    s.concluidaEm ? `concluído em ${fmt(s.concluidaEm)}` : '',
  ].filter(Boolean).join(' · ');

  return `
    <div class="asg-item" data-id="${esc(s.id)}">
      <div class="asg-item-topo">
        <div style="min-width:0">
          <div class="asg-item-nome">${esc(s.titulo)}</div>
          <div class="asg-item-meta">${esc(meta)}</div>
        </div>
        <span class="asg-selo ${st.cls}">${esc(rotulo)}</span>
      </div>
      <ul class="asg-signatarios">${sigs}</ul>
      ${s.erro ? `<div class="asg-erro">${esc(s.erro)}</div>` : ''}
      ${s.motivoRecusa && s.status === 'Recusada' ? `<div class="asg-erro">Motivo da recusa: ${esc(s.motivoRecusa)}</div>` : ''}
      <div class="asg-acoes">
        ${s.documentoAssinadoId ? `<a class="btn btn-primary btn-sm" href="/pages/documentos.html?doc=${encodeURIComponent(s.documentoAssinadoId)}">Abrir PDF assinado</a>` : ''}
        ${s.status === 'Concluida' && !s.documentoAssinadoId ? '<span class="asg-item-meta">O PDF assinado chega a Documentos em instantes.</span>' : ''}
        ${s.status === 'Rascunho' ? `<button type="button" class="btn btn-primary btn-sm" data-asg="enviar" data-id="${esc(s.id)}">Enviar para assinatura</button>` : ''}
        <button type="button" class="btn btn-secondary btn-sm" data-asg="atualizar" data-id="${esc(s.id)}">Atualizar</button>
        ${aberto ? `<button type="button" class="btn btn-secondary btn-sm" data-asg="cancelar" data-id="${esc(s.id)}">Cancelar assinatura</button>` : ''}
      </div>
    </div>`;
}

async function aoClicarCartao(e) {
  const b = e.target.closest('[data-asg]');
  if (!b || b.disabled) return;
  const id = b.dataset.id;
  const acao = b.dataset.asg;
  if (acao === 'cancelar' && !confirm('Cancelar a assinatura? Os links param de funcionar e quem ainda não assinou não poderá mais assinar.')) return;
  b.disabled = true;
  try {
    if (acao === 'reenviar') {
      await apiFetch(`${API}/solicitacoes/${id}/signatarios/${encodeURIComponent(b.dataset.sig)}/reenviar`, { method: 'POST' });
      notify('Link reenviado.');
      return;
    }
    const caminho = { atualizar: 'atualizar', enviar: 'enviar', cancelar: 'cancelar' }[acao];
    const s = await apiFetch(`${API}/solicitacoes/${id}/${caminho}`, { method: 'POST' });
    solicitacoes = solicitacoes.map(x => (x.id === s.id ? s : x));
    renderCartao();
    if (acao === 'enviar') notify('Contrato enviado para assinatura.');
    if (acao === 'cancelar') notify('Assinatura cancelada.');
    if (acao !== 'atualizar') ctx.aoMudar?.();
  } catch (err) {
    notify(err?.message || 'Não foi possível concluir a ação.', 'error');
    // O status pode ter mudado mesmo com falha ao salvar o PDF assinado.
    if (acao === 'atualizar') await recarregar();
  } finally {
    b.disabled = false;
  }
}

// ── Modal de envio ─────────────────────────────────────────────────────────────────

function montarModal() {
  if (overlay) return;
  overlay = document.createElement('div');
  overlay.className = 'modal-overlay';
  overlay.id = 'asgModal';
  overlay.innerHTML = `
    <div class="modal asg-modal" role="dialog" aria-modal="true" aria-labelledby="asgModalTitulo">
      <div class="modal-header">
        <h2 class="modal-title" id="asgModalTitulo" style="margin:0">Enviar para assinatura</h2>
        <button type="button" class="modal-close" data-asg-modal="fechar" aria-label="Fechar">&times;</button>
      </div>
      <div class="modal-body" id="asgCorpo"></div>
      <div class="modal-footer" id="asgRodape"></div>
    </div>`;
  document.body.appendChild(overlay);
  overlay.addEventListener('click', e => {
    if (e.target === overlay) return fechar();
    const a = e.target.closest('[data-asg-modal]')?.dataset.asgModal;
    if (a === 'fechar') fechar();
    else if (a === 'add-sig') adicionarSignatario();
    else if (a === 'rem-sig') { e.target.closest('.asg-sig')?.remove(); }
    else if (a === 'rascunho') enviar(false);
    else if (a === 'enviar') enviar(true);
    else if (a === 'editar-tpl') editarTemplates(overlay.querySelector('#asgTemplate')?.value || null);
    else if (a === 'novo-tpl') editarTemplates(null);
  });
  overlay.addEventListener('change', e => {
    if (e.target.id === 'asgTemplate') carregarTemplate(e.target.value);
    else if (e.target.matches('.asg-fonte')) {
      const input = overlay.querySelector(`.asg-valor[data-chave="${CSS.escape(e.target.dataset.chave)}"]`);
      if (input && e.target.value) {
        input.value = preparado?.valoresFontes?.[e.target.value] ?? '';
        if (input.value.trim()) desmarcar(input);
      }
    } else if (e.target.matches('.asg-canal')) atualizarCanal(e.target.closest('.asg-sig'));
  });
  overlay.addEventListener('input', e => {
    if (e.target.classList?.contains('asg-invalido') && e.target.value.trim()) desmarcar(e.target);
  });
  overlay.addEventListener('keydown', e => {
    e.stopPropagation();
    if (e.key === 'Escape') fechar();
  });
}

function fechar() {
  if (overlay) overlay.classList.remove('open');
  preparado = null;
}

function corpo(html) { overlay.querySelector('#asgCorpo').innerHTML = html; }
function rodape(html) { overlay.querySelector('#asgRodape').innerHTML = html; }

async function abrirEnvio() {
  montarModal();
  overlay.classList.add('open');
  preparado = null;
  rodape('<button type="button" class="btn btn-secondary" data-asg-modal="fechar">Fechar</button>');

  if (!integracao.conectado) {
    corpo(`
      <p class="asg-aviso">Para enviar contratos para assinatura eletrônica, o escritório precisa conectar a sua conta do
        <strong>Contratos</strong> (plano Empresa).</p>
      ${integracao.podeConfigurar
        ? '<p><a class="btn btn-primary" href="/pages/configuracoes.html?tab=integracoes">Conectar em Configurações → Integrações</a></p>'
        : '<p class="asg-aviso">Peça a um administrador do escritório para conectar em Configurações → Integrações.</p>'}`);
    return;
  }

  await carregarModelos();
}

/** Lista os templates publicados e monta o seletor; `selecionar` escolhe um (ex.: o que acabou de ser publicado). */
async function carregarModelos(selecionar) {
  corpo('<p class="asg-aviso">Carregando os templates do Contratos…</p>');
  let modelos;
  try {
    modelos = await apiFetch(`${API}/modelos`);
  } catch (err) {
    corpo(`<div class="alert alert-error">${esc(err?.message || 'Não foi possível listar os templates.')}</div>`);
    return;
  }
  const botoesEditor = `
    <div class="asg-tpl-acoes">
      ${modelos.length ? '<button type="button" class="asg-link" data-asg-modal="editar-tpl">Editar template</button>' : ''}
      <button type="button" class="asg-link" data-asg-modal="novo-tpl">+ Novo template</button>
    </div>`;
  if (!modelos.length) {
    corpo(`<p class="asg-aviso">Nenhum template publicado no Contratos ainda. Crie um modelo de contrato com as variáveis
      (cliente, valor, processo…) e publique — ele aparece aqui na hora.</p>
      <p><button type="button" class="btn btn-primary" data-asg-modal="novo-tpl">Criar template</button></p>`);
    return;
  }
  const escolhido = modelos.some(m => m.id === selecionar) ? selecionar : modelos.length === 1 ? modelos[0].id : '';
  corpo(`
    <div class="form-group" style="margin:0">
      <div class="asg-tpl-rotulo">
        <label class="form-label" for="asgTemplate" style="margin:0">Template do Contratos</label>
        ${botoesEditor}
      </div>
      <select id="asgTemplate" class="form-control">
        ${modelos.length > 1 ? '<option value="">Escolha…</option>' : ''}
        ${modelos.map(m => `<option value="${esc(m.id)}" ${m.id === escolhido ? 'selected' : ''}>${esc(m.nome)} (v${m.versao})</option>`).join('')}
      </select>
    </div>
    <div id="asgForm"></div>`);
  if (escolhido) carregarTemplate(escolhido);
}

/**
 * Abre o editor de templates do Contratos por cima do modal. Ao fechar, se algo foi publicado ou
 * arquivado, recarrega a lista (escolhendo o template mexido) e repreenche o formulário.
 */
function editarTemplates(templateId) {
  let ultimo = templateId;
  abrirEditorTemplates({
    templateId,
    aoMudar: (_tipo, tpl) => { if (tpl?.id) ultimo = tpl.id; },
    aoFechar: (houveMudanca) => {
      if (houveMudanca && overlay?.classList.contains('open')) carregarModelos(ultimo);
    },
  });
}

async function carregarTemplate(templateId) {
  const alvo = overlay.querySelector('#asgForm');
  preparado = null;
  rodape('<button type="button" class="btn btn-secondary" data-asg-modal="fechar">Cancelar</button>');
  if (!templateId) { alvo.innerHTML = ''; return; }
  alvo.innerHTML = '<p class="asg-aviso">Preenchendo com os dados do Causify…</p>';
  try {
    preparado = await apiFetch(`${API}/preparar?${qsOrigem()}&templateId=${encodeURIComponent(templateId)}`);
  } catch (err) {
    alvo.innerHTML = `<div class="alert alert-error">${esc(err?.message || 'Não foi possível abrir o template.')}</div>`;
    return;
  }
  alvo.innerHTML = formularioHtml(preparado);
  overlay.querySelectorAll('.asg-sig').forEach(atualizarCanal);
  rodape(`
    <div class="alert alert-error" id="asgErro" hidden style="flex-basis:100%;margin:0"></div>
    <button type="button" class="btn btn-secondary" data-asg-modal="fechar">Cancelar</button>
    <button type="button" class="btn btn-secondary" data-asg-modal="rascunho" title="Cria no Contratos sem enviar: dá para revisar lá antes">Salvar rascunho</button>
    <button type="button" class="btn btn-primary" data-asg-modal="enviar">Enviar para assinatura</button>`);
}

function opcoesFontes(p, atual) {
  const grupos = {};
  p.fontes.forEach(f => { (grupos[f.grupo] ||= []).push(f); });
  return `<option value="">Digitar</option>` + Object.entries(grupos).map(([g, fs]) =>
    `<optgroup label="${esc(g)}">${fs.map(f =>
      `<option value="${esc(f.chave)}" ${f.chave === atual ? 'selected' : ''}>${esc(`${g} · ${f.rotulo}`)}</option>`).join('')}</optgroup>`).join('');
}

function inputVariavel(v) {
  const base = `class="form-control asg-valor" data-chave="${esc(v.chave)}" data-tipo="${esc(v.tipo)}" aria-label="${esc(v.rotulo)}" ${v.obrigatoria ? 'required' : ''}`;
  if (v.tipo === 'date') return `<input type="date" ${base} value="${esc(v.valor || '')}">`;
  const modo = v.tipo === 'money' || v.tipo === 'number' ? 'inputmode="decimal"' : '';
  return `<input type="text" ${base} ${modo} maxlength="2000" value="${esc(v.valor || '')}">`;
}

function formularioHtml(p) {
  const variaveis = p.variaveis.map(v => `
    <div class="asg-var">
      <div class="asg-var-rotulo">${esc(v.rotulo)}${v.obrigatoria ? ' *' : ''} <small>${esc(v.chave)}</small></div>
      <select class="form-control asg-fonte" data-chave="${esc(v.chave)}" aria-label="De onde vem: ${esc(v.rotulo)}">${opcoesFontes(p, v.fonte)}</select>
      ${inputVariavel(v)}
    </div>`).join('');

  return `
    <div class="form-group" style="margin:0">
      <label class="form-label" for="asgTitulo2">Título</label>
      <input type="text" id="asgTitulo2" class="form-control" maxlength="200" value="${esc(p.titulo)}">
    </div>
    <div>
      <div class="asg-secao-titulo">Dados do contrato</div>
      ${variaveis || '<p class="asg-aviso">Este template não tem variáveis.</p>'}
    </div>
    <div>
      <div class="asg-secao-titulo">Quem assina</div>
      <div id="asgSigs">${p.signatarios.map(sigHtml).join('')}</div>
      <button type="button" class="btn btn-secondary btn-sm" data-asg-modal="add-sig">+ Adicionar signatário</button>
    </div>
    <div class="asg-opcoes">
      <label class="asg-check"><input type="checkbox" id="asgOrdem"> Assinar na ordem da lista</label>
      <div class="form-group" style="margin:0">
        <label class="form-label" for="asgPrazo">Prazo para assinar <small>(opcional)</small></label>
        <input type="date" id="asgPrazo" class="form-control">
      </div>
    </div>
    <div class="form-group" style="margin:0">
      <label class="form-label" for="asgMensagem">Mensagem no convite <small>(opcional)</small></label>
      <textarea id="asgMensagem" class="form-control" rows="2" maxlength="500" placeholder="Sem mensagem, vai a padrão da sua conta no Contratos."></textarea>
    </div>`;
}

function sigHtml(s = {}) {
  const canal = s.canal || 'email';
  return `
    <div class="asg-sig">
      <input type="text" class="form-control asg-sig-nome" maxlength="100" placeholder="Nome completo" aria-label="Nome" value="${esc(s.nome || '')}">
      <select class="form-control asg-canal" aria-label="Enviar por">
        ${Object.entries(CANAIS).map(([k, r]) => `<option value="${k}" ${k === canal ? 'selected' : ''}>${r}</option>`).join('')}
      </select>
      <input type="email" class="form-control asg-sig-email" maxlength="200" placeholder="e-mail" aria-label="E-mail" value="${esc(s.email || '')}">
      <input type="tel" class="form-control asg-sig-tel" maxlength="30" placeholder="(11) 98765-4321" aria-label="Celular" value="${esc(s.telefone || '')}">
      <input type="text" class="form-control asg-sig-papel" maxlength="100" placeholder="Papel (ex.: Contratante)" aria-label="Papel" value="${esc(s.papel || '')}">
      <select class="form-control asg-sig-acao" aria-label="O que faz">
        ${Object.entries(ACOES).map(([k, r]) => `<option value="${k}" ${k === (s.acao || 'sign') ? 'selected' : ''}>${r}</option>`).join('')}
      </select>
      <input type="hidden" class="asg-sig-cpf" value="${esc(s.cpf || '')}">
      <button type="button" class="asg-sig-remover" data-asg-modal="rem-sig" aria-label="Remover signatário" title="Remover">&times;</button>
    </div>`;
}

/** Mostra o campo do canal escolhido: e-mail ou celular. */
function atualizarCanal(linha) {
  if (!linha) return;
  const canal = linha.querySelector('.asg-canal').value;
  linha.querySelector('.asg-sig-email').hidden = canal !== 'email';
  linha.querySelector('.asg-sig-tel').hidden = canal === 'email';
}

function adicionarSignatario() {
  const lista = overlay.querySelector('#asgSigs');
  lista.insertAdjacentHTML('beforeend', sigHtml({ papel: 'Testemunha', acao: 'sign_as_witness' }));
  const nova = lista.lastElementChild;
  atualizarCanal(nova);
  nova.querySelector('.asg-sig-nome').focus();
}

function erro(msg) {
  const el = overlay.querySelector('#asgErro');
  if (!el) return notify(msg, 'error');
  el.textContent = msg;
  el.hidden = !msg;
  if (msg) el.scrollIntoView({ block: 'nearest' });
}

/** Pinta o campo de vermelho e, nas variáveis, escreve o motivo logo abaixo. */
function marcar(campo, msg) {
  campo.classList.add('asg-invalido');
  campo.setAttribute('aria-invalid', 'true');
  campo.title = msg;
  const linha = campo.closest('.asg-var');
  if (linha && !linha.querySelector('.asg-campo-erro')) {
    linha.insertAdjacentHTML('beforeend', `<div class="asg-campo-erro" role="alert">${esc(msg)}</div>`);
  }
}

function desmarcar(campo) {
  campo.classList.remove('asg-invalido');
  campo.removeAttribute('aria-invalid');
  campo.removeAttribute('title');
  campo.closest('.asg-var')?.querySelector('.asg-campo-erro')?.remove();
  if (!overlay.querySelector('.asg-invalido')) erro('');
}

function limparMarcas() {
  overlay.querySelectorAll('.asg-invalido').forEach(desmarcar);
}

function coletar() {
  limparMarcas();
  const variaveis = {};
  const fontes = {};
  overlay.querySelectorAll('.asg-valor').forEach(i => { variaveis[i.dataset.chave] = i.value.trim(); });
  overlay.querySelectorAll('.asg-fonte').forEach(s => { fontes[s.dataset.chave] = s.value || null; });

  // Tudo que falta de uma vez: marca cada campo e junta os nomes na caixa do rodapé.
  const faltando = [];
  const invalidos = [];
  preparado.variaveis.filter(v => v.obrigatoria && !variaveis[v.chave]).forEach(v => {
    const campo = overlay.querySelector(`.asg-valor[data-chave="${CSS.escape(v.chave)}"]`);
    if (campo) { marcar(campo, 'Obrigatório'); invalidos.push(campo); }
    faltando.push(v.rotulo);
  });
  overlay.querySelectorAll('.asg-sig').forEach((l, i) => {
    const nome = l.querySelector('.asg-sig-nome');
    const quem = nome.value.trim() || `${i + 1}º signatário`;
    if (!nome.value.trim()) { marcar(nome, 'Informe o nome'); invalidos.push(nome); faltando.push(`nome do ${i + 1}º signatário`); }
    if (l.querySelector('.asg-canal').value === 'email') {
      const email = l.querySelector('.asg-sig-email');
      if (!email.value.trim()) { marcar(email, 'Informe o e-mail'); invalidos.push(email); faltando.push(`e-mail de ${quem}`); }
    } else {
      const tel = l.querySelector('.asg-sig-tel');
      if (tel.value.replace(/\D/g, '').length < 10) { marcar(tel, 'Celular com DDD'); invalidos.push(tel); faltando.push(`celular com DDD de ${quem}`); }
    }
  });
  if (faltando.length) {
    invalidos[0]?.scrollIntoView({ block: 'center', behavior: 'smooth' });
    invalidos[0]?.focus({ preventScroll: true });
    throw new Error(`Preencha: ${faltando.join(', ')}.`);
  }

  const signatarios = [...overlay.querySelectorAll('.asg-sig')].map((l, i) => {
    const canal = l.querySelector('.asg-canal').value;
    return {
      nome: l.querySelector('.asg-sig-nome').value.trim(),
      canal,
      email: canal === 'email' ? l.querySelector('.asg-sig-email').value.trim() || null : null,
      telefone: canal !== 'email' ? l.querySelector('.asg-sig-tel').value.trim() || null : null,
      cpf: l.querySelector('.asg-sig-cpf').value || null,
      papel: l.querySelector('.asg-sig-papel').value.trim() || null,
      acao: l.querySelector('.asg-sig-acao').value,
      ordem: i + 1,
    };
  });
  if (!signatarios.length) throw new Error('Inclua pelo menos um signatário.');

  return {
    ...ctx.origem,
    templateId: preparado.templateId,
    titulo: overlay.querySelector('#asgTitulo2').value.trim() || null,
    variaveis,
    fontes,
    signatarios,
    emOrdem: overlay.querySelector('#asgOrdem').checked,
    prazo: overlay.querySelector('#asgPrazo').value || null,
    mensagem: overlay.querySelector('#asgMensagem').value.trim() || null,
  };
}

async function enviar(enviarAgora) {
  if (enviando || !preparado) return;
  let corpoReq;
  try {
    corpoReq = coletar();
  } catch (err) {
    erro(err.message);
    return;
  }
  enviando = true;
  const botoes = overlay.querySelectorAll('#asgRodape button');
  botoes.forEach(b => { b.disabled = true; });
  erro('');
  try {
    const s = await apiFetch(`${API}/solicitacoes`, { method: 'POST', body: { ...corpoReq, enviar: enviarAgora } });
    fechar();
    solicitacoes = [s, ...solicitacoes.filter(x => x.id !== s.id)];
    renderCartao();
    notify(s.erro
      ? 'Rascunho criado no Contratos, mas não foi enviado: veja o aviso no cartão.'
      : enviarAgora ? 'Contrato enviado para assinatura.' : 'Rascunho criado no Contratos.');
    ctx.aoMudar?.();
    ctx.container.scrollIntoView({ behavior: 'smooth', block: 'nearest' });
  } catch (err) {
    erro(err?.message || 'Não foi possível enviar.');
  } finally {
    enviando = false;
    botoes.forEach(b => { b.disabled = false; });
  }
}
