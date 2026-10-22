import { test, expect } from '@playwright/test';

// Páginas públicas do portal do parceiro (sem auth)
const PARTNER_PUBLIC_PAGES = [
  '/parceiro/index.html',
  '/parceiro/definir-senha.html',
  '/parceiro/esqueci-senha.html',
];

for (const pagePath of PARTNER_PUBLIC_PAGES) {
  test(`${pagePath} — carrega sem erros de JS`, async ({ page }) => {
    const jsErrors: string[] = [];
    page.on('pageerror', (err) => jsErrors.push(err.message));

    await page.goto(pagePath);
    await page.waitForLoadState('domcontentloaded');

    expect(jsErrors, `Erros JS em ${pagePath}`).toHaveLength(0);
    await expect(page).toHaveTitle(/.+/);
  });
}

test('painel do parceiro sem auth redireciona para login', async ({ page }) => {
  await page.goto('/parceiro/painel.html');
  await expect(page).toHaveURL(/\/parceiro\/index\.html/);
});

test('cadastro com ?parceiro= abre o campo do código já preenchido', async ({ page }) => {
  await page.goto('/register.html?parceiro=teste-parceiro');
  await expect(page.locator('#parceiroGroup')).toBeVisible();
  await expect(page.locator('#codigoParceiro')).toHaveValue('TESTE-PARCEIRO');
});
