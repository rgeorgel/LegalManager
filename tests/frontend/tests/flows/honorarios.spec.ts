/**
 * Fluxos de honorários E2E.
 * Requer API rodando com banco populado e credenciais em .env.
 */
import { test, expect } from '../../fixtures';

test.skip(
  !process.env.TEST_ADMIN_EMAIL,
  'Credenciais de teste não configuradas. Copie .env.example para .env.',
);

test('página de listagem de honorários carrega sem erros de JS', async ({ adminPage: page }) => {
  const jsErrors: string[] = [];
  page.on('pageerror', (err) => jsErrors.push(err.message));

  await page.goto('/pages/honorarios-contratos.html');
  await page.waitForLoadState('networkidle', { timeout: 15_000 }).catch(() => {});

  expect(page.url()).not.toContain('login.html');
  expect(jsErrors, `Erros JS: ${jsErrors.join(' | ')}`).toHaveLength(0);
  await expect(page.locator('.page-title')).toBeVisible();
});

test('botão Novo Contrato navega para formulário', async ({ adminPage: page }) => {
  await page.goto('/pages/honorarios-contratos.html');
  await page.waitForLoadState('networkidle', { timeout: 15_000 }).catch(() => {});

  const linkNovo = page.locator('a[href*="honorarios-contrato-novo.html"]').first();
  await expect(linkNovo).toBeVisible({ timeout: 5_000 });
  await linkNovo.click();
  await page.waitForLoadState('networkidle', { timeout: 5_000 }).catch(() => {});

  expect(page.url()).toContain('honorarios-contrato-novo.html');
  await expect(page.locator('#form')).toBeVisible();
});

test('página de configurações carrega', async ({ adminPage: page }) => {
  await page.goto('/pages/honorarios-config.html');
  await page.waitForLoadState('networkidle', { timeout: 15_000 }).catch(() => {});

  expect(page.url()).not.toContain('login.html');
  await expect(page.locator('#nomeEscritorio')).toBeVisible();
});
