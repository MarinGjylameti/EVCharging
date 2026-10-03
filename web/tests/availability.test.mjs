import test from "node:test";
import assert from "node:assert/strict";
import { remainingChargingMinutes } from "../src/lib/availability.ts";

test("a fresh device forecast shows the remaining charging minutes", () => {
  const now = Date.parse("2026-10-01T10:00:00Z");
  const forecast = {connectorId:1,estimatedAvailableAt:"2026-10-01T10:40:00Z",
    observedAt:"2026-10-01T10:00:00Z",source:"Device"};
  assert.equal(remainingChargingMinutes(forecast,now),40);
  assert.equal(remainingChargingMinutes(forecast,now+10*60_000),30);
});

test("stale or expired charging estimates are hidden", () => {
  const now = Date.parse("2026-10-01T10:20:00Z");
  assert.equal(remainingChargingMinutes({connectorId:1,estimatedAvailableAt:"2026-10-01T10:40:00Z",
    observedAt:"2026-10-01T10:00:00Z",source:"Device"},now),null);
  assert.equal(remainingChargingMinutes({connectorId:1,estimatedAvailableAt:"2026-10-01T10:19:00Z",
    observedAt:"2026-10-01T10:00:00Z",source:"Demo"},now),null);
});

test("calculated and simulator forecasts expire with their required telemetry even when the finish is far away", () => {
  const now = Date.parse("2026-10-03T14:00:00Z");
  for (const source of ["Calculated", "Simulator"]) {
    const forecast = { connectorId: 1, source, observedAt: new Date(now).toISOString(),
      estimatedAvailableAt: new Date(now + 90 * 60_000).toISOString(), validUntil: new Date(now + 120_000).toISOString() };
    assert.equal(remainingChargingMinutes(forecast, now), 90);
    assert.equal(remainingChargingMinutes(forecast, now + 119_000), 89);
    assert.equal(remainingChargingMinutes(forecast, now + 120_000), null);
    assert.equal(remainingChargingMinutes({ ...forecast, validUntil: null }, now), null);
    assert.equal(remainingChargingMinutes({ ...forecast, validUntil: "invalid" }, now), null);
  }
});
