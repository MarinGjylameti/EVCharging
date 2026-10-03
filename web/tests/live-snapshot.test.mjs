import test from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import ts from "typescript";

const compiled = ts.transpileModule(readFileSync(new URL("../src/features/driver/use-live-snapshot.ts", import.meta.url), "utf8"), {
  compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 },
}).outputText;

const policyExports = {};
new Function("exports", ts.transpileModule(readFileSync(new URL("../src/features/driver/live-update-policy.ts", import.meta.url), "utf8"), {
  compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 },
}).outputText)(policyExports);

test("private vehicle updates change the active session without refetching or overwriting newer meter data", () => {
  const session = { id: "session-1", version: 10, status: "Charging", stoppedAt: null, energyKWh: 1,
    batteryCapacityKWh: 60, readings: [{ measurand: "SoC", value: "45" }] };
  const current = { station: { id: "station-1", version: 1 }, session, connectionRequest: null };
  const event = { eventType: "SessionVehicleChanged", entityVersion: 11, session: { ...session, version: 11, batteryCapacityKWh: 80 } };
  const update = policyExports.chargingLivePolicy.event(current, event);
  assert.equal(update.refreshUrl, undefined);
  assert.equal(update.snapshot.session.batteryCapacityKWh, 80);
  assert.equal(update.snapshot.session.energyKWh, 1);
  assert.equal(policyExports.chargingLivePolicy.event(update.snapshot, { ...event, session }), null);
  assert.equal(policyExports.driverLivePolicy.event({ stations: [], history: [] }, event), null);
});

test("public start and stop events refresh authorized ownership without injecting another driver's private data", () => {
  const policy = policyExports.chargingLivePolicy;
  const completed = { id: "my-old-charge", version: 4, stoppedAt: "2026-10-03T14:00:00Z", cost: 1.2 };
  const current = { station: { id: "station-1", status: "Available", version: 1 }, session: completed, inUse: false };
  const start = policy.event(current, { eventType: "StationChanged", station: { ...current.station, status: "Charging", version: 2 } });
  assert.equal(start.refreshUrl, "/api/driver/stations/station-1/charging");
  const refreshed = policy.merge(start.snapshot, { ...start.snapshot, inUse: true, session: null });
  assert.equal(refreshed.session, null);
  const stop = policy.event(refreshed, { eventType: "StationChanged", station: { ...refreshed.station, status: "Available", version: 3 } });
  assert.equal(stop.refreshUrl, start.refreshUrl);
  const free = policy.merge(stop.snapshot, { ...stop.snapshot, inUse: false, session: null });
  assert.equal(free.inUse, false);
  assert.equal(free.session, null);
  const physicalStart = policy.event(current, { eventType: "ConnectorChanged", connector: { status: "Connecting", version: 2 } });
  assert.equal(physicalStart.refreshUrl, start.refreshUrl);
  assert.equal(policy.event(free, { eventType: "StationChanged", station: { ...current.station, version: 1 } }), null);
});

