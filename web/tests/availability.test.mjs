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
