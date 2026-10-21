import test from "node:test";
import assert from "node:assert/strict";
import { compile } from "./helpers/compile.mjs";

const tick = async () => { for (let i = 0; i < 12; i++) await Promise.resolve(); };
const routes = { notifications: "/me/notifications", stations: "/driver/stations", sessions: "/driver/charging-sessions" };
function harness(response) {
  const snapshots = [];
  const { createDriverLoader } = compile("../../src/driver-data.ts", { "./api": { request: response } });
  const loader = createDriverLoader(value => snapshots.push(value));
  loader.setSession("driver-a");
  return { loader, snapshots };
}
const success = (path, token = "driver-a") => path === "/me" ? { id: token, role: "DRIVER" }
  : path === routes.sessions ? { sessions: [{ id: `${token}-session` }] } : [{ id: `${token}:${path}` }];

for (const [section, failedPath] of Object.entries(routes)) test(`a failed ${section} read does not block the other sections`, async () => {
  const { loader, snapshots } = harness(async (path, token) => {
    if (path === failedPath) throw new Error("service_unavailable");
    return success(path, token);
  });
  await loader.refresh();
  const final = snapshots.at(-1);
  assert.equal(final.user.id, "driver-a");
  assert.deepEqual(final[section], []);
  assert.deepEqual(final.sections[section], { loaded: false, pending: false, error: "service_unavailable" });
  for (const other of Object.keys(routes).filter(key => key !== section)) {
    assert.equal(final[other].length, 1);
    assert.deepEqual(final.sections[other], { loaded: true, pending: false, error: "" });
  }
});

test("a slow inbox leaves ready stations and the charging shortcut available before it completes", async () => {
  let finish;
  const { loader, snapshots } = harness((path, token) => path === routes.notifications
    ? new Promise(resolve => { finish = resolve; }) : Promise.resolve(success(path, token)));
  const pending = loader.refresh(); await tick();
  assert.equal(snapshots.at(-1).sections.stations.loaded, true);
  assert.equal(snapshots.at(-1).sections.sessions.loaded, true);
  assert.equal(snapshots.at(-1).sections.notifications.pending, true);
  assert.equal(loader.refresh(), pending);
  finish([]); await pending;
  assert.deepEqual(snapshots.at(-1).sections.notifications, { loaded: true, pending: false, error: "" });
});

test("failed refreshes retain previous data, mark it stale and recover independently", async () => {
  let failed = false;
  const { loader, snapshots } = harness(async (path, token) => {
    if (failed && [routes.notifications, routes.sessions].includes(path)) throw new Error("service_unavailable");
    return success(path, token);
  });
  await loader.refresh(); const original = snapshots.at(-1);
  failed = true; await loader.refresh(); const stale = snapshots.at(-1);
  assert.deepEqual(stale.notifications, original.notifications);
  assert.deepEqual(stale.sessions, original.sessions);
  assert.equal(stale.sections.notifications.loaded, true);
  assert.equal(stale.sections.sessions.error, "service_unavailable");
  assert.equal(stale.sections.stations.error, "");
  assert.equal(original.sections.notifications.error, "", "published snapshots must not be mutated");
  failed = false; await loader.refresh();
  assert.ok(Object.values(snapshots.at(-1).sections).every(state => state.loaded && !state.error && !state.pending));
});

test("a failed identity refresh stays stale until each section has revalidated", async () => {
  let mode = "success", finish;
  const { loader, snapshots } = harness(async (path, token) => {
    if (mode === "identity-failure" && path === "/me") throw new Error("service_unavailable");
    if (mode === "recovery" && path === routes.sessions) return new Promise(resolve => { finish = resolve; });
    return success(path, token);
  });
  await loader.refresh(); mode = "identity-failure";
  await assert.rejects(loader.refresh(), /service_unavailable/);
  mode = "recovery"; const pending = loader.refresh(); await tick();
  assert.equal(snapshots.at(-1).sections.stations.error, "");
  assert.equal(snapshots.at(-1).sections.sessions.error, "service_unavailable");
  finish({ sessions: [] }); await pending;
  assert.equal(snapshots.at(-1).sections.sessions.error, "");
});

test("switching accounts clears cached sessions and notifications even when the new reads fail", async () => {
  const { loader, snapshots } = harness(async (path, token) => {
    if (token === "driver-b" && path !== "/me") throw new Error("service_unavailable");
    return success(path, token);
  });
  await loader.refresh(); loader.setSession("driver-b"); await loader.refresh();
  const final = snapshots.at(-1);
  assert.equal(final.user.id, "driver-b");
  for (const section of Object.keys(routes)) {
    assert.deepEqual(final[section], []);
    assert.equal(final.sections[section].loaded, false);
  }
});

test("401 is a session failure and aborts pending reads instead of becoming an optional error", async () => {
  let aborted = false;
  const { loader, snapshots } = harness((path, token, _method, _body, signal) => {
    if (path === "/me") return Promise.resolve(success(path, token));
    if (path === routes.notifications) return Promise.reject(Object.assign(new Error("session_expired"), { status: 401 }));
    return new Promise((_, reject) => signal.addEventListener("abort", () => { aborted = true; reject(new Error("cancelled")); }));
  });
  await assert.rejects(loader.refresh(), /session_expired/);
  assert.equal(aborted, true);
  assert.ok(snapshots.every(snapshot => Object.values(snapshot.sections).every(state => !state.loaded)));
});

test("background cancellation suppresses late partial results", async () => {
  let finish;
  const { loader, snapshots } = harness((path, token) => path === routes.stations
    ? new Promise(resolve => { finish = resolve; }) : Promise.resolve(success(path, token)));
  const controller = new AbortController(); const pending = loader.refresh(controller.signal); await tick();
  controller.abort(); const count = snapshots.length; finish([{ id: "late-station" }]); await pending;
  assert.equal(snapshots.length, count);
  assert.deepEqual(snapshots.at(-1).stations, []);
});
