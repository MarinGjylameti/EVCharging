import test from "node:test";
import assert from "node:assert/strict";
import { loadTypescript } from "./helpers/load-typescript.mjs";
import React, { createElement } from "react";
import { renderToStaticMarkup } from "react-dom/server";

function compile(path, dependencies = {}) {
  return loadTypescript(new URL(path, import.meta.url), dependencies);
}

const sharedData = compile("../../shared/history.ts");
const data = compile("../src/features/driver/history-chart-data.ts", { "../../../../shared/history": sharedData });
const { HistoryChart } = compile("../src/features/driver/history-chart.tsx", { "./history-chart-data": data });
const { HistoryView } = compile("../src/features/driver/history-view.tsx", { "./history-chart-data": data, "./history-chart": { HistoryChart }, "next/link": { default: ({ children, ...props }) => createElement("a", props, children) } });
const { buildHistoryMonths, buildHistoryBuckets, historyRange, historyDay, filterHistory, historyPage, historyScale, summarizeHistory, defaultHistoryFilters } = data;
const charge = (endedAt, energyKWh, totalCost, currency = "EUR", extra = {}) => ({
  id: endedAt + currency, startedAt: endedAt, endedAt, energyKWh, totalCost, currency,
  stationId: "station-1", stationCode: "CP-001", stationName: "Central", city: "Tirana", unitPriceSnapshot: .35, isDemo: false, ...extra,
});
function chartProps(history, overrides = {}) {
  const filters = { ...defaultHistoryFilters, period: "6m" };
  return { ...buildHistoryBuckets(history, historyRange(history, filters), true), language: "en", metric: "energy", currency: "EUR", selectedKey: "", onSelect() {}, onMetricChange() {}, ...overrides };
}
function find(node, predicate) {
  if (!node || typeof node !== "object") return;
  if (predicate(node)) return node;
  for (const child of React.Children.toArray(node.props?.children)) { const result = find(child, predicate); if (result) return result; }
}
function all(node, predicate) {
  if (!node || typeof node !== "object") return [];
  return [...(predicate(node) ? [node] : []), ...React.Children.toArray(node.props?.children).flatMap(child => all(child, predicate))];
}
function mountView(history, language = "en") {
  const states = [];
  let cursor = 0;
  const { HistoryView: View } = compile("../src/features/driver/history-view.tsx", {
    "./history-chart-data": data, "./history-chart": { HistoryChart },
    "next/link": { default: ({ children, ...props }) => createElement("a", props, children) },
    react: { ...React, useMemo: fn => fn(), useState(initial) {
      const index = cursor++; if (!(index in states)) states[index] = initial;
      return [states[index], next => { states[index] = typeof next === "function" ? next(states[index]) : next; }];
    } },
  });
  return () => { cursor = 0; return View({ history, language }); };
}
const rows = tree => all(tree, node => node.props?.className === "history-row");
const button = (tree, text) => find(tree, node => node.type === "button" && node.props.children === text);
const field = (tree, text) => find(find(tree, node => node.type === "label" && React.Children.toArray(node.props.children)[0] === text), node => node.type === "select" || node.type === "input");

test("monthly activity fills zero months, handles year boundaries and combines completed charges", () => {
  const months = buildHistoryMonths([
    charge("2027-01-01T12:00:00Z", 12, 4), charge("2027-01-20T12:00:00Z", 18, 6),
    charge("2026-10-10T12:00:00Z", 5, 2), charge("2026-07-10T12:00:00Z", 100, 50),
  ]);
  assert.deepEqual(months.map(item => item.key), ["2026-08", "2026-09", "2026-10", "2026-11", "2026-12", "2027-01"]);
  assert.equal(months[0].energyKWh, 0);
  assert.equal(months[2].energyKWh, 5);
  assert.equal(months[5].charges, 2);
  assert.equal(months[5].energyKWh, 30);
  assert.equal(months[5].costs.EUR, 10);
});

test("completion months use Tirana time and never sum different currencies", () => {
  const months = buildHistoryMonths([
    charge("2026-09-30T22:30:00Z", 5, 2), charge("2026-10-02T12:00:00Z", 7, 800, "ALL"),
    charge("2026-09-30T20:00:00Z", 3, 1),
  ]);
  assert.equal(months.at(-1).key, "2026-10");
  assert.equal(months.at(-1).charges, 2);
  assert.deepEqual(months.at(-1).costs, { EUR: 2, ALL: 800 });
  assert.equal(months.at(-2).charges, 1);
});

