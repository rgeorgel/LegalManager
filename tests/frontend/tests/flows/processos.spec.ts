/**
 * Fluxos de processos E2E.
 * Requer API rodando com banco populado e credenciais em .env.
 */
import { test, expect } from '../../fixtures';
import type { Page } from '@playwright/test';

test.skip(
  !process.env.TEST_ADMIN_EMAIL,
  'Credenciais de teste não configuradas. Copie .env.example para .env.',
);

test('lista de processos carrega e exibe tabela', async ({ adminPage: page }) => {
  await page.goto('/pages/processos.html');
  await page.waitForLoadState('networkidle', { timeout: 15_000 }).catch(() => {});

  // Verifica que a página carregou sem ir para login
  expect(page.url()).not.toContain('login.html');

  // Deve haver uma tabela ou lista de processos (ou mensagem de vazio)
  const hasList = await page.locator('table, .processo-item, [class*="processo"], .empty-state').count();
  expect(hasList).toBeGreaterThan(0);
});

test('botão de novo processo abre modal ou navega para formulário', async ({ adminPage: page }) => {
  await page.goto('/pages/processos.html');
  await page.waitForLoadState('networkidle', { timeout: 15_000 }).catch(() => {});

  const btnNovo = page.locator('#btnNovo');
  await expect(btnNovo).toBeVisible({ timeout: 5_000 });

  await btnNovo.click();

  // .open é adicionado ao .modal-overlay (wrapper externo)
  await page.waitForTimeout(300);
  const modal = page.locator('.modal-overlay.open');
  const onForm = page.url().includes('formulario') || page.url().includes('novo');
  const modalVisible = await modal.isVisible().catch(() => false);

  expect(modalVisible || onForm, 'Modal ou formulário deveria ter aberto').toBeTruthy();
});

test('detalhe de processo sem ID mostra erro sem crash de JS', async ({ adminPage: page }) => {
  const jsErrors: string[] = [];
  page.on('pageerror', (err) => jsErrors.push(err.message));

  await page.goto('/pages/processo-detalhe.html');
  await page.waitForLoadState('networkidle', { timeout: 10_000 }).catch(() => {});

  // Sem crash de JS mesmo sem ID de processo
  expect(jsErrors).toHaveLength(0);
  expect(page.url()).not.toContain('login.html');
});

// --- Busca de processo (DataJud): autopreenchimento e partes encontradas -----------------
// A busca real (/api/processos-monitorados/search) chama o DataJud, uma API externa do
// governo -- não é reproduzível de forma determinística aqui. Mockamos essa rota (e a de
// criação, quando relevante) via page.route(); o resto (login, /contatos, /usuarios) continua
// batendo na API real, como nos demais testes deste arquivo.

const PARTES_ENCONTRADAS = [
  { nome: 'João da Silva', cpf: '11122233344', cnpj: null, oab: null, polo: 'ATIVO' },
  { nome: 'Empresa XYZ Ltda', cpf: null, cnpj: '11222333000181', oab: null, polo: 'PASSIVO' },
];

const SEARCH_RESPONSE_ENCONTRADO = {
  numeroCNJ: '1234567-89.2024.8.26.0100',
  encontrado: true,
  fonte: 'datajud',
  tribunal: 'Tribunal de Justiça de São Paulo',
  vara: '3ª Vara Cível',
  movimentosCount: 2,
  classe: 'Procedimento Comum Cível',
  assuntos: ['Indenização por Dano Moral'],
  dataAjuizamento: '2024-01-10T00:00:00',
  grau: 'G1',
  valorCausa: 15000.5,
  siglaTribunal: 'TJSP',
  partes: PARTES_ENCONTRADAS,
  movimentos: [],
};

// Fallback Escavador (Fase 2): DataJud não encontrou o processo (recente demais, ainda não
// indexado) e o Escavador encontrou movimentações. tribunal/vara/classe/partes/valorCausa
// vêm null -- limitação aceita do endpoint de movimentações do Escavador (não devolve capa
// do processo), ver docs/features/busca-processo-cadastro-manual.md, Fase 2.
const SEARCH_RESPONSE_ESCAVADOR_FALLBACK = {
  numeroCNJ: '1234567-89.2024.8.26.0100',
  encontrado: true,
  fonte: 'escavador',
  tribunal: null,
  vara: null,
  movimentosCount: 1,
  classe: null,
  assuntos: null,
  dataAjuizamento: null,
  grau: null,
  valorCausa: null,
  siglaTribunal: null,
  partes: null,
  movimentos: [
    { descricao: 'Juntada de petição.', data: '2026-08-20T00:00:00', tipoNome: 'Movimentação', codigoCNJ: null, orgaoJulgador: null },
  ],
};

async function mockSearch(page: Page, response: unknown = SEARCH_RESPONSE_ENCONTRADO) {
  await page.route('**/api/processos-monitorados/search**', (route) =>
    route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(response) }),
  );
}

async function abrirNovoProcesso(page: Page) {
  await page.goto('/pages/processos.html');
  await page.waitForLoadState('networkidle', { timeout: 15_000 }).catch(() => {});
  await page.locator('#btnNovo').click();
  await expect(page.locator('#modalProcesso.open')).toBeVisible({ timeout: 5_000 });
}

async function buscarEEsperarEncontrado(page: Page, cnj = '1234567-89.2024.8.26.0100') {
  await page.locator('#fNumeroCNJ').fill(cnj);
  await page.locator('#btnBuscarProcesso').click();
  await expect(page.locator('#processPreview')).toContainText('Processo encontrado', { timeout: 5_000 });
}

