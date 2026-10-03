import test from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { createRequire } from "node:module";
import ts from "typescript";
import { createElement } from "react";
import { renderToStaticMarkup } from "react-dom/server";

const require = createRequire(import.meta.url);
function compile(path, dependencies = {}) {
  const compiled = ts.transpileModule(readFileSync(new URL(path, import.meta.url), "utf8"), {
    compilerOptions: { module: ts.ModuleKind.CommonJS, jsx: ts.JsxEmit.ReactJSX, target: ts.ScriptTarget.ES2022 },
  }).outputText;
  const exports = {};
  new Function("require", "exports", compiled)(name => dependencies[name] ?? require(name), exports);
  return exports;
}
const fault = compile("../src/features/driver/charging-fault.tsx");
const diagnostics = compile("../src/features/driver/simulator-diagnostics.tsx");
const progress = compile("../src/features/driver/charging-progress.tsx", { "./charging-fault": fault });
const { ChargingProgress, formatChargingDuration } = progress;
const session = {
  id: "session-1", status: "Charging", energyKWh: 2.75, powerKW: null,
  isDemo: true, deviceClockSkewed: true,
  stateOfCharge: null, temperatureCelsius: null, cost: 0.96, currency: "EUR",
  startedAt: "2026-10-01T12:00:00Z", startReceivedAt: "2026-10-01T13:00:00Z",
  stoppedAt: null, lastMeasurementReceivedAt: "2026-10-01T13:01:00Z",
};
const render = (changes = {}, props = {}) => renderToStaticMarkup(createElement(ChargingProgress, {
  session: { ...session, ...changes }, language: "en", now: Date.parse("2026-10-01T13:02:00Z"),
  stale: false, ...props,
}));

test("missing battery level and remaining time never produce an invented percentage or countdown", () => {
  const html = render();
  assert.match(html, /2.75/);
  assert.match(html, /0.96/);
  assert.match(html, /2:00/);
  assert.match(html, /No battery percentage reading received yet/);
  assert.match(html, /charge-progress-indeterminate/);
  assert.doesNotMatch(html, /aria-valuenow|~\d|62:00|NaN/);
  assert.match(html, /Battery level/);
  assert.match(html, /TIME UNTIL BATTERY IS FULL/);
  assert.match(html, /No estimate reported by the charger/);
});

test("reported state of charge drives the accessible progress bar", () => {
  const html = render({ stateOfCharge: 45, powerKW: 22 });
  assert.match(html, /aria-valuenow="45"/);
  assert.match(html, /width:45%/);
  assert.match(html, /Battery level reported by the charger/);
  assert.match(html, /22/);
  assert.doesNotMatch(render({ stateOfCharge: 101 }), /aria-valuenow/);
});

test("pause preserves energy and cost and hides stale power and remaining time", () => {
  const html = render({ status: "PausedByVehicle", powerKW: 22 });
  assert.match(html, /Paused by vehicle/);
  assert.match(html, /2.75/);
  assert.match(html, /0.96/);
  assert.match(html, /Energy transfer is paused/);
  assert.match(html, /includes pauses/i);
  assert.doesNotMatch(html, /~12|is-charging|>22</);
});

test("completion uses receipt duration and only confirms history with valid totals", () => {
  const changes = { status: "Completed", stoppedAt: "2026-10-01T12:40:00Z", stopReceivedAt: "2026-10-01T13:03:00Z" };
  const html = render(changes);
  assert.match(html, /3:00/);
  assert.match(html, /Saved in history/);
  const missing = render({ ...changes, energyKWh: null, cost: null });
  assert.doesNotMatch(missing, /Saved in history|has been saved/);
  assert.match(missing, /valid final reading and tariff/);
});

test("real charger duration uses the physical start even when app association or receipt is later", () => {
  const html = render({ isDemo: false });
  assert.match(html, /01:02:00/);
  assert.match(html, /Duration uses the start reported by the charger/);
  assert.doesNotMatch(html, /Duration here uses message receipt times/);
});

test("single charger page shows its live process and excludes the old multi-point testing form", () => {
  const live = { station: { id: "station-1", code: "SIM-001", name: "Station One", address: "Address", city: "Tirana", connectorType: "CCS2", powerKW: 22, pricePerKWh: .35, currency: "EUR" },
    canConnect: true, inUse: false, deviceConnected: true, connectionRequest: null, session };
  const { StationChargingView } = compile("../src/features/driver/station-charging-view.tsx", {
    "next/link": { default: ({ href, children }) => createElement("a", { href }, children) },
    "./use-live-snapshot": { useLiveSnapshot: () => ({ snapshot: live, setSnapshot: () => {}, connected: true, unavailable: false }) },
    "./charging-progress": progress,
    "./charging-fault": fault,
    "./simulator-diagnostics": diagnostics,
    "./live-update-policy": compile("../src/features/driver/live-update-policy.ts"),
  });
  const html = renderToStaticMarkup(createElement(StationChargingView, { initial: live, language: "en" }));
  assert.match(html, /Station One/);
  assert.match(html, /SIM-001/);
  assert.match(html, /2.75/);
  assert.match(html, /Back to map/);
  assert.doesNotMatch(html, /SIM-002|<select|<input|Connect for testing/);
});

