import test from "node:test";
import assert from "node:assert/strict";
import { compile, jsx, nodes } from "./helpers/compile.mjs";

const now = Date.parse("2026-10-18T10:00:00Z");
const session = {
  id: "session", status: "Charging", startedAt: "2026-10-18T09:30:00Z", stoppedAt: null,
  energyKWh: 11, powerKW: 22, powerScope: "Total", batteryCapacityKWh: 60, stateOfCharge: 45,
  powerValidUntil: "2026-10-18T10:02:00Z", stateOfChargeValidUntil: "2026-10-18T10:05:00Z",
  cost: 3.85, currency: "EUR", isDemo: false,
};
function card(platform = "ios") {
  const { ChargingProgressCard } = compile("../../src/ChargingProgressCard.tsx", {
    "react/jsx-runtime": jsx,
    "react-native": { Platform: { OS: platform }, Text: "Text", View: "View", StyleSheet: { create: styles => styles } },
    "../../shared/charging-estimate": compile("../../../shared/charging-estimate.ts"),
    "./station-data": compile("../../src/station-data.ts", { "../../shared/map-directions": compile("../../../shared/map-directions.ts") }),
    "./i18n": compile("../../src/i18n.ts"),
    "./ui": { Button: "Button", Notice: "Notice", colors: {}, styles: {} },
  });
  return (patch = {}, options = {}) => ChargingProgressCard({ session: { ...session, ...patch }, language: "en", now, stale: false, onOpenProfile() {}, ...options });
}
const progress = tree => nodes(tree, n => n.props.accessibilityRole === "progressbar")[0];
const visibleText = tree => JSON.stringify(tree);

for (const platform of ["ios", "android"]) {
  test(`${platform}: real battery percentage and time to full update together from new readings`, () => {
    const render = card(platform);
    const initial = render();
    assert.equal(progress(initial).props.accessibilityValue.now, 45);
    assert.match(visibleText(initial), /≈ 1 h 30 min/);
    const updated = render({ stateOfCharge: 56 });
    assert.equal(progress(updated).props.accessibilityValue.now, 56);
    assert.match(visibleText(updated), /≈ 1 h 12 min/);
    assert.match(visibleText(render({ powerKW: 11 })), /≈ 3 h/);
    const completeBattery = render({ stateOfCharge: 100, powerKW: 0 });
    assert.match(visibleText(completeBattery), /≈ 0 min/);
    assert.doesNotMatch(visibleText(completeBattery), /Charge completed/);
  });
}

test("missing capacity offers the profile action and uses updated session capacity on return", () => {
  let opens = 0; const render = card();
  const tree = render({ batteryCapacityKWh: null }, { language: "sq", onOpenProfile: () => opens++ });
  assert.match(visibleText(tree), /Shto kapacitetin e baterisë në profil/);
  nodes(tree, n => n.type === "Button" && n.props.title === "Plotëso profilin")[0].props.onPress();
  assert.equal(opens, 1);
  assert.match(visibleText(render({ batteryCapacityKWh: 80 })), /≈ 2 h/);
});

test("unknown, invalid and expired battery values never appear as 0% or as numeric progress", () => {
  const render = card();
  for (const stateOfCharge of [null, NaN, -1, 101, Infinity]) {
    const tree = render({ stateOfCharge });
    assert.equal(progress(tree).props.accessibilityValue.now, undefined);
    assert.doesNotMatch(visibleText(tree), /≈ /);
    assert.equal(progress(tree).props.children.props.style.at(-1).width, "0%");
  }
  assert.equal(progress(render({ stateOfCharge: 0 })).props.accessibilityValue.now, 0);
  const expired = render({}, { now: now + 300_000 });
  assert.equal(progress(expired).props.accessibilityValue.now, undefined);
  assert.match(visibleText(expired), /fresh battery percentage/);
});

test("paused, stale, partial-power and expired-power states explain the missing estimate", () => {
  const render = card();
  for (const [patch, options, note] of [
    [{ status: "PausedByVehicle" }, {}, /Estimate resumes/],
    [{ status: "ConnectionLost" }, {}, /Estimate resumes/],
    [{ faultCode: "OverCurrentFailure" }, {}, /Charger reported a fault/],
    [{}, { stale: true }, /Waiting for fresh updates/],
    [{ powerScope: "L1" }, {}, /total charging power/],
    [{}, { now: now + 120_000 }, /fresh power reading/],
  ]) {
    const tree = render(patch, options);
    assert.doesNotMatch(visibleText(tree), /≈ /);
    assert.match(visibleText(tree), note);
  }
});

test("completed sessions keep the reported percentage, remove ETA and do not confirm a disputed final cost", () => {
  const tree = card()({ stoppedAt: "2026-10-18T10:00:00Z", stateOfCharge: 78, completionWarning: "FinalMeterRegression" }, { now: now + 600_000 });
  assert.match(visibleText(tree), /Charge completed/);
  assert.equal(progress(tree).props.accessibilityValue.now, 78);
  assert.doesNotMatch(visibleText(tree), /Time until battery is full|3\.85/);
  assert.match(visibleText(tree), /Final cost has not been confirmed/);
  assert.match(visibleText(tree), /Last reported battery level/);
});
