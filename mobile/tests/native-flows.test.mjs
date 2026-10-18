import test from "node:test";
import assert from "node:assert/strict";
import { compile, hooks, jsx, nodes } from "./helpers/compile.mjs";
const tick = async () => { for (let i = 0; i < 5; i++) await Promise.resolve(); };

test("foreground polling stops in the background, aborts pending reads and resumes immediately", async t => {
  t.mock.timers.enable({ apis: ["setTimeout"] });
  let effect, listener, removed = false; const signals = [];
  const { useForegroundPoll } = compile("../../src/use-foreground-poll.ts", {
    react: { useRef: value => ({ current: value }), useEffect: run => { effect = run; } },
    "react-native": { AppState: { currentState: "active", addEventListener: (_, fn) => { listener = fn; return { remove: () => { removed = true; } }; } } },
  });
  useForegroundPoll(async signal => { signals.push(signal); }, 5000); const cleanup = effect(); await tick();
  assert.equal(signals.length, 1); listener("background"); assert.equal(signals[0].aborted, true);
  t.mock.timers.tick(15000); await tick(); assert.equal(signals.length, 1);
  listener("active"); await tick(); assert.equal(signals.length, 2);
  t.mock.timers.tick(5000); await tick(); assert.equal(signals.length, 3);
  cleanup(); assert.equal(signals[2].aborted, true); assert.equal(removed, true);
  t.mock.timers.tick(15000); await tick(); assert.equal(signals.length, 3);
});
test("foreground polling never overlaps slow requests", async t => {
  t.mock.timers.enable({ apis: ["setTimeout"] }); let effect, resolve, calls = 0;
  const { useForegroundPoll } = compile("../../src/use-foreground-poll.ts", {
    react: { useRef: value => ({ current: value }), useEffect: run => { effect = run; } },
    "react-native": { AppState: { currentState: "active", addEventListener: () => ({ remove() {} }) } },
  });
  useForegroundPoll(() => { calls++; return new Promise(done => { resolve = done; }); }, 5000);
  const cleanup = effect(); t.mock.timers.tick(20000); assert.equal(calls, 1);
  resolve(); await tick(); t.mock.timers.tick(5000); assert.equal(calls, 2); cleanup(); resolve(); await tick();
});
function pushClient({ ios = false, ownership = "standalone", projectId = "11111111-2222-3333-4444-555555555555", permission = { granted: false, canAskAgain: true }, requested = { granted: true } } = {}) {
  const events = [];
  const notifications = { setNotificationHandler() {}, AndroidImportance: { DEFAULT: 3 }, IosAuthorizationStatus: { PROVISIONAL: 3 },
    setNotificationChannelAsync: async () => events.push("channel"), getPermissionsAsync: async () => { events.push("permission"); return permission; }, requestPermissionsAsync: async () => { events.push("request"); return requested; },
    getExpoPushTokenAsync: async () => { events.push("expo-token"); return { data: "ExponentPushToken[test]" }; },
  };
  const client = compile("../../src/push.ts", { "react-native": { Platform: { OS: ios ? "ios" : "android" } }, "expo-constants": { default: { easConfig: { projectId }, appOwnership: ownership } }, "expo-device": { isDevice: true }, "expo-notifications": notifications,
    "./api": { request: async (...args) => events.push(args) },
  });
  return { ...client, events };
}
test("Android creates its notification channel before requesting permission", async () => {
  const client = pushClient(); await client.registerPush("driver-token", true);
  assert.deepEqual(client.events.slice(0, 4), ["channel", "permission", "request", "expo-token"]);
  assert.deepEqual(client.events[4], ["/me/devices", "driver-token", "POST", { token: "ExponentPushToken[test]", platform: "android" }]);
});
test("provisional iOS notification permissions work without prompting again", async () => {
  const client = pushClient({ ios: true, permission: { granted: false, ios: { status: 3 } } }); await client.registerPush("driver-token", false);
  assert.deepEqual(client.events.slice(0, 2), ["permission", "expo-token"]);
});

