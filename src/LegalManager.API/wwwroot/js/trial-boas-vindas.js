import { apiFetch } from '/js/api.js';

const FEATURES_PLUS = [
  '💰 Controle Financeiro completo',
  '📈 Indicadores e relatórios do escritório',
  '📅 Calculadora de Prazos processuais',
  '🌐 Portal do Cliente',
  '💼 Honorários e Contratos',
  '⚖️ 20 processos monitorados (vs. 10 no Free)',
  '💾 2 GB de armazenamento (vs. 1 GB no Free)',
];

export async function initTrialBoasVindasModal(onDismiss) {
  try {
    const status = await apiFetch('/assinatura/trial-boas-vindas', { cache: 'no-store' });
    if (!status.exibir) { onDismiss?.(); return; }

    document.getElementById('tbvDiasRestantes').textContent = status.diasRestantes;
    document.getElementById('tbvListaFeatures').innerHTML = FEATURES_PLUS
      .map(f => `<li>${f}</li>`).join('');

    document.getElementById('trialBoasVindasOverlay')?.classList.add('open');

    document.getElementById('tbvFecharBtn').addEventListener('click', async () => {
      document.getElementById('trialBoasVindasOverlay')?.classList.remove('open');
      try { await apiFetch('/assinatura/trial-boas-vindas/visualizado', { method: 'POST' }); } catch {}
      onDismiss?.();
    }, { once: true });
  } catch {
    onDismiss?.();
  }
}
