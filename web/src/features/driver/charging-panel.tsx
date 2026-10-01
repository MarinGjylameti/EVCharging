"use client";

import { useEffect, useState } from "react";
import type { DriverChargingState, DriverChargingSession, Language, Station } from "../../../../contracts/account";

const labels: Record<string, [string, string]> = {
  Charging: ["Në karikim", "Charging"], Preparing: ["Makina po lidhet", "Connecting vehicle"],
  SuspendedEV: ["Karikimi në pauzë nga makina", "Paused by vehicle"],
  SuspendedEVSE: ["Karikimi në pauzë nga pika", "Paused by charger"],
  Finishing: ["Duke përfunduar", "Finishing"], Stopped: ["Karikimi përfundoi", "Charge finished"],
  Disconnected: ["Pajisja nuk është e lidhur", "Device disconnected"],
  AwaitingStatus: ["Pajisja është lidhur · në pritje të statusit", "Device connected · awaiting status"],
  Faulted: ["Pajisja ka raportuar defekt", "Charger reported a fault"],
  Unavailable: ["Pajisja është jashtë shërbimit", "Charger unavailable"],
};

function SessionCard({ session, sq, now }: { session: DriverChargingSession; sq: boolean; now: number }) {
  // Receipt times measure this live connection even when the simulator clock is old.
  // Keep source times available in the API for device history and diagnostics.
  const start = session.startReceivedAt ?? session.startedAt;
  const stop = session.stopReceivedAt ?? session.stoppedAt;
  const end = stop ? Date.parse(stop) : now;
  const seconds = Math.max(0, Math.floor((end - Date.parse(start)) / 1000));
  const duration = `${Math.floor(seconds / 60)}:${String(seconds % 60).padStart(2, "0")}`;
  const state = labels[session.status]?.[sq ? 0 : 1] ?? session.status;
  const format = (value: number) => new Intl.NumberFormat(sq ? "sq-AL" : "en", { maximumFractionDigits: 3 }).format(value);
  return <article className={`charging-session ${session.status === "Charging" ? "is-charging" : ""}`}>
    <div className="charging-session-heading">
      <div><strong>{session.stationCode}</strong><small>{session.stationName} · {sq ? "Lidhësi" : "Connector"} {session.connectorId}</small></div>
      <span className="charging-state"><i aria-hidden="true" />{state}</span>
    </div>
    <div className="charging-metrics">
      <div><span>{sq ? "ENERGJIA" : "ENERGY"}{session.energyScope && session.energyScope !== "Total" && ` · ${session.energyScope}`}</span><strong>{session.energyKWh === null ? "—" : format(session.energyKWh)} <small>kWh</small></strong></div>
      <div><span>{sq ? "FUQIA AKTUALE" : "CURRENT POWER"}{session.powerScope && session.powerScope !== "Total" && ` · ${session.powerScope}`}</span><strong>{session.powerKW === null ? "—" : format(session.powerKW)} <small>kW</small></strong></div>
      <div><span>{sq ? "KOHA" : "DURATION"}</span><strong>{duration}</strong></div>
      {session.stateOfCharge !== null && <div><span>{sq ? "BATERIA" : "BATTERY"}</span><strong>{format(session.stateOfCharge)}<small>%</small></strong></div>}
      {session.temperatureCelsius !== null && <div><span>{sq ? "TEMPERATURA" : "TEMPERATURE"}</span><strong>{format(session.temperatureCelsius)}<small>°C</small></strong></div>}
    </div>
    <p className="charging-session-note">
      {session.stoppedAt ? session.cost !== null ? (sq ? "U ruajt në historikun tënd." : "Saved to your history.") :
        (sq ? "Matjet fundore duhen verifikuar." : "Final measurements need verification.") :
        (sq ? "Matjet përditësohen kur i dërgon pajisja." : "Measurements update when reported by the device.")}
      {session.stoppedAt && session.cost !== null && ` · ${format(session.cost)} ${session.currency}`}
      {(session.lastMeasurementReceivedAt ?? session.lastMeasurementAt) && <> · {sq ? "Matja e fundit u mor" : "Last measurement received"}: {new Date((session.lastMeasurementReceivedAt ?? session.lastMeasurementAt)!).toLocaleTimeString(sq ? "sq-AL" : "en")}</>}
    </p>
    {session.energyScope && session.energyScope !== "Total" && <p className="charging-session-note">{sq ? `Matja mbulon vetëm ${session.energyScope}; totali shfaqet kur e dërgon pajisja.` : `Reading covers ${session.energyScope} only; total appears when reported by the device.`}</p>}
    {!session.stoppedAt && session.powerKW === null && <p className="charging-session-note">{sq ? "Pajisja nuk ka dërguar matje të fuqisë aktuale." : "The device has not reported current power."}</p>}
    {session.deviceClockSkewed && <p className="charging-session-note" role="status">{sq ? "Ora e pajisjes ndryshon nga ora e serverit. Në simulator vendos Start time në orën aktuale. Koha këtu llogaritet nga marrja e mesazheve." : "Device time differs from server time. Set the simulator Start time to the current time. Duration here uses message receipt times."}</p>}
  </article>;
}

