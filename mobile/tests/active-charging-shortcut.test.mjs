import test from "node:test";
import assert from "node:assert/strict";
import { compile, jsx, nodes } from "./helpers/compile.mjs";

const { ActiveChargingShortcut } = compile("../../src/ActiveChargingShortcut.tsx", {
  "react/jsx-runtime": jsx,
  "react-native": { Text: "Text", View: "View", Pressable: "Pressable", StyleSheet: { create: value => value } },
  "./ui": { colors: {} },
});
const session = { id: "session", stationId: "point", stationName: "Central", connectorId: 2, status: "Charging", stoppedAt: null };
const render = (sessions, options = {}) => ActiveChargingShortcut({ sessions, language: "sq", currentStationId: null, currentConnector: 1, stale: false, onOpen() {}, ...options });

test("return to charging uses the owned session and its exact connector", () => {
  const opened = [], tree = render([session], { onOpen: value => opened.push(value) });
  const button = nodes(tree, n => n.type === "Pressable")[0];
  assert.match(button.props.accessibilityLabel, /Shko te karikimi: Central, lidhësi 2/);
  button.props.onPress(); assert.deepEqual(opened, [session]);
});

test("shortcut hides on the exact active connector and disappears after confirmed completion", () => {
  assert.equal(render([]), null);
  assert.equal(render([session], { currentStationId: "point", currentConnector: 2 }), null);
  assert.ok(render([session], { currentStationId: "point", currentConnector: 1 }));
  assert.equal(render([{ ...session, stoppedAt: "2026-10-18T12:00:00Z" }]), null);
  assert.equal(render([session], { stale: true }), null);
});

test("pauses, disconnections and faults hide the shortcut; confirmed charging makes it reappear", () => {
  for (const status of ["PausedByVehicle", "PausedByCharger", "ConnectionLost", "Faulted", "Finishing", "Connecting", "Unavailable"]) {
    assert.equal(render([{ ...session, status }]), null);
  }
  assert.ok(render([{ ...session, status: "Charging" }]));
});
