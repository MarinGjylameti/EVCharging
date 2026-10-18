import test from "node:test";
import assert from "node:assert/strict";
import { compile, hooks, jsx, nodes } from "./helpers/compile.mjs";
const station = { id: "14eedf70-4774-4b4d-808e-8f91dbbc13ce", name: "Station", address: "Address", city: "Tirana", status: "Available", connectorType: "CCS2", connectorCount: 1, powerKW: 50, pricePerKWh: .75, currency: "EUR" };
function screen(read, options = {}) {
  const state = hooks(); let refresh, clock;
  const { StationChargingScreen } = compile("../../src/StationChargingScreen.tsx", {
    react: state.react, "react/jsx-runtime": jsx, "react-native": { ActivityIndicator: "ActivityIndicator", Pressable: "Pressable", Text: "Text", View: "View" },
    "./api": { request: read }, "./config": compile("../../src/config.ts"), "./i18n": compile("../../src/i18n.ts"),
    "./station-data": compile("../../src/station-data.ts", { "../../shared/map-directions": compile("../../../shared/map-directions.ts") }), "../../shared/charging-guidance": compile("../../../shared/charging-guidance.ts"),
    "./ChargingProgressCard": { ChargingProgressCard: "ChargingProgressCard" },
    "./use-foreground-poll": { useForegroundPoll: (run, interval, enabled) => { if (interval === 5000) refresh = run; else clock = enabled ? run : undefined; } },
    "./ui": { Button: "Button", Card: "Card", Chip: "Chip", Notice: "Notice", Screen: "Screen", colors: {}, styles: {} },
  });
  return { render: () => { state.reset(); return StationChargingScreen({ stationId: station.id, token: "test", language: "en", onBack() {}, onOpenProfile() {}, ...options }); }, refresh: () => refresh(), tick: () => clock?.() };
}

test("a failed initial station read stops the loading spinner and offers recovery", async () => {
  const app = screen(async () => { throw new Error("service_unavailable"); });
  assert.equal(nodes(app.render(), n => n.type === "ActivityIndicator").length, 1);
  await assert.rejects(app.refresh(), /service_unavailable/);
  const tree = app.render();
  assert.equal(nodes(tree, n => n.type === "ActivityIndicator").length, 0);
  assert.match(JSON.stringify(tree), /could not be loaded/);
  assert.ok(nodes(tree, n => n.type === "Button" && n.props.title === "Try again").length);
});

test("returning to a charge loads its actual connector and manual changes update the shortcut context", async () => {
  const calls = [], changed = [];
  const app = screen(async path => { calls.push(path); return { station: { ...station, connectorCount: 2 }, session: null }; }, { initialConnector: 2, onConnectorChange: value => changed.push(value) });
  app.render(); await app.refresh();
  assert.match(calls[0], /connector=2$/);
  const tree = app.render();
  assert.equal(nodes(tree, n => n.type === "Chip" && n.props.title === "Connector 2")[0].props.selected, true);
  nodes(tree, n => n.type === "Chip" && n.props.title === "Connector 1")[0].props.onPress();
  assert.deepEqual(changed, [1]); app.render(); await app.refresh();
  assert.match(calls.at(-1), /connector=1$/);
});

test("a personal session displays its tariff snapshot instead of a later station price", async () => {
  const app = screen(async () => ({ station, deviceConnected: true, canConnect: false, inUse: false, connectionRequest: null,
    session: { id: "session", status: "Charging", stoppedAt: null, startedAt: "2026-10-18T10:00:00Z", unitPricePerKWh: .35, currency: "EUR", energyKWh: 1, powerKW: 22, stateOfCharge: null, cost: .35 } }));
  app.render(); await app.refresh();
  const values = nodes(app.render(), n => n.type === "Text").map(n => [n.props.children].flat().join(""));
  assert.ok(values.includes("0.35 EUR / kWh")); assert.ok(!values.includes("0.75 EUR / kWh"));
});

test("confirmed completion immediately removes the shortcut; pauses and another driver's session cannot", async () => {
  const ended = [];
  let value = { station, inUse: false, session: { id: "mine", stoppedAt: null, status: "PausedByVehicle" } };
  const app = screen(async () => value, { onSessionEnded: id => ended.push(id) });
  app.render(); await app.refresh(); assert.deepEqual(ended, []);
  value = { ...value, session: { ...value.session, status: "Completed", stoppedAt: "2026-10-18T14:30:00Z" } };
  await app.refresh(); assert.deepEqual(ended, ["mine"]);
  value = { ...value, inUse: true, session: { ...value.session, id: "other-driver" } };
  await app.refresh(); assert.deepEqual(ended, ["mine"]);
});

test("charging card receives fresh time, polling failures and recovery; another driver's session is hidden", async t => {
  const time = Date.parse("2026-10-18T10:00:00Z");
  t.mock.timers.enable({ apis: ["Date"], now: time });
  let failure = false;
  const snapshot = { station, canConnect: false, inUse: false, session: { id: "session", stoppedAt: null, status: "Charging" } };
  const app = screen(async () => { if (failure) throw new Error("service_unavailable"); return snapshot; });
  const card = () => nodes(app.render(), n => n.type === "ChargingProgressCard")[0];
  app.render(); await app.refresh();
  assert.equal(card().props.stale, false);
  t.mock.timers.tick(120_000); await app.tick();
  assert.equal(card().props.now, time + 120_000);
  failure = true; await assert.rejects(app.refresh());
  assert.equal(card().props.stale, true);
  failure = false; await app.refresh();
  assert.equal(card().props.stale, false);
  snapshot.inUse = true; await app.refresh();
  assert.equal(card(), undefined);
});
