"use client";

import { useMemo, useState } from "react";
import type { ChargingHistory, Language } from "../../../../contracts/account";
import { buildHistoryMonths } from "./history-chart-data";

export function HistoryChart({ history, language }: { history: ChargingHistory[]; language: Language }) {
  const sq = language === "sq";
  const locale = sq ? "sq-AL" : "en-GB";
  const months = useMemo(() => buildHistoryMonths(history), [history]);
  const [metric, setMetric] = useState<"energy" | "cost">("energy");
  const [selectedKey, setSelectedKey] = useState("");
  const currencies = [...new Set(history.map(item => item.currency))].sort();
  const [chosenCurrency, setChosenCurrency] = useState("");
  const currency = currencies.includes(chosenCurrency) ? chosenCurrency : currencies[0] ?? "EUR";
  const selected = months.find(item => item.key === selectedKey) ?? months.at(-1);
  if (!selected) return null;
  const number = (value: number) => new Intl.NumberFormat(locale, { maximumFractionDigits: 2 }).format(value);
  const value = (item: typeof selected) => metric === "energy" ? item.energyKWh : item.costs[currency] ?? 0;
  const unit = metric === "energy" ? "kWh" : currency;
  const peak = Math.max(...months.map(value), 1);
  const monthLabel = (date: string, full = false) => new Intl.DateTimeFormat(locale, {
    month: full ? "long" : "short", year: "numeric", timeZone: "UTC",
  }).format(new Date(date));

  return <section className="history-chart" aria-labelledby="history-chart-title">
    <div className="history-chart-heading">
      <div><span className="driver-eyebrow">{sq ? "AKTIVITETI SIPAS MUAJIT" : "MONTHLY ACTIVITY"}</span>
        <h2 id="history-chart-title">{sq ? "Si ke karikuar" : "Your charging pattern"}</h2>
        <p>{sq ? "Gjashtë muajt deri te karikimi i fundit. Zgjidh një muaj për detajet." : "Six months through your latest charge. Select a month for details."}</p>
      </div>
      <div className="history-chart-controls">
        <div className="map-filter" role="group" aria-label={sq ? "Matja në grafik" : "Chart metric"}>
          <button type="button" aria-pressed={metric === "energy"} onClick={() => setMetric("energy")}>{sq ? "Energjia" : "Energy"}</button>
          <button type="button" aria-pressed={metric === "cost"} onClick={() => setMetric("cost")}>{sq ? "Shpenzimet" : "Cost"}</button>
        </div>
        {metric === "cost" && currencies.length > 1 && <select aria-label={sq ? "Monedha" : "Currency"} value={currency} onChange={event => setChosenCurrency(event.target.value)}>
          {currencies.map(code => <option key={code} value={code}>{code}</option>)}
        </select>}
      </div>
    </div>
    <div className="history-chart-bars" data-metric={metric} role="group" aria-label={`${metric === "energy" ? (sq ? "Energjia mujore" : "Monthly energy") : (sq ? "Kostoja mujore" : "Monthly cost")} (${unit})`}>
      {months.map(item => <button type="button" key={item.key} className="history-chart-month"
        aria-pressed={item.key === selected.key} onClick={() => setSelectedKey(item.key)}
        aria-label={`${monthLabel(item.date, true)}: ${number(value(item))} ${unit}, ${item.charges} ${sq ? "karikime" : "charges"}`}>
        <span className="history-chart-column"><span className="history-chart-value">{number(value(item))}</span>
          <span className="history-chart-bar" style={{ height: `${value(item) / peak * 85}%` }} aria-hidden="true" />
        </span>
        <span className="history-chart-month-label">{monthLabel(item.date)}</span>
      </button>)}
    </div>
    <div className="history-chart-summary" aria-live="polite" aria-atomic="true">
      <strong>{monthLabel(selected.date, true)}</strong>
      <span>{selected.charges} <small>{sq ? "karikime" : "charges"}</small></span>
      <span>{number(selected.energyKWh)} <small>kWh</small></span>
      <span>{Object.entries(selected.costs).map(([code, total]) => `${number(total)} ${code}`).join(" · ") || `0 ${currency}`} <small>{sq ? "kosto e llogaritur" : "calculated cost"}</small></span>
    </div>
    <p className="history-chart-note">{sq ? `Bazuar në ${history.length} karikimet e fundit të listuara, sipas datës së përfundimit.` : `Based on the ${history.length} recent charges listed, grouped by completion date.`}</p>
  </section>;
}
