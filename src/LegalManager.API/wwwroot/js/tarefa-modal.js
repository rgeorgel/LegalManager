// Modal de tarefa (nova e editar), com painel de contexto na edição: prazo, processo,
// movimentação, pessoas e histórico. Usado pela tela de Tarefas e pelo widget "Minhas Tarefas"
// (presente em todas as páginas) — o HTML e o CSS são inseridos na primeira abertura.
//
// Depois de criar, editar, concluir ou excluir, dispara `tarefa:alterada` em window
// (detail: { id, acao }) para quem mostra tarefas recarregar.

import { esc, dataParede, inputDataHoraParede, isoLocal } from './utils.js';
import { apiFetch, getUser } from './api.js';
import { createProcessoPicker } from './processo-picker.js';

export const PRIORIDADE_LABEL = { Baixa: 'Baixa', Media: 'Média', Alta: 'Alta', Urgente: 'Urgente' };
export const STATUS_LABEL = { Pendente: 'Pendente', EmAndamento: 'Em Andamento', Concluida: 'Concluída', Cancelada: 'Cancelada' };

// Tarefa recorrente: cada ocorrência é uma tarefa própria (status/kanban independentes).
export const badgeRecorrente = t => t.recorrenciaId
  ? '<span class="badge badge-recorrente" title="Tarefa recorrente — cada ocorrência é uma tarefa separada">🔁 Recorrente</span>'
  : '';

function showErr(el, msg) { el.textContent = msg; el.style.display = ''; }

function avisarAlteracao(id, acao) {
  window.dispatchEvent(new CustomEvent('tarefa:alterada', { detail: { id, acao } }));
}

const HTML = `<div class="modal-overlay" id="modalTarefa" style="display:none">
  <div class="modal ctx-modal" id="modalTarefaBox">
    <div class="modal-header">
      <div class="ctx-header">
        <h2 class="modal-title" id="modalTarefaTitulo">Nova Tarefa</h2>
        <div class="ctx-badges" id="modalTarefaBadges"></div>
      </div>
      <button class="modal-close" id="modalTarefaClose">&times;</button>
    </div>
    <form id="formTarefa" novalidate>
      <div class="modal-body ctx-body">
       <div class="ctx-form">
        <div class="form-group">
          <label class="form-label" for="fTitulo">Título *</label>
          <input type="text" id="fTitulo" class="form-control" maxlength="300" required>
        </div>
        <div class="form-group">
          <label class="form-label" for="fDescricao">Descrição</label>
          <textarea id="fDescricao" class="form-control" rows="3" maxlength="4000"
            placeholder="Detalhes, próximos passos, o que já foi feito…"></textarea>
        </div>
        <div style="display:grid;grid-template-columns:1fr 1fr;gap:12px">
          <div class="form-group">
            <label class="form-label">Prioridade *</label>
            <select id="fPrioridade" class="form-control" required>
              <option value="Baixa">Baixa</option>
              <option value="Media" selected>Média</option>
              <option value="Alta">Alta</option>
              <option value="Urgente">Urgente</option>
            </select>
          </div>
          <div class="form-group" id="fStatusGroup" style="display:none">
            <label class="form-label">Status *</label>
            <select id="fStatus" class="form-control">
              <option value="Pendente">Pendente</option>
              <option value="EmAndamento">Em Andamento</option>
              <option value="Concluida">Concluída</option>
              <option value="Cancelada">Cancelada</option>
            </select>
          </div>
        </div>
        <div class="form-group">
          <label class="form-label" for="fPrazo">Prazo</label>
          <input type="datetime-local" id="fPrazo" class="form-control">
          <div class="tm-atalhos" id="fPrazoAtalhos" aria-label="Atalhos de prazo">
            <button type="button" data-prazo="hoje">Hoje</button>
            <button type="button" data-prazo="amanha">Amanhã</button>
            <button type="button" data-prazo="+1">+1 dia</button>
            <button type="button" data-prazo="+7">+1 semana</button>
            <button type="button" data-prazo="limpar">Sem prazo</button>
          </div>
          <div class="tm-hint" id="fPrazoCalcHint" style="display:none"></div>
        </div>
        <div class="form-group" id="fRepGroup">
          <label class="form-label" for="fRepetir">Repetir</label>
          <select id="fRepetir" class="form-control">
            <option value="">Não repete</option>
            <option value="Diaria">Diariamente</option>
            <option value="Semanal">Semanalmente</option>
            <option value="Mensal">Mensalmente</option>
          </select>
          <fieldset id="fRepDetalhes" style="border:0;padding:0;margin:0;display:none">
            <div class="rep-linha">
              <span>A cada</span>
              <input type="number" id="fRepIntervalo" class="form-control" min="1" max="99" value="1" style="width:72px">
              <span id="fRepUnidade">semana(s)</span>
            </div>
            <div class="rep-linha" id="fRepSemanal">
              <span>Em</span>
              <div class="rep-dias" id="fRepDias">
                <label title="Domingo"><input type="checkbox" value="Sunday">Dom</label>
                <label title="Segunda-feira"><input type="checkbox" value="Monday">Seg</label>
                <label title="Terça-feira"><input type="checkbox" value="Tuesday">Ter</label>
                <label title="Quarta-feira"><input type="checkbox" value="Wednesday">Qua</label>
                <label title="Quinta-feira"><input type="checkbox" value="Thursday">Qui</label>
                <label title="Sexta-feira"><input type="checkbox" value="Friday">Sex</label>
                <label title="Sábado"><input type="checkbox" value="Saturday">Sáb</label>
              </div>
            </div>
            <div class="rep-linha" id="fRepMensal">
              <span>No dia</span>
              <input type="number" id="fRepDiaMes" class="form-control" min="1" max="31" style="width:72px">
              <span>de cada mês</span>
            </div>
            <div class="rep-linha">
              <span>Termina</span>
              <select id="fRepTermina" class="form-control">
                <option value="">Nunca</option>
                <option value="data">Em uma data</option>
              </select>
              <input type="date" id="fRepTerminaEm" class="form-control" style="display:none">
            </div>
          </fieldset>
          <div class="rep-hint" id="fRepHint"></div>
          <label class="rep-aplicar" id="fRepAplicarWrap" style="display:none">
            <input type="checkbox" id="fRepAplicar" checked style="margin-top:3px">
            <span>Aplicar também às próximas ocorrências (título, descrição, prioridade, processo, tags e repetição)</span>
          </label>
        </div>
        <div class="form-group">
          <label class="form-label">Processo vinculado</label>
          <div style="display:flex;gap:8px;align-items:center">
            <div id="fProcessoPicker" style="flex:1;min-width:0"></div>
            <button type="button" id="fProcessoLink"
               class="btn btn-secondary btn-sm"
               style="display:none;white-space:nowrap">⚖️ Abrir processo</button>
          </div>
        </div>
        <div class="form-group">
          <label class="form-label">Tags (separadas por vírgula)</label>
          <input type="text" id="fTags" class="form-control" placeholder="ex: urgente, revisão, cliente-x">
        </div>
        <div id="fmsgErro" class="alert alert-error" style="display:none"></div>
       </div>
        <!-- Contexto da tarefa (só na edição): prazo, processo, partes, movimentação, histórico -->
        <aside class="ctx-painel" id="tmPainel" style="display:none"></aside>
      </div>
      <div class="modal-footer ctx-footer">
        <button type="button" class="btn btn-danger btn-sm ctx-esquerda" id="btnExcluirTarefa" style="display:none">Excluir</button>
        <button type="button" class="btn btn-secondary ctx-cancelar" id="btnCancelarTarefa">Cancelar</button>
        <button type="button" class="btn btn-secondary" id="btnConcluirTarefa" style="display:none">✓ Salvar e concluir</button>
        <button type="submit" class="btn btn-primary" id="btnSalvarTarefa">Salvar</button>
      </div>
    </form>
  </div>
</div>
`;

