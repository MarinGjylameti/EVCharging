import test from "node:test";
import assert from "node:assert/strict";
import { loadTypescript } from "./helpers/load-typescript.mjs";
import { createElement } from "react";
import { renderToStaticMarkup } from "react-dom/server";

function compile(path, dependencies = {}) {
  return loadTypescript(new URL(path, import.meta.url), dependencies);
}

const data = compile("../src/features/driver/history-chart-data.ts");
const { HistoryChart } = compile("../src/features/driver/history-chart.tsx", { "./history-chart-data": data });
const { buildHistoryMonths } = data;
const charge = (endedAt, energyKWh, totalCost, currency = "EUR") => ({ endedAt, energyKWh, totalCost, currency });

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

test("chart exposes month values and both metrics as keyboard accessible buttons", () => {
  const html = renderToStaticMarkup(createElement(HistoryChart, {
    history: [charge("2026-10-01T12:00:00Z", 25, 8.75)], language: "en",
  }));
  assert.match(html, /Your charging pattern/);
  assert.match(html, /Energy/);
  assert.match(html, /Cost/);
  assert.match(html, /October 2026: 25 kWh, 1 charges/);
  assert.match(html, /8.75 EUR/);
  assert.equal((html.match(/class="history-chart-month"/g) ?? []).length, 6);
  assert.doesNotMatch(html, /NaN|Infinity/);
});

test("empty or invalid history does not create fabricated activity", () => {
  assert.deepEqual(buildHistoryMonths([]), []);
  assert.deepEqual(buildHistoryMonths([charge("bad-date", 1, 2)]), []);
  const html = renderToStaticMarkup(createElement(HistoryChart, { history: [], language: "sq" }));
  assert.equal(html, "");
});
