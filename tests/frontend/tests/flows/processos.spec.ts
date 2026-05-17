/**
 * Fluxos de processos E2E.
 * Requer API rodando com banco populado e credenciais em .env.
 */
import { test, expect } from '../../fixtures';

test.skip(
  !process.env.TEST_ADMIN_EMAIL,
  'Credenciais de teste não configuradas. Copie .env.example para .env.',
);

test('lista de processos carrega e exibe tabela', async ({ adminPage: page }) => {
  await page.goto('/pages/processos.html');
  await page.waitForLoadState('networkidle', { timeout: 15_000 }).catch(() => {});

  // Verifica que a página carregou sem ir para login
  expect(page.url()).not.toContain('login.html');

  // Deve haver uma tabela ou lista de processos (ou mensagem de vazio)
  const hasList = await page.locator('table, .processo-item, [class*="processo"], .empty-state').count();
  expect(hasList).toBeGreaterThan(0);
});

test('botão de novo processo abre modal ou navega para formulário', async ({ adminPage: page }) => {
  await page.goto('/pages/processos.html');
  await page.waitForLoadState('networkidle', { timeout: 15_000 }).catch(() => {});

  // Procura botão de novo processo
  const btnNovo = page.locator('[id*="Novo"], [id*="novo"], button:has-text("Novo"), button:has-text("+ Novo")').first();
  await expect(btnNovo).toBeVisible({ timeout: 5_000 });

  await btnNovo.click();

  // Deve abrir modal ou navegar — verificar que algo mudou
  await page.waitForTimeout(300);
  const modal = page.locator('.modal, dialog, [role="dialog"]');
  const onForm = page.url().includes('formulario') || page.url().includes('novo');
  const modalVisible = await modal.isVisible().catch(() => false);

  expect(modalVisible || onForm, 'Modal ou formulário deveria ter aberto').toBeTruthy();
});

test('detalhe de processo sem ID mostra erro sem crash de JS', async ({ adminPage: page }) => {
  const jsErrors: string[] = [];
  page.on('pageerror', (err) => jsErrors.push(err.message));

  await page.goto('/pages/processo-detalhe.html');
  await page.waitForLoadState('networkidle', { timeout: 10_000 }).catch(() => {});

  // Sem crash de JS mesmo sem ID de processo
  expect(jsErrors).toHaveLength(0);
  expect(page.url()).not.toContain('login.html');
});