export function ChargingPanel({ stations, state, connected, unavailable, language }: {
  stations: Station[]; state: DriverChargingState; connected: boolean; unavailable: boolean; language: Language;
}) {
  const sq = language === "sq";
  const simulators = stations.filter(station => station.isDemo && station.statusSource === "OCPP");
  const [chosen, setChosen] = useState("");
  const [linked, setLinked] = useState<string[]>([]);
  const [now, setNow] = useState(0);
  useEffect(() => {
    setNow(Date.now());
    const timer = setInterval(() => setNow(Date.now()), 1000);
    return () => clearInterval(timer);
  }, []);
  const selected = chosen || simulators[0]?.id || "";
  const waiting = linked.filter(id => !state.sessions.some(session => session.stationId === id));
  if (!state.canTestSimulator && state.sessions.length === 0) return null;

  return <section className="charging-panel" aria-labelledby="charging-title">
    <div className="driver-section-heading"><div>
      <span className="driver-eyebrow">{sq ? "KARIKIMI YT" : "YOUR CHARGE"}</span>
      <h2 id="charging-title">{sq ? "Ndiq karikimin" : "Follow your charge"}</h2>
    </div><span className={`charging-live ${connected && !unavailable ? "is-live" : ""}`} role="status">
      <i aria-hidden="true" />{unavailable ? (sq ? "Lidhja po rikthehet…" : "Reconnecting…") :
        connected ? (sq ? "Përditësime aktive" : "Live updates") : (sq ? "Po rilidhet…" : "Reconnecting…")}
    </span></div>
    {state.canTestSimulator && <div className="charging-test-controls">
      <label><span>{sq ? "Pika për testim" : "Test charge point"}</span>
        <select value={selected} onChange={event => setChosen(event.target.value)}>
          {simulators.map(station => <option key={station.id} value={station.id}>{station.code} · {station.name}</option>)}
        </select>
      </label>
      <button type="button" className="driver-locate-button" disabled={!selected} onClick={() => setLinked(ids => ids.includes(selected) ? ids : [...ids, selected])}>
        {sq ? "Lidhu për test" : "Connect for testing"}
      </button>
      <p>{sq ? "Pa QR për këtë demo. Nis karikimin te simulatori i zgjedhur me TAG-001, lidhësi 1." :
        "QR bypass for this demo. Start the selected simulator with TAG-001, connector 1."}</p>
    </div>}
    {waiting.map(id => {
      const station = simulators.find(item => item.id === id);
      if (!station) return null;
      return <div key={id} className="charging-waiting"><span><strong>{station.code}</strong> · {sq ? "Në pritje të karikimit nga simulatori" : "Waiting for the simulator to start charging"}</span>
        <button type="button" className="text-button" onClick={() => setLinked(ids => ids.filter(value => value !== id))}>{sq ? "Hiq" : "Remove"}</button></div>;
    })}
    <div className="charging-session-list">
      {state.sessions.map(session => <SessionCard key={session.id} session={session} sq={sq} now={now || Date.parse(session.startReceivedAt ?? session.startedAt)} />)}
    </div>
    {state.sessions.length === 0 && waiting.length === 0 && <p className="charging-session-note">{sq ? "Zgjidh një pikë testimi. Karikimi do të shfaqet këtu sapo të fillojë." : "Choose a test point. Your charge appears here when it starts."}</p>}
  </section>;
}
