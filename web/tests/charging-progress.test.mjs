import test from "node:test";
import assert from "node:assert/strict";
import { loadTypescript } from "./helpers/load-typescript.mjs";
import { createElement } from "react";
import { renderToStaticMarkup } from "react-dom/server";

function compile(path, dependencies = {}) {
  return loadTypescript(new URL(path, import.meta.url), { "../../components/runtime-features": { useTestMode: () => true }, "../../../../shared/charging-guidance": loadTypescript(new URL("../../shared/charging-guidance.ts", import.meta.url)), ...dependencies });
}

const fault = compile("../src/features/driver/charging-fault.tsx");
const diagnostics = compile("../src/features/driver/simulator-diagnostics.tsx");
const report = compile("../src/features/driver/charging-test-report.ts");
const availability = compile("../src/features/driver/station-availability.tsx", {
  "@/lib/availability": compile("../src/lib/availability.ts"),
});
const estimate = compile("../../shared/charging-estimate.ts");
const progress = compile("../src/features/driver/charging-progress.tsx", { "./charging-fault": fault, "./charging-estimate": estimate });
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

test("production station view hides test actions and diagnostics even in a stale test snapshot", () => {
  const live = { station: { id: "station-1", code: "CP-001", name: "Station One", address: "Address", city: "Tirana", connectorType: "CCS2", isDemo: false, powerKW: 22, pricePerKWh: .35, currency: "EUR" },
    canConnect: true, inUse: false, deviceConnected: true, connectionRequest: null, session: null,
    driverAccessMode: "Card", diagnostics: { samples: [], lastCommand: { action: "Heartbeat", receivedAt: "2026-10-15T10:00:00Z" } } };
  const { StationChargingView } = compile("../src/features/driver/station-charging-view.tsx", {
    "../../components/runtime-features": { useTestMode: () => false },
    "next/link": { default: ({ href, children }) => createElement("a", { href }, children) },
    "./use-live-snapshot": { useLiveSnapshot: () => ({ snapshot: live, setSnapshot: () => {}, connected: true, unavailable: false }) },
    "./charging-progress": progress, "./charging-fault": fault, "./simulator-diagnostics": diagnostics,
    "./charging-test-report": report, "./station-availability": availability,
    "./live-update-policy": compile("../src/features/driver/live-update-policy.ts"),
  });
  const html = renderToStaticMarkup(createElement(StationChargingView, { initial: live, language: "en" }));
  assert.match(html, /Identify yourself with your linked card/);
  assert.doesNotMatch(html, /Heartbeat|Download diagnostics|Connect to charger|simulator|QR is bypassed/);
});

test("missing battery level and remaining time never produce an invented percentage or countdown", () => {
  const html = render();
  assert.match(html, /2.75/);
  assert.match(html, /0.96/);
  assert.match(html, /2 min 00 s/);
  assert.match(html, /No battery percentage reading received yet/);
  assert.match(html, /charge-progress-indeterminate/);
  assert.doesNotMatch(html, /aria-valuenow|~\d|62:00|NaN/);
  assert.match(html, /Battery level/);
  assert.match(html, /TIME UNTIL BATTERY IS FULL/);
  assert.match(html, /Add battery capacity in your profile/);
});

test("charging time advances in minutes while the start date and time stay fixed", () => {
  for (const language of ["sq", "en"]) {
    const html = render({}, { language });
    assert.match(html, /<time dateTime="2026-10-01T13:00:00Z">01\/10\/2026 · 15:00<\/time>/);
    assert.match(html, /01\/10\/2026/);
    assert.match(html, language === "sq" ? /KOHA E SEANCËS/ : /SESSION TIME/);
    assert.match(html, /<strong>2 min 00 s<\/strong>/);
    assert.match(render({}, { language, now: Date.parse("2026-10-01T13:02:01Z") }), /<strong>2 min 01 s<\/strong>/);
    const later = render({}, { language, now: Date.parse("2026-10-03T13:02:00Z") });
    assert.match(later, /<time dateTime="2026-10-01T13:00:00Z">01\/10\/2026 · 15:00<\/time>/);
    assert.match(later, /<strong>2882 min 00 s<\/strong>/);
  }
  assert.match(render({ isDemo: false }), /<time dateTime="2026-10-01T12:00:00Z">01\/10\/2026 · 14:00<\/time>/);
  assert.match(render({ startReceivedAt: "2026-12-01T13:00:00Z" }), /<time dateTime="2026-12-01T13:00:00Z">01\/12\/2026 · 14:00<\/time>/);
  const invalid = render({ startReceivedAt: "invalid" });
  assert.match(invalid, /Start time is unavailable/);
  assert.doesNotMatch(invalid, /Invalid Date|NaN|<time/);
});

