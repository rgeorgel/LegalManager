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

  // .open é adicionado ao .modal-overlay (wrapper externo)
  const modal = page.locator('.modal-overlay.open');
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

test('filtro de tags está presente na barra de filtros', async ({ adminPage: page }) => {
  await page.goto('/pages/contatos.html');
  await page.waitForLoadState('networkidle', { timeout: 15_000 }).catch(() => {});

  await expect(page.locator('#filtTag')).toBeVisible({ timeout: 5_000 });
  // Sem seleção, o botão mostra "Todas as tags"
  await expect(page.locator('#filtTag .multiselect-label')).toHaveText('Todas as tags');
});

test('clicar em coluna ordenável dispara request com sortBy e sortDir', async ({ adminPage: page }) => {
  await page.goto('/pages/contatos.html');
  await page.waitForLoadState('networkidle', { timeout: 15_000 }).catch(() => {});

  // Limpa qualquer estado persistido de testes anteriores
  await page.evaluate(() => localStorage.removeItem('contatos.listState'));
  await page.reload();
  await page.waitForLoadState('networkidle', { timeout: 15_000 }).catch(() => {});

  const sortRequest = page.waitForRequest(
    (req) => req.url().includes('/api/contatos') && req.url().includes('sortBy=nome'),
    { timeout: 5_000 }
  );
  await page.locator('th.sortable[data-sort="nome"]').click();
  const req = await sortRequest;
  expect(req.url()).toContain('sortBy=nome');
  expect(req.url()).toContain('sortDir=asc');

  // Segundo clique na mesma coluna deve inverter direção
  const descRequest = page.waitForRequest(
    (req) => req.url().includes('sortDir=desc'),
    { timeout: 5_000 }
  );
  await page.locator('th.sortable[data-sort="nome"]').click();
  const req2 = await descRequest;
  expect(req2.url()).toContain('sortBy=nome');
  expect(req2.url()).toContain('sortDir=desc');
});

test('ordenação persiste após reload da página', async ({ adminPage: page }) => {
  await page.goto('/pages/contatos.html');
  await page.waitForLoadState('networkidle', { timeout: 15_000 }).catch(() => {});

  await page.evaluate(() => localStorage.removeItem('contatos.listState'));
  await page.reload();
  await page.waitForLoadState('networkidle', { timeout: 15_000 }).catch(() => {});

  // Clica em "E-mail" (coluna diferente) — deve ir para asc
  await page.locator('th.sortable[data-sort="email"]').click();
  await page.waitForResponse(
    (res) => res.url().includes('/api/contatos') && res.url().includes('sortBy=email'),
    { timeout: 5_000 }
  );

  // Reload preserva estado (coluna ativa + indicador + localStorage)
  await page.reload();
  await page.waitForLoadState('networkidle', { timeout: 15_000 }).catch(() => {});

  const active = await page.locator('th.sortable[data-sort="email"].active').count();
  expect(active).toBe(1);
  const indicator = await page.locator('th.sortable[data-sort="email"] .sort-indicator').textContent();
  expect(indicator?.trim()).toBe('▲');

  // Próxima requisição após reload deve trazer sortBy=email+sortDir=asc
  const reqAfterReload = page.waitForRequest(
    (req) => req.url().includes('/api/contatos') && req.url().includes('sortBy=email') && req.url().includes('sortDir=asc'),
    { timeout: 5_000 }
  );
  // Dispara uma nova busca para forçar reload da lista
  await page.locator('#filtBusca').fill('');
  await page.locator('#filtBusca').press('Tab').catch(() => {});
  const req = await reqAfterReload;
  expect(req.url()).toContain('sortBy=email');
  expect(req.url()).toContain('sortDir=asc');
});

test('várias tags e categorias são enviadas ao backend', async ({ adminPage: page }) => {
  // Simula as tags do escritório para não depender dos dados do ambiente
  await page.route('**/api/contatos/tags', (route) =>
    route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(['novo', 'vip']) }));

  await page.goto('/pages/contatos.html');
  await page.waitForLoadState('networkidle', { timeout: 15_000 }).catch(() => {});
  await page.evaluate(() => localStorage.removeItem('contatos.listState'));
  await page.reload();
  await page.waitForLoadState('networkidle', { timeout: 15_000 }).catch(() => {});

  await page.locator('#filtTag .multiselect-toggle').click();
  await page.locator('#filtTag input[value="vip"]').check();
  const tagReq = page.waitForRequest(
    (req) => req.url().includes('/api/contatos?') && req.url().includes('tag=vip') && req.url().includes('tag=novo'),
    { timeout: 8_000 }
  );
  await page.locator('#filtTag input[value="novo"]').check();
  await tagReq;
  await expect(page.locator('#filtTag .multiselect-label')).toHaveText('2 tags');

  await page.locator('#filtTipoContato .multiselect-toggle').click();
  await page.locator('#filtTipoContato input[value="Cliente"]').check();
  const catReq = page.waitForRequest(
    (req) => req.url().includes('tipoContato=Cliente') && req.url().includes('tipoContato=Perito'),
    { timeout: 8_000 }
  );
  await page.locator('#filtTipoContato input[value="Perito"]').check();
  const req = await catReq;
  expect(req.url()).toContain('tag=vip');
});