test("redesigned chart has exact accessible values, a zero baseline and three metrics", () => {
  const html = renderToStaticMarkup(createElement(HistoryChart, chartProps([charge("2026-10-01T12:00:00Z", 25, 8.75)])));
  assert.match(html, /Charging over time/);
  assert.match(html, /Energy/);
  assert.match(html, /Cost/);
  assert.match(html, /Sessions/);
  assert.match(html, /October 2026: 25 kWh, 1 charge/);
  assert.match(html, /8.75 EUR/);
  assert.equal((html.match(/class="history-column"/g) ?? []).length, 6);
  assert.match(html, /height:0%/);
  assert.doesNotMatch(html, /NaN|Infinity/);
});

test("empty or invalid history does not create fabricated activity", () => {
  assert.deepEqual(buildHistoryMonths([]), []);
  assert.deepEqual(buildHistoryMonths([charge("bad-date", 1, 2)]), []);
  assert.equal(historyRange([], defaultHistoryFilters), null);
  const html = renderToStaticMarkup(createElement(HistoryView, { history: [], language: "sq" }));
  assert.match(html, /Ende asnjë karikim/);
  assert.doesNotMatch(html, /class="history-column"|class="history-detail"/);
});

test("day presets use latest completion and retain zero days across year and DST boundaries", () => {
  const history = [charge("2027-01-01T23:30:00Z", 4, 1)];
  const range = historyRange(history, defaultHistoryFilters);
  assert.deepEqual(range, { from: "2026-12-27", until: "2027-01-02" });
  const { buckets, granularity } = buildHistoryBuckets(history, range);
  assert.equal(granularity, "day"); assert.equal(buckets.length, 7);
  assert.equal(buckets.at(-1).charges, 1); assert.equal(buckets[0].charges, 0);
  const dst = [charge("2026-10-24T22:30:00Z", 3, 1), charge("2026-10-25T22:30:00Z", 5, 2)];
  assert.equal(historyDay(dst[0].endedAt), "2026-10-25");
  assert.equal(historyDay(dst[1].endedAt), "2026-10-25");
  const days = buildHistoryBuckets(dst, { from: "2026-10-24", until: "2026-10-26" }).buckets;
  assert.deepEqual(days.map(item => item.charges), [0, 2, 0]);
  assert.equal(days[1].energyKWh, 8);
});

test("30 days, six months and custom ranges preserve inclusive completion dates", () => {
  const history = [charge("2026-10-07T12:00:00Z", 4, 1)];
  assert.deepEqual(historyRange(history, { ...defaultHistoryFilters, period: "30d" }), { from: "2026-09-08", until: "2026-10-07" });
  assert.deepEqual(historyRange(history, { ...defaultHistoryFilters, period: "6m" }), { from: "2026-05-01", until: "2026-10-07" });
  for (const [from, until] of [["", "2026-10-07"], ["2026-10-08", "2026-10-07"], ["2026-02-30", "2026-10-07"]]) {
    assert.equal(historyRange(history, { ...defaultHistoryFilters, period: "custom", from, until }), null);
  }
  assert.deepEqual(historyRange(history, { ...defaultHistoryFilters, period: "custom", from: "2026-10-07", until: "2026-10-07" }), { from: "2026-10-07", until: "2026-10-07" });
});

test("station, currency, energy and date filters compose without changing source history", () => {
  const history = [charge("2026-10-01T12:00:00Z", 10, 3), charge("2026-10-02T12:00:00Z", 30, 10),
    charge("2026-10-03T12:00:00Z", 30.01, 11), charge("2026-10-04T12:00:00Z", 15, 1500, "ALL"),
    charge("2026-10-05T12:00:00Z", 20, 7, "EUR", { stationId: "station-2" })];
  const range = { from: "2026-10-01", until: "2026-10-04" };
  assert.deepEqual(filterHistory(history, { ...defaultHistoryFilters, energy: "small" }, range).map(item => item.energyKWh), [10]);
  assert.deepEqual(filterHistory(history, { ...defaultHistoryFilters, energy: "medium", currency: "EUR", stationId: "station-1" }, range).map(item => item.energyKWh), [30]);
  assert.deepEqual(filterHistory(history, { ...defaultHistoryFilters, energy: "large" }, range).map(item => item.energyKWh), [30.01]);
  assert.deepEqual(filterHistory(history, defaultHistoryFilters, null), []);
  assert.equal(history[0].energyKWh, 10);
  assert.deepEqual(summarizeHistory(history).costs, { EUR: 31, ALL: 1500 });
});