const CSS = `
/* Base em .ctx-modal (styles.css). Escopo #modalTarefa: o modal também abre fora da tela de
   tarefas (widget "Minhas Tarefas"), e estas classes não podem vazar para outras páginas. */
#modalTarefa .ctx-modal:not(.ctx-com-painel) { max-width:560px; }
#modalTarefa .ctx-com-painel #fTitulo { font-size:20px; font-weight:700; padding:6px 8px; margin-left:-8px; width:calc(100% + 8px); border-color:transparent; background:transparent; }
#modalTarefa .ctx-com-painel #fTitulo:hover { border-color:var(--color-border); }
#modalTarefa .ctx-com-painel #fTitulo:focus { border-color:var(--color-primary); background:var(--color-surface); }
#modalTarefa .ctx-com-painel label[for="fTitulo"] { position:absolute; width:1px; height:1px; overflow:hidden; clip:rect(0 0 0 0); }
#modalTarefa .ctx-com-painel #fDescricao { min-height:110px; }
#modalTarefa .badge.prioridade-baixa   { background:#f0fdf4; color:#16a34a; }
#modalTarefa .badge.prioridade-media   { background:#fefce8; color:#ca8a04; }
#modalTarefa .badge.prioridade-alta    { background:#fff7ed; color:#ea580c; }
#modalTarefa .badge.prioridade-urgente { background:#fef2f2; color:#dc2626; }
#modalTarefa .badge.status-pendente    { background:#f3f4f6; color:#374151; }
#modalTarefa .badge.status-emandamento { background:#dbeafe; color:#1d4ed8; }
#modalTarefa .badge.status-concluida   { background:#d1fae5; color:#057a55; }
#modalTarefa .badge.status-cancelada   { background:#fee2e2; color:#e02424; }
#modalTarefa .badge-recorrente { background:var(--color-primary-light); color:var(--color-primary); }
#modalTarefa .processo-link { color:var(--color-primary); text-decoration:none; font-weight:500; }
#modalTarefa .processo-link:hover { text-decoration:underline; }
#modalTarefa .tm-atalhos { display:flex; gap:6px; flex-wrap:wrap; margin-top:6px; }
#modalTarefa .tm-atalhos button { border:1px solid var(--color-border); background:var(--color-surface); color:var(--color-text-muted); border-radius:999px; font-size:12px; padding:3px 10px; cursor:pointer; }
#modalTarefa .tm-atalhos button:hover { border-color:var(--color-primary); color:var(--color-primary); }
#modalTarefa .tm-hint { font-size:12px; color:var(--color-text-muted); margin-top:6px; }
#modalTarefa .rep-linha { display:flex; gap:8px; flex-wrap:wrap; align-items:center; margin-top:8px; font-size:14px; }
#modalTarefa .rep-linha .form-control { width:auto; }
#modalTarefa .rep-dias { display:flex; gap:4px; flex-wrap:wrap; }
#modalTarefa .rep-dias label { display:inline-flex; align-items:center; justify-content:center; min-width:40px; padding:4px 8px; border:1px solid var(--color-border); border-radius:999px; font-size:12px; cursor:pointer; user-select:none; }
#modalTarefa .rep-dias input { position:absolute; opacity:0; pointer-events:none; }
#modalTarefa .rep-dias label:has(input:checked) { background:var(--color-primary); border-color:var(--color-primary); color:#fff; }
#modalTarefa .rep-dias label:has(input:focus-visible) { outline:2px solid var(--color-primary); outline-offset:2px; }
#modalTarefa .rep-hint { font-size:12px; color:var(--color-text-muted); margin-top:6px; }
#modalTarefa .rep-hint:empty { display:none; }
#modalTarefa .rep-aplicar { display:flex; gap:6px; align-items:flex-start; margin-top:8px; font-size:13px; cursor:pointer; }
#modalTarefa #fRepDetalhes:disabled { opacity:.55; }
`;

