"use client";

import { useRef, useState } from "react";
import Link from "next/link";
import type { DriverHistoryPage, Language } from "../../../../contracts/account";
import { emptyHistoryPage } from "../../../../shared/history-query";
import { useHistoryPage } from "./use-history-page";
import { HistoryChart } from "./history-chart";
import {
  defaultHistoryFilters, historyDateLabel, historyDay, historyRange, type HistoryFilters, type HistoryMetric, type HistoryPeriod,
} from "./history-chart-data";

export function HistoryView({ initial, language }: { initial: DriverHistoryPage; language: Language }) {
  const sq = language === "sq";
  const [filters, setFilters] = useState<HistoryFilters>(defaultHistoryFilters);
  const [metric, setMetric] = useState<HistoryMetric>("energy");
  const [selectedKey, setSelectedKey] = useState("");
  const [pageNumber, setPageNumber] = useState(1);
  const [selectedId, setSelectedId] = useState("");
  const validRange = filters.period !== "custom" || historyRange([], filters) !== null;
  const result = useHistoryPage(initial, filters, pageNumber, selectedKey);
  const metadata = useRef(initial);
  if (result.data) metadata.current = result.data;
  const { bounds, stations, currencies } = metadata.current;
  const page = result.data ?? emptyHistoryPage();
  const { range, chart, summary } = page;
  const selectedBucket = chart.buckets.find(item => item.key === selectedKey);
  const selected = page.items.find(item => item.id === selectedId) ?? page.items[0];
  const currency = filters.currency || (currencies.includes("EUR") ? "EUR" : currencies[0] ?? "EUR");
  const number = (value: number, digits = 1) => new Intl.NumberFormat(sq ? "sq-AL" : "en-GB", { minimumFractionDigits: digits, maximumFractionDigits: digits }).format(value);
  const money = (value: number, code: string) => `${number(value, 2)} ${code}`;
  const date = (value: string) => {
    const day = historyDay(value);
    return day ? `${historyDateLabel(day, language)} · ${new Intl.DateTimeFormat(sq ? "sq-AL" : "en-GB", { hour: "2-digit", minute: "2-digit", hourCycle: "h23", timeZone: "Europe/Tirane" }).format(new Date(value))}` : "—";
  };
  const scope = selectedBucket
    ? `${historyDateLabel(selectedBucket.from, language)}${selectedBucket.until !== selectedBucket.from ? ` – ${historyDateLabel(selectedBucket.until, language)}` : ""}`
    : range ? `${historyDateLabel(range.from, language, "day", true)} – ${historyDateLabel(range.until, language)}` : "";

  function changeFilters(next: HistoryFilters) {
    setFilters(next);
    setSelectedKey("");
    setPageNumber(1);
    setSelectedId("");
    if (!next.currency && currencies.length > 1 && metric === "cost") setMetric("energy");
  }
  function selectPeriod(period: HistoryPeriod) {
    changeFilters({ ...filters, period, from: filters.from || range?.from || bounds?.from || "", until: filters.until || range?.until || bounds?.until || "" });
  }
  function selectBucket(key: string) {
    setSelectedKey(key);
    setPageNumber(1);
    setSelectedId("");
  }
  function changeMetric(next: HistoryMetric) {
    if (next === "cost" && !filters.currency && currencies.length > 1) changeFilters({ ...filters, currency });
    setMetric(next);
  }
  function changePage(next: number) {
    setPageNumber(next);
    setSelectedId("");
  }
  const reset = () => { changeFilters(defaultHistoryFilters); setMetric("energy"); };

  return <div className="driver-content driver-history">
    {page.hasDemo && <div className="demo-banner" role="note"><span aria-hidden="true">✦</span>
      {sq ? "Historik demo: këto karikime janë krijuar vetëm për testimin e pamjes." : "Demo history: these charges exist only for the preview."}</div>}
    <div className="driver-welcome"><div><span className="driver-eyebrow">{sq ? "SEANCAT E MIA" : "MY SESSIONS"}</span>
      <h1>{sq ? "Historiku i karikimeve" : "Charging history"}</h1>
      <p>{sq ? "Shiko si ke karikuar dhe gjej seancat që të interesojnë." : "See your charging activity and find the sessions you need."}</p></div>
      <Link href="/driver" className="driver-history-link">← {sq ? "Kthehu te harta" : "Back to map"}</Link></div>
    {result.pending && <p role="status">{sq ? "Duke ngarkuar historikun…" : "Loading history…"}</p>}
    {result.error && <p role="alert">{sq ? "Historiku nuk u rifreskua. Kontrollo datat dhe riprovo." : "History could not refresh. Check the dates and retry."} {result.data && (sq ? "Po shfaqen të dhënat e fundit." : "Showing the last loaded data.")} <button type="button" onClick={() => void result.reload()}>{sq ? "Riprovo" : "Retry"}</button></p>}
    {bounds ? <>
      <section className="history-filters" aria-labelledby="history-filters-title">
        <div className="history-filter-heading"><h2 id="history-filters-title">{sq ? "Filtro historikun" : "Filter your history"}</h2>
          <button type="button" className="history-text-button" onClick={reset}>{sq ? "Rivendos filtrat" : "Reset filters"}</button></div>
        <div className="history-segmented history-periods" role="group" aria-label={sq ? "Periudha e historikut" : "History period"}>
          {(["7d", "30d", "6m", "custom"] as const).map(period => <button type="button" key={period} aria-pressed={filters.period === period} onClick={() => selectPeriod(period)}>
            {period === "7d" ? (sq ? "7 ditë" : "7 days") : period === "30d" ? (sq ? "30 ditë" : "30 days") : period === "6m" ? (sq ? "6 muaj" : "6 months") : (sq ? "Zgjidh datat" : "Choose dates")}
          </button>)}
        </div>
        <div className="history-filter-fields">
          {filters.period === "custom" && <>
            <label>{sq ? "Nga data" : "From date"}<input type="date" value={filters.from} max={bounds.until} aria-invalid={!validRange} aria-describedby={!validRange ? "history-range-error" : undefined} onChange={event => changeFilters({ ...filters, from: event.target.value })} /></label>
            <label>{sq ? "Deri më" : "Until date"}<input type="date" value={filters.until} max={bounds.until} aria-invalid={!validRange} aria-describedby={!validRange ? "history-range-error" : undefined} onChange={event => changeFilters({ ...filters, until: event.target.value })} /></label>
          </>}
          <label>{sq ? "Pika e karikimit" : "Charging station"}<select value={filters.stationId} onChange={event => changeFilters({ ...filters, stationId: event.target.value })}>
            <option value="">{sq ? "Të gjitha pikat" : "All stations"}</option>{stations.map(item => <option key={item.stationId} value={item.stationId}>{item.stationName} · {item.stationCode}</option>)}
          </select></label>
          <label>{sq ? "Energjia për seancë" : "Energy per session"}<select value={filters.energy} onChange={event => changeFilters({ ...filters, energy: event.target.value as HistoryFilters["energy"] })}>
            <option value="all">{sq ? "Çdo sasi energjie" : "Any energy amount"}</option><option value="small">≤ 10 kWh</option><option value="medium">&gt; 10 – 30 kWh</option><option value="large">&gt; 30 kWh</option>
          </select></label>
          {currencies.length > 1 && <label>{sq ? "Monedha" : "Currency"}<select value={filters.currency} onChange={event => changeFilters({ ...filters, currency: event.target.value })}>
            <option value="">{sq ? "Të gjitha monedhat" : "All currencies"}</option>{currencies.map(code => <option key={code}>{code}</option>)}
          </select></label>}
        </div>
        {!validRange && <p id="history-range-error" className="history-filter-error" role="alert">{sq ? "Plotëso të dyja datat. Data e fundit duhet të jetë pas ose njësoj me datën e parë." : "Enter both dates. The end date must be on or after the start date."}</p>}
        <p className="history-filter-note">{sq ? "Filtrat përfshijnë të gjithë historikun tënd, sipas përfundimit në orën e Tiranës." : "Filters include your full history, by completion date in Tirana time."}</p>
      </section>
      {result.data && <>
      <div className="history-scope" aria-live="polite"><span>{sq ? "Përmbledhja" : "Summary"}{scope && ` · ${scope}`}</span>
        {selectedBucket && <button type="button" className="history-text-button" onClick={() => selectBucket("")}>{sq ? "Hiq zgjedhjen ×" : "Clear selection ×"}</button>}</div>
      <div className="driver-kpis history-kpis">
        <article><span>{sq ? "KARIKIME" : "CHARGES"}</span><strong>{summary.charges}</strong><small>{sq ? "sipas filtrave" : "matching filters"}</small></article>
        <article><span>{sq ? "ENERGJI E MARRË" : "ENERGY DELIVERED"}</span><strong>{number(summary.energyKWh)}<small className="unit"> kWh</small></strong><small>{sq ? "sipas filtrave" : "matching filters"}</small></article>
        <article><span>{sq ? "KOSTO E LLOGARITUR" : "CALCULATED COST"}</span><strong>{Object.entries(summary.costs).map(([code, total]) => <span className="history-currency-total" key={code}>{money(total, code)}</span>)}</strong>{!summary.charges && <strong>{money(0, currency)}</strong>}<small>{sq ? "pa pagesë elektronike" : "no electronic payment"}</small></article>
      </div>
      <HistoryChart {...chart} language={language} metric={metric} currency={currency} selectedKey={selectedKey} onMetricChange={changeMetric} onSelect={selectBucket} />
      <div className="history-layout">
        <section className="history-list" aria-labelledby="history-list-title">
          <div className="history-list-head"><span className="driver-eyebrow">{sq ? "SEANCAT SIPAS FILTRAVE" : "FILTERED SESSIONS"}</span>
            <h2 id="history-list-title">{sq ? "Karikimet e përfunduara" : "Completed charges"}</h2>
            <p className="history-result-count" aria-live="polite">{page.total ? `${page.offset + 1}–${page.offset + page.items.length} ${sq ? "nga" : "of"} ${page.total} ${sq ? "seanca" : "sessions"}` : (sq ? "0 seanca" : "0 sessions")}</p>
          </div>
          {page.items.map(item => <button key={item.id} type="button" className="history-row" aria-pressed={selected?.id === item.id} onClick={() => setSelectedId(item.id)}>
            <span className="history-row-icon" aria-hidden="true">ϟ</span>
            <span className="history-row-main"><strong>{item.stationName}</strong><small>{item.city} · {date(item.endedAt)}</small></span>
            <span className="history-row-values"><strong>{number(item.energyKWh)} kWh</strong><small>{money(item.totalCost, item.currency)}</small></span>
            <span className="history-row-chevron" aria-hidden="true">›</span>
          </button>)}
          {!page.total && <div className="history-no-results"><h3>{sq ? "Asnjë seancë për këta filtra" : "No matching sessions"}</h3><p>{sq ? "Zgjidh një ditë tjetër ose zgjero filtrat." : "Choose another day or broaden your filters."}</p><button type="button" className="history-text-button" onClick={selectedBucket ? () => selectBucket("") : reset}>{selectedBucket ? (sq ? "Shfaq gjithë periudhën" : "Show the whole period") : (sq ? "Rivendos filtrat" : "Reset filters")}</button></div>}
          {page.pages > 1 && <nav className="history-pagination" aria-label={sq ? "Faqet e seancave" : "Session pages"}>
            <button type="button" disabled={page.page === 1} onClick={() => changePage(page.page - 1)} aria-label={sq ? "Faqja e mëparshme" : "Previous page"}>← {sq ? "Para" : "Previous"}</button>
            <span aria-live="polite">{sq ? "Faqja" : "Page"} {page.page} / {page.pages}</span>
            <button type="button" disabled={page.page === page.pages} onClick={() => changePage(page.page + 1)} aria-label={sq ? "Faqja tjetër" : "Next page"}>{sq ? "Tjetër" : "Next"} →</button>
          </nav>}
        </section>
        {selected && <aside className="history-detail" aria-label={sq ? "Detajet e karikimit" : "Charging details"}>
          <span className="driver-eyebrow">{sq ? "DETAJET E SEANCËS" : "SESSION DETAILS"}</span>
          <div className="history-detail-icon" aria-hidden="true">ϟ</div>
          <h2>{selected.stationName}</h2><p>{selected.city} · {selected.stationCode}</p>
          <div className="history-detail-line"><span>{sq ? "Fillimi" : "Started"}</span><strong>{date(selected.startedAt)}</strong></div>
          <div className="history-detail-line"><span>{sq ? "Përfundimi" : "Finished"}</span><strong>{date(selected.endedAt)}</strong></div>
          <div className="history-detail-line"><span>{sq ? "Kohëzgjatja" : "Duration"}</span><strong>{Math.max(0, Math.round((Date.parse(selected.endedAt) - Date.parse(selected.startedAt)) / 60000))} min</strong></div>
          <div className="history-detail-line"><span>{sq ? "Energjia" : "Energy"}</span><strong>{number(selected.energyKWh)} kWh</strong></div>
          <div className="history-detail-line"><span>{sq ? "Çmimi për kWh" : "Price per kWh"}</span><strong>{money(selected.unitPriceSnapshot, selected.currency)}</strong></div>
          <div className="history-total"><span>{sq ? "Kosto e llogaritur" : "Calculated cost"}</span><strong>{money(selected.totalCost, selected.currency)}</strong></div>
          <p className="history-note">{sq ? "Kjo shumë është informuese. Nuk është kryer pagesë." : "This amount is informational. No payment was made."}</p>
        </aside>}
      </div></>}
    </> : !result.pending && !result.error && <section className="empty-state"><h2>{sq ? "Ende asnjë karikim" : "No charges yet"}</h2><p>{sq ? "Kur një seancë të përfundojë, do ta shohësh këtu." : "Completed sessions will appear here."}</p></section>}
  </div>;
}
