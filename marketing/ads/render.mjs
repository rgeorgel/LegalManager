import { fileURLToPath } from 'node:url';
import path from 'node:path';

const dir = path.dirname(fileURLToPath(import.meta.url));
const playwrightEntry = path.join(dir, '../../tests/frontend/node_modules/@playwright/test/index.mjs');
const { chromium } = await import(playwrightEntry);

const ads = ['ad1', 'ad2', 'ad3'];
const names = {
  ad1: 'causify-ad-honorarios-1200x627.png',
  ad2: 'causify-ad-tarefas-1200x627.png',
  ad3: 'causify-ad-calculadora-1200x627.png',
};

const browser = await chromium.launch();
const page = await browser.newPage({
  viewport: { width: 1400, height: 900 },
  deviceScaleFactor: 2,
});
await page.goto('file://' + path.join(dir, 'ads.html'));
await page.waitForLoadState('networkidle');

for (const id of ads) {
  const out = path.join(dir, 'out', names[id]);
  await page.locator('#' + id).screenshot({ path: out });
  console.log('ok ' + names[id]);
}

await browser.close();
