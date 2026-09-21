/**
 * Fluxos de tarefas E2E.
 * Requer API rodando com banco populado e credenciais em .env.
 */
import { test, expect } from '../../fixtures';

test.skip(
  !process.env.TEST_ADMIN_EMAIL,
  'Credenciais de teste não configuradas. Copie .env.example para .env.',
);

test('deep link ?abrirId= abre a tarefa correta no modal de edição', async ({ adminPage: page }) => {
  // Precisa navegar antes de ler sessionStorage (sem isso, a página em branco inicial
  // não tem origin e o acesso é negado pelo navegador).
  await page.goto('/pages/tarefas.html');
  const token = await page.evaluate(() => sessionStorage.getItem('access_token'));

  // Cria uma tarefa via API para ter um ID real a testar.
  const createRes = await page.request.post('/api/tarefas', {
    headers: { Authorization: `Bearer ${token}` },
    data: { titulo: 'Tarefa deep-link E2E', prioridade: 'Media', tipo: 'Tarefa' },
  });
  expect(createRes.ok()).toBeTruthy();
  const tarefa = await createRes.json();

  try {
    // Regressão: switchTab('tarefas') faz history.replaceState(pathname) na inicialização
    // da página, o que apagava a querystring (?abrirId=) ANTES dela ser lida — o modal nunca
    // abria. O fix captura os query params antes de qualquer replaceState.
    await page.goto(`/pages/tarefas.html?abrirId=${tarefa.id}`);
    await page.waitForLoadState('networkidle', { timeout: 15_000 }).catch(() => {});

    const modal = page.locator('#modalTarefa');
    await expect(modal).toBeVisible({ timeout: 5_000 });
    await expect(page.locator('#fTitulo')).toHaveValue('Tarefa deep-link E2E');
  } finally {
    await page.request.delete(`/api/tarefas/${tarefa.id}`, {
      headers: { Authorization: `Bearer ${token}` },
    });
  }
});
