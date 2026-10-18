import test from "node:test";
import assert from "node:assert/strict";
import { createRequire } from "node:module";
import { compile, hooks, jsx, nodes } from "./helpers/compile.mjs";
const require = createRequire(import.meta.url);
const { storeLinks } = require("../config/store-links.cjs");
const { buildEnvironment } = require("../config/build-environment.cjs");
const pages = { EXPO_PUBLIC_PRIVACY_URL: "https://client-evcharging.al/privacy", EXPO_PUBLIC_ACCOUNT_DELETION_URL: "https://client-evcharging.al/delete-account" };
const setup = { EAS_BUILD_PROFILE: "production", EXPO_PUBLIC_API_BASE_URL: "https://api.example.com", EXPO_PUBLIC_EAS_PROJECT_ID: "11111111-2222-3333-4444-555555555555" };

test("production requires both client pages while internal preview remains available for hardware testing", () => {
  assert.throws(() => buildEnvironment(setup), /PRIVACY_URL/);
  assert.throws(() => buildEnvironment({ ...setup, EXPO_PUBLIC_PRIVACY_URL: pages.EXPO_PUBLIC_PRIVACY_URL }), /ACCOUNT_DELETION_URL/);
  assert.equal(buildEnvironment({ ...setup, ...pages }).profile, "production");
  assert.equal(buildEnvironment({ ...setup, EAS_BUILD_PROFILE: "preview" }).profile, "preview");
});

test("store links reject insecure URLs, secrets, private addresses and placeholders", () => {
  for (const url of ["http://client-evcharging.al/privacy", "https://user:secret@client-evcharging.al", "https://client-evcharging.al/privacy?token=secret", "javascript:alert(1)", "https://localhost/privacy", "https://127.0.0.1/privacy", "https://10.0.0.1/privacy", "https://192.168.1.1/privacy", "https://[::1]/privacy", "https://example.com/privacy", "https://client.test/privacy"]) {
    assert.throws(() => storeLinks({ ...pages, EXPO_PUBLIC_PRIVACY_URL: url }, true), undefined, url);
  }
  assert.deepEqual(storeLinks({}, false), { privacy: "", deletion: "" });
});

test("privacy is available before login; account settings open only the configured deletion page", async t => {
  const previous = Object.fromEntries(Object.keys(pages).map(key => [key, process.env[key]]));
  Object.assign(process.env, pages);
  t.after(() => { for (const [key, value] of Object.entries(previous)) value === undefined ? delete process.env[key] : process.env[key] = value; });
  const state = hooks(), opened = [];
  const { PrivacyLinks } = compile("../../src/PrivacyLinks.tsx", {
    react: state.react, "react/jsx-runtime": jsx,
    "react-native": { View: "View", Text: "Text", Linking: { openURL: async value => opened.push(value) } },
    "./ui": { Button: "Button", Notice: "Notice", styles: {} },
  });
  const render = account => { state.reset(); return PrivacyLinks({ language: "en", account }); };
  assert.equal(nodes(render(false), n => n.type === "Button").length, 1);
  const tree = render(true), buttons = nodes(tree, n => n.type === "Button");
  assert.equal(buttons.length, 2); buttons[1].props.onPress(); await Promise.resolve();
  assert.deepEqual(opened, [pages.EXPO_PUBLIC_ACCOUNT_DELETION_URL]);
  assert.match(JSON.stringify(tree), /does not delete your account/);
});
