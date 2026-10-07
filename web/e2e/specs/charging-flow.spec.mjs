import { test, expect, assertActive, documentMarker, expectSameDocument } from '../helpers/fixtures.mjs';

test('reserved charger status, start, metering, pause/resume and completion reach the Driver UI', async ({ charging }) => {
  const { page, station, ocpp } = charging;
  const stationRow = page.locator('.station-list button').filter({ hasText: station.name });
  await expect(stationRow.locator('.station-dot')).toHaveClass(/status-available/);
  const mapDocument = await documentMarker(page);
  await ocpp.status('Preparing');
  await expect(stationRow.locator('.station-dot')).toHaveClass(/status-occupied/);
  await expectSameDocument(page, mapDocument);

  await page.goto(`/driver/stations/${station.id}?connector=1`);
  await expect(page.locator('.charging-live')).toContainText(/Përditësime aktive|Live updates/);
  await charging.claim();
  // Deliberately different from receipt time: preserve the physical timestamp,
  // while respecting the product's receipt-time clock for demo UI duration.
  const startedAt = new Date(Date.now() - 60_000).toISOString();
  await ocpp.start(startedAt);
  const session = await assertActive(charging, page, startedAt);
  const chargeDocument = await documentMarker(page);
  await ocpp.meter(2500, 7.4, 45);
  await expect(page.locator('.charge-energy-display > div:nth-child(2) > strong')).toHaveText(/2[.,]5\s*kWh/);
  await expect(page.getByRole('progressbar')).toHaveAttribute('aria-valuenow', '45');
  await expect(page.locator('.charge-process-metrics > div:nth-child(2) > strong')).toHaveText(/7[.,]4\s*kW/);
  await expectSameDocument(page, chargeDocument);
  await ocpp.status('SuspendedEV');
  await expect(page.locator('.charge-process.is-paused')).toBeVisible();
  await ocpp.status('Charging');
  await assertActive(charging, page, startedAt, session.id);
  expect((await charging.snapshot()).session.energyKWh).toBe(2.5);

  await ocpp.stop();
  await expect(page.locator('.charge-process.is-complete')).toBeVisible();
  await expectSameDocument(page, chargeDocument);
  const completed = (await charging.snapshot()).session;
  expect(completed.id).toBe(session.id);
  expect(completed.stoppedAt).not.toBeNull();
  expect(completed.energyKWh).toBe(2.5);
  await expect.poll(async () => {
    const history = await charging.get('/api/driver/history');
    return history.filter(item => item.stationId === station.id && Date.parse(item.startedAt) === Date.parse(startedAt)).length;
  }).toBe(1);
  const history = await charging.get('/api/driver/history');
  const ownHistory = history.find(item => item.stationId === station.id && Date.parse(item.startedAt) === Date.parse(startedAt));
  expect(ownHistory.energyKWh).toBe(completed.energyKWh);
  expect(ownHistory.totalCost).toBe(completed.cost);
  expect(await charging.sessions()).toHaveLength(0);
  await page.getByRole('link', { name: /Historiku im|My history/ }).click();
  await expect(page.locator('.history-row').filter({ hasText: station.name }).first()).toContainText(/2[.,]5\s*kWh/);
});
