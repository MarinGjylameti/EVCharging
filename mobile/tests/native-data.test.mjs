import test from "node:test";
import assert from "node:assert/strict";
import { createRequire } from "node:module";
import { compile } from "./helpers/compile.mjs";
const config = compile("../../src/config.ts");
const stations = compile("../../src/station-data.ts", { "../../shared/map-directions": compile("../../../shared/map-directions.ts") });
const account = compile("../../src/account-inputs.ts");
const history = compile("../../../shared/history.ts");
const { buildEnvironment } = createRequire(import.meta.url)("../config/build-environment.cjs");
const token = "a".repeat(64), projectId = "11111111-2222-3333-4444-555555555555";

test("mobile API permits local HTTP only during development and rejects paths/credentials", () => {
  assert.equal(config.apiBase(" https://api.example.com/ ", false), "https://api.example.com");
  assert.equal(config.apiBase("http://10.0.2.2:5201", true), "http://10.0.2.2:5201");
  for (const value of [undefined, "http://example.com", "https://api.example.com/v1", "https://user:secret@example.com", "https://example.com?token=x", "https://example.com#x", "file:///x"]) assert.throws(() => config.apiBase(value, false), /configuration/);
});
test("opaque sessions require an unexpired 64-character token", () => {
  const future = { token, expiresAt: "2026-10-09T00:00:00Z" }, now = Date.parse("2026-10-08T00:00:00Z");
  assert.equal(config.validSession(future, now), true);
  for (const value of [null, {}, { ...future, token: "jwt.payload.signature" }, { ...future, expiresAt: "invalid" }, { ...future, expiresAt: "2026-10-08T00:00:00Z" }]) assert.equal(config.validSession(value, now), false);
});
test("station IDs and all status values are handled safely", () => {
  assert.equal(config.isStationId(projectId), true); assert.equal(config.isStationId("../../other"), false);
  assert.equal(stations.stationState("Unknown", "sq").text, "Në pritje të statusit");
  assert.equal(stations.stationState("FutureStatus", "en").text, "Awaiting status");
});
test("station filters combine city, availability, rating and archival", () => {
  const base = { city: "Tirana", status: "Available", powerKW: 22 };
  const items = [{ ...base, id: "1" }, { ...base, id: "2", powerKW: 50 }, { ...base, id: "3", status: "Unknown" }, { ...base, id: "4", isArchived: true }, { ...base, id: "5", city: "Durrës" }];
  assert.deepEqual(stations.selectStations(items, "Tirana", true, "fast").map(item => item.id), ["2"]);
  assert.deepEqual(stations.selectStations(items, "Tirana", true, "ac").map(item => item.id), ["1"]);
});
test("map links honor the selected provider, retain exact coordinates and validate them", () => {
  const station = { name: "Central & 2", latitude: 41.3275, longitude: 19.8187 };
  assert.match(stations.navigationUrls(station, "apple", true).primary, /^https:\/\/maps.apple.com/);
  assert.match(stations.navigationUrls(station, "google", false).primary, /^google.navigation:q=41.327500,19.818700/);
  assert.match(stations.navigationUrls(station, "google", true).primary, /^comgooglemaps:\/\//);
  assert.match(stations.navigationUrls(station, "google", true).fallback, /^https:\/\/www.google.com\/maps/);
  assert.throws(() => stations.navigationUrls({ ...station, latitude: NaN }, "apple", true), /mapUnavailable/);
  assert.throws(() => stations.navigationUrls({ ...station, longitude: 181 }, "google", false), /mapUnavailable/);
});
const session = { stoppedAt: null, status: "Charging", energyKWh: 5, powerKW: 43, stateOfCharge: 75, cost: 1.75, powerValidUntil: "2026-10-08T12:01:00Z", stateOfChargeValidUntil: "2026-10-08T12:01:00Z" };
test("other-driver sessions and previous completed sessions are private", () => {
  assert.equal(stations.ownChargingSession({ session, inUse: true }), null);
  assert.equal(stations.ownChargingSession({ session: { ...session, stoppedAt: "now" }, inUse: false, connectionRequest: {} }), null);
  assert.equal(stations.ownChargingSession({ session, inUse: false }), session);
});
test("charging readings hide expired/stale telemetry without inventing energy or cost", () => {
  const now = Date.parse("2026-10-08T12:00:00Z");
  assert.deepEqual(stations.chargingValues(session, now, false), { energy: 5, power: 43, battery: 75, cost: 1.75 });
  assert.deepEqual(stations.chargingValues(session, now, true), { energy: 5, power: null, battery: null, cost: 1.75 });
  assert.equal(stations.chargingValues(session, now + 60000, false).power, null);
  assert.equal(stations.chargingValues({ ...session, stateOfCharge: 150 }, now, false).battery, null);
  assert.equal(stations.chargingValues({ ...session, status: "PausedByVehicle" }, now, false).power, null);
  assert.equal(stations.chargingValues({ ...session, stoppedAt: "2026-10-08T12:00:00Z" }, now, true).power, 0);
});
test("profile preserves battery capacity and optimistic version across saves", () => {
  const user = { firstName: "Arta", lastName: "Deda", version: 3, batteryCapacityKWh: 77.25 };
  const draft = account.profileDraft(user);
  assert.equal(account.profilePayload(draft, 3).batteryCapacityKWh, 77.25);
  assert.equal(account.profilePayload({ ...draft, batteryCapacityKWh: "77,5" }, 4).batteryCapacityKWh, 77.5);
  assert.equal(account.profilePayload({ ...draft, batteryCapacityKWh: "" }, 4).batteryCapacityKWh, null);
  assert.equal(account.profilePayload(draft, 4).version, 4);
  for (const value of ["0", "-1", "1001", "NaN", "10e2", "77.255"]) assert.throws(() => account.profilePayload({ ...draft, batteryCapacityKWh: value }, 3), /invalid_battery_capacity/);
});
test("native and web history use the same inclusive range, currency split and eight-row pages", () => {
  const items = Array.from({ length: 20 }, (_, i) => ({ id: `${i}`, stationId: i % 2 ? "A" : "B", endedAt: `2026-10-07T${String(i).padStart(2, "0")}:00:00Z`, energyKWh: 12, totalCost: 4, currency: i % 2 ? "EUR" : "ALL" }));
  const filters = { ...history.defaultHistoryFilters, stationId: "A" };
  const filtered = history.filterHistory(items, filters, history.historyRange(items, filters));
  assert.equal(filtered.length, 10); assert.equal(history.historyPage(filtered, 1).items.length, 8); assert.equal(history.historyPage(filtered, 2).items.length, 2);
  assert.deepEqual(history.summarizeHistory(items).costs, { ALL: 40, EUR: 40 });
});
test("release config fails early for missing IDs or insecure origins and leaves local setup optional", () => {
  assert.equal(buildEnvironment({}).profile, "local");
  for (const env of [{ EAS_BUILD_PROFILE: "production" }, { EAS_BUILD_PROFILE: "preview", EXPO_PUBLIC_API_BASE_URL: "http://localhost:5201", EXPO_PUBLIC_EAS_PROJECT_ID: projectId }, { EXPO_PUBLIC_EAS_PROJECT_ID: "houss-project" }]) assert.throws(() => buildEnvironment(env));
  assert.equal(buildEnvironment({ EAS_BUILD_PROFILE: "preview", EXPO_PUBLIC_API_BASE_URL: "https://api.example.com", EXPO_PUBLIC_EAS_PROJECT_ID: projectId }).projectId, projectId);
});

test("charging duration uses received times for demo sessions and stops at completion", () => {
  const now = Date.parse("2026-10-08T12:15:05Z");
  assert.equal(stations.chargingDuration({ startedAt: "2026-10-08T12:00:00Z", stoppedAt: null, isDemo: false }, now), "15:05");
  assert.equal(stations.chargingDuration({ startedAt: "2026-11-08T12:00:00Z", startReceivedAt: "2026-10-08T12:00:00Z", stoppedAt: "2026-11-08T13:30:00Z", stopReceivedAt: "2026-10-08T13:30:00Z", isDemo: true }, now), "1:30:00");
  assert.equal(stations.chargingDuration({ startedAt: "invalid", stoppedAt: null, isDemo: false }, now), "—");
});
test("calendar pickers keep selected dates as calendar dates across DST and validate leap days", () => {
  const { calendarDay, calendarDate } = compile("../../src/calendar-date.ts");
  for (const day of ["2026-03-29", "2026-10-25", "2024-02-29"]) assert.equal(calendarDay(calendarDate(day)), day);
  const fallback = new Date(2026, 9, 8, 12);
  assert.equal(calendarDate("2026-02-29", fallback), fallback);
  assert.equal(calendarDate("invalid", fallback), fallback);
});