let iniciado = false;

function garantirModal() {
  if (iniciado) return;
  iniciado = true;
  const estilo = document.createElement('style');
  estilo.id = 'tarefa-modal-css';
  estilo.textContent = CSS;
  document.head.appendChild(estilo);
  document.body.insertAdjacentHTML('beforeend', HTML);
  iniciar();
}

/** Abre o modal vazio para criar uma tarefa. */
export function abrirNovaTarefa() {
  garantirModal();
  openCreateModal();
}

/** Abre a tarefa no modal de edição, com o painel de contexto. */
export async function abrirTarefa(id) {
  garantirModal();
  await openEditModal(id);
}

/**
 * Exclui a tarefa com as confirmações (inclusive "parar a repetição" numa ocorrência de série).
 * `t`: a tarefa, se já carregada. Retorna se excluiu.
 */
export async function excluirTarefa(id, t = null) {
  const recorrente = t?.recorrenciaId || t?.recorrencia;
  if (!confirm(recorrente ? 'Excluir esta ocorrência da tarefa recorrente?' : 'Excluir esta tarefa?')) return false;
  // Sem encerrar, a série continua criando as próximas ocorrências normalmente.
  const encerrar = recorrente
    && confirm('Parar também a repetição?\n\nOK: não criar mais ocorrências.\nCancelar: excluir só esta e manter as próximas.');
  await apiFetch(`/tarefas/${id}${encerrar ? '?encerrarRecorrencia=true' : ''}`, { method: 'DELETE' });
  avisarAlteracao(id, 'excluida');
  return true;
}

// ── Processos helpers ──────────────────────────────────────────
let processoPicker = null;   // criado em iniciar(), depois que o HTML do modal existe

function setProcesso(id, numeroCNJ) {
  processoPicker.setValue(id ? { id, label: numeroCNJ || 'Processo vinculado' } : null);
  updateProcessoLink();
}

function updateProcessoLink() {
  const btn = document.getElementById('fProcessoLink');
  btn.style.display = processoPicker.getValue() ? '' : 'none';
}

// ── Modal create/edit ──────────────────────────────────────────
// Tarefa aberta na edição, como veio da API: o PUT repete os campos que o formulário não
// mostra (tipo, contato, contagem do prazo) para não apagá-los.
let editingTarefa = null;
let editingId = null;
let editingResponsavelId = null;