test("long custom ranges group into weeks or months without losing or double counting sessions", () => {
  const history = [charge("2026-08-01T12:00:00Z", 10, 3), charge("2026-10-07T12:00:00Z", 12, 4)];
  for (const [range, granularity] of [[{ from: "2026-08-01", until: "2026-10-07" }, "week"], [{ from: "2026-05-10", until: "2026-10-07" }, "month"]]) {
    const result = buildHistoryBuckets(history, range);
    assert.equal(result.granularity, granularity);
    assert.equal(result.buckets.reduce((sum, item) => sum + item.charges, 0), 2);
    assert.equal(result.buckets.reduce((sum, item) => sum + item.energyKWh, 0), 22);
    assert.equal(result.buckets[0].from, range.from); assert.equal(result.buckets.at(-1).until, range.until);
  }
});

test("axis scales include zero, cover the peak and support tiny, large or zero totals", () => {
  for (const values of [[0, 0], [.0002, .0009], [127.2, 742.04], [0, 500000], [1, 2, 3], [NaN, Infinity, 4]]) {
    const scale = historyScale(values);
    assert.ok(scale.max > 0 && Number.isFinite(scale.max));
    assert.ok(scale.max >= Math.max(0, ...values.filter(Number.isFinite)));
    assert.equal(scale.ticks[0], 0); assert.equal(scale.ticks.length, 5);
  }
});

test("pagination bounds the list to eight sessions and clamps stale page numbers", () => {
  const history = Array.from({ length: 19 }, (_, id) => ({ id }));
  assert.deepEqual(historyPage(history, 2).items.map(item => item.id), [8, 9, 10, 11, 12, 13, 14, 15]);
  assert.equal(historyPage(history, 99).page, 3);
  assert.equal(historyPage([], 99).page, 1); assert.equal(historyPage([], 99).items.length, 0);
});

test("list, KPIs and chart share filters; a filter resets pagination and hidden session details", () => {
  const history = Array.from({ length: 18 }, (_, id) => charge(`2026-10-07T${String(id).padStart(2, "0")}:00:00Z`, id + 1, 1, "EUR", { id: String(id), stationId: id < 4 ? "station-2" : "station-1", stationName: id < 4 ? "Other station" : "Central" }));
  const render = mountView(history);
  assert.equal(rows(render()).length, 8);
  find(render(), node => node.props?.["aria-label"] === "Next page").props.onClick();
  assert.match(renderToStaticMarkup(render()), /Page 2 \/ 3/);
  field(render(), "Charging station").props.onChange({ target: { value: "station-2" } });
  const tree = render();
  assert.equal(rows(tree).length, 4);
  assert.ok(rows(tree).every(row => row.props.children[1].props.children[0].props.children === "Other station"));
  const graph = find(tree, node => node.type === HistoryChart);
  assert.equal(graph.props.buckets.reduce((sum, item) => sum + item.charges, 0), 4);
  const html = renderToStaticMarkup(tree);
  assert.match(html, /<strong>4<\/strong>/); assert.match(html, /10.0/);
  assert.doesNotMatch(html, /Page 2|>Central</);
});

test("bar drilldown restricts activity and summary, can select zero days, and clears again", () => {
  const render = mountView([charge("2026-10-06T12:00:00Z", 10, 3), charge("2026-10-07T12:00:00Z", 20, 7)]);
  const graph = () => find(render(), node => node.type === HistoryChart);
  graph().props.onSelect("2026-10-06");
  assert.equal(rows(render()).length, 1);
  const kpis = find(render(), node => node.props?.className === "driver-kpis history-kpis");
  assert.equal(kpis.props.children[1].props.children[1].props.children[0], "10.0");
  assert.match(renderToStaticMarkup(render()), /Summary · 6 October 2026/);
  graph().props.onSelect("2026-10-05");
  assert.equal(rows(render()).length, 0);
  assert.match(renderToStaticMarkup(render()), /No matching sessions/);
  assert.doesNotMatch(renderToStaticMarkup(render()), /class="history-detail"/);
  graph().props.onSelect(""); assert.equal(rows(render()).length, 2);
});

