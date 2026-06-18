import { test, expect } from '@playwright/test';

const PUBLIC_PAGES = [
  '/index.html',
  '/login.html',
  '/register.html',
  '/forgot-password.html',
  '/reset-password.html',
  '/aceitar-convite.html',
  '/termos.html',
  '/privacidade.html',
  '/lp-facebook.html',
];

for (const pagePath of PUBLIC_PAGES) {
  test(`${pagePath} — carrega sem erros de JS`, async ({ page }) => {
    const jsErrors: string[] = [];
    page.on('pageerror', (err) => jsErrors.push(err.message));

    await page.goto(pagePath);
    await page.waitForLoadState('domcontentloaded');

    expect(jsErrors, `Erros JS em ${pagePath}`).toHaveLength(0);
    await expect(page).toHaveTitle(/.+/);
  });
}

test('login.html — formulário possui campos de email e senha', async ({ page }) => {
  await page.goto('/login.html');
  await expect(page.locator('#email')).toBeVisible();
  await expect(page.locator('#senha')).toBeVisible();
  await expect(page.locator('[type=submit]')).toBeVisible();
});

// Redirect de usuário logado é testado em tests/flows/auth.spec.ts (requer credenciais reais)
