import test from "node:test";
import assert from "node:assert/strict";
import React from "react";
import { renderToStaticMarkup } from "react-dom/server";
import { loadTypescript } from "./helpers/load-typescript.mjs";

const account = {
  id: "profile-1", email: "account@example.test", firstName: "Arta", lastName: "Deda",
  role: "DRIVER", language: "sq", pushEnabled: false, version: 3, phone: "+355691234567",
  vehicleMake: "Volkswagen", vehicleModel: "ID.4", batteryType: "Li-ion", batteryCapacityKWh: 77,
};

function mount(initial) {
  const state = [];
  let cursor = 0;
  const { AccountForm } = loadTypescript(new URL("../src/features/account/account-form.tsx", import.meta.url), {
    react: { ...React,
      useState(value) { const index = cursor++; if (!(index in state)) state[index] = value; return [state[index], next => { state[index] = next; }]; },
      useEffect: () => {},
    },
    "next/navigation": { useRouter: () => ({ replace: () => {}, refresh: () => {} }) },
  });
  return () => { cursor = 0; return AccountForm({ initial }); };
}

function find(node, predicate) {
  if (!node || typeof node !== "object") return undefined;
  if (predicate(node)) return node;
  for (const child of React.Children.toArray(node.props?.children)) {
    const match = find(child, predicate);
    if (match) return match;
  }
}

test("only Drivers see vehicle and battery fields; administrative profiles keep contact and password forms", () => {
  for (const language of ["sq", "en"]) {
    for (const role of ["DRIVER", "ADMIN", "SUPERADMIN"]) {
      const html = renderToStaticMarkup(mount({ ...account, role, language })());
      assert.match(html, /name="firstName"/);
      assert.match(html, /name="phone"/);
      assert.match(html, /name="currentPassword"/);
      for (const field of ["vehicleMake", "vehicleModel", "batteryType", "batteryCapacityKWh"]) {
        assert.equal(html.includes(`name="${field}"`), role === "DRIVER");
      }
      assert.equal(html.includes(language === "sq" ? "Makina jote" : "Your vehicle"), role === "DRIVER");
      assert.equal(html.includes("battery-capacity-help"), role === "DRIVER");
    }
  }
});

test("saving an administrative contact profile preserves hidden fields in the replacement API payload", async t => {
  const previous = { fetch: globalThis.fetch, FormData: globalThis.FormData };
  t.after(() => Object.assign(globalThis, previous));
  const contact = { firstName: "Arta Updated", lastName: "Deda", phone: "+355691234568" };
  globalThis.FormData = class { get(name) { return contact[name] ?? null; } };
  for (const role of ["ADMIN", "SUPERADMIN"]) {
    let payload;
    globalThis.fetch = async (url, init) => {
      assert.equal(url, "/api/account/profile");
      payload = JSON.parse(init.body);
      return Response.json({ ...account, ...payload, role, version: 4 });
    };
    const render = mount({ ...account, role });
    find(render(), node => node.type === "form").props.onSubmit({ preventDefault() {}, currentTarget: {} });
    await new Promise(resolve => setImmediate(resolve));
    assert.deepEqual(payload, { ...contact, vehicleMake: "Volkswagen", vehicleModel: "ID.4", batteryType: "Li-ion", batteryCapacityKWh: 77, version: 3 });
    assert.match(renderToStaticMarkup(render()), /Ndryshimet u ruajtën/);
  }
});

test("Drivers can still edit vehicle details and clear an optional battery capacity", async t => {
  const previous = { fetch: globalThis.fetch, FormData: globalThis.FormData };
  t.after(() => Object.assign(globalThis, previous));
  for (const [capacity, expected] of [["60.50", 60.5], ["", null]]) {
    const fields = { firstName: "Arta", lastName: "Deda", phone: account.phone,
      vehicleMake: "Renault", vehicleModel: "Megane", batteryType: "Li-ion", batteryCapacityKWh: capacity };
    globalThis.FormData = class { get(name) { return fields[name] ?? null; } };
    let payload;
    globalThis.fetch = async (_url, init) => { payload = JSON.parse(init.body); return Response.json({ ...account, ...payload, version: 4 }); };
    find(mount(account)(), node => node.type === "form").props.onSubmit({ preventDefault() {}, currentTarget: {} });
    await new Promise(resolve => setImmediate(resolve));
    assert.deepEqual(payload, { ...fields, batteryCapacityKWh: expected, version: 3 });
  }
});