function mount(t, stationId = "station-1", initial = { value: "initial" }, policy) {
  const names = ["document", "window", "EventSource", "fetch", "setTimeout", "clearTimeout", "setInterval", "clearInterval"];
  const originals = Object.fromEntries(names.map(name => [name, globalThis[name]]));
  const realNow = Date.now;
  const timers = new Map(), intervals = new Map(), requests = [], state = [], streams = [];
  const listeners = {}, windowListeners = {};
  let cleanup, nextTimer = 1, now = 100_000;
  Date.now = () => now;
  const flush = async () => { for (let i = 0; i < 12; i++) await Promise.resolve(); };
  const advance = async ms => {
    now += ms;
    for (const [id, timer] of [...timers]) {
      if (timer.at <= now && timers.delete(id)) timer.callback();
    }
    await flush();
  };
  globalThis.document = { hidden: false, addEventListener: (name, fn) => listeners[name] = fn, removeEventListener: name => delete listeners[name] };
  globalThis.window = {
    location: { assign: url => state.redirect = url },
    addEventListener: (name, fn) => windowListeners[name] = fn,
    removeEventListener: name => delete windowListeners[name],
  };
  globalThis.EventSource = class {
    handlers = {}; closed = false;
    constructor() { streams.push(this); }
    addEventListener(name, fn) { this.handlers[name] = fn; }
    close() { this.closed = true; }
  };
  globalThis.fetch = (url, options) => new Promise((resolve, reject) => {
    requests.push({ url, options, resolve, reject });
    options.signal.addEventListener("abort", () => reject(new Error("Aborted")), { once: true });
  });
  globalThis.setTimeout = (callback, ms) => { const id = nextTimer++; timers.set(id, { callback, at: now + ms }); return id; };
  globalThis.clearTimeout = id => timers.delete(id);
  globalThis.setInterval = (callback, ms) => { const id = nextTimer++; intervals.set(id, { callback, ms }); return id; };
  globalThis.clearInterval = id => intervals.delete(id);
  const react = {
    useState: value => { const index = state.length; state.push(value); return [value, next => state[index] = next]; },
    useRef: value => ({ current: value }), useCallback: fn => fn,
    useEffect: fn => { cleanup = fn(); },
  };
  const exports = {};
  new Function("require", "exports", compiled)(name => { assert.equal(name, "react"); return react; }, exports);
  const view = exports.useLiveSnapshot(initial, "/snapshot", stationId, policy);
  t.after(async () => {
    cleanup(); await flush(); Date.now = realNow;
    for (const name of names) {
      if (originals[name] === undefined) delete globalThis[name];
      else globalThis[name] = originals[name];
    }
  });
  return {
    view, requests, state, timers, streams, listeners, windowListeners, intervals, advance, flush,
    cleanup: () => cleanup(),
    changed: data => streams.at(-1).handlers.changed({ data: typeof data === "string" ? data : JSON.stringify(data) }),
    ready: () => streams.at(-1).handlers.ready(),
    tick: async ms => { now += ms; for (const { callback } of intervals.values()) callback(); await advance(0); },
    resolve: async (value, status = 200) => {
      requests.at(-1).resolve({ ok: status === 200, status, json: async () => ({ value }) });
      await flush();
    },
    resolveData: async data => {
      requests.at(-1).resolve({ ok: true, status: 200, json: async () => data }); await flush();
    },
  };
}

test("the selected station refreshes immediately and empty/legacy hints still resync", async t => {
  const h = mount(t);
  await h.resolve("initial fetch");
  h.changed({ stationId: "station-2" });
  await h.advance(0);
  assert.equal(h.requests.length, 1);
  for (const hint of [{ stationId: "STATION-1" }, {}, { StationId: "station-1" }, null, "invalid json"]) {
    const before = h.requests.length;
    h.changed(hint); await h.advance(0);
    assert.equal(h.requests.length, before + 1, "No 250ms debounce or 30s poll is needed");
    await h.resolve("current");
  }
  assert.equal(h.state[1], true);
});

test("event bursts have one request and a trailing refresh without overwriting a mutation", async t => {
  const h = mount(t);
  for (let i = 0; i < 100; i++) h.changed({ stationId: "station-1" });
  assert.equal(h.requests.length, 1);
  h.view.setSnapshot({ value: "new connection" });
  await h.resolve("old request");
  assert.deepEqual(h.state[0], { value: "new connection" });
  await h.advance(0);
  assert.equal(h.requests.length, 2);
  await h.resolve("live energy");
  assert.deepEqual(h.state[0], { value: "live energy" });
  h.ready();
  await h.advance(0);
  h.cleanup(); await h.flush();
  assert.equal(h.streams[0].closed, true);
  assert.equal(h.requests.at(-1).options.signal.aborted, true);
  assert.equal(h.timers.size, 0);
  assert.equal(h.intervals.size, 0);
  assert.equal(h.listeners.visibilitychange, undefined);
  assert.equal(h.windowListeners.online, undefined);
});

