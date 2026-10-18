import test from "node:test";
import assert from "node:assert/strict";
import { loadTypescript } from "./helpers/load-typescript.mjs";
const { chargingGuidance } = loadTypescript(new URL("../../shared/charging-guidance.ts", import.meta.url));
const base = { station: { status: "Available" }, connector: { status: "Available" }, driverAccessMode: "Card", deviceConnected: true, inUse: false };

test("only a connected, available charger with an assigned card offers the three charging steps", () => {
  const guidance = chargingGuidance(base, "en", false);
  assert.equal(guidance.steps.length, 3);
  assert.match(guidance.body, /confirms the start/);
  assert.match(guidance.steps[1], /linked card/);
  for (const patch of [{ deviceConnected: false }, { inUse: true }, { station: { status: "Maintenance" } }, { station: { status: "OutOfService" } }, { connector: { status: "Faulted" } }, { connector: { status: "Unavailable" } }, { connector: { status: "Reserved" } }, { connector: { status: "Finishing" } }, { connector: { deviceStatus: "Faulted" } }, { station: { status: "Unknown" } }]) {
    assert.equal(chargingGuidance({ ...base, ...patch }, "en", false).steps.length, 0, JSON.stringify(patch));
  }
});

test("stale data takes priority over a previously ready card and does not instruct a start", () => {
  const guidance = chargingGuidance(base, "en", true);
  assert.equal(guidance.steps.length, 0);
  assert.match(guidance.body, /Wait for an update/);
});

test("opening a station without a linked card never promises ownership or remote start", () => {
  const guidance = chargingGuidance({ ...base, driverAccessMode: null }, "en", false);
  assert.equal(guidance.steps.length, 0);
  assert.match(guidance.body, /operator must link your card/);
  assert.match(guidance.body, /does not assign a charge/);
  const albanian = chargingGuidance(base, "sq", false);
  assert.equal(albanian.steps.length, 3);
  assert.match(albanian.title, /karikimin/);
});
