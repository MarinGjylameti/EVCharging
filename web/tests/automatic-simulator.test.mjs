import test from "node:test";
import assert from "node:assert/strict";
import { loadTypescript } from "./helpers/load-typescript.mjs";

const exports = loadTypescript(new URL("../src/features/driver/automatic-simulator.ts", import.meta.url));
const { AutomaticSimulator, validateSimulatorConfig, simulatorErrors } = exports;
const config = { url: "ws://localhost/ws/cs/SIM-001", stationCode: "SIM-001", capacityKWh: 60, initialSoc: 20, targetSoc: 100,
  powerKW: 22, meterStartWh: 1000, intervalSeconds: 5, speed: 1 };

function setup(t, changes = {}, respond) {
  let now = Date.parse("2026-10-03T14:00:00Z"), sequence = 1, socket;
  const calls = [], logs = [], snapshots = [], timers = new Map();
  const originals = Object.fromEntries(["setTimeout", "clearTimeout", "setInterval", "clearInterval"].map(name => [name, globalThis[name]]));
  globalThis.setTimeout = (fn, delay) => { const id = sequence++; timers.set(id, { fn, at: now + delay }); return id; };
  globalThis.setInterval = (fn, delay) => { const id = sequence++; timers.set(id, { fn, at: now + delay, interval: delay }); return id; };
  globalThis.clearTimeout = globalThis.clearInterval = id => timers.delete(id);
  const flush = async () => { for (let i = 0; i < 50; i++) await Promise.resolve(); };
  const advance = async ms => {
    now += ms;
    for (const [id, timer] of [...timers]) if (timer.at <= now && timers.has(id)) {
      if (timer.interval) timer.at = now + timer.interval; else timers.delete(id);
      timer.fn();
    }
    await flush();
  };
  const factory = (url, protocol) => {
    assert.equal(url, config.url); assert.equal(protocol, "ocpp1.6");
    socket = { onopen: null, onmessage: null, onclose: null, onerror: null,
      send(data) {
        const call = JSON.parse(data); calls.push(call);
        if (call[0] !== 2) return;
        const payload = call[2] === "BootNotification" ? { status: "Accepted", interval: 30 }
          : call[2] === "Authorize" ? { idTagInfo: { status: "Accepted" } }
          : call[2] === "StartTransaction" ? { transactionId: 83, idTagInfo: { status: "Accepted" } }
          : call[2] === "DataTransfer" ? { status: "Accepted" } : {};
        const frame = respond?.(call) ?? [3, call[1], payload];
        if (frame !== "drop") queueMicrotask(() => socket.onmessage?.({ data: JSON.stringify(frame) }));
      },
      close() { const closing = this; queueMicrotask(() => closing.onclose?.()); },
    };
    const opening = socket; queueMicrotask(() => opening.onopen?.()); return socket;
  };
  const engine = new AutomaticSimulator({ ...config, ...changes }, state => snapshots.push(state), log => logs.push(log), factory, () => now);
  t.after(async () => { engine.dispose(); await flush(); Object.assign(globalThis, originals); });
  return { engine, calls, logs, snapshots, advance, flush, timers, get socket() { return socket; } };
}

