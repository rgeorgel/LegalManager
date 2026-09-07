import { test, expect } from '../../fixtures';

test.describe('Sessão e tema após navegação', () => {
  test('Salvar persiste e navegação subsequente NÃO dispara refresh malformado', async ({ adminPage: page }) => {
    await page.goto('/pages/tema.html');
    await page.waitForLoadState('networkidle');

    await page.fill('#primaryColor', '#ff5722');
    await page.click('#btnSalvar');
    await page.waitForTimeout(800);

    const afterSave = await page.evaluate(() => {
      const msg = document.getElementById('themeMsg');
      return {
        primary: getComputedStyle(document.documentElement).getPropertyValue('--color-primary').trim(),
        msgOk: msg?.className.includes('alert-success') && msg?.style.display === 'block',
      };
    });
    expect(afterSave.primary).toBe('#ff5722');
    expect(afterSave.msgOk).toBe(true);

    const beforeNav = await page.evaluate(() => sessionStorage.getItem('tenant_theme'));
    expect(beforeNav).toContain('#ff5722');

    // Navega para dashboard.html. Se refreshTokenIfNeeded estivesse enviando
    // body malformado ("[object Object]"), o servidor retornaria 400, a sessão
    // seria limpa via clearSession() e a página seria redirecionada para login.
    await page.goto('/pages/dashboard.html');
    await page.waitForLoadState('networkidle');
    await page.waitForTimeout(500);

    const afterNav = await page.evaluate(() => ({
      url: window.location.pathname,
      primary: getComputedStyle(document.documentElement).getPropertyValue('--color-primary').trim(),
      stored: sessionStorage.getItem('tenant_theme'),
      access_token: !!sessionStorage.getItem('access_token'),
    }));

    expect(afterNav.url).toBe('/pages/dashboard.html');
    expect(afterNav.access_token).toBe(true);
    expect(afterNav.primary).toBe('#ff5722');
    expect(afterNav.stored).toContain('#ff5722');
  });

  test('refresh token request envia JSON válido (regressão para body "[object Object]")', async ({ request }) => {
    // login
    const loginRes = await request.post('/api/auth/login', {
      data: {
        email: process.env.TEST_ADMIN_EMAIL ?? '',
        senha: process.env.TEST_ADMIN_PASSWORD ?? '',
      },
    });
    expect(loginRes.status()).toBe(200);
    const session = await loginRes.json();
    expect(session.refreshToken).toBeTruthy();

    // refresh — o body TEM que ser JSON serializado, não "[object Object]"
    const refreshRes = await request.post('/api/auth/refresh', {
      data: { refreshToken: session.refreshToken },
    });
    expect(refreshRes.status()).toBe(200);

    // Se o body fosse "[object Object]" o servidor retornaria 400 com
    // mensagem de "Failed to read parameter ..." ou similar.
    const newSession = await refreshRes.json();
    expect(newSession.accessToken).toBeTruthy();
    expect(newSession.refreshToken).toBeTruthy();
  });

  test('DELETE /tenants/current/logo limpa o logo do tenant (regressão para "logo persistia após remover")', async ({ request }) => {
    const loginRes = await request.post('/api/auth/login', {
      data: {
        email: process.env.TEST_ADMIN_EMAIL ?? '',
        senha: process.env.TEST_ADMIN_PASSWORD ?? '',
      },
    });
    const session = await loginRes.json();
    const auth = { Authorization: `Bearer ${session.accessToken}` };

    // 1. Define um logo dummy no tenant (não precisa ser URL válida do OCI;
    // o contrato é apenas armazenar string em Tenant.LogoUrl)
    await request.put('/api/tenants/current/theme', {
      headers: auth,
      data: { primaryColor: '#ff5722' },
    });

    // Confirma que o logo atual pode ser lido
    const get1 = await request.get('/api/tenants/current/theme', { headers: auth });
    expect(get1.status()).toBe(200);
    const theme1 = await get1.json();
    // LogoUrl pode ser null inicialmente — tudo bem

    // 2. Faz upload de um logo mínimo (1x1 PNG transparente)
    const tinyPng = 'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR4nGNgYPj/HwAFBQIANAlx1gAAAABJRU5ErkJggg==';
    const uploadRes = await request.post('/api/tenants/current/logo', {
      headers: auth,
      data: { base64: `data:image/png;base64,${tinyPng}`, contentType: 'image/png' },
    });
    if (uploadRes.status() === 403) {
      // Plano Plus é o mínimo exigido — pular se não atender
      test.skip();
      return;
    }
    expect(uploadRes.status()).toBe(200);
    const uploadJson = await uploadRes.json();
    expect(uploadJson.logoUrl).toBeTruthy();

    // 3. Garante que o logo foi persistido
    const get2 = await request.get('/api/tenants/current/theme', { headers: auth });
    const theme2 = await get2.json();
    expect(theme2.logoUrl).toBeTruthy();

    // 4. DELETE deve limpar o logo (bug era: DELETE não existia ou Remover só atualizava UI)
    const delRes = await request.delete('/api/tenants/current/logo', { headers: auth });
    expect(delRes.status()).toBe(204);

    // 5. Confirma que logoUrl voltou a ser null
    const get3 = await request.get('/api/tenants/current/theme', { headers: auth });
    const theme3 = await get3.json();
    expect(theme3.logoUrl).toBeNull();
  });
});

