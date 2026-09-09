/**
 * Fluxo E2E: Export + Import de tenant como super admin.
 *
 * Requer:
 *   - API rodando em BASE_URL com banco populado
 *   - 2 tenants reais cadastrados (origem e destino)
 *   - Credenciais de super admin em tests/frontend/.env
 *
 * Não executa o download real (depende de browser context) — exercita
 * apenas os endpoints HTTP diretamente via APIRequestContext, validando
 * o contrato do JSON exportado e a substituição de emails.
 */
import { test, expect } from '@playwright/test';

test.skip(
  !process.env.TEST_SUPERADMIN_EMAIL || !process.env.TEST_SUPERADMIN_TENANT_ORIGEM || !process.env.TEST_SUPERADMIN_TENANT_DESTINO,
  'Configure TEST_SUPERADMIN_EMAIL, TEST_SUPERADMIN_TENANT_ORIGEM e TEST_SUPERADMIN_TENANT_DESTINO no .env para executar este teste.',
);

async function loginSuperAdmin(request: import('@playwright/test').APIRequestContext) {
  const res = await request.post('/api/auth/login', {
    data: {
      email: process.env.TEST_SUPERADMIN_EMAIL!,
      senha: process.env.TEST_SUPERADMIN_PASSWORD!,
    },
  });
  expect(res.ok(), `Login do super admin falhou: ${res.status()}`).toBeTruthy();
  const body = await res.json();
  expect(body.usuario?.perfil).toBe('SuperAdmin');
  return body.accessToken as string;
}

test.describe('Export / Import de tenant (super admin)', () => {
  test('export retorna JSON versionado com anonimização de emails', async ({ request }) => {
    const token = await loginSuperAdmin(request);
    const origemId = process.env.TEST_SUPERADMIN_TENANT_ORIGEM!;

    const res = await request.get(`/api/superadmin/tenants/${origemId}/export`, {
      headers: { Authorization: `Bearer ${token}` },
    });

    expect(res.status(), `Status inesperado: ${res.status()}`).toBe(200);

    const ct = res.headers()['content-type'] ?? '';
    expect(ct).toContain('application/json');

    const body = await res.json();
    expect(body.version).toBe(1);
    expect(body.sourceTenantId).toBe(origemId);
    expect(body.anonymization).toBeTruthy();
    expect(body.anonymization.emailsReplacedWith).toBe('@causify-replica.com');
    expect(body.tables).toBeTruthy();
    expect(typeof body.tables).toBe('object');

    // Pelo menos a tabela Usuarios deve estar presente no envelope
    expect(body.tables).toHaveProperty('Usuarios');
  });

  test('export do tenant Sistema é bloqueado', async ({ request }) => {
    const token = await loginSuperAdmin(request);
    const sistemaId = '00000000-0000-0000-0000-000000000001';

    const res = await request.get(`/api/superadmin/tenants/${sistemaId}/export`, {
      headers: { Authorization: `Bearer ${token}` },
    });

    expect(res.status()).toBe(400);
    const body = await res.json();
    expect(body.message).toMatch(/Sistema/i);
  });

  test('export de tenant inexistente retorna 404', async ({ request }) => {
    const token = await loginSuperAdmin(request);
    const inexistente = '99999999-9999-9999-9999-999999999999';

    const res = await request.get(`/api/superadmin/tenants/${inexistente}/export`, {
      headers: { Authorization: `Bearer ${token}` },
    });

    expect(res.status()).toBe(404);
  });

  test('import sem confirmação de nome retorna 400', async ({ request }) => {
    const token = await loginSuperAdmin(request);
    const destinoId = process.env.TEST_SUPERADMIN_TENANT_DESTINO!;

    const fakeExport = {
      version: 1,
      exportedAt: new Date().toISOString(),
      sourceTenantId: destinoId,
      sourceTenantNome: 'fake',
      appVersion: 'test',
      anonymization: {
        emailsReplacedWith: '@causify-replica.com',
        phonesReplacedWith: '+5511900000000',
        passwordsResetTo: 'fake',
        fieldsScanned: [],
      },
      tables: { Usuarios: [], Contatos: [] },
    };

    const res = await request.post(`/api/superadmin/tenants/${destinoId}/import`, {
      headers: { Authorization: `Bearer ${token}` },
      multipart: {
        file: {
          name: 'export.json',
          mimeType: 'application/json',
          buffer: Buffer.from(JSON.stringify(fakeExport)),
        },
        confirmation: '',
      },
    });

    expect(res.status()).toBe(400);
    const body = await res.json();
    expect(body.message).toMatch(/confirma/i);
  });

  test('acesso negado sem perfil SuperAdmin', async ({ request }) => {
    const res = await request.get('/api/superadmin/tenants/00000000-0000-0000-0000-000000000001/export');
    expect(res.status()).toBe(401);
  });
});
