// Aviso no topo do dashboard (novo e clássico) convidando a ativar a verificação em duas
// etapas. O MFA é opcional: o aviso só aparece enquanto ele estiver desligado e pode ser
// dispensado — volta depois de DIAS_DISPENSADO. Não aparece na impersonação (o SuperAdmin
// não pode configurar o MFA de outro usuário) nem em conta só com login Google (sem senha,
// não passa pelo MFA). A configuração fica em Configurações (montarMfa, /js/mfa-config.js).
import { apiFetch, getUser, getImpersonationInfo } from './api.js';
import { trackEvent } from './analytics.js';

const DIAS_DISPENSADO = 30;
const chave = () => `aviso_mfa_dispensado_${getUser()?.id ?? ''}`;

function dispensadoRecentemente() {
  try {
    const ts = Number(localStorage.getItem(chave()));
    return ts > 0 && Date.now() - ts < DIAS_DISPENSADO * 86400000;
  } catch {
    return false;
  }
}

export async function mostrarAvisoMfa(container) {
  if (!container || getImpersonationInfo() || dispensadoRecentemente()) return;

  let status;
  try {
    status = await apiFetch('/auth/mfa/status');
  } catch {
    return;
  }
  if (!status || status.ativo || !status.temSenha) return;

  const aviso = document.createElement('div');
  aviso.id = 'avisoMfa';
  aviso.setAttribute('role', 'note');
  aviso.style.cssText = 'display:flex;align-items:center;gap:12px;flex-wrap:wrap;margin:0 0 16px;padding:12px 16px;'
    + 'border:1px solid var(--color-border, #e5e7eb);border-left:4px solid var(--color-primary, #1a56db);'
    + 'border-radius:8px;background:var(--color-surface, #fff);color:var(--color-text, #111827)';
  aviso.innerHTML = `
    <span style="font-size:20px" aria-hidden="true">🔒</span>
    <div style="flex:1;min-width:220px;font-size:14px;line-height:1.4">
      <strong>Proteja sua conta com a verificação em duas etapas.</strong>
      <span style="color:var(--color-text-muted, #6b7280)">Além da senha, o login passa a pedir um código do seu celular.</span>
    </div>
    <a class="btn btn-primary btn-sm" href="/pages/configuracoes.html#mfa" data-aviso-mfa="ativar">Ativar agora</a>
    <button type="button" class="btn btn-secondary btn-sm" data-aviso-mfa="dispensar">Agora não</button>`;

  aviso.querySelector('[data-aviso-mfa="ativar"]').addEventListener('click', () => trackEvent('aviso_mfa_ativar'));
  aviso.querySelector('[data-aviso-mfa="dispensar"]').addEventListener('click', () => {
    try { localStorage.setItem(chave(), String(Date.now())); } catch { /* sem storage: some só nesta visita */ }
    trackEvent('aviso_mfa_dispensado');
    aviso.remove();
  });

  container.prepend(aviso);
}
