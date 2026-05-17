/**
 * Fluxos de contatos E2E.
 * Requer API rodando com banco populado e credenciais em .env.
 */
import { test, expect } from '../../fixtures';

test.skip(
  !process.env.TEST_ADMIN_EMAIL,
  'Credenciais de teste não configuradas. Copie .env.example para .env.',
);

test('lista de contatos carrega sem erros', async ({ adminPage: page }) => {
  const jsErrors: string[] = [];
  page.on('pageerror', (err) => jsErrors.push(err.message));

  await page.goto('/pages/contatos.html');
  await page.waitForLoadState('networkidle', { timeout: 15_000 }).catch(() => {});

  expect(page.url()).not.toContain('login.html');
  expect(jsErrors, `Erros JS: ${jsErrors.join(' | ')}`).toHaveLength(0);

  // Deve haver título da página
  await expect(page.locator('.page-title, h1')).toBeVisible();
});

test('botão Novo Contato abre modal de cadastro', async ({ adminPage: page }) => {
  await page.goto('/pages/contatos.html');
  await page.waitForLoadState('networkidle', { timeout: 15_000 }).catch(() => {});

  const btnNovo = page.locator('#btnNovo');
  await expect(btnNovo).toBeVisible({ timeout: 5_000 });
  await btnNovo.click();

  // Modal deve aparecer
  const modal = page.locator('.modal, dialog, [role="dialog"]');
  await expect(modal).toBeVisible({ timeout: 3_000 });
});

test('busca de contatos filtra a lista', async ({ adminPage: page }) => {
  await page.goto('/pages/contatos.html');
  await page.waitForLoadState('networkidle', { timeout: 15_000 }).catch(() => {});

  const searchInput = page.locator('input[type="search"], input[placeholder*="buscar" i], input[placeholder*="pesquisar" i], #busca, #search').first();
  const hasSearch = await searchInput.isVisible().catch(() => false);

  if (hasSearch) {
    await searchInput.fill('ZZZZ_inexistente');
    await page.waitForTimeout(500);

    // A lista deve estar vazia ou exibir mensagem de sem resultados
    const rows = await page.locator('tr[data-id], .contato-item, [class*="contato-row"]').count();
    expect(rows).toBe(0);
  }
});
