#!/usr/bin/env node
// Verifica a sintaxe de todo o JavaScript do frontend (wwwroot) com `node --check`:
//   - arquivos .js
//   - scripts inline dos .html (<script> e <script type="module">), que o
//     check-js.ps1 não cobre — boa parte da lógica das páginas vive inline.
// Multiplataforma (usado no CI e localmente sem PowerShell).
// Uso: node scripts/check-js.mjs
import { execFileSync } from 'node:child_process';
import { mkdtempSync, readdirSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join, relative } from 'node:path';
import { fileURLToPath } from 'node:url';

const raiz = fileURLToPath(new URL('..', import.meta.url));
const wwwroot = join(raiz, 'src', 'LegalManager.API', 'wwwroot');
const tmp = mkdtempSync(join(tmpdir(), 'check-js-'));

function* arquivos(dir) {
  for (const e of readdirSync(dir, { withFileTypes: true })) {
    const p = join(dir, e.name);
    if (e.isDirectory()) { if (e.name !== 'node_modules') yield* arquivos(p); }
    else yield p;
  }
}

function checar(caminho, rotulo) {
  try {
    execFileSync(process.execPath, ['--check', caminho], { stdio: 'pipe' });
    return null;
  } catch (err) {
    return `✗ ${rotulo}\n${String(err.stderr).trim()}`;
  }
}

const erros = [];
let total = 0;
let n = 0;
try {
  for (const arquivo of arquivos(wwwroot)) {
    const rel = relative(raiz, arquivo);
    if (arquivo.endsWith('.js')) {
      total++;
      const e = checar(arquivo, rel);
      if (e) erros.push(e);
    } else if (arquivo.endsWith('.html')) {
      const html = readFileSync(arquivo, 'utf8');
      // Só scripts inline executáveis (sem src; tipo vazio, JS ou module).
      const re = /<script\b([^>]*)>([\s\S]*?)<\/script>/gi;
      let m;
      while ((m = re.exec(html))) {
        const attrs = m[1];
        if (/\bsrc\s*=/.test(attrs) || !m[2].trim()) continue;
        const tipo = (attrs.match(/\btype\s*=\s*["']?([^"'\s>]+)/i)?.[1] ?? '').toLowerCase();
        if (tipo && !['module', 'text/javascript', 'application/javascript'].includes(tipo)) continue;
        total++;
        const linha = html.slice(0, m.index).split('\n').length;
        const tmpFile = join(tmp, `inline-${n++}.${tipo === 'module' ? 'mjs' : 'js'}`);
        writeFileSync(tmpFile, m[2]);
        const e = checar(tmpFile, `${rel}:${linha} (script inline${tipo === 'module' ? ' module' : ''})`);
        if (e) erros.push(e);
      }
    }
  }
} finally {
  rmSync(tmp, { recursive: true, force: true });
}

if (erros.length) {
  console.error(erros.join('\n\n'));
  console.error(`\n${erros.length} de ${total} script(s) com erro de sintaxe.`);
  process.exit(1);
}
console.log(`Todos os ${total} script(s) JS são válidos (arquivos .js + inline em .html).`);
