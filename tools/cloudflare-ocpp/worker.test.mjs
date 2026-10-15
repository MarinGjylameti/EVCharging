import assert from "node:assert/strict";
import { test } from "node:test";
import worker from "./worker.mjs";

const PUBLIC_ORIGIN = "https://evcharging-ocpp.example.workers.dev";

function chargerRequest(path = "/ws/cs/NKR2023072805", options = {}) {
  return new Request(PUBLIC_ORIGIN + path, {
    ...options,
    headers: {
      Upgrade: "websocket",
      Connection: "Upgrade",
      "Sec-WebSocket-Protocol": "ocpp1.6",
      ...options.headers,
    },
  });
}

test("forwards the charger identity, credentials and OCPP subprotocol to Render", async (t) => {
  // Node cannot create a 101 Response. This sentinel checks that the Workers
  // response (including its socket) is returned intact; live WS needs Cloudflare.
  const upgraded = {
    status: 101,
    headers: new Headers({ "Sec-WebSocket-Protocol": "ocpp1.6" }),
    webSocket: {},
  };
  const upstreamFetch = t.mock.method(globalThis, "fetch", async (request) => {
    assert.equal(
      request.url,
      "https://vcharging-api-demo.onrender.com/ws/cs/NKR2023072805?test=1",
    );
    assert.equal(request.headers.get("Host"), "vcharging-api-demo.onrender.com");
    assert.equal(request.headers.get("Upgrade"), "websocket");
    assert.equal(request.headers.get("Connection"), "Upgrade");
    assert.equal(request.headers.get("Sec-WebSocket-Protocol"), "ocpp1.6");
    assert.equal(request.headers.get("Authorization"), "Basic dXNlcjpwYXNz");
    assert.equal(request.redirect, "manual");
    return upgraded;
  });
  const response = await worker.fetch(
    chargerRequest("/ws/cs/NKR2023072805?test=1", {
      headers: { Host: "untrusted.example", Authorization: "Basic dXNlcjpwYXNz" },
    }),
  );
  assert.equal(response, upgraded);
  assert.equal(upstreamFetch.mock.callCount(), 1);
});

test("forwards readiness to Render rather than reporting proxy readiness", async (t) => {
  const notReady = new Response("Unavailable", { status: 503 });
  t.mock.method(globalThis, "fetch", async (request) => {
    assert.equal(request.url, "https://vcharging-api-demo.onrender.com/health/ready");
    return notReady;
  });
  const response = await worker.fetch(new Request(PUBLIC_ORIGIN + "/health/ready"));
  assert.equal(response.status, 503);
  assert.equal(await response.text(), "Unavailable");
  assert.equal(response.headers.get("X-OCPP-Proxy"), "evcharging-ocpp/diag-1");
});

test("keeps backend admission and duplicate-connection responses intact", async (t) => {
  for (const status of [401, 403, 409]) {
    const denied = new Response("Denied", { status, headers: { "WWW-Authenticate": "Basic", "X-Request-ID": "backend-trace" } });
    t.mock.method(globalThis, "fetch", async () => denied);
    const response = await worker.fetch(chargerRequest());
    assert.equal(response.status, status);
    assert.equal(await response.text(), "Denied");
    assert.equal(response.headers.get("WWW-Authenticate"), "Basic");
    assert.equal(response.headers.get("X-Request-ID"), "backend-trace");
    assert.equal(response.headers.get("X-OCPP-Proxy-Stage"), "upstream_response");
    t.mock.restoreAll();
  }
});

test("does not forward API, station UI or malformed identity paths", async (t) => {
  const upstreamFetch = t.mock.method(globalThis, "fetch", () => {
    assert.fail("This path must not reach Render");
  });
  for (const path of [
    "/", "/api/admin", "/ws/cs", "/ws/cs/", "/ws/cs/id/extra",
    "/ws/cs/bad%2Fid", "/ws/cs/" + "a".repeat(41),
  ]) {
    assert.equal((await worker.fetch(chargerRequest(path))).status, 404);
  }
  assert.equal(upstreamFetch.mock.callCount(), 0);
});

test("requires GET and a WebSocket upgrade for charger requests", async (t) => {
  const upstreamFetch = t.mock.method(globalThis, "fetch", () => {
    assert.fail("Invalid requests must not reach Render");
  });
  const post = await worker.fetch(chargerRequest(undefined, { method: "POST" }));
  assert.equal(post.status, 405);
  assert.equal(post.headers.get("Allow"), "GET");
  const plainGet = new Request(PUBLIC_ORIGIN + "/ws/cs/NKR2023072805");
  assert.equal((await worker.fetch(plainGet)).status, 426);
  assert.equal(upstreamFetch.mock.callCount(), 0);
});

test("reports an upstream connection failure without exposing its details", async (t) => {
  t.mock.method(globalThis, "fetch", async () => {
    throw new Error("Internal connection details");
  });
  const response = await worker.fetch(chargerRequest());
  assert.equal(response.status, 502);
  assert.equal(response.headers.get("Cache-Control"), "no-store");
  assert.equal(response.headers.get("X-OCPP-Proxy-Stage"), "upstream_error");
  assert.equal(await response.text(), "Backend unavailable");
});

test("correlates a received request with an upstream 403 without logging secrets", async (t) => {
  const entries = [];
  t.mock.method(console, "log", value => entries.push(JSON.parse(value)));
  t.mock.method(globalThis, "fetch", async () => new Response("private upstream error body", {
    status: 403,
    headers: { "CF-Ray": "1234567890abcdef-FRA", "Server": "cloudflare" },
  }));
  const response = await worker.fetch(chargerRequest("/ws/cs/NKR2023072805?secret=query-value", {
    headers: {
      "CF-Ray": "abcdef1234567890-TIA",
      Authorization: "Basic private-credentials",
      Cookie: "private-cookie",
      "Sec-WebSocket-Protocol": "private-protocol, ocpp1.6",
    },
  }));
  assert.equal(response.status, 403);
  assert.deepEqual(entries.map(entry => entry.stage), ["received", "upstream_response"]);
  assert.ok(entries.every(entry => entry.ray === "abcdef1234567890-TIA" && entry.identity === "NKR2023072805"));
  assert.equal(entries[0].ocpp16Offered, true);
  assert.equal(entries[1].status, 403);
  assert.equal(entries[1].upstreamRay, "1234567890abcdef-FRA");
  assert.doesNotMatch(JSON.stringify(entries), /private-|query-value|upstream error body/);
  assert.equal(await response.text(), "private upstream error body");
});

test("distinguishes local route rejection and never logs an arbitrary path or Ray header", async (t) => {
  const entries = [];
  t.mock.method(console, "log", value => entries.push(JSON.parse(value)));
  t.mock.method(globalThis, "fetch", () => assert.fail("Rejected path must not reach Render"));
  const response = await worker.fetch(chargerRequest("/private-path?token=private-query", {
    headers: { "CF-Ray": "private-header" },
  }));
  assert.equal(response.status, 404);
  assert.equal(response.headers.get("X-OCPP-Proxy-Stage"), "worker_rejected");
  assert.equal(entries[0].ray, null);
  assert.equal(entries[0].identity, null);
  assert.doesNotMatch(JSON.stringify(entries), /private-/);
});