test("disconnects fetch immediately, poll every five seconds and resync on reconnect", async t => {
  const h = mount(t);
  h.ready(); await h.resolve("first"); await h.advance(0); await h.resolve("connected");
  h.streams[0].onerror(); await h.advance(0);
  assert.equal(h.state[1], false);
  await h.resolve("from fallback");
  const before = h.requests.length;
  await h.tick(5_000);
  assert.equal(h.requests.length, before + 1);
  await h.resolve("still fresh");
  h.ready(); await h.advance(0);
  assert.equal(h.state[1], true);
  assert.equal(h.requests.length, before + 2);
});

test("silent streams are replaced; hidden tabs wait until visible and online resumes immediately", async t => {
  const h = mount(t);
  await h.resolve("first"); h.ready(); await h.advance(0); await h.resolve("ready");
  for (let i = 0; i < 4; i++) {
    h.streams[0].handlers.heartbeat(); await h.tick(5_000);
  }
  assert.equal(h.streams.length, 1);
  globalThis.document.hidden = true;
  await h.tick(30_000);
  const before = h.requests.length;
  h.changed({}); await h.advance(0);
  assert.equal(h.requests.length, before);
  await h.tick(30_000);
  globalThis.document.hidden = false;
  h.listeners.visibilitychange(); await h.advance(0);
  assert.equal(h.streams[0].closed, true);
  assert.equal(h.streams.length, 2);
  assert.equal(h.state[1], false);
  await h.resolve("after sleep");
  h.windowListeners.online(); await h.advance(0);
  assert.equal(h.requests.length, before + 2);
});

test("a stuck snapshot times out and subsequent events can recover", async t => {
  const h = mount(t);
  await h.advance(10_000);
  assert.equal(h.requests[0].options.signal.aborted, true);
  assert.equal(h.state[2], true);
  h.changed({}); await h.advance(0); await h.resolve("recovered");
  assert.deepEqual(h.state[0], { value: "recovered" });
  assert.equal(h.state[2], false);
});

test("expired authentication stops the stream and any further refresh", async t => {
  const h = mount(t);
  await h.resolve(null, 401);
  assert.equal(h.state.redirect, "/");
  assert.equal(h.streams[0].closed, true);
  await h.tick(30_000);
  assert.equal(h.requests.length, 1);
});

function event(eventType, extra = {}) {
  return { eventId: `${eventType}-${extra.entityVersion ?? 1}`, eventType, stationId: "station-1",
    entityType: "ChargingSession", entityId: "session-1", entityVersion: 1, occurredAt: "2026-10-02T12:00:00Z", version: 1, ...extra };
}
const station = { id: "station-1", version: 1, status: "Available" };
const session = { id: "session-1", version: 1, startedAt: "2026-10-02T12:00:00Z", startReceivedAt: "2026-10-02T12:00:01Z",
  status: "Charging", stoppedAt: null, energyKWh: 0 };
const charging = { station, session: null, connectionRequest: { id: "claim", state: "Waiting" }, deviceConnected: true, inUse: false };

test("1000 private meter deltas cause no map/history requests; completion only fetches history once", async t => {
  const initial = { stations: [station], history: [] };
  const h = mount(t, null, initial, policyExports.driverLivePolicy);
  await h.resolveData(initial);
  for (let i = 0; i < 1000; i++) h.changed(event("SessionMeasured", { entityVersion: i, session: { ...session, version: i } }));
  await h.advance(0);
  assert.equal(h.requests.length, 1);
  h.changed(event("StationChanged", { station: { ...station, version: 2, status: "Charging" } }));
  assert.equal(h.state[0].stations[0].status, "Charging");
  h.changed(event("SessionCompleted")); h.changed(event("SessionCompleted"));
  await h.advance(0);
  assert.equal(h.requests.length, 2);
  assert.equal(h.requests[1].url, "/api/driver/history");
  await h.resolveData([{ id: "history-1" }]);
  assert.equal(h.state[0].history[0].id, "history-1");
  assert.equal(h.state[0].stations[0].version, 2);
});