test.describe('Temas predefinidos', () => {
  test('clicar em card aplica cores ao formulário e ao tema visual', async ({ adminPage: page, request }) => {
    // Reseta tema para um estado neutro antes do teste
    const loginRes = await request.post('/api/auth/login', {
      data: {
        email: process.env.TEST_ADMIN_EMAIL ?? '',
        senha: process.env.TEST_ADMIN_PASSWORD ?? '',
      },
    });
    const session = await loginRes.json();
    await request.put('/api/tenants/current/theme', {
      headers: { Authorization: `Bearer ${session.accessToken}` },
      data: {
        primaryColor: '#111111',
        sidebarColor: '#222222',
        accentColor: '#333333',
        layoutMode: 'default',
        customCss: '',
      },
    });

    await page.goto('/pages/tema.html');
    await page.waitForLoadState('networkidle');

    // Grid renderizado
    const cardCount = await page.locator('.preset-card').count();
    expect(cardCount).toBeGreaterThanOrEqual(7);

    // Clica no preset "Dracula" e verifica que o form + DOM foram atualizados
    await page.locator('.preset-card[data-preset-id="dracula"]').click();
    await page.waitForTimeout(150);

    const form = await page.evaluate(() => ({
      primary: (document.getElementById('primaryColor') as HTMLInputElement).value,
      sidebar: (document.getElementById('sidebarColor') as HTMLInputElement).value,
      accent: (document.getElementById('accentColor') as HTMLInputElement).value,
      cssVar: getComputedStyle(document.documentElement).getPropertyValue('--color-primary').trim(),
    }));
    expect(form.primary).toBe('#bd93f9');
    expect(form.sidebar).toBe('#282a36');
    expect(form.accent).toBe('#ff79c6');
    expect(form.cssVar).toBe('#bd93f9');

    // Salvar persiste o preset
    await page.click('#btnSalvar');
    await page.waitForTimeout(800);
    const afterSave = await page.evaluate(() => document.getElementById('themeMsg')?.className);
    expect(afterSave).toContain('alert-success');

    // Após salvar, o card Dracula fica marcado como ativo
    const activePresetId = await page.locator('.preset-card.active').getAttribute('data-preset-id');
    expect(activePresetId).toBe('dracula');

    // Navegação subsequente mantém o tema (regressão também para o preset path)
    await page.goto('/pages/dashboard.html');
    await page.waitForLoadState('networkidle');
    await page.waitForTimeout(300);
    const onDashboard = await page.evaluate(() =>
      getComputedStyle(document.documentElement).getPropertyValue('--color-primary').trim()
    );
    expect(onDashboard).toBe('#bd93f9');
  });

  test('preset "Escuro" aplica customCss de dark mode completo (bg escuro em outras páginas)', async ({ adminPage: page, request }) => {
    const loginRes = await request.post('/api/auth/login', {
      data: {
        email: process.env.TEST_ADMIN_EMAIL ?? '',
        senha: process.env.TEST_ADMIN_PASSWORD ?? '',
      },
    });
    const session = await loginRes.json();
    await request.put('/api/tenants/current/theme', {
      headers: { Authorization: `Bearer ${session.accessToken}` },
      data: { primaryColor: '#111111', sidebarColor: '#222222', accentColor: '#333333', layoutMode: 'default', customCss: '' },
    });

    await page.goto('/pages/tema.html');
    await page.waitForLoadState('networkidle');

    await page.locator('.preset-card[data-preset-id="dark"]').click();
    await page.waitForTimeout(150);

    // Em /pages/tema.html o body deve estar com o bg dark aplicado
    const temaPageBg = await page.evaluate(() => getComputedStyle(document.body).backgroundColor);
    // #0f172a → rgb(15, 23, 42)
    expect(temaPageBg.replace(/\s/g, '')).toBe('rgb(15,23,42)');

    await page.click('#btnSalvar');
    await page.waitForTimeout(800);

    // Navega para dashboard e confirma que o bg escuro persistiu (customCss
    // está em #tenant-custom-css que é parte do <head>, sobrevive à navegação)
    await page.goto('/pages/dashboard.html');
    await page.waitForLoadState('networkidle');
    await page.waitForTimeout(500);

    const onDashboard = await page.evaluate(() => {
      const cs = getComputedStyle;
      const statCard = document.querySelector('.stat-card');
      const finSection = document.getElementById('finSection');
      return {
        body: cs(document.body).backgroundColor,
        header: cs(document.querySelector('.header')).backgroundColor,
        sidebar: cs(document.querySelector('.sidebar')).backgroundColor,
        statCard: statCard ? cs(statCard).backgroundColor : null,
        // Financeiro usa style="background:var(--color-surface)" inline —
        // se :root não for sobrescrito, fica branco.
        finSection: finSection ? cs(finSection).backgroundColor : null,
        hasCustomStyle: !!document.getElementById('tenant-custom-css'),
      };
    });
    expect(onDashboard.body.replace(/\s/g, '')).toBe('rgb(15,23,42)');
    // O stat-card de "Processos Ativos" deve estar escuro (customCss ativo)
    expect(onDashboard.statCard?.replace(/\s/g, '')).toBe('rgb(30,41,59)'); // #1e293b
    // Financeiro também fica escuro (override de :root --color-surface)
    expect(onDashboard.finSection?.replace(/\s/g, '')).toBe('rgb(30,41,59)');
    expect(onDashboard.hasCustomStyle).toBe(true);
  });

  test('preset "Dracula" aplica paleta Dracula (bg #21222c / surface #282a36)', async ({ adminPage: page, request }) => {
    const loginRes = await request.post('/api/auth/login', {
      data: {
        email: process.env.TEST_ADMIN_EMAIL ?? '',
        senha: process.env.TEST_ADMIN_PASSWORD ?? '',
      },
    });
    const session = await loginRes.json();
    await request.put('/api/tenants/current/theme', {
      headers: { Authorization: `Bearer ${session.accessToken}` },
      data: { primaryColor: '#111111', sidebarColor: '#222222', accentColor: '#333333', layoutMode: 'default', customCss: '' },
    });

    await page.goto('/pages/tema.html');
    await page.waitForLoadState('networkidle');

    await page.locator('.preset-card[data-preset-id="dracula"]').click();
    await page.waitForTimeout(150);

    const temaPageBg = await page.evaluate(() => getComputedStyle(document.body).backgroundColor);
    // #21222c → rgb(33, 34, 44)
    expect(temaPageBg.replace(/\s/g, '')).toBe('rgb(33,34,44)');

    await page.click('#btnSalvar');
    await page.waitForTimeout(800);

    await page.goto('/pages/dashboard.html');
    await page.waitForLoadState('networkidle');
    await page.waitForTimeout(500);

    const onDashboard = await page.evaluate(() => {
      const cs = getComputedStyle;
      const statCard = document.querySelector('.stat-card');
      const finSection = document.getElementById('finSection');
      return {
        body: cs(document.body).backgroundColor,
        statCard: statCard ? cs(statCard).backgroundColor : null,
        finSection: finSection ? cs(finSection).backgroundColor : null,
      };
    });
    expect(onDashboard.body.replace(/\s/g, '')).toBe('rgb(33,34,44)');
    expect(onDashboard.statCard?.replace(/\s/g, '')).toBe('rgb(40,42,54)'); // #282a36
    expect(onDashboard.finSection?.replace(/\s/g, '')).toBe('rgb(40,42,54)');
  });
});