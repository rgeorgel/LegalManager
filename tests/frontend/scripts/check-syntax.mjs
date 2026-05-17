/**
 * Verifica sintaxe de todos os arquivos .js em wwwroot via `node --check`.
 * Executar: node tests/frontend/scripts/check-syntax.mjs
 */
import { execSync } from 'child_process';
import { readdirSync, statSync } from 'fs';
import { join, extname, dirname } from 'path';
import { fileURLToPath } from 'url';

const __dirname = dirname(fileURLToPath(import.meta.url));
const wwwroot = join(__dirname, '..', '..', '..', 'src', 'LegalManager.API', 'wwwroot');

function* findJs(dir) {
  for (const entry of readdirSync(dir, { withFileTypes: true })) {
    const p = join(dir, entry.name);
    if (entry.isDirectory()) {
      yield* findJs(p);
    } else if (extname(entry.name) === '.js') {
      yield p;
    }
  }
}

let errors = 0;
const files = [...findJs(wwwroot)];

for (const file of files) {
  try {
    execSync(`node --check "${file}"`, { stdio: 'pipe' });
  } catch (err) {
    const msg = err.stderr?.toString().trim() ?? err.message;
    console.error(`\nSintaxe inválida: ${file}`);
    console.error(msg);
    errors++;
  }
}

if (errors > 0) {
  console.error(`\n${errors} arquivo(s) com erro de sintaxe JS.`);
  process.exit(1);
} else {
  console.log(`✓ ${files.length} arquivo(s) JS verificado(s) — todos válidos.`);
}