test("one start sends the accepted transaction's energy, total power and changing battery level automatically", async t => {
  const s = setup(t);
  await s.engine.connect(); await s.engine.start();
  assert.deepEqual(s.calls.slice(0, 7).map(frame => frame[2]), ["BootNotification", "StatusNotification", "Authorize", "StatusNotification", "StartTransaction", "StatusNotification", "MeterValues"]);
  assert.equal(s.engine.snapshot.transactionId, 83);
  const first = s.calls.find(frame => frame[2] === "MeterValues")[3];
  assert.equal(first.transactionId, 83);
  assert.deepEqual(first.meterValue[0].sampledValue.map(sample => [sample.measurand, sample.unit]), [
    ["Energy.Active.Import.Register", "Wh"], ["Power.Active.Import", "kW"], ["SoC", "Percent"],
  ]);
  assert.ok(first.meterValue[0].sampledValue.every(sample => sample.phase === undefined));
  await s.advance(30_000);
  const latest = s.calls.filter(frame => frame[2] === "MeterValues").at(-1)[3];
  assert.equal(latest.meterValue[0].sampledValue[0].value, "1183");
  assert.equal(latest.meterValue[0].sampledValue[1].value, "22");
  assert.equal(latest.meterValue[0].sampledValue[2].value, "20.31");
  assert.ok(s.calls.some(frame => frame[2] === "Heartbeat"));
});
test("the public estimate uses the selected target and simulation speed and follows power and pause", async t => {
  const s = setup(t, { targetSoc: 80, speed: 30 });
  await s.engine.connect(); await s.engine.start();
  const latest = () => {
    const frame = s.calls.filter(frame => frame[2] === "DataTransfer").at(-1)[3];
    assert.equal(frame.vendorId, "EVCharging.Simulator");
    assert.equal(frame.messageId, "ChargingEstimate");
    return JSON.parse(frame.data);
  };
  assert.deepEqual(latest(), { transactionId: 83, remainingSeconds: 197 });
  await s.engine.setPower(11);
  assert.deepEqual(latest(), { transactionId: 83, remainingSeconds: 393 });
  await s.engine.pause();
  assert.equal(latest().remainingSeconds, 0);
  await s.engine.resume(); await s.advance(30_000);
  assert.equal(latest().remainingSeconds, 363);
  await s.engine.stop();
  assert.equal(latest().remainingSeconds, 0);
});

test("an older server without public estimates still receives automatic charging measurements", async t => {
  const s = setup(t, {}, call => call[2] === "DataTransfer"
    ? [4, call[1], "NotSupported", "This action is not supported.", {}] : undefined);
  await s.engine.connect(); await s.engine.start(); await s.advance(30_000);
  assert.equal(s.engine.snapshot.connected, true);
  assert.ok(s.engine.snapshot.energyKWh > 0);
  assert.equal(s.calls.filter(frame => frame[2] === "DataTransfer").length, 1);
  assert.ok(s.calls.filter(frame => frame[2] === "MeterValues").length > 1);
  await s.engine.stop();
  assert.equal(s.engine.snapshot.status, "Completed");
});

