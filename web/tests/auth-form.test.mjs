import test from "node:test";
import assert from "node:assert/strict";
import React from "react";
import { renderToStaticMarkup } from "react-dom/server";
import { loadTypescript } from "./helpers/load-typescript.mjs";

function mount(t, register = true) {
  const original = { fetch: globalThis.fetch, FormData: globalThis.FormData };
  t.after(() => Object.assign(globalThis, original));
  const state = [], refs = [], redirects = [];
  let cursor = 0, refCursor = 0, cleanup, readyCalls = 0;
  const { LoginForm } = loadTypescript(new URL("../src/features/auth/login-form.tsx", import.meta.url), {
    react: { ...React,
      useState(initial) { const index = cursor++; if (!(index in state)) state[index] = initial; return [state[index], value => { state[index] = typeof value === "function" ? value(state[index]) : value; }]; },
      useRef(initial) { const index = refCursor++; return refs[index] ??= { current: initial }; },
      useEffect(fn) { cleanup = fn(); },
    },
    "next/navigation": { useRouter: () => ({ replace: url => redirects.push(url), refresh: () => {} }) },
    "next/link": { default: ({ href, children }) => React.createElement("a", { href }, children) },
    "./wait-for-api": { waitForApi: async () => { readyCalls++; return true; } },
  });
  const render = () => { cursor = 0; refCursor = 0; return LoginForm({ register }); };
  function find(node, predicate) {
    if (!node || typeof node !== "object") return undefined;
    if (predicate(node)) return node;
    for (const child of React.Children.toArray(node.props?.children)) { const match = find(child, predicate); if (match) return match; }
  }
  return {
    state, redirects, readyCalls: () => readyCalls,
    html: () => renderToStaticMarkup(render()),
    language: language => find(render(), node => node.type === "button" && node.props.children === language).props.onClick(),
    unmount: () => cleanup?.(),
    submit(values) {
      globalThis.FormData = class { get(name) { return values[name] ?? null; } };
      return find(render(), node => node.type === "form").props.onSubmit({ preventDefault() {}, currentTarget: {} });
    },
  };
}
const fields = { firstName: "Arta", lastName: "Deda", email: " arta@example.test ", phone: "+355691234567", password: "a-long-driver-passphrase", confirmPassword: "a-long-driver-passphrase" };

test("registration renders labelled required contact fields, password constraints and sign-in navigation", t => {
  const form = mount(t);
  const html = form.html();
  for (const field of ["firstName", "lastName", "email", "phone", "password", "confirmPassword"]) {
    assert.match(html, new RegExp(`<label for="${field}">`));
    assert.match(html, new RegExp(`<input(?=[^>]*name="${field}")(?=[^>]*required)[^>]*>`));
  }
  assert.match(html, /autoComplete="tel"/);
  assert.match(html, /minLength="12"/);
  assert.match(html, /href="\/"/);
  form.language("EN");
  assert.match(form.html(), /Create your account/);
  assert.match(form.html(), /Mobile number/);
});

test("registration blocks different passwords before readiness or API requests", async t => {
  const form = mount(t);
  globalThis.fetch = async () => assert.fail("Mismatched passwords cannot reach API");
  await form.submit({ ...fields, confirmPassword: "different" });
  assert.equal(form.readyCalls(), 0);
  assert.match(form.html(), /Fjalëkalimet nuk përputhen/);
  assert.equal(form.redirects.length, 0);
});

test("registration submits the six account fields once, keeps confirmation local and redirects to own driver workspace", async t => {
  const form = mount(t);
  let requests = 0, resolve;
  globalThis.fetch = async (url, init) => {
    requests++;
    assert.equal(url, "/api/auth/register");
    assert.deepEqual(JSON.parse(init.body), { firstName: fields.firstName, lastName: fields.lastName, email: "arta@example.test", phone: fields.phone, password: fields.password, language: "sq" });
    return new Promise(done => { resolve = done; });
  };
  const pending = form.submit(fields);
  await Promise.resolve();
  await form.submit(fields);
  assert.equal(requests, 1);
  resolve(Response.json({ user: { role: "DRIVER" } }));
  await pending;
  assert.deepEqual(form.redirects, ["/driver"]);
  assert.equal(form.state[1], false);
});

test("duplicate email and invalid mobile errors remain on the form and permit correction", async t => {
  const form = mount(t);
  for (const [status, code, text] of [[409, "email_unavailable", /Ky email është përdorur/], [400, "invalid_phone", /Shkruaj numrin/], [429, "rate_limited", /Shumë përpjekje/]]) {
    globalThis.fetch = async () => Response.json({ code }, { status });
    await form.submit(fields);
    assert.match(form.html(), text);
    assert.equal(form.state[1], false);
    assert.equal(form.redirects.length, 0);
  }
});

test("leaving registration aborts the active request without showing an unavailable error", async t => {
  const form = mount(t);
  let signal;
  globalThis.fetch = async (_url, init) => {
    signal = init.signal;
    return new Promise((_resolve, reject) => signal.addEventListener("abort", () => reject(signal.reason), { once: true }));
  };
  const pending = form.submit(fields);
  await Promise.resolve();
  form.unmount();
  await pending;
  assert.equal(signal.aborted, true);
  assert.equal(form.state[2], "");
});

test("login keeps its existing credentials payload and exposes registration navigation", async t => {
  const form = mount(t, false);
  assert.match(form.html(), /href="\/register"/);
  assert.doesNotMatch(form.html(), /name="phone"/);
  globalThis.fetch = async (url, init) => {
    assert.equal(url, "/api/auth/login");
    assert.deepEqual(JSON.parse(init.body), { email: "arta@example.test", password: fields.password });
    return Response.json({ user: { role: "ADMIN" } });
  };
  await form.submit(fields);
  assert.deepEqual(form.redirects, ["/dashboard"]);
});
