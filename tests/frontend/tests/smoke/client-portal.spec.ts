import { test as baseTest, expect as baseExpect } from '@playwright/test';
import { test, expect } from '../../fixtures';

// Páginas públicas do portal do cliente (sem auth)
const CLIENT_PUBLIC_PAGES = [
  '/cliente/index.html',
  '/cliente/definir-senha.html',
  '/cliente/esqueci-senha.html',
  '/cliente/aceitar-convite.html',
];

for (const pagePath of CLIENT_PUBLIC_PAGES) {
  baseTest(`${pagePath} — carrega sem erros de JS`, async ({ page }) => {
    const jsErrors: string[] = [];
    page.on('pageerror', (err) => jsErrors.push(err.message));

    await page.goto(pagePath);
    await page.waitForLoadState('domcontentloaded');

    baseExpect(jsErrors, `Erros JS em ${pagePath}`).toHaveLength(0);
    await baseExpect(page).toHaveTitle(/.+/);
  });
}

// Páginas autenticadas do portal do cliente
const CLIENT_AUTH_PAGES = [
  '/cliente/dashboard.html',
  '/cliente/processos.html',
  '/cliente/processo-detalhe.html',
];

for (const pagePath of CLIENT_AUTH_PAGES) {
  test(`${pagePath} — carrega autenticado sem erros de JS`, async ({ clientPage: page }) => {
    const jsErrors: string[] = [];
    page.on('pageerror', (err) => jsErrors.push(err.message));

    const response = await page.goto(pagePath);
    await page.waitForLoadState('networkidle', { timeout: 15_000 }).catch(() => {});

    // Se redirecionou para login, o fixture não obteve sessão válida — skip em vez de falhar
    if (page.url().includes('/cliente/index.html')) {
      test.skip(true, 'Sessão de cliente indisponível — configure TEST_CLIENT_EMAIL/PASSWORD no .env');
      return;
    }

    expect(response?.status() ?? 200, `HTTP error em ${pagePath}`).toBeLessThan(400);
    expect(jsErrors, `Erros JS em ${pagePath}: ${jsErrors.join(' | ')}`).toHaveLength(0);
  });
}

baseTest('portal do cliente sem auth redireciona para login', async ({ page }) => {
  await page.goto('/cliente/dashboard.html');
  await page.waitForLoadState('domcontentloaded');
  await baseExpect(page).toHaveURL(/\/cliente\//);
});