function modoEdicao(ativo) {
  document.getElementById('modalTarefaBox').classList.toggle('ctx-com-painel', ativo);
  document.getElementById('tmPainel').style.display = ativo ? '' : 'none';
  document.getElementById('btnExcluirTarefa').style.display = ativo ? '' : 'none';
  document.getElementById('btnSalvarTarefa').textContent = ativo ? 'Salvar alterações' : 'Salvar';
  if (!ativo) {
    document.getElementById('modalTarefaBadges').innerHTML = '';
    document.getElementById('btnConcluirTarefa').style.display = 'none';
    document.getElementById('fPrazoCalcHint').style.display = 'none';
  }
}

function openCreateModal() {
  editingId = null;
  editingTarefa = null;
  editingRecorrente = false;
  modoEdicao(false);
  document.getElementById('modalTarefaTitulo').textContent = 'Nova Tarefa';
  document.getElementById('formTarefa').reset();
  document.getElementById('fStatusGroup').style.display = 'none';
  document.getElementById('fmsgErro').style.display = 'none';
  setProcesso(null);
  preencherRepeticao(null);
  document.getElementById('modalTarefa').style.display = 'flex';
  document.getElementById('modalTarefaBox').scrollTop = 0;
}

async function openEditModal(id) {
  const t = await apiFetch(`/tarefas/${id}`);
  editingId = id;
  editingTarefa = t;
  editingResponsavelId = t.responsavelId ?? null;
  modoEdicao(true);
  document.getElementById('modalTarefaTitulo').textContent = t.tipo === 'Prazo' ? 'Prazo processual' : 'Tarefa';
  document.getElementById('fmsgErro').style.display = 'none';
  document.getElementById('fStatusGroup').style.display = '';
  document.getElementById('fTitulo').value = t.titulo;
  document.getElementById('fDescricao').value = t.descricao ?? '';
  document.getElementById('fPrioridade').value = t.prioridade;
  document.getElementById('fStatus').value = t.status;
  document.getElementById('fPrazo').value = inputDataHoraParede(t.prazo);
  document.getElementById('fTags').value = (t.tags ?? []).join(', ');
  setProcesso(t.processoId, t.numeroCNJProcesso);
  // Série já encerrada: a tarefa se comporta como avulsa (escolher uma repetição reativa a série).
  editingRecorrente = !!t.recorrencia?.ativa;
  preencherRepeticao(editingRecorrente ? t.recorrencia : null);
  renderBadgesModal();
  document.getElementById('btnConcluirTarefa').style.display =
    t.status === 'Concluida' || t.status === 'Cancelada' ? 'none' : '';
  renderPainel(t);
  atualizarPrazoUI();
  document.getElementById('modalTarefa').style.display = 'flex';
  document.getElementById('modalTarefaBox').scrollTop = 0;
  if (t.processoId) carregarContextoProcesso(t);
}

// Badges do cabeçalho acompanham o formulário (prioridade/status), não só o que está salvo.
function renderBadgesModal() {
  if (!editingTarefa) return;
  const prio = document.getElementById('fPrioridade').value;
  const status = document.getElementById('fStatus').value;
  document.getElementById('modalTarefaBadges').innerHTML = `
    <span class="badge prioridade-${prio.toLowerCase()}">${PRIORIDADE_LABEL[prio] ?? prio}</span>
    <span class="badge status-${status.toLowerCase()}">${STATUS_LABEL[status] ?? status}</span>
    ${editingTarefa.recorrenciaId || editingTarefa.recorrencia ? badgeRecorrente({ recorrenciaId: true }) : ''}`;
}
// ── Painel de contexto (edição) ────────────────────────────────
const AREA_LABEL = {
  Civil: 'Cível', Trabalhista: 'Trabalhista', Criminal: 'Criminal', Tributario: 'Tributário',
  Previdenciario: 'Previdenciário', Administrativo: 'Administrativo', Consumidor: 'Consumidor',
  Familia: 'Família', Empresarial: 'Empresarial', Ambiental: 'Ambiental', Imobiliario: 'Imobiliário', Outro: 'Outro'
};
const FASE_LABEL = {
  Conhecimento: 'Conhecimento', Recursal: 'Recursal', Execucao: 'Execução', Cumprimento: 'Cumprimento',
  InqueritoPolicial: 'Inquérito Policial', InvestigacaoDefensiva: 'Invest. Defensiva', Outro: 'Outro'
};
const PARTE_LABEL = { Autor: 'Autor', Reu: 'Réu', Interessado: 'Interes.', Terceiro: 'Terceiro' };
const fmtData = d => d.toLocaleDateString('pt-BR');
// Carimbos do servidor (CriadoEm, ConcluidaEm) são UTC reais — aqui sim vale new Date().
const fmtCarimbo = v => v ? fmtData(new Date(v)) : '–';
const plural = (n, um, varios) => `${n} ${n === 1 ? um : varios}`;

