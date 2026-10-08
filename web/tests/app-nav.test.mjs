import test from "node:test";
import assert from "node:assert/strict";
import React from "react";
import { renderToStaticMarkup } from "react-dom/server";
import { loadTypescript } from "./helpers/load-typescript.mjs";

const source = new URL("../src/components/app-nav.tsx", import.meta.url);
const link = {default: ({children, ...props}) => React.createElement("a", props, children)};
function find(tree, predicate) {
  if (!tree || typeof tree !== "object") return;
  if (predicate(tree)) return tree;
  for (const child of [tree.props?.children].flat(Infinity)) {
    const match = find(child, predicate);
    if (match) return match;
  }
}
function staticNav(pathname, props) {
  const {AppNav} = loadTypescript(source, {"next/link": link, "next/navigation": {usePathname: () => pathname}});
  return renderToStaticMarkup(React.createElement(AppNav, props));
}

test("admin and driver navigation retain their permitted destinations and translated accessible labels", () => {
  const admin = staticNav("/dashboard/account", {area: "dashboard", language: "en"});
  for (const section of ["stations", "sessions", "notifications", "locations", "account", "prices", "audit"]) assert.match(admin, new RegExp(`href="/dashboard/${section}"`));
  for (const section of ["administrators", "logs", "drivers"]) assert.doesNotMatch(admin, new RegExp(`href="/dashboard/${section}"`));
  assert.match(admin, /aria-haspopup="dialog"/);
  assert.match(admin, /aria-label="More: Account"/);
  const superadmin = staticNav("/dashboard/logs", {area: "dashboard", language: "sq", superadmin: true});
  for (const section of ["administrators", "logs", "drivers"]) assert.match(superadmin, new RegExp(`href="/dashboard/${section}"`));
  assert.match(superadmin, /aria-label="Më shumë: Loget e lidhjes"/);
  assert.match(superadmin, /aria-label="Seksionet e tjera"/);
  const driver = staticNav("/driver/history/receipt", {area: "driver", language: "en"});
  assert.match(driver, /href="\/driver\/history"[^>]*aria-current="page"/);
  assert.doesNotMatch(driver, /<dialog|app-nav-more|href="\/dashboard/);
});

test("More opens a modal, unlocks scrolling on close and closes on navigation or desktop resize", t => {
  const previous = {window: globalThis.window, document: globalThis.document};
  let resize;
  const media = {matches: true, addEventListener: (_event, callback) => {resize = callback;}, removeEventListener: () => {resize = undefined;}};
  globalThis.window = {matchMedia: () => media};
  globalThis.document = {body: {style: {overflow: "auto"}}};
  t.after(() => {
    for (const key of ["window", "document"]) {
      if (previous[key] === undefined) delete globalThis[key]; else globalThis[key] = previous[key];
    }
  });
  let pathname = "/dashboard/logs", state = null, effect;
  const dialog = {open: false, showModal() {this.open = true;}, close() {this.open = false;}};
  const {AppNav} = loadTypescript(source, {
    "next/link": link, "next/navigation": {usePathname: () => pathname},
    react: {...React, useId: () => "test-menu", useRef: () => ({current: dialog}),
      useState: () => [state, value => {state = value;}], useEffect: callback => {effect = callback;}}
  });
  const render = () => AppNav({area: "dashboard", language: "sq", superadmin: true});
  find(render(), node => node.props?.className === "app-nav-more").props.onClick();
  assert.equal(find(render(), node => node.props?.className === "app-nav-more").props["aria-expanded"], true);
  let cleanup = effect();
  assert.equal(dialog.open, true);
  assert.equal(document.body.style.overflow, "hidden");
  const sheet = find(render(), node => node.type === "dialog");
  assert.equal(sheet.props["aria-labelledby"], "test-menu-title");
  sheet.props.onCancel(); render(); cleanup();
  assert.equal(dialog.open, false);
  assert.equal(document.body.style.overflow, "auto");

  find(render(), node => node.props?.className === "app-nav-more").props.onClick(); render(); cleanup = effect();
  pathname = "/dashboard/account";
  assert.equal(find(render(), node => node.props?.className === "app-nav-more").props["aria-expanded"], false);
  cleanup(); effect(); assert.equal(dialog.open, false);

  find(render(), node => node.props?.className === "app-nav-more").props.onClick(); render(); cleanup = effect();
  media.matches = false; resize(); render(); cleanup();
  assert.equal(dialog.open, false);
  assert.equal(document.body.style.overflow, "auto");
});
