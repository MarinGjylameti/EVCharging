import test from "node:test";
import assert from "node:assert/strict";
import { compile, hooks, jsx, nodes } from "./helpers/compile.mjs";

const availability = compile("../../../shared/availability.ts");
const native = { Text: "Text", View: "View", Pressable: "Pressable", StyleSheet: { create: value => value } };
const forecast = now => ({ connectorId: 2, observedAt: new Date(now).toISOString(), estimatedAvailableAt: new Date(now + 40 * 60000).toISOString(), validUntil: new Date(now + 120000).toISOString(), source: "Calculated" });

for (const platform of ["ios", "android"]) test(`${platform}: public station estimate appears only in details and expires without exposing another driver's session`, async t => {
  const original = Date.now; let now = Date.parse("2026-10-18T12:00:00Z"); Date.now = () => now; t.after(() => { Date.now = original; });
  const state = hooks(); let poll;
  const { StationAvailability } = compile("../../src/StationAvailability.tsx", {
    react: state.react, "react/jsx-runtime": jsx, "react-native": { ...native, Platform: { OS: platform } },
    "../../shared/availability": availability, "./use-foreground-poll": { useForegroundPoll: callback => { poll = callback; } },
  });
  const station = { status: "Charging", availabilityForecast: forecast(now) };
  const render = (value = station, stale = false) => { state.reset(); return StationAvailability({ station: value, language: "en", stale }); };
  let output = JSON.stringify(render());
  assert.match(output, /About 40 min remaining/); assert.match(output, /Connector/); assert.match(output, /battery is full/);
  assert.doesNotMatch(JSON.stringify(render(station, true)), /About .* min remaining/);
  now += 60000; await poll(); assert.match(JSON.stringify(render()), /About 39 min remaining/);
  now += 60000; await poll(); assert.doesNotMatch(JSON.stringify(render()), /About .* min remaining/);
  assert.match(JSON.stringify(render()), /fresh estimate/);
  assert.equal(render({ ...station, status: "Available" }), null);
  assert.match(JSON.stringify(render({ status: "Charging" })), /fresh estimate/);
  assert.match(JSON.stringify(render({ ...station, availabilityForecast: { ...forecast(now), source: "Simulator" } })), /simulation target/);
});

test("the selected station card exposes the public estimate; collapsed list cards stay compact", () => {
  const data = compile("../../src/station-data.ts", { "../../shared/map-directions": compile("../../../shared/map-directions.ts") });
  const { StationSummaryCard } = compile("../../src/StationSummaryCard.tsx", {
    "react/jsx-runtime": jsx, "react-native": native, "./station-data": data, "./i18n": compile("../../src/i18n.ts"),
    "./StationAvailability": { StationAvailability: "StationAvailability" }, "./ui": { colors: {}, styles: {}, Button: "Button" },
  });
  const station = { status: "Charging", name: "Station", pricePerKWh: .35, currency: "EUR", availabilityForecast: forecast(Date.now()) };
  const props = { station, language: "sq", stale: false, onOpen() {}, onNavigate() {} };
  assert.equal(nodes(StationSummaryCard({ ...props, actionsVisible: false }), n => n.type === "StationAvailability").length, 0);
  const detail = nodes(StationSummaryCard(props), n => n.type === "StationAvailability")[0];
  assert.equal(detail.props.station, station);
  assert.equal(detail.props.stale, false);
});
