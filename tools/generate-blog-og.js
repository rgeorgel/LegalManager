const path = require('path');
const fs = require('fs');

const playwrightPath = path.resolve(__dirname, '..', 'tests', 'frontend', 'node_modules', 'playwright');
const { chromium } = require(playwrightPath);

(async () => {
  const templatePath = path.resolve(__dirname, 'blog-og-template.html');
  const outputPath = path.resolve(__dirname, '..', 'src', 'LegalManager.API', 'wwwroot', 'images', 'blog', 'og-default.png');

  const outputDir = path.dirname(outputPath);
  if (!fs.existsSync(outputDir)) fs.mkdirSync(outputDir, { recursive: true });

  const browser = await chromium.launch();
  const context = await browser.newContext({
    viewport: { width: 1200, height: 630 },
    deviceScaleFactor: 1,
  });
  const page = await context.newPage();

  await page.goto('file:///' + templatePath.replace(/\\/g, '/'));
  await page.waitForLoadState('networkidle');

  await page.screenshot({
    path: outputPath,
    fullPage: false,
    clip: { x: 0, y: 0, width: 1200, height: 630 },
  });

  await browser.close();

  const stats = fs.statSync(outputPath);
  console.log('PNG gerado: ' + outputPath);
  console.log('Tamanho: ' + (stats.size / 1024).toFixed(1) + ' KB');
})();
