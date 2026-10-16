import test from "node:test";
import assert from "node:assert/strict";
import React from "react";
import { renderToStaticMarkup } from "react-dom/server";
import { loadTypescript } from "./helpers/load-typescript.mjs";

test("server feature lookup enables tools only on an explicit boolean true and fails closed", async () => {
  for (const value of [true, false, "true", 1, null, undefined]) {
    const { runtimeFeatures } = loadTypescript(new URL("../src/lib/runtime-features.ts", import.meta.url), {
      react: { cache: fn => fn },
      "./backend": { backend: async path => { assert.equal(path, "/features"); return Response.json({ testMode: value }); } },
    });
    assert.deepEqual(await runtimeFeatures(), { testMode: value === true });
  }
  for (const backend of [async () => new Response(null, { status: 401 }), async () => { throw new Error("offline"); }]) {
    const { runtimeFeatures } = loadTypescript(new URL("../src/lib/runtime-features.ts", import.meta.url), { react: { cache: fn => fn }, "./backend": { backend } });
    assert.deepEqual(await runtimeFeatures(), { testMode: false });
  }
});

test("a missing feature provider defaults to production presentation", () => {
  const { useTestMode, RuntimeFeaturesProvider } = loadTypescript(new URL("../src/components/runtime-features.tsx", import.meta.url));
  const Probe = () => React.createElement("p", null, useTestMode() ? "Testing tools" : "Charging");
  assert.equal(renderToStaticMarkup(React.createElement(Probe)), "<p>Charging</p>");
  assert.equal(renderToStaticMarkup(React.createElement(RuntimeFeaturesProvider, { testMode: true }, React.createElement(Probe))), "<p>Testing tools</p>");
});

test("production card assignment requires a card and explains inactive legacy demo assignments", () => {
  const { DeviceDriverForm } = loadTypescript(new URL("../src/features/operations/device-driver-form.tsx", import.meta.url), {
    react: { ...React, useEffect: () => {} },
    "../../components/runtime-features": { useTestMode: () => false },
    "./api": {},
  });
  const html = renderToStaticMarkup(React.createElement(DeviceDriverForm, { device: { assignedDriverId: "driver-1", cardIdTag: null }, stationId: "station-1", sq: false, disabled: false, onSaved() {} }));
  assert.match(html, /textarea required/);
  assert.match(html, /Previous assignments without a card are inactive/);
  assert.doesNotMatch(html, /every new charge|optional|Authorize\/StartTransaction/);
});
