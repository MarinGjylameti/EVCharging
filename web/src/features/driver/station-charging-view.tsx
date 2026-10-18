"use client";

import { useCallback, useEffect, useRef, useState } from "react";
import Link from "next/link";
import { useTestMode } from "../../components/runtime-features";
import type { Language, StationCharging } from "../../../../contracts/account";
import { useLiveSnapshot } from "./use-live-snapshot";
import { ChargingProgress, chargingLabels } from "./charging-progress";
import { chargingLivePolicy } from "./live-update-policy";
import { ChargingFault } from "./charging-fault";
import { SimulatorDiagnostics } from "./simulator-diagnostics";
import { createChargingTestReport, downloadChargingTestReport } from "./charging-test-report";
import { AvailabilityCard } from "./station-availability";
import { blockingConnection, blockingConnectionGuidance, chargingGuidance } from "../../../../shared/charging-guidance";

export function StationChargingView({ initial, language }: { initial: StationCharging; language: Language }) {
  const testMode = useTestMode();
  const sq = language === "sq";
  const connectionConflictMessage = sq ? "Ke tashmë një karikim ose pritje në një pikë tjetër." : "You already have a charging session or waiting request at another charger.";
  const stationId = initial.station.id;
  const selectedConnector = initial.connector?.number ?? 1;
  const url = `/api/driver/stations/${stationId}/charging?connector=${selectedConnector}`;
  const { snapshot, setSnapshot, connected, unavailable } = useLiveSnapshot(initial, url, stationId, chargingLivePolicy);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const [now, setNow] = useState(0);
  const mounted = useRef(false);
  const mutationPending = useRef(false);
  const station = snapshot.station;
  const canConnect = testMode && snapshot.canConnect;
  const driverAccessMode = !testMode && snapshot.driverAccessMode === "Demo" ? null : snapshot.driverAccessMode;
  const session = snapshot.inUse || snapshot.connectionRequest && snapshot.session?.stoppedAt ? null : snapshot.session;
  const active = session && session.stoppedAt === null;
  const waiting = snapshot.connectionRequest !== null;
  const number = (value: number) => new Intl.NumberFormat(sq ? "sq-AL" : "en-GB", { maximumFractionDigits: 3 }).format(value);

  useEffect(() => {
    mounted.current = true;
    setNow(Date.now());
    const timer = setInterval(() => setNow(Date.now()), 1000);
    return () => { mounted.current = false; clearInterval(timer); };
  }, []);

  const mutate = useCallback(async (method: "POST" | "DELETE") => {
    if (mutationPending.current) return;
    mutationPending.current = true;
    setBusy(true); setError("");
    try {
      const response = await fetch(url, { method });
      if (response.status === 401) { window.location.assign("/"); return; }
      if (!response.ok) {
        const data = await response.json().catch(() => ({}));
        if (data.code === "driver_already_connected") {
          // A waiting request on another device may be newer than this snapshot.
          const current = await fetch(url, { cache: "no-store", signal: AbortSignal.timeout(10_000) }).catch(() => null);
          if (current?.ok) {
            const updated: StationCharging | null = await current.json().catch(() => null);
            if (updated && mounted.current) setSnapshot(updated);
          }
        }
        throw new Error(data.code === "charger_in_use"
          ? (sq ? "Kjo pikë po përdoret nga një drejtues tjetër." : "This charger is in use by another driver.")
          : data.code === "driver_already_connected"
            ? connectionConflictMessage
          : (sq ? "Lidhja nuk u konfirmua. Provo përsëri." : "Connection was not confirmed. Please retry."));
      }
      const data: StationCharging = await response.json();
      if (mounted.current) setSnapshot(data);
    } catch (failure) {
      if (mounted.current) setError(failure instanceof Error ? failure.message : (sq ? "Lidhja dështoi." : "Connection failed."));
    } finally { mutationPending.current = false; if (mounted.current) setBusy(false); }
  }, [url, sq, connectionConflictMessage, setSnapshot]);

  const requestExpired = waiting && now > Date.parse(snapshot.connectionRequest!.expiresAt);
  const guidance = chargingGuidance(snapshot, language, unavailable);
  const blocker = canConnect ? blockingConnection(snapshot, now) : null;
  const blockerGuidance = blocker ? blockingConnectionGuidance(blocker.state, language) : null;
  return <div className="driver-content charge-point-page">
    <nav className="charge-page-navigation" aria-label={sq ? "Navigimi i karikimit" : "Charging navigation"}>
      <Link href="/driver">← {sq ? "Kthehu te harta" : "Back to map"}</Link>
      <Link href="/driver/history">{sq ? "Historiku im" : "My history"} ↗</Link>
    </nav>
    <div className="charge-point-title"><div>
      <span className="driver-eyebrow">{sq ? "PIKA E KARIKIMIT" : "CHARGE POINT"}</span>
      <h1>{station.name}</h1><p>{station.address} · {station.city}</p>
    </div><span className={`charging-live ${connected && !unavailable ? "is-live" : ""}`} role="status"><i aria-hidden="true" />{connected && !unavailable ? (sq ? "Përditësime aktive" : "Live updates") : (sq ? "Po rilidhet…" : "Reconnecting…")}</span></div>
    <div className="charge-point-specs">
      <span><small>{sq ? "PIKA" : "POINT"}</small><strong>{station.code}</strong></span>
      <span><small>{sq ? "LIDHËSI" : "CONNECTOR"}</small><strong>{station.connectorType}</strong></span>
      <span><small>{sq ? "FUQIA E PIKËS" : "CHARGER RATING"}</small><strong>{number(station.powerKW)} kW</strong></span>
      <span><small>{sq ? "TARIFA" : "TARIFF"}</small><strong>{number(session?.unitPricePerKWh ?? station.pricePerKWh)} {session?.currency ?? station.currency} / kWh</strong></span>
    </div>
    {!station.isDemo && station.connectorCount > 1 && <nav className="operations-actions" aria-label={sq ? "Zgjidh lidhësin" : "Choose connector"}>
      {Array.from({length: station.connectorCount}, (_, i) => i + 1).map(number => <Link key={number}
        className="button-outline" aria-current={number === selectedConnector ? "page" : undefined}
        href={`/driver/stations/${stationId}?connector=${number}`}>{sq ? "Lidhësi" : "Connector"} {number}</Link>)}
    </nav>}
    {driverAccessMode === "Demo" && <p className="charge-point-notice">{sq ? "Demonstrim: sinjalet dhe karikimet e reja të kësaj pajisjeje të caktohen automatikisht. Nuk ke nevojë të shtypësh Lidhu." : "Demonstration: device signals and new charges are assigned to you automatically. You do not need to press Connect."}</p>}
    {error && !(error === connectionConflictMessage && snapshot.blockingConnection !== undefined && !blocker) && <div className="form-error" role="alert">{error}</div>}
    {blocker && blockerGuidance && <section className="charge-waiting-card" aria-labelledby="charge-blocker-title">
      <h2 id="charge-blocker-title">{blockerGuidance.title}</h2>
      <strong>{blocker.stationName} · {sq ? "Lidhësi" : "Connector"} {blocker.connectorId}</strong>
      <p>{blockerGuidance.body}</p>
      <Link className="driver-history-link" href={`/driver/stations/${blocker.stationId}?connector=${blocker.connectorId}`}>{blockerGuidance.action} →</Link>
    </section>}
    {snapshot.connector?.deviceStatus && (testMode || snapshot.connector.deviceFaultCode) && <section aria-label={sq ? "Pajisja qendrore e pikës" : "Station controller"}>
      {testMode && <p className="charge-point-notice" role="status">{sq ? "Statusi i pajisjes qendrore" : "Station controller status"}: {chargingLabels[snapshot.connector.deviceStatus]?.[sq ? 0 : 1] ?? snapshot.connector.deviceStatus}</p>}
      {snapshot.connector.deviceFaultCode && <ChargingFault code={snapshot.connector.deviceFaultCode} info={snapshot.connector.deviceFaultInfo} language={language} status={snapshot.connector.deviceStatus} />}
    </section>}
    {!active && snapshot.connector?.faultCode && <ChargingFault code={snapshot.connector.faultCode} info={snapshot.connector.faultInfo} language={language} status={snapshot.connector.status} />}
    {testMode && !active && snapshot.connector && <p className="charge-point-notice" role="status">{sq ? "Statusi i lidhësit" : "Connector status"}: {chargingLabels[snapshot.connector.status]?.[sq ? 0 : 1] ?? snapshot.connector.status}</p>}
    {snapshot.inUse && !active && <p className="charge-point-notice" role="status">{sq ? "Pika po përdoret nga një drejtues tjetër. Karikimi i tij nuk shfaqet në llogarinë tënde." : "Another driver is using this charger. Their session is private."}</p>}
    {session ? <ChargingProgress session={session} language={language} now={now || Date.parse(session.startReceivedAt ?? session.startedAt)} stale={unavailable} />
      : snapshot.inUse ? <AvailabilityCard station={station} language={language} nowMs={now || null} stale={unavailable || !connected}/>
      : blocker ? null
      : <section className="charge-waiting-card" aria-labelledby="charge-wait-title">
        <div className={`charge-wait-icon ${waiting && snapshot.deviceConnected && !requestExpired ? "is-ready" : ""}`} aria-hidden="true">ϟ</div>
        <span className="driver-eyebrow">{sq ? "LIDHJA ME PIKËN" : "CONNECTING TO THE CHARGER"}</span>
        <h2 id="charge-wait-title">{!canConnect ? guidance.title : busy ? (sq ? "Po përgatis lidhjen…" : "Preparing connection…")
          : requestExpired ? (sq ? "Koha e pritjes përfundoi" : "Waiting time expired")
          : station.status === "OutOfService" ? (sq ? "Pika është jashtë shërbimit" : "Charger unavailable")
          : station.status === "Maintenance" ? (sq ? "Pika është në mirëmbajtje" : "Charger under maintenance")
          : waiting && snapshot.deviceConnected ? (sq ? "Gati për të nisur" : "Ready to start")
          : waiting ? (sq ? "Në pritje të pajisjes" : "Waiting for the charger")
          : (sq ? "Pika e karikimit u zgjodh" : "Charge point selected")}</h2>
        <p>{!canConnect ? guidance.body : waiting && !requestExpired ? snapshot.deviceConnected
          ? (sq ? "Pajisja është lidhur. Sapo të nisë karikimi, matjet do të shfaqen këtu automatikisht." : "The charger is connected. Your measurements will appear here automatically when charging starts.")
          : (sq ? "Presim që pajisja të lidhet me shërbimin. Mund ta lësh këtë faqe hapur." : "Waiting for the charger to connect. You can leave this page open.")
          : (sq ? "Një sesion krijohet vetëm kur pajisja konfirmon nisjen e karikimit." : "A charging session is created only when the charger confirms the start.")}</p>
        {!canConnect && guidance.steps.length > 0 && <ol className="charge-connection-steps charge-driver-steps">
          {guidance.steps.map((step, index) => <li key={step}><i aria-hidden="true">{index + 1}</i><span>{step}</span></li>)}
        </ol>}
        {canConnect && <ol className="charge-connection-steps">
          <li className="is-done"><i aria-hidden="true">✓</i><span>{sq ? "Pika u zgjodh" : "Point selected"}</span></li>
          <li className={snapshot.deviceConnected ? "is-done" : ""}><i aria-hidden="true">{snapshot.deviceConnected ? "✓" : "2"}</i><span>{sq ? "Pajisja u lidh" : "Charger connected"}</span></li>
          <li><i aria-hidden="true">3</i><span>{sq ? "Nis karikimi" : "Charge starts"}</span></li>
        </ol>}
        {canConnect && <div className="charge-connect-actions">
          {waiting && !requestExpired ? <button type="button" className="button-outline" disabled={busy} onClick={() => void mutate("DELETE")}>{sq ? "Anulo pritjen" : "Cancel waiting"}</button>
            : <button type="button" className="driver-history-link" disabled={busy || snapshot.inUse} onClick={() => void mutate("POST")}>{sq ? "Lidhu me pikën" : "Connect to charger"}</button>}
        </div>}
      </section>}
    {session?.stoppedAt && canConnect && !blocker && !snapshot.inUse && <div className="charge-connect-actions">
      <button type="button" className="driver-history-link" disabled={busy} onClick={() => void mutate("POST")}>{sq ? "Përgatit karikim të ri" : "Prepare a new charge"} ↗</button>
    </div>}
    {canConnect && !blocker && !snapshot.inUse && !active && <p className="charge-test-instruction">{sq
      ? `Për testin aktual, QR anashkalohet. Lidh simulatorin ${station.code} dhe shtyp Start charging me TAG-001, lidhësi 1.`
      : `QR is bypassed for this test. Connect simulator ${station.code}, then press Start charging with TAG-001, connector 1.`}</p>}
    {canConnect && station.isDemo && <p><a href={`/driver/simulator?station=${encodeURIComponent(station.code)}`} target="_blank" rel="noopener noreferrer">{sq ? "Hap simulatorin automatik ↗" : "Open automatic simulator ↗"}</a></p>}
    {testMode && !snapshot.inUse && snapshot.diagnostics && <SimulatorDiagnostics key={session?.id ?? stationId} initial={snapshot.diagnostics} session={session} language={language} physical={!station.isDemo}>
      <button type="button" className="button-outline" onClick={() => {
        try { downloadChargingTestReport(createChargingTestReport({ source: "charger-view", station, view: snapshot, stream: { connected, unavailable } }), station.code); }
        catch { setError(sq ? "Shkarkimi i diagnostikimit dështoi. Provo përsëri." : "Diagnostic download failed. Please retry."); }
      }}>{sq ? "Shkarko diagnostikimin" : "Download diagnostics"}</button>
    </SimulatorDiagnostics>}
  </div>;
}