test("the detail view patches start, meter, pause, error and completion without fetching", async t => {
  const h = mount(t, "station-1", charging, policyExports.chargingLivePolicy);
  await h.resolveData(charging);
  for (const [i, status] of ["Charging", "Charging", "PausedByVehicle", "Faulted", "Completed"].entries()) {
    h.changed(event(i === 4 ? "SessionCompleted" : "SessionMeasured", {
      entityVersion: i + 2, session: { ...session, version: i + 2, status, energyKWh: i,
        stoppedAt: i === 4 ? "2026-10-02T12:05:00Z" : null },
    }));
    assert.equal(h.state[0].session.status, status);
    assert.equal(h.state[0].connectionRequest, null);
  }
  await h.advance(0);
  assert.equal(h.requests.length, 1);
  h.changed(event("SessionMeasured", { entityVersion: 1, session }));
  assert.equal(h.state[0].session.status, "Completed", "An old packet cannot resurrect completed charging");
});

test("recovery responses cannot resurrect an archived station or erase a new station delta", async t => {
  const initial = { stations: [station], history: [] };
  const h = mount(t, null, initial, policyExports.driverLivePolicy);
  h.changed(event("StationRemoved", { entityVersion: 3 }));
  h.changed(event("StationChanged", { eventId: "new-station", stationId: "station-2", station: { id: "station-2", version: 1 } }));
  await h.resolveData(initial);
  assert.deepEqual(h.state[0].stations.map(x => x.id), ["station-2"]);
});

test("a start delta beats an in-flight empty snapshot; mutation responses retain newer measurements", async t => {
  const h = mount(t, "station-1", charging, policyExports.chargingLivePolicy);
  h.changed(event("SessionStarted", { session: { ...session, version: 4, energyKWh: 3 } }));
  await h.resolveData(charging);
  assert.equal(h.state[0].session.energyKWh, 3);
  h.view.setSnapshot({ ...charging, connectionRequest: null, session });
  assert.equal(h.state[0].session.energyKWh, 3);
});

test("malformed envelopes recover from an authorized snapshot and do not break the listener", async t => {
  const h = mount(t, "station-1", charging, policyExports.chargingLivePolicy);
  await h.resolveData(charging);
  h.changed({ eventType: "SessionMeasured" }); await h.advance(0);
  assert.equal(h.requests.length, 2);
  await h.resolveData(charging);
  h.changed(event("SessionStarted", { session }));
  assert.equal(h.state[0].session.id, session.id);
});

test("a late completion cannot cancel waiting for the next charge and old station deltas cannot undo removal", async t => {
  const completed = { ...session, version: 8, status: "Completed", stoppedAt: "2026-10-02T12:05:00Z" };
  const waiting = { ...charging, session: completed };
  assert.equal(policyExports.chargingLivePolicy.event(waiting, event("SessionCompleted", { session: completed })), null);
  const updatedCompletion = policyExports.chargingLivePolicy.event(waiting, event("SessionCompleted", {
    session: { ...completed, version: 9 },
  }));
  assert.equal(updatedCompletion.snapshot.connectionRequest, waiting.connectionRequest);
  assert.equal(updatedCompletion.snapshot.deviceConnected, waiting.deviceConnected);
  const initial = { stations: [station], history: [] };
  const h = mount(t, null, initial, policyExports.driverLivePolicy);
  await h.resolveData(initial);
  h.changed(event("StationRemoved", { entityType: "ChargingStation", entityId: station.id, entityVersion: 3 }));
  h.changed(event("StationChanged", { entityType: "ChargingStation", entityId: station.id, entityVersion: 2, station: { ...station, version: 2 } }));
  assert.deepEqual(h.state[0].stations, []);
});

test("all command acknowledgements patch the diagnostic result immediately without a fetch", async t => {
  const initial = { ...charging, diagnostics: { externalSessionId: null, meterStartWh: null, stopReason: null, samples: [], lastCommand: null } };
  const h = mount(t, "station-1", initial, policyExports.chargingLivePolicy);
  await h.resolveData(initial);
  for (const [index, action] of ["BootNotification", "Authorize", "Heartbeat", "StatusNotification", "MeterValues", "StartTransaction", "StopTransaction"].entries()) {
    const command = { action, accepted: index % 2 === 0, failureCode: index % 2 ? "SecurityError" : null,
      description: null, receivedAt: `2026-10-02T12:00:0${index}Z` };
    h.changed(event("DeviceCommandChanged", { eventId: `command-${index}`, command }));
    assert.deepEqual(h.state[0].diagnostics.lastCommand, command);
  }
  await h.advance(0);
  assert.equal(h.requests.length, 1);
});

