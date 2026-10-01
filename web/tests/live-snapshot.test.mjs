import test from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import ts from "typescript";

const compiled = ts.transpileModule(readFileSync(new URL("../src/features/driver/use-live-snapshot.ts", import.meta.url), "utf8"), {
  compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 },
}).outputText;

test("live snapshots coalesce events, isolate the selected point and recover without replacing newer mutations", async () => {
  const names = ["document", "window", "EventSource", "fetch", "setTimeout", "clearTimeout", "setInterval", "clearInterval"];
  const originals = Object.fromEntries(names.map(name => [name, globalThis[name]]));
  const timers = new Map();
  const requests = [];
  const state = [];
  const listeners = {};
  let cleanup, stream, nextTimer = 1;
  const flush = async () => { for (let i = 0; i < 8; i++) await Promise.resolve(); };
  const fireTimer = async () => { const [id, callback] = timers.entries().next().value; timers.delete(id); callback(); await flush(); };
  try {
    globalThis.document = { hidden: false, addEventListener: (name, fn) => listeners[name] = fn, removeEventListener: name => delete listeners[name] };
    globalThis.window = { location: { assign: () => assert.fail("No redirect expected") } };
    globalThis.EventSource = class {
      handlers = {}; closed = false;
      constructor() { stream = this; }
      addEventListener(name, fn) { this.handlers[name] = fn; }
      close() { this.closed = true; }
    };
    globalThis.fetch = (url, options) => new Promise(resolve => requests.push({ url, options, resolve }));
    globalThis.setTimeout = callback => { const id = nextTimer++; timers.set(id, callback); return id; };
    globalThis.clearTimeout = id => timers.delete(id);
    globalThis.setInterval = () => 100;
    globalThis.clearInterval = () => {};
    const react = {
      useState: value => { const index = state.length; state.push(value); return [value, next => state[index] = next]; },
      useRef: value => ({ current: value }), useCallback: fn => fn,
      useEffect: fn => { cleanup = fn(); },
    };
    const exports = {};
    new Function("require", "exports", compiled)(name => { assert.equal(name, "react"); return react; }, exports);
    const view = exports.useLiveSnapshot({ value: "initial" }, "/snapshot", "station-1");
    assert.equal(requests.length, 1);
    stream.handlers.changed({ data: JSON.stringify({ stationId: "station-2" }) });
    assert.equal(timers.size, 0);
    for (let i = 0; i < 100; i++) stream.handlers.changed({ data: JSON.stringify({ stationId: "station-1" }) });
    assert.equal(timers.size, 1);
    await fireTimer();
    assert.equal(requests.length, 1, "An event cannot overlap the in-flight request");
    view.setSnapshot({ value: "new connection" });
    requests[0].resolve({ ok: true, status: 200, json: async () => ({ value: "old request" }) });
    await flush();
    assert.deepEqual(state[0], { value: "new connection" });
    assert.equal(timers.size, 1);
    await fireTimer();
    assert.equal(requests.length, 2);
    requests[1].resolve({ ok: true, status: 200, json: async () => ({ value: "live energy" }) });
    await flush();
    assert.deepEqual(state[0], { value: "live energy" });
    stream.handlers.ready();
    assert.equal(state[1], true);
    cleanup();
    assert.equal(stream.closed, true);
    assert.equal(requests[0].options.signal.aborted, true);
    assert.equal(timers.size, 0);
    assert.equal(listeners.visibilitychange, undefined);
  } finally {
    cleanup?.();
    for (const name of names) {
      if (originals[name] === undefined) delete globalThis[name];
      else globalThis[name] = originals[name];
    }
  }
});