test("background push registration respects declined permission without interrupting charging", async () => {
  const client = pushClient({ permission: { granted: false, canAskAgain: false } });
  await client.registerPush("driver-token", false);
  assert.deepEqual(client.events, ["channel", "permission"]);
  const preview = pushClient({ ownership: "expo" });
  await preview.registerPush("driver-token", false);
  assert.deepEqual(preview.events, []);
});
test("push rejects Expo Go and missing project IDs; denied permissions never register a token", async () => {
  for (const settings of [{ ownership: "expo" }, { projectId: "" }]) { const client = pushClient(settings); await assert.rejects(client.registerPush("driver-token", true), /pushSetup/); assert.equal(client.events.length, 0); }
  const client = pushClient({ permission: { granted: false, canAskAgain: false } }); await assert.rejects(client.registerPush("driver-token", true), /pushDenied/);
  assert.deepEqual(client.events, ["channel", "permission"]);
});
const ui = Object.fromEntries(["Button", "Card", "ChoiceField", "Chip", "Field", "Notice", "Screen"].map(name => [name, name]));
ui.styles = {}; ui.colors = { ink: "green", muted: "gray", green: "green", border: "gray" };
const historyData = compile("../../../shared/history.ts"), i18n = compile("../../src/i18n.ts");
const native = { Pressable: "Pressable", ScrollView: "ScrollView", Text: "Text", View: "View" };
function mountHistory(history) {
  const state = hooks();
  const { HistoryScreen } = compile("../../src/HistoryScreen.tsx", { react: state.react, "react/jsx-runtime": jsx, "react-native": native, "../../shared/history": historyData, "./i18n": i18n, "./ui": ui, "./DateField": { DateField: "DateField" } });
  return () => { state.reset(); return HistoryScreen({ history, language: "en", refresh: async () => {} }); };
}
const charges = Array.from({ length: 12 }, (_, i) => ({ id: `${i}`, stationId: i % 2 ? "A" : "B", stationName: `Station ${i % 2 ? "A" : "B"}`, endedAt: "2026-10-07T12:00:00Z", startedAt: "2026-10-07T11:00:00Z", energyKWh: 12, totalCost: 4, currency: "EUR", unitPriceSnapshot: .35 }));
const activity = tree => nodes(tree, item => item.type === "Pressable" && item.props.accessibilityState?.expanded !== undefined);
test("native history limits activities to eight and station filters also update activities", () => {
  const render = mountHistory(charges); let tree = render(); assert.equal(activity(tree).length, 8);
  const next = nodes(tree, item => item.type === "Button" && item.props.title === "Next →")[0]; next.props.onPress(); tree = render(); assert.equal(activity(tree).length, 4);
  const station = nodes(tree, item => item.type === "ChoiceField" && item.props.label === "Station")[0]; station.props.onChange("A"); tree = render(); assert.equal(activity(tree).length, 6);
  assert.ok(activity(tree).every(item => JSON.stringify(item).includes("Station A")));
});
test("native history bar selection filters activities and selecting an empty day shows none", () => {
  const render = mountHistory(charges); let tree = render();
  const empty = nodes(tree, item => item.type === "Pressable" && item.props.accessibilityLabel?.includes("0 kWh, 0 Charges"))[0];
  assert.ok(empty); empty.props.onPress(); tree = render(); assert.equal(activity(tree).length, 0);
  nodes(tree, item => item.type === "Button" && item.props.title === "Show the whole period")[0].props.onPress(); tree = render(); assert.equal(activity(tree).length, 8);
});

test("Android date picker commits a chosen day and dismissing it leaves the filter unchanged", () => {
  const state = hooks(), changes = [];
  const { DateField } = compile("../../src/DateField.tsx", { react: state.react, "react/jsx-runtime": jsx, "react-native": { ...native, Platform: { OS: "android" }, Modal: "Modal" }, "@react-native-community/datetimepicker": { default: "DateTimePicker" }, "react-native-safe-area-context": { useSafeAreaInsets: () => ({ bottom: 0 }) }, "../../shared/history": historyData, "./calendar-date": compile("../../src/calendar-date.ts"), "./ui": ui });
  const render = () => { state.reset(); return DateField({ label: "From", value: "2026-10-01", language: "en", onChange: day => changes.push(day) }); };
  let tree = render(); nodes(tree, item => item.type === "Pressable")[0].props.onPress(); tree = render();
  nodes(tree, item => item.type === "DateTimePicker")[0].props.onChange({ type: "dismissed" }, new Date(2026, 9, 7));
  assert.deepEqual(changes, []);
  tree = render(); nodes(tree, item => item.type === "Pressable")[0].props.onPress(); tree = render();
  nodes(tree, item => item.type === "DateTimePicker")[0].props.onChange({ type: "set" }, new Date(2026, 9, 7)); assert.deepEqual(changes, ["2026-10-07"]);
});
test("iOS date picker changes a draft and commits only on Choose", () => {
  const state = hooks(), changes = [];
  const { DateField } = compile("../../src/DateField.tsx", { react: state.react, "react/jsx-runtime": jsx, "react-native": { ...native, Platform: { OS: "ios" }, Modal: "Modal" }, "@react-native-community/datetimepicker": { default: "DateTimePicker" }, "react-native-safe-area-context": { useSafeAreaInsets: () => ({ bottom: 34 }) }, "../../shared/history": historyData, "./calendar-date": compile("../../src/calendar-date.ts"), "./ui": ui });
  const render = () => { state.reset(); return DateField({ label: "Until", value: "2026-10-01", language: "en", onChange: day => changes.push(day) }); };
  let tree = render(); nodes(tree, item => item.type === "Pressable")[0].props.onPress(); tree = render();
  nodes(tree, item => item.type === "DateTimePicker")[0].props.onChange({ type: "set" }, new Date(2026, 9, 8)); assert.deepEqual(changes, []);
  tree = render(); nodes(tree, item => item.type === "Button" && item.props.title === "Choose")[0].props.onPress(); assert.deepEqual(changes, ["2026-10-08"]);
});

