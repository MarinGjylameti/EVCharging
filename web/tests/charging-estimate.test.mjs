import test from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import ts from "typescript";

const exports = {};
new Function("exports", ts.transpileModule(readFileSync(new URL("../src/features/driver/charging-estimate.ts", import.meta.url), "utf8"), {
  compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 },
}).outputText)(exports);
const { chargingEstimate, formatChargingEstimate } = exports;
const now = Date.parse("2026-10-03T14:00:00Z");
const session = { status: "Charging", stoppedAt: null, batteryCapacityKWh: 60, stateOfCharge: 45,
  powerKW: 22, powerScope: "Total", powerValidUntil: "2026-10-03T14:02:00Z", stateOfChargeValidUntil: "2026-10-03T14:05:00Z" };
const estimate = (patch = {}, time = now, stale = false) => chargingEstimate({ ...session, ...patch }, time, stale);

test("remaining time changes with reported battery percentage, total power and capacity", () => {
  assert.deepEqual(estimate(), { minutes: 90, reason: null });
  assert.equal(formatChargingEstimate(90), "1 h 30 min");
  assert.equal(estimate({ stateOfCharge: 56 }).minutes, 72);
  assert.equal(estimate({ powerKW: 11 }).minutes, 180);
  assert.equal(estimate({ batteryCapacityKWh: 80 }).minutes, 120);
  assert.equal(estimate({ stateOfCharge: 99.99 }).minutes, 1);
  assert.equal(estimate({ stateOfCharge: 100, powerKW: 0 }).minutes, 0);
  assert.equal(formatChargingEstimate(0), "0 min");
  assert.equal(formatChargingEstimate(120), "2 h");
});
test("energy and elapsed time do not invent battery progress or a countdown", () => {
  assert.equal(estimate({ energyKWh: 0 }).minutes, 90);
  assert.equal(estimate({ energyKWh: 20, startedAt: "2026-10-01T12:00:00Z" }, now + 60_000).minutes, 90);
});
test("partial phase power and unknown scope cannot be treated as total, including in demo", () => {
  for (const powerScope of [null, undefined, "L1", "L2", "Partial"]) {
    assert.equal(estimate({ powerScope, isDemo: true }).reason, "partialPower");
  }
});
test("pause, fault, offline, completion and stream recovery hide the estimate", () => {
  for (const status of ["PausedByVehicle", "PausedByCharger", "Finishing", "Faulted", "ConnectionLost", "Available", "Unavailable"]) {
    assert.equal(estimate({ status }).reason, "notCharging");
  }
  assert.equal(estimate({ faultCode: "OverCurrentFailure" }).reason, "notCharging");
  assert.equal(estimate({ stoppedAt: "2026-10-03T14:00:00Z" }).reason, "notCharging");
  assert.equal(estimate({}, now, true).reason, "stale");
  assert.equal(estimate({}, 0).reason, "stale");
  assert.equal(estimate({}, NaN).reason, "stale");
});
test("each sensor must be fresh; unrelated meter traffic cannot keep old SoC alive", () => {
  assert.equal(estimate({}, now + 120_000).reason, "missingPower");
  assert.equal(estimate({ powerValidUntil: "2026-10-03T14:07:00Z", lastMeasurementReceivedAt: "2026-10-03T14:05:00Z" }, now + 300_000).reason, "staleBattery");
  assert.equal(estimate({ powerValidUntil: "invalid" }).reason, "missingPower");
  assert.equal(estimate({ stateOfChargeValidUntil: undefined }).reason, "staleBattery");
});
test("missing and invalid data never produces NaN, infinity or negative time", () => {
  for (const batteryCapacityKWh of [null, undefined, 0, -1, 1001, NaN, Infinity]) assert.equal(estimate({ batteryCapacityKWh }).reason, "missingCapacity");
  for (const stateOfCharge of [null, undefined, -1, 101, NaN, Infinity]) assert.equal(estimate({ stateOfCharge }).reason, "missingBattery");
  for (const powerKW of [null, undefined, 0, -1, NaN, Infinity]) assert.equal(estimate({ powerKW }).reason, "missingPower");
});
