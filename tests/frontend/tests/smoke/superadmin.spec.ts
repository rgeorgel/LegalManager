import { test, expect } from '../../fixtures';

// Todas as páginas do portal super admin que requerem autenticação
const SUPERADMIN_PAGES = [
  '/superadmin/dashboard.html',
  '/superadmin/tenants.html',
  '/superadmin/users.html',
  '/superadmin/waitlist.html',
  '/superadmin/tema.html',
  '/superadmin/consultas.html',
];

for (const pagePath of SUPERADMIN_PAGES) {
  test(`${pagePath} — carrega autenticado sem erros de JS`, async ({ superAdminPage: page }) => {
    const jsErrors: string[] = [];
    page.on('pageerror', (err) => jsErrors.push(err.message));

    const response = await page.goto(pagePath);
    await page.waitForLoadState('networkidle', { timeout: 15_000 }).catch(() => {});

    expect(page.url(), `Redirecionou para login em ${pagePath}`).not.toContain('login.html');

    expect(
      response?.status() ?? 200,
      `HTTP error em ${pagePath}`,
    ).toBeLessThan(400);

    expect(jsErrors, `Erros JS em ${pagePath}: ${jsErrors.join(' | ')}`).toHaveLength(0);

    await expect(page).toHaveTitle(/.+/);
  });
}

test('página super admin sem autenticação redireciona para login', async ({ page }) => {
  await page.goto('/superadmin/dashboard.html');
  await page.waitForLoadState('domcontentloaded');
  await expect(page).toHaveURL(/superadmin\/login\.html/);
});
