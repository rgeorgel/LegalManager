import { test as base, expect, APIRequestContext, BrowserContext, Page } from '@playwright/test';

interface AdminSession {
  accessToken: string;
  refreshToken: string;
  usuario: Record<string, unknown>;
}

interface ClientSession {
  accessToken: string;
  perfil: Record<string, unknown>;
}

type Fixtures = {
  adminPage: Page;
  clientPage: Page;
  superAdminPage: Page;
};

async function fetchAdminSession(request: APIRequestContext): Promise<AdminSession | null> {
  const res = await request.post('/api/auth/login', {
    data: {
      email: process.env.TEST_ADMIN_EMAIL ?? '',
      senha: process.env.TEST_ADMIN_PASSWORD ?? '',
    },
  });
  if (!res.ok()) return null;
  return res.json();
}

async function fetchClientSession(request: APIRequestContext): Promise<ClientSession | null> {
  const res = await request.post('/api/portal/login', {
    data: {
      email: process.env.TEST_CLIENT_EMAIL ?? '',
      senha: process.env.TEST_CLIENT_PASSWORD ?? '',
    },
  });
  if (!res.ok()) return null;
  return res.json();
}

async function fetchSuperAdminSession(request: APIRequestContext): Promise<AdminSession | null> {
  const res = await request.post('/api/auth/login', {
    data: {
      email: process.env.TEST_SUPERADMIN_EMAIL ?? '',
      senha: process.env.TEST_SUPERADMIN_PASSWORD ?? '',
    },
  });
  if (!res.ok()) return null;
  const body = await res.json();
  if (body?.usuario?.perfil !== 'SuperAdmin') return null;
  return body;
}

async function injectAdmin(ctx: BrowserContext, s: AdminSession) {
  // addInitScript roda em CADA navegação. Para não sobrescrever mudanças
  // feitas pelo próprio usuário (ex.: tema salvo em /pages/tema.html),
  // só populamos sessionStorage na primeira carga de cada contexto.
  await ctx.addInitScript(
    ({ at, rt, user }) => {
      if (sessionStorage.getItem('access_token')) return;
      sessionStorage.setItem('access_token', at);
      sessionStorage.setItem('refresh_token', rt);
      sessionStorage.setItem('user', JSON.stringify(user));
      if (user.tema) sessionStorage.setItem('tenant_theme', JSON.stringify(user.tema));
    },
    { at: s.accessToken, rt: s.refreshToken, user: s.usuario },
  );
}

async function injectClient(ctx: BrowserContext, s: ClientSession) {
  await ctx.addInitScript(
    ({ at, user }) => {
      sessionStorage.setItem('cliente_token', at);
      sessionStorage.setItem('cliente_user', JSON.stringify(user));
    },
    { at: s.accessToken, user: s.perfil },
  );
}

async function injectSuperAdmin(ctx: BrowserContext, s: AdminSession) {
  await ctx.addInitScript(
    ({ at, rt, user }) => {
      if (sessionStorage.getItem('sa_access_token')) return;
      sessionStorage.setItem('sa_access_token', at);
      sessionStorage.setItem('sa_refresh_token', rt);
      sessionStorage.setItem('sa_user', JSON.stringify(user));
      if (user.tema) sessionStorage.setItem('tenant_theme', JSON.stringify(user.tema));
    },
    { at: s.accessToken, rt: s.refreshToken, user: s.usuario },
  );
}

export const test = base.extend<Fixtures>({
  adminPage: async ({ browser, request, baseURL }, use) => {
    const ctx = await browser.newContext({ baseURL });
    const session = await fetchAdminSession(request);
    if (session) await injectAdmin(ctx, session);
    const page = await ctx.newPage();
    await use(page);
    await ctx.close();
  },

  clientPage: async ({ browser, request, baseURL }, use) => {
    const ctx = await browser.newContext({ baseURL });
    const session = await fetchClientSession(request);
    if (session) await injectClient(ctx, session);
    const page = await ctx.newPage();
    await use(page);
    await ctx.close();
  },

  superAdminPage: async ({ browser, request, baseURL }, use) => {
    const ctx = await browser.newContext({ baseURL });
    const session = await fetchSuperAdminSession(request);
    if (session) await injectSuperAdmin(ctx, session);
    const page = await ctx.newPage();
    await use(page);
    await ctx.close();
  },
});

export { expect };