test("connector faults patch immediately, preserve newer versions and refresh authorized ownership", async t => {
  const initial = { ...charging, connector: { status: "Available", faultCode: null, faultInfo: null, version: 1 } };
  const h = mount(t, "station-1", initial, policyExports.chargingLivePolicy);
  const connector = { status: "Faulted", faultCode: "GroundFailure", faultInfo: "Safety check", version: 4 };
  h.changed(event("ConnectorChanged", { entityType: "ChargingConnector", entityId: "connector-1", entityVersion: 4, connector }));
  assert.deepEqual(h.state[0].connector, connector);
  await h.resolveData(initial);
  assert.deepEqual(h.state[0].connector, connector);
  const restored = { status: "Available", faultCode: null, faultInfo: null, version: 5 };
  h.changed(event("ConnectorChanged", { entityType: "ChargingConnector", entityId: "connector-1", entityVersion: 5, connector: restored }));
  assert.deepEqual(h.state[0].connector, restored);
  await h.advance(0);
  assert.equal(h.requests.length, 2);
  await h.resolveData({ ...initial, connector: restored, inUse: true });
  assert.equal(h.state[0].inUse, true);
  assert.deepEqual(h.state[0].connector, restored);
});

test("new readings and command rejections received during recovery survive its older response", async t => {
  const initial = { ...charging, session, diagnostics: { externalSessionId: "1", meterStartWh: 0, stopReason: null, samples: [], lastCommand: null } };
  const h = mount(t, "station-1", initial, policyExports.chargingLivePolicy);
  const readings = [{ measurand: "Frequency", value: "50", unit: "Hz", phase: null, context: "Sample.Clock", location: "Outlet", format: "Raw", observedAt: "2026-10-02T12:00:00Z", receivedAt: "2026-10-02T12:00:01Z" }];
  h.changed(event("SessionMeasured", { entityVersion: 2, session: { ...session, version: 2, readings } }));
  const command = { action: "StopTransaction", accepted: false, failureCode: "PropertyConstraintViolation", description: "Unknown transaction", receivedAt: "2026-10-02T12:00:02Z" };
  h.changed(event("DeviceCommandChanged", { command }));
  await h.resolveData(initial);
  assert.deepEqual(h.state[0].session.readings, readings);
  assert.deepEqual(h.state[0].diagnostics.lastCommand, command);
  await h.advance(0);
  assert.equal(h.requests.length, 1);
});

test("a physical start fetches transaction metadata once and 1000 subsequent reading deltas need no fetch", async t => {
  const h = mount(t, "station-1", charging, policyExports.chargingLivePolicy);
  await h.resolveData(charging);
  h.changed(event("SessionStarted", { entityVersion: 2, session: { ...session, version: 2 } }));
  assert.equal(h.state[0].session.id, session.id);
  await h.advance(0);
  assert.equal(h.requests.length, 2);
  await h.resolveData({ ...charging, session: { ...session, version: 2 }, diagnostics: { externalSessionId: "17", meterStartWh: 1000, stopReason: null, samples: [], lastCommand: null } });
  for (let i = 3; i < 1003; i++) h.changed(event("SessionMeasured", { entityVersion: i,
    session: { ...session, version: i, readings: [{ measurand: "Voltage", value: String(i), receivedAt: "2026-10-02T12:00:01Z" }] } }));
  await h.advance(0);
  assert.equal(h.requests.length, 2);
  assert.equal(h.state[0].session.readings[0].value, "1002");
});

