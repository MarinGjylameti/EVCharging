import { test as base, expect } from '@playwright/test';
import { e2eEnvironment } from '../environment.mjs';
import { OcppTestClient } from './ocpp-client.mjs';
import { installRealtimeControl } from './realtime-control.mjs';

async function json(response, operation) {
  if (!response.ok()) throw new Error(`${operation} failed (HTTP ${response.status()}); inspect the approved test environment.`);
  try { return await response.json(); }
  catch { throw new Error(`${operation} did not return valid JSON.`); }
}

async function transport(operation, send) {
  // Playwright transport errors can include cookies/Authorization in call logs.
  // Do not forward those errors or their causes to the test reporter.
  try { return await send(); }
  catch { throw new Error(`${operation} transport failed; inspect the approved test environment.`); }
}

export const test = base.extend({
  settings: async ({}, use) => { await use(e2eEnvironment(process.env)); },
  driver: async ({ page, context, settings, playwright }, use) => {
    await context.route('**/*', async route => {
      const url = new URL(route.request().url());
      if (settings.approvedOrigins.includes(url.origin)) await route.continue();
      else await route.abort('blockedbyclient'); // Also blocks third-party map tiles.
    });
    await context.addInitScript(installRealtimeControl);
    await page.goto('/');
    await page.getByRole('button', { name: 'EN', exact: true }).click();
    await page.getByLabel('Email address', { exact: true }).fill(settings.email);
    try { await page.getByLabel('Password', { exact: true }).fill(settings.password); }
    catch { throw new Error('The dedicated Driver password field could not be filled.'); }
    await page.getByRole('button', { name: 'Sign in to platform' }).click();
    await expect(page).toHaveURL(new URL('/driver', settings.baseUrl).href);
    await expect(page.getByRole('application', { name: /Harta e pikave|Charging map/ })).toBeVisible();
    // The browser has authenticated normally. Use its existing bearer cookie in
    // memory for authorized backend assertions; never save storageState or tokens.
    const cookie = (await context.cookies(settings.baseUrl)).find(value => value.name === 'evcharging_session');
    if (!cookie) throw new Error('Driver login did not establish an authenticated session.');
    const api = await transport('Authorized API context', () => playwright.request.newContext({
      baseURL: settings.apiUrl, extraHTTPHeaders: { Authorization: `Bearer ${cookie.value}` }, timeout: 15_000
    }));
    const get = async path => json(await transport('Driver snapshot', () => context.request.get(new URL(path, settings.baseUrl).href, { maxRedirects: 0, timeout: 15_000 })), 'Driver snapshot');
    const sessions = async () => {
      const state = await json(await transport('Driver charging state', () => api.get('/api/v1/driver/charging-sessions', { maxRedirects: 0 })), 'Driver charging state');
      return state.sessions;
    };
    try { await use({ page, context, settings, get, sessions }); }
    finally { await api.dispose(); }
  },
  charging: async ({ driver, playwright }, use, testInfo) => {
    const settings = e2eEnvironment(process.env, { charging: true });
    const health = await playwright.request.newContext({ baseURL: settings.apiUrl, timeout: 15_000 });
    try {
      const ready = await health.get('/health/ready', { maxRedirects: 0 });
      if (!ready.ok()) throw new Error('The explicitly configured E2E backend is not ready.');
    } finally { await health.dispose(); }
    const live = await driver.get('/api/driver/live');
    const station = live.stations.find(value => value.code === settings.identity);
    if (!station?.isDemo || station.isArchived) throw new Error('Provision the dedicated, non-archived SIM-E2E-* demo station first.');
    const snapshotPath = `/api/driver/stations/${station.id}/charging?connector=1`;
    const snapshot = () => driver.get(snapshotPath);
    const before = await snapshot();
    if (before.inUse || before.deviceConnected || before.connectionRequest
      || before.session?.stoppedAt === null || before.driverAccessMode || !before.canConnect
      || (await driver.sessions()).length) {
      throw new Error('E2E requires an idle, disconnected, manually claimable reserved charger and an unclaimed test Driver. Existing state will not be reset.');
    }
    const ocpp = new OcppTestClient(settings);
    let ownClaimId = null;
    try {
      await ocpp.connect(); await ocpp.authorize(); await ocpp.status('Available');
      await use({ ...driver, station, ocpp, snapshot, historyBefore: live.history,
        async claim() {
          await driver.page.getByRole('button', { name: /^(Lidhu me pikën|Connect to charger|Përgatit karikim të ri|Prepare a new charge)(?:\s*↗)?$/ }).click();
          await expect.poll(async () => (await snapshot()).connectionRequest?.state).toBe('Waiting');
          ownClaimId = (await snapshot()).connectionRequest.id;
        }
      });
    } finally {
      // Only stop the transaction acknowledged to THIS fixture. Never stop an
      // unknown existing charge, delete history/accounts or reset the database.
      try {
        await ocpp.stop();
        if (ownClaimId) {
          const current = await snapshot();
          if (current.connectionRequest?.id === ownClaimId && current.connectionRequest.state === 'Waiting'
            && (!current.session || current.session.stoppedAt !== null)) {
            const cancelled = await transport('Owned waiting request cleanup', () => driver.context.request.delete(new URL(snapshotPath, settings.baseUrl).href, {
              headers: { Origin: settings.baseUrl }, maxRedirects: 0, timeout: 15_000
            }));
            if (!cancelled.ok()) throw new Error('Owned E2E waiting request cleanup was not confirmed.');
          }
        }
      } catch {
        testInfo.annotations.push({ type: 'cleanup', description: 'Inspect the reserved E2E charger/claim before another run. Cleanup was not confirmed; no broader cleanup was attempted.' });
        // A cleanup failure must fail the run instead of leaving a misleading pass.
        throw new Error('E2E cleanup was not confirmed. Inspect only the dedicated test resources before another run.');
      } finally { await ocpp.close(); }
    }
  }
});
export { expect };

export async function assertActive(charging, page, startedAt, id) {
  await expect.poll(async () => (await charging.snapshot()).session?.status).toBe('Charging');
  const session = (await charging.snapshot()).session;
  expect(Date.parse(session.startedAt)).toBe(Date.parse(startedAt));
  if (id) expect(session.id).toBe(id);
  await expect(page.locator('.charge-process.is-charging')).toBeVisible();
  const displayStart = session.isDemo ? session.startReceivedAt : session.startedAt;
  await expect.poll(async () => Date.parse(await page.locator('.charge-process-metrics time').getAttribute('datetime'))).toBe(Date.parse(displayStart));
  const sessions = await charging.sessions();
  expect(sessions).toHaveLength(1);
  expect(sessions[0].id).toBe(session.id);
  return session;
}

export async function documentMarker(page) {
  return page.evaluate(() => { window.__e2eDocumentMarker = crypto.randomUUID(); return window.__e2eDocumentMarker; });
}

export async function expectSameDocument(page, marker) {
  expect(await page.evaluate(() => window.__e2eDocumentMarker)).toBe(marker);
}
