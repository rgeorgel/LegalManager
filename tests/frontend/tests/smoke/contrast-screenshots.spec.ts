import { test, expect } from '../../fixtures';

test.describe('Contrast em dark themes', () => {
  for (const preset of ['dark', 'dracula', 'cyberpunk', 'synthwave', 'matrix']) {
    test(`preset "${preset}" tem contraste legível em honorarios/processos/agenda + logo transparente`, async ({ adminPage: page, request }) => {
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
      await page.locator(`.preset-card[data-preset-id="${preset}"]`).click();
      await page.waitForTimeout(200);
      await page.click('#btnSalvar');
      await page.waitForTimeout(800);

      const pages = [
        { name: 'honorarios-contratos', url: '/pages/honorarios-contratos.html' },
        { name: 'processos', url: '/pages/processos.html' },
        { name: 'agenda', url: '/pages/agenda.html' },
      ];

      for (const p of pages) {
        await page.goto(p.url);
        await page.waitForLoadState('networkidle');
        await page.waitForTimeout(1500);
        const probe = await page.evaluate(() => {
          const body = getComputedStyle(document.body).backgroundColor;
          const logoSrc = document.querySelector('.header-logo img')?.src || '';
          return { body, logo: logoSrc.includes('transparente') };
        });
        expect(probe.body, `body bg dark em ${p.url}`).not.toBe('rgb(255, 255, 255)');
        expect(probe.logo, `logo transparente em ${p.url}`).toBe(true);
      }
    });
  }
});