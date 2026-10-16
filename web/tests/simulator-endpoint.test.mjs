import test from "node:test";
import assert from "node:assert/strict";
import { loadTypescript } from "./helpers/load-typescript.mjs";

const endpoint = loadTypescript(new URL("../src/lib/simulator-endpoint.ts", import.meta.url));
const { simulatorSocketBase } = endpoint;
const demoApi = "https://vcharging-api-demo.onrender.com";
const cloudflareBase = "wss://evcharging-ocpp.evcharging-demo.workers.dev/ws/cs/";

test("the simulator route rejects direct navigation when backend test capability is off", async () => {
  const { default: SimulatorPage } = loadTypescript(new URL("../src/app/driver/simulator/page.tsx", import.meta.url), {
    "@/lib/backend": {
      currentUser: async () => ({ language: "en" }),
      backend: async path => Response.json(path === "/driver/charging-sessions" ? { canTestSimulator: false } : [{ code: "SIM-001", isDemo: true }]),
      apiOrigin: () => assert.fail("Disabled simulator must not resolve a socket"),
    },
    "@/lib/simulator-endpoint": endpoint,
    "@/features/driver/automatic-simulator-panel": { AutomaticSimulatorPanel: () => assert.fail("Disabled simulator rendered") },
    "../../simulator.css": {},
    "next/navigation": { notFound: () => { throw new Error("route-not-found"); }, redirect: () => assert.fail("Unexpected redirect") },
  });
  await assert.rejects(SimulatorPage({ searchParams: Promise.resolve({}) }), /route-not-found/);
});

test("the Render demo simulator uses Cloudflare while local and other deployments keep their own API", () => {
  assert.equal(simulatorSocketBase(demoApi), cloudflareBase);
  assert.equal(simulatorSocketBase(demoApi, "   "), cloudflareBase);
  assert.equal(simulatorSocketBase("http://127.0.0.1:5202"), "ws://127.0.0.1:5202/ws/cs/");
  assert.equal(simulatorSocketBase("http://localhost:5201"), "ws://localhost:5201/ws/cs/");
  assert.equal(simulatorSocketBase("https://api.example.test"), "wss://api.example.test/ws/cs/");
});

test("an explicit socket base takes precedence and normalizes the station separator", () => {
  assert.equal(simulatorSocketBase(demoApi, " wss://ocpp.example.test/ws/cs "), "wss://ocpp.example.test/ws/cs/");
  assert.equal(simulatorSocketBase(demoApi, "wss://vcharging-api-demo.onrender.com/ws/cs/"),
    "wss://vcharging-api-demo.onrender.com/ws/cs/");
});

test("invalid endpoints, credentials, preselected identities and TLS downgrades fail instead of bypassing Cloudflare", () => {
  for (const base of ["/ws/cs/", "https://ocpp.example.test/ws/cs/", "wss://ocpp.example.test/",
    "wss://ocpp.example.test/ws/cs/SIM-007", "wss://user:password@ocpp.example.test/ws/cs/",
    "wss://ocpp.example.test/ws/cs/?token=secret", "wss://ocpp.example.test/ws/cs/#fragment",
    "ws://ocpp.example.test/ws/cs/"]) {
    assert.throws(() => simulatorSocketBase(demoApi, base));
  }
});

test("the simulator page connects SIM-007 through Cloudflare with OCPP 1.6 and sends BootNotification", async t => {
  const previousBase = process.env.EVCHARGING_SIMULATOR_WS_BASE_URL;
  delete process.env.EVCHARGING_SIMULATOR_WS_BASE_URL;
  t.after(() => {
    if (previousBase === undefined) delete process.env.EVCHARGING_SIMULATOR_WS_BASE_URL;
    else process.env.EVCHARGING_SIMULATOR_WS_BASE_URL = previousBase;
  });
  const station = { id: "station-seven", code: "SIM-007", isDemo: true };
  const calls = [];
  const { default: SimulatorPage } = loadTypescript(new URL("../src/app/driver/simulator/page.tsx", import.meta.url), {
    "@/lib/backend": {
      apiOrigin: () => demoApi,
      currentUser: async () => ({ language: "sq", batteryCapacityKWh: 60 }),
      backend: async path => {
        calls.push(path);
        if (path === "/driver/charging-sessions") return Response.json({ canTestSimulator: true });
        assert.equal(path, "/driver/stations");
        return Response.json([station]);
      },
    },
    "@/lib/simulator-endpoint": endpoint,
    "@/features/driver/automatic-simulator-panel": { AutomaticSimulatorPanel: () => null },
    "../../simulator.css": {},
    "next/navigation": { notFound: () => assert.fail("Unexpected 404"), redirect: () => assert.fail("Unexpected redirect") },
  });
  const page = await SimulatorPage({ searchParams: Promise.resolve({ station: "SIM-007" }) });
  assert.equal(page.props.socketBase, cloudflareBase);
  assert.equal(page.props.selected, "SIM-007");
  assert.deepEqual(calls, ["/driver/charging-sessions", "/driver/stations"]);

  const { AutomaticSimulator } = loadTypescript(new URL("../src/features/driver/automatic-simulator.ts", import.meta.url));
  const messages = [];
  const simulator = new AutomaticSimulator({ url: page.props.socketBase + page.props.selected, stationCode: page.props.selected,
    capacityKWh: 60, initialSoc: 20, targetSoc: 80, powerKW: 22, meterStartWh: 0, intervalSeconds: 5, speed: 1 },
    () => {}, () => {}, (url, protocol) => {
      assert.equal(url, cloudflareBase + "SIM-007");
      assert.equal(protocol, "ocpp1.6");
      const socket = {
        send(data) {
          const frame = JSON.parse(data);
          messages.push(frame);
          const payload = frame[2] === "BootNotification" ? { status: "Accepted", interval: 30 } : {};
          queueMicrotask(() => socket.onmessage?.({ data: JSON.stringify([3, frame[1], payload]) }));
        },
        close() { queueMicrotask(() => socket.onclose?.()); },
      };
      queueMicrotask(() => socket.onopen?.());
      return socket;
    });
  t.after(() => simulator.dispose());
  await simulator.connect();
  assert.equal(simulator.snapshot.connected, true);
  assert.deepEqual(messages.map(frame => frame[2]), ["BootNotification", "StatusNotification"]);
  assert.equal(messages[0][3].chargePointSerialNumber, "SIM-007");
});