test("command sequence versions order equal timestamps and survive a stale recovery snapshot", async t => {
  const accepted = { action: "StopTransaction", accepted: true, failureCode: null, description: null, receivedAt: "2026-10-02T12:00:00Z", version: 12 };
  const rejected = { ...accepted, accepted: false, failureCode: "PropertyConstraintViolation", version: 13 };
  const initial = { ...charging, diagnostics: { externalSessionId: null, meterStartWh: null, stopReason: null, samples: [], lastCommand: accepted } };
  const h = mount(t, "station-1", initial, policyExports.chargingLivePolicy);
  h.changed(event("DeviceCommandChanged", { eventId: "new-rejected", command: rejected }));
  h.changed(event("DeviceCommandChanged", { eventId: "old-accepted", command: accepted }));
  assert.deepEqual(h.state[0].diagnostics.lastCommand, rejected);
  await h.resolveData(initial);
  assert.deepEqual(h.state[0].diagnostics.lastCommand, rejected);
});

test("waiting owners receive raw measurements without a session and retain them through heartbeats", async t => {
  const h = mount(t, "station-1", charging, policyExports.chargingLivePolicy);
  await h.resolveData(charging);
  const readings = [{ measurand: "Current.Export", value: "3.4", unit: "A", phase: "L3", context: "Other", format: "Raw", location: "Cable", observedAt: "2026-10-02T12:00:00Z", receivedAt: "2026-10-02T12:00:00Z" }];
  const command = { action: "MeterValues", accepted: true, failureCode: null, description: null, receivedAt: "2026-10-02T12:00:00Z", version: 1, readings };
  h.changed(event("DeviceCommandChanged", { eventId: "meter-without-session", command }));
  assert.deepEqual(h.state[0].diagnostics.samples, readings);
  h.changed(event("DeviceCommandChanged", { eventId: "heartbeat", command: { ...command, action: "Heartbeat", readings: undefined, version: 2 } }));
  assert.deepEqual(h.state[0].diagnostics.samples, readings);
  h.changed(event("SessionStarted", { session: { ...session, version: 2, readings: [] } }));
  assert.deepEqual(h.state[0].diagnostics.samples, []);
  assert.equal(h.state[0].diagnostics.lastCommand, null);
  assert.equal(h.state[0].session.readings.length, 0);
});

test("unrelated accepted activity cannot dismiss a rejected start or stop", async t => {
  const initial = { ...charging, session, diagnostics: { externalSessionId: "1", meterStartWh: 0, stopReason: null, samples: [], lastCommand: null, lastFailure: null } };
  const h = mount(t, "station-1", initial, policyExports.chargingLivePolicy);
  await h.resolveData(initial);
  let version = 1;
  const command = (action, accepted) => ({ action, accepted, failureCode: accepted ? null : "OccurrenceConstraintViolation", description: null,
    version: version++, receivedAt: "2026-10-02T12:00:00Z" });
  const change = command => h.changed(event("DeviceCommandChanged", { eventId: `command-failure-${command.version}`, command }));
  const rejectedStart = command("StartTransaction", false);
  change(rejectedStart);
  for (const action of ["Heartbeat", "StatusNotification", "MeterValues", "Authorize"]) {
    change(command(action, true));
    assert.deepEqual(h.state[0].diagnostics.lastFailure, rejectedStart);
  }
  change(command("StartTransaction", true));
  assert.equal(h.state[0].diagnostics.lastFailure, null);
  const rejectedStop = command("StopTransaction", false);
  change(rejectedStop);
  change(command("StatusNotification", true));
  assert.deepEqual(h.state[0].diagnostics.lastFailure, rejectedStop);
  h.changed(event("SessionCompleted", { entityVersion: 2, session: { ...session, version: 2, status: "Completed", stoppedAt: "2026-10-02T12:05:00Z" } }));
  assert.equal(h.state[0].diagnostics.lastFailure, null);
});

test("a successful new session clears its start rejection while retaining unrelated failures", () => {
  const command = { action: "StartTransaction", accepted: false, failureCode: "SecurityError", description: null,
    version: 10, receivedAt: "2026-10-02T12:00:00Z" };
  const initial = { ...charging, diagnostics: { externalSessionId: null, meterStartWh: null, stopReason: null, samples: [], lastCommand: command, lastFailure: command } };
  const start = event("SessionStarted", { entityVersion: 2, session: { ...session, version: 2 } });
  assert.equal(policyExports.chargingLivePolicy.event(initial, start).snapshot.diagnostics.lastFailure, null);
  const meterFailure = { ...command, action: "MeterValues" };
  const unrelated = { ...initial, diagnostics: { ...initial.diagnostics, lastFailure: meterFailure } };
  assert.deepEqual(policyExports.chargingLivePolicy.event(unrelated, start).snapshot.diagnostics.lastFailure, meterFailure);
});