function cardPrazo() {
  const t = editingTarefa;
  const prazo = dataParede(document.getElementById('fPrazo').value);
  const status = document.getElementById('fStatus').value;
  let classe = 'ctx-neutro', rel = 'Sem prazo definido', data = 'Use os atalhos ao lado para definir uma data.';
  if (status === 'Concluida') {
    classe = 'ctx-ok';
    rel = 'Concluída';
    data = t.concluidaEm ? `em ${fmtCarimbo(t.concluidaEm)}` : '';
  } else if (status === 'Cancelada') {
    rel = 'Cancelada';
    data = '';
  } else if (prazo) {
    // Prazo é hora "de parede" do escritório; o navegador do advogado está, na prática, no mesmo fuso.
    const hoje = new Date(); hoje.setHours(0, 0, 0, 0);
    const dia = new Date(prazo); dia.setHours(0, 0, 0, 0);
    const dias = Math.round((dia - hoje) / 86400000);
    if (prazo < new Date() && dias <= 0) {
      classe = 'ctx-atrasado';
      rel = dias === 0 ? 'Venceu hoje' : `Atrasada há ${plural(-dias, 'dia', 'dias')}`;
    } else if (dias === 0) { classe = 'ctx-hoje'; rel = 'Vence hoje'; }
    else if (dias === 1) { classe = 'ctx-proximo'; rel = 'Vence amanhã'; }
    else { classe = dias <= 5 ? 'ctx-proximo' : 'ctx-ok'; rel = `Faltam ${plural(dias, 'dia', 'dias')}`; }
  }
  if (prazo && status !== 'Concluida' && status !== 'Cancelada') {
    const hora = prazo.getHours() || prazo.getMinutes()
      ? ` às ${prazo.toLocaleTimeString('pt-BR', { hour: '2-digit', minute: '2-digit' })}` : '';
    data = prazo.toLocaleDateString('pt-BR', { weekday: 'long', day: '2-digit', month: '2-digit', year: 'numeric' }) + hora;
  }
  const contagemValida = t.dataInicio && t.quantidadeDias
    && document.getElementById('fPrazo').value === inputDataHoraParede(t.prazo);
  const calc = contagemValida
    ? `<div class="ctx-destaque-extra">Contagem: ${plural(t.quantidadeDias, t.tipoCalculo === 'DiasCorridos' ? 'dia corrido' : 'dia útil', t.tipoCalculo === 'DiasCorridos' ? 'dias corridos' : 'dias úteis')} a partir de ${fmtData(dataParede(t.dataInicio))}</div>`
    : '';
  return `<div class="ctx-card ctx-destaque ${classe}" id="tmCardPrazo">
    <div class="ctx-card-titulo">${t.tipo === 'Prazo' ? 'Prazo processual' : 'Prazo'}</div>
    <div class="ctx-destaque-rel">${rel}</div>
    ${data ? `<div class="ctx-destaque-data">${esc(data)}</div>` : ''}
    ${calc}
  </div>`;
}

function renderPainel(t) {
  const contato = t.contatoId && t.nomeContato
    ? `<a href="/pages/contato-detalhe.html?id=${t.contatoId}" class="processo-link">${esc(t.nomeContato)}</a>` : null;
  document.getElementById('tmPainel').innerHTML = `
    ${cardPrazo()}
    ${t.processoId ? `<div class="ctx-card" id="tmCardProcesso">
      <div class="ctx-card-titulo">Processo <a href="/pages/processo-detalhe.html?id=${t.processoId}" class="processo-link">Abrir →</a></div>
      <div class="ctx-cnj">${esc(t.numeroCNJProcesso ?? '')}</div>
      <div class="ctx-carregando" style="margin-top:6px">Carregando dados do processo…</div>
    </div>
    <div class="ctx-card" id="tmCardMov">
      <div class="ctx-card-titulo">${t.andamentoId ? 'Movimentação de origem' : 'Última movimentação'}</div>
      <div class="ctx-carregando">Carregando…</div>
    </div>` : ''}
    <div class="ctx-card">
      <div class="ctx-card-titulo">Pessoas</div>
      <div class="ctx-linha"><span>Responsável</span><span>${t.nomeResponsavel ? esc(t.nomeResponsavel) : '–'}</span></div>
      ${contato ? `<div class="ctx-linha"><span>Contato</span><span>${contato}</span></div>` : ''}
    </div>
    <div class="ctx-card">
      <div class="ctx-card-titulo">Histórico</div>
      <div class="ctx-linha"><span>Criada</span><span>${fmtCarimbo(t.criadoEm)} · ${esc(t.nomeCriadoPor ?? '')}</span></div>
      ${t.concluidaEm ? `<div class="ctx-linha"><span>Concluída</span><span>${fmtCarimbo(t.concluidaEm)}</span></div>` : ''}
      ${t.recorrencia ? `<div class="ctx-linha"><span>Repetição</span><span>🔁 ${esc(t.recorrencia.descricao)}${t.recorrencia.ativa ? '' : ' (encerrada)'}</span></div>` : ''}
    </div>`;
}

