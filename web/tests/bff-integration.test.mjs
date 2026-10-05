import test from "node:test";
import assert from "node:assert/strict";
import { loadTypescript } from "./helpers/load-typescript.mjs";

const id = "14eedf70-4774-4b4d-808e-8f91dbbc13ce";
const registration = { firstName: "Arta", lastName: "Deda", email: "arta@example.test", phone: "+355691234567", password: "a-long-driver-passphrase", language: "sq" };
const auth = { accessToken: "private-session-token", expiresAt: "2030-01-01T00:00:00Z", user: { id, role: "DRIVER", firstName: "Arta", phone: registration.phone } };

function setup(t, token = "own-session") {
  const originalFetch = globalThis.fetch;
  const originalApi = process.env.EVCHARGING_API_BASE_URL;
  const originalOrigin = process.env.EVCHARGING_WEB_ORIGIN;
  process.env.EVCHARGING_API_BASE_URL = "https://api.example.test";
  process.env.EVCHARGING_WEB_ORIGIN = "https://web.example.test";
  t.after(() => {
    globalThis.fetch = originalFetch;
    if (originalApi === undefined) delete process.env.EVCHARGING_API_BASE_URL;
    else process.env.EVCHARGING_API_BASE_URL = originalApi;
    if (originalOrigin === undefined) delete process.env.EVCHARGING_WEB_ORIGIN;
    else process.env.EVCHARGING_WEB_ORIGIN = originalOrigin;
  });
  const jar = { get: () => token ? { value: token } : undefined, has: () => Boolean(token) };
  const headers = { cookies: async () => jar };
  const backend = loadTypescript(new URL("../src/lib/backend.ts", import.meta.url), { "next/headers": headers, react: { cache: fn => fn } });
  const body = loadTypescript(new URL("../src/lib/request-body.ts", import.meta.url));
  const response = loadTypescript(new URL("../src/lib/auth-response.ts", import.meta.url), { "./backend": backend });
  return {
    backend,
    route: path => loadTypescript(new URL(`../src/app/api/${path}/route.ts`, import.meta.url), {
      "@/lib/backend": backend, "@/lib/request-body": body, "@/lib/auth-response": response, "next/headers": headers,
    }),
  };
}

function request(path, method = "POST", payload, origin = "https://web.example.test", signal) {
  return new Request(`https://web.example.test/api/${path}`, { method, signal,
    headers: { Origin: origin, "Content-Type": "application/json" },
    body: payload === undefined ? undefined : JSON.stringify(payload),
  });
}

