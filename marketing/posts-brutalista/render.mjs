// Gera os vídeos MP4 (e uma capa PNG de cada) a partir de posts.html.
// Cada quadro é capturado com o tempo da animação fixado por window.seek(t), então o vídeo sai
// liso e igual em qualquer máquina. Requer ffmpeg no PATH e o Playwright de tests/frontend.
//
//   node marketing/posts-brutalista/render.mjs              # todos os posts
//   node marketing/posts-brutalista/render.mjs a1 c2        # só alguns
//   node marketing/posts-brutalista/render.mjs --previa 2,7 # só PNGs desses instantes, em out/previa
import { fileURLToPath } from 'node:url';
import { execFileSync } from 'node:child_process';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';

const dir = path.dirname(fileURLToPath(import.meta.url));
const playwrightEntry = path.join(dir, '../../tests/frontend/node_modules/@playwright/test/index.mjs');
const { chromium } = await import(playwrightEntry);

const FPS = 30;
const DURACAO = 7; // segundos (o Instagram, o LinkedIn e o Facebook aceitam vídeo de feed a partir de 3 s)
const CAPA_T = 6.4; // fim da animação, com os dois-pontos do relógio aceso

const nomes = {
  a1: 'causify-bru-amarelo-prazo-perdido',
  a2: 'causify-bru-amarelo-dia-util',
  b1: 'causify-bru-cobalto-publicou',
  b2: 'causify-bru-cobalto-antes-do-cafe',
  c1: 'causify-bru-concreto-gaveta',
  c2: 'causify-bru-concreto-tarefa-tem-dono',
  d1: 'causify-bru-madrugada-23h59',
  d2: 'causify-bru-madrugada-seu-dia',
};
// ig: feed do Instagram em 4:5; sq: quadrado para LinkedIn e Facebook (os dois exibem 1:1 sem corte).
const formatos = {
  ig: { h: 1350, sufixo: 'instagram-1080x1350' },
  sq: { h: 1080, sufixo: '1080x1080' },
};

const args = process.argv.slice(2);
const iPrevia = args.indexOf('--previa');
const previa = iPrevia >= 0 ? args[iPrevia + 1].split(',').map(Number) : null;
const ids = args.filter((a, i) => !a.startsWith('--') && i !== iPrevia + 1);
const fila = [];
for (const id of ids.length ? ids : Object.keys(nomes)) for (const f of Object.keys(formatos)) fila.push([id, f]);

const out = path.join(dir, 'out');
fs.mkdirSync(previa ? path.join(out, 'previa') : out, { recursive: true });

const browser = await chromium.launch();

async function renderiza(id, f) {
  const { h, sufixo } = formatos[f];
  const base = `${nomes[id]}-${sufixo}`;
  const page = await browser.newPage({ viewport: { width: 1080, height: h }, deviceScaleFactor: 1 });
  await page.goto(`file://${path.join(dir, 'posts.html')}?p=${id}&f=${f}`);
  await page.waitForFunction(() => window.pronto === true);
  const post = page.locator('.post');

  if (previa) {
    for (const t of previa) {
      await page.evaluate(t => window.seek(t), t);
      await post.screenshot({ path: path.join(out, 'previa', `${base}-t${t}.png`) });
    }
    await page.close();
    console.log('prévia ' + base);
    return;
  }

  const quadros = fs.mkdtempSync(path.join(os.tmpdir(), `causify-${id}-${f}-`));
  const total = Math.round(DURACAO * FPS);
  for (let i = 0; i < total; i++) {
    await page.evaluate(t => window.seek(t), i / FPS);
    await post.screenshot({ path: path.join(quadros, `q${String(i).padStart(4, '0')}.png`) });
  }
  await page.evaluate(t => window.seek(t), CAPA_T);
  await post.screenshot({ path: path.join(out, `${base}-capa.png`) });
  await page.close();

  execFileSync('ffmpeg', [
    '-y', '-loglevel', 'error', '-framerate', String(FPS), '-i', path.join(quadros, 'q%04d.png'),
    '-c:v', 'libx264', '-preset', 'slow', '-crf', '17', '-pix_fmt', 'yuv420p',
    '-profile:v', 'high', '-movflags', '+faststart', path.join(out, `${base}.mp4`),
  ]);
  fs.rmSync(quadros, { recursive: true, force: true });
  console.log('ok ' + base + '.mp4');
}

// Quatro páginas em paralelo: cada post é independente.
const trabalhadores = Array.from({ length: 4 }, async () => {
  while (fila.length) await renderiza(...fila.shift());
});
await Promise.all(trabalhadores);
await browser.close();
