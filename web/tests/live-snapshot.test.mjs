import test from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import ts from "typescript";

const compiled = ts.transpileModule(readFileSync(new URL("../src/features/driver/use-live-snapshot.ts", import.meta.url), "utf8"), {
  compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 },
}).outputText;

function mount(t, stationId = "station-1") {
  const names = ["document", "window", "EventSource", "fetch", "setTimeout", "clearTimeout", "setInterval", "clearInterval"];
  const originals = Object.fromEntries(names.map(name => [name, globalThis[name]]));
  const realNow = Date.now;
  const timers = new Map(), intervals = new Map(), requests = [], state = [], streams = [];
  const listeners = {}, windowListeners = {};
  let cleanup, nextTimer = 1, now = 100_000;
  Date.now = () => now;
  const flush = async () => { for (let i = 0; i < 12; i++) await Promise.resolve(); };
  const advance = async ms => {
    now += ms;
    for (const [id, timer] of [...timers]) {
      if (timer.at <= now && timers.delete(id)) timer.callback();
    }
    await flush();
  };
  globalThis.document = { hidden: false, addEventListener: (name, fn) => listeners[name] = fn, removeEventListener: name => delete listeners[name] };
  globalThis.window = {
    location: { assign: url => state.redirect = url },
    addEventListener: (name, fn) => windowListeners[name] = fn,
    removeEventListener: name => delete windowListeners[name],
  };
  globalThis.EventSource = class {
    handlers = {}; closed = false;
    constructor() { streams.push(this); }
    addEventListener(name, fn) { this.handlers[name] = fn; }
    close() { this.closed = true; }
  };
  globalThis.fetch = (url, options) => new Promise((resolve, reject) => {
    requests.push({ url, options, resolve, reject });
    options.signal.addEventListener("abort", () => reject(new Error("Aborted")), { once: true });
  });
  globalThis.setTimeout = (callback, ms) => { const id = nextTimer++; timers.set(id, { callback, at: now + ms }); return id; };
  globalThis.clearTimeout = id => timers.delete(id);
  globalThis.setInterval = (callback, ms) => { const id = nextTimer++; intervals.set(id, { callback, ms }); return id; };
  globalThis.clearInterval = id => intervals.delete(id);
  const react = {
    useState: value => { const index = state.length; state.push(value); return [value, next => state[index] = next]; },
    useRef: value => ({ current: value }), useCallback: fn => fn,
    useEffect: fn => { cleanup = fn(); },
  };
  const exports = {};
  new Function("require", "exports", compiled)(name => { assert.equal(name, "react"); return react; }, exports);
  const view = exports.useLiveSnapshot({ value: "initial" }, "/snapshot", stationId);
  t.after(async () => {
    cleanup(); await flush(); Date.now = realNow;
    for (const name of names) {
      if (originals[name] === undefined) delete globalThis[name];
      else globalThis[name] = originals[name];
    }
  });
  return {
    view, requests, state, timers, streams, listeners, windowListeners, intervals, advance, flush,
    cleanup: () => cleanup(),
    changed: data => streams.at(-1).handlers.changed({ data: typeof data === "string" ? data : JSON.stringify(data) }),
    ready: () => streams.at(-1).handlers.ready(),
    tick: async ms => { now += ms; for (const { callback } of intervals.values()) callback(); await advance(0); },
    resolve: async (value, status = 200) => {
      requests.at(-1).resolve({ ok: status === 200, status, json: async () => ({ value }) });
      await flush();
    },
  };
}

test("the selected station refreshes immediately and empty/legacy hints still resync", async t => {
  const h = mount(t);
  await h.resolve("initial fetch");
  h.changed({ stationId: "station-2" });
  await h.advance(0);
  assert.equal(h.requests.length, 1);
  for (const hint of [{ stationId: "STATION-1" }, {}, { StationId: "station-1" }, null, "invalid json"]) {
    const before = h.requests.length;
    h.changed(hint); await h.advance(0);
    assert.equal(h.requests.length, before + 1, "No 250ms debounce or 30s poll is needed");
    await h.resolve("current");
  }
  assert.equal(h.state[1], true);
});

test("event bursts have one request and a trailing refresh without overwriting a mutation", async t => {
  const h = mount(t);
  for (let i = 0; i < 100; i++) h.changed({ stationId: "station-1" });
  assert.equal(h.requests.length, 1);
  h.view.setSnapshot({ value: "new connection" });
  await h.resolve("old request");
  assert.deepEqual(h.state[0], { value: "new connection" });
  await h.advance(0);
  assert.equal(h.requests.length, 2);
  await h.resolve("live energy");
  assert.deepEqual(h.state[0], { value: "live energy" });
  h.ready();
  await h.advance(0);
  h.cleanup(); await h.flush();
  assert.equal(h.streams[0].closed, true);
  assert.equal(h.requests.at(-1).options.signal.aborted, true);
  assert.equal(h.timers.size, 0);
  assert.equal(h.intervals.size, 0);
  assert.equal(h.listeners.visibilitychange, undefined);
  assert.equal(h.windowListeners.online, undefined);
});

test("disconnects fetch immediately, poll every five seconds and resync on reconnect", async t => {
  const h = mount(t);
  h.ready(); await h.resolve("first"); await h.advance(0); await h.resolve("connected");
  h.streams[0].onerror(); await h.advance(0);
  assert.equal(h.state[1], false);
  await h.resolve("from fallback");
  const before = h.requests.length;
  await h.tick(5_000);
  assert.equal(h.requests.length, before + 1);
  await h.resolve("still fresh");
  h.ready(); await h.advance(0);
  assert.equal(h.state[1], true);
  assert.equal(h.requests.length, before + 2);
});

test("silent streams are replaced; hidden tabs wait until visible and online resumes immediately", async t => {
  const h = mount(t);
  await h.resolve("first"); h.ready(); await h.advance(0); await h.resolve("ready");
  for (let i = 0; i < 4; i++) {
    h.streams[0].handlers.heartbeat(); await h.tick(5_000);
  }
  assert.equal(h.streams.length, 1);
  globalThis.document.hidden = true;
  await h.tick(30_000);
  const before = h.requests.length;
  h.changed({}); await h.advance(0);
  assert.equal(h.requests.length, before);
  await h.tick(30_000);
  globalThis.document.hidden = false;
  h.listeners.visibilitychange(); await h.advance(0);
  assert.equal(h.streams[0].closed, true);
  assert.equal(h.streams.length, 2);
  assert.equal(h.state[1], false);
  await h.resolve("after sleep");
  h.windowListeners.online(); await h.advance(0);
  assert.equal(h.requests.length, before + 2);
});

test("a stuck snapshot times out and subsequent events can recover", async t => {
  const h = mount(t);
  await h.advance(10_000);
  assert.equal(h.requests[0].options.signal.aborted, true);
  assert.equal(h.state[2], true);
  h.changed({}); await h.advance(0); await h.resolve("recovered");
  assert.deepEqual(h.state[0], { value: "recovered" });
  assert.equal(h.state[2], false);
});

test("expired authentication stops the stream and any further refresh", async t => {
  const h = mount(t);
  await h.resolve(null, 401);
  assert.equal(h.state.redirect, "/");
  assert.equal(h.streams[0].closed, true);
  await h.tick(30_000);
  assert.equal(h.requests.length, 1);
});
