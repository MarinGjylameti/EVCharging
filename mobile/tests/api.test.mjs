import test from "node:test";
import assert from "node:assert/strict";
import { compile } from "./helpers/compile.mjs";
const config = compile("../../src/config.ts");
const token = "a".repeat(64);
process.env.EXPO_PUBLIC_API_BASE_URL = "https://api.example.com";
globalThis.__DEV__ = false;
function api(storage = {}) {
  const writes = [];
  const store = { WHEN_UNLOCKED_THIS_DEVICE_ONLY: 1, getItemAsync: async () => null, setItemAsync: async (...args) => writes.push(args), deleteItemAsync: async (...args) => writes.push(args), ...storage };
  return { ...compile("../../src/api.ts", { "expo-secure-store": store, "./config": config }), writes };
}
test("invalid stored JSON and expired sessions are cleared from SecureStore", async () => {
  for (const raw of ["broken", JSON.stringify({ token, expiresAt: "2020-01-01" })]) {
    const client = api({ getItemAsync: async () => raw });
    assert.equal(await client.readSession(), null); assert.equal(client.writes.length, 1);
  }
});
test("valid sessions use device-only protected storage and malformed tokens are refused", async () => {
  const value = { token, expiresAt: "2099-01-01T00:00:00Z" }, client = api({ getItemAsync: async () => JSON.stringify(value) });
  assert.deepEqual(await client.readSession(), value); await client.saveSession(value);
  assert.equal(client.writes[0][2].keychainAccessible, 1);
  await assert.rejects(client.saveSession({ ...value, token: "short" }), /session_expired/);
  assert.equal(client.writes.length, 1);
});
test("API sends the bearer only to the configured API and omits Content-Type on GET", async t => {
  const calls = [];
  t.mock.method(globalThis, "fetch", async (...args) => { calls.push(args); return Response.json({ ok: true }); });
  const client = api();
  await client.request("/driver/stations", token);
  assert.equal(calls[0][0], "https://api.example.com/api/v1/driver/stations");
  assert.equal(calls[0][1].headers.Authorization, `Bearer ${token}`);
  assert.equal(calls[0][1].headers["Content-Type"], undefined);
  await client.request("/me/profile", token, "PATCH", { version: 3 });
  assert.equal(calls[1][1].headers["Content-Type"], "application/json");
  assert.equal(calls[1][1].body, '{"version":3}');
  for (const path of ["https://other.example.com/me", "//other.example.com/me", "/driver/../admin", "/me#secret", "/me\\admin"]) await assert.rejects(client.request(path, token), /configuration/);
  assert.equal(calls.length, 2);
});
test("401 expires only the requesting token; login 401 remains invalid credentials", async t => {
  const expired = [], client = api(); client.subscribeSessionExpiry(value => expired.push(value));
  t.mock.method(globalThis, "fetch", async () => Response.json({ code: "invalid_credentials" }, { status: 401 }));
  await assert.rejects(client.request("/me", token), /session_expired/);
  await assert.rejects(client.request("/auth/login", null, "POST", {}), /invalid_credentials/);
  assert.deepEqual(expired, [token]);
});
test("removing an old expiry subscription preserves the current subscriber", async t => {
  const client = api(), events = [];
  const old = client.subscribeSessionExpiry(() => events.push("old"));
  client.subscribeSessionExpiry(() => events.push("new")); old();
  t.mock.method(globalThis, "fetch", async () => new Response(null, { status: 401 }));
  await assert.rejects(client.request("/me", token)); assert.deepEqual(events, ["new"]);
});
test("API supports 204, reports traceable errors and rejects HTML loading pages", async t => {
  const client = api();
  t.mock.method(globalThis, "fetch", async () => new Response(null, { status: 204 }));
  assert.equal(await client.request("/auth/logout", token, "POST"), undefined);
  globalThis.fetch = async () => Response.json({ code: "version_conflict", traceId: "trace-1" }, { status: 409 });
  await assert.rejects(client.request("/me/profile", token, "PATCH", {}), error => error.code === "version_conflict" && error.traceId === "trace-1");
  globalThis.fetch = async () => new Response("starting…", { headers: { "Content-Type": "text/html" } });
  await assert.rejects(client.request("/me", token), /service_unavailable/);
  globalThis.fetch = async () => new Response(null, { status: 429 });
  await assert.rejects(client.request("/me", token), /rateLimit/);
});
test("request timeouts abort once without replaying a mutation", async t => {
  t.mock.timers.enable({ apis: ["setTimeout"] }); let calls = 0;
  t.mock.method(globalThis, "fetch", (_, { signal }) => { calls++; return new Promise((_, reject) => signal.addEventListener("abort", () => reject(Object.assign(new Error("abort"), { name: "AbortError" })))); });
  const pending = api().request("/driver/stations/abc/connect", token, "POST");
  const check = assert.rejects(pending, /service_unavailable/);
  t.mock.timers.tick(12000); await check; assert.equal(calls, 1);
});
test("foreground cancellation propagates AbortError", async t => {
  const controller = new AbortController();
  t.mock.method(globalThis, "fetch", (_, { signal }) => new Promise((_, reject) => signal.addEventListener("abort", () => reject(Object.assign(new Error("abort"), { name: "AbortError" })))));
  const pending = api().request("/me", token, "GET", undefined, controller.signal);
  const check = assert.rejects(pending, error => error.name === "AbortError"); controller.abort(); await check;
});
test("driver refresh coalesces simultaneous requests and discards a previous login", async () => {
  const resolvers = [], calls = [], delivered = [];
  const client = { request(path, token) { calls.push([path, token]); if (path === "/me") return new Promise(resolve => resolvers.push(resolve)); return Promise.resolve(path === "/driver/charging-sessions" ? { sessions: [{ id: "owned-session", connectorId: 2 }] } : []); } };
  const { createDriverLoader } = compile("../../src/driver-data.ts", { "./api": client });
  const loader = createDriverLoader(value => delivered.push(value)); loader.setSession("old");
  const old = loader.refresh(); assert.equal(loader.refresh(), old); assert.equal(calls.length, 1);
  loader.setSession("new"); const current = loader.refresh();
  resolvers[0]({ id: "old", role: "DRIVER" }); await old; assert.equal(delivered.length, 0);
  resolvers[1]({ id: "new", role: "DRIVER" }); await current;
  assert.equal(delivered.length, 1); assert.equal(delivered[0].user.id, "new"); assert.equal(calls.filter(item => item[1] === "old").length, 1);
  assert.deepEqual(delivered[0].sessions, [{ id: "owned-session", connectorId: 2 }]);
  assert.ok(calls.some(([path, token]) => path === "/driver/charging-sessions" && token === "new"));
});

