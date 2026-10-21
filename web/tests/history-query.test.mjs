import test from "node:test";
import assert from "node:assert/strict";
import { loadTypescript } from "./helpers/load-typescript.mjs";
const compile = (path, dependencies = {}) => loadTypescript(new URL(path, import.meta.url), dependencies);
const query = compile("../../shared/history-query.ts");
const { defaultHistoryFilters } = compile("../../shared/history.ts");
const tick = async () => { for(let i=0;i<15;i++) await Promise.resolve(); };

test("server history query includes the requested page, calendar range and all filters", () => {
  const params = new URLSearchParams(query.historyQuery({ period: "custom", from: "2020-01-01", until: "2020-12-31", stationId: "station&private=1", energy: "large", currency: "EUR" }, 14, "2020-03-01"));
  assert.equal(params.get("page"), "14"); assert.equal(params.get("pageSize"), "8");
  assert.equal(params.get("stationId"), "station&private=1"); assert.equal(params.has("private"), false);
  assert.equal(params.get("from"), "2020-01-01"); assert.equal(params.get("bucket"), "2020-03-01");
  assert.equal(params.get("energy"), "large"); assert.equal(params.get("currency"), "EUR");
  assert.equal(new URLSearchParams(query.historyQuery({ ...defaultHistoryFilters, from: "old", until: "old" }, 1, "")).has("from"), false);
});

test("late pages and cancelled reads cannot replace the selected filter", async () => {
  const pending = [], snapshots = [];
  const loader = query.createHistoryQuery((key, signal) => new Promise(resolve => pending.push({ key, signal, resolve })), value => snapshots.push(value));
  const old = loader.load("old"); const current = loader.load("current");
  assert.equal(pending[0].signal.aborted, true);
  pending[1].resolve({ ...query.emptyHistoryPage(), total: 120 }); await current;
  const length = snapshots.length; pending[0].resolve({ total: 999 }); await old;
  assert.equal(snapshots.length, length); assert.equal(snapshots.at(-1).data.total, 120);
  const controller = new AbortController(); const cancelled = loader.load("cancelled", controller.signal);
  controller.abort(); const boundary = snapshots.length; pending[2].resolve({ total: 500 }); await cancelled;
  assert.equal(snapshots.length, boundary);
});

test("failed refresh retains only the same query's cached totals and retry recovers", async () => {
  const snapshots = []; let failure = false;
  const initial = { key: "same", data: { ...query.emptyHistoryPage(), total: 107 }, pending: false, error: "" };
  const loader = query.createHistoryQuery(async () => { if(failure) throw new Error("service_unavailable"); return { ...query.emptyHistoryPage(), total: 108 }; }, value => snapshots.push(value), initial);
  failure = true; await loader.load("same"); assert.equal(snapshots.at(-1).data.total, 107); assert.equal(snapshots.at(-1).error, "service_unavailable");
  await loader.load("different"); assert.equal(snapshots.at(-1).data, null);
  failure = false; await loader.load("different"); assert.equal(snapshots.at(-1).data.total, 108); assert.equal(snapshots.at(-1).error, "");
});

test("web hook requests a new server page and hides previous-filter totals immediately", async t => {
  const values = []; let index = 0, effect; const calls = [];
  const react = { useRef(value) { const i=index++; values[i] ??= {current:value}; return values[i]; }, useState(value) { const i=index++; if(!(i in values))values[i]=value; return [values[i], v=>{values[i]=v;}]; }, useEffect(fn) { effect=fn; } };
  t.mock.method(globalThis, "fetch", async (url, options) => { calls.push({url,options}); return Response.json({ ...query.emptyHistoryPage(), total: 250, page: 2 }); });
  const { useHistoryPage } = compile("../src/features/driver/use-history-page.ts", { react, "../../../../shared/history-query": query });
  const initial = { ...query.emptyHistoryPage(), total: 100 };
  const render = (page = 1) => { index=0; return useHistoryPage(initial, defaultHistoryFilters, page, ""); };
  assert.equal(render().data.total, 100); const cleanup = effect(); assert.equal(calls.length, 0);
  cleanup(); assert.equal(render(2).data, null); effect(); await tick();
  assert.equal(calls.length, 1); assert.match(calls[0].url, /page=2/); assert.equal(calls[0].options.cache, "no-store");
  assert.equal(render(2).data.total, 250);
});

test("history BFF forwards only filters with cancellation and cannot accept a driver override", async () => {
  const calls = [];
  const { GET } = compile("../src/app/api/driver/history/search/route.ts", { "@/lib/backend": { backend: async (...args) => { calls.push(args); return Response.json({ total: 107 }); } } });
  const request = new Request("https://app.example/api/driver/history/search?period=6m&page=14");
  const result = await GET(request);
  assert.equal(result.status, 200); assert.equal(result.headers.get("Cache-Control"), "no-store");
  assert.equal(calls[0][0], "/driver/history/search?period=6m&page=14"); assert.equal(calls[0][1].signal, request.signal);
  for(const search of ["driverId=someone-else", "page=1&page=2"]) assert.equal((await GET(new Request("https://app.example/api/driver/history/search?"+search))).status, 400);
  assert.equal(calls.length, 1);
});
