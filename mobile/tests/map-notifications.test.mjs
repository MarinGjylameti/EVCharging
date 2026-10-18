import test from "node:test";
import assert from "node:assert/strict";
import { compile, hooks, jsx, nodes } from "./helpers/compile.mjs";

const tick = async () => { for (let i = 0; i < 12; i++) await Promise.resolve(); };
const history = compile("../../../shared/history.ts");
const directions = compile("../../../shared/map-directions.ts");
const geometry = compile("../../../shared/map-geometry.ts");
const stationsData = compile("../../src/station-data.ts", { "../../shared/map-directions": directions });
const notificationsData = compile("../../src/notification-data.ts", { "../../shared/history": history });
const ui = Object.fromEntries(["Button", "Card", "ChoiceField", "Chip", "Notice", "Screen"].map(name => [name, name]));
ui.styles = {}; ui.colors = { ink: "green", muted: "gray", border: "gray", white: "white" };
const native = { Text: "Text", View: "View", Pressable: "Pressable", Image: "Image", Platform: { OS: "ios" } };
const i18n = compile("../../src/i18n.ts");
const station = { id: "station", name: "Central", latitude: 41.3275, longitude: 19.8187, status: "Available", city: "Tirana", powerKW: 22, pricePerKWh: .35, currency: "EUR" };

test("map preference survives a new read and invalid values default to Google", async () => {
  let stored = "invalid";
  const preference = compile("../../src/map-preference.ts", { "../../shared/map-directions": directions,
    "expo-secure-store": { getItemAsync: async () => stored, setItemAsync: async (_key, value) => { stored = value; } },
  });
  assert.equal(await preference.readMapProvider(), "google");
  await preference.saveMapProvider("apple"); assert.equal(await preference.readMapProvider(), "apple");
});

test("settings load the saved provider, save changes, and retain it when saving fails", async () => {
  const state = hooks(), saved = []; let effect, fail = false;
  const { NavigationSettings } = compile("../../src/NavigationSettings.tsx", {
    react: { ...state.react, useEffect: fn => { effect = fn; } }, "react/jsx-runtime": jsx, "react-native": native, "./ui": ui,
    "./map-preference": { readMapProvider: async () => "apple", saveMapProvider: async value => { if (fail) throw Error("storage"); saved.push(value); } },
  });
  const render = () => { state.reset(); return NavigationSettings({ language: "en" }); };
  render(); const cleanup = effect(); await tick();
  let tree = render(); assert.equal(nodes(tree, n => n.type === "Chip" && n.props.title === "Apple Maps")[0].props.selected, true);
  nodes(tree, n => n.type === "Chip" && n.props.title === "Google Maps")[0].props.onPress(); await tick(); assert.deepEqual(saved, ["google"]);
  fail = true; tree = render(); nodes(tree, n => n.type === "Chip" && n.props.title === "Apple Maps")[0].props.onPress(); await tick();
  tree = render(); assert.equal(nodes(tree, n => n.type === "Chip" && n.props.title === "Google Maps")[0].props.selected, true);
  assert.match(JSON.stringify(tree), /Could not save/); cleanup();
});

test("map station taps use the saved navigation app, fall back to its website and coalesce taps", async () => {
  const state = hooks(), opened = [];
  const { StationsScreen } = compile("../../src/StationsScreen.tsx", {
    react: state.react, "react/jsx-runtime": jsx, "react-native": { ...native, Linking: { openURL: async url => { opened.push(url); if (url.startsWith("comgooglemaps:")) throw Error("Not installed"); } } },
    "./ui": ui, "./i18n": i18n, "./station-data": stationsData, "./StationMap": { StationMap: "StationMap" }, "./map-preference": { readMapProvider: async () => "google" },
  });
  const render = () => { state.reset(); return StationsScreen({ stations: [station], language: "en", refresh() {}, onSelect() {} }); };
  const tree = render(), map = nodes(tree, n => n.type === "StationMap")[0];
  map.props.onNavigate(station); map.props.onNavigate(station); await tick();
  assert.equal(opened.length, 2); assert.match(opened[0], /^comgooglemaps:/); assert.match(opened[1], /^https:\/\/www.google.com/);
  nodes(tree, n => n.type === "Chip" && n.props.title === "List")[0].props.onPress();
  assert.equal(nodes(render(), n => n.type === "StationMap").length, 0);
});

test("native map renders only valid stations, fits their coordinates and exposes navigation targets", () => {
  const state = hooks(), selected = [];
  const { StationMap } = compile("../../src/StationMap.tsx", {
    react: state.react, "react/jsx-runtime": jsx, "react-native": { ...native, PanResponder: { create: () => ({ panHandlers: {} }) } },
    "../../shared/map-geometry": geometry, "./station-data": stationsData, "./ui": ui,
  });
  const all = [station, { ...station, id: "invalid", latitude: NaN }];
  const render = () => { state.reset(); return StationMap({ stations: all, language: "en", stale: false, onNavigate: value => selected.push(value.id), onInteraction() {} }); };
  let tree = render(); nodes(tree, n => n.type === "View" && n.props.onLayout)[0].props.onLayout({ nativeEvent: { layout: { width: 360 } } }); tree = render();
  const markers = nodes(tree, n => n.type === "Pressable" && n.props.accessibilityLabel?.includes("Central"));
  assert.equal(markers.length, 1); markers[0].props.onPress(); assert.deepEqual(selected, ["station"]);
  const tiles = nodes(tree, n => n.type === "Image"); assert.ok(tiles.length > 0 && tiles.length < 16);
  assert.match(tiles[0].props.source.headers["User-Agent"], /EVCharging/);
  assert.match(JSON.stringify(tree), /OpenStreetMap contributors/);
  assert.match(JSON.stringify(tree), /no valid coordinates/);
});

