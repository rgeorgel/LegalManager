// Modal "Portal do Cliente": libera, redefine ou revoga o acesso de um contato ao portal e
// gera o link para compartilhar (do portal ou de um processo específico). Usado no cadastro
// de contatos, no perfil do contato e nos detalhes do processo — o HTML é injetado uma vez.
import { getPortalAcesso, criarPortalAcesso, revogarPortalAcesso } from '/js/contatos.js';
import { esc } from '/js/utils.js';

const MODAL_HTML = `
  <div class="modal-overlay" id="modalPortalAcesso">
    <div class="modal" style="max-width:460px">
      <div class="modal-header">
        <span class="modal-title">Portal do Cliente</span>
        <button class="modal-close" data-portal-fechar>✕</button>
      </div>
      <div class="modal-body">
        <p id="paContatoNome" style="font-weight:600;margin-bottom:16px"></p>
        <div id="paAlert" class="alert" style="display:none"></div>

        <div id="paLoading" style="text-align:center;padding:24px;color:var(--color-text-muted)">Carregando...</div>

        <div id="paSemAcesso" style="display:none">
          <p style="color:var(--color-text-muted);margin-bottom:16px;font-size:13px">
            Este contato não possui acesso ao portal. Defina as credenciais abaixo — o cliente receberá um e-mail automático com os dados de login.
          </p>
          <div class="form-group">
            <label for="paEmail">E-mail de acesso</label>
            <input type="email" id="paEmail" class="form-control" placeholder="email@exemplo.com">
          </div>
          <div class="form-group">
            <label for="paSenha">Senha inicial</label>
            <div style="display:flex;gap:8px">
              <input type="text" id="paSenha" class="form-control" placeholder="Mínimo 8 caracteres" autocomplete="new-password">
              <button type="button" class="btn btn-secondary btn-sm" id="paGerarSenha" title="Gerar senha aleatória">🎲 Gerar</button>
            </div>
          </div>
        </div>

        <div id="paComAcesso" style="display:none">
          <div style="background:var(--color-bg);border-radius:var(--radius);padding:16px;margin-bottom:16px">
            <div style="display:grid;grid-template-columns:1fr 1fr;gap:12px;font-size:13px">
              <div><div class="pa-rotulo">E-mail</div><span id="paAcessoEmail" style="word-break:break-all"></span></div>
              <div><div class="pa-rotulo">Situação</div><span id="paAcessoAtivo"></span></div>
              <div><div class="pa-rotulo">Criado em</div><span id="paAcessoCriado"></span></div>
              <div><div class="pa-rotulo">Último acesso</div><span id="paAcessoUltimo"></span></div>
            </div>
          </div>
          <div id="paCompartilhar" style="margin-bottom:16px">
            <div class="pa-rotulo" style="margin-bottom:6px" id="paLinkRotulo">Link do portal</div>
            <div style="display:flex;gap:8px">
              <input type="text" id="paLink" class="form-control" readonly style="font-size:12px">
              <button type="button" class="btn btn-secondary btn-sm" id="paCopiarLink">📋 Copiar</button>
            </div>
            <div style="display:flex;gap:8px;margin-top:8px;flex-wrap:wrap">
              <a class="btn btn-secondary btn-sm" id="paWhatsapp" target="_blank" rel="noopener">💬 Enviar por WhatsApp</a>
              <a class="btn btn-secondary btn-sm" id="paEmailLink">✉️ Enviar por e-mail</a>
            </div>
          </div>
          <p style="color:var(--color-text-muted);font-size:13px;margin-bottom:12px">
            Para redefinir a senha, informe uma nova abaixo — um novo e-mail será enviado ao cliente.
          </p>
          <div class="form-group">
            <label for="paNovaSenha">Nova senha</label>
            <div style="display:flex;gap:8px">
              <input type="text" id="paNovaSenha" class="form-control" placeholder="Mínimo 8 caracteres (opcional)" autocomplete="new-password">
              <button type="button" class="btn btn-secondary btn-sm" id="paGerarNovaSenha" title="Gerar senha aleatória">🎲 Gerar</button>
            </div>
          </div>
        </div>
      </div>
      <div class="modal-footer" id="paFooter" style="display:none">
        <button class="btn btn-secondary" data-portal-fechar>Fechar</button>
        <button class="btn btn-danger btn-sm" id="paRevogar" style="display:none">Revogar acesso</button>
        <button class="btn btn-primary" id="paSalvar">Liberar acesso</button>
      </div>
    </div>
  </div>
  <style>
    .pa-rotulo { color:var(--color-text-muted); font-size:11px; text-transform:uppercase; font-weight:600; margin-bottom:2px; }
  </style>`;