// Dados do processo e movimentação chegam depois: o modal já abre com o que a tarefa tem.
async function carregarContextoProcesso(t) {
  const [proc, andamentos] = await Promise.allSettled([
    apiFetch(`/processos/${t.processoId}`),
    apiFetch(`/processos/${t.processoId}/andamentos`),
  ]);
  if (editingTarefa?.id !== t.id) return;   // usuário já abriu outra tarefa

  const cardProc = document.getElementById('tmCardProcesso');
  if (cardProc) {
    const p = proc.status === 'fulfilled' ? proc.value : null;
    const corpo = cardProc.querySelector('.ctx-carregando');
    if (!p) corpo.outerHTML = '<div class="ctx-vazio" style="margin-top:6px">Não foi possível carregar o processo.</div>';
    else {
      const orgao = [p.vara, p.comarca].filter(Boolean).join(' · ');
      const partes = (p.partes ?? []).slice(0, 4).map(pt =>
        `<div class="ctx-parte"><span class="ctx-parte-tipo">${PARTE_LABEL[pt.tipoParte] ?? pt.tipoParte}</span><span>${esc(pt.nomeContato)}</span></div>`).join('');
      corpo.outerHTML = `
        ${p.classe || p.tipoAcao ? `<div style="margin-top:4px">${esc(p.classe || p.tipoAcao)}</div>` : ''}
        <div style="margin-top:8px">
          ${p.siglaTribunal || p.tribunal ? `<div class="ctx-linha"><span>Tribunal</span><span>${esc(p.siglaTribunal || p.tribunal)}${p.grau ? ` · ${esc(p.grau)}` : ''}</span></div>` : ''}
          ${orgao ? `<div class="ctx-linha"><span>Órgão</span><span>${esc(orgao)}</span></div>` : ''}
          <div class="ctx-linha"><span>Área / fase</span><span>${AREA_LABEL[p.areaDireito] ?? p.areaDireito} · ${FASE_LABEL[p.fase] ?? p.fase}</span></div>
          ${p.nomeAdvogadoResponsavel ? `<div class="ctx-linha"><span>Advogado</span><span>${esc(p.nomeAdvogadoResponsavel)}</span></div>` : ''}
        </div>
        ${partes ? `<div class="ctx-partes">${partes}</div>` : ''}`;
    }
  }

  const cardMov = document.getElementById('tmCardMov');
  if (cardMov) {
    const lista = andamentos.status === 'fulfilled' ? andamentos.value ?? [] : [];
    const mov = (t.andamentoId && lista.find(a => a.id === t.andamentoId))
      || [...lista].sort((a, b) => String(b.data).localeCompare(String(a.data)))[0];
    const corpo = cardMov.querySelector('.ctx-carregando');
    if (!mov) corpo.outerHTML = '<div class="ctx-vazio">Nenhuma movimentação registrada.</div>';
    else {
      const texto = mov.descricaoTraduzidaIA || mov.descricao;
      corpo.outerHTML = `
        <div class="ctx-mov-data">${fmtData(dataParede(mov.data))}${mov.orgaoJulgador ? ` <span class="ctx-vazio">· ${esc(mov.orgaoJulgador)}</span>` : ''}</div>
        <div class="ctx-mov-texto" title="${esc(texto)}">${esc(texto)}</div>`;
    }
  }
}

// ── Prazo: atalhos e reflexo no painel ─────────────────────────
function atualizarPrazoUI() {
  if (!editingTarefa) return;
  const card = document.getElementById('tmCardPrazo');
  if (card) card.outerHTML = cardPrazo();
  // Prazo calculado (data de início + N dias): mexer na data à mão desfaz a contagem.
  const t = editingTarefa;
  const hint = document.getElementById('fPrazoCalcHint');
  const mudou = document.getElementById('fPrazo').value !== inputDataHoraParede(t.prazo);
  hint.style.display = t.dataInicio && t.quantidadeDias && mudou ? '' : 'none';
  hint.textContent = 'Ao salvar, o prazo passa a ser esta data (a contagem automática deixa de valer).';
}

// ── Repetição ──────────────────────────────────────────────────
// Na edição de uma ocorrência, "Aplicar às próximas" desmarcado = muda só esta tarefa
// (a regra fica como está). Tarefa avulsa: escolher uma repetição cria a série a partir dela.
let editingRecorrente = false;
const DIAS_SEMANA = ['Sunday', 'Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday'];
const UNIDADE_REP = { Diaria: 'dia(s)', Semanal: 'semana(s)', Mensal: 'mês(es)' };

function prazoSelecionado() {
  const v = document.getElementById('fPrazo').value;
  return v ? new Date(v) : null;
}

