"use client";

import { useEffect, useRef, useState } from "react";
import type { Language, Station, StationCharging } from "../../../../contracts/account";
import { AutomaticSimulator, simulatorErrors, simulatorStopReasons, type SimulatorLog, type SimulatorState } from "./automatic-simulator";

export function AutomaticSimulatorPanel({ stations, selected, socketBase, capacityKWh, language }: {
  stations: Station[]; selected: string; socketBase: string; capacityKWh: number | null; language: Language;
}) {
  const sq = language === "sq";
  const [code, setCode] = useState(selected);
  const [own, setOwn] = useState(Number(selected.slice(-3)) <= 3);
  const [state, setState] = useState<SimulatorState>({ connected: false, status: "Idle", transactionId: null, energyKWh: 0, soc: 20, powerKW: 0 });
  const [logs, setLogs] = useState<SimulatorLog[]>([]);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const [fault, setFault] = useState<string>("HighTemperature");
  const [stopReason, setStopReason] = useState("Local");
  const [existing, setExisting] = useState<{ id: number; finalWh: number } | null>(null);
  const engine = useRef<AutomaticSimulator | null>(null);
  const mounted = useRef(false);
  const station = stations.find(item => item.code === code)!;
  const active = state.transactionId !== null;
  const blocked = busy || active || state.connected;

  useEffect(() => {
    mounted.current = true;
    const beforeUnload = (event: BeforeUnloadEvent) => {
      if (engine.current?.snapshot.transactionId != null) { event.preventDefault(); event.returnValue = ""; }
    };
    window.addEventListener("beforeunload", beforeUnload);
    return () => { mounted.current = false; window.removeEventListener("beforeunload", beforeUnload); engine.current?.dispose(); };
  }, []);

  async function run(action: () => Promise<void>) {
    setBusy(true); setError("");
    try { await action(); }
    catch (failure) { if (mounted.current) setError(failure instanceof Error ? failure.message : String(failure)); }
    finally { if (mounted.current) setBusy(false); }
  }
  async function prepare() {
    if (!own) return true;
    const response = await fetch(`/api/driver/stations/${station.id}/charging`, { method: "POST" });
    if (response.status === 401) { window.location.assign("/"); return false; }
    const data = await response.json();
    if (!response.ok) throw new Error(data.code === "driver_already_connected"
      ? (sq ? "Përfundo karikimin në pikën tjetër përpara se të nisësh këtë." : "Finish the charge at the other station first.")
      : (sq ? "Lidhja e llogarisë nuk u konfirmua; pika mund të jetë në përdorim." : "Account association failed; the charger may already be in use."));
    const view = data as StationCharging;
    if (view.session && view.session.stoppedAt === null) {
      const id = Number(view.diagnostics?.externalSessionId);
      if (!Number.isSafeInteger(id) || !view.diagnostics || view.session.energyKWh == null)
        throw new Error(sq ? "Ka një transaksion aktiv. Kontrollo diagnostikimin e pikës." : "An active transaction exists. Check charger diagnostics.");
      setExisting({ id, finalWh: (view.diagnostics.meterStartWh ?? 0) + Math.ceil(view.session.energyKWh * 1000) });
      return false;
    }
    setExisting(null); return true;
  }
  function configure(event: React.FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const form = new FormData(event.currentTarget);
    const capacity = Number(form.get("capacity"));
    void run(async () => {
      engine.current?.dispose();
      const simulator = new AutomaticSimulator({ url: socketBase + encodeURIComponent(code), stationCode: code,
        capacityKWh: capacity, initialSoc: Number(form.get("initialSoc")), targetSoc: Number(form.get("targetSoc")),
        powerKW: Number(form.get("power")), meterStartWh: Number(form.get("meterStart")),
        intervalSeconds: Number(form.get("interval")), speed: Number(form.get("speed")) },
        value => { if (mounted.current) setState(value); },
        log => { if (mounted.current) setLogs(previous => [log, ...previous].slice(0, 100)); });
      engine.current = simulator; setLogs([]); setExisting(null);
      await simulator.connect();
    });
  }
  const status = { Idle: ["Gati", "Ready"], Charging: ["Në karikim", "Charging"], Paused: ["Në pauzë", "Paused"], Faulted: ["Defekt", "Faulted"], Completed: ["Përfundoi", "Completed"], Reserved: ["E rezervuar", "Reserved"], Unavailable: ["Jashtë shërbimit", "Unavailable"] }[state.status][sq ? 0 : 1];

  return <div className="driver-content automatic-simulator">
    <span className="driver-eyebrow">{sq ? "SIMULATOR OCPP 1.6 · DEMO" : "OCPP 1.6 SIMULATOR · DEMO"}</span>
    <h1>{sq ? "Provo karikimin automatik" : "Test automatic charging"}</h1>
    <p>{sq ? "1. Lidh simulatorin. 2. Nis karikimin. Energjia, fuqia dhe përqindja dërgohen bashkë në çdo interval; ndalimi dërgohet kur arrihet objektivi." : "1. Connect the simulator. 2. Start charging. Energy, power and battery percentage are sent together at each interval; reaching the target sends the stop."}</p>
    <p><a href="/driver/account#battery-capacity" target="_blank" rel="noopener noreferrer">{sq ? "Vendos kapacitetin e makinës në profil për vlerësimin e kohës →" : "Set your vehicle capacity in your profile to estimate remaining time →"}</a></p>
    {error && <p className="form-error" role="alert">{error}</p>}
    <form className="settings-card" onSubmit={configure}>
      <fieldset disabled={blocked}><div className="field-grid">
        <label>{sq ? "Pika" : "Station"}<select value={code} onChange={event => { setCode(event.target.value); setOwn(Number(event.target.value.slice(-3)) <= 3); setExisting(null); }}>
          {stations.map(item => <option key={item.id} value={item.code}>{item.code}</option>)}
        </select></label>
        <label>{sq ? "Kapaciteti i baterisë (kWh)" : "Battery capacity (kWh)"}<input name="capacity" type="number" min="0.01" max="1000" step="0.01" required defaultValue={capacityKWh ?? 60}/></label>
        <label>{sq ? "Bateria në fillim (%)" : "Initial battery (%)"}<input name="initialSoc" type="number" min="0" max="99.99" step="0.01" required defaultValue="20"/></label>
        <label>{sq ? "Objektivi i baterisë (%)" : "Target battery (%)"}<input name="targetSoc" type="number" min="0.01" max="100" step="0.01" required defaultValue="100"/></label>
        <label>{sq ? "Fuqia totale (kW)" : "Total power (kW)"}<input name="power" type="number" min="0.01" max="1000" step="0.01" required defaultValue={station.powerKW}/></label>
        <label>{sq ? "Regjistri fillestar (Wh)" : "Initial meter register (Wh)"}<input name="meterStart" type="number" min="0" step="1" required defaultValue="0"/></label>
        <label>{sq ? "Intervali i matjeve (sekonda)" : "Meter interval (seconds)"}<input name="interval" type="number" min="2" max="60" step="1" required defaultValue="5"/></label>
        <label>{sq ? "Shpejtësia e simulimit" : "Simulation speed"}<select name="speed" defaultValue="1"><option value="1">1×</option><option value="10">10×</option><option value="30">30×</option><option value="60">60×</option></select></label>
      </div>
      <label className="simulator-own-account"><input type="checkbox" checked={own} onChange={event => setOwn(event.target.checked)}/>{sq ? "Lidhe karikimin me llogarinë time" : "Associate charging with my account"}</label>
      <p>{sq ? "60 kWh është shembull demo; përdor kapacitetin e profilit për testin tënd. Për SIM-004…010, lëre kutinë bosh që pronësia e përdoruesit tjetër të ruhet." : "60 kWh is a demo example; use your profile capacity for your test. For SIM-004…010, leave the box unchecked to preserve the other driver's ownership."}</p>
      <button className="button-primary">{sq ? "Lidh simulatorin" : "Connect simulator"}</button></fieldset>
    </form>
    <section className="settings-card" aria-label={sq ? "Kontrollet e simulimit" : "Simulation controls"}>
      <p role="status">{code} · {state.connected ? status : (sq ? "I shkëputur" : "Disconnected")}{active ? ` · Transaction ID: ${state.transactionId}` : ""}</p>
      <div className="charge-process-metrics"><div><span>{sq ? "ENERGJIA" : "ENERGY"}</span><strong>{state.energyKWh.toFixed(3)} <small>kWh</small></strong></div>
        <div><span>{sq ? "BATERIA" : "BATTERY"}</span><strong>{state.soc.toFixed(1)} <small>%</small></strong></div>
        <div><span>{sq ? "FUQIA" : "POWER"}</span><strong>{state.powerKW} <small>kW</small></strong></div></div>
      <div className="simulator-controls">
        <button type="button" className="button-primary" disabled={busy || !state.connected || active || !!existing} onClick={() => void run(async () => { if (await prepare()) await engine.current!.start(); })}>{sq ? "Nis karikimin" : "Start charging"}</button>
        <button type="button" className="button-outline" disabled={busy || !state.connected || state.status !== "Charging"} onClick={() => void run(() => engine.current!.pause())}>{sq ? "Pauzë nga makina" : "Vehicle pause"}</button>
        <button type="button" className="button-outline" disabled={busy || !state.connected || state.status !== "Charging"} onClick={() => void run(() => engine.current!.pause(true))}>{sq ? "Pauzë nga pika" : "Charger pause"}</button>
        <button type="button" className="button-outline" disabled={busy || !state.connected || !active || state.status === "Charging"} onClick={() => void run(() => engine.current!.resume())}>{sq ? "Vazhdo" : "Resume"}</button>
        <button type="button" className="button-outline" disabled={busy || !state.connected || !active} onClick={() => void run(() => engine.current!.stop(stopReason))}>{sq ? "Ndalo karikimin" : "Stop charging"}</button>
        <button type="button" className="button-outline" disabled={busy || !state.connected} onClick={() => engine.current!.disconnect()}>{sq ? "Shkëput lidhjen" : "Disconnect"}</button>
        <button type="button" className="button-outline" disabled={busy || state.connected || !engine.current} onClick={() => void run(() => engine.current!.connect())}>{sq ? "Rilidhu" : "Reconnect"}</button>
      </div>
      <div className="simulator-controls"><label>{sq ? "Arsyeja e ndalimit" : "Stop reason"}<select value={stopReason} disabled={busy} onChange={event => setStopReason(event.target.value)}>{simulatorStopReasons.map(value => <option key={value}>{value}</option>)}</select></label></div>
      <form className="simulator-controls" onSubmit={event => { event.preventDefault(); const power = Number(new FormData(event.currentTarget).get("livePower")); void run(() => engine.current!.setPower(power)); }}>
        <label>{sq ? "Ndrysho fuqinë totale (kW)" : "Change total power (kW)"}<input name="livePower" type="number" min="0" max="1000" step="0.01" required defaultValue={station.powerKW} disabled={busy || !state.connected || !active}/></label>
        <button className="button-outline" disabled={busy || !state.connected || !active}>{sq ? "Dërgo fuqinë e re" : "Send new power"}</button>
      </form>
      <div className="simulator-controls"><label>{sq ? "Defekti" : "Fault"}<select value={fault} disabled={busy} onChange={event => setFault(event.target.value)}>{simulatorErrors.map(item => <option key={item}>{item}</option>)}</select></label>
        <button type="button" className="button-outline" disabled={busy || !state.connected} onClick={() => void run(() => engine.current!.fault(fault))}>{sq ? "Dërgo defektin" : "Send fault"}</button>
        {(["Available", "Unavailable", "Reserved"] as const).map(value => <button type="button" className="button-outline" key={value} disabled={busy || !state.connected || active} onClick={() => void run(() => engine.current!.reportStatus(value))}>{value}</button>)}
      </div>
      {existing && <div className="charge-point-notice"><p>{sq ? "Ekziston një transaksion i konfirmuar. Përfundoje përpara nisjes së një të riu." : "A confirmed transaction already exists. Finish it before starting a new one."} ID: {existing.id}</p>
        <label>{sq ? "Regjistri final i testit (Wh)" : "Final test meter register (Wh)"}<input type="number" min="0" step="1" value={existing.finalWh} onChange={event => setExisting({ ...existing, finalWh: Number(event.target.value) })}/></label>
        <button type="button" className="button-outline" disabled={busy || !state.connected} onClick={() => void run(async () => { await engine.current!.stopExisting(existing.id, existing.finalWh); setExisting(null); })}>{sq ? "Përfundo transaksionin ekzistues" : "Finish existing transaction"}</button>
      </div>}
      <p>{sq ? "Mbaje këtë faqe hapur gjatë provës. Nëse e mbyll, transaksioni në server mbetet i hapur. Shpejtësia ndryshon simulimin; ora e sesionit në UI mat kohën reale." : "Keep this page open during the test. Closing it leaves the server transaction open. Speed changes the simulation; the session clock in the UI measures real time."}</p>
      <a href={`/driver/stations/${station.id}`} target="_blank" rel="noopener noreferrer">{sq ? "Shiko UI-në e karikimit në një skedë tjetër ↗" : "Open the charging UI in another tab ↗"}</a>
    </section>
    <details className="settings-card"><summary>{sq ? "Mesazhet OCPP të dërguara dhe përgjigjet" : "OCPP messages and responses"} ({logs.length})</summary>
      <ol className="simulator-traffic">{logs.map((log, index) => <li key={`${log.at}-${index}`}><time>{log.at.slice(11, 19)}</time> <b>{log.direction} {log.action}</b><pre>{log.detail}</pre></li>)}</ol>
    </details>
  </div>;
}
