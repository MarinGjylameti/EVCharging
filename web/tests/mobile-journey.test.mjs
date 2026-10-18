import test from "node:test";
import assert from "node:assert/strict";
import React from "react";
import { renderToStaticMarkup } from "react-dom/server";
import { loadTypescript } from "./helpers/load-typescript.mjs";

function hooks() {
  const values = []; let index = 0;
  const state = { effects: [], reset() { index = 0; state.effects = []; }, react: {
    ...React,
    useState(initial) { const i = index++; if (!(i in values)) values[i] = typeof initial === "function" ? initial() : initial; return [values[i], next => { values[i] = typeof next === "function" ? next(values[i]) : next; }]; },
    useRef(initial) { const i = index++; values[i] ??= { current: initial }; return values[i]; },
    useEffect(effect) { state.effects.push(effect); },
  } }; return state;
}
function nodes(tree, predicate) { return !tree || typeof tree !== "object" ? [] : [...(predicate(tree) ? [tree] : []), ...[tree.props?.children].flat(Infinity).flatMap(child => nodes(child, predicate))]; }
const station = { id: "station-a", name: "Station A", code: "CP-001", city: "Tirana", address: "Address", latitude: 41, longitude: 19, powerKW: 22, pricePerKWh: .35, connectorCount: 1, connectorType: "CCS2", status: "Available", version: 1, isArchived: false };

test("phone station selection opens the editor, protects unsaved changes and returns to the list", t => {
  const oldWindow = globalThis.window, oldFrame = globalThis.requestAnimationFrame;
  let discard = false, confirmations = 0;
  globalThis.window = { confirm: () => { confirmations++; return discard; } };
  globalThis.requestAnimationFrame = () => 0;
  t.after(() => { globalThis.window = oldWindow; globalThis.requestAnimationFrame = oldFrame; });
  const state = hooks();
  const { StationManager } = loadTypescript(new URL("../src/features/stations/station-manager.tsx", import.meta.url), {
    react: state.react, "./station-map-picker": { StationMapPicker: () => null }, "../operations/device-manager": { DeviceManager: () => null },
    "../operations/use-operations-live": { useOperationsLive: () => true }, "../operations/api": {},
  });
  const render = () => { state.reset(); return StationManager({ initial: [station], language: "en", canEdit: true }); };
  const grid = tree => nodes(tree, n => n.props["data-editor-open"] !== undefined)[0];
  let tree = render(); assert.equal(grid(tree).props["data-editor-open"], false);
  nodes(tree, n => n.props.className === "station-admin-row")[0].props.onClick();
  tree = render(); assert.equal(grid(tree).props["data-editor-open"], true);
  nodes(tree, n => n.type === "input" && n.props.value === "Station A")[0].props.onChange({ target: { value: "Edited name" } });
  tree = render(); nodes(tree, n => n.props.className === "button-outline station-mobile-back")[0].props.onClick();
  tree = render(); assert.equal(grid(tree).props["data-editor-open"], true); assert.equal(confirmations, 1);
  assert.ok(nodes(tree, n => n.type === "input" && n.props.value === "Edited name").length);
  discard = true; nodes(tree, n => n.props.className === "button-outline station-mobile-back")[0].props.onClick();
  tree = render(); assert.equal(grid(tree).props["data-editor-open"], false);
  assert.ok(nodes(tree, n => n.type === "input" && n.props.value === "Station A").length);
});

test("session filters reject invalid dates and late responses cannot replace a newer result", async () => {
  const state = hooks(), pending = []; let refresh;
  const { SessionsMonitor, sessionQuery } = loadTypescript(new URL("../src/features/operations/sessions-monitor.tsx", import.meta.url), {
    react: state.react, "./use-operations-live": { useOperationsLive: fn => { refresh = fn; return true; } },
    "./api": { operations: (...args) => new Promise(resolve => pending.push({ args, resolve })), failure: () => "Read failed" },
  });
  const filter = { stationId: "", locationId: "", status: "", from: "2026-10-18T12:00", until: "2026-10-18T11:00", driver: "", page: 1 };
  assert.throws(() => sessionQuery(filter), /invalid_session_dates/);
  assert.throws(() => sessionQuery({ ...filter, from: "invalid", until: "" }), /invalid_session_dates/);
  const initial = { items: [], total: 0, page: 1, pageSize: 20 };
  const render = () => { state.reset(); return SessionsMonitor({ initial, stations: [], locations: [], language: "en" }); };
  let tree = render(); const oldRead = refresh(new AbortController().signal);
  nodes(tree, n => n.type === "input" && n.props.placeholder === "Search for a driver")[0].props.onChange({ target: { value: "New driver" } });
  tree = render(); nodes(tree, n => n.type === "form")[0].props.onSubmit({ preventDefault() {} });
  render(); const newRead = refresh(new AbortController().signal);
  assert.match(pending[1].args[2], /driver=New\+driver/);
  pending[1].resolve({ ...initial, total: 3 }); await newRead;
  pending[0].resolve({ ...initial, total: 999 }); await oldRead;
  const html = renderToStaticMarkup(render());
  assert.match(html, /3 sessions/); assert.doesNotMatch(html, /999 sessions/);
});
