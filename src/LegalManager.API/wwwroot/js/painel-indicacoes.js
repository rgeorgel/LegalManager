import { esc, brl } from '/js/utils.js';

// Painel de indicações: link/código, totais, escritórios indicados, comissões e solicitações de
// pagamento. Compartilhado pelo portal do parceiro (parceiro/painel.html) e pelo "Indique e ganhe"
// dos usuários (pages/indicacoes.html) — as duas APIs devolvem os mesmos DTOs (ParceiroService).
// CSS .ind-* em styles.css.
//
// api: { me, escritorios, comissoes, solicitacoes, solicitarPagamento(chavePix, observacao) }

const data = v => v ? new Date(v).toLocaleDateString('pt-BR') : '—';
export const pct = v => `${Number(v).toLocaleString('pt-BR', { maximumFractionDigits: 2 })}%`;

const SITUACAO = {
  'Assinante': ['badge-green', 'Assinante'],
  'Em teste': ['badge-blue', 'Em teste'],
  'Período grátis': ['badge-blue', 'Período grátis'],
  'Gratuito': ['badge-gray', 'Plano gratuito'],
  'Cancelado': ['badge-red', 'Cancelou'],
  'Encerrado': ['badge-red', 'Conta encerrada'],
  'Desvinculado': ['badge-gray', 'Desvinculado'],
  'Disponivel': ['badge-blue', 'Disponível'],
  'Solicitada': ['badge-gray', 'Em análise'],
  'Paga': ['badge-green', 'Paga'],
  'Pendente': ['badge-gray', 'Em análise'],
  'Recusada': ['badge-red', 'Recusada'],
};
const badge = s => { const [cls, txt] = SITUACAO[s] ?? ['badge-gray', s]; return `<span class="badge ${cls}">${esc(txt)}</span>`; };
const vazio = (cols, msg) => `<tr><td colspan="${cols}" class="ind-vazio">${msg}</td></tr>`;

export function linkIndicacao(codigo) {
  return `${location.origin}/register.html?parceiro=${encodeURIComponent(codigo.toUpperCase())}`;
}

/** Frase com a regra de comissão ("Você recebe 25% de cada pagamento..."), em HTML. */
export function regraComissao(percentual, meses) {
  return `Você recebe <strong>${pct(percentual)}</strong> de cada pagamento de assinatura dos escritórios que se cadastrarem
    com seu link ou código${meses ? `, durante os primeiros <strong>${meses} ${meses === 1 ? 'mês' : 'meses'}</strong> de cada escritório` : ', enquanto eles forem assinantes'}.`;
}

async function copiar(texto, btn, rotulo) {
  try {
    await navigator.clipboard.writeText(texto);
    btn.textContent = 'Copiado!';
    setTimeout(() => { btn.textContent = rotulo; }, 1500);
  } catch {
    prompt('Copie:', texto);
  }
}

