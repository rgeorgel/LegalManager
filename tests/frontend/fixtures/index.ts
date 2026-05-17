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

async function injectAdmin(ctx: BrowserContext, s: AdminSession) {
  await ctx.addInitScript(
    ({ at, rt, user }) => {
      sessionStorage.setItem('access_token', at);
      sessionStorage.setItem('refresh_token', rt);
      sessionStorage.setItem('user', JSON.stringify(user));
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
});

export { expect };
