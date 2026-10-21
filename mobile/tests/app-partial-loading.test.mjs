import test from "node:test";
import assert from "node:assert/strict";
import { compile, hooks, jsx, nodes } from "./helpers/compile.mjs";

const tick = async () => { for (let i = 0; i < 30; i++) await Promise.resolve(); };
const user = { id: "driver", role: "DRIVER", language: "sq", version: 1, pushEnabled: false };
const defaults = path => path === "/me" ? user : path === "/driver/charging-sessions" ? { sessions: [{ id: "session", stationId: "station", connectorId: 1 }] } : [{ id: path }];
const find = (tree, type) => nodes(tree, node => node.type === type);
async function harness(response) {
  const state = hooks(), effects = [];
  let captureEffects = true, poll;
  const api = { request: response, readSession: async () => ({ token: "token", expiresAt: "2099-01-01T00:00:00Z" }), clearSession: async () => {}, saveSession: async () => {}, subscribeSessionExpiry: () => () => {} };
  const dependencies = {
    react: { ...state.react, useEffect: fn => { if (captureEffects) effects.push(fn); } },
    "react/jsx-runtime": jsx,
    "react-native": Object.fromEntries(["ActivityIndicator", "Pressable", "StatusBar", "Text", "View"].map(name => [name, name])),
    "react-native-safe-area-context": { SafeAreaProvider: "SafeAreaProvider", SafeAreaView: "SafeAreaView" },
    "expo-notifications": {},
    "./src/api": api,
    "./src/driver-data": compile("../../src/driver-data.ts", { "./api": api }),
    "./src/use-foreground-poll": { useForegroundPoll: callback => { poll = callback; } },
    "./src/push": { pushAvailable: false },
    "./src/wait-for-api": { waitForMobileApi: async () => true },
    "./src/i18n": compile("../../src/i18n.ts"),
    "./src/ui": { ...Object.fromEntries(["Brand", "Button", "Notice", "Screen"].map(name => [name, name])), styles: {}, colors: {} },
  };
  for (const name of ["AuthScreen", "ProfileScreen", "SettingsScreen", "StationsScreen", "StationChargingScreen", "ActiveChargingShortcut", "BottomNavigation", "HistoryScreen", "NotificationsScreen"]) dependencies[`./src/${name}`] = { [name]: name };
  const App = compile("../../App.tsx", dependencies).default;
  const Content = nodes(App(), node => typeof node.type === "function")[0].type;
  const render = () => { state.reset(); return Content(); };
  render(); captureEffects = false;
  const cleanup = effects[0](); // Real startup flow with controlled storage and network.
  await tick();
  return { render, refresh: async () => { await poll(new AbortController().signal); await tick(); }, cleanup };
}

test("cold start reveals stations and charging while the inbox is still pending", async () => {
  let finishInbox;
  const app = await harness(path => path === "/me/notifications" ? new Promise(resolve => { finishInbox = resolve; }) : Promise.resolve(defaults(path)));
  let tree = app.render();
  assert.equal(find(tree, "StationsScreen").length, 1);
  assert.equal(find(tree, "ActiveChargingShortcut")[0].props.stale, false);
  find(tree, "Pressable")[0].props.onPress();
  tree = app.render();
  assert.equal(find(tree, "NotificationsScreen").length, 0);
  assert.equal(find(tree, "ActivityIndicator").length, 1);
  finishInbox([]); await tick();
  assert.equal(find(app.render(), "NotificationsScreen").length, 1);
  app.cleanup();
});

test("an initial inbox failure shows retry instead of an empty inbox and recovers", async () => {
  let fail = true;
  const app = await harness(async path => {
    if (fail && path === "/me/notifications") throw new Error("service_unavailable");
    return defaults(path);
  });
  let tree = app.render();
  assert.equal(find(tree, "StationsScreen")[0].props.stale, false);
  find(tree, "Pressable")[0].props.onPress();
  tree = app.render();
  assert.equal(find(tree, "NotificationsScreen").length, 0);
  assert.ok(find(tree, "Notice").some(node => node.props.text));
  fail = false;
  find(tree, "Button")[0].props.onPress(); await tick();
  assert.deepEqual(find(app.render(), "NotificationsScreen")[0].props.notifications, defaults("/me/notifications"));
  app.cleanup();
});

test("a failed overview refresh does not block the independent history screen or fresh stations", async () => {
  let fail = false;
  const app = await harness(async path => {
    if (fail && ["/driver/history", "/driver/charging-sessions"].includes(path)) throw new Error("network_error");
    return defaults(path);
  });
  fail = true; await app.refresh();
  let tree = app.render();
  assert.equal(find(tree, "StationsScreen")[0].props.stale, false);
  assert.equal(find(tree, "ActiveChargingShortcut")[0].props.stale, true);
  find(tree, "BottomNavigation")[0].props.onSelect("history");
  tree = app.render();
  assert.equal(find(tree, "HistoryScreen")[0].props.token, "token");
  fail = false; await app.refresh();
  tree = app.render();
  assert.equal(find(tree, "ActiveChargingShortcut")[0].props.stale, false);
  assert.equal(find(tree, "Notice").length, 0);
  app.cleanup();
});