test("two profile saves use successive server versions and retain the battery capacity", async () => {
  const state = hooks(), payloads = [], updates = [];
  const user = { id: "driver", email: "driver@example.com", firstName: "Arta", lastName: "Deda", phone: "+355691234567", version: 1, batteryCapacityKWh: 77.5 };
  const { ProfileScreen } = compile("../../src/ProfileScreen.tsx", { react: state.react, "react/jsx-runtime": jsx, "react-native": native, "./i18n": i18n, "./ui": ui, "./account-inputs": compile("../../src/account-inputs.ts"), "./api": { request: async (_path, _token, _method, payload) => { payloads.push(payload); return { ...user, version: payload.version + 1 }; } } });
  const render = () => { state.reset(); return ProfileScreen({ user, token: "driver-token", language: "en", onUpdate: value => updates.push(value) }); };
  let tree = render(); nodes(tree, item => item.type === "Button" && item.props.title === "Save changes")[0].props.onPress(); await tick();
  tree = render(); nodes(tree, item => item.type === "Button" && item.props.title === "Save changes")[0].props.onPress(); await tick();
  assert.deepEqual(payloads.map(item => item.version), [1, 2]); assert.deepEqual(payloads.map(item => item.batteryCapacityKWh), [77.5, 77.5]); assert.equal(updates.at(-1).version, 3);
});
test("registration checks confirmation before health/API and coalesces repeated submit taps", async () => {
  const state = hooks(), requests = [], authenticated = []; let health = 0;
  const { AuthScreen } = compile("../../src/AuthScreen.tsx", { react: state.react, "react/jsx-runtime": jsx, "react-native": native, "./PrivacyLinks": { PrivacyLinks: "PrivacyLinks" }, "./i18n": i18n, "./ui": { ...ui, Brand: "Brand" }, "./wait-for-api": { waitForMobileApi: async () => { health++; return true; } }, "./api": { request: async (...args) => { requests.push(args); return { accessToken: "driver-token" }; } } });
  const render = () => { state.reset(); return AuthScreen({ language: "en", setLanguage() {}, onAuthenticated: async result => authenticated.push(result) }); };
  let tree = render(); nodes(tree, item => item.type === "Chip" && item.props.title === "Register")[0].props.onPress(); tree = render();
  for (const [label, value] of [["First name", "Arta"], ["Last name", "Deda"], ["Email", " arta@example.com "], ["Phone", "+355691234567"], ["Password", "my-long-password"], ["Confirm password", "different"]]) nodes(tree, item => item.type === "Field" && item.props.label === label)[0].props.onChangeText(value);
  tree = render(); nodes(tree, item => item.type === "Button" && item.props.title === "Create account")[0].props.onPress(); await tick(); assert.equal(health, 0); assert.equal(requests.length, 0);
  tree = render(); nodes(tree, item => item.type === "Field" && item.props.label === "Confirm password")[0].props.onChangeText("my-long-password");
  tree = render(); const submit = nodes(tree, item => item.type === "Button" && item.props.title === "Create account")[0]; submit.props.onPress(); submit.props.onPress(); await tick();
  assert.equal(health, 1); assert.equal(requests.length, 1); assert.equal(authenticated.length, 1);
  assert.equal(requests[0][3].email, "arta@example.com"); assert.equal(requests[0][3].confirmation, undefined);
});
