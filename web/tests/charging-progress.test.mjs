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
const { ChargingProgress } = compile("../src/features/driver/charging-progress.tsx");
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
  assert.match(html, /does not report battery percentage/);
  assert.match(html, /charge-progress-indeterminate/);
  assert.doesNotMatch(html, /aria-valuenow|Battery level|~\d|62:00|NaN/);
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
  assert.match(html, /Includes pauses/);
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

test("single charger page shows its live process and excludes the old multi-point testing form", () => {
  const live = { station: { id: "station-1", code: "SIM-001", name: "Station One", address: "Address", city: "Tirana", connectorType: "CCS2", powerKW: 22, pricePerKWh: .35, currency: "EUR" },
    canConnect: true, inUse: false, deviceConnected: true, connectionRequest: null, session };
  const { StationChargingView } = compile("../src/features/driver/station-charging-view.tsx", {
    "next/link": { default: ({ href, children }) => createElement("a", { href }, children) },
    "./use-live-snapshot": { useLiveSnapshot: () => ({ snapshot: live, setSnapshot: () => {}, connected: true, unavailable: false }) },
    "./charging-progress": { ChargingProgress },
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
    assert.match(html, /Waiting for readings/);
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