test("Albania notification dates and 24-hour times handle midnight and daylight saving", () => {
  assert.equal(notificationsData.notificationDay("2026-10-17T22:30:00Z"), "2026-10-18");
  assert.equal(notificationsData.notificationTime("2026-10-17T22:30:00Z"), "00:30");
  assert.equal(notificationsData.notificationTime("2026-01-15T12:43:00Z"), "13:43");
  assert.equal(notificationsData.notificationTime("2026-07-15T12:43:00Z"), "14:43");
  assert.equal(notificationsData.notificationTime("2026-10-25T00:30:00Z"), "02:30");
  assert.equal(notificationsData.notificationTime("2026-10-25T01:30:00Z"), "02:30");
  assert.equal(notificationsData.notificationTime("bad"), "—");
});

test("map gestures pan and pinch without resetting on status refresh, and release page scrolling", () => {
  const state = hooks(), interactions = []; let responder;
  const { StationMap } = compile("../../src/StationMap.tsx", {
    react: state.react, "react/jsx-runtime": jsx, "react-native": { ...native, PanResponder: { create: value => { responder = value; return { panHandlers: {} }; } } },
    "../../shared/map-geometry": geometry, "./station-data": stationsData, "./ui": ui,
  });
  const render = (status = "Available") => { state.reset(); return StationMap({ stations: [{ ...station, status }], language: "en", stale: false, onNavigate() {}, onInteraction: value => interactions.push(value) }); };
  let tree = render(); nodes(tree, n => n.type === "View" && n.props.onLayout)[0].props.onLayout({ nativeEvent: { layout: { width: 360 } } }); tree = render();
  const markerLeft = tree => nodes(tree, n => n.type === "Pressable" && n.props.accessibilityLabel?.includes("Central"))[0].props.style.left;
  const before = markerLeft(tree), touches = x => ({ nativeEvent: { touches: [{ pageX: x, pageY: 100 }] } });
  responder.onPanResponderGrant(touches(100)); responder.onPanResponderMove(touches(130)); tree = render("Occupied");
  assert.ok(Math.abs(markerLeft(tree) - before - 30) < .001);
  responder.onPanResponderRelease(); assert.deepEqual(interactions, [true, false]);
  const pinch = (a, b) => ({ nativeEvent: { touches: [{ pageX: a, pageY: 100 }, { pageX: b, pageY: 100 }] } });
  responder.onPanResponderGrant(pinch(100, 200)); responder.onPanResponderMove(pinch(50, 250));
  tree = render(); assert.match(nodes(tree, n => n.type === "Image")[0].props.source.uri, /\/16\//);
  responder.onPanResponderTerminate(); assert.equal(interactions.at(-1), false);
});

const notifications = Array.from({ length: 12 }, (_, i) => ({ id: String(i), createdAt: `2026-10-${i < 10 ? "18" : "17"}T${String(i).padStart(2, "0")}:00:00Z`, text: { title: "Charge complete", body: "Completed" } }));
test("notifications sort newest first, filter on the Albanian day and clamp pagination", () => {
  const first = notificationsData.notificationPage(notifications, "", 1);
  assert.equal(first.items.length, 8); assert.equal(first.items[0].id, "9");
  assert.equal(notificationsData.notificationPage(notifications, "", 99).page, 2);
  assert.equal(notificationsData.notificationPage(notifications, "2026-10-17", 1).total, 2);
  assert.equal(notificationsData.notificationPage(notifications, "2026-10-19", 1).total, 0);
});

test("notifications display eight cards and selecting a day resets the page", () => {
  const state = hooks();
  const { NotificationsScreen } = compile("../../src/NotificationsScreen.tsx", {
    react: state.react, "react/jsx-runtime": jsx, "react-native": native, "./ui": ui, "./i18n": i18n,
    "../../shared/history": history, "./notification-data": notificationsData,
  });
  const render = () => { state.reset(); return NotificationsScreen({ notifications, language: "en", refresh() {} }); };
  let tree = render(); assert.equal(nodes(tree, n => n.type === "Card").length, 8);
  nodes(tree, n => n.type === "Button" && n.props.title === "Older →")[0].props.onPress(); tree = render();
  assert.equal(nodes(tree, n => n.type === "Card").length, 4);
  nodes(tree, n => n.type === "ChoiceField")[0].props.onChange("2026-10-18"); tree = render();
  assert.equal(nodes(tree, n => n.type === "Card").length, 8);
  assert.match(JSON.stringify(tree), /Albania time/);
});
