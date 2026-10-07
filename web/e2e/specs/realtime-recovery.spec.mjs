import { test, expect, assertActive, documentMarker, expectSameDocument } from '../helpers/fixtures.mjs';

test('reload, a second client and SSE recovery retain one physical session and its telemetry', async ({ charging }) => {
  const { page, context, station, ocpp } = charging;
  const path = `/driver/stations/${station.id}?connector=1`;
  await page.goto(path); await charging.claim();
  const startedAt = new Date(Date.now() - 60_000).toISOString();
  await ocpp.start(startedAt); await ocpp.meter(1000, 7.4, 45);
  const original = await assertActive(charging, page, startedAt);
  await expect(page.locator('.charge-energy-display > div:nth-child(2) > strong')).toHaveText(/1\s*kWh/);
  const originalCost = (await charging.snapshot()).session.cost;
  await page.reload();
  await assertActive(charging, page, startedAt, original.id);
  expect((await charging.snapshot()).session.cost).toBe(originalCost);
  const reopened = await context.newPage();
  try {
    await reopened.goto(new URL(path, charging.settings.baseUrl).href);
    await assertActive(charging, reopened, startedAt, original.id);
    const marker = await documentMarker(page);
    await page.evaluate(() => window.__e2eSetRealtimePaused(true));
    await expect(page.locator('.charging-live')).toContainText(/Po rilidhet|Reconnecting/);
    await expect(page.locator('.charge-energy-display > div:nth-child(2) > strong')).toHaveText(/1\s*kWh/);
    await ocpp.meter(2000, 11, 47);
    await expect(reopened.locator('.charge-energy-display > div:nth-child(2) > strong')).toHaveText(/2\s*kWh/);
    await page.evaluate(() => window.__e2eSetRealtimePaused(false));
    // The existing client may wait up to its 25s silence guard before replacing
    // a closed stream. Recovery must use real SSE/REST, never a fabricated delta.
    await expect(page.locator('.charging-live')).toContainText(/Përditësime aktive|Live updates/, { timeout: 45_000 });
    await expect(page.locator('.charge-energy-display > div:nth-child(2) > strong')).toHaveText(/2\s*kWh/);
    await expect(page.getByRole('progressbar')).toHaveAttribute('aria-valuenow', '47');
    await assertActive(charging, page, startedAt, original.id);
    await expectSameDocument(page, marker);
  } finally { await reopened.close(); }
  // Fixture teardown stops only its own acknowledged transaction. No database reset.
});
