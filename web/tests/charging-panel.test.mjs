import test from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { createRequire } from "node:module";
import ts from "typescript";
import { createElement } from "react";
import { renderToStaticMarkup } from "react-dom/server";

const compiled = ts.transpileModule(readFileSync(new URL("../src/features/driver/charging-panel.tsx", import.meta.url), "utf8"), {
  compilerOptions: { module: ts.ModuleKind.CommonJS, jsx: ts.JsxEmit.ReactJSX, target: ts.ScriptTarget.ES2020 },
}).outputText;
const exports = {};
new Function("require", "exports", compiled)(createRequire(import.meta.url), exports);
const { ChargingPanel } = exports;
const stations = [1, 2, 3].map(id => ({ id: String(id), code: `SIM-00${id}`, name: `Simulator ${id}`, isDemo: true, statusSource: "OCPP" }));
const session = {
  id: "charge-1", stationId: "1", stationCode: "SIM-001", stationName: "Simulator 1", connectorId: 1,
  status: "Charging", energyKWh: 1.5, powerKW: 22, stateOfCharge: null, temperatureCelsius: null,
  startedAt: "2026-10-01T12:00:00Z", stoppedAt: null, lastMeasurementAt: null, cost: null, currency: "EUR", isDemo: true,
};
const render = (state, language = "sq") => renderToStaticMarkup(createElement(ChargingPanel, { stations, state, connected: true, unavailable: false, language }));

test("QR bypass offers all three configured demo points and shows separate simultaneous charges", () => {
  const html = render({ canTestSimulator: true, sessions: [session, { ...session, id: "charge-2", stationId: "2", stationCode: "SIM-002", stationName: "Simulator 2", powerKW: 11 }] });
  assert.match(html, /Lidhu për test/);
  assert.match(html, /SIM-003/);
  assert.match(html, /Përditësime aktive/);
  assert.equal((html.match(/class="charging-session is-charging"/g) || []).length, 2);
  assert.match(html, /22/);
  assert.match(html, /11/);
  assert.doesNotMatch(html, /BATERIA|TEMPERATURA/);
});

test("completed charge keeps a summary with cost and reported battery data", () => {
  const html = render({ canTestSimulator: true, sessions: [{ ...session, status: "Stopped", stoppedAt: "2026-10-01T12:05:00Z", powerKW: 0, cost: 0.53, stateOfCharge: 45 }] });
  assert.match(html, /Karikimi përfundoi/);
  assert.match(html, /U ruajt në historikun tënd/);
  assert.match(html, /5:00/);
  assert.match(html, /BATERIA/);
  assert.match(html, /EUR/);
});

test("test controls stay hidden for accounts outside the configured demo", () => {
  assert.equal(render({ canTestSimulator: false, sessions: [] }), "");
  const html = render({ canTestSimulator: false, sessions: [session] }, "en");
  assert.match(html, /Follow your charge/);
  assert.doesNotMatch(html, /Connect for testing|Test charge point/);
});

test("phase energy and clock diagnostics appear during a live session", () => {
  const html = render({ canTestSimulator: true, sessions: [{ ...session,
    status: "AwaitingStatus", energyKWh: 6.906, powerKW: null, energyScope: "L1",
    deviceClockSkewed: true, startReceivedAt: "2026-10-01T13:08:00Z",
    lastMeasurementAt: "2026-10-01T12:20:00Z", lastMeasurementReceivedAt: "2026-10-01T13:08:20Z",
  }] }, "en");
  assert.match(html, /6.906/);
  assert.match(html, /ENERGY · L1/);
  assert.match(html, /Device connected · awaiting status/);
  assert.match(html, /has not reported current power/);
  assert.match(html, /Set the simulator Start time to the current time/);
  assert.match(html, /Last measurement received/);
  assert.doesNotMatch(html, /Device disconnected|68:00/);
});

test("receipt times prevent an old simulator clock inflating the duration", () => {
  const html = render({ canTestSimulator: true, sessions: [{ ...session,
    status: "Stopped", stoppedAt: "2026-10-01T12:05:00Z",
    startReceivedAt: "2026-10-01T13:08:00Z", stopReceivedAt: "2026-10-01T13:10:00Z",
    energyKWh: null,
  }] }, "en");
  assert.match(html, /2:00/);
  assert.doesNotMatch(html, /5:00|68:00|NaN/);
  assert.match(html, /— <small>kWh/);
});
