import test from "node:test";
import assert from "node:assert/strict";
import { compile } from "./helpers/compile.mjs";

const geometry = compile("../../../shared/map-geometry.ts");
const { initialMapView, fitMobileMap } = compile("../../src/map-camera.ts", { "../../shared/map-geometry": geometry });
const { mapScene, mapTranslation } = compile("../../src/map-scene.ts", { "../../shared/map-geometry": geometry });
const tirana = { latitude: 41.3275, longitude: 19.8187 };
const berlin = { latitude: 52.52, longitude: 13.405 };
const tick = async () => { for (let i = 0; i < 15; i++) await Promise.resolve(); };
function assertVisible(point, view, width = 360, height = 360) {
  const at = geometry.toWorldPixels(point, view.zoom), center = geometry.toWorldPixels(view.center, view.zoom);
  assert.ok(Math.abs(at.x - center.x) < width / 2 - 44);
  assert.ok(Math.abs(at.y - center.y) < height / 2 - 44);
}

test("initial camera centers on the driver when a nearby charger fits, ignoring remote chargers", () => {
  const near = { latitude: tirana.latitude + .0001, longitude: tirana.longitude };
  const view = initialMapView([berlin, near], tirana, 360, 360);
  assert.deepEqual(view.center, tirana); assert.equal(view.zoom, 15); assertVisible(near, view);
});

test("initial camera fits both the driver abroad and the nearest charger, even below zoom 6", () => {
  const newYork = { latitude: 40.7128, longitude: -74.006 };
  const view = initialMapView([tirana], newYork, 360, 360);
  assert.ok(view.zoom < 6); assertVisible(newYork, view); assertVisible(tirana, view);
  const near = { latitude: 52.1, longitude: 13.4 };
  const local = initialMapView([tirana, near], berlin, 360, 360);
  assertVisible(berlin, local); assertVisible(near, local); assert.ok(local.zoom > view.zoom);
  assert.equal(geometry.pinchMapView(view, 360, 360, { x: 180, y: 180 }, { x: 180, y: 180 }, 1, true, 0).zoom, view.zoom);
});

test("denied or invalid location fits the chargers and missing chargers keeps the driver's location", () => {
  for (const location of [undefined, null, { latitude: NaN, longitude: 0 }]) {
    const view = initialMapView([tirana, { latitude: NaN, longitude: 0 }], location, 360, 360);
    assertVisible(tirana, view); assert.equal(view.zoom, 15);
  }
  assert.deepEqual(initialMapView([], berlin, 360, 360), { center: berlin, zoom: 15 });
  assert.ok(Number.isFinite(fitMobileMap([], 360, 360).zoom));
});

test("native pan retains tile coordinates, translates tiles and markers together, and loads only new visible tiles", () => {
  const start = { center: tirana, zoom: 15.3 };
  const world = geometry.toWorldPixels(tirana, start.zoom);
  const next = { ...start, center: geometry.fromWorldPixels({ x: world.x - 320, y: world.y - 70 }, start.zoom) };
  const first = mapScene(start, tirana, 360, 360), second = mapScene(next, tirana, 360, 360);
  assert.notEqual(first.key, second.key);
  const translation = mapTranslation(next, tirana);
  assert.ok(Math.abs(translation.x - 320) < .001); assert.ok(Math.abs(translation.y - 70) < .001);
  for (const tile of first.tiles) {
    const same = second.tiles.find(item => item.x === tile.x && item.y === tile.y);
    if (same) { assert.ok(Math.abs(same.left - tile.left) < .001); assert.ok(Math.abs(same.top - tile.top) < .001); }
  }
  assert.equal(second.zoom, 15);
  assert.ok(second.tiles.every(tile => tile.left + translation.x <= 360 && tile.left + translation.x + second.size >= 0));
});

function locator(overrides = {}) {
  const calls = [];
  const permissions = { granted: true, canAskAgain: true };
  const api = { Accuracy: { Balanced: 3 },
    getForegroundPermissionsAsync: async () => { calls.push("permission"); return permissions; },
    requestForegroundPermissionsAsync: async () => { calls.push("request"); return permissions; },
    hasServicesEnabledAsync: async () => true,
    getCurrentPositionAsync: async options => { calls.push(options); return { coords: { ...tirana, accuracy: 100 } }; },
    ...overrides,
  };
  return { calls, ...compile("../../src/map-location.ts", { "expo-location": api, "../../shared/map-geometry": geometry }) };
}

test("location requests coalesce, use foreground balanced accuracy and retain only coordinates in session memory", async () => {
  const { calls, readInitialLocation } = locator(), session = {};
  const first = readInitialLocation(session), second = readInitialLocation(session);
  assert.equal(first, second); assert.deepEqual(await first, tirana);
  assert.deepEqual(session.location, tirana);
  assert.deepEqual(calls, ["permission", { accuracy: 3, mayShowUserSettingsDialog: false }]);
  await readInitialLocation(session); assert.equal(calls.length, 2);
});

test("permission denial, disabled services, invalid coordinates and native errors keep the map available", async () => {
  for (const overrides of [
    { getForegroundPermissionsAsync: async () => ({ granted: false, canAskAgain: false }) },
    { getForegroundPermissionsAsync: async () => ({ granted: false, canAskAgain: true }), requestForegroundPermissionsAsync: async () => ({ granted: false }) },
    { hasServicesEnabledAsync: async () => false },
    { getCurrentPositionAsync: async () => ({ coords: { latitude: 100, longitude: 0 } }) },
    { getCurrentPositionAsync: async () => { throw Error("location unavailable"); } },
  ]) assert.equal(await locator(overrides).readInitialLocation({}), null);
});

test("a slow location fix times out and a late result cannot replace the fallback", async t => {
  const originals = [globalThis.setTimeout, globalThis.clearTimeout]; let expire, resolveFix, cleared = false;
  globalThis.setTimeout = (callback, delay) => { assert.equal(delay, 12000); expire = callback; return 17; };
  globalThis.clearTimeout = value => { assert.equal(value, 17); cleared = true; };
  t.after(() => { [globalThis.setTimeout, globalThis.clearTimeout] = originals; });
  const { readInitialLocation } = locator({ getCurrentPositionAsync: () => new Promise(resolve => { resolveFix = resolve; }) });
  const session = {}, request = readInitialLocation(session); await tick(); expire();
  assert.equal(await request, null); assert.equal(cleared, true);
  resolveFix({ coords: tirana }); await tick(); assert.equal(session.location, null);
});