let modal = null;
// Estado do modal aberto.
let atual = null;

function el(id) { return document.getElementById(id); }

function mostrarAlerta(msg, type = 'error') {
  const a = el('paAlert');
  a.className = `alert alert-${type}`;
  a.textContent = msg;
  a.style.display = 'block';
}

function formatarData(dt) {
  if (!dt) return '—';
  const d = new Date(dt);
  return !isNaN(d) ? d.toLocaleDateString('pt-BR', { day: '2-digit', month: '2-digit', year: 'numeric', hour: '2-digit', minute: '2-digit' }) : '—';
}

function gerarSenha() {
  // Sem caracteres ambíguos (0/O, 1/l/I) — o cliente pode digitar a partir do e-mail.
  const letras = 'abcdefghjkmnpqrstuvwxyzABCDEFGHJKLMNPQRSTUVWXYZ23456789';
  const bytes = crypto.getRandomValues(new Uint32Array(10));
  return Array.from(bytes, b => letras[b % letras.length]).join('');
}

/** Link para o cliente: o processo (quando informado) ou a entrada do portal. */
export function linkPortal(processoId = null) {
  return processoId
    ? `${location.origin}/cliente/processo-detalhe.html?id=${encodeURIComponent(processoId)}`
    : `${location.origin}/cliente/`;
}

function fechar() {
  modal.classList.remove('open');
  atual = null;
}

function montar() {
  if (modal) return;
  document.body.insertAdjacentHTML('beforeend', MODAL_HTML);
  modal = el('modalPortalAcesso');

  modal.querySelectorAll('[data-portal-fechar]').forEach(b => b.addEventListener('click', fechar));
  modal.addEventListener('click', e => { if (e.target === modal) fechar(); });
  el('paGerarSenha').addEventListener('click', () => { el('paSenha').value = gerarSenha(); });
  el('paGerarNovaSenha').addEventListener('click', () => { el('paNovaSenha').value = gerarSenha(); });

  el('paCopiarLink').addEventListener('click', async () => {
    const btn = el('paCopiarLink');
    try {
      await navigator.clipboard.writeText(el('paLink').value);
    } catch {
      el('paLink').select();
      document.execCommand('copy');
    }
    btn.textContent = '✓ Copiado';
    setTimeout(() => { btn.textContent = '📋 Copiar'; }, 1500);
  });

  el('paSalvar').addEventListener('click', salvar);
  el('paRevogar').addEventListener('click', revogar);
}

function preencherCompartilhar(acesso) {
  const link = linkPortal(atual.processoId);
  el('paLinkRotulo').textContent = atual.processoId ? 'Link do processo no portal' : 'Link do portal';
  el('paLink').value = link;
  const texto = atual.processoId && atual.numeroProcesso
    ? `Olá, ${atual.nome}! Você pode acompanhar o processo ${atual.numeroProcesso} pelo nosso Portal do Cliente: ${link}\nEntre com o e-mail ${acesso.email}.`
    : `Olá, ${atual.nome}! Acompanhe seus processos pelo nosso Portal do Cliente: ${link}\nEntre com o e-mail ${acesso.email}.`;
  const telefone = (atual.telefone || '').replace(/\D/g, '');
  // Número brasileiro sem DDI ganha o 55; sem telefone, o WhatsApp pede o contato.
  const fone = telefone && telefone.length <= 11 ? `55${telefone}` : telefone;
  el('paWhatsapp').href = `https://wa.me/${fone}?text=${encodeURIComponent(texto)}`;
  el('paEmailLink').href = `mailto:${encodeURIComponent(acesso.email)}?subject=${encodeURIComponent('Portal do Cliente')}&body=${encodeURIComponent(texto)}`;
}