test("an older active snapshot cannot revive a Stop rejection resolved by a completion delta", () => {
  const rejection = { action: "StopTransaction", accepted: false, failureCode: "PropertyConstraintViolation", description: null,
    version: 10, receivedAt: "2026-10-02T12:00:00Z" };
  const old = { ...charging, session, diagnostics: { externalSessionId: "1", meterStartWh: 0, stopReason: null, samples: [], lastCommand: rejection, lastFailure: rejection } };
  const completed = { ...old, session: { ...session, version: 3, status: "Completed", stoppedAt: "2026-10-02T12:05:00Z" },
    diagnostics: { ...old.diagnostics, lastCommand: { ...rejection, accepted: true, action: "StatusNotification", version: 9 }, lastFailure: null } };
  const result = policyExports.chargingLivePolicy.merge(completed, old);
  assert.equal(result.session.status, "Completed");
  assert.equal(result.diagnostics.lastFailure, null);
});

test("accepted raw measurement groups survive unrelated commands even when the session summary is older", async t => {
  const oldReadings = [{ measurand: "Voltage", value: "230", receivedAt: "2026-10-02T12:00:00Z" }];
  const latestReadings = [{ measurand: "Voltage", value: "240", observedAt: "2030-10-02T12:00:00Z", receivedAt: "2026-10-02T12:00:01Z" }];
  const initial = { ...charging, session: { ...session, readings: oldReadings }, diagnostics: { externalSessionId: "1", meterStartWh: 0, stopReason: null, samples: oldReadings, lastCommand: null } };
  const h = mount(t, "station-1", initial, policyExports.chargingLivePolicy);
  await h.resolveData(initial);
  const command = { action: "MeterValues", accepted: true, failureCode: null, description: null, receivedAt: "2026-10-02T12:00:01Z", version: 2, readings: latestReadings };
  h.changed(event("DeviceCommandChanged", { eventId: "latest-unprojected-meter", command }));
  h.changed(event("DeviceCommandChanged", { eventId: "following-heartbeat", command: { ...command, action: "Heartbeat", version: 3, readings: undefined } }));
  h.changed(event("SessionStatusChanged", { entityVersion: 2, session: { ...session, version: 2, readings: oldReadings } }));
  assert.deepEqual(h.state[0].diagnostics.samples, latestReadings);
  assert.deepEqual(h.state[0].session.readings, oldReadings);
});

test("a public warning update patches one of ten points without merging private payloads", () => {
  const stations = Array.from({ length: 10 }, (_, index) => ({ ...station, id: `station-${index + 1}`, warningCode: null }));
  const initial = { stations, history: [{ id: "own-history" }] };
  const warning = { ...stations[3], version: 2, warningCode: "HighTemperature" };
  const publicUpdate = event("StationChanged", { entityType: "ChargingStation", entityId: warning.id, stationId: warning.id,
    entityVersion: 2, station: warning, session: { ...session, id: "other-driver-session" },
    command: { action: "StartTransaction", accepted: true } });
  const map = policyExports.driverLivePolicy.event(initial, publicUpdate).snapshot;
  assert.deepEqual(map.stations[3], warning);
  assert.equal(map.stations[3].status, "Available");
  assert.equal(map.history, initial.history);
  for (let index = 0; index < 10; index++) if (index !== 3) assert.equal(map.stations[index], stations[index]);
  const own = { ...charging, station: stations[3], session, diagnostics: null };
  const detail = policyExports.chargingLivePolicy.event(own, publicUpdate).snapshot;
  assert.equal(detail.station.warningCode, "HighTemperature");
  assert.equal(detail.session.id, session.id);
  assert.equal(detail.diagnostics, null);
});
