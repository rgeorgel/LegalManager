// Portal do cliente: contratos enviados pelo escritório para o cliente assinar (pelo Contratos).
// Mostra o que falta assinar (com o link, quando o provedor gera um) e baixa o PDF dos assinados.
// O cartão só aparece quando há algum envio.
import { portalApi, getToken } from '/cliente/js/clienteApi.js';

const esc = (v) => String(v ?? '').replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
const CANAL = { email: 'e-mail', whatsapp: 'WhatsApp', sms: 'SMS' };
const ABERTO = ['Enviada', 'ParcialmenteAssinada'];

function data(iso) {
  if (!iso) return '';
  const d = new Date(iso);
  return Number.isNaN(d.getTime()) ? '' : d.toLocaleDateString('pt-BR');
}

function situacao(a) {
  if (a.status === 'Concluida') return ['badge-green', 'Assinado'];
  if (a.status === 'Recusada') return ['badge-red', 'Recusado'];
  if (a.status === 'Expirada') return ['badge-gray', 'Prazo encerrado'];
  if (a.minhaSituacao === 'signed') return ['badge-blue', 'Você já assinou'];
  return ['badge-yellow', 'Aguardando sua assinatura'];
}

function itemHtml(a) {
  const [cls, rotulo] = situacao(a);
  const pendente = ABERTO.includes(a.status) && a.minhaSituacao !== 'signed';
  const detalhes = [
    a.enviadaEm ? `Enviado em ${data(a.enviadaEm)}` : '',
    pendente && a.expiraEm ? `assine até ${data(a.expiraEm)}` : '',
    a.concluidaEm ? `concluído em ${data(a.concluidaEm)}` : '',
    a.total > 1 ? `${a.assinados} de ${a.total} assinaturas` : '',
  ].filter(Boolean).join(' · ');
  let acao = '';
  if (pendente && a.linkAssinatura && /^https:\/\//i.test(a.linkAssinatura)) {
    acao = `<a class="btn btn-primary btn-sm" href="${esc(a.linkAssinatura)}" target="_blank" rel="noopener noreferrer">Assinar agora</a>`;
  } else if (pendente) {
    acao = `<span class="pa-dica">O link para assinar foi enviado para o seu ${esc(CANAL[a.canal] || 'e-mail')}.</span>`;
  } else if (a.temPdf) {
    acao = `<button type="button" class="btn btn-secondary btn-sm" data-pdf="${esc(a.id)}" data-nome="${esc(a.titulo)}">Baixar PDF assinado</button>`;
  }
  return `
    <div class="pa-item">
      <div class="pa-topo">
        <div class="pa-titulo">${esc(a.titulo)}</div>
        <span class="badge ${cls}">${rotulo}</span>
      </div>
      ${detalhes ? `<div class="pa-meta">${esc(detalhes)}</div>` : ''}
      ${acao ? `<div class="pa-acao">${acao}</div>` : ''}
    </div>`;
}

async function baixarPdf(botao) {
  botao.disabled = true;
  try {
    const res = await fetch(`/api/portal/minhas-assinaturas/${encodeURIComponent(botao.dataset.pdf)}/pdf`, {
      headers: { Authorization: `Bearer ${getToken()}` },
    });
    if (!res.ok) throw new Error();
    const url = URL.createObjectURL(await res.blob());
    const a = document.createElement('a');
    a.href = url;
    a.download = `${botao.dataset.nome || 'contrato'} (assinado).pdf`;
    document.body.appendChild(a);
    a.click();
    a.remove();
    setTimeout(() => URL.revokeObjectURL(url), 10000);
  } catch {
    alert('Não foi possível baixar o PDF agora. Tente de novo em instantes.');
  } finally {
    botao.disabled = false;
  }
}

/** Monta o cartão em `container` (fica oculto se não houver envios). */
export async function montarAssinaturasPortal(container) {
  let lista;
  try {
    lista = await portalApi.getMinhasAssinaturas();
  } catch {
    return;
  }
  if (!lista?.length) return;
  // Primeiro o que falta o cliente assinar.
  const peso = a => (ABERTO.includes(a.status) && a.minhaSituacao !== 'signed' ? 0 : 1);
  lista.sort((x, y) => peso(x) - peso(y));
  const pendentes = lista.filter(a => peso(a) === 0).length;
  container.innerHTML = `
    <div class="card pa-card">
      <div class="card-header">
        <span class="card-title">Contratos para assinar</span>
        ${pendentes ? `<span class="badge badge-yellow" style="margin-left:auto">${pendentes} pendente${pendentes > 1 ? 's' : ''}</span>` : ''}
      </div>
      <div>${lista.map(itemHtml).join('')}</div>
    </div>`;
  container.hidden = false;
  container.addEventListener('click', e => {
    const b = e.target.closest('[data-pdf]');
    if (b && !b.disabled) baixarPdf(b);
  });
}
