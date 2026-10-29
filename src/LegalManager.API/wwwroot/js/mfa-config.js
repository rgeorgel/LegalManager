// Seção "Verificação em duas etapas" (MFA por app autenticador). Usada em
// /pages/configuracoes.html (apiFetch) e /superadmin/seguranca.html (saFetch):
// `request(path, options)` é o fetch autenticado de cada portal (base /api).
import { esc } from '/js/utils.js';

export function montarMfa(container, request) {
  const post = (path, body) => request(path, {
    method: 'POST',
    body: body ? JSON.stringify(body) : undefined,
  });

  function msg(texto, tipo = 'error') {
    return `<div class="alert alert-${tipo}" style="display:block;margin:12px 0">${esc(texto)}</div>`;
  }

  async function renderStatus(aviso = '') {
    container.innerHTML = '<p style="color:var(--color-text-muted)">Carregando...</p>';
    let status;
    try {
      status = await request('/auth/mfa/status');
    } catch (err) {
      container.innerHTML = msg(err.message);
      return;
    }

    if (!status.ativo) {
      container.innerHTML = `
        ${aviso}
        <p style="margin:0 0 12px;max-width:560px">Além da senha, o login passa a pedir um código de 6 dígitos gerado no seu celular
          (Google Authenticator, Microsoft Authenticator, Authy, 1Password...). Mesmo que alguém descubra sua senha, não consegue entrar.</p>
        <button type="button" class="btn btn-primary" data-mfa="configurar">Ativar verificação em duas etapas</button>`;
      return;
    }

    const poucos = status.codigosRecuperacaoRestantes <= 3;
    container.innerHTML = `
      ${aviso}
      <p style="margin:0 0 8px"><span class="badge badge-green">Ativa</span> O login pede o código do app autenticador.</p>
      <p style="margin:0 0 12px;color:${poucos ? 'var(--color-danger)' : 'var(--color-text-muted)'};font-size:13px">
        Códigos de recuperação restantes: <strong>${status.codigosRecuperacaoRestantes}</strong>${poucos ? ' — gere novos códigos.' : ''}</p>
      <div style="display:flex;gap:8px;flex-wrap:wrap">
        <button type="button" class="btn btn-secondary" data-mfa="pedir-senha" data-acao="codigos">Gerar novos códigos de recuperação</button>
        <button type="button" class="btn btn-danger" data-mfa="pedir-senha" data-acao="desativar">Desativar</button>
      </div>
      <div data-mfa-senha></div>`;
  }

  async function configurar() {
    container.innerHTML = '<p style="color:var(--color-text-muted)">Gerando chave...</p>';
    let cfg;
    try {
      cfg = await post('/auth/mfa/configurar');
    } catch (err) {
      await renderStatus(msg(err.message));
      return;
    }
    container.innerHTML = `
      <ol style="margin:0 0 12px;padding-left:20px;max-width:560px;line-height:1.6">
        <li>Abra o app autenticador no celular e escaneie o QR code.</li>
        <li>Sem câmera? Digite a chave: <code style="user-select:all">${esc(cfg.chave)}</code></li>
        <li>Digite abaixo o código de 6 dígitos que aparece no app.</li>
      </ol>
      <img src="${cfg.qrCodeDataUri}" alt="QR code para o app autenticador" width="200" height="200"
           style="display:block;margin:0 0 12px;background:#fff;padding:8px;border-radius:8px">
      <form data-mfa-form="ativar" style="max-width:320px">
        <div class="form-group">
          <label class="form-label" for="mfaCodigoAtivar">Código do app *</label>
          <input type="text" id="mfaCodigoAtivar" class="form-control" required inputmode="numeric"
                 autocomplete="one-time-code" maxlength="7" placeholder="123456">
        </div>
        <div data-mfa-erro></div>
        <div style="display:flex;gap:8px">
          <button type="submit" class="btn btn-primary">Confirmar e ativar</button>
          <button type="button" class="btn btn-secondary" data-mfa="cancelar">Cancelar</button>
        </div>
      </form>`;
    container.querySelector('#mfaCodigoAtivar').focus();
  }

  function mostrarCodigos(codigos, titulo) {
    container.innerHTML = `
      ${msg(titulo, 'success')}
      <p style="margin:0 0 8px;max-width:560px"><strong>Guarde estes códigos de recuperação</strong> num lugar seguro (gerenciador de senhas ou papel).
        Cada um vale uma vez e permite entrar se você perder o celular. Eles não serão mostrados de novo.</p>
      <pre style="display:grid;grid-template-columns:repeat(2,max-content);gap:4px 24px;padding:12px 16px;background:var(--color-bg, #f5f5f5);border-radius:8px;font-size:15px;margin:0 0 12px">${codigos.map(esc).join('\n')}</pre>
      <div style="display:flex;gap:8px">
        <button type="button" class="btn btn-secondary" data-mfa="copiar">Copiar códigos</button>
        <button type="button" class="btn btn-primary" data-mfa="concluir">Já guardei</button>
      </div>`;
    container.querySelector('[data-mfa="copiar"]').addEventListener('click', async e => {
      try {
        await navigator.clipboard.writeText(codigos.join('\n'));
        e.target.textContent = 'Copiado!';
      } catch {
        e.target.textContent = 'Não foi possível copiar';
      }
    });
  }

  function pedirSenha(acao) {
    const alvo = container.querySelector('[data-mfa-senha]');
    alvo.innerHTML = `
      <form data-mfa-form="${acao}" style="max-width:320px;margin-top:12px">
        <div class="form-group">
          <label class="form-label" for="mfaSenha">Confirme sua senha *</label>
          <input type="password" id="mfaSenha" class="form-control" required autocomplete="current-password">
        </div>
        <div data-mfa-erro></div>
        <button type="submit" class="btn ${acao === 'desativar' ? 'btn-danger' : 'btn-primary'}">
          ${acao === 'desativar' ? 'Desativar verificação em duas etapas' : 'Gerar novos códigos'}</button>
      </form>`;
    alvo.querySelector('#mfaSenha').focus();
  }

  container.addEventListener('click', e => {
    const btn = e.target.closest('[data-mfa]');
    if (!btn) return;
    const acao = btn.dataset.mfa;
    if (acao === 'configurar') configurar();
    else if (acao === 'cancelar' || acao === 'concluir') renderStatus();
    else if (acao === 'pedir-senha') pedirSenha(btn.dataset.acao);
  });

  container.addEventListener('submit', async e => {
    const form = e.target.closest('[data-mfa-form]');
    if (!form) return;
    e.preventDefault();
    const erro = form.querySelector('[data-mfa-erro]');
    const botao = form.querySelector('button[type="submit"]');
    erro.innerHTML = '';
    botao.disabled = true;
    try {
      const tipo = form.dataset.mfaForm;
      if (tipo === 'ativar') {
        const r = await post('/auth/mfa/ativar', { codigo: form.querySelector('#mfaCodigoAtivar').value.trim() });
        mostrarCodigos(r.codigos, 'Verificação em duas etapas ativada.');
      } else if (tipo === 'codigos') {
        const r = await post('/auth/mfa/codigos-recuperacao', { senha: form.querySelector('#mfaSenha').value });
        mostrarCodigos(r.codigos, 'Novos códigos gerados. Os anteriores deixaram de valer.');
      } else if (tipo === 'desativar') {
        await post('/auth/mfa/desativar', { senha: form.querySelector('#mfaSenha').value });
        await renderStatus(msg('Verificação em duas etapas desativada.', 'success'));
      }
    } catch (err) {
      erro.innerHTML = msg(err.message);
      botao.disabled = false;
    }
  });

  renderStatus();
}
