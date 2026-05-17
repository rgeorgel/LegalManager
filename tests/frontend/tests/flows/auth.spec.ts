/**
 * Fluxos de autenticação E2E.
 * Requer API rodando em BASE_URL com banco populado.
 * Configurar credenciais em tests/frontend/.env
 */
import { test, expect } from '@playwright/test';

test.skip(
  !process.env.TEST_ADMIN_EMAIL,
  'Credenciais de teste não configuradas. Copie .env.example para .env.',
);

test('login com credenciais válidas redireciona para dashboard', async ({ page }) => {
  await page.goto('/login.html');

  await page.fill('#email', process.env.TEST_ADMIN_EMAIL!);
  await page.fill('#senha', process.env.TEST_ADMIN_PASSWORD!);
  await page.click('[type=submit]');

  await expect(page).toHaveURL(/dashboard\.html/, { timeout: 10_000 });
});

test('login com credenciais inválidas exibe mensagem de erro', async ({ page }) => {
  await page.goto('/login.html');

  await page.fill('#email', 'invalido@test.com');
  await page.fill('#senha', 'senhaerrada');
  await page.click('[type=submit]');

  // Continua na tela de login
  await expect(page).not.toHaveURL(/dashboard\.html/);

  // Exibe alerta de erro
  const alert = page.locator('#alert');
  await expect(alert).toBeVisible();
  await expect(alert).not.toBeEmpty();
});

test('logout limpa sessão e redireciona para login', async ({ page }) => {
  // Login primeiro
  await page.goto('/login.html');
  await page.fill('#email', process.env.TEST_ADMIN_EMAIL!);
  await page.fill('#senha', process.env.TEST_ADMIN_PASSWORD!);
  await page.click('[type=submit]');
  await expect(page).toHaveURL(/dashboard\.html/, { timeout: 10_000 });

  // Logout
  const logoutBtn = page.locator('#logoutBtn');
  await logoutBtn.click();

  // Deve voltar para login e sessionStorage deve estar limpo
  await expect(page).toHaveURL(/login\.html/, { timeout: 10_000 });

  const hasToken = await page.evaluate(() => !!sessionStorage.getItem('access_token'));
  expect(hasToken).toBe(false);
});

test('sessão expirada redireciona para login automaticamente', async ({ page }) => {
  // Injeta token inválido para simular expiração
  await page.addInitScript(() => {
    sessionStorage.setItem('access_token', 'token-expirado-invalido');
    sessionStorage.setItem('refresh_token', 'refresh-invalido');
    sessionStorage.setItem('user', JSON.stringify({ nome: 'Test', plano: 'Free' }));
  });

  await page.goto('/pages/dashboard.html');

  // Aguarda redirecionamento após falha de auth
  await expect(page).toHaveURL(/login\.html/, { timeout: 15_000 });
});