test("custom date edits and energy filters also update activity below the chart", () => {
  const render = mountView([charge("2026-10-06T12:00:00Z", 10, 3), charge("2026-10-07T12:00:00Z", 35, 12)]);
  button(render(), "Choose dates").props.onClick();
  field(render(), "From date").props.onChange({ target: { value: "2026-10-07" } });
  assert.equal(rows(render()).length, 1);
  field(render(), "Energy per session").props.onChange({ target: { value: "small" } });
  assert.equal(rows(render()).length, 0);
  field(render(), "Until date").props.onChange({ target: { value: "2026-10-01" } });
  assert.match(renderToStaticMarkup(render()), /role="alert"/);
  assert.doesNotMatch(renderToStaticMarkup(render()), /class="history-detail"/);
  button(render(), "Reset filters").props.onClick(); assert.equal(rows(render()).length, 2);
});

test("cost metric chooses a single currency for all results instead of summing currencies", () => {
  const render = mountView([charge("2026-10-07T12:00:00Z", 25, 8.75), charge("2026-10-07T13:00:00Z", 30, 800, "ALL")]);
  let graph = find(render(), node => node.type === HistoryChart);
  graph.props.onMetricChange("cost");
  graph = find(render(), node => node.type === HistoryChart);
  assert.equal(graph.props.metric, "cost"); assert.equal(graph.props.currency, "EUR");
  assert.equal(rows(render()).length, 1);
  assert.equal(graph.props.buckets.at(-1).costs.ALL, undefined);
  assert.match(renderToStaticMarkup(render()), /8.75 EUR/);
  assert.doesNotMatch(renderToStaticMarkup(render()), /808.75/);
  field(render(), "Currency").props.onChange({ target: { value: "" } });
  assert.equal(find(render(), node => node.type === HistoryChart).props.metric, "energy");
  assert.equal(rows(render()).length, 2);
});

test("chart metrics, selection toggle and arrow/Home/End navigation work without hover", () => {
  const selected = []; const metrics = []; const focused = [];
  const { HistoryChart: Chart } = compile("../src/features/driver/history-chart.tsx", { "./history-chart-data": data,
    react: { ...React, useId: () => "chart-test", useRef: () => ({ current: Array.from({ length: 6 }, (_, index) => ({ focus: () => focused.push(index) })) }) },
  });
  const props = chartProps([charge("2026-10-01T12:00:00Z", 25, 8.75)], { onSelect: key => selected.push(key), onMetricChange: metric => metrics.push(metric) });
  let tree = Chart(props);
  let columns = all(tree, node => node.props?.className === "history-column");
  columns[5].props.onClick(); assert.equal(selected.at(-1), "2026-10-01");
  tree = Chart({ ...props, selectedKey: "2026-10-01" });
  columns = all(tree, node => node.props?.className === "history-column");
  columns[5].props.onClick(); assert.equal(selected.at(-1), "");
  let prevented = 0;
  for (const key of ["ArrowLeft", "Home", "End", "ArrowRight"]) columns[5].props.onKeyDown({ key, preventDefault: () => prevented++ });
  assert.deepEqual(focused, [4, 0, 5, 5]); assert.equal(prevented, 4);
  button(tree, "Cost").props.onClick(); button(tree, "Sessions").props.onClick(); assert.deepEqual(metrics, ["cost", "charges"]);
});

test("Albanian labels and zero-valued completed charges stay visible", () => {
  assert.equal(data.historyDateLabel("2026-10-07", "sq"), "7 tetor 2026");
  const html = renderToStaticMarkup(createElement(HistoryView, { history: [charge("2026-10-07T12:00:00Z", 0, 0)], language: "sq" }));
  assert.match(html, /7 tetor 2026/); assert.match(html, /class="history-row"/);
  assert.match(html, /height:0%/); assert.doesNotMatch(html, /October|NaN|Infinity/);
});

test("zero energy selects a completed day for the insight and exposes a text table", () => {
  const range = { from: "2026-10-01", until: "2026-10-07" };
  const props = { ...buildHistoryBuckets([charge("2026-10-02T12:00:00Z", 0, 0)], range), language: "en", metric: "energy", currency: "EUR", selectedKey: "", onSelect() {}, onMetricChange() {} };
  const html = renderToStaticMarkup(createElement(HistoryChart, props));
  assert.match(html, /<h3>2 October 2026<\/h3>/);
  assert.match(html, /View chart values/); assert.match(html, /scope="row">2 October 2026/);
});
