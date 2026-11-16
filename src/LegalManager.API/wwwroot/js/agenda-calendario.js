// "Sincronizar com meu calendário" (Agenda): gera o link .ics privado do usuário para assinar no
// Google Agenda, Apple (iPhone/Mac) ou Outlook. O link completo só existe na resposta do POST
// (o servidor guarda só o hash do token); depois disso dá para ajustar escopo/privacidade (PUT),
// gerar outro (derruba o anterior) ou desativar (DELETE). Abre também por ?sincronizar=1.
import { apiFetch, getUser } from './api.js';
import { esc } from './utils.js';

const API = '/agenda/feed';
let overlay = null;
let estado = null;      // FeedAgendaDto
let linkGerado = null;  // URL https completa — só nesta abertura, logo após gerar
let ocupado = false;

export const calendarioAberto = () => !!overlay && overlay.style.display !== 'none';

export async function abrirSincronizacao() {
  montar();
  overlay.style.display = 'flex';
  linkGerado = null;
  render('<p class="cal-muted">Carregando…</p>');
  try {
    estado = await apiFetch(API);
    render();
  } catch (err) {
    render(`<div class="alert alert-error">${esc(err?.message || 'Não foi possível carregar.')}</div>`);
  }
}

function fechar() {
  if (overlay) overlay.style.display = 'none';
  linkGerado = null;
}

function montar() {
  if (overlay) return;
  overlay = document.createElement('div');
  overlay.className = 'modal-overlay';
  overlay.id = 'modalCalendario';
  overlay.style.display = 'none';
  overlay.innerHTML = `
    <div class="modal cal-modal" role="dialog" aria-modal="true" aria-labelledby="calTitulo">
      <div class="modal-header">
        <h2 class="modal-title" id="calTitulo">Agenda no seu calendário</h2>
        <button type="button" class="modal-close" data-cal="fechar" aria-label="Fechar">&times;</button>
      </div>
      <div class="modal-body" id="calCorpo"></div>
    </div>`;
  document.body.appendChild(overlay);

  overlay.addEventListener('click', e => {
    if (e.target === overlay) return fechar();
    const acao = e.target.closest('[data-cal]')?.dataset.cal;
    if (acao === 'fechar') fechar();
    else if (acao === 'gerar') gerar();
    else if (acao === 'regerar') {
      if (confirm('Gerar um novo link? O link atual para de funcionar e será preciso assinar de novo no calendário.')) gerar();
    } else if (acao === 'desativar') desativar();
    else if (acao === 'copiar') copiar(e.target.closest('[data-cal]'));
  });
  overlay.addEventListener('change', e => {
    if (e.target.name === 'calEscopo') {
      // Escritório inteiro: nomes de clientes ocultos por padrão (o conteúdo vai para o Google/Microsoft).
      const ocultar = overlay.querySelector('#calOcultar');
      if (ocultar) ocultar.checked = e.target.value === 'Escritorio';
    }
    if (estado?.ativo && (e.target.name === 'calEscopo' || e.target.id === 'calOcultar')) salvarOpcoes();
  });
  // Os atalhos da agenda (N, D, S, setas…) não valem com o modal aberto.
  overlay.addEventListener('keydown', e => {
    e.stopPropagation();
    if (e.key === 'Escape') fechar();
  });
}

function opcoes() {
  const escopo = overlay.querySelector('input[name="calEscopo"]:checked')?.value || 'Meus';
  return { escopo, ocultarClientes: !!overlay.querySelector('#calOcultar')?.checked };
}