test("faults, disconnections and expired measurements never display old charging power", () => {
  for (const status of ["Faulted", "Unavailable", "ConnectionLost", "Connecting"]) {
    const html = render({ status, powerKW: 22, powerSource: "Reported" });
    assert.doesNotMatch(html, />22</);
    assert.match(html, /Power is unavailable|Waiting for the charger to reconnect|Waiting for charging and readings/);
  }
  const expired = render({ powerKW: 22, powerSource: "Reported", powerValidUntil: "2026-10-01T13:01:00Z" });
  assert.doesNotMatch(expired, />22</);
  assert.match(expired, /Waiting for fresh readings/);
});

test("100 percent reports battery level without inventing a stop or completion time", () => {
  const html = render({ stateOfCharge: 100 });
  assert.match(html, /aria-valuenow="100"/);
  assert.match(html, /Charging/);
  assert.doesNotMatch(html, /Charge completed|Saved in history|remaining/i);
});

test("duration is readable across hours and days and rejects invalid timestamps", () => {
  assert.equal(formatChargingDuration(59, false), "00:00:59");
  assert.equal(formatChargingDuration(3661, false), "01:01:01");
  assert.equal(formatChargingDuration(86461, false), "1 day 00:01:01");
  assert.equal(formatChargingDuration(172861, true), "2 ditë 00:01:01");
  assert.equal(formatChargingDuration(-10, true), "00:00:00");
  assert.equal(formatChargingDuration(NaN, false), "—");
});

test("a contradictory final register closes the process but never claims saved financial totals", () => {
  const html = render({ status: "Completed", stoppedAt: "2026-10-01T13:03:00Z", completionWarning: "FinalMeterRegression" });
  assert.match(html, /Charge completed/);
  assert.match(html, /final meter register is lower/);
  assert.match(html, /Awaiting verification/);
  assert.doesNotMatch(html, /Saved in history|has been saved|>0.96</);
});

test("all reported fault codes appear with a clear description and device details remain escaped", () => {
  const codes = ["ConnectorLockFailure", "EVCommunicationError", "GroundFailure", "HighTemperature", "InternalError", "LocalListConflict", "OtherError", "OverCurrentFailure", "OverVoltage", "PowerMeterFailure", "PowerSwitchFailure", "ReaderFailure", "ResetFailure", "UnderVoltage", "WeakSignal"];
  for (const code of codes) {
    const html = render({ faultCode: code, status: "Faulted", faultInfo: "<script>fault</script>" });
    assert.match(html, new RegExp(code));
    assert.match(html, /role="alert"/);
    assert.match(html, /&lt;script&gt;fault&lt;\/script&gt;/);
    assert.doesNotMatch(html, /<script>/);
  }
  assert.doesNotMatch(render({ faultCode: "NoError" }), /role="alert"/);
});

const initialDiagnostics = { externalSessionId: "17", meterStartWh: 2500, stopReason: null, lastCommand: null, samples: [] };
const sample = { measurand: "Voltage", value: "230", unit: "V", phase: "L2", context: "Sample.Clock", location: "Inlet", format: "Raw", observedAt: "2026-10-01T13:00:00Z", receivedAt: "2026-10-01T13:00:01Z" };
const renderDiagnostics = (changes = {}, sessionChanges = {}) => renderToStaticMarkup(createElement(diagnostics.SimulatorDiagnostics, {
  initial: { ...initialDiagnostics, ...changes }, session: { ...session, ...sessionChanges }, language: "en",
}));

test("diagnostic readings expose every reported sample dimension and stop reason from live session deltas", () => {
  const html = renderDiagnostics({}, { readings: [sample], stopReason: "EVDisconnected" });
  for (const text of ["Voltage", "230", "L2", "Sample.Clock", "Inlet", "Raw", "EVDisconnected", "17", "2500"])
    assert.ok(html.includes(text), text);
  assert.match(html, /tabindex="0"/);
  assert.match(html, /Latest accepted samples from the connector update live/);
  assert.doesNotMatch(html, /every 3 seconds/);
});

