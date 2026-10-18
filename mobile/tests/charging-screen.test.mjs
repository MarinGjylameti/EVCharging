import test from "node:test";
import assert from "node:assert/strict";
import { compile, hooks, jsx, nodes } from "./helpers/compile.mjs";
const station = { id: "14eedf70-4774-4b4d-808e-8f91dbbc13ce", name: "Station", address: "Address", city: "Tirana", status: "Available", connectorType: "CCS2", connectorCount: 1, powerKW: 50, pricePerKWh: .75, currency: "EUR" };
function screen(read) {
  const state = hooks(); let refresh;
  const { StationChargingScreen } = compile("../../src/StationChargingScreen.tsx", {
    react: state.react, "react/jsx-runtime": jsx, "react-native": { ActivityIndicator: "ActivityIndicator", Text: "Text", View: "View" },
    "./api": { request: read }, "./config": compile("../../src/config.ts"), "./i18n": compile("../../src/i18n.ts"),
    "./station-data": compile("../../src/station-data.ts", { "../../shared/map-directions": compile("../../../shared/map-directions.ts") }), "../../shared/charging-guidance": compile("../../../shared/charging-guidance.ts"),
    "./use-foreground-poll": { useForegroundPoll: (run, interval) => { if (interval === 5000) refresh = run; } },
    "./ui": { Button: "Button", Card: "Card", Chip: "Chip", Notice: "Notice", Screen: "Screen", colors: {}, styles: {} },
  });
  return { render: () => { state.reset(); return StationChargingScreen({ stationId: station.id, token: "test", language: "en", onBack() {} }); }, refresh: () => refresh() };
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

test("a personal session displays its tariff snapshot instead of a later station price", async () => {
  const app = screen(async () => ({ station, deviceConnected: true, canConnect: false, inUse: false, connectionRequest: null,
    session: { id: "session", status: "Charging", stoppedAt: null, startedAt: "2026-10-18T10:00:00Z", unitPricePerKWh: .35, currency: "EUR", energyKWh: 1, powerKW: 22, stateOfCharge: null, cost: .35 } }));
  app.render(); await app.refresh();
  const values = nodes(app.render(), n => n.type === "Text").map(n => [n.props.children].flat().join(""));
  assert.ok(values.includes("0.35 EUR / kWh")); assert.ok(!values.includes("0.75 EUR / kWh"));
});
