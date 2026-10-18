"use client";

import type { DriverChargingSession, Language } from "../../../../contracts/account";
import { ChargingFault } from "./charging-fault";
import { chargingEstimate, formatChargingEstimate, chargingEstimateNotes as estimateNotes } from "./charging-estimate";

export const chargingLabels: Record<string, [string, string]> = {
  Charging: ["Në karikim", "Charging"], Connecting: ["Makina po lidhet", "Connecting vehicle"],
  PausedByVehicle: ["Në pauzë nga makina", "Paused by vehicle"],
  PausedByCharger: ["Në pauzë nga pika", "Paused by charger"],
  Finishing: ["Duke përfunduar", "Finishing"], Completed: ["Karikimi përfundoi", "Charge completed"],
  ConnectionLost: ["Lidhja me pikën u ndërpre", "Charger connection lost"],
  Unknown: ["Në pritje të statusit", "Awaiting charger status"],
  Faulted: ["Pika ka raportuar defekt", "Charger reported a fault"],
  Unavailable: ["Pika është jashtë shërbimit", "Charger unavailable"],
  Available: ["Pika është e lirë", "Charger available"], Reserved: ["Pika është e rezervuar", "Charger reserved"],
};

export function ChargingProgress({ session, language, now, stale }: {
  session: DriverChargingSession; language: Language; now: number;
  stale: boolean;
}) {
  const sq = language === "sq";
  const ended = session.stoppedAt !== null;
  const paused = !ended && (session.status === "PausedByVehicle" || session.status === "PausedByCharger");
  const charging = !ended && session.status === "Charging" && !stale;
  const soc = session.stateOfCharge !== null && session.stateOfCharge >= 0 && session.stateOfCharge <= 100
    ? session.stateOfCharge : null;
  const simulatedClock = session.isDemo;
  const startTimestamp = simulatedClock ? session.startReceivedAt ?? session.startedAt : session.startedAt;
  const start = Date.parse(startTimestamp);
  const startDate = Number.isFinite(start) ? new Date(start) : null;
  const end = simulatedClock ? session.stopReceivedAt ?? session.stoppedAt : session.stoppedAt;
  const elapsed = ((end ? Date.parse(end) : now) - start) / 1000;
  const duration = formatChargingDuration(elapsed);
  const number = (value: number, digits = 3) => new Intl.NumberFormat(sq ? "sq-AL" : "en-GB", { maximumFractionDigits: digits }).format(value);
  const state = ended ? chargingLabels.Completed[sq ? 0 : 1]
    : stale ? (sq ? "Po presim përditësimin…" : "Waiting for an update…")
    : chargingLabels[session.status]?.[sq ? 0 : 1] ?? session.status;
  const faulted = session.status === "Faulted" || session.status === "Unavailable";
  const powerExpired = session.powerValidUntil ? now >= Date.parse(session.powerValidUntil) : false;
  const actualPower = ended ? 0 : session.status !== "Charging" || powerExpired ? null : session.powerKW;
  const powerNote = ended ? (sq ? "Sesioni u mbyll" : "Session closed")
    : paused ? (sq ? "Transferimi është në pauzë" : "Energy transfer is paused")
    : stale || powerExpired ? (sq ? "Në pritje të matjeve të reja" : "Waiting for fresh readings")
    : faulted ? (sq ? "Fuqia nuk është e disponueshme gjatë defektit" : "Power is unavailable while the charger reports a fault")
    : session.status === "ConnectionLost" ? (sq ? "Në pritje të rilidhjes së pajisjes" : "Waiting for the charger to reconnect")
    : session.status !== "Charging" ? (sq ? "Në pritje të karikimit dhe matjeve" : "Waiting for charging and readings")
    : actualPower === null ? (sq ? "Në pritje të një matjeje të vlefshme fuqie" : "Waiting for a valid power reading")
    : (sq ? "Sipas pajisjes" : "Reported by charger");
  const saved = ended && session.energyKWh !== null && session.cost !== null && !session.completionWarning;
  const estimate = chargingEstimate(session, now, stale);

  return <article className={`charge-process ${charging ? "is-charging" : paused ? "is-paused" : ended ? "is-complete" : faulted ? "is-faulted" : ""}`}>
    <div className="charge-process-heading">
      <span className="driver-eyebrow">{sq ? "KARIKIMI YT" : "YOUR CHARGE"}</span>
      <span className="charge-status" role="status"><i aria-hidden="true" />{state}</span>
    </div>
    {!ended && session.faultCode && <ChargingFault code={session.faultCode} info={session.faultInfo} language={language} status={session.status} />}
    {ended && session.completionWarning && <div className="charge-fault-notice" role="alert">
      <strong>{sq ? "Matja finale kërkon verifikim" : "The final reading needs verification"}</strong>
      <span>{session.completionWarning === "FinalMeterRegression"
        ? (sq ? "Pajisja konfirmoi ndalimin, por regjistri final është më i ulët se matjet e pranuara. Kostoja finale dhe historiku presin verifikimin e kësaj matjeje." : "The charger confirmed the stop, but its final meter register is lower than accepted readings. Final cost and history await verification of this reading.")
        : (sq ? "Pajisja konfirmoi ndalimin. Përmbledhja financiare pret verifikimin e matjes finale." : "The charger confirmed the stop. The financial summary awaits verification of the final reading.")}</span>
    </div>}
    {!ended && <p className="charge-session-start">{sq ? "Sesioni mbetet i hapur deri në konfirmimin e ndalimit nga pajisja." : "The session remains open until the charger confirms its stop."}</p>}
    {ended && session.stopReason === "EVDisconnected" && <p className="charge-point-notice" role="status">{sq ? "Kablloja u hoq nga makina. Karikuesi konfirmoi ndalimin; sesioni u mbyll dhe lidhja e llogarisë me pikën u lirua." : "The vehicle was unplugged. The charger confirmed the stop; the session closed and the account association was released."}</p>}
    <div className="charge-energy-display">
      <div className="charge-bolt" aria-hidden="true">ϟ</div>
      <div><span>{sq ? "Energjia e marrë" : "Energy delivered"}{session.energyScope && session.energyScope !== "Total" ? ` · ${session.energyScope}` : ""}</span>
        <strong>{session.energyKWh === null ? "—" : number(session.energyKWh)} <small>kWh</small></strong>
      </div>
      <div className="charge-battery"><span>{sq ? "Niveli i baterisë" : "Battery level"}</span><strong>{soc === null ? "—" : number(soc, 1)}<small>%</small></strong></div>
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
        : paused ? (sq ? "Energjia ruhet; sesioni ende nuk është mbyllur" : "Energy is retained; the session is still open")
        : (sq ? "Ende nuk kemi matje të përqindjes së baterisë" : "No battery percentage reading received yet")}</span>
      {session.lastMeasurementReceivedAt && <span>{sq ? "Matja e fundit" : "Last reading"}: {new Date(session.lastMeasurementReceivedAt).toLocaleTimeString(sq ? "sq-AL" : "en-GB", { timeZone: "Europe/Tirane", hour: "2-digit", minute: "2-digit", second: "2-digit" })}</span>}
    </div>
    <div className="charge-process-metrics">
      <div><span>{sq ? "KOSTO E LLOGARITUR" : "CALCULATED COST"}</span><strong>{session.cost === null || session.completionWarning ? "—" : number(session.cost, 2)} <small>{session.currency}</small></strong><small>{saved ? (sq ? "U ruajt në historik" : "Saved in history") : session.completionWarning ? (sq ? "Në pritje të verifikimit" : "Awaiting verification") : ended ? (sq ? "Mungon matja finale ose tarifa" : "Final reading or tariff missing") : (sq ? "Deri tani" : "So far")}</small></div>
      <div><span>{sq ? "FUQIA AKTUALE" : "CURRENT POWER"}</span><strong>{actualPower === null || stale && !ended ? "—" : number(actualPower)} <small>kW</small></strong><small>{powerNote}{actualPower !== null && !ended && !stale && session.powerScope && session.powerScope !== "Total" ? ` · ${session.powerScope}` : ""}</small></div>
      <div><span>{sq ? "KOHA E KARIKIMIT" : "CHARGING TIME"}</span>
        <strong>{duration}</strong>
        <small>{sq ? "Nga nisja · përfshin pauzat" : "Since start · includes pauses"}</small>
        <small>{startDate ? <>{sq ? "Nisur" : "Started"}: <time dateTime={startTimestamp}>{startDate.toLocaleDateString("en-GB", { timeZone: "Europe/Tirane", day: "2-digit", month: "2-digit", year: "numeric" })} · {startDate.toLocaleTimeString(sq ? "sq-AL" : "en-GB", { timeZone: "Europe/Tirane", hour: "2-digit", minute: "2-digit", hourCycle: "h23" })}</time> · {sq ? "Tiranë" : "Tirana"}</> : (sq ? "Koha e nisjes nuk është e disponueshme" : "Start time is unavailable")}</small>
      </div>
      {!ended && <div><span>{sq ? "KOHA DERI NË BATERI TË PLOTË" : "TIME UNTIL BATTERY IS FULL"}</span>
        <strong>{estimate.minutes === null ? "—" : `≈ ${formatChargingEstimate(estimate.minutes)}`}</strong>
        <small>{estimate.reason ? estimateNotes[estimate.reason][sq ? 0 : 1] : (sq ? "Vlerësim me fuqinë aktuale · deri në 100%" : "Estimate at current power · to 100%")}</small>
        {estimate.reason === "missingCapacity" && <small><a href="/driver/account#battery-capacity">{sq ? "Plotëso profilin →" : "Update profile →"}</a></small>}
      </div>}
    </div>
    <details className="charge-reading-details">
      <summary>{sq ? "Rreth matjeve" : "About the readings"}</summary>
      {session.energyScope && session.energyScope !== "Total" && <p>{sq ? `Energjia dhe kostoja gjatë karikimit bazohen në matjen e raportuar për ${session.energyScope}. Totali final merret nga pajisja kur sesioni përfundon.` : `Live energy and cost use the reported ${session.energyScope} reading. Final totals come from the charger when the session ends.`}</p>}
      {(simulatedClock || session.deviceClockSkewed) && <p>{simulatedClock
        ? (sq ? "Kohëzgjatja përdor marrjen e nisjes dhe ndalimit nga serveri. Shpejtësia e simulatorit nuk e përshpejton këtë orë." : "Duration uses the server receipt of start and stop. The simulator speed does not accelerate this clock.")
        : (sq ? "Ora e pajisjes ndryshon nga ora reale. Kohëzgjatja bazohet në nisjen e raportuar nga pajisja." : "Device time differs from real time. Duration uses the start reported by the charger.")}</p>}
      {session.temperatureCelsius !== null && <p>{sq ? "Temperatura e raportuar" : "Reported temperature"}: {number(session.temperatureCelsius, 1)} °C</p>}
      {!ended && <p>{sq ? "Koha e mbetur është vlerësim deri në 100%, nga kapaciteti i profilit, përqindja e fundit dhe fuqia totale aktuale. Përditësohet me matjet e reja; humbjet dhe ulja e fuqisë afër fundit mund ta zgjasin karikimin. Vlerësimi fshihet nëse fuqia nuk rifreskohet për 2 minuta ose përqindja për 5 minuta." : "Remaining time estimates charging to 100% using your profile capacity, the latest battery percentage and current total power. New readings update it; losses and reduced power near full charge can extend charging. The estimate is hidden if power is not refreshed for 2 minutes or battery percentage for 5 minutes."}</p>}
    </details>
    {ended && <p className="charge-session-end">{saved ? (sq ? "Ky karikim përfundoi. Përmbledhja e tij është ruajtur në historikun tënd." : "This charge is complete. Its summary has been saved in your history.") : (sq ? "Sesioni u mbyll. Përmbledhja financiare kërkon një matje finale dhe tarifë të vlefshme." : "The session is closed. A financial summary requires a valid final reading and tariff.")}</p>}
  </article>;
}

export function formatChargingDuration(seconds: number) {
  if (!Number.isFinite(seconds)) return "—";
  const elapsed = Math.max(0, Math.floor(seconds));
  const minutes = Math.floor(elapsed / 60);
  const rest = String(elapsed % 60).padStart(2, "0");
  return `${minutes} min ${rest} s`;
}

