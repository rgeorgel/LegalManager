/**
 * Testes de regressão visual (screenshot baseline).
 *
 * PRIMEIRO USO: gerar os baselines com:
 *   npm run test:update-snapshots
 *
 * Após isso, os snapshots ficam em *-snapshots/ e devem ser commitados.
 * Mudanças visuais inesperadas causarão falha com diff visual.
 *
 * USO NORMAL:
 *   npm run test:visual
 */
import { test, expect } from '../../fixtures';

const KEY_PAGES: { path: string; name: string }[] = [
  { path: '/pages/dashboard.html', name: 'dashboard' },
  { path: '/pages/processos.html', name: 'processos' },
  { path: '/pages/contatos.html', name: 'contatos' },
  { path: '/pages/tarefas.html', name: 'tarefas' },
  { path: '/pages/financeiro.html', name: 'financeiro' },
];

for (const { path, name } of KEY_PAGES) {
  test(`visual: ${name}`, async ({ adminPage: page }) => {
    await page.goto(path);
    await page.waitForLoadState('networkidle', { timeout: 15_000 }).catch(() => {});

    // Aguarda animações terminarem
    await page.waitForTimeout(500);

    await expect(page).toHaveScreenshot(`${name}.png`, {
      maxDiffPixelRatio: 0.02,
      animations: 'disabled',
    });
  });
}