function preencherRepeticao(r) {
  document.getElementById('fRepetir').value = r?.frequencia ?? '';
  document.getElementById('fRepIntervalo').value = r?.intervalo ?? 1;
  const dias = r?.diasSemana ?? [];
  document.querySelectorAll('#fRepDias input').forEach(cb => { cb.checked = dias.includes(cb.value); });
  document.getElementById('fRepDiaMes').value = r?.diaDoMes ?? '';
  document.getElementById('fRepTermina').value = r?.terminaEm ? 'data' : '';
  document.getElementById('fRepTerminaEm').value = r?.terminaEm ? r.terminaEm.substring(0, 10) : '';
  document.getElementById('fRepAplicar').checked = true;
  document.getElementById('fRepAplicarWrap').style.display = editingId && editingRecorrente ? '' : 'none';
  atualizarRepeticaoUI();
}

// Sem dia escolhido, usa o do prazo (semanal: dia da semana; mensal: dia do mês).
function completarComPrazo() {
  const prazo = prazoSelecionado();
  if (!prazo) return;
  const freq = document.getElementById('fRepetir').value;
  const dias = [...document.querySelectorAll('#fRepDias input')];
  if (freq === 'Semanal' && !dias.some(cb => cb.checked)) dias[prazo.getDay()].checked = true;
  const diaMes = document.getElementById('fRepDiaMes');
  if (freq === 'Mensal' && !diaMes.value) diaMes.value = prazo.getDate();
}

function atualizarRepeticaoUI() {
  const freq = document.getElementById('fRepetir').value;
  const detalhes = document.getElementById('fRepDetalhes');
  detalhes.style.display = freq ? '' : 'none';
  document.getElementById('fRepSemanal').style.display = freq === 'Semanal' ? '' : 'none';
  document.getElementById('fRepMensal').style.display = freq === 'Mensal' ? '' : 'none';
  document.getElementById('fRepUnidade').textContent = UNIDADE_REP[freq] ?? '';
  document.getElementById('fRepTerminaEm').style.display =
    document.getElementById('fRepTermina').value === 'data' ? '' : 'none';
  const aplicar = !editingRecorrente || !editingId || document.getElementById('fRepAplicar').checked;
  document.getElementById('fRepetir').disabled = !aplicar;
  detalhes.disabled = !aplicar;
  completarComPrazo();

  const hint = document.getElementById('fRepHint');
  if (editingId && editingRecorrente && !aplicar) hint.textContent = 'As alterações valem só para esta ocorrência.';
  else if (editingId && editingRecorrente && !freq) hint.textContent = 'Ao salvar, a repetição é encerrada: esta e as ocorrências já criadas continuam, mas não serão criadas novas.';
  else if (freq) hint.textContent = 'Cada ocorrência é uma tarefa separada no kanban — concluir uma não afeta as outras. A próxima é criada quando o prazo da atual vence, e as futuras aparecem na agenda.';
  else hint.textContent = '';
}

function lerRepeticao() {
  const freq = document.getElementById('fRepetir').value;
  if (!freq) return null;
  const intervalo = parseInt(document.getElementById('fRepIntervalo').value, 10);
  const r = { frequencia: freq, intervalo: intervalo >= 1 ? intervalo : 1 };
  if (freq === 'Semanal') r.diasSemana = [...document.querySelectorAll('#fRepDias input:checked')].map(cb => cb.value);
  if (freq === 'Mensal') {
    const dia = parseInt(document.getElementById('fRepDiaMes').value, 10);
    if (dia >= 1 && dia <= 31) r.diaDoMes = dia;
  }
  if (document.getElementById('fRepTermina').value === 'data' && document.getElementById('fRepTerminaEm').value)
    r.terminaEm = document.getElementById('fRepTerminaEm').value;
  return r;
}

function closeModal() {
  document.getElementById('modalTarefa').style.display = 'none';
}