function render(conteudo) {
  const corpo = overlay.querySelector('#calCorpo');
  if (conteudo !== undefined) { corpo.innerHTML = conteudo; return; }

  const st = estado;
  const escritorio = st.escopo === 'Escritorio';
  const ocultar = st.ativo ? st.ocultarClientes : escritorio;
  const escopoHtml = st.podeEscritorio ? `
    <fieldset class="cal-grupo">
      <legend class="form-label">O que aparece</legend>
      <label class="cal-radio"><input type="radio" name="calEscopo" value="Meus" ${escritorio ? '' : 'checked'}>
        <span><strong>Meus itens</strong> — eventos, prazos e tarefas em que sou o responsável</span></label>
      <label class="cal-radio"><input type="radio" name="calEscopo" value="Escritorio" ${escritorio ? 'checked' : ''}>
        <span><strong>Escritório inteiro</strong> — de todos os responsáveis</span></label>
    </fieldset>` : `
    <p class="cal-muted">Entram os eventos, prazos e tarefas em aberto em que você é o responsável.</p>`;
  const privacidadeHtml = `
    <label class="cal-check"><input type="checkbox" id="calOcultar" ${ocultar ? 'checked' : ''}>
      <span>Ocultar nomes de clientes e descrições <small>(o calendário mostra só o tipo, o nº do processo e o link para o Causify)</small></span></label>`;

  let html = '';
  if (linkGerado) {
    const webcal = linkGerado.replace(/^https?:\/\//, 'webcal://');
    const nome = encodeURIComponent('Causify');
    html += `
      <div class="cal-sucesso">
        <p><strong>Link criado.</strong> Escolha onde assinar — ou copie o link. Guarde-o: por segurança ele só aparece agora.</p>
        <div class="cal-link">
          <input type="text" class="form-control" id="calLink" readonly value="${esc(linkGerado)}" aria-label="Link do calendário">
          <button type="button" class="btn btn-secondary" data-cal="copiar">Copiar</button>
        </div>
        <div class="cal-apps">
          <a class="btn btn-secondary" target="_blank" rel="noopener" href="https://calendar.google.com/calendar/r?cid=${encodeURIComponent(webcal)}">Google Agenda</a>
          <a class="btn btn-secondary" href="${esc(webcal)}">Apple (iPhone/Mac)</a>
          <a class="btn btn-secondary" target="_blank" rel="noopener" href="https://outlook.office.com/calendar/0/addfromweb?url=${encodeURIComponent(linkGerado)}&name=${nome}">Outlook (trabalho)</a>
          <a class="btn btn-secondary" target="_blank" rel="noopener" href="https://outlook.live.com/calendar/0/addfromweb?url=${encodeURIComponent(linkGerado)}&name=${nome}">Outlook.com</a>
        </div>
      </div>`;
  } else if (st.ativo) {
    html += `
      <div class="cal-status">
        <span class="cal-ponto" aria-hidden="true"></span>
        <div>
          <strong>Link ativo</strong> desde ${esc(fmt(st.criadoEm))}<br>
          <span class="cal-muted">${st.ultimoAcessoEm
            ? `Última busca pelo calendário: ${esc(fmt(st.ultimoAcessoEm))}`
            : 'O calendário ainda não buscou a agenda — o Google pode levar algumas horas.'}</span>
        </div>
      </div>
      <p class="cal-muted">O link completo só aparece quando é gerado. Perdeu ou quer trocar? Gere um novo — o atual para de funcionar.</p>`;
  } else {
    html += `
      <p>Veja seus prazos, audiências e tarefas no celular, no <strong>Google Agenda</strong>, no <strong>Outlook</strong>
        ou no calendário da <strong>Apple</strong>, com os lembretes do próprio aparelho.</p>`;
  }

  html += escopoHtml + privacidadeHtml;
  html += `
    <ul class="cal-notas">
      <li>Só leitura: o que você muda no calendário não volta para o Causify.</li>
      <li>O Google atualiza calendários assinados a cada 8 a 24 horas; Apple e Outlook, com mais frequência.</li>
      <li>Entram os últimos 30 dias e os próximos 12 meses. Tarefas concluídas saem na próxima atualização.</li>
      <li>Quem tiver o link vê a agenda — trate-o como uma senha.</li>
    </ul>
    <div class="cal-erro alert alert-error" hidden></div>
    <div class="cal-acoes">
      ${st.ativo
        ? `<button type="button" class="btn btn-secondary cal-perigo" data-cal="desativar">Desativar link</button>
           <button type="button" class="btn btn-primary" data-cal="regerar">Gerar novo link</button>`
        : `<button type="button" class="btn btn-secondary" data-cal="fechar">Cancelar</button>
           <button type="button" class="btn btn-primary" data-cal="gerar">Gerar meu link</button>`}
    </div>`;
  overlay.querySelector('#calCorpo').innerHTML = html;
  (overlay.querySelector('#calLink') || overlay.querySelector('.cal-acoes .btn-primary'))?.focus();
}

function fmt(iso) {
  if (!iso) return '—';
  const d = new Date(iso);
  return Number.isNaN(d.getTime()) ? '—' : d.toLocaleString('pt-BR', { dateStyle: 'short', timeStyle: 'short' });
}

function erro(msg) {
  const el = overlay.querySelector('.cal-erro');
  if (!el) return;
  el.textContent = msg;
  el.hidden = !msg;
}

async function gerar() {
  if (ocupado) return;
  ocupado = true;
  try {
    const r = await apiFetch(API, { method: 'POST', body: opcoes() });
    estado = r.feed;
    linkGerado = location.origin + r.caminho;
    render();
  } catch (err) {
    erro(err?.message || 'Não foi possível gerar o link.');
  } finally { ocupado = false; }
}

async function salvarOpcoes() {
  try {
    estado = await apiFetch(API, { method: 'PUT', body: opcoes() });
    erro('');
  } catch (err) {
    erro(err?.message || 'Não foi possível salvar.');
  }
}

async function desativar() {
  if (!confirm('Desativar o link? A agenda some do calendário em que foi assinada na próxima atualização.')) return;
  try {
    await apiFetch(API, { method: 'DELETE' });
    estado = { ...estado, ativo: false, criadoEm: null, ultimoAcessoEm: null };
    linkGerado = null;
    render();
  } catch (err) {
    erro(err?.message || 'Não foi possível desativar.');
  }
}

async function copiar(botao) {
  const input = overlay.querySelector('#calLink');
  if (!input) return;
  try {
    await navigator.clipboard.writeText(input.value);
  } catch {
    input.select();
    document.execCommand('copy');
  }
  const original = botao.textContent;
  botao.textContent = 'Copiado!';
  setTimeout(() => { botao.textContent = original; }, 1800);
}

/** Portal do cliente não tem agenda; o resto dos perfis tem. */
export function podeSincronizar() {
  const u = getUser();
  return !!u && u.perfil !== 'Cliente';
}