function renderAcesso(acesso) {
  el('paLoading').style.display = 'none';
  el('paFooter').style.display = 'flex';
  atual.temAcesso = !!acesso;
  if (acesso) {
    atual.email = acesso.email;
    el('paAcessoEmail').textContent = acesso.email;
    el('paAcessoAtivo').innerHTML = acesso.ativo
      ? '<span class="badge badge-green">Ativo</span>'
      : '<span class="badge badge-gray">Inativo</span>';
    el('paAcessoCriado').textContent = formatarData(acesso.criadoEm);
    el('paAcessoUltimo').textContent = acesso.ultimoAcessoEm ? formatarData(acesso.ultimoAcessoEm) : 'Nunca acessou';
    preencherCompartilhar(acesso);
    el('paSemAcesso').style.display = 'none';
    el('paComAcesso').style.display = 'block';
    el('paRevogar').style.display = 'inline-flex';
    el('paSalvar').textContent = 'Redefinir senha';
  } else {
    el('paComAcesso').style.display = 'none';
    el('paSemAcesso').style.display = 'block';
    el('paRevogar').style.display = 'none';
    el('paSalvar').textContent = 'Liberar acesso';
  }
}

async function salvar() {
  const btn = el('paSalvar');
  const email = atual.temAcesso ? atual.email : el('paEmail').value.trim();
  const senha = atual.temAcesso ? el('paNovaSenha').value : el('paSenha').value;

  if (!atual.temAcesso && (!email || !senha)) { mostrarAlerta('Preencha o e-mail e a senha.'); return; }
  if (atual.temAcesso && !senha) { mostrarAlerta('Informe a nova senha para redefinir.'); return; }
  if (senha.length < 8) { mostrarAlerta('A senha deve ter ao menos 8 caracteres.'); return; }

  const eraNovo = !atual.temAcesso;
  btn.disabled = true;
  btn.innerHTML = '<span class="spinner"></span>';
  el('paAlert').style.display = 'none';
  try {
    const acesso = await criarPortalAcesso(atual.contatoId, { email, senha });
    el('paNovaSenha').value = '';
    renderAcesso(acesso);
    mostrarAlerta(eraNovo
      ? 'Acesso liberado. O cliente recebeu um e-mail com os dados de login — use o link abaixo para compartilhar também.'
      : 'Senha redefinida. Um novo e-mail foi enviado ao cliente.', 'success');
    atual.onAlterado?.(acesso);
  } catch (err) {
    mostrarAlerta(err.message);
  } finally {
    btn.disabled = false;
    btn.textContent = atual?.temAcesso ? 'Redefinir senha' : 'Liberar acesso';
  }
}

async function revogar() {
  if (!confirm('Revogar o acesso ao portal deste cliente?')) return;
  try {
    await revogarPortalAcesso(atual.contatoId);
    const onAlterado = atual.onAlterado;
    fechar();
    onAlterado?.(null);
  } catch (err) {
    mostrarAlerta(err.message);
  }
}

/**
 * Abre o modal para o contato.
 * @param {{contatoId: string, nome: string, email?: string, telefone?: string,
 *          processoId?: string, numeroProcesso?: string, onAlterado?: (acesso|null) => void}} opcoes
 */
export async function abrirPortalAcesso(opcoes) {
  montar();
  atual = { ...opcoes, temAcesso: false };
  el('paContatoNome').innerHTML = opcoes.numeroProcesso
    ? `${esc(opcoes.nome)} <span style="font-weight:400;color:var(--color-text-muted);font-size:13px">· processo ${esc(opcoes.numeroProcesso)}</span>`
    : esc(opcoes.nome);
  el('paAlert').style.display = 'none';
  el('paLoading').style.display = 'block';
  el('paSemAcesso').style.display = 'none';
  el('paComAcesso').style.display = 'none';
  el('paFooter').style.display = 'none';
  el('paEmail').value = opcoes.email || '';
  el('paSenha').value = '';
  el('paNovaSenha').value = '';
  modal.classList.add('open');

  try {
    renderAcesso(await getPortalAcesso(opcoes.contatoId));
  } catch (err) {
    el('paLoading').style.display = 'none';
    mostrarAlerta(err.message);
  }
}

document.addEventListener('keydown', e => {
  if (e.key === 'Escape' && modal?.classList.contains('open')) fechar();
});