test("vehicle pause and charger pause freeze battery and energy, resume uses the same transaction", async t => {
  const s = setup(t); await s.engine.connect(); await s.engine.start(); await s.advance(30_000);
  for (const byCharger of [false, true]) {
    await s.engine.pause(byCharger);
    const paused = s.engine.snapshot;
    await s.advance(60_000);
    assert.equal(s.engine.snapshot.energyKWh, paused.energyKWh);
    assert.equal(s.engine.snapshot.soc, paused.soc);
    assert.equal(s.engine.snapshot.powerKW, 0);
    assert.ok(s.calls.some(frame => frame[2] === "StatusNotification" && frame[3].status === (byCharger ? "SuspendedEVSE" : "SuspendedEV")));
    await s.engine.resume(); await s.advance(5_000);
    assert.ok(s.engine.snapshot.energyKWh > paused.energyKWh);
    assert.equal(s.engine.snapshot.transactionId, 83);
  }
  assert.equal(s.calls.filter(frame => frame[2] === "StartTransaction").length, 1);
});
test("target battery level sends an actual stop, final register and Available, and next cycle continues the meter", async t => {
  const s = setup(t, { capacityKWh: 1, initialSoc: 20, targetSoc: 80, speed: 60 });
  await s.engine.connect(); await s.engine.start(); await s.advance(5_000);
  const stop = s.calls.find(frame => frame[2] === "StopTransaction")[3];
  assert.equal(stop.transactionId, 83); assert.equal(stop.meterStop, 1600);
  assert.equal(s.engine.snapshot.soc, 80); assert.equal(s.engine.snapshot.status, "Completed");
  assert.equal(s.engine.snapshot.transactionId, null);
  assert.equal(s.calls.at(-1)[3].status, "Available");
  const count = s.calls.filter(frame => frame[2] === "MeterValues").length;
  await s.advance(20_000);
  assert.equal(s.calls.filter(frame => frame[2] === "MeterValues").length, count);
  await s.engine.start();
  assert.equal(s.calls.filter(frame => frame[2] === "StartTransaction").at(-1)[3].meterStart, 1600);
});
test("faults freeze energy, recovery reports Charging and a socket reconnect preserves identity", async t => {
  const s = setup(t); await s.engine.connect(); await s.engine.start(); await s.advance(5_000);
  await s.engine.fault("OverCurrentFailure");
  const faulted = s.engine.snapshot;
  await s.advance(60_000); assert.equal(s.engine.snapshot.energyKWh, faulted.energyKWh);
  assert.equal(s.engine.snapshot.status, "Faulted");
  await s.engine.resume(); s.engine.disconnect(); await s.flush();
  assert.equal(s.engine.snapshot.transactionId, 83);
  assert.match(s.logs.findLast(log => log.action === "Disconnected").detail, /transaction 83 has no confirmed StopTransaction/);
  await s.engine.connect(); assert.equal(s.engine.snapshot.status, "Paused");
  await s.engine.resume(); await s.advance(5_000); await s.engine.stop();
  assert.equal(s.calls.filter(frame => frame[2] === "StartTransaction").length, 1);
  assert.equal(s.calls.find(frame => frame[2] === "StopTransaction")[3].transactionId, 83);
});
test("disconnecting before a start does not claim a transaction exists on the server", async t => {
  const s = setup(t); await s.engine.connect();
  s.engine.disconnect(); await s.flush();
  assert.equal(s.engine.snapshot.transactionId, null);
  assert.match(s.logs.findLast(log => log.action === "Disconnected").detail, /no active transaction is tracked.*Server state was not checked/);
  assert.ok(!s.calls.some(frame => ["StartTransaction", "StopTransaction"].includes(frame[2])));
});
test("a rejected start never emits measurements, StopTransaction or Charging", async t => {
  const s = setup(t, {}, call => call[2] === "StartTransaction" ? [4, call[1], "OccurrenceConstraintViolation", "Active transaction exists", {}] : undefined);
  await s.engine.connect(); await assert.rejects(s.engine.start(), /OccurrenceConstraintViolation/);
  await s.advance(60_000);
  assert.equal(s.engine.snapshot.transactionId, null);
  assert.equal(s.engine.snapshot.connected, false);
  assert.ok(!s.calls.some(frame => ["MeterValues", "StopTransaction"].includes(frame[2]) || frame[3].status === "Charging"));
});
test("changing power reports it immediately and changes later battery progress without resetting the session", async t => {
  const s = setup(t); await s.engine.connect(); await s.engine.start(); await s.advance(30_000);
  const original = s.engine.snapshot;
  await s.engine.setPower(11);
  const power = s.calls.filter(frame => frame[2] === "MeterValues").at(-1)[3].meterValue[0].sampledValue.find(sample => sample.measurand === "Power.Active.Import");
  assert.equal(power.value, "11");
  assert.equal(s.engine.snapshot.energyKWh, original.energyKWh);
  await s.advance(30_000);
  assert.ok(Math.abs(s.engine.snapshot.energyKWh - original.energyKWh - 11 / 120) < 1e-9);
  assert.equal(s.engine.snapshot.transactionId, 83);
  await s.engine.setPower(0);
  const zero = s.engine.snapshot;
  await s.advance(30_000);
  assert.equal(s.engine.snapshot.energyKWh, zero.energyKWh);
  assert.equal(s.engine.snapshot.transactionId, 83);
  await s.engine.stop("EVDisconnected");
  assert.equal(s.calls.find(frame => frame[2] === "StopTransaction")[3].reason, "EVDisconnected");
});
test("a lost meter response preserves the active transaction and stops generating messages", async t => {
  const s = setup(t, {}, call => call[2] === "MeterValues" ? "drop" : undefined);
  await s.engine.connect();
  const starting = s.engine.start();
  const rejected = assert.rejects(starting, /timed out/);
  await s.flush(); await s.advance(15_000); await rejected;
  assert.equal(s.engine.snapshot.transactionId, 83);
  assert.equal(s.engine.snapshot.connected, false);
  assert.equal(s.timers.size, 0);
});
test("all standard fault codes and invalid simulation inputs are checked", () => {
  assert.equal(simulatorErrors.length, 15);
  for (const changes of [{ capacityKWh: 0 }, { targetSoc: 10 }, { initialSoc: -1 }, { powerKW: NaN }, { speed: 61 }, { meterStartWh: 1.5 }, { url: "https://localhost" }])
    assert.throws(() => validateSimulatorConfig({ ...config, ...changes }));
});