for (const action of ["register", "login"]) {
  test(`${action} forwards credentials once and exposes the user through an HttpOnly session`, async t => {
    const { route } = setup(t);
    const payload = action === "register" ? registration : { email: registration.email, password: registration.password };
    let calls = 0;
    globalThis.fetch = async (url, init) => {
      calls++;
      assert.equal(url, `https://api.example.test/api/v1/auth/${action}`);
      assert.equal(init.method, "POST");
      assert.equal(init.cache, "no-store");
      assert.deepEqual(JSON.parse(init.body), payload);
      return Response.json(auth);
    };
    const result = await route(`auth/${action}`).POST(request(`auth/${action}`, "POST", payload));
    assert.equal(result.status, 200);
    assert.deepEqual(await result.json(), { user: auth.user });
    assert.equal(result.headers.get("Cache-Control"), "no-store");
    const cookie = result.headers.get("set-cookie");
    assert.match(cookie, /evcharging_session=private-session-token/);
    assert.match(cookie, /HttpOnly/i);
    assert.match(cookie, /SameSite=strict/i);
    assert.match(cookie, /Path=\//);
    assert.equal(calls, 1);
  });
}

test("registration rejects cross-origin, missing contact data and role injection before forwarding", async t => {
  const { route } = setup(t);
  globalThis.fetch = async () => assert.fail("Invalid request must not reach the API");
  const { POST } = route("auth/register");
  for (const [payload, origin, expected] of [
    [registration, "https://attacker.test", 403],
    [{ ...registration, phone: "" }, undefined, 400],
    [{ ...registration, phone: undefined }, undefined, 400],
    [{ ...registration, firstName: " " }, undefined, 400],
    [{ ...registration, role: "SUPERADMIN" }, undefined, 400],
    [{ ...registration, ownerId: id }, undefined, 400],
    [{ ...registration, email: "a".repeat(255) }, undefined, 400],
  ]) assert.equal((await POST(request("auth/register", "POST", payload, origin))).status, expected);
});

test("registration preserves backend validation, conflict and rate limit responses", async t => {
  const { route } = setup(t);
  for (const [status, code] of [[400, "invalid_phone"], [409, "email_unavailable"], [429, "rate_limited"]]) {
    globalThis.fetch = async () => Response.json({ code }, { status });
    const response = await route("auth/register").POST(request("auth/register", "POST", registration));
    assert.equal(response.status, status);
    assert.deepEqual(await response.json(), { code });
    assert.equal(response.headers.get("set-cookie"), null);
  }
});

test("registration never grants administrative access even if an upstream response has that role", async t => {
  const { route } = setup(t);
  const paths = [];
  globalThis.fetch = async (url, init) => {
    paths.push(url);
    if (url.endsWith("/logout")) {
      assert.equal(init.headers.Authorization, "Bearer private-session-token");
      return new Response(null, { status: 204 });
    }
    return Response.json({ ...auth, user: { ...auth.user, role: "ADMIN" } });
  };
  const response = await route("auth/register").POST(request("auth/register", "POST", registration));
  assert.equal(response.status, 403);
  assert.equal(response.headers.get("set-cookie"), null);
  assert.equal(paths.length, 2);
});

test("authentication handles unavailable upstream and passes caller cancellation", async t => {
  const { route } = setup(t);
  const controller = new AbortController();
  let signal;
  globalThis.fetch = async (_url, init) => { signal = init.signal; throw new TypeError("offline"); };
  const response = await route("auth/register").POST(request("auth/register", "POST", registration, undefined, controller.signal));
  assert.equal(response.status, 503);
  controller.abort();
  assert.equal(signal.aborted, true);
});

test("station GET is read-only; connect and cancellation use explicit mutations and retain private filtering", async t => {
  const { route } = setup(t);
  const calls = [];
  const snapshot = { inUse: true, session: null, diagnostics: null, station: { id, status: "Charging", availabilityForecast: { estimatedAvailableAt: "2030-01-01T00:00:00Z" } } };
  globalThis.fetch = async (url, init) => {
    calls.push([url, init.method]);
    assert.equal(init.headers.get("Authorization"), "Bearer own-session");
    assert.equal(init.cache, "no-store");
    return Response.json(snapshot);
  };
  const handler = route("driver/stations/[id]/charging");
  const context = { params: Promise.resolve({ id }) };
  for (const method of ["GET", "POST", "DELETE"]) {
    const response = await handler[method](request(`driver/stations/${id}/charging`, method), context);
    assert.equal(response.status, 200);
    assert.deepEqual(await response.json(), snapshot);
    assert.equal(response.headers.get("cache-control"), "no-store");
  }
  assert.deepEqual(calls, [
    [`https://api.example.test/api/v1/driver/stations/${id}/charging`, "GET"],
    [`https://api.example.test/api/v1/driver/stations/${id}/connect`, "POST"],
    [`https://api.example.test/api/v1/driver/stations/${id}/cancel`, "POST"],
  ]);
});

test("station mutations reject CSRF and path injection, and preserve API failures", async t => {
  const { route } = setup(t);
  const handler = route("driver/stations/[id]/charging");
  globalThis.fetch = async () => assert.fail("Invalid request must not be forwarded");
  assert.equal((await handler.POST(request("charging", "POST", undefined, "https://attacker.test"), { params: Promise.resolve({ id }) })).status, 403);
  assert.equal((await handler.GET(request("charging", "GET"), { params: Promise.resolve({ id: "../me" }) })).status, 404);
  globalThis.fetch = async () => Response.json({ code: "station_busy" }, { status: 409 });
  const result = await handler.POST(request("charging"), { params: Promise.resolve({ id }) });
  assert.equal(result.status, 409);
  assert.deepEqual(await result.json(), { code: "station_busy" });
});

test("charger detail forwards the selected connector and rejects malformed connector parameters", async t => {
  const { route } = setup(t);
  const handler = route("driver/stations/[id]/charging");
  const context = { params: Promise.resolve({ id }) };
  let calls = 0;
  globalThis.fetch = async url => {
    calls++;
    assert.equal(url, `https://api.example.test/api/v1/driver/stations/${id}/charging?connector=2`);
    return Response.json({ connector: { number: 2 } });
  };
  const response = await handler.GET(request(`driver/stations/${id}/charging?connector=2`, "GET"), context);
  assert.equal(response.status, 200);
  assert.equal((await response.json()).connector.number, 2);
  for (const value of ["0", "-1", "1.5", "x", "1000"])
    assert.equal((await handler.GET(request(`driver/stations/${id}/charging?connector=${value}`, "GET"), context)).status, 400);
  assert.equal(calls, 1);
});

test("backend forwarding keeps the caller's abort signal", async t => {
  const { backend } = setup(t);
  const controller = new AbortController();
  let forwarded;
  globalThis.fetch = async (_url, init) => { forwarded = init.signal; return Response.json({}); };
  await backend.backend("/driver/live", { signal: controller.signal });
  controller.abort();
  assert.equal(forwarded.aborted, true);
});

test("password change clears the web session on 204 and account action cannot resolve prototype keys", async t => {
  const { route } = setup(t);
  const { POST } = route("account/[action]");
  globalThis.fetch = async (url, init) => {
    assert.equal(url, "https://api.example.test/api/v1/auth/password");
    assert.equal(init.method, "POST");
    return new Response(null, { status: 204 });
  };
  const response = await POST(request("account/password", "POST", { currentPassword: "old", newPassword: "a-long-new-password" }), { params: Promise.resolve({ action: "password" }) });
  assert.equal(response.status, 204);
  assert.equal(await response.text(), "");
  assert.match(response.headers.get("set-cookie"), /evcharging_session=;/);
  globalThis.fetch = async () => assert.fail("Unknown action cannot reach the API");
  assert.equal((await POST(request("account/toString", "POST", {}), { params: Promise.resolve({ action: "toString" }) })).status, 404);
});

test("SSE returns the first event while the upstream remains open and propagates cancellation", async t => {
  const { route } = setup(t);
  let upstream, signal;
  globalThis.fetch = async (url, init) => {
    assert.equal(url, "https://api.example.test/api/v1/driver/stations/events");
    assert.equal(init.headers.Authorization, "Bearer own-session");
    signal = init.signal;
    return new Response(new ReadableStream({ start(controller) { upstream = controller; } }));
  };
  const controller = new AbortController();
  const response = await route("driver/events").GET(request("driver/events", "GET", undefined, undefined, controller.signal));
  assert.equal(response.headers.get("content-type"), "text/event-stream");
  assert.equal(response.headers.get("x-accel-buffering"), "no");
  const reader = response.body.getReader();
  const pending = reader.read();
  upstream.enqueue(new TextEncoder().encode("event: station\ndata: {}\n\n"));
  const first = await pending;
  assert.equal(first.done, false);
  assert.match(new TextDecoder().decode(first.value), /event: station/);
  controller.abort();
  assert.equal(signal.aborted, true);
  await reader.cancel();
});

test("SSE refuses an anonymous subscriber without contacting the backend", async t => {
  const { route } = setup(t, null);
  globalThis.fetch = async () => assert.fail("Anonymous request cannot reach upstream");
  assert.equal((await route("driver/events").GET(request("driver/events", "GET"))).status, 401);
});
