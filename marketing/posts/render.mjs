import { fileURLToPath } from 'node:url';
import path from 'node:path';

const dir = path.dirname(fileURLToPath(import.meta.url));
const playwrightEntry = path.join(dir, '../../tests/frontend/node_modules/@playwright/test/index.mjs');
const { chromium } = await import(playwrightEntry);

const posts = ['p1', 'p2', 'p3', 'p4', 'p5', 'p6', 'p7', 'p8', 'p9', 'p10'];
const names = {
  p1: 'causify-post-prazos-1200x1200.png',
  p2: 'causify-post-honorarios-1200x1200.png',
  p3: 'causify-post-organizacao-1200x1200.png',
  p4: 'causify-post-portal-cliente-1200x1200.png',
  p5: 'causify-post-timesheet-1200x1200.png',
  p6: 'causify-post-publicacoes-1200x1200.png',
  p7: 'causify-post-documentos-1200x1200.png',
  p8: 'causify-post-indicadores-1200x1200.png',
  p9: 'causify-post-tarefas-equipe-1200x1200.png',
  p10: 'causify-post-contatos-1200x1200.png',
};

const browser = await chromium.launch();
const page = await browser.newPage({
  viewport: { width: 1400, height: 1400 },
  deviceScaleFactor: 2,
});
await page.goto('file://' + path.join(dir, 'posts.html'));
await page.waitForLoadState('networkidle');

for (const id of posts) {
  const out = path.join(dir, 'out', names[id]);
  await page.locator('#' + id).screenshot({ path: out });
  console.log('ok ' + names[id]);
}

await browser.close();