test("rejected commands are visible while diagnostics are collapsed and correctly identify the action", () => {
  for (const action of ["StartTransaction", "StopTransaction", "Authorize", "BootNotification", "StatusNotification", "MeterValues"]) {
    const html = renderDiagnostics({ lastCommand: { action, accepted: false, failureCode: "PropertyConstraintViolation", description: "Invalid device value", receivedAt: "2026-10-01T13:01:00Z" } });
    assert.ok(html.indexOf('role="alert"') < html.indexOf("<details"), action);
    assert.match(html, /Invalid device value/);
    if (action !== "StopTransaction") assert.doesNotMatch(html, /The stop was rejected/);
  }
  const accepted = renderDiagnostics({ lastCommand: { action: "Heartbeat", accepted: true, failureCode: null, description: null, receivedAt: "2026-10-01T13:01:00Z" } });
  assert.match(accepted, /Heartbeat: Accepted/);
  assert.doesNotMatch(accepted, /role="alert"/);
});

test("unresolved rejection stays visible after accepted heartbeat activity", () => {
  const lastFailure = { action: "StartTransaction", accepted: false, failureCode: "OccurrenceConstraintViolation", description: "Active transaction", receivedAt: "2026-10-01T13:00:00Z", version: 1 };
  const lastCommand = { action: "Heartbeat", accepted: true, failureCode: null, description: null, receivedAt: "2026-10-01T13:01:00Z", version: 2 };
  const html = renderDiagnostics({ lastCommand, lastFailure });
  assert.match(html, /The new start was rejected/);
  assert.match(html, /Heartbeat: Accepted/);
  assert.match(html, /Active transaction/);
  assert.doesNotMatch(renderDiagnostics({ lastCommand: lastFailure, lastFailure: null }), /role="alert"/);
});

test("a reported warning during Charging keeps live power and uses warning styling", () => {
  const html = render({ status: "Charging", faultCode: "HighTemperature", powerKW: 22 });
  assert.match(html, /Warning: High temperature/);
  assert.match(html, /charge-fault-notice is-warning/);
  assert.match(html, />22 /);
  assert.match(html, /is-charging/);
  assert.doesNotMatch(html, /role="alert"|Charger reported a fault|Power is unavailable/);
});

test("station controller faults are separate from an active connector's Charging state", () => {
  const live = { station: { id: "station-1", code: "SIM-001", name: "Station One", address: "Address", city: "Tirana", connectorType: "CCS2", powerKW: 22, pricePerKWh: .35, currency: "EUR" },
    canConnect: true, inUse: false, deviceConnected: true, connectionRequest: null, session: { ...session, powerKW: 22 },
    connector: { status: "Charging", faultCode: null, faultInfo: null, version: 10, deviceStatus: "Faulted", deviceFaultCode: "InternalError", deviceFaultInfo: "Main controller check" } };
  const { StationChargingView } = compile("../src/features/driver/station-charging-view.tsx", {
    "next/link": { default: ({ href, children }) => createElement("a", { href }, children) },
    "./use-live-snapshot": { useLiveSnapshot: () => ({ snapshot: live, setSnapshot: () => {}, connected: true, unavailable: false }) },
    "./charging-progress": progress, "./charging-fault": fault, "./simulator-diagnostics": diagnostics,
    "./live-update-policy": compile("../src/features/driver/live-update-policy.ts"),
  });
  const html = renderToStaticMarkup(createElement(StationChargingView, { initial: live, language: "en" }));
  assert.match(html, /aria-label="Station controller"/);
  assert.match(html, /Main controller check/);
  assert.match(html, /charge-process is-charging/);
  assert.match(html, />22 /);
});

test("the latest raw command group stays visible when summary readings are older", () => {
  const latest = { ...sample, value: "240", observedAt: "2030-10-01T13:00:00Z" };
  const html = renderDiagnostics({ samples: [latest], lastCommand: { action: "Heartbeat", accepted: true, failureCode: null,
    description: null, receivedAt: "2026-10-01T13:00:02Z", version: 3 } }, { readings: [sample] });
  assert.match(html, /240 V/);
  assert.match(html, /2030-10-01T13:00:00Z/);
  assert.doesNotMatch(html, /230 V/);
});

test("public station warning badges describe the warning in the selected language", () => {
  const renderWarning = (code, language) => renderToStaticMarkup(createElement(fault.StationWarning, { code, language }));
  assert.match(renderWarning("HighTemperature", "en"), /Warning: High temperature/);
  assert.match(renderWarning("HighTemperature", "sq"), /Paralajmërim: Temperaturë e lartë/);
  assert.equal(renderWarning("NoError", "en"), "");
  assert.equal(renderWarning(null, "en"), "");
});

