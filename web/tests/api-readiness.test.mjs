import test from "node:test";
import assert from "node:assert/strict";
import { loadTypescript } from "./helpers/load-typescript.mjs";
import { waitForApi } from "../src/features/auth/wait-for-api.ts";

function options(responses, budgetMs = 10_000) {
  let clock = 0;
  const requests = [];
  return {
    requests,
    budgetMs,
    now: () => clock,
    sleep: async milliseconds => { clock += milliseconds; },
    fetcher: async (url, init) => {
      requests.push({ url, init });
      const response = responses.shift();
      if (response instanceof Error) throw response;
      return response ?? new Response(null, { status: 503 });
    },
  };
}

test("cold start retries only readiness probes until the API confirms ready", async () => {
  const setup = options([
    new Response("<html>Render is starting</html>"),
    new Response(null, { status: 503 }),
    new TypeError("Connection unavailable"),
    Response.json({ status: "ready" }),
  ]);
  let waiting = 0;
  assert.equal(await waitForApi(new AbortController().signal, () => waiting++, setup), true);
  assert.equal(waiting, 3);
  assert.equal(setup.requests.length, 4);
  assert.ok(setup.requests.every(x => x.url === "/api/auth/ready" && !x.init.body && !x.init.method));
});

test("persistent failures stop at the time budget rather than waiting forever", async () => {
  const setup = options([], 6_000);
  assert.equal(await waitForApi(new AbortController().signal, () => {}, setup), false);
  assert.equal(setup.requests.length, 3);
});

test("leaving the form aborts a pending readiness request", async () => {
  const controller = new AbortController();
  let requestSignal;
  const pending = waitForApi(controller.signal, () => assert.fail("No further waiting expected"), {
    fetcher: async (_url, init) => {
      requestSignal = init.signal;
      return new Promise((_resolve, reject) => init.signal.addEventListener("abort", () => reject(init.signal.reason), { once: true }));
    },
  });
  controller.abort();
  await assert.rejects(pending, { name: "AbortError" });
  assert.equal(requestSignal.aborted, true);
});

test("BFF probes reject the platform loading page and confirm the database readiness endpoint", async () => {
  const exports = loadTypescript(new URL("../src/app/api/auth/ready/route.ts", import.meta.url), {
    "@/lib/backend": { apiOrigin: () => "https://api.example.test" },
  });
  const original = globalThis.fetch;
  try {
    for (const [response, expected] of [
      [new Response("<html>starting</html>"), 503],
      [Response.json({ status: "alive" }), 503],
      [Response.json({ status: "ready" }), 200],
    ]) {
      globalThis.fetch = async (url, init) => {
        assert.equal(url, "https://api.example.test/health/ready");
        assert.equal(init.cache, "no-store");
        return response;
      };
      const result = await exports.GET();
      assert.equal(result.status, expected);
      assert.equal(result.headers.get("cache-control"), "no-store");
    }
  } finally { globalThis.fetch = original; }
});