test('busca CNJ preenche valor da causa e tribunal quando vazios, e mostra partes encontradas', async ({ adminPage: page }) => {
  await mockSearch(page);
  await abrirNovoProcesso(page);
  await buscarEEsperarEncontrado(page);

  await expect(page.locator('#fVara')).toHaveValue('3ª Vara Cível');
  await expect(page.locator('#fValorCausa')).toHaveValue('15000.5');
  await expect(page.locator('#fTribunal')).toHaveValue('TJSP');

  const preview = page.locator('#partesFoundPreview');
  await expect(preview).toBeVisible();
  await expect(preview.locator('li')).toHaveCount(2);
  await expect(preview).toContainText('João da Silva');
  await expect(preview).toContainText('Empresa XYZ Ltda');
});

test('busca CNJ encontrada via fallback Escavador mostra badge de busca paga', async ({ adminPage: page }) => {
  await mockSearch(page, SEARCH_RESPONSE_ESCAVADOR_FALLBACK);
  await abrirNovoProcesso(page);
  await buscarEEsperarEncontrado(page);

  const preview = page.locator('#processPreview');
  await expect(preview).toContainText('via Escavador (busca paga)');
  await expect(preview).not.toContainText('via DataJud');
});

test('busca CNJ encontrada via DataJud mostra badge de fonte gratuita', async ({ adminPage: page }) => {
  await mockSearch(page); // SEARCH_RESPONSE_ENCONTRADO (fonte: 'datajud')
  await abrirNovoProcesso(page);
  await buscarEEsperarEncontrado(page);

  const preview = page.locator('#processPreview');
  await expect(preview).toContainText('via DataJud');
  await expect(preview).not.toContainText('busca paga');
});

test('busca CNJ não sobrescreve valor da causa e tribunal já preenchidos manualmente', async ({ adminPage: page }) => {
  await abrirNovoProcesso(page);
  await page.locator('#fValorCausa').fill('999');
  await page.locator('#fTribunal').fill('TJRJ');

  await mockSearch(page);
  await buscarEEsperarEncontrado(page);

  await expect(page.locator('#fValorCausa')).toHaveValue('999');
  await expect(page.locator('#fTribunal')).toHaveValue('TJRJ');
});

test('clicar em "Usar partes encontradas" adiciona as partes ao formulário sem duplicar em clique duplo', async ({ adminPage: page }) => {
  await mockSearch(page);
  await abrirNovoProcesso(page);
  await buscarEEsperarEncontrado(page);

  await page.locator('#btnUsarPartes').click();
  const selects = page.locator('.parte-row .parte-contato');
  await expect(selects).toHaveCount(2);
  await expect(selects.nth(0)).toHaveValue('__datajud__');
  await expect(selects.nth(1)).toHaveValue('__datajud__');
  const nomeSelecionado = await selects.nth(0).evaluate((el: HTMLSelectElement) => el.selectedOptions[0].textContent);
  expect(nomeSelecionado).toContain('João da Silva');

  // Clique duplo não deve duplicar (dedupe por nome)
  await page.locator('#btnUsarPartes').click();
  await expect(page.locator('.parte-row')).toHaveCount(2);
});

test('salvar processo com partes encontradas envia partesDataJud com os dados brutos ao backend', async ({ adminPage: page }) => {
  await mockSearch(page);
  await abrirNovoProcesso(page);
  await buscarEEsperarEncontrado(page);
  await page.locator('#btnUsarPartes').click();
  await expect(page.locator('.parte-row')).toHaveCount(2);

  let postedBody: { partes?: unknown[]; partesDataJud?: { nome: string }[] } | null = null;
  await page.route('**/api/processos', async (route) => {
    if (route.request().method() !== 'POST') return route.continue();
    postedBody = route.request().postDataJSON();
    await route.fulfill({
      status: 201,
      contentType: 'application/json',
      body: JSON.stringify({ id: 'test-id', numeroCNJ: '1234567-89.2024.8.26.0100', partes: [] }),
    });
  });

  await page.locator('#btnSalvar').click();
  await expect(page.locator('#modalProcesso.open')).toHaveCount(0, { timeout: 5_000 });

  expect(postedBody).not.toBeNull();
  expect(postedBody!.partesDataJud).toHaveLength(2);
  expect(postedBody!.partesDataJud![0].nome).toBe('João da Silva');
  expect(postedBody!.partesDataJud![1].nome).toBe('Empresa XYZ Ltda');
  // Partes "brutas" do DataJud não têm ContatoId ainda, então não entram em `partes` --
  // são resolvidas/criadas em Contato só no backend, no Salvar (nunca na busca/preview).
  expect(postedBody!.partes ?? []).toHaveLength(0);
});

test('buscar processo e cancelar não dispara nenhuma chamada de criação (nem Contato, nem Processo)', async ({ adminPage: page }) => {
  let creationCalls = 0;
  await page.route('**/api/processos', async (route) => {
    if (route.request().method() === 'POST') creationCalls++;
    await route.continue();
  });
  await page.route('**/api/contatos', async (route) => {
    if (route.request().method() === 'POST') creationCalls++;
    await route.continue();
  });

  await mockSearch(page);
  await abrirNovoProcesso(page);
  await buscarEEsperarEncontrado(page);
  await page.locator('#btnUsarPartes').click();
  await expect(page.locator('.parte-row')).toHaveCount(2);

  await page.locator('#btnCancelar').click();
  await expect(page.locator('#modalProcesso.open')).toHaveCount(0);

  expect(creationCalls).toBe(0);
});