const MARCACAO = `
  <div class="ind-card ind-link-card">
    <div class="ind-bloco">
      <div class="ind-rotulo">Seu código</div>
      <div class="ind-codigo" data-ind="codigo">—</div>
    </div>
    <div class="ind-bloco" style="flex:1">
      <div class="ind-rotulo">Link de indicação</div>
      <div class="ind-link" data-ind="link">—</div>
    </div>
    <div class="ind-acoes">
      <button type="button" class="btn btn-primary btn-sm" data-ind="copiarLink">Copiar link</button>
      <button type="button" class="btn btn-secondary btn-sm" data-ind="copiarCodigo">Copiar código</button>
      <a class="btn btn-secondary btn-sm" data-ind="whatsapp" target="_blank" rel="noopener">WhatsApp</a>
    </div>
    <div class="ind-regras" data-ind="regras"></div>
  </div>
  <div class="ind-aviso" data-ind="aviso" hidden></div>

  <div class="ind-grid">
    <div class="ind-card ind-stat">
      <div class="ind-rotulo">Escritórios indicados</div>
      <div class="ind-valor" data-ind="statEscritorios">—</div>
      <div class="ind-sub" data-ind="statAssinantes"></div>
    </div>
    <div class="ind-card ind-stat destaque">
      <div class="ind-rotulo">Disponível para saque</div>
      <div class="ind-valor" data-ind="statDisponivel">—</div>
      <div class="ind-sub"><button type="button" class="btn btn-primary btn-sm" data-ind="solicitar" style="margin-top:6px" disabled>Solicitar pagamento</button></div>
    </div>
    <div class="ind-card ind-stat">
      <div class="ind-rotulo">Em análise</div>
      <div class="ind-valor" data-ind="statSolicitado">—</div>
      <div class="ind-sub">solicitado, aguardando pagamento</div>
    </div>
    <div class="ind-card ind-stat">
      <div class="ind-rotulo">Já recebido</div>
      <div class="ind-valor" data-ind="statPago">—</div>
      <div class="ind-sub" data-ind="statTotal"></div>
    </div>
  </div>

  <div class="ind-card ind-tabela-card">
    <div class="tab-nav">
      <button type="button" class="tab-btn active" data-tab="escritorios">Escritórios</button>
      <button type="button" class="tab-btn" data-tab="comissoes">Pagamentos e comissões</button>
      <button type="button" class="tab-btn" data-tab="solicitacoes">Minhas solicitações</button>
    </div>
    <div class="tab-content active" data-aba="escritorios">
      <table class="ind-tabela"><thead><tr>
        <th>Escritório</th><th>Situação</th><th class="num">Mensalidade</th><th>Pagamentos</th><th class="num">Total pago</th><th class="num">Sua comissão</th>
      </tr></thead><tbody data-ind="escritorios">${vazio(6, 'Carregando...')}</tbody></table>
    </div>
    <div class="tab-content" data-aba="comissoes">
      <table class="ind-tabela"><thead><tr>
        <th>Data do pagamento</th><th>Escritório</th><th class="num">Valor pago</th><th class="num">%</th><th class="num">Comissão</th><th>Situação</th>
      </tr></thead><tbody data-ind="comissoes">${vazio(6, 'Carregando...')}</tbody></table>
    </div>
    <div class="tab-content" data-aba="solicitacoes">
      <table class="ind-tabela"><thead><tr>
        <th>Solicitada em</th><th class="num">Valor</th><th>Chave Pix</th><th>Status</th><th>Retorno do Causify</th>
      </tr></thead><tbody data-ind="solicitacoes">${vazio(5, 'Carregando...')}</tbody></table>
    </div>
  </div>`;

const MODAL = `
  <div class="modal" style="max-width:460px" role="dialog" aria-modal="true" aria-labelledby="indSolicitarTitulo">
    <div class="modal-header">
      <span class="modal-title" id="indSolicitarTitulo">Solicitar pagamento</span>
      <button type="button" class="modal-close" data-fechar aria-label="Fechar">✕</button>
    </div>
    <div class="modal-body">
      <p style="margin-top:0">Você vai solicitar <strong data-ind="solicitarValor"></strong> — todas as comissões disponíveis hoje.
        O pagamento é feito por Pix após a análise da equipe Causify.</p>
      <div class="form-group">
        <label for="indChavePix">Chave Pix</label>
        <input class="form-control" id="indChavePix" maxlength="200" placeholder="CPF, CNPJ, e-mail, telefone ou chave aleatória">
      </div>
      <div class="form-group">
        <label for="indObservacao">Observação <span class="ind-sub">(opcional)</span></label>
        <textarea class="form-control" id="indObservacao" rows="3" maxlength="1000" placeholder="Ex.: dados para a nota fiscal"></textarea>
      </div>
      <div class="form-error" data-ind="solicitarErro" hidden></div>
    </div>
    <div class="modal-footer">
      <button type="button" class="btn btn-secondary" data-fechar>Cancelar</button>
      <button type="button" class="btn btn-primary" data-ind="solicitarConfirmar">Solicitar</button>
    </div>
  </div>`;