test("reported state of charge drives the accessible progress bar", () => {
  const html = render({ stateOfCharge: 45, powerKW: 22, stateOfChargeValidUntil: "2026-10-01T13:07:00Z" });
  assert.match(html, /aria-valuenow="45"/);
  assert.match(html, /width:45%/);
  assert.match(html, /Battery level reported by the charger/);
  assert.match(html, /22/);
  assert.doesNotMatch(render({ stateOfCharge: 101 }), /aria-valuenow/);
});

test("disconnected or expired battery readings stay visible as historical data without a live estimate", () => {
  for (const language of ["sq", "en"]) {
    const html = render({ status: "ConnectionLost", stateOfCharge: 43, powerKW: 22, batteryCapacityKWh: 77,
      stateOfChargeValidUntil: "2026-10-01T13:07:00Z", powerValidUntil: "2026-10-01T13:04:00Z", powerScope: "Total" }, { language });
    assert.match(html, /aria-valuenow="43"/);
    assert.match(html, /background:#96a79d/);
    assert.match(html, language === "sq" ? /Përqindja e fundit · në pritje të përditësimit/ : /Last battery level · awaiting an update/);
    assert.match(html, language === "sq" ? /Koha e seancës përfshin edhe këtë pritje/ : /Session time includes this wait/);
    assert.match(html, /15:01:00/);
    assert.doesNotMatch(html, /≈ |AM|PM|p\.d\.|m\.d\./);
  }
  const expired = render({ stateOfCharge: 43, stateOfChargeValidUntil: "2026-10-01T13:01:00Z" });
  assert.match(expired, /Last battery level · awaiting an update/);
  const ended = render({ stateOfCharge: 43, stoppedAt: "2026-10-01T13:02:00Z" });
  assert.match(ended, /aria-valuenow="43"/);
  assert.doesNotMatch(ended, /awaiting an update/);
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
  assert.match(html, /<strong>3 min 00 s<\/strong>/);
  assert.match(render(changes, { now: Date.parse("2026-10-03T13:02:00Z") }), /<strong>3 min 00 s<\/strong>/);
  assert.match(html, /Saved in history/);
  const missing = render({ ...changes, energyKWh: null, cost: null });
  assert.doesNotMatch(missing, /Saved in history|has been saved/);
  assert.match(missing, /valid final reading and tariff/);
});

test("real charger duration uses the physical start even when app association or receipt is later", () => {
  const html = render({ isDemo: false });
  assert.match(html, /62 min 00 s/);
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
    "./charging-test-report": report, "./station-availability": availability,
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

test("assigned physical charger shows idle signals without a Connect action and supports connector selection", () => {
  const live = { station: { id: "physical-1", code: "CP-001", name: "Real charger", address: "Address", city: "Tirana", connectorType: "CCS2", connectorCount: 2, isDemo: false, powerKW: 22, pricePerKWh: .35, currency: "EUR" },
    canConnect: false, driverAccessMode: "Demo", inUse: false, deviceConnected: true, connectionRequest: null, session: null,
    connector: { number: 2, status: "Available" }, diagnostics: { samples: [], lastCommand: { action: "Heartbeat", accepted: true, receivedAt: "2026-10-05T10:00:00Z", version: 1 } } };
  const { StationChargingView } = compile("../src/features/driver/station-charging-view.tsx", {
    "next/link": { default: ({ href, children }) => createElement("a", { href }, children) },
    "./use-live-snapshot": { useLiveSnapshot: (_initial, url) => { assert.match(url, /connector=2$/); return { snapshot: live, setSnapshot: () => {}, connected: true, unavailable: false }; } },
    "./charging-progress": progress, "./charging-fault": fault, "./simulator-diagnostics": diagnostics,
    "./charging-test-report": report, "./station-availability": availability,
    "./live-update-policy": compile("../src/features/driver/live-update-policy.ts"),
  });
  const html = renderToStaticMarkup(createElement(StationChargingView, { initial: live, language: "en" }));
  assert.match(html, /Charger signals/);
  assert.match(html, /Heartbeat/);
  assert.match(html, /assigned to you automatically/);
  assert.match(html, /href="\/driver\/stations\/physical-1\?connector=1"/);
  assert.doesNotMatch(html, /Connect to charger|only for test charge points|Open automatic simulator/);
});

test("100 percent reports battery level without inventing a stop or completion time", () => {
  const html = render({ stateOfCharge: 100 });
  assert.match(html, /aria-valuenow="100"/);
  assert.match(html, /Charging/);
  assert.doesNotMatch(html, /Charge completed|Saved in history|≈ \d/i);
});

test("remaining time renders an estimate and changes on new battery, power and profile data", () => {
  const valid = { batteryCapacityKWh: 60, stateOfCharge: 45, powerKW: 22, powerScope: "Total",
    powerValidUntil: "2026-10-01T13:04:00Z", stateOfChargeValidUntil: "2026-10-01T13:07:00Z" };
  assert.match(render(valid), /≈ 1 h 30 min/);
  assert.match(render({ ...valid, stateOfCharge: 56 }), /≈ 1 h 12 min/);
  assert.match(render(valid, { language: "sq" }), /Vlerësim me fuqinë aktuale/);
  assert.match(render({ ...valid, powerScope: "L1" }), /A total charging power reading is needed/);
  for (const status of ["PausedByVehicle", "Faulted", "ConnectionLost"]) {
    assert.doesNotMatch(render({ ...valid, status }), /≈ \d/);
  }
  const full = render({ ...valid, stateOfCharge: 100, powerKW: 0 });
  assert.match(full, /≈ 0 min/);
  assert.doesNotMatch(full, /Charge completed|Saved in history/);
});

test("elapsed minutes do not wrap after an hour or day and reject invalid timestamps", () => {
  assert.equal(formatChargingDuration(59), "0 min 59 s");
  assert.equal(formatChargingDuration(3661), "61 min 01 s");
  assert.equal(formatChargingDuration(86461), "1441 min 01 s");
  assert.equal(formatChargingDuration(172861), "2881 min 01 s");
  assert.equal(formatChargingDuration(-10), "0 min 00 s");
  assert.equal(formatChargingDuration(NaN), "—");
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
  assert.ok(html.indexOf("Active transaction: 17") < html.indexOf("<details"));
  assert.match(html, /Use this ID in MeterValues/);
  assert.doesNotMatch(renderDiagnostics({ lastCommand: lastFailure, lastFailure: null }), /role="alert"/);
});

test("an acknowledged power sample with an energy unit explains why power did not change", () => {
  const invalid = { ...sample, measurand: "Power.Active.Import", value: "22", unit: "Wh" };
  const command = { action: "MeterValues", accepted: true, failureCode: null, description: null,
    receivedAt: sample.receivedAt, readings: [invalid] };
  const html = renderDiagnostics({ lastCommand: command });
  assert.ok(html.indexOf("The power reading was not used") < html.indexOf("<details"));
  assert.match(html, /unit Wh/);
  assert.match(html, /kW for a value of 22, or W for 22000/);
  assert.match(html, /confirms receipt of the message/);
  assert.doesNotMatch(html, /MeterValues was rejected/);
  for (const unit of ["W", "kW"]) {
    assert.doesNotMatch(renderDiagnostics({ samples: [{ ...invalid, unit }], lastCommand: command }), /power reading was not used/);
  }
  assert.doesNotMatch(renderDiagnostics({ samples: [{ ...invalid, format: "SignedData" }] }), /power reading was not used/);
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
    "./charging-test-report": report, "./station-availability": availability,
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

test("another driver's charge page hides private cost, energy, battery and diagnostics even with a stale personal summary", () => {
  const live = { station: { id: "station-1", code: "SIM-001", name: "Station One", status: "Charging", address: "Address", city: "Tirana", connectorType: "CCS2", powerKW: 22, pricePerKWh: .35, currency: "EUR" },
    canConnect: true, inUse: true, deviceConnected: true, connectionRequest: null,
    session: { ...session, stateOfCharge: 45, cost: 123.45, energyKWh: 987.65 }, diagnostics: { externalSessionId: "secret-transaction", samples: [] } };
  const { StationChargingView } = compile("../src/features/driver/station-charging-view.tsx", {
    "next/link": { default: ({ href, children }) => createElement("a", { href }, children) },
    "./use-live-snapshot": { useLiveSnapshot: () => ({ snapshot: live, setSnapshot: () => {}, connected: true, unavailable: false }) },
    "./charging-progress": progress, "./charging-fault": fault, "./simulator-diagnostics": diagnostics,
    "./charging-test-report": report, "./station-availability": availability,
    "./live-update-policy": compile("../src/features/driver/live-update-policy.ts"),
  });
  const html = renderToStaticMarkup(createElement(StationChargingView, { initial: live, language: "en" }));
  assert.match(html, /Another driver is using this charger/);
  assert.match(html, /TIME REMAINING/);
  assert.doesNotMatch(html, /123.45|987.65|secret-transaction|Battery level|ESTIMATED COST|Download diagnostics|Connect to charger/);
});

test("confirmed vehicle unplug shows closure and released association without treating a pause as completion", () => {
  const html = render({ status: "Completed", stopReason: "EVDisconnected", stoppedAt: "2026-10-01T13:03:00Z" });
  assert.match(html, /vehicle was unplugged/);
  assert.match(html, /account association was released/);
  assert.doesNotMatch(render({ status: "PausedByVehicle", stopReason: "EVDisconnected" }), /account association was released/);
});

