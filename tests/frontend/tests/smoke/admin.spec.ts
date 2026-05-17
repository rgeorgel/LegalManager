import { test, expect } from '../../fixtures';

// Todas as páginas do portal admin que requerem autenticação
const ADMIN_PAGES = [
  '/pages/dashboard.html',
  '/pages/processos.html',
  '/pages/processo-detalhe.html',
  '/pages/contatos.html',
  '/pages/documentos.html',
  '/pages/tarefas.html',
  '/pages/agenda.html',
  '/pages/timesheet.html',
  '/pages/publicacoes.html',
  '/pages/prazos.html',
  '/pages/kanban.html',
  '/pages/financeiro.html',
  '/pages/honorarios.html',
  '/pages/notificacoes.html',
  '/pages/alertas.html',
  '/pages/usuarios.html',
  '/pages/configuracoes.html',
  '/pages/assinatura.html',
  '/pages/indicadores.html',
  '/pages/modelos.html',
  '/pages/debug-esaj.html',
];

for (const pagePath of ADMIN_PAGES) {
  test(`${pagePath} — carrega autenticado sem erros de JS`, async ({ adminPage: page }) => {
    const jsErrors: string[] = [];
    page.on('pageerror', (err) => jsErrors.push(err.message));

    const response = await page.goto(pagePath);
    await page.waitForLoadState('networkidle', { timeout: 15_000 }).catch(() => {});

    // Não foi redirecionado para login
    expect(page.url(), `Redirecionou para login em ${pagePath}`).not.toContain('login.html');

    // Recurso estático carregou com sucesso
    expect(
      response?.status() ?? 200,
      `HTTP error em ${pagePath}`,
    ).toBeLessThan(400);

    // Sem exceções JS não capturadas
    expect(jsErrors, `Erros JS em ${pagePath}: ${jsErrors.join(' | ')}`).toHaveLength(0);

    await expect(page).toHaveTitle(/.+/);
  });
}

test('página sem autenticação redireciona para login', async ({ page }) => {
  // Navega sem tokens no sessionStorage
  await page.goto('/pages/dashboard.html');
  await page.waitForLoadState('domcontentloaded');
  await expect(page).toHaveURL(/login\.html/);
});