/**
 * Monta o painel dentro de `raiz` e carrega os dados.
 * @param {HTMLElement} raiz
 * @param {{ api: object, textoVazio?: string, inativo?: string|null, mensagemWhatsapp?: (link: string) => string }} opcoes
 *   inativo: aviso quando o cadastro foi desativado (link sem efeito; solicitar continua se houver saldo).
 */
export function montarPainelIndicacoes(raiz, opcoes) {
  const { api } = opcoes;
  const textoVazio = opcoes.textoVazio ?? 'Nenhum escritório se cadastrou com seu código ainda. Compartilhe seu link de indicação!';
  const mensagemWhatsapp = opcoes.mensagemWhatsapp
    ?? (link => `Conheça o Causify, o sistema de gestão para escritórios de advocacia: ${link}`);

  raiz.innerHTML = MARCACAO;
  const overlay = document.createElement('div');
  overlay.className = 'modal-overlay';
  overlay.innerHTML = MODAL;
  document.body.appendChild(overlay);

  const el = nome => raiz.querySelector(`[data-ind="${nome}"]`) ?? overlay.querySelector(`[data-ind="${nome}"]`);
  let resumo = null;

  raiz.querySelectorAll('.tab-btn').forEach(b => b.addEventListener('click', () => {
    raiz.querySelectorAll('.tab-btn').forEach(x => x.classList.toggle('active', x === b));
    raiz.querySelectorAll('.tab-content').forEach(x => x.classList.toggle('active', x.dataset.aba === b.dataset.tab));
  }));

  if (opcoes.inativo) {
    el('aviso').textContent = opcoes.inativo;
    el('aviso').hidden = false;
  }

  function renderResumo() {
    const r = resumo;
    const link = linkIndicacao(r.codigo);
    el('codigo').textContent = r.codigo;
    el('link').textContent = link;
    el('whatsapp').href = `https://wa.me/?text=${encodeURIComponent(mensagemWhatsapp(link))}`;
    el('regras').innerHTML = `${regraComissao(r.percentualComissao, r.mesesComissao)}
      As comissões ficam disponíveis assim que o pagamento é confirmado; para receber, clique em <strong>Solicitar pagamento</strong>.`;
    el('statEscritorios').textContent = r.escritorios;
    el('statAssinantes').textContent = `${r.assinantes} ${r.assinantes === 1 ? 'assinante' : 'assinantes'}`;
    el('statDisponivel').textContent = brl(r.disponivel);
    el('statSolicitado').textContent = brl(r.solicitado);
    el('statPago').textContent = brl(r.pago);
    el('statTotal').textContent = `${brl(r.totalComissoes)} em comissões no total`;

    const btn = el('solicitar');
    btn.disabled = r.solicitacaoPendente || !(r.disponivel > 0);
    btn.title = r.solicitacaoPendente ? 'Você já tem uma solicitação em análise.'
      : !(r.disponivel > 0) ? 'Ainda não há comissões disponíveis.' : '';
  }

  async function carregar() {
    try {
      const [r, escritorios, comissoes, solicitacoes] = await Promise.all([
        api.me(), api.escritorios(), api.comissoes(), api.solicitacoes(),
      ]);
      resumo = r;
      renderResumo();

      el('escritorios').innerHTML = escritorios.length === 0
        ? vazio(6, textoVazio)
        : escritorios.map(e => `
          <tr>
            <td><strong>${esc(e.nome)}</strong><div class="ind-sub">cadastro em ${data(e.vinculadoEm ?? e.cadastradoEm)}</div></td>
            <td>${badge(e.situacao)}${e.situacao === 'Assinante' ? `<div class="ind-sub">${esc(e.plano)}${e.periodo ? ` ${esc(e.periodo.toLowerCase())}` : ''}</div>` : ''}</td>
            <td class="num">${e.valorMensal != null ? brl(e.valorMensal) : '—'}</td>
            <td class="ind-sub">${e.primeiroPagamento ? `1º em ${data(e.primeiroPagamento)}<br>último em ${data(e.ultimoPagamento)}` : 'Nenhum ainda'}
              ${e.comissaoAte ? `<br>comissão até ${data(e.comissaoAte)}` : ''}</td>
            <td class="num">${brl(e.totalPago)}</td>
            <td class="num"><strong>${brl(e.totalComissao)}</strong></td>
          </tr>`).join('');

      el('comissoes').innerHTML = comissoes.length === 0
        ? vazio(6, 'Nenhum pagamento dos seus escritórios ainda.')
        : comissoes.map(c => `
          <tr>
            <td>${data(c.pagoEm)}<div class="ind-sub">${esc(c.descricao ?? '')}</div></td>
            <td>${esc(c.nomeEscritorio)}</td>
            <td class="num">${brl(c.valorPagamento)}</td>
            <td class="num">${pct(c.percentual)}</td>
            <td class="num"><strong>${brl(c.valor)}</strong></td>
            <td>${badge(c.situacao)}</td>
          </tr>`).join('');

      el('solicitacoes').innerHTML = solicitacoes.length === 0
        ? vazio(5, 'Você ainda não solicitou nenhum pagamento.')
        : solicitacoes.map(s => `
          <tr>
            <td>${data(s.solicitadaEm)}</td>
            <td class="num"><strong>${brl(s.valor)}</strong></td>
            <td>${esc(s.chavePix)}</td>
            <td>${badge(s.status)}${s.resolvidaEm ? `<div class="ind-sub">em ${data(s.resolvidaEm)}</div>` : ''}</td>
            <td class="ind-sub">${esc(s.resposta ?? '—')}</td>
          </tr>`).join('');
    } catch (e) {
      el('escritorios').innerHTML = vazio(6, `Erro ao carregar: ${esc(e.message)}`);
    }
  }

  el('copiarLink').addEventListener('click', e => resumo && copiar(linkIndicacao(resumo.codigo), e.currentTarget, 'Copiar link'));
  el('copiarCodigo').addEventListener('click', e => resumo && copiar(resumo.codigo.toUpperCase(), e.currentTarget, 'Copiar código'));

  // ── Solicitar pagamento ───────────────────────────────────────────────────

  const chavePix = overlay.querySelector('#indChavePix');
  const observacao = overlay.querySelector('#indObservacao');
  const fecharModal = () => overlay.classList.remove('open');
  overlay.querySelectorAll('[data-fechar]').forEach(b => b.addEventListener('click', fecharModal));
  overlay.addEventListener('keydown', e => { if (e.key === 'Escape') fecharModal(); });

  el('solicitar').addEventListener('click', () => {
    if (!resumo) return;
    el('solicitarValor').textContent = brl(resumo.disponivel);
    chavePix.value = resumo.chavePix ?? '';
    observacao.value = '';
    el('solicitarErro').hidden = true;
    overlay.classList.add('open');
    chavePix.focus();
  });

  el('solicitarConfirmar').addEventListener('click', async () => {
    const chave = chavePix.value.trim();
    const erro = msg => { el('solicitarErro').textContent = msg; el('solicitarErro').hidden = false; };
    if (!chave) return erro('Informe a chave Pix para receber.');

    const btn = el('solicitarConfirmar');
    btn.disabled = true;
    try {
      await api.solicitarPagamento(chave, observacao.value.trim() || null);
      fecharModal();
      await carregar();
      raiz.querySelector('.tab-btn[data-tab="solicitacoes"]').click();
    } catch (e) {
      erro(e.message);
    } finally {
      btn.disabled = false;
    }
  });

  carregar();
  return { recarregar: carregar };
}
