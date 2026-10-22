import { test, expect } from '../../fixtures';

// Todas as páginas do portal super admin que requerem autenticação
const SUPERADMIN_PAGES = [
  '/superadmin/dashboard.html',
  '/superadmin/tenants.html',
  '/superadmin/tenant-detalhe.html', // sem ?id: mostra "Tenant não informado" sem erro de JS
  '/superadmin/users.html',
  '/superadmin/waitlist.html',
  '/superadmin/tema.html',
  '/superadmin/consultas.html',
  '/superadmin/assistente.html',
  '/superadmin/codigos-promocionais.html',
  '/superadmin/precos-planos.html',
  '/superadmin/parceiros.html',
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

test('ficha do tenant abre em página própria com a timeline', async ({ superAdminPage: page }) => {
  const jsErrors: string[] = [];
  page.on('pageerror', (err) => jsErrors.push(err.message));

  await page.goto('/superadmin/tenants.html');
  const detalhes = page.locator('a.action-btn', { hasText: 'Detalhes' }).first();
  await expect(detalhes).toBeVisible({ timeout: 15_000 });
  await detalhes.click();

  await expect(page).toHaveURL(/\/superadmin\/tenant-detalhe\.html\?id=/);
  await expect(page.locator('#tenantNome')).not.toBeEmpty({ timeout: 15_000 });
  // Timeline é a aba padrão: termina em eventos, "carregar mais" ou estado vazio.
  await expect(page.locator('#tlRodape button, #tlRodape span, #tlRodape .tl-vazio').first()).toBeVisible({ timeout: 15_000 });
  await expect(page.locator('#tlRodape')).not.toContainText('Erro');

  // Link antigo (dashboard/usuários) redireciona para a página nova.
  const id = new URL(page.url()).searchParams.get('id');
  await page.goto(`/superadmin/tenants.html?id=${id}`);
  await expect(page).toHaveURL(new RegExp(`tenant-detalhe\\.html\\?id=${id}`));

  expect(jsErrors, `Erros JS: ${jsErrors.join(' | ')}`).toHaveLength(0);
});
