"use client";

import { useId, useRef } from "react";
import type { Language } from "../../../../contracts/account";
import { historyDateLabel, historyScale, type HistoryBucket, type HistoryMetric } from "./history-chart-data";

type Props = {
  buckets: HistoryBucket[]; granularity: "day" | "week" | "month"; language: Language;
  metric: HistoryMetric; currency: string; selectedKey: string;
  onMetricChange: (metric: HistoryMetric) => void; onSelect: (key: string) => void;
};
export function HistoryChart({ buckets, granularity, language, metric, currency, selectedKey, onMetricChange, onSelect }: Props) {
  const sq = language === "sq";
  const titleId = useId();
  const hintId = useId();
  const buttons = useRef<(HTMLButtonElement | null)[]>([]);
  const number = (value: number) => new Intl.NumberFormat(sq ? "sq-AL" : "en-GB", { maximumFractionDigits: 2 }).format(value);
  const axisNumber = (value: number) => new Intl.NumberFormat(sq ? "sq-AL" : "en-GB", {
    maximumSignificantDigits: 3, notation: value >= 10000 ? "compact" : "standard",
  }).format(value);
  const value = (item: HistoryBucket) => metric === "energy" ? item.energyKWh : metric === "charges" ? item.charges : item.costs[currency] ?? 0;
  const unit = metric === "energy" ? "kWh" : metric === "charges" ? (sq ? "karikime" : "charges") : currency;
  const scale = historyScale(buckets.map(value));
  const selected = buckets.find(item => item.key === selectedKey);
  const peak = buckets.reduce<HistoryBucket | undefined>((best, item) => item.charges > 0 && (!best || value(item) >= value(best)) ? item : best, undefined);
  const focus = selected ?? peak;
  const label = (item: HistoryBucket, short = false) => granularity === "month" && item.from.slice(0, 7) !== item.until.slice(0, 7)
    ? `${historyDateLabel(item.from, language, "month", short)} – ${historyDateLabel(item.until, language, "month", short)}`
    : granularity === "week"
    ? `${historyDateLabel(item.from, language, "day", short)} – ${historyDateLabel(item.until, language, "day", short)}`
    : historyDateLabel(item.from, language, granularity === "month" ? "month" : "day", short);
  const hasActivity = buckets.some(item => item.charges > 0);
  const dense = buckets.length > 10;

  return <section className="history-chart" data-metric={metric} aria-labelledby={titleId}>
    <div className="history-chart-heading">
      <div><span className="driver-eyebrow">{sq ? "AKTIVITETI YT" : "YOUR ACTIVITY"}</span>
        <h2 id={titleId}>{sq ? "Karikimet në kohë" : "Charging over time"}</h2>
        <p>{sq ? "Zgjidh një kolonë për të parë vetëm karikimet e asaj periudhe." : "Select a bar to see only the charges from that period."}</p>
      </div>
      <div className="history-segmented history-metrics" role="group" aria-label={sq ? "Matja në grafik" : "Chart metric"}>
        {(["energy", "cost", "charges"] as const).map(kind => <button type="button" key={kind} aria-pressed={metric === kind} onClick={() => onMetricChange(kind)}>
          {kind === "energy" ? (sq ? "Energjia" : "Energy") : kind === "cost" ? (sq ? "Kostoja" : "Cost") : (sq ? "Seancat" : "Sessions")}
        </button>)}
      </div>
    </div>
    {hasActivity ? <div className="history-chart-body">
      <div className="history-plot-panel">
        <div className="history-plot-caption"><span>{unit}</span><span>{granularity === "month" ? (sq ? "Sipas muajit" : "By month") : granularity === "week" ? (sq ? "Sipas javës" : "By week") : (sq ? "Sipas ditës" : "By day")}</span></div>
        <div className="history-plot-shell">
          <div className="history-y-axis" aria-hidden="true">{scale.ticks.map(tick => <span key={tick} style={{ bottom: `${tick / scale.max * 100}%` }}>{axisNumber(tick)}</span>)}</div>
          <div className="history-plot-scroll" tabIndex={dense ? 0 : undefined} role="group" aria-label={sq ? "Grafiku i karikimeve" : "Charging chart"} aria-describedby={hintId}>
            <div className="history-plot" style={{ minWidth: dense ? `${buckets.length * 32}px` : undefined }}>
              <div className="history-grid" aria-hidden="true">{scale.ticks.map(tick => <span key={tick} style={{ bottom: `${tick / scale.max * 100}%` }} />)}</div>
              <div className="history-columns" style={{ gridTemplateColumns: `repeat(${buckets.length}, minmax(0, 1fr))` }}>
                {buckets.map((item, index) => <button type="button" key={item.key} ref={element => { buttons.current[index] = element; }}
                  className="history-column" aria-pressed={item.key === selectedKey} data-empty={value(item) === 0}
                  aria-label={`${label(item)}: ${number(value(item))} ${unit}, ${item.charges} ${sq ? "karikime" : item.charges === 1 ? "charge" : "charges"}`}
                  onClick={() => onSelect(item.key === selectedKey ? "" : item.key)}
                  onKeyDown={event => {
                    const next = event.key === "ArrowRight" ? Math.min(index + 1, buckets.length - 1) : event.key === "ArrowLeft" ? Math.max(0, index - 1) : event.key === "Home" ? 0 : event.key === "End" ? buckets.length - 1 : null;
                    if (next !== null) { event.preventDefault(); buttons.current[next]?.focus(); }
                  }}>
                  <span className="history-column-track"><span className="history-column-bar" style={{ height: `${Math.max(0, value(item)) / scale.max * 100}%` }} aria-hidden="true" />
                    {item.key === selectedKey && <span className="history-column-value" style={{ bottom: `${Math.max(0, Math.min(value(item) / scale.max * 100, 90))}%` }}>{number(value(item))}</span>}
                  </span>
                  <span className="history-x-label" aria-hidden="true">{granularity === "week" ? historyDateLabel(item.from, language, "day", true) : dense && granularity === "day" ? new Date(item.date).getUTCDate() : label(item, true)}</span>
                </button>)}
              </div>
            </div>
          </div>
        </div>
        <p id={hintId} className="history-plot-hint">{dense ? (sq ? "Rrëshqit për të parë periudhën. " : "Scroll to see the period. ") : ""}{sq ? "Prek një kolonë për detajet; preke sërish për gjithë periudhën." : "Tap a bar for details; tap it again for the whole period."}</p>
      </div>
      {focus && <aside className="history-chart-insight" aria-live="polite" aria-atomic="true">
        <span className="history-insight-label">{selected ? (sq ? "PERIUDHA E ZGJEDHUR" : "SELECTED PERIOD") : metric === "energy" ? (sq ? "MË SHUMË ENERGJI" : "MOST ENERGY") : metric === "cost" ? (sq ? "KOSTOJA MË E LARTË" : "HIGHEST COST") : (sq ? "MË SHUMË SEANCA" : "MOST SESSIONS")}</span>
        <h3>{label(focus)}</h3>
        <strong className="history-insight-value">{number(value(focus))} <small>{unit}</small></strong>
        <dl><div><dt>{sq ? "Seanca" : "Sessions"}</dt><dd>{focus.charges}</dd></div>
          <div><dt>{sq ? "Energjia" : "Energy"}</dt><dd>{number(focus.energyKWh)} kWh</dd></div>
          <div><dt>{sq ? "Kosto e llogaritur" : "Calculated cost"}</dt><dd>{Object.entries(focus.costs).map(([code, total]) => `${number(total)} ${code}`).join(" · ") || `0 ${currency}`}</dd></div>
        </dl>
        <button type="button" className="history-text-button" onClick={() => onSelect(selected ? "" : focus.key)}>{selected ? (sq ? "Shfaq gjithë periudhën" : "Show the whole period") : (sq ? "Shiko këto karikime →" : "View these charges →")}</button>
      </aside>}
    </div> : <p className="history-chart-empty">{sq ? "Nuk ka karikime për këta filtra. Ndrysho periudhën ose filtrat." : "No charges match these filters. Change the period or filters."}</p>}
    {hasActivity && <details className="history-chart-data">
      <summary>{sq ? "Shiko vlerat e grafikut" : "View chart values"}</summary>
      <div className="history-chart-table-scroll" role="region" tabIndex={0} aria-label={sq ? "Vlerat e grafikut" : "Chart values"}>
        <table><caption className="sr-only">{sq ? "Seancat, energjia dhe kostoja për çdo periudhë në grafik" : "Sessions, energy and cost for each chart period"}</caption>
          <thead><tr><th scope="col">{sq ? "Periudha" : "Period"}</th><th scope="col">{sq ? "Seanca" : "Sessions"}</th><th scope="col">kWh</th><th scope="col">{sq ? "Kosto e llogaritur" : "Calculated cost"}</th></tr></thead>
          <tbody>{buckets.map(item => <tr key={item.key}><th scope="row">{label(item)}</th><td>{item.charges}</td><td>{number(item.energyKWh)}</td><td>{Object.entries(item.costs).map(([code, total]) => `${number(total)} ${code}`).join(" · ") || "—"}</td></tr>)}</tbody>
        </table>
      </div>
    </details>}
  </section>;
}
