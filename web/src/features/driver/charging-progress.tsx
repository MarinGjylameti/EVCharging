"use client";

import type { DriverChargingSession, Language } from "../../../../contracts/account";

export const chargingLabels: Record<string, [string, string]> = {
  Charging: ["Në karikim", "Charging"], Preparing: ["Makina po lidhet", "Connecting vehicle"],
  SuspendedEV: ["Në pauzë nga makina", "Paused by vehicle"],
  SuspendedEVSE: ["Në pauzë nga pika", "Paused by charger"],
  Finishing: ["Duke përfunduar", "Finishing"], Stopped: ["Karikimi përfundoi", "Charge completed"],
  Disconnected: ["Lidhja me pikën u ndërpre", "Charger connection lost"],
  AwaitingStatus: ["Në pritje të statusit", "Awaiting charger status"],
  Faulted: ["Pika ka raportuar defekt", "Charger reported a fault"],
  Unavailable: ["Pika është jashtë shërbimit", "Charger unavailable"],
};

export function ChargingProgress({ session, language, now, stale }: {
  session: DriverChargingSession; language: Language; now: number;
  stale: boolean;
}) {
  const sq = language === "sq";
  const ended = session.stoppedAt !== null;
  const paused = session.status === "SuspendedEV" || session.status === "SuspendedEVSE";
  const charging = session.status === "Charging" && !stale;
  const soc = session.stateOfCharge !== null && session.stateOfCharge >= 0 && session.stateOfCharge <= 100
    ? session.stateOfCharge : null;
  const start = Date.parse(session.startReceivedAt ?? session.startedAt);
  const end = session.stopReceivedAt ?? session.stoppedAt;
  const elapsed = Math.max(0, Math.floor(((end ? Date.parse(end) : now) - start) / 1000));
  const duration = `${Math.floor(elapsed / 60)}:${String(elapsed % 60).padStart(2, "0")}`;
  const number = (value: number, digits = 3) => new Intl.NumberFormat(sq ? "sq-AL" : "en-GB", { maximumFractionDigits: digits }).format(value);
  const state = stale && !ended ? (sq ? "Po presim përditësimin…" : "Waiting for an update…")
    : chargingLabels[session.status]?.[sq ? 0 : 1] ?? session.status;
  const actualPower = ended ? 0 : paused ? null : session.powerKW;
  const saved = ended && session.energyKWh !== null && session.cost !== null;

  return <article className={`charge-process ${charging ? "is-charging" : paused ? "is-paused" : ended ? "is-complete" : ""}`}>
    <div className="charge-process-heading">
      <span className="driver-eyebrow">{sq ? "KARIKIMI YT" : "YOUR CHARGE"}</span>
      <span className="charge-status" role="status"><i aria-hidden="true" />{state}</span>
    </div>
    <div className="charge-energy-display">
      <div className="charge-bolt" aria-hidden="true">ϟ</div>
      <div><span>{sq ? "Energjia e marrë" : "Energy delivered"}{session.energyScope && session.energyScope !== "Total" ? ` · ${session.energyScope}` : ""}</span>
        <strong>{session.energyKWh === null ? "—" : number(session.energyKWh)} <small>kWh</small></strong>
      </div>
      {soc !== null && <div className="charge-battery"><span>{sq ? "Niveli i baterisë" : "Battery level"}</span><strong>{number(soc, 1)}<small>%</small></strong></div>}
    </div>
    {(soc !== null || !ended) && <div className="charge-progress-track" role="progressbar"
      aria-label={soc !== null ? (sq ? "Niveli i baterisë" : "Battery level") : (sq ? "Procesi i karikimit" : "Charging activity")}
      aria-valuemin={0} aria-valuemax={100} aria-valuenow={soc ?? undefined}
      aria-valuetext={soc !== null ? `${number(soc, 1)}%` : state}>
      <span className={soc === null ? "charge-progress-indeterminate" : "charge-progress-fill"}
        style={soc !== null ? { width: `${soc}%` } : undefined} />
    </div>}
    <div className="charge-progress-caption">
      <span>{soc !== null ? (sq ? "Niveli i raportuar nga pajisja" : "Battery level reported by the charger")
        : ended ? (sq ? "Sesioni u mbyll" : "Session closed")
        : paused ? (sq ? "Energjia ruhet; sesioni vazhdon pas pauzës" : "Energy is retained; the session resumes after the pause")
        : (sq ? "Pajisja nuk raporton përqindjen e baterisë" : "The charger does not report battery percentage")}</span>
      {session.lastMeasurementReceivedAt && <span>{sq ? "Matja e fundit" : "Last reading"}: {new Date(session.lastMeasurementReceivedAt).toLocaleTimeString(sq ? "sq-AL" : "en-GB", { timeZone: "Europe/Tirane", hour: "2-digit", minute: "2-digit", second: "2-digit" })}</span>}
    </div>
    <div className="charge-process-metrics">
      <div><span>{sq ? "KOSTO E LLOGARITUR" : "CALCULATED COST"}</span><strong>{session.cost === null ? "—" : number(session.cost, 2)} <small>{session.currency}</small></strong><small>{saved ? (sq ? "U ruajt në historik" : "Saved in history") : ended ? (sq ? "Mungon matja finale ose tarifa" : "Final reading or tariff missing") : (sq ? "Deri tani" : "So far")}</small></div>
      <div><span>{sq ? "FUQIA AKTUALE" : "CURRENT POWER"}</span><strong>{actualPower === null || stale && !ended ? "—" : number(actualPower)} <small>kW</small></strong><small>{paused ? (sq ? "Transferimi është në pauzë" : "Energy transfer is paused") : actualPower === null ? (sq ? "Nuk raportohet" : "Not reported") : (sq ? "Sipas pajisjes" : "Reported by charger")}</small></div>
      <div><span>{sq ? "KOHA E SESIONIT" : "SESSION DURATION"}</span><strong>{duration}</strong><small>{sq ? "Përfshin edhe pauzat" : "Includes pauses"}</small></div>
    </div>
    {(session.energyScope && session.energyScope !== "Total" || session.deviceClockSkewed || session.temperatureCelsius !== null) && <details className="charge-reading-details">
      <summary>{sq ? "Rreth matjeve" : "About the readings"}</summary>
      {session.energyScope && session.energyScope !== "Total" && <p>{sq ? `Energjia dhe kostoja gjatë karikimit bazohen në matjen e raportuar për ${session.energyScope}. Totali final merret nga pajisja kur sesioni përfundon.` : `Live energy and cost use the reported ${session.energyScope} reading. Final totals come from the charger when the session ends.`}</p>}
      {session.deviceClockSkewed && <p>{sq ? "Ora e pajisjes ndryshon nga ora reale. Kohëzgjatja këtu përdor kohën e marrjes së mesazheve." : "Device time differs from real time. Duration here uses message receipt times."}</p>}
      {session.temperatureCelsius !== null && <p>{sq ? "Temperatura e raportuar" : "Reported temperature"}: {number(session.temperatureCelsius, 1)} °C</p>}
    </details>}
    {ended && <p className="charge-session-end">{saved ? (sq ? "Ky karikim përfundoi. Përmbledhja e tij është ruajtur në historikun tënd." : "This charge is complete. Its summary has been saved in your history.") : (sq ? "Sesioni u mbyll. Përmbledhja financiare kërkon një matje finale dhe tarifë të vlefshme." : "The session is closed. A financial summary requires a valid final reading and tariff.")}</p>}
  </article>;
}
