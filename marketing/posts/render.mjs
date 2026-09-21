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

// Instagram feed — 1080x1350 (4:5), formato recomendado pelo Instagram
// (usa mais espaço vertical no feed do que o quadrado 1080x1080/1200x1200).
// Os mesmos PNGs do LinkedIn (1200x1200) servem para o Facebook, que aceita
// bem posts quadrados sem corte — por isso não há um render específico de FB.
const igPosts = ['ig1', 'ig2', 'ig3', 'ig4', 'ig5', 'ig6', 'ig7', 'ig8', 'ig9', 'ig10'];
const igNames = {
  ig1: 'causify-post-prazos-instagram-1080x1350.png',
  ig2: 'causify-post-honorarios-instagram-1080x1350.png',
  ig3: 'causify-post-organizacao-instagram-1080x1350.png',
  ig4: 'causify-post-portal-cliente-instagram-1080x1350.png',
  ig5: 'causify-post-timesheet-instagram-1080x1350.png',
  ig6: 'causify-post-publicacoes-instagram-1080x1350.png',
  ig7: 'causify-post-documentos-instagram-1080x1350.png',
  ig8: 'causify-post-indicadores-instagram-1080x1350.png',
  ig9: 'causify-post-tarefas-equipe-instagram-1080x1350.png',
  ig10: 'causify-post-contatos-instagram-1080x1350.png',
};

const browser = await chromium.launch();
const page = await browser.newPage({
  viewport: { width: 1450, height: 1450 },
  deviceScaleFactor: 2,
});
await page.goto('file://' + path.join(dir, 'posts.html'));
await page.waitForLoadState('networkidle');

for (const id of posts) {
  const out = path.join(dir, 'out', names[id]);
  await page.locator('#' + id).screenshot({ path: out });
  console.log('ok ' + names[id]);
}

for (const id of igPosts) {
  const out = path.join(dir, 'out', igNames[id]);
  await page.locator('#' + id).screenshot({ path: out });
  console.log('ok ' + igNames[id]);
}

await browser.close();