function iniciar() {
  processoPicker = createProcessoPicker(document.getElementById('fProcessoPicker'), {
    onChange: updateProcessoLink,
  });
  ['fPrioridade', 'fStatus'].forEach(id =>
    document.getElementById(id).addEventListener('change', renderBadgesModal));

  document.getElementById('fPrazo').addEventListener('input', atualizarPrazoUI);
  document.getElementById('fStatus').addEventListener('change', atualizarPrazoUI);
  document.getElementById('fPrazoAtalhos').addEventListener('click', e => {
    const acao = e.target.closest('[data-prazo]')?.dataset.prazo;
    if (!acao) return;
    const input = document.getElementById('fPrazo');
    if (acao === 'limpar') input.value = '';
    else {
      const atual = dataParede(input.value);
      let d;
      if (acao === 'hoje' || acao === 'amanha') {
        d = new Date();
        if (acao === 'amanha') d.setDate(d.getDate() + 1);
        // Mantém o horário já escolhido; sem prazo ainda, fim do expediente.
        d.setHours(atual ? atual.getHours() : 18, atual ? atual.getMinutes() : 0, 0, 0);
      } else {
        d = atual ? new Date(atual) : (() => { const x = new Date(); x.setHours(18, 0, 0, 0); return x; })();
        d.setDate(d.getDate() + Number(acao));
      }
      input.value = isoLocal(d).substring(0, 16);
    }
    input.dispatchEvent(new Event('change'));
    atualizarPrazoUI();
  });

  document.getElementById('btnConcluirTarefa').addEventListener('click', () => {
    document.getElementById('fStatus').value = 'Concluida';
    document.getElementById('formTarefa').requestSubmit();
  });
  document.getElementById('btnExcluirTarefa').addEventListener('click', async () => {
    if (!editingTarefa) return;
    if (await excluirTarefa(editingTarefa.id, editingTarefa)) closeModal();
  });

  ['fRepetir', 'fRepTermina', 'fRepAplicar'].forEach(id =>
    document.getElementById(id).addEventListener('change', atualizarRepeticaoUI));
  document.getElementById('fPrazo').addEventListener('change', () => {
    if (document.getElementById('fRepetir').value) completarComPrazo();
  });

  document.getElementById('fProcessoLink').addEventListener('click', () => {
    const id = processoPicker.getValue();
    if (id) window.location.href = `/pages/processo-detalhe.html?id=${id}`;
  });
  document.getElementById('modalTarefaClose').addEventListener('click', closeModal);
  document.getElementById('btnCancelarTarefa').addEventListener('click', closeModal);
  document.getElementById('modalTarefa').addEventListener('click', e => {
    if (e.target === e.currentTarget) closeModal();
  });

  document.getElementById('formTarefa').addEventListener('submit', async e => {
    e.preventDefault();
    const errEl = document.getElementById('fmsgErro');
    errEl.style.display = 'none';
    const titulo = document.getElementById('fTitulo').value.trim();
    if (!titulo) { showErr(errEl, 'Título é obrigatório.'); return; }
    const tagsRaw = document.getElementById('fTags').value.trim();
    const tags = tagsRaw ? tagsRaw.split(',').map(s => s.trim()).filter(Boolean) : [];
    const prazoVal = document.getElementById('fPrazo').value;
    const processoVal = processoPicker.getValue() || null;
    const recorrencia = lerRepeticao();
    const aplicarRepeticao = !editingRecorrente || document.getElementById('fRepAplicar').checked;
    if (recorrencia && aplicarRepeticao) {
      if (!prazoVal) { showErr(errEl, 'Informe o prazo para repetir a tarefa.'); return; }
      if (recorrencia.frequencia === 'Semanal' && !recorrencia.diasSemana.length) { showErr(errEl, 'Escolha ao menos um dia da semana.'); return; }
    }

    try {
      if (editingId) {
        const t = editingTarefa;
        // Prazo com contagem (início + N dias): o backend recalcula a data a partir dela, então
        // só a mantém se o prazo não foi mexido — senão vale a data escolhida.
        const manterContagem = t?.dataInicio && t?.quantidadeDias && prazoVal === inputDataHoraParede(t.prazo);
        await apiFetch(`/tarefas/${editingId}`, { method: 'PUT', body: JSON.stringify({
          tipo: t?.tipo ?? 'Tarefa',
          contatoId: t?.contatoId ?? null,
          dataInicio: manterContagem ? t.dataInicio : null,
          quantidadeDias: manterContagem ? t.quantidadeDias : null,
          tipoCalculo: manterContagem ? t.tipoCalculo : null,
          titulo,
          descricao: document.getElementById('fDescricao').value.trim() || null,
          prioridade: document.getElementById('fPrioridade').value,
          status: document.getElementById('fStatus').value,
          prazo: prazoVal || null,
          processoId: processoVal,
          // Editar não transfere a tarefa para quem editou.
          responsavelId: editingResponsavelId || getUser()?.id || null,
          tags,
          recorrencia,
          // Ocorrência de série: só mexe na série com "Aplicar às próximas". Tarefa avulsa: só
          // quando o usuário escolheu uma repetição (senão nada muda).
          atualizarRecorrencia: editingRecorrente ? aplicarRepeticao : !!recorrencia
        })});
      } else {
        await apiFetch('/tarefas', { method: 'POST', body: JSON.stringify({
          titulo,
          descricao: document.getElementById('fDescricao').value.trim() || null,
          prioridade: document.getElementById('fPrioridade').value,
          prazo: prazoVal || null,
          processoId: processoVal,
          responsavelId: getUser()?.id || null,
          tags,
          recorrencia
        })});
      }
      closeModal();
      avisarAlteracao(editingId, editingId ? 'editada' : 'criada');
    } catch (err) {
      showErr(errEl, err.message ?? 'Erro ao salvar tarefa.');
    }
  });


}
