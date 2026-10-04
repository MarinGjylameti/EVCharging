import test from "node:test";
import assert from "node:assert/strict";
import { loadTypescript } from "./helpers/load-typescript.mjs";

const compile = path => loadTypescript(new URL(path, import.meta.url));
const { SimulatorTestJournal, createChargingTestReport, reportSimulatorConfig } = compile("../src/features/driver/charging-test-report.ts");
const { prepareSimulatorAccount, SimulatorAccountError } = compile("../src/features/driver/simulator-account.ts");
const station = { id: "station-one", code: "SIM-001", status: "Charging", version: 12 };
const entry = (action, detail, direction = "OUT") => ({ at: "2026-10-03T14:00:00Z", direction, action, detail: JSON.stringify(detail) });

test("traffic remains bounded while lifecycle and rejections survive frequent meter updates and reconnects", () => {
  const journal = new SimulatorTestJournal(4, 3);
  journal.record(entry("StartTransaction", [3, "start", { transactionId: 27 }], "IN"));
  journal.record(entry("MeterValues", [4, "bad", "PropertyConstraintViolation", "Invalid unit", {}], "IN"));
  for (let i = 0; i < 8; i++) journal.record(entry("MeterValues", [2, `${i}`, { value: i }]));
  journal.record(entry("Connection", "reconnected", "SYS"));
  const snapshot = journal.snapshot();
  assert.equal(snapshot.totalEntries, 11);
  assert.equal(snapshot.logs.length, 4);
  assert.equal(snapshot.omittedTrafficEntries, 7);
  assert.equal(snapshot.lifecycle.length, 3);
  assert.match(snapshot.lifecycle[0].detail, /transactionId.*27/);
  assert.match(snapshot.lifecycle[1].detail, /PropertyConstraintViolation/);
  snapshot.logs[0].detail = "edited";
  snapshot.lifecycle[0].detail = "edited";
  assert.doesNotMatch(JSON.stringify(journal.snapshot()), /edited/);
  const other = new SimulatorTestJournal();
  assert.equal(other.snapshot().totalEntries, 0);
  journal.record(entry("StopTransaction", [2, "stop", { transactionId: 27, meterStop: 1000 }]));
  assert.equal(journal.snapshot().omittedLifecycleEntries, 1);
});

test("download reports preserve transaction and units while excluding connection credentials", () => {
  const journal = new SimulatorTestJournal();
  journal.record(entry("ChangeConfiguration", [2, "secret", { key: "AuthorizationKey", value: "station-password" }]));
  journal.record(entry("MeterValues", [2, "meter", { transactionId: 27, sampledValue: [{ value: "22", unit: "kW" }] }]));
  const config = { stationCode: station.code, url: "ws://user:socket-password@localhost", password: "extra-secret",
    capacityKWh: 77, initialSoc: 20, targetSoc: 80, powerKW: 22, meterStartWh: 0, intervalSeconds: 5, speed: 1 };
  const report = createChargingTestReport({ source: "automatic-simulator", station,
    view: { station, inUse: false, session: { id: "own", cost: .35 }, diagnostics: { password: "diagnostic-secret" }, accessToken: "session-token" },
    simulator: { config, state: { transactionId: 27 }, traffic: journal.snapshot(), lastError: null, associateWithAccount: true, existingTransaction: null },
    exportedAt: "2026-10-03T14:01:00Z" });
  const json = JSON.stringify(report);
  assert.doesNotMatch(json, /socket-password|station-password|extra-secret|diagnostic-secret|session-token/);
  assert.match(json, /\[redacted\]/);
  assert.equal(report.simulator.state.transactionId, 27);
  assert.match(json, /kW/);
  assert.equal(report.serverSnapshot.session.cost, .35);
  assert.equal(reportSimulatorConfig(config).url, undefined);
  assert.equal(report.exportedAt, "2026-10-03T14:01:00Z");
});

test("reports refuse a snapshot or simulator belonging to another point and still export after API failure", () => {
  assert.throws(() => createChargingTestReport({ source: "charger-view", station, view: { station: { id: "other" } } }), /different charge point/);
  assert.throws(() => createChargingTestReport({ source: "automatic-simulator", station, simulator: { config: { stationCode: "SIM-002" } } }), /different charge point/);
  const report = createChargingTestReport({ source: "automatic-simulator", station, snapshotError: "HTTP 503" });
  assert.equal(report.serverSnapshot, null);
  assert.equal(report.serverSnapshotError, "HTTP 503");
});

test("unchecked ownership cancels only the caller's waiting association before simulator start", async () => {
  const calls = [];
  const view = { station, inUse: true, session: null, connectionRequest: null };
  const request = async (url, options) => { calls.push({ url, ...options }); return { ok: true, json: async () => view }; };
  assert.equal(await prepareSimulatorAccount(station.id, false, request), view);
  assert.equal(calls[0].method, "DELETE");
  assert.equal(calls[0].url, "/api/driver/stations/station-one/charging");
  await prepareSimulatorAccount(station.id, true, request);
  assert.equal(calls[1].method, "POST");
  assert.equal(calls[1].body, undefined);
});

test("association failures and mismatched responses prevent starting under the wrong account", async () => {
  for (const status of [401, 409]) {
    await assert.rejects(() => prepareSimulatorAccount(station.id, true, async () => ({ ok: false, status, json: async () => ({ code: "charger_in_use" }) })),
      error => error instanceof SimulatorAccountError && error.status === status && error.code === "charger_in_use");
  }
  await assert.rejects(() => prepareSimulatorAccount(station.id, false, async () => ({ ok: true, json: async () => ({ station: { id: "other" } }) })), /Invalid charging association response/);
});

test("another driver's public snapshot cannot export stale private summary fields", () => {
  const report = createChargingTestReport({ source: "charger-view", station,
    view: { station, inUse: true, session: { cost: 123 }, diagnostics: { externalSessionId: "private" } } });
  assert.equal(report.serverSnapshot.session, null);
  assert.equal(report.serverSnapshot.diagnostics, null);
});