test("a late charging-session response from a previous account cannot replace the current shortcut data", async () => {
  let finishOld; const delivered = [];
  const { createDriverLoader } = compile("../../src/driver-data.ts", { "./api": { request: async (path, token) => {
    if (path === "/me") return { id: token, role: "DRIVER" };
    if (path === "/driver/charging-sessions") return token === "old" ? new Promise(resolve => { finishOld = resolve; }) : { sessions: [] };
    return [];
  } } });
  const loader = createDriverLoader(value => delivered.push(value));
  loader.setSession("old"); const old = loader.refresh(); await Promise.resolve();
  assert.equal(typeof finishOld, "function");
  loader.setSession("new"); await loader.refresh();
  finishOld({ sessions: [{ id: "private-old-session" }] }); await old;
  assert.equal(delivered.length, 1);
  assert.equal(delivered[0].user.id, "new");
  assert.deepEqual(delivered[0].sessions, []);
});
test("administrative accounts never load private driver data", async () => {
  const calls = [];
  const { createDriverLoader } = compile("../../src/driver-data.ts", { "./api": { request: async path => { calls.push(path); return { role: "SUPERADMIN" }; } } });
  const loader = createDriverLoader(() => assert.fail("Must not deliver admin data")); loader.setSession(token);
  await assert.rejects(loader.refresh(), /administratorAccount/); assert.deepEqual(calls, ["/me"]);
});

test("a slower list response cannot restore a shortcut after the charging screen confirms completion", async () => {
  let finish; const delivered = [];
  const { createDriverLoader } = compile("../../src/driver-data.ts", { "./api": { request: async path => {
    if (path === "/me") return { role: "DRIVER" };
    if (path === "/driver/charging-sessions") return new Promise(resolve => { finish = resolve; });
    return [];
  } } });
  const loader = createDriverLoader(value => delivered.push(value)); loader.setSession("account");
  const pending = loader.refresh(); await Promise.resolve();
  loader.confirmSessionEnded("completed");
  finish({ sessions: [{ id: "completed", stoppedAt: null }, { id: "other-open", stoppedAt: null }] }); await pending;
  assert.deepEqual(delivered[0].sessions.map(s => s.id), ["other-open"]);
});
test("cold-start health probes require ready JSON and retry only GET", async () => {
  const { waitForMobileApi } = compile("../../src/wait-for-api.ts", { "./config": config });
  let time = 0, calls = 0, waiting = 0;
  assert.equal(await waitForMobileApi(new AbortController().signal, () => waiting++, {
    now: () => time, sleep: async ms => { time += ms; }, budgetMs: 6000,
    fetcher: async (url, options) => { assert.equal(url, "https://api.example.com/health/ready"); assert.equal(options.body, undefined); calls++; return calls === 1 ? new Response("warming…") : Response.json({ status: "ready" }); },
  }), true);
  assert.equal(calls, 2); assert.equal(waiting, 1);
});
test("cold-start waiting is bounded and cancellable", async () => {
  const { waitForMobileApi } = compile("../../src/wait-for-api.ts", { "./config": config }); let time = 0;
  assert.equal(await waitForMobileApi(new AbortController().signal, () => {}, { now: () => time, sleep: async ms => { time += ms; }, budgetMs: 3000, fetcher: async () => Response.json({ status: "unavailable" }) }), false);
  const controller = new AbortController(); controller.abort(); await assert.rejects(waitForMobileApi(controller.signal, () => {}), /cancelled/);
});

test("logging out aborts an unfinished driver snapshot and never delivers it", async () => {
  let signal; const delivered = [];
  const { createDriverLoader } = compile("../../src/driver-data.ts", { "./api": { request: (_path, _token, _method, _body, current) => { signal = current; return new Promise((_, reject) => current.addEventListener("abort", () => reject(new Error("cancelled")))); } } });
  const loader = createDriverLoader(value => delivered.push(value)); loader.setSession(token);
  const pending = loader.refresh(); loader.setSession(null); await pending;
  assert.equal(signal.aborted, true); assert.equal(delivered.length, 0); await loader.refresh(); assert.equal(delivered.length, 0);
});
